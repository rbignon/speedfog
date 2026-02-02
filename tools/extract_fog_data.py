#!/usr/bin/env python3
"""
Extract fog gate metadata from FogRando's fog.txt for the C# writer.

This script extracts fog gate information needed by the Phase 3 C# writer.
Position data is NOT extracted here (positions are resolved at runtime from MSB files).

## Key Behavior: Fog ID Keys

Some fog names (e.g., "AEG099_002_9000") appear in multiple maps. This script handles
duplication by:
1. First occurrence uses the plain name as key: "AEG099_002_9000"
2. Subsequent occurrences use map-prefixed key: "m10_00_00_00_AEG099_002_9000"

At runtime, the C# writer should:
1. Try the plain fog_id first
2. If the zone doesn't match, try map-prefixed key (using the zone's map)

This matches how generate_clusters.py uses fog names - the zone context disambiguates.

## Output format (fog_data.json):

{
    "version": "1.0",
    "fogs": {
        "AEG099_002_9000": {
            "type": "entrance",
            "zone": "stormveil",
            "map": "m10_00_00_00",
            "entity_id": 10001800,
            "model": "AEG099_002",
            "asset_name": "AEG099_002_9000",
            "lookup_by": "name",
            "position": null,
            "rotation": null
        },
        "m14_00_00_00_AEG099_002_9000": {
            "type": "entrance",
            "zone": "academy",
            ...
        },
        "1034471610": {
            "type": "warp",
            "zone": "liurnia",
            "map": "m60_34_47_00",
            "entity_id": 1034471610,
            "model": "AEG099_510",
            "asset_name": "AEG099_510_9000",
            "lookup_by": "entity_id",
            "position": null,
            "rotation": null
        }
    }
}

Note: `model` is the model name shared by many assets (e.g., "AEG099_002").
      `asset_name` is the unique instance name in the MSB file (e.g., "AEG099_002_9000").

Usage:
    python extract_fog_data.py fog.txt fog_data.json [--validate-clusters clusters.json]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml

# =============================================================================
# Data Structures
# =============================================================================


@dataclass
class FogEntry:
    """Parsed fog gate entry."""

    fog_id: str  # Name field (unique identifier)
    fog_type: str  # "entrance" or "warp" or "makefrom"
    aside_zone: str  # ASide.Area
    bside_zone: str  # BSide.Area
    map_id: str  # Area field (e.g., "m10_00_00_00")
    entity_id: int  # ID or Location field
    model: str  # Model name (e.g., "AEG099_002")
    asset_name: str  # Full asset name in MSB (e.g., "AEG099_002_9000")
    lookup_by: str | None  # "name" or "entity_id" or None
    position: list[float] | None = None  # [x, y, z] if MakeFrom
    rotation: list[float] | None = None  # [x, y, z] if MakeFrom
    # Height adjustments - FogRando applies entrance-level + side-level (L433-440)
    entrance_adjust_height: float = 0.0  # Entrance.AdjustHeight (applies to all sides)
    aside_adjust_height: float = 0.0  # ASide.AdjustHeight
    bside_adjust_height: float = 0.0  # BSide.AdjustHeight
    # Destination map for one-way warps (BSide.DestinationMap)
    destination_map: str | None = None

    @property
    def zones(self) -> list[str]:
        """All zones this fog connects."""
        zones = []
        if self.aside_zone:
            zones.append(self.aside_zone)
        if self.bside_zone and self.bside_zone != self.aside_zone:
            zones.append(self.bside_zone)
        return zones


# =============================================================================
# Parsing Functions
# =============================================================================


def extract_model_from_name(name: str) -> str:
    """
    Extract model name from fog Name field.

    Examples:
        "AEG099_002_9000" -> "AEG099_002"
        "AEG099_230_9001" -> "AEG099_230"
        "1034471610" -> ""  (numeric names need DebugInfo)
    """
    # Named fogs have format "AEG099_XXX_9YYY"
    match = re.match(r"^(AEG\d{3}_\d{3})_\d+$", name)
    if match:
        return match.group(1)
    return ""


def extract_from_debug_info(debug_info: str | list[str]) -> tuple[str, str]:
    """
    Extract model name and full asset name from DebugInfo field.

    Examples:
        "asset 10001800 (m10_00_00_00 (Stormveil Castle) AEG099_002_9000)"
        -> ("AEG099_002", "AEG099_002_9000")

        "asset 1034471610 (m60_34_47_00 (...) AEG099_510_9000)"
        -> ("AEG099_510", "AEG099_510_9000")

    Returns:
        Tuple of (model, asset_name). Both may be empty strings if not found.
    """
    if isinstance(debug_info, list):
        debug_info = debug_info[0] if debug_info else ""

    if not debug_info:
        return "", ""

    # Look for full AEG pattern with suffix (e.g., AEG099_002_9000)
    match = re.search(r"\b(AEG\d{3}_\d{3})_(\d+)\b", debug_info)
    if match:
        model = match.group(1)
        asset_name = f"{match.group(1)}_{match.group(2)}"
        return model, asset_name

    # Also check for AEG pattern without suffix
    match = re.search(r"\b(AEG\d{3}_\d{3})\b", debug_info)
    if match:
        return match.group(1), ""

    return "", ""


def parse_makefrom(makefrom: str) -> tuple[str, list[float], list[float]]:
    """
    Parse MakeFrom field.

    Format: "<model> <base_asset> <x> <y> <z> <rot_y> [rot_x] [rot_z]"

    Examples:
        "AEG099_170 AEG027_041_0500 -63.656 51.250 68.100 -90.000"
        -> ("AEG099_170", [-63.656, 51.250, 68.100], [0, -90.0, 0])

        "AEG099_170 AEG441_150_1000 -111.483 207.7 14.804 177.14 -10.306 -2.607"
        -> ("AEG099_170", [-111.483, 207.7, 14.804], [-10.306, 177.14, -2.607])
    """
    parts = makefrom.split()
    if len(parts) < 6:
        raise ValueError(f"Invalid MakeFrom format: {makefrom}")

    model = parts[0]
    # parts[1] is base_asset (ignored)
    x = float(parts[2])
    y = float(parts[3])
    z = float(parts[4])
    rot_y = float(parts[5])

    # Optional rotation components
    rot_x = float(parts[6]) if len(parts) > 6 else 0.0
    rot_z = float(parts[7]) if len(parts) > 7 else 0.0

    return model, [x, y, z], [rot_x, rot_y, rot_z]


def get_zones(fog_data: dict[str, Any]) -> tuple[str, str]:
    """
    Get both zones for a fog gate.

    Returns (aside_zone, bside_zone).
    """
    aside = fog_data.get("ASide", {})
    bside = fog_data.get("BSide", {})
    return aside.get("Area", ""), bside.get("Area", "")


def get_adjust_heights(fog_data: dict[str, Any]) -> tuple[float, float]:
    """
    Get height adjustments for both sides of a fog gate.

    FogRando applies these after calculating the spawn position to account
    for floor level differences at fog gates.

    Returns (aside_adjust_height, bside_adjust_height).
    """
    aside = fog_data.get("ASide", {})
    bside = fog_data.get("BSide", {})
    return float(aside.get("AdjustHeight", 0.0)), float(bside.get("AdjustHeight", 0.0))


def parse_fog_entry(fog_data: dict[str, Any], section: str) -> FogEntry | None:
    """Parse a single fog entry from Entrances or Warps section."""
    name = str(fog_data.get("Name", ""))
    if not name:
        return None

    aside_zone, bside_zone = get_zones(fog_data)
    aside_adjust, bside_adjust = get_adjust_heights(fog_data)
    # Entrance-level AdjustHeight applies to both sides
    entrance_adjust = float(fog_data.get("AdjustHeight", 0.0))

    # Check for MakeFrom (custom position)
    makefrom = fog_data.get("MakeFrom")
    if makefrom:
        model, position, rotation = parse_makefrom(makefrom)
        entity_id = fog_data.get("ID", 0)
        # MakeFrom fogs create new assets, use name as asset_name
        return FogEntry(
            fog_id=name,
            fog_type="makefrom",
            aside_zone=aside_zone,
            bside_zone=bside_zone,
            map_id=fog_data.get("Area", ""),
            entity_id=int(entity_id),
            model=model,
            asset_name=name,  # Full asset name from Name field
            lookup_by=None,  # Position is inline
            position=position,
            rotation=rotation,
            entrance_adjust_height=entrance_adjust,
            aside_adjust_height=aside_adjust,
            bside_adjust_height=bside_adjust,
        )

    # Regular fog - determine lookup method and model
    entity_id = fog_data.get("ID", 0)

    # For warps, use Location if available
    location = fog_data.get("Location")
    if location:
        entity_id = location

    # Extract model and asset_name
    # For named fogs (AEG...), model comes from name, asset_name is the name itself
    # For numeric fogs, both come from DebugInfo
    model = extract_model_from_name(name)
    asset_name = name

    if name.startswith("AEG"):
        lookup_by = "name"
    else:
        lookup_by = "entity_id"
        # For numeric fogs, extract both model and asset_name from DebugInfo
        debug_info = fog_data.get("DebugInfo") or fog_data.get("DebugInfos", [])
        debug_model, debug_asset = extract_from_debug_info(debug_info)
        if debug_model:
            model = debug_model
        if debug_asset:
            asset_name = debug_asset

    fog_type = "entrance" if section == "Entrances" else "warp"

    # Extract destination map for one-way warps (sending gates, etc.)
    bside = fog_data.get("BSide", {})
    destination_map = bside.get("DestinationMap")

    return FogEntry(
        fog_id=name,
        fog_type=fog_type,
        aside_zone=aside_zone,
        bside_zone=bside_zone,
        map_id=fog_data.get("Area", ""),
        entity_id=int(entity_id),
        model=model,
        asset_name=asset_name,  # Full asset name for MSB lookup
        lookup_by=lookup_by,
        position=None,
        rotation=None,
        entrance_adjust_height=entrance_adjust,
        aside_adjust_height=aside_adjust,
        bside_adjust_height=bside_adjust,
        destination_map=destination_map,
    )


def parse_fog_txt(path: Path) -> list[FogEntry]:
    """Parse fog.txt and extract all fog entries."""
    with open(path, encoding="utf-8") as f:
        data = yaml.safe_load(f)

    entries: list[FogEntry] = []

    # Parse Entrances section
    for fog_raw in data.get("Entrances", []):
        entry = parse_fog_entry(fog_raw, "Entrances")
        if entry:
            entries.append(entry)

    # Parse Warps section
    for fog_raw in data.get("Warps", []):
        entry = parse_fog_entry(fog_raw, "Warps")
        if entry:
            entries.append(entry)

    return entries


# =============================================================================
# Validation
# =============================================================================


def validate_against_clusters(fogs: dict[str, dict], clusters_path: Path) -> list[str]:
    """
    Validate that all fog_ids from clusters.json can be resolved in fog_data.

    A fog_id can be resolved if any key (plain or map-prefixed) contains
    the target zone in its zones list.

    Returns list of missing fog_ids.
    """
    with open(clusters_path, encoding="utf-8") as f:
        clusters_data = json.load(f)

    def can_resolve(fog_id: str, zone: str) -> bool:
        """Check if fog_id can be resolved with given zone context."""
        # Find all keys that could match this fog_id
        # (either plain fog_id or any map-prefixed version)
        for key, fog_data in fogs.items():
            # Check if this key is for our fog_id
            if key == fog_id or key.endswith(f"_{fog_id}"):
                zones = fog_data.get("zones", [])
                if not zone or zone in zones:
                    return True
        return False

    missing = []
    clusters = clusters_data.get("clusters", [])

    for cluster in clusters:
        for fog in cluster.get("entry_fogs", []):
            fog_id = fog.get("fog_id", "")
            zone = fog.get("zone", "")
            if fog_id and not can_resolve(fog_id, zone):
                missing.append(f"{fog_id} (zone={zone})")

        for fog in cluster.get("exit_fogs", []):
            fog_id = fog.get("fog_id", "")
            zone = fog.get("zone", "")
            if fog_id and not can_resolve(fog_id, zone):
                missing.append(f"{fog_id} (zone={zone})")

    # Deduplicate while preserving order
    seen = set()
    unique_missing = []
    for item in missing:
        if item not in seen:
            seen.add(item)
            unique_missing.append(item)

    return unique_missing


# =============================================================================
# Output
# =============================================================================


def entries_to_json(entries: list[FogEntry]) -> dict[str, Any]:
    """
    Convert fog entries to JSON-serializable format.

    Handles duplicate fog names by using map-prefixed keys for subsequent entries.
    """
    fogs: dict[str, dict] = {}
    # Track which names have been seen
    seen_names: dict[str, str] = {}  # name -> first map that used it
    duplicate_count = 0

    for entry in entries:
        # Total height adjustment = entrance-level + side-level
        # FogRando applies both sequentially (GameDataWriterE.cs L433-440)
        aside_total = entry.entrance_adjust_height + entry.aside_adjust_height
        bside_total = entry.entrance_adjust_height + entry.bside_adjust_height

        fog_data = {
            "type": entry.fog_type,
            "zones": entry.zones,  # List of zones (both aside and bside)
            "map": entry.map_id,
            "entity_id": entry.entity_id,
            "model": entry.model,
            "asset_name": entry.asset_name,  # Full asset name for MSB lookup
            "lookup_by": entry.lookup_by,
            "position": entry.position,
            "rotation": entry.rotation,
            # Total height adjustments per side (entrance + side level)
            # Index matches zones: [0] = ASide, [1] = BSide
            "adjust_heights": [aside_total, bside_total],
            # Destination map for one-way warps (BSide.DestinationMap)
            "destination_map": entry.destination_map,
        }

        if entry.fog_id in seen_names:
            # Duplicate name - use map-prefixed key
            key = f"{entry.map_id}_{entry.fog_id}"
            duplicate_count += 1
        else:
            # First occurrence - use plain name
            key = entry.fog_id
            seen_names[entry.fog_id] = entry.map_id

        # Also add map-prefixed version for direct lookup
        # This allows C# to look up either way
        map_key = f"{entry.map_id}_{entry.fog_id}"

        fogs[key] = fog_data
        if key != map_key:
            fogs[map_key] = fog_data

    return {
        "version": "1.0",
        "duplicate_names_handled": duplicate_count,
        "fogs": fogs,
    }


# =============================================================================
# CLI
# =============================================================================


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Extract fog gate metadata from fog.txt for C# writer",
    )
    parser.add_argument(
        "fog_txt",
        type=Path,
        help="Path to fog.txt input file",
    )
    parser.add_argument(
        "output",
        type=Path,
        help="Path to output fog_data.json",
    )
    parser.add_argument(
        "--validate-clusters",
        type=Path,
        default=None,
        help="Path to clusters.json for validation",
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Verbose output",
    )

    args = parser.parse_args()

    if not args.fog_txt.exists():
        print(f"Error: Input file not found: {args.fog_txt}", file=sys.stderr)
        return 1

    # Parse fog.txt
    print(f"Loading {args.fog_txt}...")
    try:
        entries = parse_fog_txt(args.fog_txt)
    except yaml.YAMLError as e:
        print(f"Error parsing YAML: {e}", file=sys.stderr)
        return 1

    print(f"Parsed {len(entries)} fog entries")

    # Count by type
    by_type: dict[str, int] = {}
    for entry in entries:
        by_type[entry.fog_type] = by_type.get(entry.fog_type, 0) + 1

    for fog_type, count in sorted(by_type.items()):
        print(f"  {fog_type}: {count}")

    # Count entries with empty zones
    empty_zones = sum(1 for e in entries if not e.zones)
    if empty_zones > 0:
        print(f"  (empty zones: {empty_zones} - marked unused in source)")

    # Convert to JSON
    output_data = entries_to_json(entries)

    if args.verbose:
        print(f"\nDuplicate names handled: {output_data['duplicate_names_handled']}")
        print(f"Total keys in output: {len(output_data['fogs'])}")

    # Validate against clusters if specified
    if args.validate_clusters:
        if not args.validate_clusters.exists():
            print(
                f"Warning: clusters.json not found: {args.validate_clusters}",
                file=sys.stderr,
            )
        else:
            print(f"\nValidating against {args.validate_clusters}...")
            missing = validate_against_clusters(
                output_data["fogs"], args.validate_clusters
            )
            if missing:
                print(f"Warning: {len(missing)} fog_ids not found in fog_data:")
                for fog_id in missing[:10]:  # Show first 10
                    print(f"  - {fog_id}")
                if len(missing) > 10:
                    print(f"  ... and {len(missing) - 10} more")
            else:
                print("All cluster fog_ids found in fog_data!")

    # Write output
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(output_data, f, indent=2)

    print(f"\nWrote {args.output}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
