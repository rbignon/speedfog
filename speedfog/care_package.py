"""Care package: randomized starting build for SpeedFog runs.

Loads curated item pools from data/care_package_items.toml and samples
a random starting build based on the run seed and per-category counts.
"""

from __future__ import annotations

import math
import random
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Any

if sys.version_info >= (3, 11):
    import tomllib
else:
    try:
        import tomli as tomllib
    except ImportError as e:
        raise ImportError(
            "tomli is required for Python < 3.11. Install with: pip install tomli"
        ) from e

if TYPE_CHECKING:
    from speedfog.config import CarePackageConfig


# Item type values matching EMEDF bank 2003 index 43 (DirectlyGivePlayerItem)
ITEM_TYPE_WEAPON = 0
ITEM_TYPE_PROTECTOR = 1
ITEM_TYPE_ACCESSORY = 2
ITEM_TYPE_GOODS = 3
ITEM_TYPE_GEM = 4


@dataclass
class CarePackageItem:
    """A single item to give the player at game start."""

    type: int  # 0=Weapon, 1=Protector, 2=Accessory, 3=Goods, 4=Gem (Ash of War)
    id: int  # Param row ID (with upgrade level encoded for weapons)
    name: str  # Display name for spoiler log


def load_item_pool(path: Path) -> dict[str, Any]:
    """Load curated item pool from care_package_items.toml.

    Args:
        path: Path to the TOML file.

    Returns:
        Parsed TOML data as a dictionary.

    Raises:
        ValueError: If any item has id=0 (placeholder not replaced).
    """
    with path.open("rb") as f:
        result: dict[str, Any] = tomllib.load(f)
    _validate_pool_ids(result)
    return result


def _validate_pool_ids(pool: dict[str, Any]) -> None:
    """Check that all items in the pool have non-zero IDs."""
    for key, value in pool.items():
        if isinstance(value, list):
            for item in value:
                if isinstance(item, dict) and item.get("id") == 0:
                    raise ValueError(
                        f"Item '{item.get('name', '?')}' in [{key}] has id=0 "
                        f"(placeholder not replaced)"
                    )
        elif isinstance(value, dict):
            for sub_key, sub_items in value.items():
                if isinstance(sub_items, list):
                    for item in sub_items:
                        if isinstance(item, dict) and item.get("id") == 0:
                            raise ValueError(
                                f"Item '{item.get('name', '?')}' in [{key}.{sub_key}] "
                                f"has id=0 (placeholder not replaced)"
                            )


def _somber_upgrade(standard_level: int) -> int:
    """Convert standard upgrade level to somber equivalent.

    Somber upgrade = floor(standard / 2.5).
    Examples: +8 -> +3, +10 -> +4, +25 -> +10.
    """
    return math.floor(standard_level / 2.5)


def _apply_weapon_upgrade(base_id: int, upgrade: int) -> int:
    """Apply upgrade level to a weapon base ID.

    Weapon param IDs encode upgrade as: base_id + upgrade_level.
    """
    return base_id + upgrade


def _format_upgrade(name: str, level: int) -> str:
    """Format weapon name with upgrade level for display."""
    if level == 0:
        return name
    return f"{name} +{level}"


def sample_care_package(
    config: CarePackageConfig,
    seed: int,
    pool_path: Path,
) -> list[CarePackageItem]:
    """Sample a random care package from the item pool.

    Uses seed-based RNG for deterministic results (same seed = same build).

    Args:
        config: Care package configuration with per-category counts.
        seed: Random seed for deterministic sampling.
        pool_path: Path to care_package_items.toml.

    Returns:
        List of CarePackageItem to give at game start.
    """
    pool = load_item_pool(pool_path)
    rng = random.Random(seed)
    items: list[CarePackageItem] = []

    standard_upgrade = config.weapon_upgrade
    somber_upgrade = _somber_upgrade(standard_upgrade)

    # Helper to sample from a simple pool (no upgrade sub-categories)
    def sample_simple(
        pool_items: list[dict[str, Any]],
        count: int,
        item_type: int,
    ) -> None:
        if count <= 0 or not pool_items:
            return
        chosen = rng.sample(pool_items, min(count, len(pool_items)))
        for item in chosen:
            items.append(
                CarePackageItem(type=item_type, id=item["id"], name=item["name"])
            )

    # Helper to sample from a merged standard+somber pool with correct upgrade
    def sample_weapons(
        pool_dict: dict[str, list[dict[str, Any]]],
        count: int,
    ) -> None:
        if count <= 0:
            return
        # Tag each item with its upgrade path, then merge and sample
        tagged: list[tuple[dict[str, Any], bool]] = []
        for item in pool_dict.get("standard", []):
            tagged.append((item, False))
        for item in pool_dict.get("somber", []):
            tagged.append((item, True))
        if not tagged:
            return
        chosen = rng.sample(tagged, min(count, len(tagged)))
        for item, is_somber in chosen:
            base_id = item["id"]
            name = item["name"]
            upgrade = somber_upgrade if is_somber else standard_upgrade
            if upgrade > 0:
                final_id = _apply_weapon_upgrade(base_id, upgrade)
                display_name = _format_upgrade(name, upgrade)
            else:
                final_id = base_id
                display_name = name
            items.append(
                CarePackageItem(type=ITEM_TYPE_WEAPON, id=final_id, name=display_name)
            )

    # Weapons (merged standard + somber pool)
    sample_weapons(pool.get("weapons", {}), config.weapons)

    # Shields (standard upgrade, Weapon type)
    def sample_standard_weapons(
        pool_items: list[dict[str, Any]],
        count: int,
    ) -> None:
        if count <= 0 or not pool_items:
            return
        chosen = rng.sample(pool_items, min(count, len(pool_items)))
        for item in chosen:
            base_id = item["id"]
            name = item["name"]
            if standard_upgrade > 0:
                final_id = _apply_weapon_upgrade(base_id, standard_upgrade)
                display_name = _format_upgrade(name, standard_upgrade)
            else:
                final_id = base_id
                display_name = name
            items.append(
                CarePackageItem(type=ITEM_TYPE_WEAPON, id=final_id, name=display_name)
            )

    sample_standard_weapons(pool.get("shields", []), config.shields)

    # Catalysts (merged standard + somber pool, Weapon type)
    sample_weapons(pool.get("catalysts", {}), config.catalysts)

    # Armor (Protector type, no upgrade)
    armor = pool.get("armor", {})
    sample_simple(armor.get("head", []), config.head_armor, ITEM_TYPE_PROTECTOR)
    sample_simple(armor.get("body", []), config.body_armor, ITEM_TYPE_PROTECTOR)
    sample_simple(armor.get("arm", []), config.arm_armor, ITEM_TYPE_PROTECTOR)
    sample_simple(armor.get("leg", []), config.leg_armor, ITEM_TYPE_PROTECTOR)

    # Talismans (Accessory type, no upgrade)
    sample_simple(pool.get("talismans", []), config.talismans, ITEM_TYPE_ACCESSORY)

    # Sorceries (Goods type)
    sample_simple(pool.get("sorceries", []), config.sorceries, ITEM_TYPE_GOODS)

    # Incantations (Goods type)
    sample_simple(pool.get("incantations", []), config.incantations, ITEM_TYPE_GOODS)

    # Crystal Tears (Goods type)
    sample_simple(pool.get("crystal_tears", []), config.crystal_tears, ITEM_TYPE_GOODS)

    # Ashes of War (Gem type, no upgrade)
    sample_simple(pool.get("ashes_of_war", []), config.ashes_of_war, ITEM_TYPE_GEM)

    return items
