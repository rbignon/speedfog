#!/usr/bin/env python3
"""
Validate zones.toml and zone_warps.json are in sync.

Usage:
    python validate_zones.py [zones.toml] [zone_warps.json]
    python validate_zones.py core/zones.toml data/zone_warps.json
"""

import json
import sys
from pathlib import Path

try:
    import tomllib
except ImportError:
    import tomli as tomllib


def validate(zones_path: Path, warps_path: Path) -> list[str]:
    """Return list of validation errors."""
    errors = []

    with open(zones_path, "rb") as f:
        zones_data = tomllib.load(f)

    with open(warps_path, "r", encoding="utf-8") as f:
        warps = json.load(f)

    zones_list = zones_data.get("zones", [])
    zone_ids = {z["id"] for z in zones_list}
    warp_ids = set(warps.keys())

    # Zones without warp data (warning, not error for now)
    missing_warps = zone_ids - warp_ids
    for z in sorted(missing_warps):
        errors.append(f"WARNING: Zone '{z}' has no warp data in zone_warps.json")

    # Orphaned warps (in zone_warps.json but not in zones.toml)
    orphaned_warps = warp_ids - zone_ids
    for z in sorted(orphaned_warps):
        errors.append(f"WARNING: Warp zone '{z}' not defined in zones.toml")

    # Validate zone weights are set (not zero)
    for zone in zones_list:
        if zone.get("weight", 0) <= 0:
            errors.append(f"Zone '{zone['id']}' has invalid weight: {zone.get('weight', 0)}")

    # Validate fog_count is set (not zero)
    for zone in zones_list:
        if zone.get("fog_count", 0) <= 0:
            errors.append(f"Zone '{zone['id']}' has invalid fog_count: {zone.get('fog_count', 0)}")

    return errors


def main() -> None:
    if len(sys.argv) == 1:
        zones_path = Path("core/zones.toml")
        warps_path = Path("data/zone_warps.json")
    elif len(sys.argv) == 3:
        zones_path = Path(sys.argv[1])
        warps_path = Path(sys.argv[2])
    else:
        print(__doc__)
        sys.exit(1)

    if not zones_path.exists():
        print(f"Error: {zones_path} not found")
        sys.exit(1)
    if not warps_path.exists():
        print(f"Error: {warps_path} not found")
        sys.exit(1)

    errors = validate(zones_path, warps_path)

    warnings = [e for e in errors if e.startswith("WARNING")]
    real_errors = [e for e in errors if not e.startswith("WARNING")]

    for w in warnings:
        print(w, file=sys.stderr)
    for e in real_errors:
        print(f"ERROR: {e}", file=sys.stderr)

    if real_errors:
        print(f"\n{len(real_errors)} errors, {len(warnings)} warnings")
        sys.exit(1)
    else:
        print(f"Validation passed with {len(warnings)} warnings")
        sys.exit(0)


if __name__ == "__main__":
    main()
