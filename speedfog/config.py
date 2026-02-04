"""Configuration parsing for SpeedFog."""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
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


@dataclass
class BudgetConfig:
    """Path budget configuration."""

    total_weight: int = 30
    tolerance: int = 5

    @property
    def min_weight(self) -> int:
        """Minimum acceptable path weight."""
        return self.total_weight - self.tolerance

    @property
    def max_weight(self) -> int:
        """Maximum acceptable path weight."""
        return self.total_weight + self.tolerance


@dataclass
class RequirementsConfig:
    """Zone requirements configuration."""

    legacy_dungeons: int = 1
    bosses: int = 5
    mini_dungeons: int = 5


@dataclass
class StructureConfig:
    """DAG structure configuration."""

    max_parallel_paths: int = 3
    min_layers: int = 6
    max_layers: int = 10
    split_probability: float = 0.3
    merge_probability: float = 0.3
    max_branches: int = 3
    first_layer_type: str | None = None
    major_boss_ratio: float = 0.0
    final_boss_candidates: list[str] = field(default_factory=list)

    @property
    def effective_final_boss_candidates(self) -> list[str]:
        """Return candidates or default if empty."""
        return self.final_boss_candidates or ["leyndell_erdtree"]


def resolve_final_boss_candidates(
    candidates: list[str], all_boss_zones: set[str]
) -> list[str]:
    """Expand 'all' keyword to all major/final boss zones.

    Args:
        candidates: List of zone names, may include 'all' keyword.
        all_boss_zones: Set of all valid boss zone names.

    Returns:
        List of zone names with 'all' expanded to actual zones.
    """
    if "all" in candidates:
        return sorted(all_boss_zones)
    return candidates


@dataclass
class PathsConfig:
    """File paths configuration."""

    game_dir: str = ""
    output_dir: str = "./output"
    clusters_file: str = "./data/clusters.json"
    randomizer_dir: str | None = None
    platform: str | None = None  # None = auto-detect, "windows", "linux"


@dataclass
class StartingItemsConfig:
    """Starting items given when picking up the Tarnished's Wizened Finger.

    These items are awarded via DirectlyGivePlayerItem using Good IDs.
    Good IDs are from fog.txt KeyItems section (format: 3:XXXX where 3=Goods).
    """

    # Key items for progression shortcuts
    academy_key: bool = True  # Academy Glintstone Key (Good ID 8109)
    pureblood_medal: bool = True  # Pureblood Knight's Medal (Good ID 2160)
    drawing_room_key: bool = True  # Drawing-Room Key for Volcano Manor (Good ID 8134)

    # Great Runes (restored versions, equippable at graces)
    # Restored Great Runes have Good IDs 191-196 (not the boss drop versions 8148-8153)
    great_runes: bool = True  # All Great Runes below
    # Individual Great Runes (only used if great_runes=False)
    rune_godrick: bool = True  # Good ID 191 (restored)
    rune_radahn: bool = True  # Good ID 192 (restored)
    rune_morgott: bool = True  # Good ID 193 (restored)
    rune_rykard: bool = True  # Good ID 194 (restored)
    rune_mohg: bool = True  # Good ID 195 (restored)
    rune_malenia: bool = True  # Good ID 196 (restored)

    # Consumable starting resources
    golden_seeds: int = 0  # Golden Seeds (Good ID 10010) - upgrade flask uses
    sacred_tears: int = 0  # Sacred Tears (Good ID 10020) - upgrade flask potency
    starting_runes: int = 0  # Runes added to starting character via CharaInitParam

    def __post_init__(self) -> None:
        """Validate starting items configuration."""
        if self.golden_seeds < 0 or self.golden_seeds > 99:
            raise ValueError(f"golden_seeds must be 0-99, got {self.golden_seeds}")
        if self.sacred_tears < 0 or self.sacred_tears > 12:
            raise ValueError(f"sacred_tears must be 0-12, got {self.sacred_tears}")
        if self.starting_runes < 0 or self.starting_runes > 10_000_000:
            raise ValueError(
                f"starting_runes must be 0-10000000, got {self.starting_runes}"
            )

    def get_item_lots(self) -> list[int]:
        """Get list of ItemLot IDs to award at game start.

        DEPRECATED: Use get_starting_goods() instead. ItemLots are randomized
        by the Item Randomizer, so using AwardItemLot gives wrong items.
        """
        lots: list[int] = []

        if self.academy_key:
            lots.append(1034450100)
        if self.pureblood_medal:
            lots.append(100320)

        # Great Runes
        if self.great_runes:
            # Add all Great Runes
            lots.extend(
                [
                    34100500,  # Godrick
                    34130050,  # Radahn
                    34140700,  # Morgott
                    34140710,  # Mohg
                    34120500,  # Rykard
                    34150000,  # Malenia
                ]
            )
        else:
            # Add individual Great Runes based on flags
            if self.rune_godrick:
                lots.append(34100500)
            if self.rune_radahn:
                lots.append(34130050)
            if self.rune_morgott:
                lots.append(34140700)
            if self.rune_mohg:
                lots.append(34140710)
            if self.rune_rykard:
                lots.append(34120500)
            if self.rune_malenia:
                lots.append(34150000)

        return lots

    def get_starting_goods(self) -> list[int]:
        """Get list of Good IDs to award at game start.

        Uses DirectlyGivePlayerItem which is not affected by Item Randomizer.
        Good IDs are from fog.txt KeyItems section (format: 3:XXXX where 3=Goods).
        """
        goods: list[int] = []

        # Key items for progression shortcuts
        if self.academy_key:
            goods.append(8109)  # Academy Glintstone Key
        if self.pureblood_medal:
            goods.append(2160)  # Pureblood Knight's Medal
        if self.drawing_room_key:
            goods.append(8134)  # Drawing-Room Key (Volcano Manor)

        # Great Runes (RESTORED versions - Good IDs 191-196)
        # These are the activated/restored versions, equippable at Graces
        # NOT the boss drop versions (8148-8153) which need Divine Tower activation
        if self.great_runes:
            goods.extend(
                [
                    191,  # Godrick's Great Rune (restored)
                    192,  # Radahn's Great Rune (restored)
                    193,  # Morgott's Great Rune (restored)
                    194,  # Rykard's Great Rune (restored)
                    195,  # Mohg's Great Rune (restored)
                    196,  # Malenia's Great Rune (restored)
                ]
            )
        else:
            if self.rune_godrick:
                goods.append(191)
            if self.rune_radahn:
                goods.append(192)
            if self.rune_morgott:
                goods.append(193)
            if self.rune_rykard:
                goods.append(194)
            if self.rune_mohg:
                goods.append(195)
            if self.rune_malenia:
                goods.append(196)

        return goods


@dataclass
class ItemRandomizerConfig:
    """Item Randomizer configuration."""

    enabled: bool = True
    difficulty: int = 50
    remove_requirements: bool = True
    auto_upgrade_weapons: bool = True

    def __post_init__(self) -> None:
        """Validate configuration."""
        if self.difficulty < 0 or self.difficulty > 100:
            raise ValueError(f"difficulty must be 0-100, got {self.difficulty}")


@dataclass
class Config:
    """Main configuration container."""

    seed: int = 0
    budget: BudgetConfig = field(default_factory=BudgetConfig)
    requirements: RequirementsConfig = field(default_factory=RequirementsConfig)
    structure: StructureConfig = field(default_factory=StructureConfig)
    paths: PathsConfig = field(default_factory=PathsConfig)
    starting_items: StartingItemsConfig = field(default_factory=StartingItemsConfig)
    item_randomizer: ItemRandomizerConfig = field(default_factory=ItemRandomizerConfig)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Config:
        """Create Config from a dictionary (e.g., parsed TOML)."""
        run_section = data.get("run", {})
        budget_section = data.get("budget", {})
        requirements_section = data.get("requirements", {})
        structure_section = data.get("structure", {})
        paths_section = data.get("paths", {})
        starting_items_section = data.get("starting_items", {})
        item_randomizer_section = data.get("item_randomizer", {})

        return cls(
            seed=run_section.get("seed", 0),
            budget=BudgetConfig(
                total_weight=budget_section.get("total_weight", 30),
                tolerance=budget_section.get("tolerance", 5),
            ),
            requirements=RequirementsConfig(
                legacy_dungeons=requirements_section.get("legacy_dungeons", 1),
                bosses=requirements_section.get("bosses", 5),
                mini_dungeons=requirements_section.get("mini_dungeons", 5),
            ),
            structure=StructureConfig(
                max_parallel_paths=structure_section.get("max_parallel_paths", 3),
                min_layers=structure_section.get("min_layers", 6),
                max_layers=structure_section.get("max_layers", 10),
                split_probability=structure_section.get("split_probability", 0.3),
                merge_probability=structure_section.get("merge_probability", 0.3),
                max_branches=structure_section.get("max_branches", 3),
                first_layer_type=structure_section.get("first_layer_type"),
                major_boss_ratio=structure_section.get("major_boss_ratio", 0.0),
                final_boss_candidates=structure_section.get(
                    "final_boss_candidates", []
                ),
            ),
            paths=PathsConfig(
                game_dir=paths_section.get("game_dir", ""),
                output_dir=paths_section.get("output_dir", "./output"),
                clusters_file=paths_section.get(
                    "clusters_file", "./data/clusters.json"
                ),
                randomizer_dir=paths_section.get("randomizer_dir"),
                platform=paths_section.get("platform"),
            ),
            starting_items=StartingItemsConfig(
                academy_key=starting_items_section.get("academy_key", True),
                pureblood_medal=starting_items_section.get("pureblood_medal", True),
                drawing_room_key=starting_items_section.get("drawing_room_key", True),
                great_runes=starting_items_section.get("great_runes", True),
                rune_godrick=starting_items_section.get("rune_godrick", True),
                rune_radahn=starting_items_section.get("rune_radahn", True),
                rune_morgott=starting_items_section.get("rune_morgott", True),
                rune_mohg=starting_items_section.get("rune_mohg", True),
                rune_rykard=starting_items_section.get("rune_rykard", True),
                rune_malenia=starting_items_section.get("rune_malenia", True),
                golden_seeds=starting_items_section.get("golden_seeds", 0),
                sacred_tears=starting_items_section.get("sacred_tears", 0),
                starting_runes=starting_items_section.get("starting_runes", 0),
            ),
            item_randomizer=ItemRandomizerConfig(
                enabled=item_randomizer_section.get("enabled", True),
                difficulty=item_randomizer_section.get("difficulty", 50),
                remove_requirements=item_randomizer_section.get(
                    "remove_requirements", True
                ),
                auto_upgrade_weapons=item_randomizer_section.get(
                    "auto_upgrade_weapons", True
                ),
            ),
        )

    @classmethod
    def from_toml(cls, path: str | Path) -> Config:
        """Load configuration from a TOML file."""
        path = Path(path)
        with path.open("rb") as f:
            data = tomllib.load(f)
        return cls.from_dict(data)


def load_config(path: str | Path) -> Config:
    """Load configuration from a TOML file.

    This is a convenience function that wraps Config.from_toml().

    Args:
        path: Path to the TOML configuration file.

    Returns:
        Parsed Config object.
    """
    return Config.from_toml(path)
