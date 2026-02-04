"""Item Randomizer integration for SpeedFog."""

from __future__ import annotations

from typing import Any

from speedfog.config import Config


def generate_item_config(config: Config, seed: int) -> dict[str, Any]:
    """Generate item_config.json content for ItemRandomizerWrapper.

    Args:
        config: SpeedFog configuration.
        seed: Random seed for the run.

    Returns:
        Dictionary ready to be serialized to JSON.
    """
    return {
        "seed": seed,
        "difficulty": config.item_randomizer.difficulty,
        "options": {
            "item": True,
            "enemy": True,
            "fog": True,
            "crawl": True,
            "weaponreqs": config.item_randomizer.remove_requirements,
        },
        "preset": "enemy_preset.yaml",
        "helper_options": {
            "autoUpgradeWeapons": config.item_randomizer.auto_upgrade_weapons,
        },
    }
