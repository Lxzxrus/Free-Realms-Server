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
using Sanctuary.Login;
using Sanctuary.Packet.Common.Extensions;
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

    configurationBuilder.AddJsonFile("login.json", optional: false, reloadOnChange: true);
    configurationBuilder.AddJsonFile("login.local.json", optional: true, reloadOnChange: true);

    configurationBuilder.AddEnvironmentVariables();
});

builder.ConfigureServices((hostBuilderContext, serviceCollection) =>
{
    // Options
    serviceCollection.AddOptions<DatabaseOptions>()
        .BindConfiguration(DatabaseOptions.Section)
        .ValidateOnStart();

    serviceCollection.AddOptions<LoginServerOptions>()
        .BindConfiguration(ServerOptions.Section)
        .ValidateOnStart();

    // Database
    serviceCollection.AddDatabase(hostBuilderContext.Configuration);

    // Server Options
    var serverOptions = hostBuilderContext.Configuration.GetSection(ServerOptions.Section).Get<LoginServerOptions>();

    ArgumentNullException.ThrowIfNull(serverOptions);

    // Limits for the player-facing socket (the "Udp" section; defaults are for a public server)
    var playerUdpOptions = hostBuilderContext.Configuration.GetSection(PlayerUdpOptions.Section).Get<PlayerUdpOptions>() ?? new PlayerUdpOptions();

    // The UDP library drops a connection whose packets throw, so an exception that still reaches the host is a bug
    // in the main loop itself: stop, so a supervisor (systemd) restarts the server, rather than run on without a loop.
    serviceCollection.Configure<HostOptions>(options => options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

    // LoginGateway UDP Server
    serviceCollection.AddSingleton(serviceProvider =>
    {
        var udpParams = new UdpParams(ManagerRole.ExternalServer)
        {
#if DEBUG
            NoDataTimeout = 0,
#endif
            KeepAliveDelay = 10000,
            ProtocolName = "LoginGateway",
            Port = serverOptions.LoginGatewayPort,
            BindIpAddress = serverOptions.LoginGatewayBindAddress,
        };

        return ActivatorUtilities.CreateInstance<GatewayServer>(serviceProvider, udpParams);
    });

    // Login UDP Server
    serviceCollection.AddSingleton(serviceProvider =>
    {
        var udpParams = new UdpParams
        {
            CrcBytes = 2,
            MaxConnections = 2000,
            KeepAliveDelay = 29000,
            Port = serverOptions.Port,
            ProtocolName = "LoginUdp_6"
        };

        playerUdpOptions.ApplyTo(udpParams);

        if (serverOptions.UseCompression)
        {
            udpParams.EncryptMethod[0] = EncryptMethod.UserSupplied;
            udpParams.UserSuppliedEncryptExpansionBytes = 1;
        }

        return ActivatorUtilities.CreateInstance<LoginServer>(serviceProvider, udpParams);
    });

    serviceCollection.AddHostedService<LoginService>();

    // Managers
    serviceCollection.AddSingleton<IResourceManager, ResourceManager>();
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
var options = host.Services.GetRequiredService<IOptions<LoginServerOptions>>().Value;

if (!StartupChecks.Passes(host.Services.GetRequiredService<ILogger<Program>>(), isDebugBuild,
    StartupChecks.CheckBuild(isDebugBuild, configuration),
    StartupChecks.CheckLoginGatewayChallenge(options.LoginGatewayChallenge),
    StartupChecks.CheckLoginGatewayBindAddress(options.LoginGatewayBindAddress)))
{
    return 1;
}

// Packet Handlers
host.Services.ConfigurePacketHandlers();

await host.RunAsync();

return 0;
