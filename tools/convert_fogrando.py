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


def derive_zone_type(area: dict) -> str | None:
    """
    Derive zone type from area data.

    Returns None if zone should be excluded.

    Type derivation rules:
    - Tag contains 'legacy' -> legacy_dungeon
    - Tag contains 'start' -> start
    - Map starts with m30 -> catacomb
    - Map starts with m31 -> cave
    - Map starts with m32 -> tunnel
    - Map starts with m39 -> gaol
    - Has DefeatFlag and not minidungeon -> boss_arena
    - Tag contains 'minidungeon' -> mini_dungeon
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

    # Check for start tag
    if "start" in tags_lower:
        return "start"

    # Check for legacy dungeon tag
    if "legacy" in tags_lower:
        return "legacy_dungeon"

    # Get the primary map
    primary_map = get_primary_map(area)
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


def convert_area_to_zone(area: dict) -> Zone | None:
    """Convert a FogRando area to a SpeedFog zone."""
    name = area.get("Name", "")
    if not name:
        return None

    zone_type = derive_zone_type(area)
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
        fog_count=0,  # To be filled manually
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
        "# fog_count: number of fog gates (fill manually)",
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

    # Convert areas to zones
    zones: list[Zone] = []
    excluded_count = 0

    for area in areas:
        zone = convert_area_to_zone(area)
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
