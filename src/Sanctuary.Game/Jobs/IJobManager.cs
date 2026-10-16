using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Interactions;

namespace Sanctuary.Game.Jobs;

public interface IJobManager
{
    /// <summary>
    /// Gives the player a job while they play: saved first, then the job and its kit sent, and the player switched
    /// into it.
    /// </summary>
    JobGrantResult Grant(Player player, int profileId);

    /// <summary>Saves a job on a character who isn't online; they get it on their next login.</summary>
    JobGrantResult GrantOffline(ulong characterId, int profileId);

    /// <summary>A trainer's "become one" options, one per job they teach that the player doesn't have.</summary>
    List<NpcInteractionOption> GetTrainerOptions(Player player, Npc npc);
}
