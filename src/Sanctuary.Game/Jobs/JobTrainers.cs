using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Jobs;

/// <summary>A job trainer: an npc spawned by <c>FabledRealms.lua</c> who gives one job.</summary>
/// <param name="NpcGuid">The spawn's guid (100000000000 plus the <c>Npcs.json</c> id).</param>
/// <param name="ProfileId">The job, from <c>Profiles.json</c>.</param>
/// <param name="Name">The npc's name in <c>Npcs.json</c>.</param>
public readonly record struct JobTrainer(ulong NpcGuid, int ProfileId, string Name);

/// <summary>
/// Who teaches which job. In the live game "most jobs unlock by simply talking to a basic trainer for that
/// profession"; the trainers and where they stood are in <c>docs/research/jobs.md</c>. Each name below matches the
/// wiki's trainer exactly, but none has been checked in game: the spawn could be the same character standing
/// somewhere else (Therin has four spawns). Jobs whose trainer isn't in our npcs (Warrior, Postman, Card Duelist), or
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
        new(100000002968, Fisherman, "Reed Stillwater"),

        // Singing Crystal Mines, Snowhill: "east side of the first cavern". Which of the four spawns stands there is a
        // guess, so each of them teaches it.
        new(100000001201, Miner, "Therin"),
        new(100000001202, Miner, "Therin"),
        new(100000001697, Miner, "Therin"),
        new(100000003602, Miner, "Therin"),

        // Highroad Junction and Stillwater Crossing. Allakhazam names Merigold for Harold's quests; Fandom names Harold.
        new(100000002910, Brawler, "Harold"),
        new(100000002943, Brawler, "Caitlyn Gravefog"),

        new(100000003467, Archer, "Lugabow"),        // Greenwood Camp
        new(100000001327, Wizard, "Fizzlesticks"),   // Robgoblin Camp
        new(100000002340, Medic, "Nurse Naia"),      // Sanctuary hospital
        new(100000003599, Blacksmith, "Smitty"),     // Snowhill

        new(100000002069, Chef, "Simone"),           // Cobblestone Village
        new(100000002896, Chef, "Auguste Frydaze"),  // Highroad Junction
        new(100000002416, Chef, "Bordon Cloo"),      // Sanctuary
        new(100000002948, Chef, "Helena"),           // Stillwater Crossing

        // One at each speedway.
        new(100000002986, KartDriver, "Mustang Alli"),
        new(100000003039, KartDriver, "Dane Blueshift"),
        new(100000003010, KartDriver, "Johnny Thunder"),
        new(100000002684, KartDriver, "Cale Yardbird"),

        new(100000002988, DemoDerbyDriver, "Burt 'Wrecking' Crewe"),
        new(100000003043, DemoDerbyDriver, "Crash Davis"),
        new(100000002680, DemoDerbyDriver, "Rip Ringa"),
        new(100000003009, DemoDerbyDriver, "Mad Jack"),
    ];

    private static readonly ILookup<ulong, int> JobsByNpc = All.ToLookup(trainer => trainer.NpcGuid, trainer => trainer.ProfileId);

    public static bool IsTrainer(ulong npcGuid) => JobsByNpc.Contains(npcGuid);

    /// <summary>The jobs the npc gives; empty for anyone who isn't a trainer.</summary>
    public static IEnumerable<int> JobsTaughtBy(ulong npcGuid) => JobsByNpc[npcGuid];

    /// <summary>
    /// The marker over a trainer: the quests' "available" one (image 2), so new players know to click them. Ariel asked
    /// for it in the playtest of 2026-10-24.
    /// </summary>
    public const int NotificationImageId = 2;

    /// <summary>
    /// <see cref="NotificationImageId"/> while the trainer still has a job to teach this character, else 0. A job the
    /// server doesn't know is never offered, so it doesn't count.
    /// </summary>
    public static int NotificationImageFor(ulong npcGuid, Func<int, bool> hasJob, Func<int, bool> jobExists) =>
        JobsTaughtBy(npcGuid).Any(profileId => jobExists(profileId) && !hasJob(profileId)) ? NotificationImageId : 0;
}
