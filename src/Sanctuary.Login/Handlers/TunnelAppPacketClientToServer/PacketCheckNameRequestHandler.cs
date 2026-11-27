using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Login.Handlers;

[PacketHandler]
public static class PacketCheckNameRequestHandler
{
    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketCheckNameRequestHandler));

        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(LoginConnection connection, Span<byte> data)
    {
        if (!PacketCheckNameRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketCheckNameRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketCheckNameRequest), packet);

        // The creation screen checks names after logging in. Before that a check is someone else's: each one is a
        // database query, and the replies would tell which names exist.
        if (connection.UserId == 0)
        {
            connection.ReportFault();
            return true;
        }

        if (string.IsNullOrWhiteSpace(packet.FirstName))
        {
            _logger.LogError("Invalid name received.");
            return true;
        }

        var packetCheckNameReply = new PacketCheckNameReply
        {
            FirstName = packet.FirstName,
            LastName = packet.LastName
        };

        // Results
        // -1	Char_Name_Invalid_PS3
        //  4	Char_Name_Invalid_PS3
        //  2	Char_Name_Taken_PS3
        //  3	Char_Name_Profane_PS3
        //  5	Char_Name_Incorrect_Length_PS3
        //  6	Char_Server_Failure_PS3
        //  7	First_Name_Short_PS3
        //  8	Last_Name_Short_PS3
        //  9	First_Name_Long_PS3
        // 10	Last_Name_Long_PS3
        // def	Char_Unable_Log_Server_PS3

        // Evergrove: the name rules (CharacterNameRules), which creation applies too. Only "is it taken" was checked.
        var problem = CharacterNameRules.Check(packet.FirstName, packet.LastName, _resourceManager.NameFilter);

        if (problem != CharacterNameProblem.None)
        {
            packetCheckNameReply.Result = problem switch
            {
                CharacterNameProblem.Profane => 3,
                CharacterNameProblem.Reserved => 2, // a staff or server word: "taken" is the client's message that fits
                CharacterNameProblem.FirstNameTooShort => 7,
                CharacterNameProblem.LastNameTooShort => 8,
                CharacterNameProblem.FirstNameTooLong => 9,
                CharacterNameProblem.LastNameTooLong => 10,
                _ => 4
            };

            connection.SendTunneled(packetCheckNameReply);

            return true;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var nameExists = dbContext.Characters.Any(x => x.FirstName == packet.FirstName && x.LastName == packet.LastName);

        packetCheckNameReply.Result = nameExists ? 2 : 1;

        connection.SendTunneled(packetCheckNameReply);

        return true;
    }
}