using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Login.Handlers;

/// <summary>
/// A player's first packet: the session the Web API gave the launcher. Anyone can send one before signing in, as fast
/// as they like, so a refused one costs the server as little as possible: a session that isn't one the Web API makes
/// never reaches the database, no session is ever written to a log (it is a credential), and the connection is closed
/// once the refusal has been delivered, so the next try needs a new connection (and the connect limits apply).
/// </summary>
[PacketHandler]
public static class LoginRequestHandler
{
    /// <summary>How long a refused connection has to deliver the refusal before it closes, in milliseconds.</summary>
    public const int RefusalFlushTimeout = 1000;

    private static ILogger _logger = null!;
    private static LoginServerOptions _options = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(LoginRequestHandler));

        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        var options = serviceProvider.GetRequiredService<IOptionsMonitor<LoginServerOptions>>();
        _options = options.CurrentValue;
        options.OnChange(o => _options = o);
    }

    public static bool HandlePacket(LoginConnection connection, Span<byte> data)
    {
        if (!LoginRequest.TryDeserialize(data, out var packet))
        {
            // A real client's LoginRequest always parses, so this is someone else's packet: close the connection
            // rather than log one line for each of a flood of them.
            _logger.LogWarning("{connection} sent a {packet} that doesn't parse ( {length} bytes ). Disconnecting it.", connection, nameof(LoginRequest), data.Length);

            connection.Disconnect();

            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(LoginRequest), packet);

        var loginReply = new LoginReply();

        // Have we already logged-in?
        if (connection.UserId > 0)
        {
            connection.Send(loginReply);

            // once per connection: a logged-in player could send these all day
            if (!connection.LoggedTwiceLogin)
            {
                connection.LoggedTwiceLogin = true;

                _logger.LogWarning("{connection} tried to log in twice. ( UserId: {UserId} )", connection, connection.UserId);
            }

            return true;
        }

        if (!LoginSession.IsWellFormed(packet.Session))
        {
            Refuse(connection, loginReply);

            _logger.LogWarning("{connection} tried to log in with a session the Web API doesn't make ( {length} chars ).", connection, packet.Session?.Length ?? 0);

            return true;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var user = dbContext.Users.SingleOrDefault(x => x.Session == packet.Session);

        if (user is null
#if !DEBUG
            || !user.SessionCreated.HasValue
#endif
            )
        {
            Refuse(connection, loginReply);

            _logger.LogWarning("{connection} tried to log in with an invalid session.", connection);

            return true;
        }

#if !DEBUG
        if ((DateTimeOffset.UtcNow - user.SessionCreated.Value).TotalMinutes > 5)
        {
            Refuse(connection, loginReply);

            _logger.LogWarning("{connection} tried to log in with an expired session. ( UserId: {UserId}, SessionCreated: {sessionCreated} )", connection, user.Id, user.SessionCreated);

            return true;
        }

        user.Session = null;
        user.SessionCreated = null;
#endif

        user.LastLogin = DateTimeOffset.UtcNow;

        if (dbContext.SaveChanges() <= 0)
        {
            connection.Send(loginReply);

            return true;
        }

        if (_options.IsLocked && !user.IsAdmin)
        {
            loginReply.Status = 2;

            connection.Send(loginReply);

            return true;
        }

        connection.UserId = user.Id;

        loginReply.LoggedIn = true;
        loginReply.Status = 1;
        // Everyone is a member (Membership).
        loginReply.IsMember = true;

        var accountInfo = new AccountInfo
        {
            IsMember = true,
            MaxCharacters = user.MaxCharacters,
            IsAdminAccount = user.IsAdmin
        };

        loginReply.Payload = accountInfo.Serialize();

        connection.Send(loginReply);

        return true;
    }

    /// <summary>
    /// Sends the refusal and closes the connection once it has been delivered (or after
    /// <see cref="RefusalFlushTimeout"/>). Packets that arrive meanwhile are dropped (LoginConnection.OnRoutePacket).
    /// </summary>
    private static void Refuse(LoginConnection connection, LoginReply loginReply)
    {
        connection.Send(loginReply);

        connection.Disconnect(RefusalFlushTimeout);
    }
}
