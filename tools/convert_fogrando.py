#!/usr/bin/env python3
"""
Convert FogRando fog.txt to SpeedFog zones.toml

This script extracts zone data from FogRando's fog.txt (YAML format) and converts
it to SpeedFog's zones.toml format. It excludes overworld, trivial, and DLC zones,
as well as certain problematic areas (sewers, ainsel, deeproot, etc.).

Usage:
    python convert_fogrando.py <fog.txt> <output.toml>
    python convert_fogrando.py reference/fogrando-data/fog.txt core/zones.toml
"""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
from pathlib import Path

import yaml


@dataclass
class Zone:
    """Intermediate zone representation."""

    id: str
    map: str
    name: str
    type: str
    weight: int
    fog_count: int
    boss: str
    tags: list[str] = field(default_factory=list)


# Tags that cause exclusion
EXCLUDE_TAGS = {"overworld", "trivial", "dlc"}

# Name patterns that cause exclusion
EXCLUDE_NAME_PATTERNS = [
    "ainsel",
    "deeproot",
    "lakeofrot",
    "lake_of_rot",
    "shunning",
    "sewer",
]

# Areas that are overworld even when connected by legacy entrances
# (these appear in legacy-tagged entrances but are not legacy dungeons themselves)
OVERWORLD_AREAS = {
    "liurnia",
    "gelmir",
    "gravesite",
    "scadualtus",
    "scadualtus_lower",
    "cerulean",
    "rauhruins_east",
}

# Map prefixes for legacy dungeons (excluding overworld areas on the same map)
# These are used to identify zones within legacy dungeons
LEGACY_MAP_PREFIXES = [
    "m10_00",  # Stormveil Castle
    "m11_00",  # Leyndell, Royal Capital
    "m11_05",  # Leyndell, Ashen Capital
    "m13_00",  # Crumbling Farum Azula
    "m14_00",  # Academy of Raya Lucaria
    "m15_00",  # Haligtree
    "m16_00",  # Volcano Manor
]

# Area names that are on legacy dungeon maps but are actually overworld
LEGACY_MAP_OVERWORLD_EXCEPTIONS = {
    "stormhill",  # m10_00 but overworld
}

# Zone type output order
ZONE_TYPE_ORDER = [
    "start",
    "legacy_dungeon",
    "boss_arena",
    "catacomb",
    "cave",
    "tunnel",
    "gaol",
    "mini_dungeon",
]


def load_fog_txt(path: Path) -> dict:
    """Load and parse FogRando's fog.txt YAML file."""
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def parse_entrances(data: dict) -> tuple[set[str], dict[str, int]]:
    """
    Parse Entrances section to extract legacy zones and fog counts.

    Returns:
        legacy_zones: Set of area names connected by legacy-tagged entrances
        fog_counts: Dict mapping area name to number of fog gates (entrances)
    """
    legacy_zones: set[str] = set()
    fog_counts: dict[str, int] = {}

    entrances = data.get("Entrances", [])

    for entrance in entrances:
        # Skip unused entrances
        tags = entrance.get("Tags", "")
        tags_list = tags.split() if isinstance(tags, str) else tags or []
        tags_lower = [t.lower() for t in tags_list]

        if "unused" in tags_lower or "remove" in tags_lower:
            continue

        # Get connected areas
        a_side = entrance.get("ASide", {})
        b_side = entrance.get("BSide", {})
        a_area = a_side.get("Area", "")
        b_area = b_side.get("Area", "")

        # Count fog gates for each area
        if a_area:
            fog_counts[a_area] = fog_counts.get(a_area, 0) + 1
        if b_area:
            fog_counts[b_area] = fog_counts.get(b_area, 0) + 1

        # Collect legacy zones (excluding known overworld areas)
        if "legacy" in tags_lower:
            if a_area and a_area not in OVERWORLD_AREAS:
                legacy_zones.add(a_area)
            if b_area and b_area not in OVERWORLD_AREAS:
                legacy_zones.add(b_area)

    return legacy_zones, fog_counts


def should_exclude_by_name(name: str) -> bool:
    """Check if zone should be excluded based on name pattern."""
    name_lower = name.lower()
    return any(pattern in name_lower for pattern in EXCLUDE_NAME_PATTERNS)


def should_exclude_by_tags(tags: list[str]) -> bool:
    """Check if zone should be excluded based on tags."""
    if not tags:
        return False
    tag_set = {t.lower() for t in tags}
    return bool(tag_set & EXCLUDE_TAGS)


def get_primary_map(area: dict) -> str:
    """Get the primary map ID from area data."""
    maps = area.get("Maps", "")
    if not maps:
        return ""
    # Take the first map
    return maps.split()[0]


def derive_zone_type(area: dict, legacy_zones: set[str]) -> str | None:
    """
    Derive zone type from area data.

    Returns None if zone should be excluded.

    Type derivation rules (in priority order):
    1. Tag contains 'start' -> start
    2. Area name in legacy_zones (from Entrances) OR map prefix is legacy -> legacy_dungeon
    3. Map starts with m30 -> catacomb
    4. Map starts with m31 -> cave
    5. Map starts with m32 -> tunnel
    6. Map starts with m39 -> gaol
    7. Has DefeatFlag and not minidungeon -> boss_arena
    8. Tag contains 'minidungeon' -> mini_dungeon
    """
    name = area.get("Name", "")
    tags = area.get("Tags", "")
    tags_list = tags.split() if isinstance(tags, str) else tags or []
    tags_lower = [t.lower() for t in tags_list]

    # Check exclusions
    if should_exclude_by_name(name):
        return None
    if should_exclude_by_tags(tags_list):
        return None

    # Check for start tag (highest priority)
    if "start" in tags_lower:
        return "start"

    # Get the primary map early - needed for legacy check
    primary_map = get_primary_map(area)

    # Check if area is identified as legacy dungeon:
    # 1. From Entrances section (legacy_zones set)
    # 2. From map prefix (LEGACY_MAP_PREFIXES)
    is_legacy_from_entrances = name in legacy_zones
    is_legacy_from_map = (
        primary_map
        and any(primary_map.startswith(prefix) for prefix in LEGACY_MAP_PREFIXES)
        and name not in LEGACY_MAP_OVERWORLD_EXCEPTIONS
    )

    if is_legacy_from_entrances or is_legacy_from_map:
        return "legacy_dungeon"

    if not primary_map:
        return None

    # Check map prefixes for minidungeon types
    if primary_map.startswith("m30"):
        return "catacomb"
    if primary_map.startswith("m31"):
        return "cave"
    if primary_map.startswith("m32"):
        return "tunnel"
    if primary_map.startswith("m39"):
        return "gaol"

    # Check for boss arena (has DefeatFlag and not minidungeon)
    has_defeat_flag = "DefeatFlag" in area
    is_minidungeon = "minidungeon" in tags_lower

    if has_defeat_flag and not is_minidungeon:
        return "boss_arena"

    # Check for minidungeon tag
    if is_minidungeon:
        return "mini_dungeon"

    # No type match - exclude
    return None


def extract_boss_name(area: dict) -> str:
    """
    Extract boss name from DebugInfo if present.

    Format: "DefeatFlag: 12345 - m10_00_00_00 - Boss Name"
    """
    debug_info = area.get("DebugInfo", "")
    if not debug_info:
        return ""

    # Parse format: "DefeatFlag: 12345 - m10_00_00_00 - Boss Name"
    parts = debug_info.split(" - ")
    if len(parts) >= 3:
        return parts[2].strip()
    return ""


def convert_area_to_zone(
    area: dict, legacy_zones: set[str], fog_counts: dict[str, int]
) -> Zone | None:
    """Convert a FogRando area to a SpeedFog zone."""
    name = area.get("Name", "")
    if not name:
        return None

    zone_type = derive_zone_type(area, legacy_zones)
    if zone_type is None:
        return None

    primary_map = get_primary_map(area)
    text = area.get("Text", "")
    boss = extract_boss_name(area)
    tags_raw = area.get("Tags", "")
    tags = tags_raw.split() if isinstance(tags_raw, str) else tags_raw or []

    return Zone(
        id=name,
        map=primary_map,
        name=text,
        type=zone_type,
        weight=0,  # To be filled manually
        fog_count=fog_counts.get(name, 0),
        boss=boss,
        tags=tags,
    )


def escape_toml_string(s: str) -> str:
    """Escape a string for TOML output."""
    # Use double quotes and escape backslashes and quotes
    s = s.replace("\\", "\\\\")
    s = s.replace('"', '\\"')
    return f'"{s}"'


def zones_to_toml(zones: list[Zone]) -> str:
    """
    Convert zones to TOML format string.

    Groups by type, outputs in order: start, legacy_dungeon, boss_arena,
    catacomb, cave, tunnel, gaol, mini_dungeon
    """
    lines = [
        "# SpeedFog Zone Definitions",
        "# Generated from FogRando fog.txt",
        "#",
        "# weight: approximate duration in minutes (fill manually)",
        "# fog_count: number of fog gates (calculated from Entrances)",
        "",
    ]

    # Group zones by type
    zones_by_type: dict[str, list[Zone]] = {}
    for zone in zones:
        if zone.type not in zones_by_type:
            zones_by_type[zone.type] = []
        zones_by_type[zone.type].append(zone)

    # Output in specified order
    for zone_type in ZONE_TYPE_ORDER:
        if zone_type not in zones_by_type:
            continue

        type_zones = zones_by_type[zone_type]
        lines.append(f"# {zone_type.upper().replace('_', ' ')} ({len(type_zones)} zones)")
        lines.append("")

        for zone in type_zones:
            lines.append(f"[[zone]]")
            lines.append(f"id = {escape_toml_string(zone.id)}")
            lines.append(f"map = {escape_toml_string(zone.map)}")
            lines.append(f"name = {escape_toml_string(zone.name)}")
            lines.append(f"type = {escape_toml_string(zone.type)}")
            lines.append(f"weight = {zone.weight}")
            lines.append(f"fog_count = {zone.fog_count}")
            if zone.boss:
                lines.append(f"boss = {escape_toml_string(zone.boss)}")
            lines.append("")

    return "\n".join(lines)


def main() -> int:
    """Main entry point."""
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <fog.txt> <output.toml>", file=sys.stderr)
        print(f"Example: {sys.argv[0]} reference/fogrando-data/fog.txt core/zones.toml", file=sys.stderr)
        return 1

    input_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    if not input_path.exists():
        print(f"Error: Input file not found: {input_path}", file=sys.stderr)
        return 1

    # Load fog.txt
    print(f"Loading {input_path}...")
    try:
        data = load_fog_txt(input_path)
    except yaml.YAMLError as e:
        print(f"Error parsing YAML: {e}", file=sys.stderr)
        return 1

    # Get areas
    areas = data.get("Areas", [])
    if not areas:
        print("Error: No Areas found in fog.txt", file=sys.stderr)
        return 1

    print(f"Found {len(areas)} total areas")

    # Parse Entrances to get legacy zones and fog counts
    legacy_zones, fog_counts = parse_entrances(data)
    print(f"Found {len(legacy_zones)} legacy dungeon areas from Entrances")
    print(f"Found fog counts for {len(fog_counts)} areas")

    # Convert areas to zones
    zones: list[Zone] = []
    excluded_count = 0

    for area in areas:
        zone = convert_area_to_zone(area, legacy_zones, fog_counts)
        if zone is not None:
            zones.append(zone)
        else:
            excluded_count += 1

    print(f"Converted {len(zones)} zones, excluded {excluded_count} areas")

    # Generate TOML
    toml_content = zones_to_toml(zones)

    # Write output
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(toml_content)

    print(f"Wrote {output_path}")

    # Print summary by type
    print("\nZone summary by type:")
    zones_by_type: dict[str, int] = {}
    for zone in zones:
        zones_by_type[zone.type] = zones_by_type.get(zone.type, 0) + 1

    for zone_type in ZONE_TYPE_ORDER:
        count = zones_by_type.get(zone_type, 0)
        if count > 0:
            print(f"  {zone_type}: {count}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
