using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Jobs;

public sealed class JobManager : IJobManager
{
    private readonly IResourceManager _resourceManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly IZoneManager _zoneManager;
    private readonly IQuestManager _questManager;
    private readonly ILogger<JobManager> _logger;

    public JobManager(IResourceManager resourceManager, IDbContextFactory<DatabaseContext> dbContextFactory,
        IZoneManager zoneManager, IQuestManager questManager, ILogger<JobManager> logger)
    {
        _resourceManager = resourceManager;
        _dbContextFactory = dbContextFactory;
        _zoneManager = zoneManager;
        _questManager = questManager;
        _logger = logger;
    }

    public JobGrantResult Grant(Player player, int profileId)
    {
        if (player.Profiles.Any(profile => profile.Id == profileId))
            return JobGrantResult.AlreadyHad;

        DbProfile dbProfile;

        using (var dbContext = _dbContextFactory.CreateDbContext())
        {
            var result = JobGrant.TrySave(dbContext, _resourceManager, _logger, player.CharacterId, profileId, out dbProfile);

            if (result != JobGrantResult.Granted)
                return result;
        }

        // The kit's new items go in the bags before the job that wears them.
        foreach (var dbItem in dbProfile.Items)
        {
            if (_resourceManager.ClientItemDefinitions.TryGetValue(dbItem.Definition, out var itemDefinition)
                && player.Items.All(item => item.Id != dbItem.Id))
            {
                RewardHelper.SendItem(player, dbItem, itemDefinition);
            }
        }

        var profileData = _resourceManager.Profiles[profileId];

        ProfileHelper.AddClientProfile(player, ProfileHelper.CreateClientProfile(profileData, dbProfile, _resourceManager));

        // There is no "add job" packet in the client's opcode table. The job switch carries the whole job, the way
        // accepting a job's first quest switched you into it in the live game; the jobs window should pick it up from
        // there (to be checked in game).
        ProfileHelper.Activate(player, profileId, _zoneManager);

        // A trainer with nothing left to teach this character loses its marker.
        foreach (var trainer in JobTrainers.All.Where(trainer => trainer.ProfileId == profileId))
        {
            foreach (var npc in player.VisibleNpcs.Values.Where(npc => npc.DefinitionId == trainer.NpcId))
                _questManager.RefreshQuestNotification(player, npc.Guid);
        }

        _logger.LogInformation("Character {characterId} got job {profileId}.", player.CharacterId, profileId);

        return JobGrantResult.Granted;
    }

    public JobGrantResult GrantOffline(ulong characterId, int profileId)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return JobGrant.TrySave(dbContext, _resourceManager, _logger, characterId, profileId, out _);
    }

    public List<NpcInteractionOption> GetTrainerOptions(Player player, Npc npc)
    {
        var options = new List<NpcInteractionOption>();

        foreach (var profileId in JobTrainers.JobsTaughtBy(npc.DefinitionId))
        {
            if (player.Profiles.Any(profile => profile.Id == profileId))
                continue;

            if (!_resourceManager.Profiles.TryGetValue(profileId, out var profileData))
                continue;

            options.Add(new NpcInteractionOption
            {
                IconId = ContextIcons.QuestOffer,
                ButtonTextId = profileData.NameId,
                TooltipId = profileData.DescriptionId,
                Invoke = interactingPlayer => Grant(interactingPlayer, profileId)
            });
        }

        return options;
    }
}
