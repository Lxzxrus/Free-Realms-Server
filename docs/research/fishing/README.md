# Fishing (task 33)

Free Realms fishing, ported from edenfps' upstream OSFR [PR #31](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/31)
("Add the fishing mini-game", branch `fishing` of `edenfps/Sanctuary`, commit `70c664b`). edenfps reverse-engineered
opcode 138 from the 2014 client and played it in game at Sacred Grove Shallows. **Nothing here has been checked in
our game client yet**; the checklist is at the end.

**Licence.** edenfps' fork had no licence file at that commit, but the work was offered as a pull request to the
AGPL-3.0 OSFR repository, so this port relies on that offer and credits edenfps.

## The four documents beside this one

They are edenfps' own notes, kept as written. Read them in this order:

1. [`FISHING_HANDOFF.md`](FISHING_HANDOFF.md): the map. What works, why, and the traps found on the way.
2. [`FISHING_RE_NOTES.md`](FISHING_RE_NOTES.md): every packet's layout and what the client does with each field.
3. [`FISHING_WIKI.md`](FISHING_WIKI.md): the six holes, their fish by level, rods and lures, from the wiki.
4. [`FISHING_1TO1_PLAN.md`](FISHING_1TO1_PLAN.md): what full fidelity needs (levels, XP, collections, the tension
   minigame) and in what order.

Where they mention paths, they mean edenfps' checkout (`src/Sanctuary.Gateway/Fishing/...`, `StartingZone`). In our
code the pieces live as below. Where they say to type `tp x y z`, `tporigin` or `/pos`, use `!teleport x y z`
(moderators and admins), `!teleport 0 0 0` and `!whereami`.

## Where it lives here

| Piece | File |
|---|---|
| Packets, opcode 138 | `src/Sanctuary.Packet/BaseFishingPacket.cs`, `src/Sanctuary.Packet/BaseFishingPacket/` |
| Their structs | `src/Sanctuary.Packet.Common/Fishing/` |
| The fishing state bit (player update 20) | `src/Sanctuary.Packet/BasePlayerUpdatePacket/PlayerUpdatePacketUpdateCharacterState.cs` |
| The session: cast, bite, reel, catch | `src/Sanctuary.Game/Minigames/Fishing/FishingSession.cs` |
| What a cast brings up | `FishingCatch.cs` (the roll), `FishTables.cs` (fish, treasure, junk), `FishingGear.cs` (rods, lures) |
| The holes | `src/Resources/Zones/*_fishing_*.json` (zone type `"Fishing"`), `src/Sanctuary.Game/Zones/FishingZone.cs` |
| Joining, starting, ending; the fishing packets | `src/Sanctuary.Gateway/Handlers/Minigames/FishingHandler.cs`, routed by `BaseFishingPacketHandler.cs` |
| Lures from the action bar | `src/Sanctuary.Gateway/Helpers/Abilities/FishingLureAbility.cs` |
| Tests | `src/Sanctuary.Game.Tests/FishingSessionTests.cs`, `FishingPacketTests.cs`, `FishingResourceTests.cs` |

## How a player fishes

1. **Start screen.** Choosing a hole in the Games menu (activities 560 to 565), or picking a hole on the map's
   points of interest while in Fabled Realms, sends the hole's start screen: the invite, "launched" and minigame info
   packets edenfps sent. The map first teleports the player to the hole's water in Fabled Realms, as before; from a
   house or another hole it only takes the player back to Fabled Realms.
2. **Into the hole.** The start screen's button sends a start game for the activity. The server takes the player into
   the hole's zone with `Player.TeleportToZone` (zone id = activity id) and sets the fishing state bit, 0x400000. The
   client switches to fishing and asks to register; the loading screen waits for the answer: casting range (from the
   equipped rod), the hole's scene, the underwater bed's X, the water's height and the Fish Finder.
3. **Cast and bite.** A cast spawns the bobber (shown to everyone who can see the player) and the underwater fish (to
   the caster only: they crash a client that hasn't made the bobber). 3.5 to 5 s later a fish gets interested, 1.2 to
   1.8 s after that it bites. Reeling in before the bite scares it off; a fish not reeled in within 12 s gets away.
4. **The catch.** After the fight (1.9 s) the catch is pulled up (1.8 s), then **granted** and held up with its
   banner. One cast in seven brings up treasure (the chest waits on the hook, no bite) or junk (bites like a fish).
5. **Leaving.** The minigame's end, the map, the stuck button or a teleport to a friend in Fabled Realms take the
   player out of the hole; leaving the zone ends the session and clears the fishing state bit. The minigame's end and
   the stuck button return the player to where they stood in Fabled Realms. A relog from a hole comes back there too.
   Nobody can teleport to a friend who is fishing, and a fisher can't go straight into a house.

Lures are used from the action bar while fishing: one is used up, and it lasts until the player leaves the hole. A
lure makes its three fish more likely; the Treasure Magnet brings up more treasure. Outside a hole a lure is
refused and not used up (before, any use ate it for nothing).

## What changed from edenfps' code, and why

- **Our zoning.** The PR sent the zoning packet by hand and kept the player in Fabled Realms, so the server wrote
  fishing-scene coordinates into the overworld position, other players in Fabled Realms saw fishers standing in
  odd places, and nothing ever took a player back out. Here each hole is a zone of its own (`"$type": "Fishing"`),
  entered only through `Player.TeleportToZone`; everyone at a hole shares it, which keeps edenfps' multiplayer view.
  The zoning packet our `TeleportToZone` sends differs from the PR's in two fields: `Id` is the zone instance's id,
  not the activity id, and `Unknown` is 0, not 1 (an in-game check below).
- **Rainbow Lake's scene.** The PR put Rainbow Lake (562) in `bw_fishing_medpond`, Darklit Lagoon's scene, and never
  used `sg_fishing_stream`. Two holes in one zone would share one instance, and Rainbow Lake is a Sacred Grove hole
  (Lakeshore), so 562 is `sg_fishing_stream` here. Its underwater bed X is borrowed from `sg_fishing_medpond` (68):
  the real value is in the client's `sg_fishing_streamAreas.xml`. The zone file says how to go back to the PR's
  mapping if the scene is wrong.
- **Spawns.** Only Sacred Grove Shallows' (563) was checked in game, and its coordinates are the scene's own, not
  Fabled Realms'. The other five are the PR's placeholder (200, 0, 200), marked as guesses in their zone files.
- **Only who joined a hole can start it.** The PR zoned anyone who sent a start game with a fishing activity id;
  here the start screen must have been sent first. The fishing packets do nothing unless the player is in a hole.
- **Catches are saved like every other item.** The PR wrote a new item row per catch, which the database's unique
  (definition, tint) index refuses from the second catch of a species on; the PR then kept the fish in memory only
  (gone on relog, and trading refused until then). Here a catch goes through `IRewardManager.TryGrantItem`, which
  stacks it, saves it first and only then updates the bag. It is granted on the Gateway's packet thread, under the
  same guard a trade takes, so a catch can't land in the middle of a trade being saved.
- **The catch's notice.** `TryGrantItem` shows the reward-bundle notice our other rewards use, not the PR's yellow
  "You received" line (opcode 50 sub-type 2, our `RewardNonBundledItemPacket`). An in-game check below.
- **Lures** use our consumable-ability system (the action bar's item tracking and `ConsumeItem`, which saves before it
  touches the bag) instead of a second action-bar map and a consume that ignored the database.
- **Debug commands.** The PR's `tp`, `tporigin` (no role check, matched on any chat line starting with "tp") and
  `/pos` are not ported. Our `!teleport` (moderators and admins) and `!whereami` already do the same.
- **Testable.** The session and the catch rules live in `Sanctuary.Game`, with time and randomness passed in.
- **No re-rolling.** The fish run shows what a cast will bring up (the chest is the catchable first fish), so a
  client could cast again and again until it saw the chest. A catch is now rolled at most once per 8.4 s, the
  quickest honest cast to catch; casting again sooner brings up the same thing.
- **Smaller things.** A relayed cast pose carries the caster's own guid whatever the client wrote, needs a finite
  position and distance, and is relayed at most twice a second; casts at non-finite positions are ignored; a cast
  while the catch is coming up doesn't throw it back; a lure already on isn't used up again; client-driven logging
  is at Debug. The Fish Finder is sent in its own packet after the register answer, as edenfps did.

## Not ported

- **Ambient fish and fishing-spot markers in Fabled Realms** (`AmbientFishNpc`, `StartingZone.SpawnFishingScenery`).
  Unfinished in the PR (fish that might not move) and spawned at the login point as a diagnostic.
- **The tile-visibility reorder in `BaseZone.UpdateEntityZoneTile`.** edenfps fixed one-way visibility between two
  players loading at once by adding an entity to its own tile before telling its neighbours. That race can't happen
  here: every packet handler runs on the Gateway's one packet thread, so two players never move at once. The race
  that is left, a player against a moving NPC on a zone's thread, the reorder doesn't close, and it can send a
  duplicate add. If two players at a hole don't see each other (check 11), this is the first place to look.
- **The Gateway's extra console log file** (`NLog.config`): every Trace line to disk, too much for a public server.
- **XP for fishing** (task 31), **collections** ("What I've Caught") and **the tension minigame**: out of scope, and
  not in the PR either. Fish rolls aren't gated by fishing level yet; the levels only make high-level fish rarer.

## Open questions for Nate

- **The treasure chest is item 3016,** a members-only house fixture (`hsg_chest_02`, cost 50,000, no trade), granted
  to anyone, member or not. About one cast in seventeen brings it up (one in six with the Treasure Magnet). On a
  housing server that may be too generous, or the wrong item.
- **No rod or job check on the server.** The real client needs a rod to cast, but a modified one could fish without.
  Catches pay no coins today (fish have no resell value), so this is left for later: refuse a start or a cast when
  `FishingHandler.EquippedRodId` finds no rod.

## Check in game (Nate)

Use a character with the Fisherman job (from Reed Stillwater, or `!job grant 137`; see `docs/research/jobs.md`)
and a fishing rod equipped: the job's starter kit is clothes only, and the client needs the rod to cast. The coin
store sells the five rods and, since task 34, the thirteen lures, as the live game's Coin Shop did: 45 coins each (50
less the 10% member discount). Buy five Skipper Seekers for step 10.

1. **Get there.** Open the map's points of interest and pick **Sacred Grove Shallows**: you land by the water in
   Fabled Realms and the hole's start screen opens. (Or open the Games menu: six fishing holes are listed.)
2. **Into the hole.** Press the start button. The `sg_fishing_medpond` scene loads, the loading screen drops (if it
   hangs, the register answer didn't arrive: note it) and you are in fishing controls by the water.
3. **Cast.** Aim at the water and cast. The bobber lands, the line hangs from the rod, and fish swim below.
4. **Bite.** After 3 to 5 s a fish swims to the hook, then lunges and bites.
5. **Reel.** Reel in after the bite. The fish fights, is pulled up and held up with a banner (name, size, weight),
   and the camera comes back by itself. Note which notice appears for the item (a reward window or a yellow line at
   the bottom), and whether more than one does.
6. **Bag.** The fish is in the bag. Catch the same species again: one stack, x2.
7. **Too early, too late.** Reel in before a bite: "Nothing caught". Let a fish bite and don't reel for 12 s: it
   gets away.
8. **Relog** (inside the hole, or after leaving): you come back in Fabled Realms where you stood before fishing, and
   the fish are still in the bag.
9. **Leave.** In the hole, find how the client leaves fishing (an exit button, the minigame window's close, Escape).
   Note what happens: the server returns you to Fabled Realms on the minigame's end. The stuck button and the map's
   points of interest must also take you out.
10. **Lure.** Put the Skipper Seekers on the action bar. Outside a hole, using one is refused and the count stays.
    At Sacred Grove Shallows one is used up (the count drops) and Slugmud Skippers come up more often (about one
    cast in four instead of one in six).
11. **Two players** at Sacred Grove Shallows: each sees the other cast, bite and catch.
12. **The other five holes.** Start each from the Games menu. Their spawn points and Rainbow Lake's scene are
    guesses: note where you land. If it's wrong, stand by the water, type `!whereami`, and send the numbers.
