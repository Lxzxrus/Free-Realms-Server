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
the Wilds quarters split by quadrant around Sanctuary (x grows east, z grows south). **That pairing is only partly
verified in game** (see "Checked in game" below). The collection and entry name ids are in `Collections.json` for `tools/client-strings/strings.py`.

## Gaps

The 2014 npc list (hence PR 66's spawns) lacked coins for 14 entries: Snowhill Rare 4 and 5, Sanctuary Rare 5 and 6,
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
- **Between Ninjas And Graveyards** (Blackspore Elite) is at the right place, but the ground around it has changed
  and it can't be reached on foot. It needs moving to the nearest spot a player can stand on.

`ExplorationCollectionTests.CoinsCheckedInGame_TickTheEntryNamedForTheirPlace` pins these pairs. The other regions'
pairings are still the id-order guess, so expect the same kind of shift there.
