"""Zone data parsing for SpeedFog."""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
from enum import Enum, auto
from pathlib import Path
from typing import Any

# Use tomllib (Python 3.11+) with fallback to tomli
if sys.version_info >= (3, 11):
    import tomllib
else:
    try:
        import tomli as tomllib
    except ImportError as e:
        raise ImportError(
            "tomli is required for Python < 3.11. Install with: pip install tomli"
        ) from e


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
    def from_string(cls, s: str) -> ZoneType:
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


@dataclass
class Zone:
    """Represents a game zone/area."""

    id: str
    map: str
    name: str
    type: ZoneType
    weight: int
    fog_count: int = 2
    boss: str = ""
    tags: list[str] = field(default_factory=list)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Zone:
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
    def from_toml(cls, path: Path) -> ZonePool:
        """Load zone pool from TOML file."""
        with open(path, "rb") as f:
            data = tomllib.load(f)

        pool = cls()
        for zone_data in data.get("zones", []):
            zone = Zone.from_dict(zone_data)
            pool.add(zone)

        return pool


def load_zones(path: Path) -> ZonePool:
    """Load zones from file.

    This is a convenience function that wraps ZonePool.from_toml().

    Args:
        path: Path to the TOML zones file.

    Returns:
        Parsed ZonePool object.

    Raises:
        FileNotFoundError: If the zones file does not exist.
    """
    if not path.exists():
        raise FileNotFoundError(f"Zones file not found: {path}")
    return ZonePool.from_toml(path)
