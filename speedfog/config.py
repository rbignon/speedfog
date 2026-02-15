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
    final_tier: int = 28  # Enemy scaling tier for final boss (1-28)

    def __post_init__(self) -> None:
        """Validate structure configuration."""
        if self.max_branches < 1:
            raise ValueError(f"max_branches must be >= 1, got {self.max_branches}")
        if self.max_parallel_paths < 1:
            raise ValueError(
                f"max_parallel_paths must be >= 1, got {self.max_parallel_paths}"
            )
        if self.max_branches >= 2 and self.max_parallel_paths < 2:
            raise ValueError(
                f"max_parallel_paths must be >= 2 when max_branches >= 2, "
                f"got max_parallel_paths={self.max_parallel_paths}"
            )
        if not isinstance(self.final_tier, int):
            raise TypeError(
                f"final_tier must be int, got {type(self.final_tier).__name__}"
            )
        if self.final_tier < 1 or self.final_tier > 28:
            raise ValueError(f"final_tier must be 1-28, got {self.final_tier}")

    @property
    def effective_final_boss_candidates(self) -> list[str]:
        """Return candidates or default if empty."""
        return self.final_boss_candidates or ["leyndell_erdtree", "enirilim_radahn"]


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
    output_dir: str = "./seeds"
    platform: str | None = None  # None = auto-detect, "windows", "linux"


@dataclass
class StartingItemsConfig:
    """Starting items given when picking up the Tarnished's Wizened Finger.

    These items are awarded via DirectlyGivePlayerItem using Good IDs.
    Good IDs are from fog.txt KeyItems section (format: 3:XXXX where 3=Goods).
    """

    # Key items for progression shortcuts
    academy_key: bool = True  # Academy Glintstone Key (Good ID 8109)
    pureblood_medal: bool = False  # Pureblood Knight's Medal (Good ID 2160)
    drawing_room_key: bool = True  # Drawing-Room Key for Volcano Manor (Good ID 8134)
    lantern: bool = True  # Lantern (Good ID 2070) - hands-free light source
    whetblades: bool = (
        True  # Whetstone Knife + all Whetblades (Good IDs 8590, 8970-8974)
    )

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

    # DLC key items
    omother: bool = True  # O, Mother (Good ID 2009004)
    welldepthskey: bool = True  # Well Depths Key (Good ID 2008004)
    gaolupperlevelkey: bool = True  # Gaol Upper Level Key (Good ID 2008005)
    gaollowerlevelkey: bool = True  # Gaol Lower Level Key (Good ID 2008006)
    holeladennecklace: bool = True  # Hole-Laden Necklace (Good ID 2008008)
    messmerskindling: bool = True  # Messmer's Kindling (Good ID 2008021)

    # Talisman pouches (expand equip slots)
    talisman_pouches: int = 3  # Talisman Pouches (Good ID 10040) - +1 slot each, max 3

    # Consumable starting resources
    golden_seeds: int = 0  # Golden Seeds (Good ID 10010) - upgrade flask uses
    sacred_tears: int = 0  # Sacred Tears (Good ID 10020) - upgrade flask potency
    starting_runes: int = 0  # Runes added to starting character via CharaInitParam
    larval_tears: int = 10  # Larval Tears (Good ID 8185) - for rebirth at graces

    def __post_init__(self) -> None:
        """Validate starting items configuration."""
        if self.talisman_pouches < 0 or self.talisman_pouches > 3:
            raise ValueError(
                f"talisman_pouches must be 0-3, got {self.talisman_pouches}"
            )
        if self.golden_seeds < 0 or self.golden_seeds > 99:
            raise ValueError(f"golden_seeds must be 0-99, got {self.golden_seeds}")
        if self.sacred_tears < 0 or self.sacred_tears > 12:
            raise ValueError(f"sacred_tears must be 0-12, got {self.sacred_tears}")
        if self.starting_runes < 0 or self.starting_runes > 10_000_000:
            raise ValueError(
                f"starting_runes must be 0-10000000, got {self.starting_runes}"
            )
        if self.larval_tears < 0 or self.larval_tears > 99:
            raise ValueError(f"larval_tears must be 0-99, got {self.larval_tears}")

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
        if self.lantern:
            goods.append(2070)  # Lantern
        if self.whetblades:
            goods.extend(
                [
                    8590,  # Whetstone Knife
                    8970,  # Iron Whetblade (Heavy, Keen, Quality)
                    8971,  # Red-Hot Whetblade (Fire, Flame Art)
                    8972,  # Sanctified Whetblade (Lightning, Sacred)
                    8973,  # Glintstone Whetblade (Magic, Cold)
                    8974,  # Black Whetblade (Poison, Blood, Occult)
                ]
            )

        # DLC key items
        if self.omother:
            goods.append(2009004)  # O, Mother
        if self.welldepthskey:
            goods.append(2008004)  # Well Depths Key
        if self.gaolupperlevelkey:
            goods.append(2008005)  # Gaol Upper Level Key
        if self.gaollowerlevelkey:
            goods.append(2008006)  # Gaol Lower Level Key
        if self.holeladennecklace:
            goods.append(2008008)  # Hole-Laden Necklace
        if self.messmerskindling:
            goods.append(2008021)  # Messmer's Kindling

        # Talisman Pouches (+1 equip slot each, max 3)
        for _ in range(self.talisman_pouches):
            goods.append(10040)  # Talisman Pouch

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
    auto_upgrade_dropped: bool = True
    reduce_upgrade_cost: bool = True
    dlc: bool = True

    def __post_init__(self) -> None:
        """Validate configuration."""
        if self.difficulty < 0 or self.difficulty > 100:
            raise ValueError(f"difficulty must be 0-100, got {self.difficulty}")


@dataclass
class CarePackageConfig:
    """Care package configuration for randomized starting builds.

    Items are sampled from data/care_package_items.toml per category.
    Weapon upgrade level controls how upgraded starting weapons are.
    """

    enabled: bool = False
    weapon_upgrade: int = 8  # Standard upgrade level (0-25)
    weapons: int = 5
    shields: int = 2
    catalysts: int = 2
    talismans: int = 4
    sorceries: int = 5
    incantations: int = 5
    head_armor: int = 2
    body_armor: int = 2
    arm_armor: int = 2
    leg_armor: int = 2
    crystal_tears: int = 5
    ashes_of_war: int = 0

    def __post_init__(self) -> None:
        """Validate care package configuration."""
        if self.weapon_upgrade < 0 or self.weapon_upgrade > 25:
            raise ValueError(f"weapon_upgrade must be 0-25, got {self.weapon_upgrade}")
        count_fields = [
            "weapons",
            "shields",
            "catalysts",
            "talismans",
            "sorceries",
            "incantations",
            "head_armor",
            "body_armor",
            "arm_armor",
            "leg_armor",
            "crystal_tears",
            "ashes_of_war",
        ]
        for field_name in count_fields:
            value = getattr(self, field_name)
            if value < 0:
                raise ValueError(f"{field_name} must be >= 0, got {value}")


@dataclass
class Config:
    """Main configuration container."""

    seed: int = 0
    run_complete_message: str = "RUN COMPLETE"
    chapel_grace: bool = True
    budget: BudgetConfig = field(default_factory=BudgetConfig)
    requirements: RequirementsConfig = field(default_factory=RequirementsConfig)
    structure: StructureConfig = field(default_factory=StructureConfig)
    paths: PathsConfig = field(default_factory=PathsConfig)
    starting_items: StartingItemsConfig = field(default_factory=StartingItemsConfig)
    item_randomizer: ItemRandomizerConfig = field(default_factory=ItemRandomizerConfig)
    care_package: CarePackageConfig = field(default_factory=CarePackageConfig)

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
        care_package_section = data.get("care_package", {})

        return cls(
            seed=run_section.get("seed", 0),
            run_complete_message=run_section.get(
                "run_complete_message", "RUN COMPLETE"
            ),
            chapel_grace=run_section.get("chapel_grace", True),
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
                final_tier=structure_section.get("final_tier", 28),
            ),
            paths=PathsConfig(
                game_dir=paths_section.get("game_dir", ""),
                output_dir=paths_section.get("output_dir", "./seeds"),
                platform=paths_section.get("platform"),
            ),
            starting_items=StartingItemsConfig(
                academy_key=starting_items_section.get("academy_key", True),
                pureblood_medal=starting_items_section.get("pureblood_medal", False),
                drawing_room_key=starting_items_section.get("drawing_room_key", True),
                lantern=starting_items_section.get("lantern", True),
                whetblades=starting_items_section.get("whetblades", True),
                great_runes=starting_items_section.get("great_runes", True),
                rune_godrick=starting_items_section.get("rune_godrick", True),
                rune_radahn=starting_items_section.get("rune_radahn", True),
                rune_morgott=starting_items_section.get("rune_morgott", True),
                rune_mohg=starting_items_section.get("rune_mohg", True),
                rune_rykard=starting_items_section.get("rune_rykard", True),
                rune_malenia=starting_items_section.get("rune_malenia", True),
                omother=starting_items_section.get("omother", True),
                welldepthskey=starting_items_section.get("welldepthskey", True),
                gaolupperlevelkey=starting_items_section.get("gaolupperlevelkey", True),
                gaollowerlevelkey=starting_items_section.get("gaollowerlevelkey", True),
                holeladennecklace=starting_items_section.get("holeladennecklace", True),
                messmerskindling=starting_items_section.get("messmerskindling", True),
                talisman_pouches=starting_items_section.get("talisman_pouches", 3),
                golden_seeds=starting_items_section.get("golden_seeds", 0),
                sacred_tears=starting_items_section.get("sacred_tears", 0),
                starting_runes=starting_items_section.get("starting_runes", 0),
                larval_tears=starting_items_section.get("larval_tears", 10),
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
                auto_upgrade_dropped=item_randomizer_section.get(
                    "auto_upgrade_dropped", True
                ),
                reduce_upgrade_cost=item_randomizer_section.get(
                    "reduce_upgrade_cost", True
                ),
                dlc=item_randomizer_section.get("dlc", True),
            ),
            care_package=CarePackageConfig(
                enabled=care_package_section.get("enabled", False),
                weapon_upgrade=care_package_section.get("weapon_upgrade", 8),
                weapons=care_package_section.get("weapons", 5),
                shields=care_package_section.get("shields", 2),
                catalysts=care_package_section.get("catalysts", 2),
                talismans=care_package_section.get("talismans", 4),
                sorceries=care_package_section.get("sorceries", 5),
                incantations=care_package_section.get("incantations", 5),
                head_armor=care_package_section.get("head_armor", 2),
                body_armor=care_package_section.get("body_armor", 2),
                arm_armor=care_package_section.get("arm_armor", 2),
                leg_armor=care_package_section.get("leg_armor", 2),
                crystal_tears=care_package_section.get("crystal_tears", 5),
                ashes_of_war=care_package_section.get("ashes_of_war", 0),
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
