#!/usr/bin/env python3
"""
Writes the zone files for the empty lots (and the Apartment) that task 26 opens, under src/Resources/Zones/, and
points their src/Resources/Houses.json entries at those zones. Run once from the repository root; the output is
committed. Rerunning it rewrites the same files.

Where each value comes from:
  ZoneName, Sky              AlchemyDevelopment/Sanctuary, branch feature/housing, commit c301cee (Jacob Plunkett,
                             AGPL-3.0), src/Resources/Houses.json. Copied into LOTS below, so this runs offline.
  NameId, icon, spawn,       our src/Resources/Houses.json (equal to AlchemyDevelopment's for these lots).
  rotation, build areas
  deed, store bundle         our ClientItemDefinitions.json and StoreBundles.json; see docs/research/housing-scout.md
                             section 3. Bundles 900202, 900208, 900215 and 900224 are ours, added by task 26.
  DirectorySnapshot          the client's <name>_lot.dds UI images, read out in housing-scout.md section 10.

Zone ids: zone definitions share one table with world zones (FabledRealms is 1), so a lot's zone id is 1000 plus
its Houses.json id. The two lots that were open before (14 Seaside, 22 Blackspore Swamp House) keep 14 and 22,
because players' Houses rows point at them.

Usage: tools/housing-lots/generate-lot-zones.py
"""

import json
import math
import os
import re
import sys

ZONE_ID_OFFSET = 1000

# house id: (zone name, sky, display name, /house command, deed item, store bundle, directory snapshot)
LOTS = {
    1: ("hsg_emptylot_blackspore_01", "sky_blackspore24.xml", "Blackspore Swamp Lot", "swamplot", 17567, 4666,
        "blackspore_lot"),
    # yacht_lot exists in the client but may be a different boat, so this one is left blank until it's checked.
    2: ("hsg_emptylot_boat_01", "sky_seaside24.xml", "Sunset Party Boat", "boat", 79149, 900202, ""),
    3: ("hsg_emptylot_bog_shore_01", "sky_housing_wilds.xml", "Bog Shore Lot", "bogshore", 17192, 4701,
        "bogshore_lot"),
    4: ("hsg_emptylot_briarfalls_01", "sky_briarwood24.xml", "Briar Falls Lot", "briarfalls", 17193, 4846,
        "briarfalls_lot"),
    5: ("hsg_emptylot_briarwood_01", "sky_briarwood24.xml", "Briarwood Lot", "briarwood", 7485, 4295,
        "briarwood_lot"),
    6: ("hsg_emptylot_bw_towers_01", "sky_briarwood24.xml", "Briarwall Towers Lot", "towers", 17189, 4978,
        "briarwoodtowers_lot"),
    8: ("hsg_emptylot_gl_staircase_01", "sky_bristlewood_glade.xml", "Shrouded Stairs Lot", "stairs", 10338, 900208,
        "shroudedgladestairs_lot"),
    9: ("hsg_emptylot_gl_valley_01", "sky_hidden_valley24.xml", "Shrouded Glade Lot", "glade", 17182, 4633,
        "shroudedglade_lot"),
    10: ("hsg_emptylot_island_01", "sky_seaside24.xml", "Lonely Island Lot", "island", 10386, 4596,
         "lonelyisland_lot"),
    15: ("hsg_emptylot_sh_batcave_01", "sky_bat_cave.xml", "Bat Cavern Lot", "batcave", 17195, 900215,
         "snowhillbatcave_lot"),
    16: ("hsg_emptylot_sh_crystalmines_01", "sky_deep_mines.xml", "Crystal Mines Lot", "mines", 17191, 4786,
         "crystalmines_lot"),
    17: ("hsg_emptylot_shrouded_gloam_01", "sky_shrouded_gloam.xml", "Shrouded Gloam Lot", "gloam", 17183, 4715,
         "shroudedgloam_lot"),
    19: ("hsg_emptylot_snowhill_01", "sky_snowhill_housinglot_01.xml", "Snowhill Lot", "snowhill", 7486, 4296,
         "snowhill_lot"),
    20: ("hsg_emptylot_vale_steam_01", "sky_merryvale24.xml", "Vale Stream Lot", "valestream", 17194, 4885,
         "valestream_lot"),
    21: ("hsg_emptylot_wugachug_01", "sky_wugachug24.xml", "Wugachug Lot", "wugachug", 17185, 4634,
         "wugachug_lot"),
    24: ("hsg_hum_condo", "sky_sunstone_valley24.xml", "Apartment", "apartment", 10337, 900224, "condo_lot"),
}

TILE_SIZE = 64
# The two zone files that already worked use a 12 by 12 tile grid; grow it only if a lot reaches past it.
MIN_TILES = 12


def load_json(path):
    with open(path, encoding="utf-8-sig") as file:
        return json.load(file)


def number(value):
    return json.dumps(value)


def vector(values):
    return "[" + ", ".join(number(value) for value in values) + "]"


def tiles_to_cover(house):
    coordinates = [house["SpawnPosition"][0], house["SpawnPosition"][2]]
    for area in house["BuildAreas"]:
        coordinates += [area["Min"][0], area["Max"][0], area["Min"][2], area["Max"][2]]

    if min(coordinates) < 0:
        sys.exit(f"House {house['Id']} has a negative coordinate; the grid here starts at 0.")

    return max(MIN_TILES, math.ceil(max(coordinates) / TILE_SIZE) + 1)


def zone_file(house_id, house):
    zone_name, sky, display_name, command, deed_id, bundle_id, snapshot = LOTS[house_id]
    tiles = tiles_to_cover(house)
    spawn = house["SpawnPosition"][:3]
    # Houses.json stores the heading as [x, z, 0, 0]; a zone file stores [x, z].
    rotation = house["SpawnRotation"][:2]

    build_areas = ",\n".join(
        "    {\n"
        f"      \"Min\": {vector(area['Min'][:3] + [1])},\n"
        f"      \"Max\": {vector(area['Max'][:3] + [1])}\n"
        "    }"
        for area in house["BuildAreas"])

    return (
        "{\n"
        "  \"$type\": \"Housing\",\n"
        f"  \"Id\": {ZONE_ID_OFFSET + house_id},\n"
        f"  \"Name\": {json.dumps(zone_name)},\n"
        f"  \"DisplayName\": {json.dumps(display_name)},\n"
        f"  \"CommandName\": {json.dumps(command)},\n"
        f"  \"NameId\": {house['NameId']},\n"
        f"  \"IconId\": {house['Icon']},\n"
        f"  \"StoreBundleId\": {bundle_id},\n"
        f"  \"ItemDefinitionId\": {deed_id},\n"
        f"  \"DirectorySnapshot\": {json.dumps(snapshot)},\n"
        f"  \"TileSize\": {TILE_SIZE},\n"
        "  \"StartLatitude\": 0,\n"
        "  \"StartLongitude\": 0,\n"
        f"  \"EndLatitude\": {tiles},\n"
        f"  \"EndLongitude\": {tiles},\n"
        f"  \"Sky\": {json.dumps(sky)},\n"
        "  \"SpawnPosition\": [\n"
        + ",\n".join(f"    {number(value)}" for value in spawn) + "\n"
        "  ],\n"
        "  \"SpawnRotation\": [\n"
        + ",\n".join(f"    {number(value)}" for value in rotation) + "\n"
        "  ],\n"
        "  \"BuildAreas\": [\n"
        f"{build_areas}\n"
        "  ]\n"
        "}\n")


def main():
    resources = os.path.join("src", "Resources")
    houses_path = os.path.join(resources, "Houses.json")

    if not os.path.isfile(houses_path):
        sys.exit("Run this from the repository root.")

    houses = {house["Id"]: house for house in load_json(houses_path)}
    items = {item["Id"]: item for item in load_json(os.path.join(resources, "ClientItemDefinitions.json"))}

    for house_id, lot in sorted(LOTS.items()):
        house = houses[house_id]
        deed = items.get(lot[4])

        if deed is None or deed.get("Type") != 16:
            sys.exit(f"Lot {house_id}: item {lot[4]} is not a deed.")
        if deed["NameId"] != house["NameId"]:
            sys.exit(f"Lot {house_id}: deed {lot[4]} is named {deed['NameId']}, Houses.json says {house['NameId']}.")
        if not house["BuildAreas"]:
            sys.exit(f"Lot {house_id} has no build area.")

        path = os.path.join(resources, "Zones", lot[0] + ".json")
        with open(path, "w", encoding="utf-8", newline="\n") as file:
            file.write(zone_file(house_id, house))
        print(f"{path}: zone {ZONE_ID_OFFSET + house_id}, bundle {lot[5]}")

    # Point each lot's Houses.json entry at its new zone id, editing the text so the file's layout stays as it is.
    with open(houses_path, encoding="utf-8") as file:
        text = file.read()

    for house_id in LOTS:
        pattern = re.compile(r'("Id": ' + str(house_id) + r',\s*"NameId": \d+,\s*"ZoneId": )\d+')
        text, count = pattern.subn(lambda match: match.group(1) + str(ZONE_ID_OFFSET + house_id), text)
        if count != 1:
            sys.exit(f"Couldn't find house {house_id}'s ZoneId in Houses.json.")

    with open(houses_path, "w", encoding="utf-8", newline="\n") as file:
        file.write(text)


if __name__ == "__main__":
    main()
