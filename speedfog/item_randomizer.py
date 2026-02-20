"""Item Randomizer integration for SpeedFog."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

from speedfog.config import Config


def generate_item_config(
    config: Config, seed: int, finish_boss_defeat_flag: int = 0
) -> dict[str, Any]:
    """Generate item_config.json content for ItemRandomizerWrapper.

    Args:
        config: SpeedFog configuration.
        seed: Random seed for the run.
        finish_boss_defeat_flag: Defeat flag of the final boss (for lock_final_boss).

    Returns:
        Dictionary ready to be serialized to JSON.
    """
    result: dict[str, Any] = {
        "seed": seed,
        "difficulty": config.item_randomizer.difficulty,
        "options": {
            "item": True,
            "enemy": True,
            "fog": True,
            "crawl": True,
            "dlc": config.item_randomizer.dlc,
            "weaponreqs": config.item_randomizer.remove_requirements,
            "sombermode": config.item_randomizer.reduce_upgrade_cost,
            "nerfgargoyles": config.item_randomizer.nerf_gargoyles,
        },
        "enemy_options": {
            "randomize_bosses": config.enemy.randomize_bosses,
            "lock_final_boss": config.enemy.lock_final_boss,
            "finish_boss_defeat_flag": finish_boss_defeat_flag,
        },
        # RandomizerHelper.dll defaults almost everything to true when not
        # specified in the INI.  We must be exhaustive to avoid surprises
        # (e.g. auto-equip activating silently).  Int options like
        # weaponLevelsBelowMax/weaponLevelRange default to 0 which is fine.
        "helper_options": {
            # Auto-equip: disabled — SpeedFog gives a care package instead
            "autoEquip": False,
            "equipShop": False,
            "equipWeapons": False,
            "bowLeft": False,
            "castLeft": False,
            "equipArmor": False,
            "equipAccessory": False,
            "equipSpells": False,
            "equipCrystalTears": False,
            # Auto-upgrade: enabled
            "autoUpgrade": True,
            "autoUpgradeWeapons": config.item_randomizer.auto_upgrade_weapons,
            "regionLockWeapons": False,
            "autoUpgradeSpiritAshes": True,
            "autoUpgradeDropped": config.item_randomizer.auto_upgrade_dropped,
        },
    }

    if config.item_randomizer.item_preset:
        result["item_preset_path"] = "item_preset.yaml"

    return result


def run_item_randomizer(
    seed_dir: Path,
    game_dir: Path,
    output_dir: Path,
    platform: str | None,
    verbose: bool,
) -> bool:
    """Run ItemRandomizerWrapper to generate randomized items/enemies.

    Args:
        seed_dir: Directory containing item_config.json
        game_dir: Path to Elden Ring Game directory
        output_dir: Output directory for randomized files
        platform: "windows", "linux", or None for auto-detect
        verbose: Print command and output

    Returns:
        True on success, False on failure.
    """
    project_root = Path(__file__).parent.parent
    wrapper_dir = project_root / "writer" / "ItemRandomizerWrapper"
    wrapper_exe = wrapper_dir / "publish" / "win-x64" / "ItemRandomizerWrapper.exe"

    if not wrapper_exe.exists():
        print(
            f"Error: ItemRandomizerWrapper not found at {wrapper_exe}", file=sys.stderr
        )
        print(
            "Run: python tools/setup_dependencies.py --fogrando <path> --itemrando <path>",
            file=sys.stderr,
        )
        return False

    # Detect platform
    if platform is None or platform == "auto":
        platform = "windows" if sys.platform == "win32" else "linux"

    # Check Wine availability on non-Windows
    if platform == "linux" and shutil.which("wine") is None:
        print(
            "Error: Wine not found. Install wine to run Item Randomizer on Linux.",
            file=sys.stderr,
        )
        return False

    # Build command with absolute paths
    seed_dir = seed_dir.resolve()
    game_dir = game_dir.resolve()
    output_dir = output_dir.resolve()
    config_path = seed_dir / "item_config.json"

    if platform == "linux":
        cmd = ["wine", str(wrapper_exe.resolve())]
    else:
        cmd = [str(wrapper_exe.resolve())]

    cmd.extend(
        [
            str(config_path),
            "--game-dir",
            str(game_dir),
            "--data-dir",
            str(wrapper_dir / "diste"),
            "-o",
            str(output_dir),
        ]
    )

    if verbose:
        print(f"Running: {' '.join(cmd)}")
        print(f"Working directory: {wrapper_dir}")

    # Run from wrapper_dir so it finds diste/
    # Don't use text=True - Wine output may contain non-UTF-8 bytes
    process = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        cwd=wrapper_dir,
    )

    assert process.stdout is not None
    for line in process.stdout:
        # Decode with error replacement for Wine's binary output
        print(line.decode("utf-8", errors="replace"), end="")

    process.wait()
    return process.returncode == 0
