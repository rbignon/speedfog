# Phase 1: Foundations - Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Set up the Python project structure, convert FogRando zone data, and implement config/zone parsers.

**Architecture:** Python package (`speedfog-core`) with TOML configuration. Data pipeline converts FogRando's `fog.txt` (YAML) to our `zones.toml` format. Two extraction scripts produce zone metadata and warp positions separately.

**Tech Stack:** Python 3.10+, tomli/tomllib, PyYAML, pytest

---

## Task 1: Create Python Project Structure

**Files:**
- Create: `core/pyproject.toml`
- Create: `core/README.md`
- Create: `core/speedfog_core/__init__.py`

**Step 1: Create directory structure**

```bash
mkdir -p core/speedfog_core core/tests
```

**Step 2: Write pyproject.toml**

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

**Step 3: Write minimal __init__.py**

```python
"""SpeedFog core - DAG generator for Elden Ring zone randomization."""

__version__ = "0.1.0"
```

**Step 4: Write README.md**

```markdown
# SpeedFog Core

DAG generator for Elden Ring SpeedFog mod.

## Installation

```bash
pip install -e ".[dev]"
```

## Usage

```bash
speedfog config.toml -o graph.json --spoiler spoiler.txt
```

## Documentation

See [Design Document](../docs/plans/2026-01-29-speedfog-design.md).
```

**Step 5: Verify installation**

Run: `cd core && pip install -e ".[dev]"`
Expected: Installation succeeds (speedfog command not yet functional)

**Step 6: Commit**

```bash
git add core/
git commit -m "feat(core): initialize Python project structure"
```

---

## Task 2: Implement Config Parser

**Files:**
- Create: `core/speedfog_core/config.py`
- Create: `core/tests/test_config.py`

**Step 1: Write the failing test for BudgetConfig**

```python
# core/tests/test_config.py
"""Tests for config parsing."""

import pytest
from speedfog_core.config import BudgetConfig


def test_budget_min_max():
    """BudgetConfig computes min/max weight from total and tolerance."""
    budget = BudgetConfig(total_weight=30, tolerance=5)
    assert budget.min_weight == 25
    assert budget.max_weight == 35
```

**Step 2: Run test to verify it fails**

Run: `cd core && python -m pytest tests/test_config.py::test_budget_min_max -v`
Expected: FAIL with "ModuleNotFoundError" or "ImportError"

**Step 3: Write minimal BudgetConfig implementation**

```python
# core/speedfog_core/config.py
"""Configuration parsing for SpeedFog."""

from dataclasses import dataclass


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
```

**Step 4: Run test to verify it passes**

Run: `cd core && python -m pytest tests/test_config.py::test_budget_min_max -v`
Expected: PASS

**Step 5: Write failing test for Config defaults**

Add to `core/tests/test_config.py`:

```python
from speedfog_core.config import Config


def test_config_defaults():
    """Config.from_dict with empty dict uses all defaults."""
    config = Config.from_dict({})
    assert config.seed == 0
    assert config.budget.total_weight == 30
    assert config.budget.tolerance == 5
    assert config.requirements.bosses == 5
    assert config.requirements.legacy_dungeons == 1
    assert config.requirements.mini_dungeons == 5
    assert config.structure.max_parallel_paths == 3
```

**Step 6: Run test to verify it fails**

Run: `cd core && python -m pytest tests/test_config.py::test_config_defaults -v`
Expected: FAIL with "cannot import name 'Config'"

**Step 7: Implement full Config class**

Extend `core/speedfog_core/config.py`:

```python
"""Configuration parsing for SpeedFog."""

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

try:
    import tomllib
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
    def from_toml(cls, path: Path) -> "Config":
        """Load configuration from TOML file."""
        with open(path, "rb") as f:
            data = tomllib.load(f)
        return cls.from_dict(data)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Config":
        """Create Config from dictionary."""
        run = data.get("run", {})
        budget = data.get("budget", {})
        requirements = data.get("requirements", {})
        structure = data.get("structure", {})
        paths = data.get("paths", {})

        return cls(
            seed=run.get("seed", 0),
            budget=BudgetConfig(
                total_weight=budget.get("total_weight", 30),
                tolerance=budget.get("tolerance", 5),
            ),
            requirements=RequirementsConfig(
                legacy_dungeons=requirements.get("legacy_dungeons", 1),
                bosses=requirements.get("bosses", 5),
                mini_dungeons=requirements.get("mini_dungeons", 5),
            ),
            structure=StructureConfig(
                max_parallel_paths=structure.get("max_parallel_paths", 3),
                min_layers=structure.get("min_layers", 6),
                max_layers=structure.get("max_layers", 10),
            ),
            paths=PathsConfig(
                game_dir=Path(paths.get("game_dir", ".")),
                output_dir=Path(paths.get("output_dir", "./output")),
                zones_file=Path(paths.get("zones_file", "./zones.toml")),
                randomizer_dir=(
                    Path(paths["randomizer_dir"])
                    if paths.get("randomizer_dir")
                    else None
                ),
            ),
        )


def load_config(path: Path) -> Config:
    """Load configuration from file, with defaults for missing values."""
    if not path.exists():
        raise FileNotFoundError(f"Config file not found: {path}")
    return Config.from_toml(path)
```

**Step 8: Run test to verify it passes**

Run: `cd core && python -m pytest tests/test_config.py::test_config_defaults -v`
Expected: PASS

**Step 9: Write failing test for Config.from_toml**

Add to `core/tests/test_config.py`:

```python
def test_config_from_toml(tmp_path):
    """Config.from_toml parses TOML file correctly."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[run]
seed = 42

[budget]
total_weight = 25
tolerance = 3

[requirements]
bosses = 7
""")
    config = Config.from_toml(config_file)
    assert config.seed == 42
    assert config.budget.total_weight == 25
    assert config.budget.tolerance == 3
    assert config.budget.min_weight == 22
    assert config.budget.max_weight == 28
    assert config.requirements.bosses == 7
    # Defaults for unspecified values
    assert config.requirements.legacy_dungeons == 1
```

**Step 10: Run test to verify it passes**

Run: `cd core && python -m pytest tests/test_config.py::test_config_from_toml -v`
Expected: PASS (implementation already supports this)

**Step 11: Run all config tests**

Run: `cd core && python -m pytest tests/test_config.py -v`
Expected: All 3 tests PASS

**Step 12: Commit**

```bash
git add core/speedfog_core/config.py core/tests/test_config.py
git commit -m "feat(core): implement config parser with TOML support"
```

---

## Task 3: Implement Zone Parser

**Files:**
- Create: `core/speedfog_core/zones.py`
- Create: `core/tests/test_zones.py`

**Step 1: Write failing test for ZoneType enum**

```python
# core/tests/test_zones.py
"""Tests for zone parsing."""

import pytest
from speedfog_core.zones import ZoneType


def test_zone_type_from_string():
    """ZoneType.from_string parses zone type strings."""
    assert ZoneType.from_string("legacy_dungeon") == ZoneType.LEGACY_DUNGEON
    assert ZoneType.from_string("catacomb_short") == ZoneType.CATACOMB_SHORT
    assert ZoneType.from_string("catacomb") == ZoneType.CATACOMB_MEDIUM  # default
    assert ZoneType.from_string("cave") == ZoneType.CAVE_MEDIUM  # default
    assert ZoneType.from_string("tunnel") == ZoneType.TUNNEL
    assert ZoneType.from_string("gaol") == ZoneType.GAOL
    assert ZoneType.from_string("boss_arena") == ZoneType.BOSS_ARENA
```

**Step 2: Run test to verify it fails**

Run: `cd core && python -m pytest tests/test_zones.py::test_zone_type_from_string -v`
Expected: FAIL with "ModuleNotFoundError"

**Step 3: Write ZoneType enum implementation**

```python
# core/speedfog_core/zones.py
"""Zone data parsing for SpeedFog."""

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
    def from_string(cls, s: str) -> "ZoneType":
        """Parse zone type from string."""
        mapping = {
            "start": cls.START,
            "final_boss": cls.FINAL_BOSS,
            "legacy_dungeon": cls.LEGACY_DUNGEON,
            "catacomb_short": cls.CATACOMB_SHORT,
            "catacomb_medium": cls.CATACOMB_MEDIUM,
            "catacomb_long": cls.CATACOMB_LONG,
            "catacomb": cls.CATACOMB_MEDIUM,  # Default
            "cave_short": cls.CAVE_SHORT,
            "cave_medium": cls.CAVE_MEDIUM,
            "cave_long": cls.CAVE_LONG,
            "cave": cls.CAVE_MEDIUM,  # Default
            "tunnel": cls.TUNNEL,
            "gaol": cls.GAOL,
            "boss_arena": cls.BOSS_ARENA,
        }
        return mapping.get(s.lower(), cls.BOSS_ARENA)

    def is_mini_dungeon(self) -> bool:
        """Check if this is a mini-dungeon type."""
        return self in {
            ZoneType.CATACOMB_SHORT,
            ZoneType.CATACOMB_MEDIUM,
            ZoneType.CATACOMB_LONG,
            ZoneType.CAVE_SHORT,
            ZoneType.CAVE_MEDIUM,
            ZoneType.CAVE_LONG,
            ZoneType.TUNNEL,
            ZoneType.GAOL,
        }

    def is_boss(self) -> bool:
        """Check if this zone type has a boss."""
        return (
            self
            in {
                ZoneType.LEGACY_DUNGEON,
                ZoneType.BOSS_ARENA,
                ZoneType.FINAL_BOSS,
            }
            or self.is_mini_dungeon()
        )
```

**Step 4: Run test to verify it passes**

Run: `cd core && python -m pytest tests/test_zones.py::test_zone_type_from_string -v`
Expected: PASS

**Step 5: Write failing test for Zone dataclass**

Add to `core/tests/test_zones.py`:

```python
from speedfog_core.zones import Zone


def test_zone_can_split_or_merge():
    """Zone.can_split_or_merge checks fog_count >= 3."""
    zone_3fogs = Zone(
        id="stormveil",
        map="m10_00_00_00",
        name="Stormveil Castle",
        type=ZoneType.LEGACY_DUNGEON,
        weight=15,
        fog_count=3,
    )
    assert zone_3fogs.can_split_or_merge() is True

    zone_2fogs = Zone(
        id="murkwater",
        map="m30_00_00_00",
        name="Murkwater Catacombs",
        type=ZoneType.CATACOMB_SHORT,
        weight=4,
        fog_count=2,
    )
    assert zone_2fogs.can_split_or_merge() is False
```

**Step 6: Run test to verify it fails**

Run: `cd core && python -m pytest tests/test_zones.py::test_zone_can_split_or_merge -v`
Expected: FAIL with "cannot import name 'Zone'"

**Step 7: Write Zone dataclass implementation**

Add to `core/speedfog_core/zones.py`:

```python
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

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Zone":
        """Create Zone from dictionary."""
        return cls(
            id=data["id"],
            map=data.get("map", ""),
            name=data.get("name", data["id"]),
            type=ZoneType.from_string(data.get("type", "boss_arena")),
            weight=data.get("weight", 5),
            fog_count=data.get("fog_count", 2),
            boss=data.get("boss", ""),
            tags=data.get("tags", []),
        )

    def can_split_or_merge(self) -> bool:
        """Check if this zone can be a split or merge point (3+ fogs)."""
        return self.fog_count >= 3
```

**Step 8: Run test to verify it passes**

Run: `cd core && python -m pytest tests/test_zones.py::test_zone_can_split_or_merge -v`
Expected: PASS

**Step 9: Write failing test for ZonePool**

Add to `core/tests/test_zones.py`:

```python
from speedfog_core.zones import ZonePool


def test_zone_pool_from_toml(tmp_path):
    """ZonePool.from_toml loads zones from TOML file."""
    zones_file = tmp_path / "zones.toml"
    zones_file.write_text('''
[[zones]]
id = "stormveil"
map = "m10_00_00_00"
name = "Stormveil Castle"
type = "legacy_dungeon"
weight = 15
fog_count = 3
boss = "godrick"

[[zones]]
id = "murkwater"
map = "m30_00_00_00"
name = "Murkwater Catacombs"
type = "catacomb_short"
weight = 4
fog_count = 2
''')
    pool = ZonePool.from_toml(zones_file)

    assert len(pool.all_zones()) == 2
    assert len(pool.legacy_dungeons()) == 1
    assert len(pool.mini_dungeons()) == 1

    stormveil = pool.get("stormveil")
    assert stormveil is not None
    assert stormveil.name == "Stormveil Castle"
    assert stormveil.boss == "godrick"
    assert stormveil.can_split_or_merge() is True


def test_zone_pool_by_type():
    """ZonePool.by_type returns zones of specified type."""
    pool = ZonePool()
    pool.add(Zone(
        id="z1",
        map="m10",
        name="Zone 1",
        type=ZoneType.LEGACY_DUNGEON,
        weight=10,
    ))
    pool.add(Zone(
        id="z2",
        map="m30",
        name="Zone 2",
        type=ZoneType.CATACOMB_SHORT,
        weight=4,
    ))
    pool.add(Zone(
        id="z3",
        map="m30",
        name="Zone 3",
        type=ZoneType.CATACOMB_SHORT,
        weight=5,
    ))

    assert len(pool.by_type(ZoneType.LEGACY_DUNGEON)) == 1
    assert len(pool.by_type(ZoneType.CATACOMB_SHORT)) == 2
    assert len(pool.by_type(ZoneType.CAVE_MEDIUM)) == 0
```

**Step 10: Run test to verify it fails**

Run: `cd core && python -m pytest tests/test_zones.py::test_zone_pool_from_toml -v`
Expected: FAIL with "cannot import name 'ZonePool'"

**Step 11: Write ZonePool implementation**

Add to `core/speedfog_core/zones.py`:

```python
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

    @classmethod
    def from_toml(cls, path: Path) -> "ZonePool":
        """Load zone pool from TOML file."""
        with open(path, "rb") as f:
            data = tomllib.load(f)

        pool = cls()
        for zone_data in data.get("zones", []):
            zone = Zone.from_dict(zone_data)
            pool.add(zone)

        return pool


def load_zones(path: Path) -> ZonePool:
    """Load zones from file."""
    if not path.exists():
        raise FileNotFoundError(f"Zones file not found: {path}")
    return ZonePool.from_toml(path)
```

**Step 12: Run tests to verify they pass**

Run: `cd core && python -m pytest tests/test_zones.py -v`
Expected: All 4 tests PASS

**Step 13: Commit**

```bash
git add core/speedfog_core/zones.py core/tests/test_zones.py
git commit -m "feat(core): implement zone parser with ZoneType and ZonePool"
```

---

## Task 4: Create FogRando Conversion Script

**Files:**
- Create: `tools/convert_fogrando.py`

**Step 1: Create tools directory**

```bash
mkdir -p tools
```

**Step 2: Write the conversion script**

```python
#!/usr/bin/env python3
"""
Convert FogRando fog.txt to SpeedFog zones.toml

Usage:
    python convert_fogrando.py <fog.txt> <output.toml>
    python convert_fogrando.py reference/fogrando-data/fog.txt core/zones.toml
"""

import sys
from dataclasses import dataclass
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
    tags: list[str]


def load_fog_txt(path: Path) -> dict:
    """Load and parse FogRando's fog.txt YAML file."""
    with open(path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def derive_zone_type(area: dict) -> str | None:
    """
    Derive zone type from area data.
    Returns None if zone should be excluded.
    """
    tags_str = area.get("Tags", "")
    tags = tags_str.lower().split() if tags_str else []
    maps = area.get("Maps", "")
    first_map = maps.split()[0] if maps else ""

    # Exclusion rules
    if "overworld" in tags:
        return None
    if "trivial" in tags:
        return None
    if "dlc" in tags:
        return None  # v1: exclude DLC

    # Specific exclusions by name
    name = area.get("Name", "").lower()
    excluded_patterns = [
        "ainsel",
        "deeproot",
        "lakeofrot",
        "lake_of_rot",
        "shunning",
        "sewers",
    ]
    if any(x in name for x in excluded_patterns):
        return None

    # Type derivation by tags first
    if "legacy" in tags:
        return "legacy_dungeon"
    if "start" in tags:
        return "start"

    # Type derivation by map prefix
    if first_map.startswith("m30"):
        return "catacomb"
    if first_map.startswith("m31"):
        return "cave"
    if first_map.startswith("m32"):
        return "tunnel"
    if first_map.startswith("m39"):
        return "gaol"

    # Boss arena (has DefeatFlag but not a minidungeon)
    if area.get("DefeatFlag") and "minidungeon" not in tags:
        return "boss_arena"

    # Generic minidungeon
    if "minidungeon" in tags:
        return "mini_dungeon"

    return None  # Unknown, exclude


def extract_boss_name(area: dict) -> str:
    """Extract boss name from DebugInfo if present."""
    debug_info = area.get("DebugInfo", "")
    if " - " in debug_info:
        # Format: "DefeatFlag: 12345 - m10_00_00_00 - Boss Name"
        parts = debug_info.split(" - ")
        if len(parts) >= 3:
            return parts[-1].strip().strip("'\"")
    return ""


def convert_area_to_zone(area: dict) -> Zone | None:
    """Convert a FogRando area to a SpeedFog zone."""
    zone_type = derive_zone_type(area)
    if zone_type is None:
        return None

    maps = area.get("Maps", "")
    first_map = maps.split()[0] if maps else ""

    tags_str = area.get("Tags", "")
    tags = tags_str.split() if tags_str else []

    return Zone(
        id=area.get("Name", ""),
        map=first_map,
        name=area.get("Text", ""),
        type=zone_type,
        weight=0,  # Manual assignment required
        fog_count=0,  # Manual assignment required
        boss=extract_boss_name(area),
        tags=tags,
    )


def zones_to_toml(zones: list[Zone]) -> str:
    """Convert zones to TOML format string."""
    lines = [
        "# SpeedFog Zone Data",
        "# Auto-generated from FogRando fog.txt",
        "#",
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
        "start",
        "legacy_dungeon",
        "boss_arena",
        "catacomb",
        "cave",
        "tunnel",
        "gaol",
        "mini_dungeon",
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
            lines.append(f"weight = {zone.weight}  # TODO: estimate minutes")
            lines.append(f"fog_count = {zone.fog_count}  # TODO: 2=linear, 3=split/merge")
            if zone.boss:
                lines.append(f'boss = "{zone.boss}"')
            if zone.tags:
                tags_str = ", ".join(f'"{t}"' for t in zone.tags)
                lines.append(f"tags = [{tags_str}]")
            lines.append("")

    return "\n".join(lines)


def main() -> None:
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

    areas = data.get("Areas", [])
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
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(toml_content, encoding="utf-8")
    print(f"Written to {output_path}")

    # Summary by type
    print("\nSummary by type:")
    by_type: dict[str, int] = {}
    for zone in zones:
        by_type[zone.type] = by_type.get(zone.type, 0) + 1
    for t, count in sorted(by_type.items()):
        print(f"  {t}: {count}")


if __name__ == "__main__":
    main()
```

**Step 3: Test the script**

Run: `python tools/convert_fogrando.py reference/fogrando-data/fog.txt core/zones.toml`
Expected: Script runs and outputs zone summary by type

**Step 4: Verify output file exists**

Run: `head -50 core/zones.toml`
Expected: See TOML header and first zones

**Step 5: Commit**

```bash
git add tools/convert_fogrando.py
git commit -m "feat(tools): add FogRando to zones.toml conversion script"
```

---

## Task 5: Create Warp Extraction Script

**Files:**
- Create: `tools/extract_warps.py`
- Create: `data/` directory

**Step 1: Create data directory**

```bash
mkdir -p data
```

**Step 2: Write the warp extraction script**

```python
#!/usr/bin/env python3
"""
Extract fog gate warp data from FogRando fog.txt.

Usage:
    python extract_warps.py <fog.txt> <output.json>
    python extract_warps.py reference/fogrando-data/fog.txt data/zone_warps.json
"""

import json
import sys
from pathlib import Path

import yaml


def extract_warps(fog_path: Path) -> dict:
    """Extract warp data from FogRando fog.txt."""
    with open(fog_path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)

    # Build area map lookup
    areas = {a["Name"]: a for a in data.get("Areas", [])}

    warps: dict[str, dict] = {}

    for entrance in data.get("Entrances", []):
        # Get zone IDs from ASide and BSide
        aside = entrance.get("ASide", {})
        bside = entrance.get("BSide", {})

        # Extract area names from both sides
        for side_data in [aside, bside]:
            area_id = side_data.get("Area", "")
            if not area_id or area_id.startswith("m"):
                # Skip map IDs, we want area names
                continue

            if area_id not in warps:
                area_data = areas.get(area_id, {})
                maps = area_data.get("Maps", "")
                first_map = maps.split()[0] if maps else ""
                warps[area_id] = {
                    "map": first_map,
                    "fogs": [],
                }

            # Add fog gate data
            fog_data = {
                "id": entrance.get("Name", ""),
                "entity_id": entrance.get("ID", 0),
                "text": entrance.get("Text", ""),
            }

            # Only add if not already present (avoid duplicates)
            existing_ids = [f["id"] for f in warps[area_id]["fogs"]]
            if fog_data["id"] not in existing_ids:
                warps[area_id]["fogs"].append(fog_data)

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
```

**Step 3: Test the script**

Run: `python tools/extract_warps.py reference/fogrando-data/fog.txt data/zone_warps.json`
Expected: Script runs and outputs fog gate count

**Step 4: Verify output**

Run: `head -50 data/zone_warps.json`
Expected: See JSON with zone warp data

**Step 5: Commit**

```bash
git add tools/extract_warps.py data/
git commit -m "feat(tools): add fog gate warp extraction script"
```

---

## Task 6: Create Validation Script

**Files:**
- Create: `tools/validate_zones.py`

**Step 1: Write the validation script**

```python
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

    with open(warps_path, "r") as f:
        warps = json.load(f)

    zones_list = zones_data.get("zones", [])
    zone_ids = {z["id"] for z in zones_list}
    warp_ids = set(warps.keys())

    # Zones without warp data (warning, not error for now)
    missing_warps = zone_ids - warp_ids
    for z in sorted(missing_warps):
        errors.append(f"WARNING: Zone '{z}' has no warp data in zone_warps.json")

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
```

**Step 2: Test the validation script**

Run: `python tools/validate_zones.py core/zones.toml data/zone_warps.json`
Expected: Script runs (will show warnings/errors since zones.toml needs manual enrichment)

**Step 3: Commit**

```bash
git add tools/validate_zones.py
git commit -m "feat(tools): add zone validation script"
```

---

## Task 7: Run Full Pipeline and Manual Zone Enrichment

**Files:**
- Modify: `core/zones.toml` (manual enrichment)

**Step 1: Run full extraction pipeline**

```bash
python tools/convert_fogrando.py reference/fogrando-data/fog.txt core/zones.toml
python tools/extract_warps.py reference/fogrando-data/fog.txt data/zone_warps.json
```

**Step 2: Run validation to see current state**

Run: `python tools/validate_zones.py`
Expected: Many errors about weight=0 and fog_count=0

**Step 3: Manual enrichment of zones.toml**

Edit `core/zones.toml` to add weight and fog_count values for at least:
- All legacy dungeons
- 10+ catacombs
- 10+ caves
- All tunnels and gaols
- 10+ boss arenas

Use these guidelines:
| Zone Type | Weight Range | fog_count |
|-----------|--------------|-----------|
| legacy_dungeon | 12-20 | 3 (can split/merge) |
| catacomb_short | 3-4 | 2 |
| catacomb_medium | 5-7 | 2 |
| catacomb_long | 8-10 | 2 |
| cave_short | 3-4 | 2 |
| cave_medium | 5-7 | 2 |
| cave_long | 8-10 | 2 |
| tunnel | 3-6 | 2 |
| gaol | 2-4 | 2 |
| boss_arena | 2-5 | 2 |

**Step 4: Re-run validation**

Run: `python tools/validate_zones.py`
Expected: Fewer errors, mostly warnings about missing warp data

**Step 5: Run all tests**

Run: `cd core && python -m pytest -v`
Expected: All tests pass

**Step 6: Commit enriched zones**

```bash
git add core/zones.toml data/zone_warps.json
git commit -m "data: add enriched zone data with weights and fog counts"
```

---

## Task 8: Final Verification

**Files:** None (verification only)

**Step 1: Verify project structure**

Run: `ls -la core/ tools/ data/`
Expected:
```
core/
  pyproject.toml
  README.md
  speedfog_core/
    __init__.py
    config.py
    zones.py
  tests/
    test_config.py
    test_zones.py
  zones.toml

tools/
  convert_fogrando.py
  extract_warps.py
  validate_zones.py

data/
  zone_warps.json
```

**Step 2: Verify package installation**

Run: `cd core && pip install -e . && python -c "from speedfog_core import config, zones; print('OK')"`
Expected: OK

**Step 3: Run full test suite**

Run: `cd core && python -m pytest -v --tb=short`
Expected: All tests pass

**Step 4: Run validation**

Run: `python tools/validate_zones.py`
Expected: Pass with only warnings (no errors)

**Step 5: Final commit**

```bash
git add -A
git commit -m "feat(phase-1): complete foundations implementation"
```

---

## Acceptance Criteria Checklist

- [ ] `core/pyproject.toml` exists and `pip install -e .` succeeds
- [ ] `core/README.md` exists with basic usage instructions
- [ ] `Config.from_toml()` parses example config correctly
- [ ] Default values work when config sections are missing
- [ ] `BudgetConfig.min_weight` and `.max_weight` compute correctly
- [ ] `ZonePool.from_toml()` loads zones correctly
- [ ] `ZoneType.from_string()` handles all expected types
- [ ] Filter methods (`by_type`, `legacy_dungeons`, `mini_dungeons`) work correctly
- [ ] `convert_fogrando.py` runs without errors
- [ ] `extract_warps.py` runs without errors
- [ ] `validate_zones.py` validates zone data
- [ ] `zones.toml` has weights assigned to key zones
- [ ] `zones.toml` has fog_count assigned to key zones
- [ ] All pytest tests pass

---

## Next Phase

After completing Phase 1, proceed to [Phase 2: DAG Generation](./phase-2-dag-generation.md).
