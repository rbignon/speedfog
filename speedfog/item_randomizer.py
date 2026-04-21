"""Item Randomizer integration for SpeedFog."""

from __future__ import annotations

import random
import shutil
import subprocess
import sys
from collections.abc import Iterable, Mapping
from pathlib import Path
from typing import Any

from speedfog.boss_arena_constraints import (
    EntityTags,
    match_arenas_to_bosses,
)
from speedfog.clusters import ClusterData
from speedfog.config import Config
from speedfog.output import resolve_entity_id


def generate_item_config(
    config: Config,
    seed: int,
    *,
    boss_clusters: Iterable[ClusterData] = (),
    tags: Mapping[int, EntityTags] | None = None,
    vanilla_major_ids: Iterable[int] = (),
    vanilla_minor_ids: Iterable[int] = (),
    phase_mapping: Mapping[int, int] | None = None,
) -> dict[str, Any]:
    """Generate item_config.json content for ItemRandomizerWrapper.

    When ``config.enemy.randomize_bosses`` is ``"minor"`` or ``"all"``, computes
    an arena-compatible boss assignment and emits it as ``enemy_assignments``
    (a ``{arena_entity_id: boss_entity_id}`` dict, both as strings). The
    assignment is threaded into ``Preset.Enemies`` by ItemRandomizerWrapper.

    ``tags`` must be provided when boss randomization is active.
    ``vanilla_major_ids`` / ``vanilla_minor_ids`` are the entity IDs from the
    current ``clusters.json`` whose ``cluster.type`` is ``major_boss`` /
    ``boss_arena`` respectively. They are combined with the source-only
    entries from ``tags`` (entities with ``pool = "minor" | "major"``) to form
    the matcher's source pools. Entities with ``boss.exclude_from_pool = True``
    are filtered out of the source pool by ``match_arenas_to_bosses``.

    ``phase_mapping`` (from ``speedfog.output.parse_boss_phases``) maps
    ``phase2_entity_id -> phase1_entity_id`` for multi-phase bosses. When a
    DAG cluster's leader is in ``phase_mapping`` keys, the phase-1 slot is
    added as an additional independent arena (same pool, no phase pairing).
    """
    result: dict[str, Any] = {
        "seed": seed,
        "difficulty": config.item_randomizer.difficulty,
        "options": {
            "item": True,
            "enemy": True,
            "fog": True,
            "crawl": True,
            "mats": True,
            "copydrops": True,
            # Rewrite boss healthbar names to match the randomized enemy.
            "editnames": True,
            "nohand": config.item_randomizer.remove_requirements,
            "dlc": config.item_randomizer.dlc,
            "weaponreqs": config.item_randomizer.remove_requirements,
            "sombermode": config.item_randomizer.reduce_upgrade_cost,
            "nerfgargoyles": config.item_randomizer.nerf_gargoyles,
            "nerfmalenia": config.item_randomizer.nerf_malenia,
            "allcraft": config.item_randomizer.allcraft,
        },
        "enemy_options": {
            "randomize_bosses": config.enemy.randomize_bosses,
            "ignore_arena_size": config.enemy.ignore_arena_size,
            "swap_boss": config.enemy.swap_boss,
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

    if config.enemy.randomize_bosses != "none":
        if tags is None:
            raise ValueError("tags required when randomize_bosses != 'none'")

        major_pool = _compose_pool(tags, "major", vanilla_major_ids)
        minor_pool = _compose_pool(tags, "minor", vanilla_minor_ids)

        assignments = _build_enemy_assignments(
            boss_clusters=boss_clusters,
            tags=tags,
            major_pool=major_pool,
            minor_pool=minor_pool,
            phase_mapping=phase_mapping or {},
            randomize_majors=(config.enemy.randomize_bosses == "all"),
            check_size=not config.enemy.ignore_arena_size,
            seed=seed,
        )
        if assignments:
            result["enemy_assignments"] = {
                str(aid): str(bid) for aid, bid in assignments.items()
            }

    return result


def _compose_pool(
    tags: Mapping[int, EntityTags], kind: str, vanilla_ids: Iterable[int]
) -> list[int]:
    """Combine vanilla IDs of a given ``cluster.type`` with source-only
    entries whose ``pool`` field matches ``kind``.
    """
    ids = {eid for eid in vanilla_ids if eid in tags}
    ids.update(eid for eid, entry in tags.items() if entry.pool == kind)
    return sorted(ids)


# Seed salt to decorrelate the matcher RNG stream from the main run seed
# (ensures changes to the matcher logic don't perturb unrelated seeded
# consumers that share the same base seed).
BOSS_ASSIGNMENT_SEED_SALT = 0xBA7A5A5A


def _build_enemy_assignments(
    *,
    boss_clusters: Iterable[ClusterData],
    tags: Mapping[int, EntityTags],
    major_pool: list[int],
    minor_pool: list[int],
    phase_mapping: Mapping[int, int],
    randomize_majors: bool,
    check_size: bool,
    seed: int,
) -> dict[int, int]:
    """Match DAG boss clusters to candidate bosses under compatibility rules.

    Majors and minors are matched independently so that major arenas only
    receive majors and vice versa. Multi-phase bosses with separate phase
    entities (per ``phase_mapping``) get one independent slot per phase, both
    drawn from the same pool without pairing.
    """
    majors: list[int] = []
    minors: list[int] = []
    for cluster in boss_clusters:
        leader = resolve_entity_id(cluster.defeat_flag)
        if leader == 0 or leader not in tags:
            continue
        slots = [leader]
        phase1 = phase_mapping.get(leader)
        if phase1 is not None and phase1 in tags:
            slots.append(phase1)
        if cluster.type == "major_boss":
            majors.extend(slots)
        elif cluster.type == "boss_arena":
            minors.extend(slots)

    rng = random.Random(seed ^ BOSS_ASSIGNMENT_SEED_SALT)
    out: dict[int, int] = {}
    if minors:
        out.update(
            match_arenas_to_bosses(
                arena_ids=minors,
                boss_ids=minor_pool,
                tags=tags,
                rng=rng,
                check_size=check_size,
            )
        )
    if randomize_majors and majors:
        out.update(
            match_arenas_to_bosses(
                arena_ids=majors,
                boss_ids=major_pool,
                tags=tags,
                rng=rng,
                check_size=check_size,
            )
        )
    return out


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
            "Run: python tools/bootstrap.py --fogrando <path> --itemrando <path>",
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
