"""Item Randomizer integration for SpeedFog."""

from __future__ import annotations

import errno
import random
import shutil
import sys
from collections.abc import Iterable, Iterator, Mapping
from contextlib import ExitStack, contextmanager
from pathlib import Path
from typing import Any

from speedfog.boss_arena_constraints import (
    ArenaTags,
    BossTags,
    EntityTags,
    assign_bosses_uniform,
    match_arenas_to_bosses,
    resolve_boss_allowlist,
)
from speedfog.clusters import ClusterData
from speedfog.config import Config
from speedfog.enemy_data import resolve_entity_id
from speedfog.proc import stream_command

# Lock file serializing the extraction of the shared diste/Vanilla cache
# between parallel generations (see ensure_vanilla_cache).
VANILLA_CACHE_LOCK = ".vanilla-cache.lock"


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
    assignment is threaded into ``EnemyPreset.Enemies`` by ItemRandomizerWrapper.

    ``tags`` must be provided when boss randomization is active.

    **Allowlist path** (``config.enemy.bosses`` is non-empty): the pool is
    resolved via ``resolve_boss_allowlist(tags, config.enemy.bosses)``, which
    matches each name as a case-insensitive substring and raises on ambiguity or
    zero matches. All in-scope arenas are matched from this single pool with
    reuse permitted (``assign_bosses_uniform``). The ``dlc_bosses``,
    ``exclude_from_pool``, and pool-composition rules that apply to the standard
    path do not apply here: the allowlist is authoritative. Phase-1 slots are
    still expanded per the ``phase_mapping`` (one independent slot per phase
    entity), and size compatibility is still enforced unless
    ``ignore_arena_size`` is set.

    **Standard path** (``config.enemy.bosses`` is empty):
    ``vanilla_major_ids`` / ``vanilla_minor_ids`` are the entity IDs from the
    current ``clusters.json`` whose ``cluster.type`` is ``major_boss`` /
    ``boss_arena`` respectively. They are combined with the source-only
    entries from ``tags`` (entities with ``pool = "minor" | "major"``) by
    ``_compose_pool``, which also drops entries with
    ``boss.exclude_from_pool = True`` before the matcher runs. When
    ``config.enemy.dlc_bosses`` is False, DLC-tagged entries are also dropped
    from the pool; arena selection is unchanged, so a DLC vanilla arena in
    the DAG still receives a non-DLC replacement boss.

    ``phase_mapping`` (from ``speedfog.enemy_data.parse_boss_phases``) maps
    ``phase2_entity_id -> phase1_entity_id`` for multi-phase bosses. When a
    DAG cluster's leader is in ``phase_mapping`` keys, the phase-1 slot is
    added as an additional independent arena (same pool, no phase pairing).
    """
    auto_equip = config.item_randomizer.auto_equip
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
            # RandomizerOptions returns false for unset bool keys; scale and
            # phasehp must be set explicitly or relocated bosses keep their
            # native HP (no tier->tier rescaling SpEffect injected).
            "scale": True,
            "phasehp": True,
            "nerflantern": True,
            "nohand": config.item_randomizer.remove_requirements,
            "dlc": config.item_randomizer.dlc,
            "tarnished": config.tarnished.enabled,
            "weaponreqs": config.item_randomizer.remove_requirements,
            "sombermode": config.item_randomizer.reduce_upgrade_cost,
            "nerfgargoyles": config.item_randomizer.nerf_gargoyles,
            "nerfmalenia": config.item_randomizer.nerf_malenia,
            "allcraft": config.item_randomizer.allcraft,
            # v0.12 keys two annotation-default behaviors on named options
            # (Switch: in diste/Base/annotations.txt); both are checked by
            # default in the GUI but unset booleans are false headless. Pin
            # the GUI defaults. spellshops is live (neither preset overrides
            # the SorceryShop/MiracleShop sections): spell shops keep selling
            # random spells. dlcblessing is inert with the shipped presets
            # (it only filters the default preset's DlcOnlyItems entries, and
            # each shipped preset's own DlcOnlyItems item list replaces them:
            # empty in item_preset/item_preset_without_weapons, both blessings
            # in uwyg_item_preset) but pinned anyway so a future preset
            # without that section gets the GUI default, not the
            # headless-false flip.
            "dlcblessing": True,
            "spellshops": True,
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
            # Auto-equip: driven by config.item_randomizer.auto_equip. Disabled
            # by default since SpeedFog gives a care package instead. equipShop
            # stays disabled (not covered by the auto_equip toggle).
            "autoEquip": auto_equip,
            "equipShop": False,
            "equipWeapons": auto_equip,
            "bowLeft": auto_equip,
            "castLeft": auto_equip,
            "equipArmor": auto_equip,
            "equipAccessory": auto_equip,
            "equipSpells": auto_equip,
            "equipCrystalTears": auto_equip,
            # Auto-upgrade: enabled
            "autoUpgrade": True,
            "autoUpgradeWeapons": config.item_randomizer.auto_upgrade_weapons,
            "regionLockWeapons": False,
            "autoUpgradeSpiritAshes": True,
            "autoUpgradeDropped": config.item_randomizer.auto_upgrade_weapons,
            "autoUpgradeEquipped": config.item_randomizer.auto_upgrade_weapons,
        },
    }

    if config.item_randomizer.item_preset:
        result["item_preset_path"] = "item_preset.yaml"

    if config.enemy.randomize_bosses != "none":
        if tags is None:
            raise ValueError("tags required when randomize_bosses != 'none'")

        if config.enemy.bosses:
            pool = resolve_boss_allowlist(tags, config.enemy.bosses)
            assignments = _build_uniform_assignments(
                boss_clusters=boss_clusters,
                tags=tags,
                pool=pool,
                phase_mapping=phase_mapping or {},
                randomize_majors=(config.enemy.randomize_bosses == "all"),
                check_size=not config.enemy.ignore_arena_size,
                seed=seed,
            )
        else:
            exclude_dlc = not config.enemy.dlc_bosses
            major_pool = _compose_pool(
                tags,
                "major",
                vanilla_major_ids,
                other_vanilla_ids=vanilla_minor_ids,
                phase_mapping=phase_mapping,
                exclude_dlc=exclude_dlc,
            )
            minor_pool = _compose_pool(
                tags,
                "minor",
                vanilla_minor_ids,
                other_vanilla_ids=vanilla_major_ids,
                phase_mapping=phase_mapping,
                exclude_dlc=exclude_dlc,
            )
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
    tags: Mapping[int, EntityTags],
    kind: str,
    vanilla_ids: Iterable[int],
    *,
    other_vanilla_ids: Iterable[int] = (),
    phase_mapping: Mapping[int, int] | None = None,
    exclude_dlc: bool = False,
) -> dict[int, BossTags]:
    """Compose the candidate boss pool for ``kind`` (``"major"`` or ``"minor"``).

    Entries are routed by priority:

    1. **Vanilla IDs** for ``kind`` (entity IDs of ``clusters.json`` bosses
       whose ``cluster.type`` matches): always added unless
       ``exclude_from_pool``.
    2. **Phase-1 siblings** of vanilla leaders (via ``phase_mapping``):
       added to mirror the arena-side expansion in
       ``_build_enemy_assignments``; otherwise the matcher can hit
       ``|arenas| > |pool|`` purely from phase splits.
    3. **Source-only entries** with an explicit ``pool`` field equal to
       ``kind`` (``pool`` is authoritative when present).
    4. **Orphan arena entries**: have an ``arena`` block but no ``pool``
       field and no slot in ``clusters.json`` (checked against the union
       of ``vanilla_ids`` and ``other_vanilla_ids`` so a vanilla minor
       doesn't leak into the major pool when its ``arena.type`` would
       otherwise map there). ``arena.type == 2`` routes to the major pool,
       every other type routes to the minor pool. This captures BAR
       entries that are vanilla bindings in BAR's worldview but lack a
       corresponding fog-gate arena in SpeedFog (DLC field bosses without
       ``BossTrigger``, multi-phase splits of fights absent from the DAG,
       evergaol variants, etc.).

    ``exclude_from_pool`` filters at every branch (the variants tagged as
    duplicates stay out of the pool here, while still being valid arena
    targets in the matcher's other input).

    ``exclude_dlc`` applies the same "drop from pool but keep arena
    addressable" semantics for DLC entries: when True, any entry with
    ``entry.dlc`` is skipped at every insertion branch. The arena side
    (``_build_enemy_assignments``) is untouched, so a DLC vanilla arena in
    the DAG still receives a non-DLC replacement.

    Phase-1 IDs absent from ``tags`` are silently skipped (tolerable partial
    data); the strict missing-tag error stays on ``vanilla_ids`` where a gap
    signals a misconfigured cluster. The phase-mapping leader check uses
    ``vanilla_set`` (full vanilla list, including excluded entries) so an
    ``exclude_from_pool`` leader still gates inclusion of its phase-1
    sibling.

    Raises ``KeyError`` for any ``vanilla_ids`` entry missing from ``tags``.

    The returned dict is key-sorted so seed-to-result stability does not
    depend on the iteration order of ``tags`` or ``vanilla_ids``.
    """
    pool: dict[int, BossTags] = {}
    vanilla_set: set[int] = set()
    for eid in vanilla_ids:
        entry = tags.get(eid)
        if entry is None:
            raise KeyError(
                f"vanilla {kind} boss entity {eid} missing from "
                f"boss_arena_tags.json"
            )
        vanilla_set.add(eid)
        if entry.boss.exclude_from_pool:
            continue
        if exclude_dlc and entry.dlc:
            continue
        pool[eid] = entry.boss
    if phase_mapping:
        for leader, phase1 in phase_mapping.items():
            if leader not in vanilla_set:
                continue
            entry = tags.get(phase1)
            if entry is None:
                continue
            if entry.boss.exclude_from_pool:
                continue
            if exclude_dlc and entry.dlc:
                continue
            pool[phase1] = entry.boss
    # Union with the other kind's vanilla IDs so the orphan branch below
    # doesn't promote (say) a vanilla minor with arena.type=2 into the
    # major pool. The vanilla branch above already handles each entry in
    # its own pool; the orphan branch must only fire for entities that
    # are vanilla in neither pool.
    all_vanilla = vanilla_set | set(other_vanilla_ids)
    for eid, entry in tags.items():
        if entry.boss.exclude_from_pool:
            continue
        if exclude_dlc and entry.dlc:
            continue
        if entry.pool == kind:
            pool[eid] = entry.boss
            continue
        # Orphan arena fallback: tagged with an arena but neither pinned by
        # a ``pool`` field nor matched in ``clusters.json``. arena.type==2 is
        # BAR's "walled arena" category (major), everything else routes to
        # minor. See docs/boss-arena-constraints.md, "Pool composition".
        if entry.pool is None and entry.arena is not None and eid not in all_vanilla:
            derived = "major" if entry.arena.type == 2 else "minor"
            if derived == kind:
                pool[eid] = entry.boss
    return dict(sorted(pool.items()))


# Seed salt to decorrelate the matcher RNG stream from the main run seed
# (ensures changes to the matcher logic don't perturb unrelated seeded
# consumers that share the same base seed).
BOSS_ASSIGNMENT_SEED_SALT = 0xBA7A5A5A

# Cluster types that receive an arena-matched boss. final_boss terminals
# (Elden Beast / Promised Consort Radahn) are treated as major arenas in "all"
# mode (see docs/boss-arena-constraints.md); boss_arena clusters are the minors.
# Centralized so adding a new arena type is a single edit.
MAJOR_ARENA_TYPES = {"major_boss", "final_boss"}
MINOR_ARENA_TYPES = {"boss_arena"}
ASSIGNABLE_ARENA_TYPES = MAJOR_ARENA_TYPES | MINOR_ARENA_TYPES


def _family_forbidden(
    arena_ids: Iterable[int], phase_mapping: Mapping[int, int]
) -> dict[int, frozenset[int]]:
    """Forbidden boss IDs per arena: the arena's own phase family.

    A boss must never be placed in its original arena, and a multi-phase
    boss counts as one identity: no family member may land in any family
    slot (Fire Giant phase 2 is forbidden in the phase-1 arena and vice
    versa). Single-phase bosses have a family of one. ``phase_mapping``
    maps leader (phase 2) -> phase 1; both directions are resolved here.
    """
    phase1_to_leader = {p1: leader for leader, p1 in phase_mapping.items()}
    out: dict[int, frozenset[int]] = {}
    for eid in arena_ids:
        family = {eid}
        phase1 = phase_mapping.get(eid)
        if phase1 is not None:
            family.add(phase1)
        leader = phase1_to_leader.get(eid)
        if leader is not None:
            family.add(leader)
        out[eid] = frozenset(family)
    return out


def _build_enemy_assignments(
    *,
    boss_clusters: Iterable[ClusterData],
    tags: Mapping[int, EntityTags],
    major_pool: dict[int, BossTags],
    minor_pool: dict[int, BossTags],
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

    A boss is never assigned to its own arena, and multi-phase families are
    excluded as a unit (strict; see _family_forbidden).

    Raises ``KeyError`` if a DAG boss cluster's leader (or its phase-1
    sibling) has no entry in ``tags`` or no ``arena`` block. A silent skip
    there would leave a vanilla boss in the run without any signal.
    """
    majors: dict[int, ArenaTags] = {}
    minors: dict[int, ArenaTags] = {}
    for cluster in boss_clusters:
        if cluster.type in MAJOR_ARENA_TYPES:
            target = majors
        elif cluster.type in MINOR_ARENA_TYPES:
            target = minors
        else:
            continue
        leader = resolve_entity_id(cluster.defeat_flag)
        slots = [leader]
        phase1 = phase_mapping.get(leader)
        if phase1 is not None:
            slots.append(phase1)
        for eid in slots:
            entry = tags.get(eid)
            if entry is None:
                raise KeyError(
                    f"cluster {cluster.id!r} entity {eid} missing from "
                    f"boss_arena_tags.json"
                )
            if entry.arena is None:
                raise KeyError(
                    f"cluster {cluster.id!r} entity {eid} has no arena block "
                    f"in boss_arena_tags.json"
                )
            target[eid] = entry.arena

    rng = random.Random(seed ^ BOSS_ASSIGNMENT_SEED_SALT)
    out: dict[int, int] = {}
    jobs = [(minors, minor_pool, True), (majors, major_pool, randomize_majors)]
    for arenas, pool, enabled in jobs:
        if enabled and arenas:
            out.update(
                match_arenas_to_bosses(
                    arenas=arenas,
                    bosses=pool,
                    rng=rng,
                    check_size=check_size,
                    forbidden=_family_forbidden(arenas, phase_mapping),
                )
            )
    return out


def _build_uniform_assignments(
    *,
    boss_clusters: Iterable[ClusterData],
    tags: Mapping[int, EntityTags],
    pool: dict[int, BossTags],
    phase_mapping: Mapping[int, int],
    randomize_majors: bool,
    check_size: bool,
    seed: int,
) -> dict[int, int]:
    """Match every randomized boss arena against a single allowlist pool.

    Collapses the major/minor distinction (uniform mode for ``enemy.bosses``):
    minor arenas are always included; major arenas only when ``randomize_majors``
    is set (``randomize_bosses == "all"``). Multi-phase arenas contribute one
    slot per phase entity, mirroring ``_build_enemy_assignments``. Reuse is
    permitted via ``assign_bosses_uniform``.

    Self/family placement is avoided best-effort: the allowlist stays
    authoritative when it offers no alternative.

    Raises ``KeyError`` if a DAG boss cluster's leader (or phase-1 sibling) has
    no entry in ``tags`` or no ``arena`` block, matching the strictness of
    ``_build_enemy_assignments``.
    """
    arenas: dict[int, ArenaTags] = {}
    for cluster in boss_clusters:
        if cluster.type not in ASSIGNABLE_ARENA_TYPES:
            continue
        if cluster.type in MAJOR_ARENA_TYPES and not randomize_majors:
            continue
        leader = resolve_entity_id(cluster.defeat_flag)
        slots = [leader]
        phase1 = phase_mapping.get(leader)
        if phase1 is not None:
            slots.append(phase1)
        for eid in slots:
            entry = tags.get(eid)
            if entry is None:
                raise KeyError(
                    f"cluster {cluster.id!r} entity {eid} missing from "
                    f"boss_arena_tags.json"
                )
            if entry.arena is None:
                raise KeyError(
                    f"cluster {cluster.id!r} entity {eid} has no arena block "
                    f"in boss_arena_tags.json"
                )
            arenas[eid] = entry.arena

    if not arenas:
        return {}

    rng = random.Random(seed ^ BOSS_ASSIGNMENT_SEED_SALT)
    return assign_bosses_uniform(
        arenas=arenas,
        pool=pool,
        rng=rng,
        check_size=check_size,
        forbidden=_family_forbidden(arenas, phase_mapping),
    )


def _resolve_wrapper(platform: str | None) -> tuple[list[str], Path] | None:
    """Resolve how to launch ItemRandomizerWrapper on this platform.

    Returns the command prefix (Wine-wrapped on Linux) and the wrapper
    directory to run from, or None when the wrapper is not built or Wine is
    missing.
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
        return None

    # Detect platform
    if platform is None or platform == "auto":
        platform = "windows" if sys.platform == "win32" else "linux"

    # Check Wine availability on non-Windows
    if platform == "linux":
        if shutil.which("wine") is None:
            print(
                "Error: Wine not found. Install wine to run Item Randomizer on Linux.",
                file=sys.stderr,
            )
            return None
        return ["wine", str(wrapper_exe.resolve())], wrapper_dir

    return [str(wrapper_exe.resolve())], wrapper_dir


def vanilla_cache_is_complete(diste_dir: Path, game_dir: Path) -> bool:
    """Whether diste/Vanilla already holds every game file the randomizer needs.

    Mirrors RandomizerCommon's own check (``GameData.UnpackVanillaFiles``) so a
    warm cache costs no subprocess: ``Vanilla/files.txt`` lists
    ``<archive path> <archive> <size> <md5>`` per line (the randomizer only
    reads the first three, and its comments consider moving to the hash), a
    cached file is named after the archive path's file name (msgbnd entries
    keep their directories), and DLC entries are only required when the game
    has the DLC installed. A line the randomizer would reject counts as
    incomplete, so the extraction gets a chance to fix it.
    """
    vanilla_dir = diste_dir / "Vanilla"
    manifest = vanilla_dir / "files.txt"
    if not manifest.is_file():
        return False

    dlc_archive = game_dir / "DLC.bdt"
    has_dlc = dlc_archive.is_file() and dlc_archive.stat().st_size > 1000

    for line in manifest.read_text().splitlines():
        if not line.strip():
            continue
        parts = line.split()
        if len(parts) < 3 or not parts[2].isdigit():
            return False
        path, archive, expected_size = parts[0], parts[1], int(parts[2])
        name = path.lstrip("/") if "msgbnd" in path else path.rsplit("/", 1)[-1]
        cached = vanilla_dir / name
        if not cached.is_file():
            if archive == "DLC" and not has_dlc:
                continue
            return False
        if cached.stat().st_size != expected_size:
            return False

    return True


def _acquire_exclusive_lock(fd: int) -> None:
    """Block until an exclusive lock is held on *fd*."""
    if sys.platform == "win32":
        import msvcrt

        while True:
            try:
                msvcrt.locking(fd, msvcrt.LK_LOCK, 1)
                return
            except OSError as exc:
                # LK_LOCK gives up after about ten seconds; a cold-cache
                # extraction takes longer than that, so keep waiting. Any
                # other error (bad descriptor, filesystem without locking)
                # would spin forever instead.
                if exc.errno != errno.EDEADLOCK:
                    raise

    import fcntl

    fcntl.flock(fd, fcntl.LOCK_EX)


def _release_exclusive_lock(fd: int) -> None:
    """Release the lock taken by _acquire_exclusive_lock."""
    if sys.platform == "win32":
        import msvcrt

        msvcrt.locking(fd, msvcrt.LK_UNLCK, 1)
        return

    import fcntl

    fcntl.flock(fd, fcntl.LOCK_UN)


@contextmanager
def _exclusive_lock(lock_path: Path) -> Iterator[None]:
    """Hold an exclusive inter-process lock on *lock_path* for the block."""
    # Append mode: the file is only a lock token, and truncating one another
    # process holds a byte-range lock on is asking for trouble on Windows.
    with lock_path.open("a") as handle:
        _acquire_exclusive_lock(handle.fileno())
        try:
            yield
        finally:
            _release_exclusive_lock(handle.fileno())


@contextmanager
def vanilla_cache_guard(
    game_dir: Path,
    platform: str | None = None,
    verbose: bool = False,
) -> Iterator[bool]:
    """Hold the Item Randomizer's shared vanilla cache still for the block.

    RandomizerCommon extracts the game files it needs into ``diste/Vanilla``,
    a cache shared by every generation, and re-extracts all of them as soon as
    one is missing or has an unexpected size. Parallel generations would each
    start that extraction and write (and read) the same paths at once, which
    fails under Wine with "The process cannot access the file ... because it
    is being used by another process".

    The cache is therefore inspected, and extracted if needed, under an
    exclusive inter-process lock. The lock is released before the block when
    the cache came out complete: nothing extracts it any more, so generations
    run concurrently. When it cannot be completed (the installed game no
    longer matches the manifest the Item Randomizer shipped), the lock is held
    for the whole block instead, because ``Randomizer.Randomize`` then
    re-extracts the cache from inside the randomization itself, out of reach
    of this lock.

    Yields True when the cache is usable, False when the extraction failed.
    """
    wrapper = _resolve_wrapper(platform)
    if wrapper is None:
        yield False
        return
    cmd_prefix, wrapper_dir = wrapper

    diste_dir = wrapper_dir / "diste"
    if not diste_dir.is_dir():
        print(
            f"Error: Item Randomizer data directory not found: {diste_dir}",
            file=sys.stderr,
        )
        yield False
        return

    game_dir = game_dir.resolve()
    with ExitStack() as stack:
        stack.enter_context(_exclusive_lock(diste_dir / VANILLA_CACHE_LOCK))

        extraction_ok = True
        complete = vanilla_cache_is_complete(diste_dir, game_dir)
        if not complete:
            print("Extracting Item Randomizer vanilla game files (other runs wait)...")
            cmd = [
                *cmd_prefix,
                "--game-dir",
                str(game_dir),
                "--data-dir",
                str(diste_dir),
                "--extract-only",
            ]
            if verbose:
                print(f"Running: {' '.join(cmd)}")
            extraction_ok = stream_command(cmd, cwd=wrapper_dir) == 0
            if not extraction_ok:
                print(
                    "Error: vanilla cache extraction failed. A wrapper published "
                    "before --extract-only existed fails here: republish it with "
                    "python tools/bootstrap.py --game-dir <game> --itemrando <zip>",
                    file=sys.stderr,
                )
            complete = extraction_ok and vanilla_cache_is_complete(diste_dir, game_dir)

        if complete:
            # Nothing writes the cache any more: let the other generations in.
            stack.close()
        elif extraction_ok:
            print(
                "Warning: the installed game does not match the Item Randomizer's "
                "Vanilla/files.txt, so the randomizer re-extracts the cache on every "
                "run. Generations stay serialized until the Item Randomizer catches "
                "up with the game version."
            )

        yield extraction_ok


def ensure_vanilla_cache(
    game_dir: Path,
    platform: str | None = None,
    verbose: bool = False,
) -> bool:
    """Fill the shared vanilla cache now, so later generations find it warm.

    The guard a generation takes, entered and released immediately:
    ``tools/bootstrap.py`` calls this so the first batch of seeds after a
    setup does not have to extract anything (see vanilla_cache_guard).
    """
    with vanilla_cache_guard(game_dir, platform, verbose) as ok:
        return ok


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
    wrapper = _resolve_wrapper(platform)
    if wrapper is None:
        return False
    cmd_prefix, wrapper_dir = wrapper

    # Build command with absolute paths
    seed_dir = seed_dir.resolve()
    game_dir = game_dir.resolve()
    output_dir = output_dir.resolve()
    config_path = seed_dir / "item_config.json"

    cmd = [
        *cmd_prefix,
        str(config_path),
        "--game-dir",
        str(game_dir),
        "--data-dir",
        str(wrapper_dir / "diste"),
        "-o",
        str(output_dir),
    ]

    if verbose:
        print(f"Running: {' '.join(cmd)}")
        print(f"Working directory: {wrapper_dir}")

    # The shared vanilla cache must not be extracted by another generation
    # while this one reads it (see vanilla_cache_guard).
    with vanilla_cache_guard(game_dir, platform, verbose) as cache_ok:
        if not cache_ok:
            return False
        # Run from wrapper_dir so it finds diste/
        return stream_command(cmd, cwd=wrapper_dir) == 0
