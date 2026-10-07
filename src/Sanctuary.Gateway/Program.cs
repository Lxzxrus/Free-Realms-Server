using System;
using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NLog.Extensions.Logging;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.Extensions;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Trading;
using Sanctuary.Gateway;
using Sanctuary.Scripting;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = new HostBuilder();

builder.ConfigureHostConfiguration((configurationBuilder) =>
{
    configurationBuilder.AddEnvironmentVariables("DOTNET_");
});

builder.ConfigureAppConfiguration((hostBuilderContext, configurationBuilder) =>
{
    if (hostBuilderContext.HostingEnvironment.IsDevelopment())
        configurationBuilder.AddUserSecrets<Program>();
    else
        configurationBuilder.AddJsonFile("database.json", optional: true);

    configurationBuilder.AddJsonFile("gateway.json", optional: false, reloadOnChange: true);
    configurationBuilder.AddJsonFile("gateway.local.json", optional: true, reloadOnChange: true);

    configurationBuilder.AddEnvironmentVariables();
});

builder.ConfigureServices((hostBuilderContext, serviceCollection) =>
{
    // Options
    serviceCollection.AddOptions<DatabaseOptions>()
        .BindConfiguration(DatabaseOptions.Section)
        .ValidateOnStart();

    serviceCollection.AddOptions<GatewayServerOptions>()
        .BindConfiguration(ServerOptions.Section)
        .ValidateOnStart();

    // Database
    serviceCollection.AddDatabase(hostBuilderContext.Configuration);

    // Server Options
    var serverOptions = hostBuilderContext.Configuration.GetSection(ServerOptions.Section).Get<GatewayServerOptions>();

    ArgumentNullException.ThrowIfNull(serverOptions);

    // Limits for the player-facing socket (the "Udp" section; defaults are for a public server)
    var playerUdpOptions = hostBuilderContext.Configuration.GetSection(PlayerUdpOptions.Section).Get<PlayerUdpOptions>() ?? new PlayerUdpOptions();

    // The UDP library drops a connection whose packets throw, so an exception that still reaches the host is a bug
    // in the main loop itself: stop, so a supervisor (systemd) restarts the server, rather than run on without a loop.
    serviceCollection.Configure<HostOptions>(options => options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

    // LoginGateway UDP Client
    serviceCollection.AddSingleton(serviceProvider =>
    {
        var udpParams = new UdpParams(ManagerRole.ExternalClient)
        {
            KeepAliveDelay = 10000,
            ProtocolName = "LoginGateway",
            // this side opens the connection, so it has nothing to tell a stranger, and its port faces the internet
            ReplyUnreachableConnection = false
        };

        return ActivatorUtilities.CreateInstance<LoginClient>(serviceProvider, udpParams);
    });

    // Gateway UDP Server
    serviceCollection.AddSingleton(serviceProvider =>
    {
        var udpParams = new UdpParams
        {
            CrcBytes = 2,
            NoDataTimeout = 30000,
            MaxConnections = 2000,
            KeepAliveDelay = 29000,
            Port = serverOptions.Port,
            ProtocolName = "CGAPI_527"
        };

        playerUdpOptions.ApplyTo(udpParams);

        if (serverOptions.UseCompression)
        {
            udpParams.EncryptMethod[0] = EncryptMethod.UserSupplied;
            udpParams.UserSuppliedEncryptExpansionBytes = 1;
        }

        return ActivatorUtilities.CreateInstance<GatewayServer>(serviceProvider, udpParams);
    });

    serviceCollection.AddHostedService<GatewayService>();

    // Managers
    serviceCollection.AddSingleton<IZoneManager, ZoneManager>();
    serviceCollection.AddSingleton<IResourceManager, ResourceManager>();
    serviceCollection.AddSingleton<IScriptManager, ScriptManager>();
    serviceCollection.AddSingleton<IInteractionManager, InteractionManager>();
    serviceCollection.AddSingleton<IChatCommandManager, ChatCommandManager>();
    serviceCollection.AddSingleton<IQuestManager, QuestManager>();
    serviceCollection.AddSingleton<IRewardManager, RewardManager>();
    serviceCollection.AddSingleton<IHouseManager, HouseManager>();
    serviceCollection.AddSingleton<TradeOptions>();
    serviceCollection.AddSingleton<ITradeCommitter, TradeCommitter>();
    serviceCollection.AddSingleton<ITradeManager, TradeManager>();
});

builder.ConfigureLogging((hostBuilderContext, loggingBuilder) =>
{
    loggingBuilder.ClearProviders();

#if DEBUG
    loggingBuilder.SetMinimumLevel(LogLevel.Debug);
#endif

    var nlogConfigFile = hostBuilderContext.HostingEnvironment.IsDevelopment()
        ? "NLog.Development.config"
        : "NLog.config";

    loggingBuilder.AddNLog(nlogConfigFile);
});

var host = builder.Build();

#if DEBUG
const bool isDebugBuild = true;
#else
const bool isDebugBuild = false;
#endif

var configuration = host.Services.GetRequiredService<IConfiguration>();
var options = host.Services.GetRequiredService<IOptions<GatewayServerOptions>>().Value;

if (!StartupChecks.Passes(host.Services.GetRequiredService<ILogger<Program>>(), isDebugBuild,
    StartupChecks.CheckBuild(isDebugBuild, configuration),
    StartupChecks.CheckLoginGatewayChallenge(options.LoginGatewayChallenge)))
{
    return 1;
}

await host.RunAsync();

return 0;
