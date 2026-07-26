"""Configuration parsing for SpeedFog."""

from __future__ import annotations

import random
import tomllib
import warnings
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from speedfog.constants import (
    DEFAULT_MAX_LAYER_SPREAD,
    INTERMEDIATE_CLUSTER_TYPES,
    MAX_TIER,
)

_VALID_CLUSTER_TYPES = INTERMEDIATE_CLUSTER_TYPES

_CLUSTER_TYPE_TO_FIELD = {
    "legacy_dungeon": "legacy_dungeons",
    "mini_dungeon": "mini_dungeons",
    "boss_arena": "bosses",
    "major_boss": "major_bosses",
}


@dataclass
class RequirementsConfig:
    """Zone requirements configuration."""

    legacy_dungeons: int = 1
    bosses: int = 5
    mini_dungeons: int = 5
    major_bosses: int = 8
    zones: list[str] = field(default_factory=list)
    exclude_zones: list[str] = field(default_factory=list)
    allowed_types: list[str] = field(default_factory=lambda: list(_VALID_CLUSTER_TYPES))

    def __post_init__(self) -> None:
        if not self.allowed_types:
            raise ValueError("allowed_types must be non-empty")
        seen: set[str] = set()
        for t in self.allowed_types:
            if t not in _VALID_CLUSTER_TYPES:
                raise ValueError(
                    f"invalid cluster type in allowed_types: {t!r} "
                    f"(valid: {_VALID_CLUSTER_TYPES})"
                )
            if t in seen:
                raise ValueError(f"duplicate entry in allowed_types: {t!r}")
            seen.add(t)

        # Warn about non-zero minima on excluded types
        for cluster_type, field_name in _CLUSTER_TYPE_TO_FIELD.items():
            if cluster_type in self.allowed_types:
                continue
            value = getattr(self, field_name)
            if value > 0:
                warnings.warn(
                    f"requirements.{field_name} = {value} ignored: "
                    f"'{cluster_type}' not in allowed_types",
                    UserWarning,
                    stacklevel=2,
                )

    def required_count(self, cluster_type: str) -> int:
        """Return minimum count for a cluster type, 0 if excluded."""
        if cluster_type not in self.allowed_types:
            return 0
        field_name = _CLUSTER_TYPE_TO_FIELD[cluster_type]
        return int(getattr(self, field_name))


@dataclass
class StructureConfig:
    """DAG structure configuration."""

    max_parallel_paths: int = 3
    max_exits: int = 3  # Split fan-out
    max_entrances: int = 3  # Merge fan-in
    first_layer_type: str | None = None
    final_boss_candidates: dict[str, int] = field(default_factory=dict)
    start_tier: int = 1  # Enemy scaling tier for first layer (1-MAX_TIER)
    final_tier: int = MAX_TIER  # Enemy scaling tier for final boss (1-MAX_TIER)
    tier_curve: str = "linear"  # "linear" or "power"
    tier_curve_exponent: float = 0.6  # Power curve exponent (only for "power")
    max_weight_tolerance: float = (
        3.0  # Soft preference radius around the anchor weight (0=disabled).
        # Must be a non-negative multiple of 0.5 (matcher widens by 0.5 steps).
        # Independent of max_layer_spread: even at 3.0, the layer's hard
        # window still clamps the actual spread to max_layer_spread.
    )
    max_layer_spread: float = (
        DEFAULT_MAX_LAYER_SPREAD  # Hard cap on weight spread (max - min)
        # within a single layer. Enforces balanced parallel branches;
        # violations either trigger a type fallback during generation or get
        # the seed rerolled.
    )
    layers_count: int = 30  # Total layers (start + intermediates + final boss)

    def __post_init__(self) -> None:
        """Validate structure configuration."""
        if self.max_parallel_paths < 1:
            raise ValueError(
                f"max_parallel_paths must be >= 1, got {self.max_parallel_paths}"
            )
        if self.max_exits < 1:
            raise ValueError(f"max_exits must be >= 1, got {self.max_exits}")
        if self.max_entrances < 1:
            raise ValueError(f"max_entrances must be >= 1, got {self.max_entrances}")
        # Cross-validation: splits and merges both need room for parallel paths
        if self.max_exits >= 2 and self.max_parallel_paths < 2:
            raise ValueError(
                f"max_parallel_paths must be >= 2 when max_exits >= 2, "
                f"got max_parallel_paths={self.max_parallel_paths}"
            )
        if self.max_entrances >= 2 and self.max_parallel_paths < 2:
            raise ValueError(
                f"max_parallel_paths must be >= 2 when max_entrances >= 2, "
                f"got max_parallel_paths={self.max_parallel_paths}"
            )
        if not isinstance(self.start_tier, int):
            raise TypeError(
                f"start_tier must be int, got {type(self.start_tier).__name__}"
            )
        if self.start_tier < 1 or self.start_tier > MAX_TIER:
            raise ValueError(f"start_tier must be 1-{MAX_TIER}, got {self.start_tier}")
        if not isinstance(self.final_tier, int):
            raise TypeError(
                f"final_tier must be int, got {type(self.final_tier).__name__}"
            )
        if self.final_tier < 1 or self.final_tier > MAX_TIER:
            raise ValueError(f"final_tier must be 1-{MAX_TIER}, got {self.final_tier}")
        if self.start_tier > self.final_tier:
            raise ValueError(
                f"start_tier ({self.start_tier}) must be <= final_tier ({self.final_tier})"
            )
        if self.max_weight_tolerance < 0:
            raise ValueError(
                f"max_weight_tolerance must be >= 0, got {self.max_weight_tolerance}"
            )
        # Matcher widens tolerance in 0.5 steps; non-multiples would silently
        # truncate (e.g. 2.7 would behave like 2.5).
        if (self.max_weight_tolerance * 2) % 1 != 0:
            raise ValueError(
                f"max_weight_tolerance must be a multiple of 0.5, "
                f"got {self.max_weight_tolerance}"
            )
        if self.max_layer_spread < 0:
            raise ValueError(
                f"max_layer_spread must be >= 0, got {self.max_layer_spread}"
            )
        if self.tier_curve not in ("linear", "power"):
            raise ValueError(
                f"tier_curve must be 'linear' or 'power', got '{self.tier_curve}'"
            )
        if self.tier_curve_exponent <= 0:
            raise ValueError(
                f"tier_curve_exponent must be > 0, got {self.tier_curve_exponent}"
            )

    @property
    def effective_final_boss_candidates(self) -> dict[str, int]:
        """Return candidates or default if empty."""
        return self.final_boss_candidates or {
            "leyndell_erdtree": 1,
            "enirilim_radahn": 1,
        }


def resolve_final_boss_candidates(
    candidates: dict[str, int], all_boss_zones: set[str]
) -> dict[str, int]:
    """Expand 'all' keyword to all major/final boss zones.

    Args:
        candidates: Dict of zone name -> weight, may include 'all' keyword.
        all_boss_zones: Set of all valid boss zone names.

    Returns:
        Dict of zone name -> weight with 'all' expanded to actual zones (weight 1).
    """
    if "all" in candidates:
        return dict.fromkeys(sorted(all_boss_zones), 1)
    return candidates


def prune_final_boss_candidates(
    candidates: dict[str, int], excluded: set[str]
) -> dict[str, int]:
    """Drop excluded zones from a final_boss candidate mapping (fresh dict).

    The 'all' keyword has no zone name to match, so it is preserved and later
    resolves against the boss_candidates snapshot (post-exclusion, taken
    before the passant filter).
    """
    return {zone: weight for zone, weight in candidates.items() if zone not in excluded}


def _parse_final_boss_candidates(raw: list[str] | dict[str, int]) -> dict[str, int]:
    """Parse final_boss_candidates from TOML.

    Accepts either a list of zone names (all weight 1) or a dict of
    zone name -> weight for backward compatibility.
    """
    if isinstance(raw, list):
        return dict.fromkeys(raw, 1)
    return {zone: int(weight) for zone, weight in raw.items()}


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
    rusty_key: bool = True  # Rusty Key (Good ID 8010) - Stormveil Castle gate
    drawing_room_key: bool = True  # Drawing-Room Key for Volcano Manor (Good ID 8134)
    lantern: bool = True  # Lantern (Good ID 2070) - hands-free light source
    spirit_calling_bell: bool = (
        True  # Spirit Calling Bell (Good ID 8158) - summon spirits
    )
    physick_flask: bool = (
        True  # Flask of Wondrous Physick (Good ID 250) - mix crystal tears
    )
    whetstone_knife: bool = (
        True  # Whetstone Knife (Good ID 8590) - enables weapon infusion
    )
    whetblades: bool = (
        True  # All Whetblades (Good IDs 8970-8974) - unlocks all affinities
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
    stonesword_keys: int = 6  # Stonesword Keys (Good ID 8000) - unlock imp statue seals

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
        if self.stonesword_keys < 0 or self.stonesword_keys > 99:
            raise ValueError(
                f"stonesword_keys must be 0-99, got {self.stonesword_keys}"
            )

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
        if self.rusty_key:
            goods.append(8010)  # Rusty Key (Stormveil Castle gate)
        if self.drawing_room_key:
            goods.append(8134)  # Drawing-Room Key (Volcano Manor)
        if self.lantern:
            goods.append(2070)  # Lantern
        if self.spirit_calling_bell:
            goods.append(8158)  # Spirit Calling Bell
        if self.physick_flask:
            goods.append(250)  # Flask of Wondrous Physick
        if self.whetstone_knife:
            goods.append(8590)  # Whetstone Knife
        if self.whetblades:
            goods.extend(
                [
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
    # Auto-equip best gear at start (item randomizer helper). Disabled by
    # default: SpeedFog gives a care package instead.
    auto_equip: bool = False
    dlc: bool = True
    nerf_gargoyles: bool = (
        True  # Disable damage tick in Valiant Gargoyles's poison cloud
    )
    nerf_malenia: bool = False  # Remove HP drain from Malenia's attacks
    allcraft: bool = True  # Unlock all crafting recipes at start
    item_preset: bool = True  # Enable item placement preset
    item_preset_path: str = ""  # Custom preset path (empty = built-in default)

    def __post_init__(self) -> None:
        """Validate configuration."""
        if self.difficulty < 0 or self.difficulty > 100:
            raise ValueError(f"difficulty must be 0-100, got {self.difficulty}")


@dataclass
class EnemyConfig:
    """Enemy randomization configuration."""

    randomize_bosses: str = "none"  # "none", "minor", "all"
    ignore_arena_size: bool = False
    swap_boss: bool = False  # Swap multi-phase boss entities (swappable tag)
    # When False, DLC bosses are removed from the boss randomization candidate
    # pool (arena selection is untouched). Independent from
    # item_randomizer.dlc, which controls item-randomizer scope.
    dlc_bosses: bool = True
    # Allowlist of bosses that may appear (case-insensitive substring of the
    # boss display name, resolved against data/boss_arena_tags.json at item
    # config generation). Empty = current behavior. When set, every randomized
    # boss slot (minor and major) draws uniformly from this list, with reuse
    # permitted, so e.g. bosses = ["Malenia"] yields a Malenia-only run.
    bosses: list[str] = field(default_factory=list)

    def __post_init__(self) -> None:
        """Validate and normalize enemy config."""
        # Accept legacy booleans: false→"none", true→"all"
        if isinstance(self.randomize_bosses, bool):
            self.randomize_bosses = "all" if self.randomize_bosses else "none"
        valid = ("none", "minor", "all")
        if self.randomize_bosses not in valid:
            raise ValueError(
                f"randomize_bosses must be one of {valid}, got {self.randomize_bosses!r}"
            )
        if not isinstance(self.bosses, list):
            raise ValueError("enemy.bosses must be a list of strings")
        cleaned: list[str] = []
        for entry in self.bosses:
            if not isinstance(entry, str):
                raise TypeError(f"enemy.bosses entries must be strings, got {entry!r}")
            stripped = entry.strip()
            if not stripped:
                raise ValueError("enemy.bosses entries must be non-empty")
            cleaned.append(stripped)
        self.bosses = cleaned
        if self.bosses and self.randomize_bosses == "none":
            raise ValueError(
                "enemy.bosses requires randomize_bosses to be 'minor' or 'all'"
            )


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


# Known config sections and their accepted keys. None means the section's
# content is free-form (validated elsewhere). Keep in sync with from_dict;
# _reject_unknown_keys uses this to fail loudly on typos instead of letting
# them be silent no-ops.
_KNOWN_SECTION_KEYS: dict[str, frozenset[str] | None] = {
    "run": frozenset(
        {
            "seed",
            "run_complete_message",
            "chapel_grace",
            "sentry_torch_shop",
            "death_markers",
        }
    ),
    "requirements": frozenset(
        {
            "legacy_dungeons",
            "bosses",
            "mini_dungeons",
            "major_bosses",
            "zones",
            "exclude_zones",
            "allowed_types",
        }
    ),
    "structure": frozenset(
        {
            "max_parallel_paths",
            "max_exits",
            "max_entrances",
            "first_layer_type",
            "final_boss_candidates",
            "start_tier",
            "final_tier",
            "tier_curve",
            "tier_curve_exponent",
            "max_weight_tolerance",
            "max_layer_spread",
            "layers_count",
            # Legacy keys: accepted with a DeprecationWarning in from_dict
            "split_probability",
            "merge_probability",
            "max_branches",
            "min_layers",
            "max_layers",
            "min_branch_age",
            "crosslinks",
        }
    ),
    "paths": frozenset({"game_dir", "output_dir", "platform"}),
    "starting_items": frozenset(
        {
            "academy_key",
            "pureblood_medal",
            "rusty_key",
            "drawing_room_key",
            "lantern",
            "spirit_calling_bell",
            "physick_flask",
            "whetstone_knife",
            "whetblades",
            "great_runes",
            "rune_godrick",
            "rune_radahn",
            "rune_morgott",
            "rune_mohg",
            "rune_rykard",
            "rune_malenia",
            "omother",
            "welldepthskey",
            "gaolupperlevelkey",
            "gaollowerlevelkey",
            "holeladennecklace",
            "messmerskindling",
            "talisman_pouches",
            "golden_seeds",
            "sacred_tears",
            "starting_runes",
            "larval_tears",
            "stonesword_keys",
        }
    ),
    "item_randomizer": frozenset(
        {
            "enabled",
            "difficulty",
            "remove_requirements",
            "auto_upgrade_weapons",
            "auto_upgrade_dropped",
            "reduce_upgrade_cost",
            "auto_equip",
            "dlc",
            "nerf_gargoyles",
            "nerf_malenia",
            "allcraft",
            "item_preset",
            "item_preset_path",
        }
    ),
    "care_package": frozenset(
        {
            "enabled",
            "weapon_upgrade",
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
        }
    ),
    "enemy": frozenset(
        {
            "randomize_bosses",
            "ignore_arena_size",
            "swap_boss",
            "dlc_bosses",
            "bosses",
        }
    ),
    # Free-form plugin tables, envelope-validated in Config.__post_init__
    "plugin": None,
    # Preset metadata consumed by the speedfog-racing platform, not by
    # speedfog itself (sort_order, description, estimated_duration, ...)
    "display": None,
}


def _reject_unknown_keys(data: dict[str, Any]) -> None:
    """Reject unknown sections and unknown keys within known sections.

    The removed [budget] section only warns (deprecated, ignored) so old
    configs keep loading.
    """
    errors: list[str] = []
    for section, content in data.items():
        if section == "budget":
            warnings.warn(
                "[budget] is no longer used and will be ignored; "
                "remove it from your config",
                DeprecationWarning,
                stacklevel=3,
            )
            continue
        if section not in _KNOWN_SECTION_KEYS:
            errors.append(f"unknown section [{section}]")
            continue
        if not isinstance(content, dict):
            errors.append(f"section [{section}] must be a table")
            continue
        known = _KNOWN_SECTION_KEYS[section]
        if known is not None:
            for key in content:
                if key not in known:
                    errors.append(f"unknown key {section}.{key}")
    if errors:
        raise ValueError("invalid config: " + "; ".join(errors))


@dataclass
class Config:
    """Main configuration container."""

    seed: int = 0
    run_complete_message: str | list[str] = "RUN COMPLETE"
    chapel_grace: bool = True
    sentry_torch_shop: bool = True
    death_markers: bool = True
    requirements: RequirementsConfig = field(default_factory=RequirementsConfig)
    structure: StructureConfig = field(default_factory=StructureConfig)
    paths: PathsConfig = field(default_factory=PathsConfig)
    starting_items: StartingItemsConfig = field(default_factory=StartingItemsConfig)
    item_randomizer: ItemRandomizerConfig = field(default_factory=ItemRandomizerConfig)
    care_package: CarePackageConfig = field(default_factory=CarePackageConfig)
    enemy: EnemyConfig = field(default_factory=EnemyConfig)
    plugins: dict[str, dict[str, Any]] = field(default_factory=dict)

    def __post_init__(self) -> None:
        """Validate cross-field constraints."""
        first = self.structure.first_layer_type
        if first and first not in self.requirements.allowed_types:
            raise ValueError(
                f"first_layer_type = {first!r} not in allowed_types = "
                f"{self.requirements.allowed_types!r}"
            )

        # Validate the [plugin.*] envelope. The values are forwarded verbatim
        # into graph.json and consumed by C# (Dictionary<string, PluginConfig>
        # with a typed bool `enabled`), so a malformed entry must fail here with
        # a clear message rather than as an opaque C# JSON error later. This is
        # envelope-only validation (no per-plugin schema): each entry must be a
        # table, and `enabled`, if present, must be a boolean.
        if not isinstance(self.plugins, dict):
            raise ValueError("[plugin] must be a table of plugin tables")
        for name, cfg in self.plugins.items():
            if not isinstance(cfg, dict):
                raise ValueError(f"[plugin.{name}] must be a table")
            if "enabled" in cfg and not isinstance(cfg["enabled"], bool):
                raise ValueError(f"[plugin.{name}].enabled must be a boolean")

    def resolve_run_complete_message(self, seed: int) -> str:
        """Resolve run_complete_message to a single string.

        When the field is a list, picks one entry using a seeded RNG so the
        same seed yields the same message.
        """
        if isinstance(self.run_complete_message, list):
            return random.Random(seed).choice(self.run_complete_message)
        return self.run_complete_message

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Config:
        """Create Config from a dictionary (e.g., parsed TOML).

        Unknown sections and unknown keys within known sections raise
        ValueError so a typo fails loudly instead of being a silent no-op.
        """
        _reject_unknown_keys(data)
        run_section = data.get("run", {})
        requirements_section = data.get("requirements", {})
        structure_section = data.get("structure", {})
        for legacy in (
            "split_probability",
            "merge_probability",
            "max_branches",
            "min_layers",
            "max_layers",
            "min_branch_age",
            "crosslinks",
        ):
            if legacy in structure_section:
                warnings.warn(
                    f"structure.{legacy} is no longer supported and will be ignored; "
                    "remove it from your config. "
                    "(max_branches was replaced by max_exits + max_entrances; "
                    "min_layers/max_layers by layers_count.)",
                    DeprecationWarning,
                    stacklevel=2,
                )
        paths_section = data.get("paths", {})
        starting_items_section = data.get("starting_items", {})
        item_randomizer_section = data.get("item_randomizer", {})
        care_package_section = data.get("care_package", {})
        enemy_section = data.get("enemy", {})

        run_complete_message = run_section.get("run_complete_message", "RUN COMPLETE")
        if isinstance(run_complete_message, list):
            if not run_complete_message:
                raise ValueError("run_complete_message list must not be empty")
            if not all(isinstance(m, str) for m in run_complete_message):
                raise TypeError("run_complete_message list must contain only strings")
        elif not isinstance(run_complete_message, str):
            raise TypeError(
                "run_complete_message must be a string or a list of strings"
            )

        return cls(
            seed=run_section.get("seed", 0),
            run_complete_message=run_complete_message,
            chapel_grace=run_section.get("chapel_grace", True),
            sentry_torch_shop=run_section.get("sentry_torch_shop", True),
            death_markers=run_section.get("death_markers", True),
            requirements=RequirementsConfig(
                legacy_dungeons=requirements_section.get("legacy_dungeons", 1),
                bosses=requirements_section.get("bosses", 5),
                mini_dungeons=requirements_section.get("mini_dungeons", 5),
                major_bosses=requirements_section.get("major_bosses", 8),
                zones=requirements_section.get("zones", []),
                exclude_zones=requirements_section.get("exclude_zones", []),
                allowed_types=requirements_section.get(
                    "allowed_types",
                    list(_VALID_CLUSTER_TYPES),
                ),
            ),
            structure=StructureConfig(
                max_parallel_paths=structure_section.get("max_parallel_paths", 3),
                max_exits=structure_section.get("max_exits", 3),
                max_entrances=structure_section.get("max_entrances", 3),
                first_layer_type=structure_section.get("first_layer_type"),
                final_boss_candidates=_parse_final_boss_candidates(
                    structure_section.get("final_boss_candidates", {})
                ),
                start_tier=structure_section.get("start_tier", 1),
                final_tier=structure_section.get("final_tier", MAX_TIER),
                tier_curve=structure_section.get("tier_curve", "linear"),
                tier_curve_exponent=structure_section.get("tier_curve_exponent", 0.6),
                max_weight_tolerance=float(
                    structure_section.get("max_weight_tolerance", 3.0)
                ),
                max_layer_spread=float(
                    structure_section.get("max_layer_spread", DEFAULT_MAX_LAYER_SPREAD)
                ),
                layers_count=structure_section.get("layers_count", 30),
            ),
            paths=PathsConfig(
                game_dir=paths_section.get("game_dir", ""),
                output_dir=paths_section.get("output_dir", "./seeds"),
                platform=paths_section.get("platform"),
            ),
            starting_items=StartingItemsConfig(
                academy_key=starting_items_section.get("academy_key", True),
                pureblood_medal=starting_items_section.get("pureblood_medal", False),
                rusty_key=starting_items_section.get("rusty_key", True),
                drawing_room_key=starting_items_section.get("drawing_room_key", True),
                lantern=starting_items_section.get("lantern", True),
                spirit_calling_bell=starting_items_section.get(
                    "spirit_calling_bell", True
                ),
                physick_flask=starting_items_section.get("physick_flask", True),
                whetstone_knife=starting_items_section.get("whetstone_knife", True),
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
                stonesword_keys=starting_items_section.get("stonesword_keys", 6),
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
                auto_equip=item_randomizer_section.get("auto_equip", False),
                dlc=item_randomizer_section.get("dlc", True),
                nerf_gargoyles=item_randomizer_section.get("nerf_gargoyles", True),
                nerf_malenia=item_randomizer_section.get("nerf_malenia", False),
                allcraft=item_randomizer_section.get("allcraft", True),
                item_preset=item_randomizer_section.get("item_preset", True),
                item_preset_path=item_randomizer_section.get("item_preset_path", ""),
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
            enemy=EnemyConfig(
                randomize_bosses=enemy_section.get("randomize_bosses", "none"),
                ignore_arena_size=enemy_section.get("ignore_arena_size", False),
                swap_boss=enemy_section.get("swap_boss", False),
                dlc_bosses=enemy_section.get("dlc_bosses", True),
                bosses=enemy_section.get("bosses", []),
            ),
            plugins=data.get("plugin", {}),
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
