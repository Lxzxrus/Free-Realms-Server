using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Jobs;

/// <summary>A job trainer: an npc spawned by <c>FabledRealms.lua</c> who gives one job.</summary>
/// <param name="NpcId">The spawn's <c>Npcs.json</c> id. Upstream OSFR's quests name NPCs by it (and its PR #164
/// derives NPC GUIDs from it), so trainers are matched on <c>Npc.DefinitionId</c>, never on a GUID.</param>
/// <param name="ProfileId">The job, from <c>Profiles.json</c>.</param>
/// <param name="Name">The npc's name in <c>Npcs.json</c>.</param>
public readonly record struct JobTrainer(int NpcId, int ProfileId, string Name);

/// <summary>
/// Who teaches which job. In the live game "most jobs unlock by simply talking to a basic trainer for that
/// profession"; the trainers and where they stood are in <c>docs/research/jobs.md</c>. Each name below matches the
/// wiki's trainer exactly, but none has been checked in game: the spawn could be the same character standing
/// somewhere else (Therin has three spawns). Jobs whose trainer isn't in our npcs (Warrior, Postman, Card Duelist), or
/// is unclear (Ninja, Soccer Star), have none yet; <c>!job grant</c> gives them.
/// </summary>
public static class JobTrainers
{
    private const int Fisherman = 137;
    private const int Miner = 14;
    private const int Brawler = 43;
    private const int Archer = 35;
    private const int Wizard = 12;
    private const int Medic = 11;
    private const int Blacksmith = 16;
    private const int Chef = 45;
    private const int KartDriver = 48;
    private const int DemoDerbyDriver = 49;

    public static readonly IReadOnlyList<JobTrainer> All =
    [
        // Stillwater Crossing, "next to the fishing boat".
        new(2968, Fisherman, "Reed Stillwater"),

        // Singing Crystal Mines, Snowhill: "east side of the first cavern". Which of the spawns stands there is a
        // guess, so each of them teaches it. Upstream OSFR dropped spawn 1202, a second Therin standing exactly where
        // 1201 stands (merged 2026-10-28).
        new(1201, Miner, "Therin"),
        new(1697, Miner, "Therin"),
        new(3602, Miner, "Therin"),

        // Highroad Junction and Stillwater Crossing. Allakhazam names Merigold for Harold's quests; Fandom names Harold.
        new(2910, Brawler, "Harold"),
        new(2943, Brawler, "Caitlyn Gravefog"),

        new(3467, Archer, "Lugabow"),        // Greenwood Camp
        new(1327, Wizard, "Fizzlesticks"),   // Robgoblin Camp
        new(2340, Medic, "Nurse Naia"),      // Sanctuary hospital
        new(3599, Blacksmith, "Smitty"),     // Snowhill

        new(2069, Chef, "Simone"),           // Cobblestone Village
        new(2896, Chef, "Auguste Frydaze"),  // Highroad Junction
        new(2416, Chef, "Bordon Cloo"),      // Sanctuary
        new(2948, Chef, "Helena"),           // Stillwater Crossing

        // One at each speedway.
        new(2986, KartDriver, "Mustang Alli"),
        new(3039, KartDriver, "Dane Blueshift"),
        new(3010, KartDriver, "Johnny Thunder"),
        new(2684, KartDriver, "Cale Yardbird"),

        new(2988, DemoDerbyDriver, "Burt 'Wrecking' Crewe"),
        new(3043, DemoDerbyDriver, "Crash Davis"),
        new(2680, DemoDerbyDriver, "Rip Ringa"),
        new(3009, DemoDerbyDriver, "Mad Jack"),
    ];

    private static readonly ILookup<int, int> JobsByNpc = All.ToLookup(trainer => trainer.NpcId, trainer => trainer.ProfileId);

    public static bool IsTrainer(int npcId) => JobsByNpc.Contains(npcId);

    /// <summary>The jobs the npc gives; empty for anyone who isn't a trainer.</summary>
    public static IEnumerable<int> JobsTaughtBy(int npcId) => JobsByNpc[npcId];

    /// <summary>
    /// The marker over a trainer: the quests' "available" one (image 2), so new players know to click them. Ariel asked
    /// for it in the playtest of 2026-10-24.
    /// </summary>
    public const int NotificationImageId = 2;

    /// <summary>
    /// Jobs with nothing to do on Evergrove yet: no combat, no racing. Their trainers still teach them when clicked, but
    /// show no marker, so the "!" never leads a new player to a job with nothing behind it. Take a job out of here when
    /// its game arrives.
    /// </summary>
    public static readonly IReadOnlySet<int> JobsWithNothingToDoYet =
        new HashSet<int> { Brawler, Archer, Wizard, Medic, KartDriver, DemoDerbyDriver };

    /// <summary>
    /// <see cref="NotificationImageId"/> while the trainer still has a job to teach this character, else 0. A job the
    /// server doesn't know is never offered, and one in <see cref="JobsWithNothingToDoYet"/> isn't advertised, so
    /// neither counts.
    /// </summary>
    public static int NotificationImageFor(int npcId, Func<int, bool> hasJob, Func<int, bool> jobExists) =>
        JobsTaughtBy(npcId).Any(profileId => !JobsWithNothingToDoYet.Contains(profileId) && jobExists(profileId) && !hasJob(profileId))
            ? NotificationImageId
            : 0;
}
