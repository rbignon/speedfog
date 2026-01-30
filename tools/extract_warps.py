#!/usr/bin/env python3
"""
Extract fog gate warp data from FogRando fog.txt.

This script extracts technical fog gate data (entity IDs, map references, positions)
from FogRando's fog.txt file for use by the C# writer in Phase 3.

Usage:
    python extract_warps.py <fog.txt> <output.json>
    python extract_warps.py reference/fogrando-data/fog.txt data/zone_warps.json
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import yaml


def extract_warps(fog_path: Path) -> dict[str, dict]:
    """
    Extract warp data from FogRando fog.txt.

    Returns a dict mapping zone names to their fog gate data:
    {
        "zone_name": {
            "fogs": [
                {
                    "id": "AEG099_002_9000",
                    "entity_id": 10001800,
                    "map": "m10_00_00_00",
                    "text": "Godrick front",
                    "side": "ASide",
                    "connects_to": "stormveil_godrick"
                },
                ...
            ]
        },
        ...
    }
    """
    with open(fog_path, encoding="utf-8") as f:
        data = yaml.safe_load(f)

    warps: dict[str, dict] = {}

    for entrance in data.get("Entrances", []):
        # Skip unused or removed entrances
        # Tags is a space-separated string
        tags = entrance.get("Tags", "").split()
        if "unused" in tags or "remove" in tags:
            continue

        # Get entrance-level data
        entrance_name = entrance.get("Name", "")
        entity_id = entrance.get("ID", 0)
        entrance_map = entrance.get("Area", "")  # This is the map ID
        text = entrance.get("Text", "")

        # Process ASide
        aside = entrance.get("ASide", {})
        aside_area = aside.get("Area", "")
        if aside_area and not aside_area.startswith("m"):
            # Valid zone name (not a raw map ID)
            if aside_area not in warps:
                warps[aside_area] = {"fogs": []}

            # Find what the other side connects to
            bside = entrance.get("BSide", {})
            bside_area = bside.get("Area", "")

            fog_data = {
                "id": entrance_name,
                "entity_id": entity_id,
                "map": entrance_map,
                "text": text,
                "side": "ASide",
                "connects_to": bside_area if bside_area else None,
            }

            # Avoid duplicates
            existing_ids = [(f["id"], f["side"]) for f in warps[aside_area]["fogs"]]
            if (fog_data["id"], fog_data["side"]) not in existing_ids:
                warps[aside_area]["fogs"].append(fog_data)

        # Process BSide
        bside = entrance.get("BSide", {})
        bside_area = bside.get("Area", "")
        if bside_area and not bside_area.startswith("m"):
            # Valid zone name
            if bside_area not in warps:
                warps[bside_area] = {"fogs": []}

            # Find what the other side connects to
            aside = entrance.get("ASide", {})
            aside_area = aside.get("Area", "")

            fog_data = {
                "id": entrance_name,
                "entity_id": entity_id,
                "map": entrance_map,
                "text": text,
                "side": "BSide",
                "connects_to": aside_area if aside_area else None,
            }

            # Avoid duplicates
            existing_ids = [(f["id"], f["side"]) for f in warps[bside_area]["fogs"]]
            if (fog_data["id"], fog_data["side"]) not in existing_ids:
                warps[bside_area]["fogs"].append(fog_data)

    return warps


def main() -> None:
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)

    fog_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    if not fog_path.exists():
        print(f"Error: {fog_path} not found")
        sys.exit(1)

    print(f"Extracting warps from {fog_path}...")
    warps = extract_warps(fog_path)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(warps, f, indent=2)

    # Count stats
    total_fogs = sum(len(w["fogs"]) for w in warps.values())
    print(f"Extracted {total_fogs} fog gates for {len(warps)} zones to {output_path}")


if __name__ == "__main__":
    main()
