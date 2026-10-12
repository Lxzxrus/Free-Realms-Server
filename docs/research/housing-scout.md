# Housing scout (task 25)

*Research only, 2026-10-12. Nothing here is ported or verified in the game client. Every source's licence is
recorded; "reference only" means read it, don't copy code or data from it.*

We can enter two of the 31 houses in `src/Resources/Houses.json`. The data to open most of the rest already
exists, in the open and under the AGPL: one fork worked out the client zone name, sky and room geometry for 29 lots
and wrote down what the client requires. The Marketplace side of every lot (deed item, icon, store bundle) is already
in our resource files. Free Realms never shipped neighbourhoods; a "plot" was always one instanced lot per owner,
which is what we have. The remaining gaps are fixtures the client has models for but no item for, and icons that are
missing from the client's packs, for which no public source turned up.

## 1. Summary: the five best finds

Ranked by value to players against effort to bring in.

1. **`AlchemyDevelopment/Sanctuary`, branch `feature/housing` (AGPL-3.0): zone names and skies for 29 of 31 lots,
   room geometry for six houses, and a 663-line client handoff.** One commit by Jacob Plunkett ("Plunk"),
   [c301cee, 2026-08-21](https://github.com/AlchemyDevelopment/Sanctuary/commit/c301cee), 80 files, 15,659 lines,
   132 commits behind upstream `main`. Its [`Houses.json`](https://github.com/AlchemyDevelopment/Sanctuary/blob/feature/housing/src/Resources/Houses.json)
   adds `ZoneName`, `Sky`, `GeometryId`, and for the houses with walls, `SurfaceGroups`, `Rooms`, `RoomOrigin` and
   `RoomRotation`, all generated from the client assets by three Python tools in
   [`tools/`](https://github.com/AlchemyDevelopment/Sanctuary/tree/feature/housing/tools) (`.gzne` zone names,
   `.gcnk` terrain object ids, `.cdt` floor polygons). The commit message says 29 of 31 lots are enterable and
   names five unverified assignments (lots 18, 29, 31, 32, and lot 30's spawn point); the author verified placement
   and wallpaper in game on the Large Wilds House (lot 26).
   [`HOUSING.md`](https://github.com/AlchemyDevelopment/Sanctuary/blob/feature/housing/HOUSING.md) records what the
   client requires, from the client binary, with the failed attempts. **Zone files for the empty lots are an S-sized
   data port** (section 7). Its wallpaper method is M and needs a change to the client's `Models.txt` (section 2).
2. **The lot catalogue is already in our resources.** `ClientItemDefinitions.json` holds 36 lot deeds (item
   `Type` 16) with names, icons and `NameId`s, and `StoreBundles.json` holds a Marketplace bundle for 22 of them, 24 bundles in all
   (bundle group 206 for empty lots, 125 for houses). Our purchase handler finds a lot's zone by the zone file's
   `StoreBundleId`, so for every lot with a bundle, buying it already works once a zone file exists. Section 3 maps
   all 31 houses to their deed, bundle, zone name and sky.
3. **Plot instancing is solved, and neighbourhoods never existed.** SOE announced "instanced neighbourhoods" with
   three buildings per plot at E3 2009
   ([Engadget](https://www.engadget.com/2009-06-08-e3-2009-player-housing-coming-to-free-realms.html)); what
   shipped in December 2009 was one instanced lot per owned deed, entered from the house icon, with a menu when you
   own several ([fandom: Lots](https://freerealms.fandom.com/wiki/Lots)). Visiting went through friends and the
   **Housing Directory** (publish, 1 to 5 stars, sort by rating, views or newest; a published house can't be edited
   until unpublished; views and rating reset on re-publish;
   [fandom](https://freerealms.fandom.com/wiki/Housing_Directory)). Our `ZoneManager` already creates one
   `HousingZone` instance per house row. "More plots" is therefore the design's two plots per account plus more lot
   types, not a new zoning feature. The Clone Wars Adventures server [`soir20/oxide`](https://github.com/soir20/oxide)
   (AGPL-3.0) uses the same model, one zone template per lot asset and one instance per house guid, but its housing
   is a stub with a hard-coded test house.
4. **369 housing models in the client have no item, and so are not in the catalog.** `Models.txt` names 801
   `hsg_` models; 394 are the `ModelName` of a shipped item definition and 29 more get a definition from our
   `HousingItemDefinitionGenerator` (built from store bundles). Of the remaining 378, nine are house wall and floor
   meshes; the other 369 are placeable-looking fixtures: 85 Snow Days toys (`hsg_toy_*`), 73 seasonal pieces
   (Halloween, Valentine's, birthday, holiday, snowflake blocks), 43 block variants (gloamstone, precursor, "v2"
   blocks, doors and windows), 42 farming plants, 24 Trading Card Game and Treasure War trophies and paintings,
   18 Player Studio pieces, 10 VIP pieces, and about 80 others (lodge and biker furniture sets, bleachers, racing
   checkpoints and teleport pads, traps, savanna trees, cacti, flamingos, string lights, a boardwalk). Making them
   placeable needs an item definition each, and a name string the server can't supply without the client's string
   table (section 5). Allakhazam's wikibase lists 766 house items by type (253 blocks, 149 furniture, 81 outdoors,
   72 wall hangings, 69 decorations, 48 walls, 28 floors, 22 exterior, 21 platforms, 4 roofs), a usable checklist.
5. **raisingkaines' public forks hold nothing newer than our import.** All 13 `housing-*` branches of
   [`raisingkaines/Sanctuary`](https://github.com/raisingkaines/Sanctuary/branches) are slices of
   `housing-full-archive`, whose tip `01bec64` is our `import/housing-full-archive` and the head of upstream
   [PR #111](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/111) (closed unmerged 2026-09-11 as too
   large and "architected by AI"). [`raisingkaines/FreeRealms-Legacy`](https://github.com/raisingkaines/FreeRealms-Legacy)
   `main` (tip `9e08202`, 2026-09-23) is our `import/frl-main`, and has no housing beyond upstream's packet
   classes. The housing update that broke lot loading on his server is not public.

## 2. What a working house zone needs

Compared from `src/Resources/Zones/hsg_emptylot_seaside_cliffs_01.json`, `hsg_hum_blackspore_01.json`, their
`Houses.json` entries (ids 14 and 22) and the code that reads them.

| Field in the zone file | Where it comes from | Used by |
|---|---|---|
| `Id` | `Houses.json` `ZoneId` (equals `Id`) | `DbHouse.ZoneDefinitionId`, `Player.TeleportToZone`. **Zone ids share one table with world zones** (`ZoneDefinitionCollection` is one dictionary, `FabledRealms.json` is id 1), so lot 1 can't use id 1. AlchemyDevelopment offsets house zones by 1,000,000. Ours must keep 14 and 22, because existing `Houses` rows point at them |
| `Name` | **The client's zone asset name** (`hsg_emptylot_seaside_cliffs_01`). Sent in `PacketClientBeginZoning`; the client loads that `.gzne` and its chunks. Not in upstream's `Houses.json`; AlchemyDevelopment derived it for 29 lots by matching names and terrain data | `Player.TeleportToZone`, `HousingSurfaceCatalog` key |
| `DisplayName`, `CommandName` | Ours (`/house <command>`) | Chat command, fallback house name |
| `NameId`, `IconId` | `Houses.json`; the `NameId` is also the deed item's | Directory and house list packets |
| `StoreBundleId` | `StoreBundles.json` (section 3) | **`PacketInGamePurchasePlaceOrderPacketHandler` finds the lot by this**; `BaseZone` lists them for the store |
| `ItemDefinitionId` | The deed item, `Type` 16 | Purchase |
| `DirectorySnapshot` | Client UI image name (`seaside_lot`, `blacksporeswamphouse_lot`) | `PlayerHousingInstanceInfo.Unknown21`, the directory picture. Unknown for the other 29 (section 9) |
| `Sky` | AlchemyDevelopment's data (`sky_seaside24.xml` and so on) | `PacketClientBeginZoning.Sky` |
| `TileSize`, `Start/EndLatitude/Longitude` | 64 and a 12 by 12 grid in both files; AlchemyDevelopment uses -2 to 32 | Zone tiles for interest management |
| `SpawnPosition`, `SpawnRotation`, `BuildAreas` | `Houses.json`. Both of ours match AlchemyDevelopment's data exactly. Lot 1's spawn is outside its own build area (X 444.3 against max 443.3) and the client refuses placement outside, so the arrival position has to be clamped | Teleport, `HousingPacketInstanceData.BuildAreas` |

The two houses differ in one thing: Blackspore has walls, so `HousingSurfaceCatalog` maps its zone name and
`Floor`/`Wall`/`Roof` to model ids 5000 to 5008, nine `hsg_hum_house_blackspore_01_*` meshes raisingkaines added to
`Models.txt`. An empty lot needs no surface table.

**Where the rest of the data for 29 houses comes from.** AlchemyDevelopment's `Houses.json` gives `ZoneName` and
`Sky` for every lot but the two farms, and `SurfaceGroups` plus `Rooms` for six houses (22 Blackspore, 24
Apartment, 26 Large Wilds, 27 Small Wilds, 28 Club House, 29 `hsg_hum_snowhill_01`). Its spawn points and build areas
equal upstream's for 28 lots; it moved lot 30 (Rumbledome) to `gw_rumbledome` at (175, 72.5, 300), and raised the
spawn height of lots 31 and 32. ImAlko's
[`major-feature/zones-testing`](https://github.com/ImAlko/Sanctuary/tree/major-feature/zones-testing/src/Resources/Zones)
holds one more zone file, `hsg_emptylot_seaside_beach_01.json` (presumably the Sandy Beach Lot), but it is a stub
with the Shrouded lots' spawn point copied in and no build area. No other fork, PR or branch has a housing zone file.

**Two ways to do wallpaper.** raisingkaines (ours) re-textures the house meshes by model id, which needs the mesh
rows in `Models.txt`. AlchemyDevelopment binds an npc to each surface by its **terrain object id** from the lot's
`.gcnk` chunks, then spawns a second npc carrying the mesh as a model for collision, and resolves which room the
player stands in server-side from floor polygons, because the client always answers "Exterior". Both approaches add
rows to `Models.txt`, and AlchemyDevelopment states that **the client's copy of `Models.txt` must carry the same
rows** or the surfaces have no collision. That is a client file change, so it interacts with task 22's whole-client
pinning: the launcher would have to ship and pin our modified `Models.txt`. Whether raisingkaines' ids 5000 to 5008
are in the stock client file is a question for the client (section 9); Nate's playtest of 2026-10-07 entered
Blackspore and placed fixtures, and the commit does not say whether wallpaper was tried.

## 3. The 31 houses, with what each is missing

Deed and bundle ids from our `ClientItemDefinitions.json` and `StoreBundles.json`; zone names and skies from
AlchemyDevelopment's data (reference for `ZoneName` is the client's `.gzne` list, section 9); prices from the fandom
wiki, where it has a page. "Rooms" means AlchemyDevelopment has surface and room data for it.

| Id | Lot (deed name) | Client zone (`ZoneName`) | Sky | Deed item / store bundle | State |
|---|---|---|---|---|---|
| 1 | Blackspore Swamp Lot | `hsg_emptylot_blackspore_01` | `sky_blackspore24` | 17567 / 4666 | Data complete. Spawn outside build area |
| 2 | Sunset Party Boat | `hsg_emptylot_boat_01` | `sky_seaside24` | 79149 / none | Data complete; was a February 2014 member gift ([fandom](https://freerealms.fandom.com/wiki/Sunset_Party_Boat)), no bundle |
| 3 | Bog Shore Lot | `hsg_emptylot_bog_shore_01` | `sky_housing_wilds` | 17192 / 4701 | Data complete |
| 4 | Briar Falls Lot | `hsg_emptylot_briarfalls_01` | `sky_briarwood24` | 17193 / 4846 | Data complete |
| 5 | Briarwood Lot | `hsg_emptylot_briarwood_01` | `sky_briarwood24` | 7485 / 4295 | Data complete, 300 SC originally |
| 6 | Briarwall Towers Lot | `hsg_emptylot_bw_towers_01` | `sky_briarwood24` | 17189 / 4978 | Data complete |
| 7 | *unknown* (`NameId` 510215) | `hsg_emptylot_castle_01` | `sky_briarheart_castle_interior` | none | No deed, no wiki page. Unreleased or internal |
| 8 | Shrouded Stairs Lot | `hsg_emptylot_gl_staircase_01` | `sky_bristlewood_glade` | 10338 / none | 400 SC originally. Bundle 4715 is **named** "Shrouded Stairs Lot" but sells deed 17183, the Gloam lot: a data defect to check |
| 9 | Shrouded Glade Lot | `hsg_emptylot_gl_valley_01` | `sky_hidden_valley24` | 17182 / 4633 | Data complete, 300 SC |
| 10 | Lonely Island Lot | `hsg_emptylot_island_01` | `sky_seaside24` | 10386 / 4596 | Data complete |
| 14 | Seaside Lot | `hsg_emptylot_seaside_cliffs_01` | `sky_seaside24` | 10384 / 4536 | **Enterable today** |
| 15 | Bat Cavern Lot | `hsg_emptylot_sh_batcave_01` | `sky_bat_cave` | 17195 / none | Data complete; was a Treasure War loot card ([fandom](https://freerealms.fandom.com/wiki/Bat_Cavern_Lot)) |
| 16 | Crystal Mines Lot | `hsg_emptylot_sh_crystalmines_01` | `sky_deep_mines` | 17191 / 4786 | Data complete |
| 17 | Shrouded Gloam Lot | `hsg_emptylot_shrouded_gloam_01` | `sky_shrouded_gloam` | 17183 / 4715 | Data complete (bundle mis-named, see 8) |
| 18 | *unknown* (`NameId` 441205) | `hsg_emptylot_snowdays_01` | `sky_hidden_valley24` | none | **Zone unverified.** Likely the Snow Days Lot, a 2012 "12 Days of Presents" gift ([fandom](https://freerealms.fandom.com/wiki/Snow_Days_Lot)); no deed in our data |
| 19 | Snowhill Lot | `hsg_emptylot_snowhill_01` | `sky_snowhill_housinglot_01` | 7486 / 4296 | Data complete |
| 20 | Vale Stream Lot | `hsg_emptylot_vale_steam_01` | `sky_merryvale24` | 17194 / 4885 | Data complete, 400 SC |
| 21 | Wugachug Lot | `hsg_emptylot_wugachug_01` | `sky_wugachug24` | 10999 and 17185 / 4634 | Data complete, two deeds share the name |
| 22 | Blackspore Swamp House | `hsg_hum_blackspore_01` | `sky_blackspore24` | 11110 / 4709 | **Enterable today.** Rooms: 12 surfaces, 4 rooms. 950 SC |
| 23 | *unknown* (`NameId` 426811) | `hsg_hum_chromakey_01` | `sky_actortool_greenscreen` | none | A green-screen developer zone, not a player lot |
| 24 | Apartment | `hsg_hum_condo` | `sky_sunstone_valley24` | 10337 / none | Rooms: 3 surfaces, 1 room. Every player's first home until January 2012 ([fandom](https://freerealms.fandom.com/wiki/Apartment)) |
| 25 | Wilds Condo | `hsg_hum_condo_w_lot` | `sky_sunstone_valley24` | 17184 / 50038 | No room data. Bundle 50038 is named "Blackspore Swamp House" in group 10001: check. Starting home from 2012-01-26 |
| 26 | Large Wilds House | `hsg_hum_deluxe_01` | `sky_sunstone_valley24` | 2214 / 3438, 50048 | Rooms: 12 surfaces, 4 rooms. **AlchemyDevelopment's verified house.** 750 SC |
| 27 | Small Wilds House | `hsg_hum_economy_01` | `sky_sunstone_valley24` | 2213 / 3437 | Rooms: 8 surfaces, 2 rooms. 549 SC, free to members |
| 28 | Club House | `hsg_hum_sg_night_club_01` | `sky_sunset` | 11140 / 4855 | Rooms: 13 surfaces, 4 rooms (with a basement). 1,499 SC, VIP 5 until May 2013 ([fandom](https://freerealms.fandom.com/wiki/Club_House)) |
| 29 | *unknown* (`NameId` 404) | `hsg_hum_snowhill_01` | `sky_snowhill24` | none | **Zone unverified.** Rooms: 13 surfaces, 6 rooms. Probably the Snowhill Lodge (deed 12117, `NameId` 26345, bundles 5083 and 50062, 950 SC), whose `NameId` differs |
| 30 | Rumbledome Lot | `gw_rumbledome` (a world zone) | `sky_sunstone_valley24` | 15955 / none | **Spawn unverified**; AlchemyDevelopment moved it. Was a TCG Series 5 loot card ([fandom](https://freerealms.fandom.com/wiki/Rumbledome_Lot)) |
| 31 | *unknown* (`NameId` 394) | `hsg_emptylot_merryvale_01` | `sky_merryvale24` | none | **Zone unverified.** Probably the Merry Vale Lot (deed 17186, `NameId` 69968, bundle 4865, 300 SC) |
| 32 | *unknown* (`NameId` 33944) | `hsg_emptylot_laketree_01` | `sky_sunstone_valley24` | none | **Zone unverified.** Probably the Lake Tree Lot (deed 17196, `NameId` 69978, bundle 5239, 500 SC) |
| 33 | Briarwood Farm | none | | 2215 / none | Farming plot, not a house; spawn (1409, 0, 750.8) is outside every housing zone |
| 34 | Wilds Farm | none | | 11435 / none | As 33. Farms had crops, a Farmer's Market and a vending machine ([fandom](https://freerealms.fandom.com/wiki/Farms)) |

**Deeds with no house entry:** Sandy Beach Lot (10385 and 17188, bundle 4603, 400 SC; ImAlko's stub zone
`hsg_emptylot_seaside_beach_01` is probably its zone), Sanctuary Lot (17187), Wilds Rapids Lot (17190), Snowhill
Lodge (12117), Merry Vale Lot (17186), Lake Tree Lot (17196), and three early names never released: Large Snowhill
House (2216), Small and Large Briarwood House (2217, 2218). **Lots the wiki knows that our data doesn't:** Sunstone
Dam (1,499 SC, "more building space than you may know what to do with",
[fandom](https://freerealms.fandom.com/wiki/Sunstone_Dam)), Sunstone Valley Lot (TCG Series 3 card), and three that
never shipped (Bixie Field Lot, announced at SOE Live 2013, icon in the game files; "Girl House" concept art; a golden
yacht teased in January 2014). Our `ClientItemDefinitions.json` has no deed for any of these, so the client we have
may predate them or never carried them; check the string table for "Sunstone Dam" (section 9).

**Ranking: which houses to make enterable first.**

1. **Twelve empty lots whose data is complete and whose deed has a Marketplace bundle:** 1, 3, 4, 5, 6, 9, 10, 16,
   17, 19, 20, 21. Zone file only. Lot 1 needs its spawn clamped into the build area.
2. **Four complete empty lots without a bundle:** 2, 8, 15 and the Apartment (24, which has no yard but is
   enterable). They need a store bundle added to `StoreBundles.json`, or a grant command, before a player can own
   one. The Wilds Condo (25) is in this group if bundle 50038 turns out to be wrong.
3. **The houses with rooms, as "enter and build, wallpaper later":** 26, 27, 28. Entering needs only the zone
   file; wallpaper needs either raisingkaines' model-id table or AlchemyDevelopment's terrain-object data for each,
   and the client `Models.txt` question settled.
4. **The four unverified zones:** 18, 29, 31, 32, and Rumbledome's spawn (30). Each needs one walk-in by Nate to
   confirm the zone loads and the spawn point is on the ground.
5. **Not for players:** 7 (castle, unknown), 23 (green screen), 33 and 34 (farms: a different system; bigmommlors'
   [`farming-prototype`](https://github.com/bigmommlors/Sanctuary/tree/farming-prototype), AGPL-3.0, has a Wilds farm
   instance with persisted obstacles, see the minigames report).

## 4. Plots, neighbourhoods and visiting: how the live game did it

- **Announced (June 2009):** "neighbourhoods will be instanced", "three buildings per personal plot", choose
  "land, size, and style of house" ([Engadget, E3 2009](https://www.engadget.com/2009-06-08-e3-2009-player-housing-coming-to-free-realms.html)).
- **Shipped (December 2009):** three house types, Apartment (free, 1 room, 75 items), Small Wilds House (members,
  2 bedrooms) and Large Wilds House (Marketplace, 3 bedrooms, up to 400 items), each its own instance
  ([MMORPG.com](https://forums.mmorpg.com/discussion/263446/free-realms-major-update-adds-fishing-housing-pets),
  [Engadget](https://www.engadget.com/2009-12-15-free-realms-releases-massive-december-patch.html)). No neighbourhood
  ever appeared; the fandom [Lots](https://freerealms.fandom.com/wiki/Lots) page describes the house icon on the
  bottom panel and a menu of owned lots.
- **Empty lots (August 2011):** the house was removed from new lots so players could build anything, the start of
  the block-building scene; Referee Ruby's tour video (Massively, 2011-08-10, via
  [Engadget](https://www.engadget.com/2011-08-10-massively-exclusive-referee-ruby-shows-off-free-realms-housing.html))
  shows lots and a player-built theme park and how a build is shared.
- **Housing Directory:** members publish a house; others rate it 1 to 5 stars; sorted by rating, views or newest; a
  published house is locked against editing until unpublished; rating and views reset on re-publish; house names are
  filtered ([fandom](https://freerealms.fandom.com/wiki/Housing_Directory)). Our `BaseRatingPacketHandler` and
  `DbHouse` already implement publish, votes, rating and the directory sort.
- **Wilds Condo (January 2012):** became every new player's starting home, "a lawn, more options to decorate, and
  more vertical space in the front yard" ([Engadget](https://www.engadget.com/2012-01-25-free-realms-shows-off-a-new-player-condo.html)).
- **Item limits:** 75 (Apartment) and 400 (Large Wilds House) in the 2009 sources. AlchemyDevelopment carries `MaxFixtures` 100 and `MaxLandmarks` 10 as defaults. Our creative mode has no
  limit, and Nate's playtest measured about 4 MB for the 1,700-item catalog on Decorate, so a per-lot fixture cap
  for performance is worth deciding before launch.
- **Sibling emulator:** oxide's house zone config
  ([`mustafar.yaml`](https://github.com/soir20/oxide/blob/master/config/zones/houses/mustafar/mustafar.yaml)) is one
  template per lot asset (`Housing_EmptyLot_Mustafar`) with a default point of interest; `handlers/housing.rs`
  creates a zone instance per house guid on `EnterRequest`. Same shape as `ZoneManager.TryMovePlayerToZone` with an
  owner id. Nothing to port; it confirms the model.

For the design's **two plots per account** (`docs/design/evergrove-housing.md` section 2) the client needs nothing
new: `HousingPacketInstanceList` already lists several houses per player, and the live game let one character own
many lots. The server change is ownership by account instead of character, which no public fork has done.

## 5. Build items and icons

**Fixtures without items.** Section 1, find 4, has the counts. The groups most worth adding, because they are
ordinary furniture rather than event loot: the lodge set (`hsg_bed_lodge_01`, `hsg_dining_table_lodge_01`,
`hsg_sofa_lodge_01`, `hsg_rug_lodge_01`, `hsg_lodge_chair_01`), the biker set (bed, chair, dining table, floor lamp,
loveseat), the spooky set (chair, clock, coffee table, painting, sofa), the "v2" and coloured block variants (43),
racing props (`hsg_checkpoint_*`, `hsg_teleport_02..04`, `hsg_traps_*`, three colours of bleachers), the beach set
(`hsg_beach_chair_01`, `hsg_boardwalk_01`, `hsg_fishing_boat_tied_01`), savanna trees and cacti, and the 18 Player
Studio pieces (`hsg_playerstudio_*`: gazebo, fireplace, grandfather clock, laptop, gingerbread furniture), which are
exactly the kind of volunteer-made content the project plans. Our generator already covers the lodge, biker, spooky,
cactus and savanna pieces that have a store bundle; the rest have no bundle and so no `NameId`. **What they need:**
an item definition with a model, category and icon, and a name. The name is the blocker: the client shows the
`NameId` string, and nobody has published the string table mapping (the same problem task 23 found for quest text).
The Allakhazam and fandom item pages give the human names, but not the ids.

**Checklist sources.** Allakhazam's [House Items (FR)](https://everquest.allakhazam.com/wiki/Category:House_Items_(FR))
category, 766 items, with a [by type](https://everquest.allakhazam.com/wiki/Category:House_Items_by_Type_(FR))
breakdown and 81 colour groups; the category page shows only part of the list and has no visible pagination, so
the full list needs its `style=checklist3` views or the per-type subcategory pages. The fandom
[Category:Furniture](https://freerealms.fandom.com/wiki/Category:Furniture) (107 pages) and
[Category:Premium Painting](https://freerealms.fandom.com/wiki/Category:Premium_Painting) (30) are reachable through
`api.php` even though the pages block fetchers. A name-level comparison with our item definitions is not possible
from here: our definitions carry an internal `Comment`, not the display name.

**Missing icons.** Every one of the 672 dyeable `hsg_` items has an icon id; the image files are what is missing.
`tools/asset-mirror/README.md` records that OSFR's asset server lacks some housing assets
(`hsg_chair_throne_01.dds.z` in `MISSING.txt`), and Nate's branch `housing/default-tints` works around the dark
silhouettes by defaulting dyeable fixtures to their own colour. **No public source for the missing files was found:**
the Internet Archive has no Free Realms client upload (searched "free realms" and "freerealms" across all media
types; 60 hits are trailers, podcasts, comics and the 2010 official handbook), the former owner's asset server is off
limits, and the one GitLab repo that contains client files
([`padlocks/Free-Realms`](https://gitlab.com/padlocks/Free-Realms), 2018, "Free Realms Reconnected Client", no
licence) is a copy of a client folder, not of the download packs, and is reference only. The fandom wiki hosts item
and lot pictures as uploads (`File:...png`), which could stand in for a few icons at the cost of hand work; they are
SOE's art either way, like everything the asset mirror serves. The question of **which** icons are missing can only be
answered against the client (section 9).

## 6. Findings

| Name | Link | Licence | Last activity | What it holds | State | Porting it | Effort | Value |
|---|---|---|---|---|---|---|---|---|
| AlchemyDevelopment `feature/housing` | [tree](https://github.com/AlchemyDevelopment/Sanctuary/tree/feature/housing), [commit](https://github.com/AlchemyDevelopment/Sanctuary/commit/c301cee) | AGPL-3.0 (LICENSE in branch; forked 2026-08-03, after upstream's licence) | 2026-08-21; merged to its own `main` 2026-08-22; 3 commits ahead, 132 behind upstream | `Houses.json` with `ZoneName`, `Sky`, `GeometryId` for 29 lots, `SurfaceGroups`/`Rooms` for 6 houses; `HOUSING.md` (663 lines of client facts); `tools/gensurfaces.py`, `genrooms.py`, `addmodels.py`; `HouseZone` (1,368 lines), `HousingManager`, `HouseDeeds` (deed to lot by `NameId`), 48 `Models.txt` rows (ids 9000 to 9047), SQLite and MySQL migrations, tests | Author-verified on lot 26; 29 lots "enterable" per author; 5 assignments unverified; debug scaffolding present (`GrantAllHouses`, every surface item granted on entry); written against upstream before the zoning rewrite | **Take the data, not the code:** generate our zone files from its `Houses.json`; read `HOUSING.md` before touching surfaces. Credit Jacob Plunkett | S (zones), M (rooms) | **High** |
| `AlchemyDevelopment` `housing/data` | [branch](https://github.com/AlchemyDevelopment/Sanctuary/tree/housing/data) | AGPL-3.0 | 2026-08-16 | The data commit alone (`Houses.json` +5,098 lines, `Models.txt` +48, `HouseDefinition.cs`, the three tools); its message documents every new field | Same data as above, one commit | Same | S | High |
| Our `ClientItemDefinitions.json` and `StoreBundles.json` | in repo | Client data | | 36 lot deeds (`Type` 16) with `NameId`, icon, `Param1`/`Param2`; 24 bundles for 22 of them (lots in group 206, houses in group 125), all priced 1 | Complete for the client we have; two bundle names don't match their deed (4715, 50038) | Already here; the zone files reference them | — | High |
| Upstream `Houses.json` (EDITz) | [commit 60de008](https://github.com/Open-Source-Free-Realms/Sanctuary/commit/60de008) | AGPL-3.0 | 2024-11-30 | 31 houses: `NameId`, `ZoneId`, icon, spawn, rotation, build areas | Source not stated; AlchemyDevelopment's independent extraction matches 28 of 31 | Already on `main` | — | High |
| raisingkaines `housing-full-archive` and the 12 other `housing-*` branches | [branches](https://github.com/raisingkaines/Sanctuary/branches) | AGPL-3.0 | 2026-08-20 | The system we merged in task 2. The side branches (`housing-ownership-two-homes`, `housing-store-catalog-tints`, `housing-directory-ratings`, …) are the PR-sized slices of the same tree, identical at the tip | Ours. **Nothing newer** | Nothing | — | — |
| Upstream PR #111 (raisingkaines) | [pull/111](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/111) | AGPL-3.0 | Opened 2026-08-15, closed unmerged 2026-09-11 | Head `01bec64` = our import. Labels "publicly tested"; closed as "a massive 16k+ line change" that was "architected by AI"; `#116` (generic zoning) marked as its blocker | Closed | Nothing | — | — |
| Upstream PR #118 (raisingkaines) | [pull/118](https://github.com/Open-Source-Free-Realms/Sanctuary/pull/118) | AGPL-3.0 | Opened and closed 2026-08-20 | `housing-editor-runtime`, the editor slice, rebased on ImAlko's zoning prototype | Closed, no description, no review | Nothing | — | — |
| `raisingkaines/FreeRealms-Legacy` | [repo](https://github.com/raisingkaines/FreeRealms-Legacy) | AGPL-3.0 | 2026-09-23 (`main`), 2026-09-14 (`minigame`, a fresh "initial commit") | `main` is our `import/frl-main` exactly; housing is upstream's packet classes plus `Houses.json`, no zones | No housing beyond upstream | Nothing | — | — |
| Upstream `main` housing | [Sanctuary](https://github.com/Open-Source-Free-Realms/Sanctuary) | AGPL-3.0 | 2026-10-11 | Packet classes, `HousingZone` shell and `HousingZoneDefinition` from ImAlko's zoning (PR #116, reverted and restored in PRs #132/#133), PR #58's preview soft-lock fix (2 lines, 2026-07-09) | On our `main` already | — | — | — |
| ImAlko `major-feature/zones-testing` | [Zones](https://github.com/ImAlko/Sanctuary/tree/major-feature/zones-testing/src/Resources/Zones) | AGPL-3.0 | 2026-08-30 | `hsg_emptylot_seaside_beach_01.json` (id 2, Shrouded lots' spawn copied, no build area), two Sacred Grove instance zones | Stub | The zone name only | S | Low |
| `soir20/oxide` housing | [handlers/housing.rs](https://github.com/soir20/oxide/blob/master/src/game_server/handlers/housing.rs), [mustafar.yaml](https://github.com/soir20/oxide/blob/master/config/zones/houses/mustafar/mustafar.yaml) | AGPL-3.0 (LICENSE.md) | 2026-10-08 | Clone Wars Adventures: housing packets (320 lines), enter, edit mode, house list, fixture packets; one house template per lot asset; `lookup_house` returns a hard-coded test house | Stub, test data | Design reference only; packet names map one to one onto ours | — | Low |
| Fandom wiki, housing | [Lots](https://freerealms.fandom.com/wiki/Lots), [Category:Lots](https://freerealms.fandom.com/wiki/Category:Lots), [Housing Directory](https://freerealms.fandom.com/wiki/Housing_Directory) | CC-BY-SA text, reference only | 2020s edits | 35 lot pages with price, source (Marketplace, TCG card, member gift, event), pictures, dimensions in blocks for the Apartment and Large Wilds House; directory rules | Readable through `api.php` only | Facts for section 3 | — | Medium |
| Allakhazam FR wikibase, housing | [Category:Housing (FR)](https://everquest.allakhazam.com/wiki/Category:Housing_(FR)), [House Items (FR)](https://everquest.allakhazam.com/wiki/Category:House_Items_(FR)) | Site content, reference only | 2009 to 2013 | 24 lot pages with 2011 prices; 766 house items by type, tier and colour; three player building guides ("Home-Made Walls", "Shelf Floors", "Shelf Structures") | Readable | Checklist for missing items | — | Medium |
| Engadget/Massively articles | [E3 2009](https://www.engadget.com/2009-06-08-e3-2009-player-housing-coming-to-free-realms.html), [Dec 2009](https://www.engadget.com/2009-12-15-free-realms-releases-massive-december-patch.html), [Aug 2011](https://www.engadget.com/2011-08-10-massively-exclusive-referee-ruby-shows-off-free-realms-housing.html), [Jan 2012](https://www.engadget.com/2012-01-25-free-realms-shows-off-a-new-player-condo.html) | Press, reference only | 2009 to 2012 | How housing was announced and shipped; the empty-lot change; the Condo | | Section 4 | — | Low |
| Videos named by the wiki (unwatched) | [Large Wilds House Tour](https://www.youtube.com/watch?v=W0Qj_lDBIW4), [out of bounds: Laketree lot, condo, apartment](https://www.youtube.com/watch?v=xlK_LQJmuUQ), [Snow Days Lot present](https://youtu.be/f1Nyf-VPIkA?t=16), [Shrouded Stairs Lot](https://www.youtube.com/watch?v=-GqzT9PDf3I) | Reference only | 2011 to 2014 | Walkthroughs of four lots, for checking spawn points and skies against the real thing | Not viewable from here | — | — | Low |
| Nate's `housing/default-tints` | branch on this repo | AGPL-3.0 | 2026-10 | Dyeable fixtures default to their own colour instead of raw pink; `tools/client-mods/housing-search` | Local, unmerged | — | — | — |
| bigmommlors `farming-prototype` | [branch](https://github.com/bigmommlors/Sanctuary/tree/farming-prototype) | AGPL-3.0 | 2026-09-24 | A Wilds farm instance with persisted obstacles (see the minigames report) | Prototype | For lots 33 and 34, later | M | Low now |

## 7. Recommended first step: open the empty lots

**One PR: zone files for the 16 empty lots whose data is complete (ids 1, 2, 3, 4, 5, 6, 8, 9, 10, 15, 16, 17, 19,
20, 21 and the Apartment, 24), so the Marketplace's lot shelf fills up and every player can pick a seaside, swamp,
snow, cave, mine, island, glade or boat lot.** Players see it immediately: in creative mode lots are free, so the
shelf is the feature.

What it needs:

1. A small generator (Python under `tools/`, run once, output committed) that joins AlchemyDevelopment's
   `Houses.json` (`ZoneName`, `Sky`; credit Jacob Plunkett in the commit) with ours (spawn, rotation, build areas)
   and the deed and bundle ids from section 3, and writes `src/Resources/Zones/hsg_*.json` in our format. Keep ids 14
   and 22; give the new zones ids that can't collide with world zones (`1000 + house id`, say), because
   `ZoneDefinitionCollection` is one table with `FabledRealms` (id 1).
2. `DirectorySnapshot` set to the client image name once Nate reads them out (section 9); until then fall back to the
   lot's own name slug, and expect a blank picture in the directory.
3. Store bundles for the four lots that have none (2, 8, 15, 24): add a bundle entry each to `StoreBundles.json`
   with the deed as `MarketingItemId`, group 206, price 0 under `Housing:FreeLots`. Fix bundle 4715's name.
4. Clamp the arrival position into the first build area (lot 1 spawns 1 m outside its own), as AlchemyDevelopment's
   `ClampToBuildArea` does; otherwise the client refuses every placement there.
5. Tests: every `Houses.json` entry with a zone file agrees on `NameId`, icon and spawn; every zone file's
   `StoreBundleId` exists and its bundle sells the zone's `ItemDefinitionId`; no two zone definitions share an id.
6. Leave `HousingSurfaceCatalog` alone: empty lots have no walls. The houses with rooms (26, 27, 28) are the second
   PR, after the `Models.txt` question is answered.

**How Nate tests it in game:** open the Marketplace's lot shelf and count 18 lots with icons and names; buy three
(a free lot each); the house icon lists them; enter each: the right scenery and sky load, the player stands on the
ground at the spawn point, Decorate opens, a fixture places at the edge of the build area and is refused outside it,
relog returns to the same lot with the fixture in place, a second account visits through the directory. Then walk the
four unverified lots (18, 29, 31, 32) and Rumbledome (30) with a debug command and report which load.

## 8. Dead ends

- **raisingkaines' public forks:** every `housing-*` branch of `raisingkaines/Sanctuary` is a slice of our import
  (`git diff` of `housing-store-catalog-tints` against `housing-full-archive` is empty); `guild`,
  `shop-selling-item-fixes` and `fix/afk-disconnection` are unrelated; `trading-card-game` is the minigame
  experiment. `FreeRealms-Legacy` `main` equals `import/frl-main`.
- **The other 21 forks of upstream:** branch lists checked for all 24 forks on the
  [forks page](https://github.com/Open-Source-Free-Realms/Sanctuary/forks) (the task said 39; GitHub lists 24).
  None but AlchemyDevelopment, raisingkaines and ImAlko has a housing branch or a housing zone file.
  `DeanSawyer/Free-Realms-New-Dawn` (2026-10-11) is upstream plus a README; `Sulphural/Sanctuary-fork`,
  `StimpyDev`, `Ehren1337`, `SengokuNadeko`, `GroaxyVRC/fresh-sanctuary` and `datah4zard/resources` carry only
  upstream's housing files. `claytonsulby/docker-freerealms` is packaging.
- **Upstream PRs:** of 128 PR heads, only #58 (preview soft-lock, merged), #111 and #118 (raisingkaines, closed)
  touch housing. #116/#132/#133 are the zoning rewrite.
- **GitLab:** `padlocks/LibSOE` (SOE transport library, C#, 2018), `padlocks/Free-Realms` (a client folder and
  unfinished server from "FreeRealms Reconnected", 2018) and `munchiem/project-fr` (a README). No licence on any;
  no housing. No Sanctuary fork on GitLab.
- **Free Realms Sunrise / FR Sunrise:** a closed emulator project (2020 to 2021) whose progress video showed housing
  lots ([Massively OP](https://massivelyop.com/2020/03/31/free-realms-sunrise-rogue-server-readies-beta-adds-housing/),
  unreadable from here, summary via search). No public code found.
- **Wayback Machine:** `freerealms.com` housing pages are not in the index at the URLs tried (`/housing`,
  `/game/housing`, `/news`, `/game/features`); the SOE forums aren't either; the CDX search API and
  `web.archive.org/web/` fetches are blocked from this container (egress policy, connection reset), so the archived
  freerealmsinsider.com snapshot (2013-01-16) and the fandom pages' Wayback copies could not be read.
- **Fandom wiki pages** return 402 to fetchers; the MediaWiki `api.php` works and was used instead.
- **Internet Archive:** no client, asset pack or icon upload under "free realms" or "freerealms"; the
  [official handbook](https://archive.org/details/freerealmsoffi00trac) (2010) predates lots.
- **h1emu / PlanetSide 2:** H1Z1's base building is a construction system, not fixture housing; nothing on housing
  packets turned up, and the shared packet families are transport-level. Not pursued further.
- **Icon files:** no public mirror of the missing housing icons (section 5).
- **Zone asset names on the web:** no page anywhere mentions `hsg_emptylot_*`; AlchemyDevelopment's list is the
  only one.

## 9. Check in the client

1. **Zone assets.** List the `hsg_*.gzne` (and `gw_rumbledome`) zone names in the client packs and the asset
   manifest, and tick them against section 3's `ZoneName` column. This settles the four unverified lots (18, 29,
   31, 32) and the castle (7).
2. **Strings.** What do string ids 510215 (lot 7), 441205 (18), 426811 (23), 404 (29), 394 (31) and 33944 (32) say?
   Does any string read "Sunstone Dam", "Sunstone Valley Lot", "Sandy Beach Lot" or "Bixie Field Lot"? That decides
   whether the client we ship even knows those lots.
3. **Directory snapshots.** The two zone files name `seaside_lot` and `blacksporeswamphouse_lot`. Find the UI images
   with those names and list their siblings, to fill `DirectorySnapshot` for the other lots.
4. **`Models.txt`.** Does the client's `Resources/Models.txt` contain ids 5000 to 5008 (raisingkaines' Blackspore
   meshes)? Did wallpaper actually change in the 2026-10-07 playtest? If the stock file lacks them, every wallpaper
   plan needs a pinned client file change, and AlchemyDevelopment's 48 rows (ids 9000 to 9047) are the alternative.
5. **Build areas.** Does the client refuse placement outside the build area, and does the lot-1 spawn point stand
   outside it as both data sets say? Also, do the `W` components of our build areas stay 0 (the client compares all
   four lanes)?
6. **Missing icons.** With the asset mirror's `MISSING.txt` in hand, which of the dyeable fixtures' icon ids resolve
   to a file in the packs and which don't? The list of missing `.dds` names is the only way to search for a source.
7. **Store shelf.** Does the Marketplace lot shelf show bundle 4715 as "Shrouded Stairs Lot" and sell the Gloam lot,
   and bundle 50038 as "Blackspore Swamp House" and sell the Wilds Condo?
8. **Fixture names.** For a model with no item (say `hsg_bed_lodge_01` without its bundle, or
   `hsg_playerstudio_gazebo_01`), what does the client show when the server sends an item definition with
   `NameId` 0? If it shows blank rather than crashing, the missing fixtures can ship unnamed first.
