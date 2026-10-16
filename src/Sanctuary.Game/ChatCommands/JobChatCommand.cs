using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Jobs;

namespace Sanctuary.Game.ChatCommands;

/// <summary>
/// Gives a job to yourself or another player, for testing and support: what a trainer gives, without the walk. Staff
/// only, like <c>!mod</c>.
/// </summary>
public class JobChatCommand : IChatCommand
{
    private readonly IChatCommandManager _chatCommandManager;
    private readonly IJobManager _jobManager;
    private readonly IZoneManager _zoneManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;

    public string KeyWord => "job";
    public string Usage => "grant <profileId> [player]";
    public string Description => "Gives a job (profile) to you or the named player, as its trainer would.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Mod;

    public JobChatCommand(IChatCommandManager chatCommandManager, IJobManager jobManager, IZoneManager zoneManager,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _chatCommandManager = chatCommandManager;
        _jobManager = jobManager;
        _zoneManager = zoneManager;
        _dbContextFactory = dbContextFactory;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length < 2 || !args[0].Equals("grant", System.StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(args[1], out var profileId))
            return false;

        var targetName = args.Length > 2 ? string.Join(' ', args[2..]) : invoker.Name.FullName;

        JobGrantResult result;

        var online = _zoneManager.TryGetPlayer(targetName, out var target);

        if (online)
            result = _jobManager.Grant(target!, profileId);
        else
        {
            using var dbContext = _dbContextFactory.CreateDbContext();

            var characterId = dbContext.Characters
                .Where(character => character.FullName == targetName)
                .Select(character => (ulong?)character.Id)
                .SingleOrDefault();

            if (characterId is null)
            {
                ChatHelper.SendSystemMessage(invoker, $"No player named \"{targetName}\" was found.");
                return true;
            }

            result = _jobManager.GrantOffline(characterId.Value, profileId);
        }

        if (result == JobGrantResult.Granted)
            _chatCommandManager.LogAction(this, invoker, "Job grant", targetName, $"profileId={profileId}");

        ChatHelper.SendSystemMessage(invoker, result switch
        {
            JobGrantResult.Granted => online
                ? $"{targetName} now has job {profileId}."
                : $"{targetName} will have job {profileId} on their next login.",
            JobGrantResult.AlreadyHad => $"{targetName} already has job {profileId}.",
            JobGrantResult.UnknownJob => $"There is no job {profileId}.",
            JobGrantResult.StaffJob => $"Job {profileId} comes with the staff flags, at login.",
            JobGrantResult.CharacterNotFound => $"No player named \"{targetName}\" was found.",
            _ => $"Failed to give {targetName} job {profileId}."
        });

        return true;
    }
}
