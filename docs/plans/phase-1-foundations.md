# Phase 1: Foundations - Detailed Implementation Spec

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Status**: Ready for implementation

## Objective

Set up the project structure and create the zone data pipeline:
1. Convert FogRando's `fog.txt` into our `zones.toml` format
2. Create the Python package structure
3. Implement config and zone parsers

## Prerequisites

- Python 3.10+
- Access to FogRando's `fog.txt` at: `/home/rom1/src/games/ER/fog/eldendata/Base/fog.txt`

## Deliverables

```
speedfog/
├── core/
│   ├── speedfog_core/
│   │   ├── __init__.py
│   │   ├── config.py          # Task 1.4
│   │   └── zones.py           # Task 1.5
│   ├── config.toml            # Task 1.4 (example)
│   └── zones.toml             # Task 1.3
├── data/
│   └── zone_warps.json        # Task 1.6 (fog gate positions)
├── tools/
│   ├── convert_fogrando.py    # Task 1.2
│   └── extract_warps.py       # Task 1.6 (extract from FogRando)
├── pyproject.toml             # Task 1.1
└── README.md                  # Task 1.1
```

**Note**: Zone data is split into two files:
- `zones.toml`: Gameplay metadata (type, weight, fog_count) - manually edited
- `data/zone_warps.json`: Technical warp data (positions, entity IDs) - extracted from FogRando

---

## Task 1.1: Project Structure

Create the base Python project with modern tooling.

### pyproject.toml

```toml
[project]
name = "speedfog-core"
version = "0.1.0"
description = "SpeedFog DAG generator for Elden Ring zone randomization"
requires-python = ">=3.10"
dependencies = [
    "tomli>=2.0.0;python_version<'3.11'",
    "pyyaml>=6.0",
]

[project.optional-dependencies]
dev = [
    "pytest>=7.0",
    "pytest-cov>=4.0",
]

[project.scripts]
speedfog = "speedfog_core.main:main"

[build-system]
requires = ["setuptools>=61.0"]
build-backend = "setuptools.build_meta"
```

### README.md

Create a basic README explaining:
- What SpeedFog is (1-2 sentences)
- How to install (`pip install -e .`)
- How to run (`speedfog config.toml`)
- Link to design document

---

## Task 1.2: convert_fogrando.py

Script to extract zone data from FogRando's `fog.txt` (YAML format) into our `zones.toml`.

### Input

FogRando's `fog.txt` is a YAML file with this structure:

```yaml
Areas:
- Name: chapel_start
  Text: Chapel of Anticipation
  Maps: m10_01_00_00
  To:
  - Area: roundtable
    Text: accessing an overworld grace
  Tags: start

- Name: stormveil
  Text: Stormveil Castle
  Maps: m10_00_00_00
  DefeatFlag: 10000800
  Tags: legacy

- Name: murkwater_catacombs
  Text: Murkwater Catacombs
  Maps: m30_00_00_00
  Tags: minidungeon

# ... etc
```

Key fields to extract:
- `Name`: zone identifier
- `Text`: human-readable name
- `Maps`: map ID(s)
- `Tags`: zone type indicators (legacy, minidungeon, overworld, boss, etc.)
- `DefeatFlag`: if present, indicates a boss zone
- `BossTrigger`: boss trigger ID

### Output

Generate `zones.toml` with this structure:

```toml
# Auto-generated from FogRando fog.txt
# Manual review required for weights and fog_count

[[zones]]
id = "stormveil"
map = "m10_00_00_00"
name = "Stormveil Castle"
type = "legacy_dungeon"    # Derived from tags
weight = 0                 # MANUAL: set after testing
fog_count = 0              # MANUAL: count fog gates (2=linear, 3=split/merge)
boss = "godrick"           # Derived from DefeatFlag presence
tags = ["legacy"]

[[zones]]
id = "murkwater_catacombs"
map = "m30_00_00_00"
name = "Murkwater Catacombs"
type = "catacomb"          # Derived from map prefix m30
weight = 0                 # MANUAL: estimate ~4-5
fog_count = 0              # MANUAL: typically 2 for mini-dungeons
boss = ""
tags = ["minidungeon"]
```

### Type Derivation Rules

Based on analysis of `fog.txt`:

| Condition | Derived Type |
|-----------|--------------|
| Tag contains `legacy` | `legacy_dungeon` |
| Map starts with `m30` | `catacomb` |
| Map starts with `m31` | `cave` |
| Map starts with `m32` | `tunnel` |
| Map starts with `m39` | `gaol` |
| Tag contains `boss` and no minidungeon tag | `boss_arena` |
| Tag contains `overworld` | `overworld` (excluded from pool) |
| Tag contains `start` | `start` |

### Exclusion Rules

Do NOT include zones that:
- Have tag `overworld` (open world areas)
- Have tag `trivial` (transition areas)
- Have tag `dlc` (for v1, DLC excluded)
- Are coffin-related (Ainsel, Deeproot, Lake of Rot)
- Are in Subterranean Shunning-Grounds

### Script Structure

```python
#!/usr/bin/env python3
"""
Convert FogRando fog.txt to SpeedFog zones.toml

Usage:
    python convert_fogrando.py <fog.txt> <output.toml>
    python convert_fogrando.py /path/to/fog.txt ./zones.toml
"""

import sys
import yaml
from pathlib import Path
from dataclasses import dataclass
from typing import Optional


@dataclass
class Zone:
    id: str
    map: str
    name: str
    type: str
    weight: int
    fog_count: int  # 2=linear, 3=can split/merge
    boss: str
    tags: list[str]


def load_fog_txt(path: Path) -> dict:
    """Load and parse FogRando's fog.txt YAML file."""
    with open(path, 'r', encoding='utf-8') as f:
        return yaml.safe_load(f)


def derive_zone_type(area: dict) -> Optional[str]:
    """
    Derive zone type from area data.
    Returns None if zone should be excluded.
    """
    tags = area.get('Tags', '').lower().split()
    maps = area.get('Maps', '')
    first_map = maps.split()[0] if maps else ''

    # Exclusion rules
    if 'overworld' in tags:
        return None
    if 'trivial' in tags:
        return None
    if 'dlc' in tags:
        return None  # v1: exclude DLC

    # Specific exclusions
    name = area.get('Name', '').lower()
    if any(x in name for x in ['ainsel', 'deeproot', 'lakeofrot', 'shunning']):
        return None

    # Type derivation
    if 'legacy' in tags:
        return 'legacy_dungeon'
    if first_map.startswith('m30'):
        return 'catacomb'
    if first_map.startswith('m31'):
        return 'cave'
    if first_map.startswith('m32'):
        return 'tunnel'
    if first_map.startswith('m39'):
        return 'gaol'
    if area.get('DefeatFlag') and 'minidungeon' not in tags:
        return 'boss_arena'
    if 'minidungeon' in tags:
        return 'mini_dungeon'  # Generic, needs manual review
    if 'start' in tags:
        return 'start'

    return None  # Unknown, exclude


def extract_boss_name(area: dict) -> str:
    """Extract boss name from DebugInfo if present."""
    debug_info = area.get('DebugInfo', '')
    if ' - ' in debug_info:
        # Format: "DefeatFlag: 12345 - m10_00_00_00 - Boss Name"
        parts = debug_info.split(' - ')
        if len(parts) >= 3:
            return parts[-1].strip()
    return ''


def convert_area_to_zone(area: dict) -> Optional[Zone]:
    """Convert a FogRando area to a SpeedFog zone."""
    zone_type = derive_zone_type(area)
    if zone_type is None:
        return None

    maps = area.get('Maps', '')
    first_map = maps.split()[0] if maps else ''

    tags = area.get('Tags', '').split() if area.get('Tags') else []

    return Zone(
        id=area.get('Name', ''),
        map=first_map,
        name=area.get('Text', ''),
        type=zone_type,
        weight=0,  # Manual assignment required
        fog_count=0,  # Manual assignment required (2=linear, 3=split/merge)
        boss=extract_boss_name(area),
        tags=tags,
    )


def zones_to_toml(zones: list[Zone]) -> str:
    """Convert zones to TOML format string."""
    lines = [
        "# SpeedFog Zone Data",
        "# Auto-generated from FogRando fog.txt",
        "# ",
        "# MANUAL REVIEW REQUIRED:",
        "# - Set weight for each zone (estimated minutes to complete)",
        "# - Set fog_count (2=linear passage, 3=can split/merge)",
        "# - Verify type categorization",
        "# - Add duration suffix to mini-dungeons (_short, _medium, _long)",
        "",
    ]

    # Group by type
    by_type: dict[str, list[Zone]] = {}
    for zone in zones:
        by_type.setdefault(zone.type, []).append(zone)

    type_order = [
        'start', 'legacy_dungeon', 'boss_arena',
        'catacomb', 'cave', 'tunnel', 'gaol', 'mini_dungeon'
    ]

    for zone_type in type_order:
        if zone_type not in by_type:
            continue

        lines.append(f"# {'=' * 50}")
        lines.append(f"# {zone_type.upper().replace('_', ' ')}S")
        lines.append(f"# {'=' * 50}")
        lines.append("")

        for zone in sorted(by_type[zone_type], key=lambda z: z.id):
            lines.append("[[zones]]")
            lines.append(f'id = "{zone.id}"')
            lines.append(f'map = "{zone.map}"')
            lines.append(f'name = "{zone.name}"')
            lines.append(f'type = "{zone.type}"')
            lines.append(f'weight = {zone.weight}  # TODO: estimate minutes')
            lines.append(f'fog_count = {zone.fog_count}  # TODO: 2=linear, 3=split/merge')
            if zone.boss:
                lines.append(f'boss = "{zone.boss}"')
            if zone.tags:
                tags_str = ', '.join(f'"{t}"' for t in zone.tags)
                lines.append(f'tags = [{tags_str}]')
            lines.append("")

    return '\n'.join(lines)


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)

    fog_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    if not fog_path.exists():
        print(f"Error: {fog_path} not found")
        sys.exit(1)

    print(f"Loading {fog_path}...")
    data = load_fog_txt(fog_path)

    areas = data.get('Areas', [])
    print(f"Found {len(areas)} areas")

    zones = []
    excluded = 0
    for area in areas:
        zone = convert_area_to_zone(area)
        if zone:
            zones.append(zone)
        else:
            excluded += 1

    print(f"Converted {len(zones)} zones, excluded {excluded}")

    toml_content = zones_to_toml(zones)
    output_path.write_text(toml_content, encoding='utf-8')
    print(f"Written to {output_path}")

    # Summary by type
    print("\nSummary by type:")
    by_type: dict[str, int] = {}
    for zone in zones:
        by_type[zone.type] = by_type.get(zone.type, 0) + 1
    for t, count in sorted(by_type.items()):
        print(f"  {t}: {count}")


if __name__ == '__main__':
    main()
```

### Expected Output Stats (approximate)

Based on fog.txt analysis:
- Legacy dungeons: ~8-10
- Catacombs: ~20-25
- Caves: ~15-20
- Tunnels: ~6-8
- Gaols: ~8-10
- Boss arenas: ~15-20

---

## Task 1.3: zones.toml (Manual Enrichment)

After running `convert_fogrando.py`, manually enrich the output:

### Weight Assignment Guidelines

| Zone Type | Short | Medium | Long |
|-----------|-------|--------|------|
| Legacy dungeon | - | - | 12-20 |
| Catacomb | 3-4 | 5-7 | 8-10 |
| Cave | 3-4 | 5-7 | 8-10 |
| Tunnel | 3-4 | 5-6 | - |
| Gaol | 2-3 | - | - |
| Boss arena | 2-5 | - | - |

### Duration Suffix Rules

For mini-dungeons, add suffix based on estimated completion time:
- `_short`: Can be completed in <5 minutes
- `_medium`: Takes 5-10 minutes
- `_long`: Takes >10 minutes

Example:
```toml
type = "catacomb_short"   # Was "catacomb"
```

### Entrance/Exit Population

Cross-reference with FogRando's `Entrances:` section in fog.txt to populate:
- `entrances`: fog gate IDs that lead INTO this zone
- `exits`: fog gate IDs that lead OUT of this zone

For v1, this can be simplified - we mainly need to know HOW MANY entrances/exits each zone has, not the exact IDs (the C# writer will handle the actual fog gate creation).

Simplified approach:
```toml
entrance_count = 1
exit_count = 2  # For zones with choices
```

---

## Task 1.4: config.py

Parse user configuration from TOML.

### config.py

```python
"""
Configuration parsing for SpeedFog.
"""

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

try:
    import tomllib  # Python 3.11+
except ImportError:
    import tomli as tomllib


@dataclass
class BudgetConfig:
    """Path budget configuration."""
    total_weight: int = 30
    tolerance: int = 5

    @property
    def min_weight(self) -> int:
        return self.total_weight - self.tolerance

    @property
    def max_weight(self) -> int:
        return self.total_weight + self.tolerance


@dataclass
class RequirementsConfig:
    """Minimum requirements for generated runs."""
    legacy_dungeons: int = 1
    bosses: int = 5
    mini_dungeons: int = 5


@dataclass
class StructureConfig:
    """DAG structure parameters."""
    max_parallel_paths: int = 3
    min_layers: int = 6
    max_layers: int = 10


@dataclass
class PathsConfig:
    """File paths configuration."""
    game_dir: Path = field(default_factory=lambda: Path("."))
    output_dir: Path = field(default_factory=lambda: Path("./output"))
    zones_file: Path = field(default_factory=lambda: Path("./zones.toml"))
    randomizer_dir: Path | None = None


@dataclass
class Config:
    """Main SpeedFog configuration."""
    seed: int = 0
    budget: BudgetConfig = field(default_factory=BudgetConfig)
    requirements: RequirementsConfig = field(default_factory=RequirementsConfig)
    structure: StructureConfig = field(default_factory=StructureConfig)
    paths: PathsConfig = field(default_factory=PathsConfig)

    @classmethod
    def from_toml(cls, path: Path) -> 'Config':
        """Load configuration from TOML file."""
        with open(path, 'rb') as f:
            data = tomllib.load(f)
        return cls.from_dict(data)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> 'Config':
        """Create Config from dictionary."""
        run = data.get('run', {})
        budget = data.get('budget', {})
        requirements = data.get('requirements', {})
        structure = data.get('structure', {})
        paths = data.get('paths', {})

        return cls(
            seed=run.get('seed', 0),
            budget=BudgetConfig(
                total_weight=budget.get('total_weight', 30),
                tolerance=budget.get('tolerance', 5),
            ),
            requirements=RequirementsConfig(
                legacy_dungeons=requirements.get('legacy_dungeons', 1),
                bosses=requirements.get('bosses', 5),
                mini_dungeons=requirements.get('mini_dungeons', 5),
            ),
            structure=StructureConfig(
                max_parallel_paths=structure.get('max_parallel_paths', 3),
                min_layers=structure.get('min_layers', 6),
                max_layers=structure.get('max_layers', 10),
            ),
            paths=PathsConfig(
                game_dir=Path(paths.get('game_dir', '.')),
                output_dir=Path(paths.get('output_dir', './output')),
                zones_file=Path(paths.get('zones_file', './zones.toml')),
                randomizer_dir=Path(paths['randomizer_dir']) if paths.get('randomizer_dir') else None,
            ),
        )


def load_config(path: Path) -> Config:
    """Load configuration from file, with defaults for missing values."""
    if not path.exists():
        raise FileNotFoundError(f"Config file not found: {path}")
    return Config.from_toml(path)
```

### Example config.toml

```toml
[run]
seed = 12345

[budget]
total_weight = 30
tolerance = 5

[requirements]
legacy_dungeons = 1
bosses = 5
mini_dungeons = 5

[structure]
max_parallel_paths = 3
min_layers = 6
max_layers = 10

[paths]
game_dir = "C:/Program Files/Steam/steamapps/common/ELDEN RING/Game"
output_dir = "./output"
zones_file = "./zones.toml"
# randomizer_dir = "./mods/randomizer"  # Optional
```

---

## Task 1.5: zones.py

Parse zone data from TOML.

### zones.py

```python
"""
Zone data parsing for SpeedFog.
"""

from dataclasses import dataclass, field
from enum import Enum, auto
from pathlib import Path
from typing import Any

try:
    import tomllib
except ImportError:
    import tomli as tomllib


class ZoneType(Enum):
    """Zone type categories."""
    START = auto()
    FINAL_BOSS = auto()
    LEGACY_DUNGEON = auto()
    CATACOMB_SHORT = auto()
    CATACOMB_MEDIUM = auto()
    CATACOMB_LONG = auto()
    CAVE_SHORT = auto()
    CAVE_MEDIUM = auto()
    CAVE_LONG = auto()
    TUNNEL = auto()
    GAOL = auto()
    BOSS_ARENA = auto()

    @classmethod
    def from_string(cls, s: str) -> 'ZoneType':
        """Parse zone type from string."""
        mapping = {
            'start': cls.START,
            'final_boss': cls.FINAL_BOSS,
            'legacy_dungeon': cls.LEGACY_DUNGEON,
            'catacomb_short': cls.CATACOMB_SHORT,
            'catacomb_medium': cls.CATACOMB_MEDIUM,
            'catacomb_long': cls.CATACOMB_LONG,
            'catacomb': cls.CATACOMB_MEDIUM,  # Default
            'cave_short': cls.CAVE_SHORT,
            'cave_medium': cls.CAVE_MEDIUM,
            'cave_long': cls.CAVE_LONG,
            'cave': cls.CAVE_MEDIUM,  # Default
            'tunnel': cls.TUNNEL,
            'gaol': cls.GAOL,
            'boss_arena': cls.BOSS_ARENA,
        }
        return mapping.get(s.lower(), cls.BOSS_ARENA)

    def is_mini_dungeon(self) -> bool:
        """Check if this is a mini-dungeon type."""
        return self in {
            ZoneType.CATACOMB_SHORT, ZoneType.CATACOMB_MEDIUM, ZoneType.CATACOMB_LONG,
            ZoneType.CAVE_SHORT, ZoneType.CAVE_MEDIUM, ZoneType.CAVE_LONG,
            ZoneType.TUNNEL, ZoneType.GAOL,
        }

    def is_boss(self) -> bool:
        """Check if this zone has a boss."""
        return self in {
            ZoneType.LEGACY_DUNGEON, ZoneType.BOSS_ARENA, ZoneType.FINAL_BOSS,
        } or self.is_mini_dungeon()


@dataclass
class Zone:
    """Represents a game zone/area."""
    id: str
    map: str
    name: str
    type: ZoneType
    weight: int
    fog_count: int = 2  # Number of fog gates (2=linear, 3=can split/merge)
    boss: str = ""
    tags: list[str] = field(default_factory=list)
    min_tier: int = 1
    max_tier: int = 34

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> 'Zone':
        """Create Zone from dictionary."""
        return cls(
            id=data['id'],
            map=data.get('map', ''),
            name=data.get('name', data['id']),
            type=ZoneType.from_string(data.get('type', 'boss_arena')),
            weight=data.get('weight', 5),
            fog_count=data.get('fog_count', 2),
            boss=data.get('boss', ''),
            tags=data.get('tags', []),
            min_tier=data.get('min_tier', 1),
            max_tier=data.get('max_tier', 34),
        )

    def can_split_or_merge(self) -> bool:
        """Check if this zone can be a split or merge point (3+ fogs)."""
        return self.fog_count >= 3


@dataclass
class ZonePool:
    """Collection of available zones."""
    zones: dict[str, Zone] = field(default_factory=dict)
    _by_type: dict[ZoneType, list[Zone]] = field(default_factory=dict, repr=False)

    def add(self, zone: Zone) -> None:
        """Add a zone to the pool."""
        self.zones[zone.id] = zone
        self._by_type.setdefault(zone.type, []).append(zone)

    def get(self, zone_id: str) -> Zone | None:
        """Get zone by ID."""
        return self.zones.get(zone_id)

    def by_type(self, zone_type: ZoneType) -> list[Zone]:
        """Get all zones of a given type."""
        return self._by_type.get(zone_type, [])

    def legacy_dungeons(self) -> list[Zone]:
        """Get all legacy dungeon zones."""
        return self.by_type(ZoneType.LEGACY_DUNGEON)

    def mini_dungeons(self) -> list[Zone]:
        """Get all mini-dungeon zones."""
        result = []
        for zone_type in ZoneType:
            if zone_type.is_mini_dungeon():
                result.extend(self.by_type(zone_type))
        return result

    def boss_arenas(self) -> list[Zone]:
        """Get standalone boss arena zones."""
        return self.by_type(ZoneType.BOSS_ARENA)

    def all_zones(self) -> list[Zone]:
        """Get all zones."""
        return list(self.zones.values())

    def filter_by_tier(self, tier: int) -> list[Zone]:
        """Get zones available at a given tier."""
        return [z for z in self.zones.values()
                if z.min_tier <= tier <= z.max_tier]

    @classmethod
    def from_toml(cls, path: Path) -> 'ZonePool':
        """Load zone pool from TOML file."""
        with open(path, 'rb') as f:
            data = tomllib.load(f)

        pool = cls()
        for zone_data in data.get('zones', []):
            zone = Zone.from_dict(zone_data)
            pool.add(zone)

        return pool


def load_zones(path: Path) -> ZonePool:
    """Load zones from file."""
    if not path.exists():
        raise FileNotFoundError(f"Zones file not found: {path}")
    return ZonePool.from_toml(path)
```

---

## Acceptance Criteria

### Task 1.1
- [ ] `pyproject.toml` exists and `pip install -e .` succeeds
- [ ] `README.md` exists with basic usage instructions

### Task 1.2
- [ ] `convert_fogrando.py` runs without errors
- [ ] Output contains expected zone types
- [ ] Excluded zones (overworld, DLC, coffin areas) are not in output

### Task 1.3
- [ ] `zones.toml` has weights assigned to all zones
- [ ] `zones.toml` has fog_count assigned to all zones (2 or 3)
- [ ] Mini-dungeons have duration suffixes
- [ ] At least 5 legacy dungeons available
- [ ] At least 15 mini-dungeons available
- [ ] At least 10 boss arenas available
- [ ] At least some zones with fog_count=3 (for splits/merges)

### Task 1.4
- [ ] `Config.from_toml()` parses example config correctly
- [ ] Default values work when sections are missing
- [ ] `BudgetConfig.min_weight` and `.max_weight` compute correctly

### Task 1.5
- [ ] `ZonePool.from_toml()` loads zones correctly
- [ ] `ZoneType.from_string()` handles all expected types
- [ ] Filter methods (`by_type`, `filter_by_tier`) work correctly

### Task 1.6
- [ ] `extract_warps.py` extracts fog positions from FogRando's fog.txt
- [ ] `zone_warps.json` contains all zones from `zones.toml`
- [ ] Validation script confirms zones.toml and zone_warps.json are in sync

---

## Task 1.6: extract_warps.py

Extract fog gate positions from FogRando's data files to create `zone_warps.json`.

### Input

FogRando's `fog.txt` contains an `Entrances:` section with fog gate data:

```yaml
Entrances:
- Name: stormveil_main_gate
  Area: stormveil
  Text: Stormveil Main Gate
  Pos: 123.4 56.7 89.0
  Rot: 0 180 0
  ID: 10001800
  # ...
```

### Output

`data/zone_warps.json`:

```json
{
  "stormveil": {
    "map": "m10_00_00_00",
    "fogs": [
      {
        "id": "stormveil_main_gate",
        "position": [123.4, 56.7, 89.0],
        "rotation": [0, 180, 0],
        "entity_id": 10001800
      }
    ]
  }
}
```

### Script Structure

```python
#!/usr/bin/env python3
"""
Extract fog gate warp data from FogRando fog.txt.

Usage:
    python extract_warps.py <fog.txt> <output.json>
"""

import sys
import json
import yaml
from pathlib import Path


def extract_warps(fog_path: Path) -> dict:
    """Extract warp data from FogRando fog.txt."""
    with open(fog_path, 'r', encoding='utf-8') as f:
        data = yaml.safe_load(f)

    warps = {}
    areas = {a['Name']: a for a in data.get('Areas', [])}

    for entrance in data.get('Entrances', []):
        area_id = entrance.get('Area', '')
        if area_id not in warps:
            area_data = areas.get(area_id, {})
            warps[area_id] = {
                'map': area_data.get('Maps', '').split()[0] if area_data.get('Maps') else '',
                'fogs': []
            }

        pos = entrance.get('Pos', '0 0 0').split()
        rot = entrance.get('Rot', '0 0 0').split()

        warps[area_id]['fogs'].append({
            'id': entrance.get('Name', ''),
            'position': [float(x) for x in pos],
            'rotation': [float(x) for x in rot],
            'entity_id': entrance.get('ID', 0),
        })

    return warps


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)

    fog_path = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    warps = extract_warps(fog_path)

    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(warps, f, indent=2)

    print(f"Extracted warps for {len(warps)} zones to {output_path}")


if __name__ == '__main__':
    main()
```

### Validation Script

Add to `tools/validate_zones.py`:

```python
#!/usr/bin/env python3
"""Validate zones.toml and zone_warps.json are in sync."""

import sys
import json
from pathlib import Path

try:
    import tomllib
except ImportError:
    import tomli as tomllib


def validate(zones_path: Path, warps_path: Path) -> list[str]:
    """Return list of validation errors."""
    errors = []

    with open(zones_path, 'rb') as f:
        zones_data = tomllib.load(f)

    with open(warps_path, 'r') as f:
        warps = json.load(f)

    zones_list = zones_data.get('zones', [])
    zone_ids = {z['id'] for z in zones_list}
    warp_ids = set(warps.keys())

    # Zones without warp data
    missing_warps = zone_ids - warp_ids
    for z in missing_warps:
        errors.append(f"Zone '{z}' has no warp data in zone_warps.json")

    # Warps without zone data
    extra_warps = warp_ids - zone_ids
    for w in extra_warps:
        errors.append(f"Warp data '{w}' has no zone in zones.toml")

    # Validate fog_count matches actual fog count in zone_warps.json
    for zone in zones_list:
        zone_id = zone['id']
        expected_fogs = zone.get('fog_count', 2)
        if zone_id in warps:
            actual_fogs = len(warps[zone_id].get('fogs', []))
            if actual_fogs != expected_fogs:
                errors.append(
                    f"Zone '{zone_id}': fog_count={expected_fogs} "
                    f"but {actual_fogs} fogs in zone_warps.json"
                )

    # Validate zone weights are positive
    for zone in zones_list:
        if zone.get('weight', 0) <= 0:
            errors.append(f"Zone '{zone['id']}' has invalid weight: {zone.get('weight')}")

    return errors


if __name__ == '__main__':
    zones_path = Path('core/zones.toml')
    warps_path = Path('data/zone_warps.json')

    errors = validate(zones_path, warps_path)
    if errors:
        for e in errors:
            print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)
    else:
        print("Validation passed: zones.toml and zone_warps.json are in sync")
```

---

## Testing

### Unit Tests (tests/test_config.py)

```python
import pytest
from pathlib import Path
from speedfog_core.config import Config, BudgetConfig

def test_budget_min_max():
    budget = BudgetConfig(total_weight=30, tolerance=5)
    assert budget.min_weight == 25
    assert budget.max_weight == 35

def test_config_defaults():
    config = Config.from_dict({})
    assert config.seed == 0
    assert config.budget.total_weight == 30
    assert config.requirements.bosses == 5

def test_config_from_toml(tmp_path):
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
seed = 42

[budget]
total_weight = 25
""")
    config = Config.from_toml(config_file)
    assert config.seed == 42
    assert config.budget.total_weight == 25
```

### Unit Tests (tests/test_zones.py)

```python
import pytest
from speedfog_core.zones import Zone, ZoneType, ZonePool

def test_zone_type_from_string():
    assert ZoneType.from_string("legacy_dungeon") == ZoneType.LEGACY_DUNGEON
    assert ZoneType.from_string("catacomb_short") == ZoneType.CATACOMB_SHORT
    assert ZoneType.from_string("catacomb") == ZoneType.CATACOMB_MEDIUM

def test_zone_can_split_or_merge():
    zone = Zone(id="test", map="m10", name="Test", type=ZoneType.LEGACY_DUNGEON,
                weight=10, fog_count=3)
    assert zone.can_split_or_merge() is True

    zone2 = Zone(id="test2", map="m10", name="Test2", type=ZoneType.BOSS_ARENA,
                 weight=3, fog_count=2)
    assert zone2.can_split_or_merge() is False

def test_zone_pool_by_type(tmp_path):
    zones_file = tmp_path / "zones.toml"
    zones_file.write_text("""
[[zones]]
id = "stormveil"
type = "legacy_dungeon"
weight = 15

[[zones]]
id = "murkwater"
type = "catacomb_short"
weight = 4
""")
    pool = ZonePool.from_toml(zones_file)
    assert len(pool.legacy_dungeons()) == 1
    assert len(pool.mini_dungeons()) == 1
```

---

## Next Phase

After completing Phase 1, proceed to [Phase 2: DAG Generation](./phase-2-dag-generation.md).
