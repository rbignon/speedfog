#!/usr/bin/env python3
"""
Extract zone→map mapping from zones.toml for the C# writer.
Outputs zones_data.json with minimal zone metadata.

Usage:
    python extract_zones_data.py zones.toml zones_data.json [--validate-clusters clusters.json]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    import tomllib  # Python 3.11+
except ImportError:
    import tomli as tomllib  # pip install tomli


def extract_zones(zones_toml_path: Path) -> dict:
    """Extract zone data from zones.toml."""
    with open(zones_toml_path, "rb") as f:
        data = tomllib.load(f)

    zones = {}
    for zone in data.get("zones", []):
        zone_id = zone.get("id")
        if not zone_id:
            continue

        zones[zone_id] = {
            "map": zone.get("map", ""),
            "name": zone.get("name", zone_id),
        }

    return zones


def validate_zones(zones: dict, clusters_path: Path | None) -> list[str]:
    """Validate that all zones in clusters.json are present."""
    if clusters_path is None or not clusters_path.exists():
        return []

    with open(clusters_path, encoding="utf-8") as f:
        clusters = json.load(f)

    missing = []
    for cluster in clusters.get("clusters", []):
        for zone_id in cluster.get("zones", []):
            if zone_id not in zones:
                missing.append(zone_id)

    return list(set(missing))  # Deduplicate


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Extract zone→map mapping from zones.toml"
    )
    parser.add_argument("zones_toml", type=Path, help="Path to zones.toml")
    parser.add_argument(
        "output_json", type=Path, help="Output path for zones_data.json"
    )
    parser.add_argument(
        "--validate-clusters",
        type=Path,
        help="Path to clusters.json for validation",
    )
    args = parser.parse_args()

    if not args.zones_toml.exists():
        print(f"Error: Input file not found: {args.zones_toml}", file=sys.stderr)
        return 1

    # Extract zones
    zones = extract_zones(args.zones_toml)
    print(f"Parsed {len(zones)} zones")

    # Validate against clusters if provided
    if args.validate_clusters:
        if not args.validate_clusters.exists():
            print(
                f"Warning: clusters.json not found: {args.validate_clusters}",
                file=sys.stderr,
            )
        else:
            missing = validate_zones(zones, args.validate_clusters)
            if missing:
                print(f"Warning: {len(missing)} zones missing from zones.toml:")
                for z in sorted(missing)[:10]:
                    print(f"  - {z}")
                if len(missing) > 10:
                    print(f"  ... and {len(missing) - 10} more")
            else:
                print("All cluster zones found in zones.toml!")

    # Write output
    output = {
        "version": "1.0",
        "zones": zones,
    }

    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    with open(args.output_json, "w", encoding="utf-8") as f:
        json.dump(output, f, indent=2)

    print(f"Written {args.output_json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
