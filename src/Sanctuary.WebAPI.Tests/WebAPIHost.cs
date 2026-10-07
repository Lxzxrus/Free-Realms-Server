using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Tests;

/// <summary>
/// WebAPI on the ASP.NET test host, with its own Sqlite database, portrait folder and clock.
/// Requests carry their client address in <see cref="ClientAddressHeader"/>, as Kestrel would see it.
/// </summary>
internal sealed class WebAPIHost : WebApplicationFactory<Program>
{
    public const string ClientAddressHeader = "X-Test-Client-Address";
    public const string DefaultAddress = "198.51.100.1";
    public const string Password = "correct-horse";

    private readonly Dictionary<string, string?> _settings;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sanctuary-webapi-tests", Guid.NewGuid().ToString("N"));

    public string ImagesDirectory => Path.Combine(Root, "Images");

    public TestTimeProvider Time { get; } = new();

    public WebAPIHost(Dictionary<string, string?>? settings = null)
    {
        Directory.CreateDirectory(Root);

        // Limits high enough that tests about something else never hit them.
        _settings = new Dictionary<string, string?>
        {
            // Tests run Debug builds, which refuse to start without this. The test host has no network listener.
            ["AllowDebugBuild"] = "true",
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={Path.Combine(Root, "test.db")}",
            ["WebAPI:LaunchArguments"] = "AssetDelivery:IndirectServerAddress=http://assets.example",
            ["WebAPI:PortraitUploadUrl"] = "https://play.example/image",
            ["WebAPI:ImagesDirectory"] = ImagesDirectory,
            ["WebAPI:RateLimits:LoginPerMinute"] = "1000",
            ["WebAPI:RateLimits:RegisterPerHour"] = "1000",
            ["WebAPI:RateLimits:ImagePerMinute"] = "1000",
        };

        foreach (var (key, value) in settings ?? [])
            _settings[key] = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>();
        });
    }

    public async Task<HostSeed> StartAsync()
    {
        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();

        await dbContext.Database.MigrateAsync();

        // Low bcrypt cost keeps the tests quick; WebAPI verifies any cost.
        var alice = new DbUser { Username = "alice", Password = BC.HashPassword(Password, BC.GenerateSalt(4)), MaxCharacters = 4 };
        var bob = new DbUser { Username = "bob", Password = BC.HashPassword(Password, BC.GenerateSalt(4)), MaxCharacters = 4 };
        var banned = new DbUser { Username = "banned", Password = BC.HashPassword(Password, BC.GenerateSalt(4)), MaxCharacters = 4, LockedUntil = DateTimeOffset.UtcNow.AddYears(10) };

        dbContext.Users.AddRange(alice, bob, banned);

        var aliceCharacter = NewCharacter(alice, "Alice");
        var bobCharacter = NewCharacter(bob, "Bob");
        var bannedCharacter = NewCharacter(banned, "Banned");

        dbContext.Characters.AddRange(aliceCharacter, bobCharacter, bannedCharacter);

        await dbContext.SaveChangesAsync();

        return new HostSeed(
            GuidHelper.GetPlayerGuid(aliceCharacter.Id),
            GuidHelper.GetPlayerGuid(bobCharacter.Id),
            GuidHelper.GetPlayerGuid(bannedCharacter.Id));
    }

    public HttpClient CreateClient(string address, string? forwardedFor = null)
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Add(ClientAddressHeader, address);

        if (forwardedFor is not null)
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        return client;
    }

    public Task<HttpResponseMessage> LoginAsync(string username, string password = Password, string address = DefaultAddress)
    {
        return CreateClient(address).PostAsJsonAsync("/login", new { username, password });
    }

    /// <summary>Logs in and returns the portrait upload path (<c>/image/{token}</c>) from the launch arguments.</summary>
    public async Task<string> GetPortraitUploadPathAsync(string username)
    {
        using var response = await LoginAsync(username);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginBody>();

        const string prefix = "Portrait:UploadUrl=https://play.example";

        var argument = Array.Find(body!.LaunchArguments!.Split(' '), x => x.StartsWith(prefix, StringComparison.Ordinal));

        Assert.IsNotNull(argument);

        return argument[prefix.Length..];
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static DbCharacter NewCharacter(DbUser user, string firstName)
    {
        return new DbCharacter
        {
            User = user,
            FirstName = firstName,
            Head = "head",
            HeadId = 1,
            Hair = "hair",
            HairId = 1,
            SkinTone = "skin",
            SkinToneId = 1
        };
    }

    private sealed class ClientAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    var address = context.Request.Headers.TryGetValue(ClientAddressHeader, out var value)
                        ? value.ToString()
                        : DefaultAddress;

                    context.Connection.RemoteIpAddress = IPAddress.Parse(address);

                    return nextMiddleware(context);
                });

                next(app);
            };
        }
    }
}

internal sealed record HostSeed(ulong AliceCharacterGuid, ulong BobCharacterGuid, ulong BannedCharacterGuid);

internal sealed record LoginBody(string SessionId, string? LaunchArguments);

internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
