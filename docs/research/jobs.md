# How a player gets a new job

Research for task 34 (`job-trainers`), 2026-10-16. Sources are the Free Realms Fandom wiki (F, read through its
MediaWiki API, `https://freerealms.fandom.com/api.php?action=parse&page=<Page>&prop=wikitext&format=json`, because
the site refuses plain fetches), the Allakhazam wikibase (A, `https://everquest.allakhazam.com/wiki/...`), the live
opcode table in the archived OpenSourceFreeRealms repository, and our own data. Nothing here has been checked in the
client yet: the client strings and the decompiled UI Lua need Nate's local client (see the last section).

## How the live game did it

- **Talk to a trainer.** "To gain access to a profession, you must first unlock the job. Most jobs unlock by simply
  talking to a basic trainer for that profession" (F: Jobs). In practice the trainer offered a short first quest,
  usually "*Job*: ..." or "Becoming a ...", and accepting it switched you into the job: Allakhazam notes that
  accepting a Brawler quest "automatically changes you to the Brawler job".
- **Objects point you to the trainer.** Clicking a job's minigame object or node without the job "will activate a
  quest… direct[ing] you to the nearest trainer" (F: Jobs). Clicking a postbox offered "Postman: It's all in the
  Training"; clicking an ore node or forge offered "Become a Miner".
- **No job item, no purchase, no tryout quest.** None of the sources has a job you could buy or an item that unlocked
  one. The free-trial limit was a level cap: every job was free to level 4 or 5 depending on the page, then needed
  membership (A: FR_Job_(FR_Help); A: Category:Fisherman_(FR_Job)). Evergrove has no paid membership, so the cap
  doesn't apply here, and `Profiles.json`'s `Trial` flag isn't used.
- **The "job shard" in Restless Rumors is a Power Shard**, not an unlock: an equippable stat item for one job
  ("Brawler's Power Shard of Vitality I", "Increases health by 76 in combat"; A: FR_Quest:Brawler:_Restless_Rumors,
  A: FR_Item:Brawler's_Power_Shard_of_Vitality_I). The job switch in that quest comes from accepting it.
- **Starter items.** No trainer is recorded handing out a tool. The first quests rewarded clothes (Miner boots and
  gloves, Brawler pants and boots). Rods came from the Coin Shop (A: FR_Item:Simple_Bamboo_Fishing_Rod), and the
  Pointy Pickaxe from a TCG loot card (F: Pointy_Pickaxe).
- **Lures were Coin Shop items**, "under the supplies item category" (F: Fisherman). Skipper Seeker and Treasure
  Magnet are "only available in the Coin Shop" (A: FR_Item:Skipper_Seeker, A: FR_Item:Treasure_Magnet). The Coin Shop
  took coins; it isn't the Station Cash Marketplace (F: Coin_Shop). The job merchants added in 2013 had no Fisherman
  merchant (F: Merchants).

## The protocol

The archived live opcode table (`ofrserver/Server/LibSOE/Core/PacketCodes.cs` in
`Open-Source-Free-Realms/OpenSourceFreeRealms`) has no packet that adds a job:

| Packet | Op | What it tells us |
|---|---|---|
| `ClientUpdatePacketDeleteProfile` | 38 / 20 | Removing a job has its own packet; adding one doesn't |
| `ClientUpdatePacketActivateProfile` | 38 / 21 | The job switch, carrying the whole job (`ClientPcProfile`). What we send |
| `CommandPacketSetProfile` | 37 / 13 | Client asks to switch job |
| `CommandPacketSetProfileByItemDefinitionId` | 37 / 36 | Client asks to switch job by an item (not handled by us; what sends it is unknown) |
| `CommandPacketActivateProfileFailed` | 37 / 26 | A refused switch |
| `JobCustomizationBasePacket` | 155 | Begin and finalize job customization; meaning unknown |
| `RewardBundleEntryType.ProfileAdd` | entry 7 | A reward bundle can carry a "new job" entry. Its byte layout after the common fields is unknown, so we don't send it |

Since accepting a trainer's quest switched you into the job, the server most likely added it with the job switch
itself, which carries the full job. That's what our grant does. If the client turns out to ignore a switch into a job
it doesn't know, the job is still saved and appears after a relog, and the `ProfileAdd` reward entry is the next thing
to try.

## The trainers

Our npcs (`Npcs.json`, all spawned by `FabledRealms.lua`) matched by exact name. **Taught** means the trainer gives
the job in this PR. Each spawn is the right character by name; whether it stands where the wiki puts it hasn't been
checked.

| Job (profile) | Trainer (wiki) | Where (wiki) | First quest (wiki) | Our npc | Taught |
|---|---|---|---|---|---|
| Fisherman (137) | Reed Stillwater, "next to the fishing boat"; the quest starts from the Sports Club message board | Stillwater Crossing | "Testing the Waters": catch a Flutterfish at Sacred Grove Shallows | 2968 | Yes |
| Miner (14) | Therin, "east side of the first cavern" | Singing Crystal Mines, Snowhill | "Miner: So, You Want To Be A Miner?" | 1201, 1202 (same spot), 1697, 3602; which one is in the mine is a guess, so all four teach it | Yes |
| Brawler (43) | Harold (Fandom) or Merigold (Allakhazam); Carlos Brazenfist; Caitlyn Gravefog | Highroad Junction; Cobblestone Village; Stillwater Crossing | "Hewey's Escape!", "Bringing Back Brazenfist", "Graveyard Grumble" | Harold 2910, Caitlyn Gravefog 2943 (Carlos isn't in our npcs; Merigold 2912 left out while the sources disagree) | Yes |
| Archer (35) | Lugabow | Greenwood Camp | "Path of the Archer" | 3467 | Yes |
| Wizard (12) | Fizzlesticks | Robgoblin Camp | "Wizard: How to be a Wizard" (from 2013 it also needed Medic 20; not applied) | 1327 | Yes |
| Medic (11) | Nurse Naia | Sanctuary hospital | "Medic: So You Want to be a Medic?" | 2340 | Yes |
| Blacksmith (16) | Smitty (Steelbloom in Sanctuary sends you) | Snowhill | "Blacksmith: Basics of Blacksmithing" | 3599 | Yes |
| Chef (45) | Simone, Auguste Frydaze, Bordon Cloo, Helena | Cobblestone Village, Highroad Junction, Sanctuary, Stillwater Crossing | "Chef: Crossroads Cafe", "Chef: Stillwater Inn" | 2069, 2896, 2416, 2948 | Yes |
| Kart Driver (48) | Mustang Alli, Dane Blueshift, Johnny Thunder, Cale Yardbird | The four speedways | "Kart Driver: Challenge Verve McNichols" | 2986, 3039, 3010, 2684 | Yes |
| Demo Derby Driver (49) | Burt 'Wrecking' Crewe, Crash Davis, Rip Ringa, Mad Jack | The four speedways | Not found | 2988, 3043, 2680, 3009 | Yes |
| Ninja (2) | Master Ty | Southern Lakeshore | "Ninja: The Ninja Path" | Our only Ty (3081) wears `human_m_ty_downtrodden`, and Kai (2022) wears `human_m_ninjatrainer`. Unclear | No |
| Warrior (32) | Drill Sergeant Dewey | Snowhill, in front of Town Hall | "Becoming a Warrior" | Not in our npcs | No |
| Postman (4) | Felipe | Sanctuary | "Postman: Can You Deliver?" | Not in our npcs | No |
| Card Duelist (120) | Poe Tatters, Sam Potts, Garrison Gold | Sanctuary, Snowhill | "Card Duelist: Enter the Champion" | None in our npcs | No |
| Soccer Star (52) | Manager Landon | Snowhill Soccer Field | "Soccer Star: Becoming a Soccer Star" | Landon 1573 wears a merchant model, while Scotty Trackrunner (2351) wears `human_m_manager_landon`. Unclear | No |
| Adventurer (1) | Everyone's first job | – | – | – | – |

Pet Trainer (removed in 2009) isn't in `Profiles.json`. The wiki's "Ice Mage" and "Pixie" aren't jobs.

A later Job Center in Sanctuary had Queen Valerian send you to Ellandra Shieldbreaker (2385), Maximus Axegrinder
(2338) and Scotty Trackrunner (2351) (A: FR_Quest:Visiting_the_Job_Center). What they did isn't documented, so they
teach nothing here.

## What was built

- **The grant** (`Sanctuary.Game/Jobs/JobGrant.cs`, `JobManager.cs`): the job's `Profiles` row at
  `ProfileHelper.StartingLevel` with its starter kit (`ProfileHelper.GrantDefaultItems`, which fits the gender and
  reuses owned items), saved first. A job with no kit of its own wears the Adventurer's clothes, as `UnlockAllProfiles`
  does at character creation. Then the kit's new items are sent, the job is added to the player, and the player is
  switched into it with the job swirl. A job the character has changes nothing. Referee and Enforcer are refused: login
  gives and takes them with the staff flags. Character creation, `UnlockAllProfiles`, the staff jobs at login, the
  login job list and the job switch now share this code (`ProfileHelper.TryAddProfile`, `CreateClientProfile`,
  `AddClientProfile`, `Activate`).
- **Trainers** (`Sanctuary.Game/Jobs/JobTrainers.cs`): clicking a trainer opens their menu with one button per job
  they teach that you don't have, labelled with the job's name (its tooltip is the job's description), next to any
  quests they offer. Taking a job is always a menu pick, never the click alone. Once you have the job, the trainer
  behaves as before. The live game's first quests aren't recreated: `Quests.json` belongs to a local session, and an
  interaction is what the task allows.
- **`!job grant <profileId> [player]`**: mods and admins (the manager's role check, as `!mod`). Online players get the
  job at once; offline characters get it saved for their next login. Logged in the audit log.
- **Lures** in the coin store (`CoinStoreItems.json`, item category 3, the lures' own): all thirteen, 50 coins less the
  10% member discount.

## Still to look at with the client

None of these could be done in the cloud session, which has no client.

1. `tools/client-strings/strings.py <client> search "trainer"`, `"become a"`, `"new job"` and `"tryout"`: a client
   string for "Become a Fisherman" would make a better button than the bare job name, and a "You are now a ..."
   string could go in a notice.
2. In the decompiled UI Lua, what the jobs window does with `ClientUpdatePacketActivateProfile` for a job it doesn't
   have, and how it reads a `ProfileAdd` reward entry (its layout after the common fields).
3. What sends `CommandPacketSetProfileByItemDefinitionId` (op 37 / 36), and whether some item switched jobs.
