# Exploration collections: what the 2014 captures hold

*Task 29 (`exploration-collections`). Read with `docs/research/quests-scout.md` section 3, which describes the
captures and how they decode.*

## Source

The `SendSelf` burst in `possible_character_data.pcapng` (archived `OpenSourceFreeRealms` repo, `ofrserver/Packets`,
no licence, reference only) carries the captured character's **253 collections** with ids, name ids, icons, rewards
and entries. The decoder is 200 lines of standard-library Python written for the task and kept out of the repo, as
the quest scout did. The collections list starts at offset 8,718 of the 204,327-byte payload, after the profile
types; the 1,382 items follow it.

## Wire layout of a collection (`ClientCollection`)

Every row parses as: `Id, NameId, DescriptionId, CategoryId, IconId, IconTintId, int 1`, then one **reward bundle**
with the same layout as the reward packets (`RewardBundleBase`: success byte, coins, 8 more ints with the third
always 15 and a float 1.0, source guid, player guid, icon, name, entries), then **one int** (5604 for the Briarwood
mushrooms, 5301 to 5314 for the Rare Exploration collections, 6314 to 6322 for the Elite ones; its meaning is unknown,
probably a string id), then the entries (`Id, DefinitionId, Index, CollectionId, NameId, IconId, IconTintId, int 0,
collected byte`).

Reward entry tails depend on the entry type, not on the success byte: **Item (1) and QuestAdd (6) and CollectionAdd
(10) carry a 4-byte tail; Experience (3) and Token (12) carry none.** Sanctuary's `ClientCollection` used to spell
this bundle out field by field, which only worked for a single experience entry; it now embeds `RewardBundleBase`
and the two tests in `CollectionDefinitionTests` compare our bytes with the captured rows of collections 10 and 267.

What the mushroom collection's old "unknown" fields were: `HeaderMetadata` 9 is the coin reward,
`Unknown13` 3 is the experience entry type, `RewardPoints` 50 the experience, `RewardMetadata` 11 the profile it goes
to. `Collections.json` now names them (`RewardCoins`, `RewardExperience`, `RewardProfileId`, `RewardTextId`,
`RewardItemIds`).

## The exploration collections

| Ids | Count | What | Reward |
|---|---|---|---|
| 265 to 276 | 12 | Rare Exploration: Snowhill, Sanctuary, Seaside, Blackspore, Briarwood, Wugachug, Merry Vale, Shrouded Glade, Wilds SE, SW, NE, NW | one or two pieces of Traveler's, Seeker's or Pathfinder's clothing; no coins, no experience |
| 284 to 291, 309 | 9 | Elite Exploration: Snowhill, Wugachug, Shrouded Glade, Briarwood, Blackspore, Seaside, Merry Vale, Sanctuary, Sacred Grove | "Elite Explorer Backpack of <region>" plus a Token entry (type 12, definition 520 to 527; not a player title, meaning unknown, not granted) |
| 244 to 264 | 21 | The "Explorer" collections of whole regions and places (Token rewards), out of scope | |

**Reward colours (2026-10-27).** Each clothing name exists as dozens of item ids: fixed-colour versions, whose colour
is their icon tint, and a few dyeable ones (`IsTintable`), whose icon tint is 227 or 228, the first tint of the
client's two dye groups (`Resources/TintGroupMappings.txt`), so a placeholder. The capture's Pathfinder's Pants and
Capris (265), Seeker's Pants (270) and Traveler's Shirt and Tank Top (273) are dyeable ones and came out bright red
whatever tint we saved. They are now the fixed-colour versions in their set's colour, the colour of the set's other
rewards: Traveler's 234 (commoner), Seeker's 243 (cloverleaf), Pathfinder's 237 (turbo). No girls' Pathfinder's Boots
come in turbo, so girls get sunrise (231, in `RewardItemIdsGirls`) rather than the first girls' version, which
was red. Missing Homework's Striped T-Shirt is likewise the fixed white one (32532, snowfall 263).

Regions were matched through the collection icon (2232 Blackspore, 2233 Briarwood, 2234 Wugachug, 2235 Merry Vale,
2236 Sanctuary and the Wilds, 2237 Seaside, 2238 Shrouded Glade, 2239 Snowhill), which the coin npc models confirm,
and through the wiki's reward list, which agrees with the capture on all twelve Rare collections. The capture has no
Sunstone Valley exploration collection and the wiki has none. Its two collections (9001 Rare, 9002 Elite; our own ids)
come from a video of a completed collections window that Ariel found on 2026-10-12: names 3795/3797, descriptions
3796/3798, entries 8797 to 8804 and 8805 to 8812 in the window's order, icon 7913 (`poi_env_sunstone_valley`). Each
has eight entries and seven coins, paired in order, so its last entry has no coin. The rewards are shown only as
pictures and aren't granted yet. Elite coins are on neither the wiki nor PR 66's list of what they are; the capture says they fill the
Elite collections, and that is what they do now.

Entry ids are **not** the coin npc ids. They are their own id space (1935 to 1989 Rare, 2012 to 2066 Elite,
2170 to 2177 Sacred Grove). The entries' creation order matches the coins' npc id order in the one region where both
show a gap (Seaside: one early, four later), so entries were paired with coins in that order, region by region, with
the Wilds quarters split by quadrant around Sanctuary, assuming x grows east and z grows south. That compass was
wrong (x grows north, z grows east; see "Coin research" below), so until 2026-10-24 each quarter had another
quarter's coins. **That pairing is only partly
verified in game** (see "Checked in game" below). The collection and entry name ids are in `Collections.json` for `tools/client-strings/strings.py`.

## Gaps

The 2014 npc list (hence PR 66's spawns) lacked coins for 14 entries: Snowhill Rare 4 and 5, Sanctuary Rare 4 and 5 (placed on 2026-10-25, see below),
Wilds Northeast Rare 5, one Blackspore Elite entry (Oberyn's Broken Home, placed on 2026-10-20, see below), and all of
Sacred Grove Elite (that hub isn't in our world). Those
collections cannot be finished until the coins are placed (`!admin collection place <pool>` writes a spawn; the
spawn then needs its `CollectionEntryId`).

## Checked in game

Ariel collected three whole sets on 2026-10-20 and named each coin's place from the original game's guides (its
position, and video walkthroughs of the collections). The pop-up and the collections book always agreed with each
other, so a wrong name meant a wrong `CollectionEntryId`.

- **Briarwood Elite** (287): all eight right; the backpack was granted.
- **Blackspore Elite** (288): the id-order pairing was off. Branching Out, A Relic View, Hollow Hiding Hole and
  Splortata's Timeout Spot each carried another place's entry, and the coin with no entry was really Oberyn's Broken
  Home's, not the eighth. All re-paired; Oberyn's coin (spawn 4493) placed at Ariel's `!whereami`.
- **Briarwood Rare** (269): four of five off by one place (Campfire, Pond-er, Xandril's Nook, Spore Falls); re-paired.
- **Snowhill Rare** (265) and **Elite** (284), 2026-10-21: the two sets were crossed. The elite coin npc 4435 stands
  at Perry's Lookout, a Rare place, so it moved to the Rare pool; Penguin Point and Smitty's Hiding Place, listed as
  gaps, are the coins at those places. Seven Elite coins re-paired, and Melted Corner's coin (spawn 4494) placed at
  Ariel's `!whereami`. **Between the Peaks** (Rare) has no coin: its hillside was raised and a camp built where the
  coin stood. Since 2026-10-24 it stands at the nearest place Ariel could reach (spawn 4495).
- **Between Ninjas And Graveyards** (Blackspore Elite) is at the right place, but the ground around it has changed
  and it can't be reached on foot. Since 2026-10-24 it stands at the nearest spot Ariel could reach.
- **Round 3 (2026-10-24)** on a fresh character: Blackspore Elite, Briarwood Rare and Snowhill Rare and Elite all
  correct after the re-pairing; Snowhill Rare's reward came out as the girls' Pathfinder's Capris.
- **Sanctuary Rare** (266) and **Elite** (291), 2026-10-24: Rare's four coins were right after the research below;
  Pixiewood Mist and A Royal Way In, listed as gaps, now have coins at Ariel's `!whereami` (spawns 4496 and 4497).
  Elite's A Small Cell and Darvon's Hook were right; four coins were one place round a loop (Left Falls, Tree Cover,
  Shallow Water, Up the Palace Walls) and Spying on the Warriors and Sunset Cliffs were swapped. All re-paired; the
  backpack was granted.

`ExplorationCollectionTests.CoinsCheckedInGame_TickTheEntryNamedForTheirPlace` pins these pairs. The other regions'
pairings are still the id-order guess, so expect the same kind of shift there.

## Coin research (2026-10-24)

For the sets nobody had checked, each coin was matched by its surroundings: the nearest points of interest
(`PointOfInterests.json`), the nearest named npcs (`FabledRealms.lua`), the client's ambient areas around it
(`FabledRealmsAreas.xml`: waterfalls, rivers, rainbows, the Roadhouse sign) and the wiki's location notes
([Exploration (Collections)](https://freerealms.fandom.com/wiki/Exploration_(Collections)) and its Sunstone Valley
page). The script is kept out of the repo, like the capture decoder.

**The map's compass: x grows north, z grows east.** Three checks agree: Sunstone Valley lies north of Briarwood and
north-east of Shrouded Glade (the wiki's words); the Forest Troll camp is north of Lakeshore ("the first camp walking
north from Lakeshore"); the Snowhill soccer field is south of Snowhill ("overlooks the soccer field ... to the south").
Turning the old quadrants by a quarter makes the Wilds names fit their coins.

| Set | Change | Confidence |
|---|---|---|
| Sanctuary Rare | Break my Fall: the coin by Lorna under the caverns' waterfall (2483). Over the Rainbow: the coin in the rainbow and waterfall spray at the moat's west end (2473). Darvon's Spot and Break Free unchanged. Pixiewood Mist and A Royal Way In have no coin | high |
| Wilds Northeast | Took the five coins that were "Southeast": Troll Scope by the Forest Troll Fort (4492), Perfect Robgoblin View by the Robgoblin Treasure Trove (4491), Rushing Twin Falls in two waterfalls (4489), Rocking the River on the river (4488), Swimmer's Haven at Lakeshore (4490) | high |
| Wilds Southeast | Took the "Southwest" coins: Wildwood Shroud by Wildwood Speedway (4477), Pixiewood Stare just south of Sanctuary (4480); Serpent's Climb, Atop Spore Flow and Peace of Mind guessed (4474, 4478, 4479) | medium, then low |
| Wilds Southwest | Took four "Northwest" coins: Spying on Bandits by the Bandit Hideout (4470), Shepherd's Watch on Farnum Farmstead (4473); Towering the Toll and Ancient Dig Site guessed (4465, 4467); Tent Dent has no coin | medium, then low |
| Wilds Northwest | Took the "Northeast" coins and Merry Notch by Merry Vale (4471); Tin Man's Nest, Bridge View, A Path Less Traveled and Gazing Beyond guessed (4481, 4482, 4484, 4485) | medium, then low |
| Sunstone Valley Rare | Wheelie Pete's Roadhouse under the Roadhouse sign (4446), Noggle's Retreat by Noggle Cogloose (4463), The Dig by Sarkin Stonehearth (4448), Scorch Stream Falls at the waterfall (4451); Abandoned Mine and Sunstone Arch guessed (4445, 4453); Mystical Barrier unchanged; The Great Rift has no coin | high, then low |
| Sunstone Valley Elite | Sandspear's Stand by Sandspear (4450), The Scrap Yard by Scrapmaster Fizzbang (4452), Skullz Base Camp among the Skullz (4455), Sandscale Tribal Lands among the Sandscale (4454); Drake Boneyard and Drakewind Peak guessed (4447, 4449); Vulture Alley unchanged; Glimmerdeep Pass Overlook has no coin | high, then low |

Unchanged, because the evidence fits the id-order pairing or doesn't decide it: Seaside Rare (Behind the Weeds by Amai
and Seaspray Hideaway by the inn fit; Bridging the Gap and Under the Boardwalk can't be told apart without the
volleyball net), Shrouded Glade Rare (all three fit the wiki), Wugachug Rare (Two Trees by the warpstone fits), Merry
Vale Rare, Blackspore Rare, and the Elite sets of Wugachug (Stage Presence on the stage fits), Shrouded Glade, Seaside
(Sea-cret Spot behind the small waterfall and Oceanfront Property among the houses fit), Merry Vale and Sanctuary
(Darvon's Hook fits). The guesses and these sets still need a check in game; the old guides' coin videos are the
quickest way to tell each place.
