"""DAG generation algorithm for SpeedFog.

Generates a randomized DAG with dynamic topology:
- Start: chapel_start cluster (natural split via multiple exits)
- Split/Merge/Passant operations for dynamic branching
- End: final_boss cluster (force merge before reaching)
"""

from __future__ import annotations

import random
from collections.abc import Callable
from dataclasses import dataclass
from enum import Enum, auto
from itertools import combinations

from speedfog.clusters import ClusterData, ClusterPool, fog_matches_spec
from speedfog.config import Config, resolve_final_boss_candidates
from speedfog.crosslinks import add_crosslinks
from speedfog.dag import Branch, Dag, DagNode, FogRef
from speedfog.planner import compute_tier, plan_layer_types
from speedfog.validator import ValidationResult, validate_dag


class GenerationError(Exception):
    """Error during DAG generation."""

    pass


# Valid cluster types for first_layer_type
VALID_FIRST_LAYER_TYPES = {"legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"}


def validate_config(
    config: Config, clusters: ClusterPool, boss_candidates: list[ClusterData]
) -> tuple[list[str], list[str]]:
    """Validate configuration options against available clusters.

    Args:
        config: Configuration to validate.
        clusters: Available cluster pool.
        boss_candidates: Pre-filtered list of clusters eligible as final boss.

    Returns:
        Tuple of (errors, warnings). Errors are blocking; warnings are informational.
    """
    errors: list[str] = []
    warnings: list[str] = []

    # Validate first_layer_type
    if config.structure.first_layer_type:
        if config.structure.first_layer_type not in VALID_FIRST_LAYER_TYPES:
            errors.append(
                f"Invalid first_layer_type: '{config.structure.first_layer_type}'. "
                f"Valid options: {', '.join(sorted(VALID_FIRST_LAYER_TYPES))}"
            )

    # Validate major_bosses
    if config.requirements.major_bosses < 0:
        errors.append(
            f"major_bosses must be >= 0, got {config.requirements.major_bosses}"
        )

    # Warn if total requirements exceed min_layers (some will be trimmed)
    total_requirements = (
        config.requirements.legacy_dungeons
        + config.requirements.bosses
        + config.requirements.mini_dungeons
        + config.requirements.major_bosses
    )
    if total_requirements > config.structure.min_layers:
        warnings.append(
            f"Total requirements ({total_requirements}) exceed min_layers "
            f"({config.structure.min_layers}); some types may be trimmed"
        )

    # Validate final_boss_candidates
    all_boss_clusters = boss_candidates
    all_boss_zones = {zone for cluster in all_boss_clusters for zone in cluster.zones}

    # Resolve "all" keyword and validate each zone
    resolved_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    for zone in resolved_candidates:
        if zone not in all_boss_zones:
            errors.append(f"Unknown final_boss candidate zone: '{zone}'")

    return errors, warnings


class LayerOperation(Enum):
    """Type of operation to perform on a layer."""

    PASSANT = auto()  # 1 branch -> 1 branch (per branch)
    SPLIT = auto()  # 1 branch -> N branches
    MERGE = auto()  # N branches -> 1 branch
    REBALANCE = auto()  # merge 2 + split 1 stale (same layer, N -> N)


@dataclass
class GenerationResult:
    """Result of DAG generation.

    Attributes:
        dag: The generated DAG.
        seed: The actual seed used for generation.
        validation: Validation result (with any warnings).
        attempts: Number of generation attempts made.
    """

    dag: Dag
    seed: int
    validation: ValidationResult
    attempts: int


# =============================================================================
# Cluster Compatibility Helpers
# =============================================================================


def _filter_exits_by_proximity(
    cluster: ClusterData, entry: dict, exits: list[dict]
) -> list[dict]:
    """Remove exits that share a proximity group with the entry."""
    if not cluster.proximity_groups:
        return exits

    entry_id = entry["fog_id"]
    entry_zone = entry["zone"]

    # Find all groups the entry belongs to
    blocked_specs: set[str] = set()
    for group in cluster.proximity_groups:
        entry_in_group = any(
            fog_matches_spec(entry_id, entry_zone, spec) for spec in group
        )
        if entry_in_group:
            blocked_specs.update(group)

    if not blocked_specs:
        return exits

    return [
        f
        for f in exits
        if not any(
            fog_matches_spec(f["fog_id"], f["zone"], spec) for spec in blocked_specs
        )
    ]


def compute_net_exits(cluster: ClusterData, consumed_entries: list[dict]) -> list[dict]:
    """Return exits remaining after consuming given entry fogs.

    A fog gate connecting two zones has two sides. Consuming an entry
    from zone A only removes the exit from zone A (same side), not
    the exit from zone B (opposite side of the same gate).

    Args:
        cluster: The cluster to check.
        consumed_entries: List of entry fog dicts {"fog_id", "zone"} being used.

    Returns:
        List of exit fog dicts remaining after consuming entries.
    """
    consumed_set = {(e["fog_id"], e["zone"]) for e in consumed_entries}
    return [
        f for f in cluster.exit_fogs if (f["fog_id"], f["zone"]) not in consumed_set
    ]


def count_net_exits(cluster: ClusterData, num_entries: int) -> int:
    """Minimum net exits when consuming num_entries (greedy: prefer non-bidirectional).

    This calculates the worst-case net exits by greedily selecting entries
    that cost the least (non-bidirectional entries have zero cost).
    Also accounts for proximity_groups: exits sharing a proximity group
    with any consumed entry are excluded.

    A fog is bidirectional only if the same (fog_id, zone) pair appears
    in both entry and exit lists - meaning the same side of the gate.

    Args:
        cluster: The cluster to check.
        num_entries: Number of entry fogs to consume.

    Returns:
        Minimum number of exits remaining after consuming num_entries.
    """
    if num_entries > len(cluster.entry_fogs):
        return 0

    if not cluster.proximity_groups:
        # Fast path: no proximity constraints
        exit_keys = {(f["fog_id"], f["zone"]) for f in cluster.exit_fogs}
        entry_costs: list[tuple[dict, int]] = []
        for entry in cluster.entry_fogs:
            key = (entry["fog_id"], entry["zone"])
            cost = 1 if key in exit_keys else 0
            entry_costs.append((entry, cost))
        entry_costs.sort(key=lambda x: x[1])
        consumed = [entry for entry, _ in entry_costs[:num_entries]]
        return len(compute_net_exits(cluster, consumed))

    # With proximity: worst-case across all entry combinations.
    # For each combination, compute net exits then filter by proximity
    # for each consumed entry. Return the minimum.
    min_exits = len(cluster.exit_fogs)
    for combo in combinations(cluster.entry_fogs, num_entries):
        consumed = list(combo)
        net = compute_net_exits(cluster, consumed)
        for entry in consumed:
            net = _filter_exits_by_proximity(cluster, entry, net)
        min_exits = min(min_exits, len(net))

    return min_exits


def can_be_split_node(cluster: ClusterData, num_out: int) -> bool:
    """Check if cluster can be a split node (1 entry -> num_out exits).

    Requires at least num_out exits after consuming 1 entry.
    Extra exits beyond num_out are left unmapped (no fog gate created).

    Args:
        cluster: The cluster to check.
        num_out: Minimum number of exits required after using 1 entry.

    Returns:
        True if cluster has enough exits for num_out branches.
    """
    if cluster.allow_entry_as_exit:
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= num_out
    return count_net_exits(cluster, 1) >= num_out


def can_be_merge_node(cluster: ClusterData, num_in: int) -> bool:
    """Check if cluster can be a merge node (num_in entries -> 1+ exit).

    With shared entrance enabled, multiple branches connect to the same
    entrance fog gate. Only needs 2+ entries + 1+ exit regardless of fan-in.
    Extra exits beyond 1 are left unmapped.

    Args:
        cluster: The cluster to check.
        num_in: Number of entry fogs to consume.

    Returns:
        True if cluster can serve as a merge node.
    """
    if cluster.allow_shared_entrance:
        return len(cluster.entry_fogs) >= 2 and len(cluster.exit_fogs) >= 1
    return len(cluster.entry_fogs) >= num_in and count_net_exits(cluster, num_in) >= 1


def can_be_passant_node(cluster: ClusterData) -> bool:
    """Check if cluster can be a passant node (1 entry -> 1+ exit).

    Requires at least 1 exit after consuming 1 entry.
    Extra exits are left unmapped (no fog gate created).

    Args:
        cluster: The cluster to check.

    Returns:
        True if cluster has at least 1 exit available after using 1 entry.
    """
    if cluster.allow_entry_as_exit:
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= 1
    return count_net_exits(cluster, 1) >= 1


def _stable_main_shuffle(entries: list[dict], rng: random.Random) -> list[dict]:
    """Shuffle entries with main-tagged ones first.

    Within each group (main vs non-main), order is randomized.
    This gives a soft preference to main entries without hard-excluding others.
    """
    main = [e for e in entries if e.get("main")]
    rest = [e for e in entries if not e.get("main")]
    rng.shuffle(main)
    rng.shuffle(rest)
    return main + rest


def select_entries_for_merge(
    cluster: ClusterData, num: int, rng: random.Random
) -> list[dict]:
    """Select entry fogs that maximize remaining exits.

    Prefers non-bidirectional entries to preserve more exits.
    A fog is bidirectional only if same (fog_id, zone) appears in both lists.
    Within each group, main-tagged entries are preferred.

    Args:
        cluster: The cluster to select entries from.
        num: Number of entries to select.
        rng: Random number generator.

    Returns:
        List of selected entry fog dicts {"fog_id", "zone"}.
    """
    exit_keys = {(f["fog_id"], f["zone"]) for f in cluster.exit_fogs}

    # Separate entries by cost (bidirectional = same side exists in exits)
    non_bidir = [
        e for e in cluster.entry_fogs if (e["fog_id"], e["zone"]) not in exit_keys
    ]
    bidir = [e for e in cluster.entry_fogs if (e["fog_id"], e["zone"]) in exit_keys]

    # Shuffle each group with main entries first
    non_bidir = _stable_main_shuffle(non_bidir, rng)
    bidir = _stable_main_shuffle(bidir, rng)

    # Take from non-bidir first, then bidir
    result = non_bidir[:num]
    remaining = num - len(result)
    if remaining > 0:
        result.extend(bidir[:remaining])

    return result


def pick_entry_with_max_exits(
    cluster: ClusterData, min_exits: int, rng: random.Random
) -> dict | None:
    """Pick an entry fog that leaves at least min_exits available.

    Prefers main-tagged entries when multiple valid entries exist.

    Args:
        cluster: The cluster to pick from.
        min_exits: Minimum required exits after using the entry.
        rng: Random number generator.

    Returns:
        The entry fog dict {"fog_id", "zone"}, or None if no valid entry exists.
    """
    valid_entries: list[dict] = []
    for entry in cluster.entry_fogs:
        remaining = compute_net_exits(cluster, [entry])
        remaining = _filter_exits_by_proximity(cluster, entry, remaining)
        if len(remaining) >= min_exits:
            valid_entries.append(entry)

    if not valid_entries:
        return None

    # Prefer main-tagged entries (boss arena main gate)
    main_entries = [e for e in valid_entries if e.get("main")]
    if main_entries:
        return rng.choice(main_entries)
    return rng.choice(valid_entries)


def pick_cluster_with_filter(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    filter_fn: Callable[[ClusterData], bool],
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> ClusterData | None:
    """Pick a cluster that passes the filter function.

    Args:
        candidates: List of candidate clusters.
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        filter_fn: Function that takes a ClusterData and returns bool.
        reserved_zones: Zones reserved for prerequisite placement (excluded).

    Returns:
        A cluster that passes the filter, or None if none available.
    """
    available = []
    for cluster in candidates:
        # Check no zone overlap (including reserved)
        if any(z in used_zones or z in reserved_zones for z in cluster.zones):
            continue

        # Check filter
        if not filter_fn(cluster):
            continue

        available.append(cluster)

    if not available:
        return None

    return rng.choice(available)


def pick_cluster_uniform(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> ClusterData | None:
    """Pick a cluster uniformly at random (no capability filter).

    Only checks zone overlap and reserved zones. Capability is determined
    after selection.

    Args:
        candidates: List of candidate clusters.
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        reserved_zones: Zones reserved for prerequisite placement (excluded).

    Returns:
        A random available cluster, or None if all zones overlap.
    """
    available = [
        c
        for c in candidates
        if not any(z in used_zones or z in reserved_zones for z in c.zones)
    ]
    if not available:
        return None
    return rng.choice(available)


# Types eligible for fallback when the planned type is exhausted.
_FALLBACK_TYPES = ("mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss")


def pick_cluster_with_type_fallback(
    clusters: ClusterPool,
    preferred_type: str,
    used_zones: set[str],
    rng: random.Random,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> ClusterData | None:
    """Pick a cluster of the preferred type, falling back to other types.

    Tries the preferred type first. If exhausted, tries other types in
    decreasing order of remaining available clusters.

    Args:
        clusters: Full cluster pool.
        preferred_type: Desired cluster type (e.g. "legacy_dungeon").
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        reserved_zones: Zones reserved for prerequisite placement.

    Returns:
        A random available cluster, or None if all types exhausted.
    """
    # Try preferred type first
    result = pick_cluster_uniform(
        clusters.get_by_type(preferred_type),
        used_zones,
        rng,
        reserved_zones=reserved_zones,
    )
    if result is not None:
        return result

    # Fallback: try other types sorted by remaining capacity (largest first)
    fallback_types = [t for t in _FALLBACK_TYPES if t != preferred_type]

    def _available_count(t: str) -> int:
        return sum(
            1
            for c in clusters.get_by_type(t)
            if not any(z in used_zones or z in reserved_zones for z in c.zones)
        )

    fallback_types.sort(key=_available_count, reverse=True)

    for t in fallback_types:
        result = pick_cluster_uniform(
            clusters.get_by_type(t),
            used_zones,
            rng,
            reserved_zones=reserved_zones,
        )
        if result is not None:
            return result

    return None


def determine_operation(
    cluster: ClusterData,
    branches: list[Branch],
    config: Config,
    rng: random.Random,
    *,
    current_layer: int = 0,
    force: LayerOperation | None = None,
) -> tuple[LayerOperation, int]:
    """Determine what operation to perform given a pre-selected cluster.

    Checks what the cluster can do (split, merge, passant) and decides
    based on configured probabilities and current DAG state.

    Args:
        cluster: Pre-selected cluster.
        branches: Current active branches.
        config: Configuration with probabilities and limits.
        rng: Random number generator.
        current_layer: Current layer index (used for min_branch_age check).
        force: If set, bypass probability roll for this operation type.
            Falls back to normal logic if the cluster can't perform it.

    Returns:
        Tuple of (operation, fan_out/fan_in). fan is 1 for PASSANT.
    """
    num_branches = len(branches)
    max_paths = config.structure.max_parallel_paths
    max_ex = config.structure.max_exits
    max_en = config.structure.max_entrances
    split_prob = config.structure.split_probability
    merge_prob = config.structure.merge_probability
    min_age = config.structure.min_branch_age

    # Determine split capability
    can_split = False
    split_fan = 2
    if max_ex >= 2 and num_branches < max_paths:
        room = max_paths - num_branches + 1
        max_fan = min(max_ex, room)
        for n in range(max_fan, 1, -1):
            if can_be_split_node(cluster, n):
                can_split = True
                split_fan = n
                break

    # Determine merge capability (respects min_branch_age)
    can_merge = (
        max_en >= 2
        and num_branches >= 2
        and _has_valid_merge_pair(
            branches, min_age=min_age, current_layer=current_layer
        )
        and can_be_merge_node(cluster, 2)
    )

    # Forced operation: bypass probability when spacing threshold exceeded
    if force == LayerOperation.SPLIT and can_split:
        return LayerOperation.SPLIT, split_fan
    if force == LayerOperation.MERGE:
        # Forced merge (from saturated spacing): bypass min_branch_age
        can_merge_forced = (
            max_en >= 2
            and num_branches >= 2
            and _has_valid_merge_pair(branches, min_age=0, current_layer=current_layer)
            and can_be_merge_node(cluster, 2)
        )
        if can_merge_forced:
            return LayerOperation.MERGE, 2

    # Decide based on capabilities.
    # When split_prob + merge_prob >= 1.0 (e.g. 0.9 + 0.5), the probabilities
    # act as a priority cascade: split is tried first, then merge gets the
    # remainder, and passant is only reached if the sum < 1.0.
    if can_split and can_merge:
        roll = rng.random()
        if roll < split_prob:
            return LayerOperation.SPLIT, split_fan
        elif roll < split_prob + merge_prob:
            return LayerOperation.MERGE, 2
        else:
            return LayerOperation.PASSANT, 1
    elif can_split:
        if rng.random() < split_prob:
            return LayerOperation.SPLIT, split_fan
    elif can_merge:
        if rng.random() < merge_prob:
            return LayerOperation.MERGE, 2

    return LayerOperation.PASSANT, 1


def _pick_entry_and_exits_for_node(
    cluster: ClusterData, min_exits: int, rng: random.Random
) -> tuple[FogRef, list[FogRef]]:
    """Pick an entry fog and exactly min_exits randomly-selected exits.

    Returns a randomized subset of available exits, trimmed to min_exits.
    With entry-as-exit, the entry's bidirectional pair is not consumed,
    so all exit_fogs are candidates. Otherwise, uses the standard
    net-exit calculation to determine candidates.

    Args:
        cluster: The cluster to pick entry/exits for.
        min_exits: Exact number of exits to return.
        rng: Random number generator.

    Returns:
        Tuple of (entry_fog, exit_fogs) where len(exit_fogs) == min_exits.

    Raises:
        GenerationError: If no valid entry fog found.
    """
    if cluster.allow_entry_as_exit:
        main_entries = [e for e in cluster.entry_fogs if e.get("main")]
        entry = (
            rng.choice(main_entries) if main_entries else rng.choice(cluster.entry_fogs)
        )
        entry_fog = FogRef(entry["fog_id"], entry["zone"])
        # Prefer exits that don't match the consumed entry;
        # entry-as-exit is a fallback for when more exits are needed (splits).
        entry_key = (entry["fog_id"], entry["zone"])
        preferred = [
            f for f in cluster.exit_fogs if (f["fog_id"], f["zone"]) != entry_key
        ]
        fallback = [
            f for f in cluster.exit_fogs if (f["fog_id"], f["zone"]) == entry_key
        ]
        rng.shuffle(preferred)
        rng.shuffle(fallback)
        ordered = preferred + fallback
        ordered = _filter_exits_by_proximity(cluster, entry, ordered)
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in ordered[:min_exits]]
        return entry_fog, exit_fogs

    picked = pick_entry_with_max_exits(cluster, min_exits, rng)
    if picked is None:
        raise GenerationError(
            f"Cluster {cluster.id} has no valid entry fog with {min_exits}+ exits"
        )
    entry_fog = FogRef(picked["fog_id"], picked["zone"])
    exits = compute_net_exits(cluster, [picked])
    exits = _filter_exits_by_proximity(cluster, picked, exits)
    rng.shuffle(exits)
    exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:min_exits]]
    return entry_fog, exit_fogs


def update_branch_counters(
    operation: LayerOperation,
    *,
    split_children: list[Branch] | None = None,
    passant_branches: list[Branch] | None = None,
    merged_branches: tuple[Branch, list[Branch]] | None = None,
) -> None:
    """Update layers_since_last_split counters in-place after an operation.

    Args:
        operation: The operation that was performed.
        split_children: New branches created by a split (counter set to 0).
        passant_branches: Branches that did passant this layer (counter += 1).
        merged_branches: Tuple of (merged_branch, source_branches) for merge.
            merged_branch gets max(sources). passant_branches get += 1.
    """
    if operation == LayerOperation.SPLIT:
        for b in split_children or []:
            b.layers_since_last_split = 0
        for b in passant_branches or []:
            b.layers_since_last_split += 1

    elif operation == LayerOperation.MERGE:
        if merged_branches is not None:
            merged, sources = merged_branches
            merged.layers_since_last_split = max(
                s.layers_since_last_split for s in sources
            )
        for b in passant_branches or []:
            b.layers_since_last_split += 1

    elif operation == LayerOperation.PASSANT:
        for b in passant_branches or []:
            b.layers_since_last_split += 1


def _execute_spacing_rebalance(
    dag: Dag,
    branches: list[Branch],
    current_layer: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    config: Config,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> list[Branch] | None:
    """Combined merge + split on the same layer for saturated spacing.

    When max_parallel_paths is reached and a branch exceeds the spacing
    threshold, doing merge-then-split on separate layers causes oscillation
    (branch count bounces between N and N-1). Instead, this function merges
    2 branches AND splits the stale branch on the same layer, keeping the
    total branch count constant.

    Returns updated branches, or None if the rebalance can't be performed
    (caller should fall back to normal flow).
    """
    # 1. Identify the most stale branch (split target)
    stale_idx = max(
        range(len(branches)),
        key=lambda i: branches[i].layers_since_last_split,
    )

    # 2. Find 2 merge candidates among other branches (bypass min_age,
    #    enforce anti-micro-merge: different parent nodes)
    other_indices = [i for i in range(len(branches)) if i != stale_idx]
    rng.shuffle(other_indices)
    merge_pair: tuple[int, int] | None = None
    for i in range(len(other_indices)):
        for j in range(i + 1, len(other_indices)):
            a, b = other_indices[i], other_indices[j]
            if branches[a].current_node_id != branches[b].current_node_id:
                merge_pair = (a, b)
                break
        if merge_pair:
            break
    if merge_pair is None:
        return None

    # 3. Pick a split-capable cluster (try preferred type, then others)
    split_cluster = None
    all_types = [layer_type] + [t for t in clusters.by_type if t != layer_type]
    for t in all_types:
        split_cluster = pick_cluster_with_filter(
            clusters.get_by_type(t),
            used_zones,
            rng,
            lambda c: can_be_split_node(c, 2),
            reserved_zones=reserved_zones,
        )
        if split_cluster is not None:
            break
    if split_cluster is None:
        return None

    # 4. Pick a merge-capable cluster (try preferred type, then others)
    used_after_split = used_zones | set(split_cluster.zones)
    merge_cluster = None
    for t in all_types:
        merge_cluster = pick_cluster_with_filter(
            clusters.get_by_type(t),
            used_after_split,
            rng,
            lambda c: can_be_merge_node(c, 2),
            reserved_zones=reserved_zones,
        )
        if merge_cluster is not None:
            break
    if merge_cluster is None:
        return None

    new_branches: list[Branch] = []
    letter = 0

    # A. Split the stale branch
    stale_branch = branches[stale_idx]
    used_zones.update(split_cluster.zones)
    entry_fog, exit_fogs = _pick_entry_and_exits_for_node(split_cluster, 2, rng)
    split_node_id = f"node_{current_layer}_{chr(97 + letter)}"
    split_node = DagNode(
        id=split_node_id,
        cluster=split_cluster,
        layer=current_layer,
        tier=tier,
        entry_fogs=[entry_fog],
        exit_fogs=exit_fogs,
    )
    dag.add_node(split_node)
    dag.add_edge(
        stale_branch.current_node_id,
        split_node_id,
        stale_branch.available_exit,
        entry_fog,
    )
    split_children: list[Branch] = []
    for j in range(2):
        split_children.append(
            Branch(
                f"{stale_branch.id}_{chr(97 + j)}",
                split_node_id,
                exit_fogs[j],
                birth_layer=current_layer,
                layers_since_last_split=0,
            )
        )
    new_branches.extend(split_children)
    letter += 1

    # B. Merge the pair
    merge_a, merge_b = merge_pair
    merge_branches_list = [branches[merge_a], branches[merge_b]]
    used_zones.update(merge_cluster.zones)

    if merge_cluster.allow_shared_entrance:
        entries = select_entries_for_merge(merge_cluster, 1, rng)
        shared_entry = FogRef(entries[0]["fog_id"], entries[0]["zone"])
        entry_fogs_list = [shared_entry]
        exits = compute_net_exits(merge_cluster, entries)
        for e in entries:
            exits = _filter_exits_by_proximity(merge_cluster, e, exits)
    else:
        entries = select_entries_for_merge(merge_cluster, 2, rng)
        entry_fogs_list = [FogRef(e["fog_id"], e["zone"]) for e in entries]
        exits = compute_net_exits(merge_cluster, entries)
        for e in entries:
            exits = _filter_exits_by_proximity(merge_cluster, e, exits)

    rng.shuffle(exits)
    merge_exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]
    if not merge_exit_fogs:
        return None

    merge_node_id = f"node_{current_layer}_{chr(97 + letter)}"
    merge_node = DagNode(
        id=merge_node_id,
        cluster=merge_cluster,
        layer=current_layer,
        tier=tier,
        entry_fogs=entry_fogs_list,
        exit_fogs=merge_exit_fogs,
    )
    dag.add_node(merge_node)

    if merge_cluster.allow_shared_entrance:
        for mb in merge_branches_list:
            dag.add_edge(
                mb.current_node_id, merge_node_id, mb.available_exit, shared_entry
            )
    else:
        for mb, ef in zip(merge_branches_list, entry_fogs_list, strict=False):
            dag.add_edge(mb.current_node_id, merge_node_id, mb.available_exit, ef)

    # Merged branch inherits max counter; update_branch_counters will += 1
    merged_counter = max(b.layers_since_last_split for b in merge_branches_list)
    merged_branch = Branch(
        f"merged_{current_layer}",
        merge_node_id,
        rng.choice(merge_exit_fogs),
        birth_layer=current_layer,
        layers_since_last_split=merged_counter,
    )
    new_branches.append(merged_branch)
    letter += 1

    # C. Passant for remaining branches
    handled = {stale_idx, merge_a, merge_b}
    for i, branch in enumerate(branches):
        if i in handled:
            continue
        pc = pick_cluster_with_type_fallback(
            clusters, layer_type, used_zones, rng, reserved_zones=reserved_zones
        )
        if pc is None:
            return None
        used_zones.update(pc.zones)
        ef, exf = _pick_entry_and_exits_for_node(pc, 1, rng)
        nid = f"node_{current_layer}_{chr(97 + letter)}"
        n = DagNode(
            id=nid,
            cluster=pc,
            layer=current_layer,
            tier=tier,
            entry_fogs=[ef],
            exit_fogs=exf,
        )
        dag.add_node(n)
        dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
        new_branches.append(
            Branch(
                branch.id,
                nid,
                rng.choice(exf),
                birth_layer=branch.birth_layer,
                layers_since_last_split=branch.layers_since_last_split,
            )
        )
        letter += 1

    # D. Update counters: split children = 0, everyone else += 1
    update_branch_counters(
        LayerOperation.SPLIT,
        split_children=split_children,
        passant_branches=[b for b in new_branches if b not in split_children],
    )

    return new_branches


def execute_passant_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> list[Branch]:
    """Execute a passant layer where each branch advances to its own new node.

    Args:
        dag: The DAG being built.
        branches: Current branches.
        layer_idx: Current layer index.
        tier: Difficulty tier for this layer.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        reserved_zones: Zones reserved for prerequisite placement (excluded).

    Returns:
        Updated list of branches.

    Raises:
        GenerationError: If no suitable cluster found.
    """
    new_branches: list[Branch] = []
    candidates = clusters.get_by_type(layer_type)

    for i, branch in enumerate(branches):
        cluster = pick_cluster_with_filter(
            candidates,
            used_zones,
            rng,
            can_be_passant_node,
            reserved_zones=reserved_zones,
        )
        if cluster is None:
            raise GenerationError(
                f"No passant-compatible cluster for layer {layer_idx} branch {i} (type: {layer_type})"
            )
        used_zones.update(cluster.zones)

        entry_fog, exit_fogs = _pick_entry_and_exits_for_node(cluster, 1, rng)

        node_id = f"node_{layer_idx}_{chr(97 + i)}"
        node = DagNode(
            id=node_id,
            cluster=cluster,
            layer=layer_idx,
            tier=tier,
            entry_fogs=[entry_fog],
            exit_fogs=exit_fogs,
        )
        dag.add_node(node)
        dag.add_edge(branch.current_node_id, node_id, branch.available_exit, entry_fog)

        new_branches.append(
            Branch(
                branch.id,
                node_id,
                rng.choice(exit_fogs),
                birth_layer=branch.birth_layer,
                layers_since_last_split=branch.layers_since_last_split,
            )
        )

    return new_branches


def _has_valid_merge_pair(
    branches: list[Branch],
    *,
    min_age: int = 0,
    current_layer: int = 0,
) -> bool:
    """Check if any two age-eligible branches have different current nodes."""
    eligible = [b for b in branches if current_layer - b.birth_layer >= min_age]
    node_ids = {b.current_node_id for b in eligible}
    return len(node_ids) >= 2


def _find_valid_merge_indices(
    branches: list[Branch],
    rng: random.Random,
    count: int = 2,
    *,
    min_age: int = 0,
    current_layer: int = 0,
) -> list[int] | None:
    """Select branch indices with at least 2 different parent nodes for merging.

    Prevents micro split-merge where all merging branches come from the same
    split node, creating a pointless fan-out/fan-in. When min_age > 0,
    only branches old enough (current_layer - birth_layer >= min_age) are
    eligible for merging.

    Args:
        branches: Current branches.
        rng: Random number generator.
        count: Number of branches to select for merging.
        min_age: Minimum age (in layers) for a branch to be merge-eligible.
        current_layer: Current layer index (used with min_age).

    Returns:
        List of branch indices, or None if no valid selection exists.
    """
    # Only consider age-eligible branches
    eligible_indices = [
        i for i, b in enumerate(branches) if current_layer - b.birth_layer >= min_age
    ]

    if count > len(eligible_indices):
        return None

    valid_combos: list[list[int]] = []
    for combo in combinations(eligible_indices, count):
        parents = {branches[i].current_node_id for i in combo}
        if len(parents) >= 2:
            valid_combos.append(list(combo))

    if not valid_combos:
        return None

    return rng.choice(valid_combos)


def execute_merge_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    config: Config,
    *,
    reserved_zones: frozenset[str] = frozenset(),
    min_age: int = 0,
) -> list[Branch]:
    """Execute a merge layer where N branches merge into one.

    The fan-in N is controlled by config.structure.max_entrances.
    Tries from max down to 2.

    Args:
        dag: The DAG being built.
        branches: Current branches (must have at least 2).
        layer_idx: Current layer index.
        tier: Difficulty tier for this layer.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        config: Configuration with max_entrances.
        reserved_zones: Zones reserved for prerequisite placement (excluded).
        min_age: Minimum branch age for merge eligibility (0=ignore).

    Returns:
        Updated list of branches (with fewer branches).

    Raises:
        GenerationError: If no suitable cluster found.
    """
    if len(branches) < 2:
        raise GenerationError("Cannot merge with fewer than 2 branches")

    max_merge = max(min(config.structure.max_entrances, len(branches)), 2)
    candidates = clusters.get_by_type(layer_type)

    # Try from max_merge down to 2: find valid indices AND matching cluster
    merge_indices: list[int] | None = None
    merge_cluster: ClusterData | None = None
    actual_merge = 2
    for n in range(max_merge, 1, -1):
        indices = _find_valid_merge_indices(
            branches, rng, n, min_age=min_age, current_layer=layer_idx
        )
        if indices is None:
            continue
        c = pick_cluster_with_filter(
            candidates,
            used_zones,
            rng,
            lambda c, n=n: can_be_merge_node(c, n),  # type: ignore[misc]
            reserved_zones=reserved_zones,
        )
        if c is not None:
            merge_indices = indices
            merge_cluster = c
            actual_merge = n
            break

    if merge_indices is None or merge_cluster is None:
        raise GenerationError(
            f"No merge-compatible cluster for layer {layer_idx} (type: {layer_type})"
        )

    merge_branches = [branches[i] for i in merge_indices]
    assert merge_cluster is not None  # narrowing for mypy
    cluster = merge_cluster

    new_branches: list[Branch] = []
    letter_offset = 0

    used_zones.update(cluster.zones)

    if cluster.allow_shared_entrance:
        # Shared entrance: all branches connect to the same entry fog.
        # Use select_entries_for_merge(num=1) to prefer non-bidirectional
        # entries, ensuring we don't consume exits unnecessarily.
        entries = select_entries_for_merge(cluster, 1, rng)
        shared_entry_fog = FogRef(entries[0]["fog_id"], entries[0]["zone"])
        entry_fogs_list = [shared_entry_fog]
        exits = compute_net_exits(cluster, entries)
        for e in entries:
            exits = _filter_exits_by_proximity(cluster, e, exits)
        rng.shuffle(exits)
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]
    else:
        # Original model: select N distinct entries
        entries = select_entries_for_merge(cluster, actual_merge, rng)
        entry_fogs_list = [FogRef(e["fog_id"], e["zone"]) for e in entries]
        exits = compute_net_exits(cluster, entries)
        for e in entries:
            exits = _filter_exits_by_proximity(cluster, e, exits)
        rng.shuffle(exits)
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]

    merge_node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
    merge_node = DagNode(
        id=merge_node_id,
        cluster=cluster,
        layer=layer_idx,
        tier=tier,
        entry_fogs=entry_fogs_list,
        exit_fogs=exit_fogs,
    )
    dag.add_node(merge_node)
    letter_offset += 1

    # Connect all merging branches to the merge node
    if cluster.allow_shared_entrance:
        # All branches connect to the same entry fog
        for branch in merge_branches:
            dag.add_edge(
                branch.current_node_id,
                merge_node_id,
                branch.available_exit,
                shared_entry_fog,
            )
    else:
        # Original model: each branch gets a distinct entry
        for branch, entry_fog in zip(merge_branches, entry_fogs_list, strict=False):
            dag.add_edge(
                branch.current_node_id,
                merge_node_id,
                branch.available_exit,
                entry_fog,
            )

    # Create single branch for merged path
    if not exit_fogs:
        raise GenerationError(
            f"Merge node {merge_node_id} ({cluster.id}): "
            "no exits remaining after consuming entries"
        )
    merged_counter = max(b.layers_since_last_split for b in merge_branches)
    new_branches.append(
        Branch(
            f"merged_{layer_idx}",
            merge_node_id,
            rng.choice(exit_fogs),
            birth_layer=layer_idx,
            layers_since_last_split=merged_counter,
        )
    )

    # Handle non-merged branches as passant
    merge_idx_set = set(merge_indices)
    for i, branch in enumerate(branches):
        if i in merge_idx_set:
            continue

        passant_cluster = pick_cluster_with_filter(
            candidates,
            used_zones,
            rng,
            can_be_passant_node,
            reserved_zones=reserved_zones,
        )
        if passant_cluster is None:
            raise GenerationError(
                f"No passant-compatible cluster for layer {layer_idx} branch {i} (type: {layer_type})"
            )
        used_zones.update(passant_cluster.zones)

        passant_entry_fog, exit_fogs = _pick_entry_and_exits_for_node(
            passant_cluster, 1, rng
        )

        node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
        node = DagNode(
            id=node_id,
            cluster=passant_cluster,
            layer=layer_idx,
            tier=tier,
            entry_fogs=[passant_entry_fog],
            exit_fogs=exit_fogs,
        )
        dag.add_node(node)
        dag.add_edge(
            branch.current_node_id, node_id, branch.available_exit, passant_entry_fog
        )

        new_branches.append(
            Branch(
                branch.id,
                node_id,
                rng.choice(exit_fogs),
                birth_layer=branch.birth_layer,
                layers_since_last_split=branch.layers_since_last_split,
            )
        )
        letter_offset += 1

    return new_branches


def execute_forced_merge(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    config: Config,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> tuple[list[Branch], int]:
    """Force all branches to merge into one.

    Repeatedly merges until only 1 branch remains. With N-ary merges
    controlled by config.structure.max_entrances, this may complete faster.

    Args:
        dag: The DAG being built.
        branches: Current branches.
        layer_idx: Starting layer index.
        tier: Difficulty tier.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        config: Configuration with max_entrances.
        reserved_zones: Zones reserved for prerequisite placement (excluded).

    Returns:
        Tuple of (list with single branch, final layer index used).

    Raises:
        GenerationError: If merging fails.
    """
    current_layer = layer_idx
    while len(branches) > 1:
        # Forced merge deliberately uses min_age=0 to guarantee convergence
        # regardless of branch age.
        if not _has_valid_merge_pair(branches):
            # All branches share the same source; insert passant to diverge
            branches = execute_passant_layer(
                dag,
                branches,
                current_layer,
                tier,
                layer_type,
                clusters,
                used_zones,
                rng,
                reserved_zones=reserved_zones,
            )
            current_layer += 1
        branches = execute_merge_layer(
            dag,
            branches,
            current_layer,
            tier,
            layer_type,
            clusters,
            used_zones,
            rng,
            config,
            reserved_zones=reserved_zones,
        )
        current_layer += 1

    return branches, current_layer


# =============================================================================
# Main DAG Generation
# =============================================================================


def _inject_prerequisite(
    dag: Dag,
    branches: list[Branch],
    current_layer: int,
    end_cluster: ClusterData,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    final_tier: int,
    *,
    tier_curve: str = "linear",
    tier_curve_exponent: float = 0.6,
) -> tuple[list[Branch], int]:
    """Inject mandatory prerequisite cluster before final boss if needed.

    When the final boss cluster has a `requires` field (e.g. leyndell_erdtree
    requires farumazula_maliketh), this places the prerequisite as a passant
    node on the single merged path just before the final boss.

    Args:
        dag: The DAG being built.
        branches: Current branches (should be exactly 1 after forced merge).
        current_layer: Current layer index.
        end_cluster: The pre-selected final boss cluster.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        final_tier: Final tier for tier computation.

    Returns:
        Tuple of (updated branches, updated layer index).

    Raises:
        GenerationError: If prerequisite cluster is not available.
    """
    if not end_cluster.requires:
        return branches, current_layer

    prereq_zone = end_cluster.requires

    # Find cluster containing the prerequisite zone
    prereq: ClusterData | None = None
    for c in clusters.clusters:
        if prereq_zone in c.zones and not any(z in used_zones for z in c.zones):
            prereq = c
            break

    if prereq is None:
        raise GenerationError(f"Prerequisite cluster not available: {prereq_zone}")

    used_zones.update(prereq.zones)
    tier = compute_tier(
        current_layer,
        current_layer + 2,
        final_tier,
        curve=tier_curve,
        exponent=tier_curve_exponent,
    )
    ef, exf = _pick_entry_and_exits_for_node(prereq, 1, rng)
    node_id = f"node_{current_layer}_a"
    node = DagNode(
        id=node_id,
        cluster=prereq,
        layer=current_layer,
        tier=tier,
        entry_fogs=[ef],
        exit_fogs=exf,
    )
    dag.add_node(node)

    assert (
        len(branches) == 1
    ), f"Expected 1 branch for prerequisite, got {len(branches)}"
    branch = branches[0]
    dag.add_edge(branch.current_node_id, node_id, branch.available_exit, ef)

    return [
        Branch("prereq", node_id, rng.choice(exf), birth_layer=current_layer)
    ], current_layer + 1


def generate_dag(
    config: Config,
    clusters: ClusterPool,
    seed: int | None = None,
    *,
    boss_candidates: list[ClusterData],
) -> Dag:
    """Generate a randomized DAG with dynamic split/merge/passant topology.

    Algorithm:
    1. Create start node (no entry consumed, all exits available)
    2. Initialize branches from start exits
    3. Plan layer types based on requirements
    4. Execute layers with dynamic topology operations
    5. Force merge if multiple branches before final boss
    6. Connect to final boss

    Args:
        config: Configuration with requirements and structure
        clusters: Pool of available clusters
        seed: Random seed (uses config.seed if None)
        boss_candidates: Pre-filtered list of clusters eligible as final boss.

    Returns:
        Generated DAG

    Raises:
        GenerationError: If generation fails (not enough clusters)
    """
    if seed is None:
        seed = config.seed

    rng = random.Random(seed)
    dag = Dag(seed=seed)
    used_zones: set[str] = set()

    # 1. Create start node
    start_candidates = clusters.get_by_type("start")
    if not start_candidates:
        raise GenerationError("No start cluster found")

    # Start cluster has no entry fogs, so zone-overlap filter is sufficient
    start_cluster = pick_cluster_uniform(start_candidates, used_zones, rng)
    if start_cluster is None:
        raise GenerationError("Could not pick start cluster")

    # Start node: no entry consumed, all exits available
    start_node = DagNode(
        id="start",
        cluster=start_cluster,
        layer=0,
        tier=1,
        entry_fogs=[],  # Player spawns here, no entry fog consumed
        exit_fogs=[FogRef(f["fog_id"], f["zone"]) for f in start_cluster.exit_fogs],
    )
    dag.add_node(start_node)
    dag.start_id = "start"
    used_zones.update(start_cluster.zones)

    # 2. Initialize branches from start exits
    # Natural split at start based on available exits
    start_exits = start_node.exit_fogs
    num_initial_branches = min(
        len(start_exits),
        config.structure.max_parallel_paths,
        config.structure.max_exits,
    )

    if num_initial_branches == 0:
        raise GenerationError("Start cluster has no exits")

    # Shuffle exits for randomness
    rng.shuffle(start_exits)
    branches = [
        Branch(f"b{i}", "start", start_exits[i], birth_layer=0)
        for i in range(num_initial_branches)
    ]

    # 3. Pre-select final boss and compute reserved zones
    # Must happen before layer execution so prerequisite zones are reserved.
    all_boss_zones = {zone for cluster in boss_candidates for zone in cluster.zones}
    final_zone_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    final_zone_candidates = list(final_zone_candidates)  # Make a copy for shuffling
    rng.shuffle(final_zone_candidates)

    # Find a cluster matching one of the candidate zones
    end_cluster = None
    for zone_name in final_zone_candidates:
        for cluster in boss_candidates:
            if zone_name in cluster.zones:
                if not any(z in used_zones for z in cluster.zones):
                    end_cluster = cluster
                    break
        if end_cluster:
            break

    if end_cluster is None:
        raise GenerationError(
            f"No available final boss from candidates: {final_zone_candidates}"
        )

    # Determine reserved zones: end cluster + prerequisite
    # End cluster zones must be reserved to prevent intermediate layers
    # from consuming them.
    reserved_zones: frozenset[str] = frozenset(end_cluster.zones)
    if end_cluster.requires:
        prereq_zone = end_cluster.requires
        for candidate in clusters.clusters:
            if prereq_zone in candidate.zones:
                reserved_zones = reserved_zones | frozenset(candidate.zones)
                break

    # 4. Execute first layer if forced type (cluster-first passant)
    current_layer = 1
    if config.structure.first_layer_type:
        first_type = config.structure.first_layer_type
        tier = compute_tier(
            current_layer,
            10,
            config.structure.final_tier,
            curve=config.structure.tier_curve,
            exponent=config.structure.tier_curve_exponent,
        )
        first_candidates = clusters.get_by_type(first_type)

        new_branches: list[Branch] = []
        for i, branch in enumerate(branches):
            c = pick_cluster_uniform(
                first_candidates, used_zones, rng, reserved_zones=reserved_zones
            )
            if c is None:
                raise GenerationError(
                    f"No cluster for first layer branch {i} (type: {first_type})"
                )
            used_zones.update(c.zones)
            ef, exf = _pick_entry_and_exits_for_node(c, 1, rng)
            nid = f"node_{current_layer}_{chr(97 + i)}"
            n = DagNode(
                id=nid,
                cluster=c,
                layer=current_layer,
                tier=tier,
                entry_fogs=[ef],
                exit_fogs=exf,
            )
            dag.add_node(n)
            dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
            new_branches.append(
                Branch(
                    branch.id,
                    nid,
                    rng.choice(exf),
                    birth_layer=current_layer,
                    layers_since_last_split=branch.layers_since_last_split,
                )
            )
        branches = new_branches
        update_branch_counters(LayerOperation.PASSANT, passant_branches=branches)
        current_layer += 1

    # 5. Plan remaining layer types
    # Reserve max_parallel_paths layers for post-loop forced merges.
    # min_layers/max_layers refer to total layer count (start + end included).
    merge_reserve = config.structure.max_parallel_paths
    max_planned = max(
        config.structure.min_layers,
        config.structure.max_layers - merge_reserve,
    )
    target_total = rng.randint(config.structure.min_layers, max_planned)
    first_layer_offset = 1 if config.structure.first_layer_type else 0
    # Subtract start (1), end (1), optional first forced layer
    num_intermediate_layers = max(1, target_total - 2 - first_layer_offset)

    # Compute pool sizes per type for proportional padding
    pool_sizes = {
        t: len(clusters.get_by_type(t))
        for t in ("mini_dungeon", "boss_arena", "legacy_dungeon")
    }

    layer_types = plan_layer_types(
        config.requirements,
        num_intermediate_layers,
        rng,
        pool_sizes=pool_sizes,
    )

    # Estimated total for tier computation (planned + merge reserve)
    estimated_total = target_total + merge_reserve

    # 6. Execute layers with cluster-first selection
    for layer_idx, layer_type in enumerate(layer_types):
        is_near_end = layer_idx >= len(layer_types) - 2
        tier = compute_tier(
            current_layer,
            estimated_total,
            config.structure.final_tier,
            curve=config.structure.tier_curve,
            exponent=config.structure.tier_curve_exponent,
        )

        # Force merge if near end and multiple branches
        if is_near_end and len(branches) > 1:
            branches, current_layer = execute_forced_merge(
                dag,
                branches,
                current_layer,
                tier,
                layer_type,
                clusters,
                used_zones,
                rng,
                config,
                reserved_zones=reserved_zones,
            )
            continue

        # --- Max branch spacing enforcement ---
        max_spacing = config.structure.max_branch_spacing
        force_op: LayerOperation | None = None

        if max_spacing > 0 and not is_near_end:
            max_stale = max(b.layers_since_last_split for b in branches)
            needs_forced_split = max_stale >= max_spacing

            if needs_forced_split:
                if len(branches) >= config.structure.max_parallel_paths:
                    # Saturated — merge 2 branches + split the stale
                    # branch on the same layer to avoid oscillation.
                    result = _execute_spacing_rebalance(
                        dag,
                        branches,
                        current_layer,
                        tier,
                        layer_type,
                        clusters,
                        used_zones,
                        rng,
                        config,
                        reserved_zones=reserved_zones,
                    )
                    if result is not None:
                        branches = result
                        current_layer += 1
                        continue
                    # Fallback: force a merge, split next iteration
                    force_op = LayerOperation.MERGE
                else:
                    force_op = LayerOperation.SPLIT

        # Pick a cluster for the "primary" branch action.
        # When forcing an operation (split or merge for spacing enforcement),
        # select a capable cluster to guarantee the operation can proceed.
        # Without this, random selection may pick incapable clusters for
        # several consecutive layers, defeating the spacing guarantee.
        if force_op == LayerOperation.SPLIT:
            # Try preferred type first, then all types for a split-capable cluster
            primary_cluster = None
            for t in [layer_type] + [t for t in clusters.by_type if t != layer_type]:
                primary_cluster = pick_cluster_with_filter(
                    clusters.get_by_type(t),
                    used_zones,
                    rng,
                    lambda c: can_be_split_node(c, 2),
                    reserved_zones=reserved_zones,
                )
                if primary_cluster is not None:
                    break
            if primary_cluster is None:
                # No split-capable cluster in any type — accept passant
                primary_cluster = pick_cluster_with_type_fallback(
                    clusters,
                    layer_type,
                    used_zones,
                    rng,
                    reserved_zones=reserved_zones,
                )
        elif force_op == LayerOperation.MERGE:
            # Try preferred type first, then all types for a merge-capable cluster
            primary_cluster = None
            for t in [layer_type] + [t for t in clusters.by_type if t != layer_type]:
                primary_cluster = pick_cluster_with_filter(
                    clusters.get_by_type(t),
                    used_zones,
                    rng,
                    lambda c: can_be_merge_node(c, 2),
                    reserved_zones=reserved_zones,
                )
                if primary_cluster is not None:
                    break
            if primary_cluster is None:
                primary_cluster = pick_cluster_with_type_fallback(
                    clusters,
                    layer_type,
                    used_zones,
                    rng,
                    reserved_zones=reserved_zones,
                )
        else:
            primary_cluster = pick_cluster_with_type_fallback(
                clusters, layer_type, used_zones, rng, reserved_zones=reserved_zones
            )
        if primary_cluster is None:
            raise GenerationError(
                f"No cluster available for layer {current_layer} "
                f"(type: {layer_type})"
            )

        # Determine operation from cluster capabilities
        operation, fan = determine_operation(
            primary_cluster,
            branches,
            config,
            rng,
            current_layer=current_layer,
            force=force_op,
        )

        if operation == LayerOperation.SPLIT:
            # Pick which branch to split — prefer most stale when forced
            if force_op == LayerOperation.SPLIT:
                max_stale_val = max(b.layers_since_last_split for b in branches)
                stale_indices = [
                    i
                    for i, b in enumerate(branches)
                    if b.layers_since_last_split == max_stale_val
                ]
                split_idx = rng.choice(stale_indices)
            else:
                split_idx = rng.randrange(len(branches))
            split_child_branches: list[Branch] = []
            passant_branches_list: list[Branch] = []
            letter_offset = 0

            for i, branch in enumerate(branches):
                if i == split_idx:
                    used_zones.update(primary_cluster.zones)
                    entry_fog, exit_fogs = _pick_entry_and_exits_for_node(
                        primary_cluster, fan, rng
                    )
                    node_id = f"node_{current_layer}_{chr(97 + letter_offset)}"
                    node = DagNode(
                        id=node_id,
                        cluster=primary_cluster,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[entry_fog],
                        exit_fogs=exit_fogs,
                    )
                    dag.add_node(node)
                    dag.add_edge(
                        branch.current_node_id,
                        node_id,
                        branch.available_exit,
                        entry_fog,
                    )
                    for j in range(fan):
                        split_child_branches.append(
                            Branch(
                                f"{branch.id}_{chr(97 + j)}",
                                node_id,
                                exit_fogs[j],
                                birth_layer=current_layer,
                                layers_since_last_split=0,  # Reset: player just had a choice
                            )
                        )
                    letter_offset += 1
                else:
                    # Passant for non-split branches, with type fallback.
                    pc = pick_cluster_with_type_fallback(
                        clusters,
                        layer_type,
                        used_zones,
                        rng,
                        reserved_zones=reserved_zones,
                    )
                    if pc is None:
                        raise GenerationError(
                            f"No cluster for layer {current_layer} branch {i} "
                            f"(type: {layer_type})"
                        )
                    used_zones.update(pc.zones)
                    ef, exf = _pick_entry_and_exits_for_node(pc, 1, rng)
                    nid = f"node_{current_layer}_{chr(97 + letter_offset)}"
                    n = DagNode(
                        id=nid,
                        cluster=pc,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[ef],
                        exit_fogs=exf,
                    )
                    dag.add_node(n)
                    dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
                    passant_branches_list.append(
                        Branch(
                            branch.id,
                            nid,
                            rng.choice(exf),
                            birth_layer=branch.birth_layer,
                            layers_since_last_split=branch.layers_since_last_split,
                        )
                    )
                    letter_offset += 1

            update_branch_counters(
                LayerOperation.SPLIT,
                split_children=split_child_branches,
                passant_branches=passant_branches_list,
            )
            branches = split_child_branches + passant_branches_list

        elif operation == LayerOperation.MERGE:
            # Find merge indices and actual fan-in
            # Forced merge (saturated spacing) bypasses min_branch_age
            min_age = (
                0
                if force_op == LayerOperation.MERGE
                else config.structure.min_branch_age
            )
            max_merge = max(min(config.structure.max_entrances, len(branches)), 2)
            merge_indices: list[int] | None = None
            actual_merge = 2
            for merge_n in range(max_merge, 1, -1):
                if can_be_merge_node(primary_cluster, merge_n):
                    indices = _find_valid_merge_indices(
                        branches,
                        rng,
                        merge_n,
                        min_age=min_age,
                        current_layer=current_layer,
                    )
                    if indices is not None:
                        merge_indices = indices
                        actual_merge = merge_n
                        break

            if merge_indices is None:
                merge_indices = _find_valid_merge_indices(
                    branches,
                    rng,
                    2,
                    min_age=min_age,
                    current_layer=current_layer,
                )

            if merge_indices is None:
                # Fallback: treat as passant
                operation = LayerOperation.PASSANT
            else:
                used_zones.update(primary_cluster.zones)
                merge_branches_list = [branches[i] for i in merge_indices]

                if primary_cluster.allow_shared_entrance:
                    entries = select_entries_for_merge(primary_cluster, 1, rng)
                    shared_entry = FogRef(entries[0]["fog_id"], entries[0]["zone"])
                    entry_fogs_list = [shared_entry]
                    exits = compute_net_exits(primary_cluster, entries)
                    for e in entries:
                        exits = _filter_exits_by_proximity(primary_cluster, e, exits)
                else:
                    entries = select_entries_for_merge(
                        primary_cluster, actual_merge, rng
                    )
                    entry_fogs_list = [FogRef(e["fog_id"], e["zone"]) for e in entries]
                    exits = compute_net_exits(primary_cluster, entries)
                    for e in entries:
                        exits = _filter_exits_by_proximity(primary_cluster, e, exits)

                rng.shuffle(exits)
                exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]

                merge_node_id = f"node_{current_layer}_a"
                merge_node = DagNode(
                    id=merge_node_id,
                    cluster=primary_cluster,
                    layer=current_layer,
                    tier=tier,
                    entry_fogs=entry_fogs_list,
                    exit_fogs=exit_fogs,
                )
                dag.add_node(merge_node)

                if primary_cluster.allow_shared_entrance:
                    for branch in merge_branches_list:
                        dag.add_edge(
                            branch.current_node_id,
                            merge_node_id,
                            branch.available_exit,
                            shared_entry,
                        )
                else:
                    for branch, ef in zip(
                        merge_branches_list, entry_fogs_list, strict=False
                    ):
                        dag.add_edge(
                            branch.current_node_id,
                            merge_node_id,
                            branch.available_exit,
                            ef,
                        )

                if not exit_fogs:
                    raise GenerationError(
                        f"Merge node {merge_node_id}: no exits remaining"
                    )

                new_branches = [
                    Branch(
                        f"merged_{current_layer}",
                        merge_node_id,
                        rng.choice(exit_fogs),
                        birth_layer=current_layer,
                        layers_since_last_split=0,  # Overwritten by update_branch_counters
                    )
                ]

                # Non-merged branches get passant, with type fallback.
                merge_set = set(merge_indices)
                letter = 1
                for i, branch in enumerate(branches):
                    if i in merge_set:
                        continue
                    pc = pick_cluster_with_type_fallback(
                        clusters,
                        layer_type,
                        used_zones,
                        rng,
                        reserved_zones=reserved_zones,
                    )
                    if pc is None:
                        raise GenerationError(
                            f"No cluster for layer {current_layer} branch {i} "
                            f"(type: {layer_type})"
                        )
                    used_zones.update(pc.zones)
                    ef, exf = _pick_entry_and_exits_for_node(pc, 1, rng)
                    nid = f"node_{current_layer}_{chr(97 + letter)}"
                    n = DagNode(
                        id=nid,
                        cluster=pc,
                        layer=current_layer,
                        tier=tier,
                        entry_fogs=[ef],
                        exit_fogs=exf,
                    )
                    dag.add_node(n)
                    dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
                    new_branches.append(
                        Branch(
                            branch.id,
                            nid,
                            rng.choice(exf),
                            birth_layer=branch.birth_layer,
                            layers_since_last_split=branch.layers_since_last_split,
                        )
                    )
                    letter += 1

                update_branch_counters(
                    LayerOperation.MERGE,
                    merged_branches=(new_branches[0], merge_branches_list),
                    passant_branches=new_branches[1:],
                )
                branches = new_branches

        # Passant fallback (also handles merge-fallback case)
        if operation == LayerOperation.PASSANT:
            new_branches = []
            first = True
            for i, branch in enumerate(branches):
                if first:
                    c = primary_cluster
                    first = False
                else:
                    c = pick_cluster_with_type_fallback(
                        clusters,
                        layer_type,
                        used_zones,
                        rng,
                        reserved_zones=reserved_zones,
                    )
                    if c is None:
                        raise GenerationError(
                            f"No cluster for layer {current_layer} branch {i} "
                            f"(type: {layer_type})"
                        )
                used_zones.update(c.zones)
                ef, exf = _pick_entry_and_exits_for_node(c, 1, rng)
                nid = f"node_{current_layer}_{chr(97 + i)}"
                n = DagNode(
                    id=nid,
                    cluster=c,
                    layer=current_layer,
                    tier=tier,
                    entry_fogs=[ef],
                    exit_fogs=exf,
                )
                dag.add_node(n)
                dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
                new_branches.append(
                    Branch(
                        branch.id,
                        nid,
                        rng.choice(exf),
                        birth_layer=branch.birth_layer,
                        layers_since_last_split=branch.layers_since_last_split,
                    )
                )
            update_branch_counters(
                LayerOperation.PASSANT,
                passant_branches=new_branches,
            )
            branches = new_branches

        current_layer += 1

    # 7. Final merge if still multiple branches
    if len(branches) > 1:
        # Use the last layer type for final merge operations
        last_layer_type = layer_types[-1] if layer_types else "mini_dungeon"
        tier = compute_tier(
            current_layer,
            estimated_total,
            config.structure.final_tier,
            curve=config.structure.tier_curve,
            exponent=config.structure.tier_curve_exponent,
        )
        branches, current_layer = execute_forced_merge(
            dag,
            branches,
            current_layer,
            tier,
            last_layer_type,
            clusters,
            used_zones,
            rng,
            config,
            reserved_zones=reserved_zones,
        )

    # 8. Inject prerequisite if needed (after merge, before final boss)
    branches, current_layer = _inject_prerequisite(
        dag,
        branches,
        current_layer,
        end_cluster,
        clusters,
        used_zones,
        rng,
        config.structure.final_tier,
        tier_curve=config.structure.tier_curve,
        tier_curve_exponent=config.structure.tier_curve_exponent,
    )

    # 9. Create end node (using pre-selected end_cluster)
    # Final boss has exactly 1 entry (from the single remaining branch)
    # Prefer main-tagged entry (boss arena main gate for correct Stake of Marika)
    entry_fog_end: FogRef | None = None
    if end_cluster.entry_fogs:
        main_entries = [e for e in end_cluster.entry_fogs if e.get("main")]
        chosen = (
            rng.choice(main_entries)
            if main_entries
            else rng.choice(end_cluster.entry_fogs)
        )
        entry_fog_end = FogRef(chosen["fog_id"], chosen["zone"])
    entry_fogs_end = [entry_fog_end] if entry_fog_end else []

    end_node = DagNode(
        id="end",
        cluster=end_cluster,
        layer=current_layer,
        tier=config.structure.final_tier,
        entry_fogs=entry_fogs_end,
        exit_fogs=[],  # No exits from final boss
    )
    dag.add_node(end_node)
    dag.end_id = "end"

    # Connect the single remaining branch to end
    if not branches:
        raise GenerationError("No branches remaining to connect to end")

    branch = branches[0]
    # Use entry_fog_end, or empty FogRef if None (final boss may have no entry fogs)
    dag.add_edge(
        branch.current_node_id,
        end_node.id,
        branch.available_exit,
        entry_fog_end or FogRef("", ""),
    )

    # Cross-link pass (post-hoc): add optional edges between parallel branches
    if config.structure.crosslinks:
        dag.crosslinks_added = add_crosslinks(dag, rng)

    return dag


def generate_with_retry(
    config: Config,
    clusters: ClusterPool,
    max_attempts: int = 100,
    *,
    boss_candidates: list[ClusterData],
) -> GenerationResult:
    """Generate DAG with automatic retry on failure.

    If config.seed is 0, tries random seeds until success (generation + validation).
    If config.seed is non-zero, uses that seed (fails if generation or validation fails).

    Args:
        config: Configuration
        clusters: Cluster pool
        max_attempts: Maximum retry attempts (only for seed=0)
        boss_candidates: Pre-filtered list of clusters eligible as final boss.

    Returns:
        GenerationResult with DAG, seed, validation, and attempt count.

    Raises:
        GenerationError: If generation fails after max_attempts
    """
    # Validate config before attempting generation
    config_errors, config_warnings = validate_config(config, clusters, boss_candidates)
    if config_errors:
        raise GenerationError(f"Invalid configuration: {'; '.join(config_errors)}")
    for warning in config_warnings:
        print(f"  Config warning: {warning}")

    if config.seed != 0:
        # Fixed seed - single attempt
        dag = generate_dag(
            config, clusters, config.seed, boss_candidates=boss_candidates
        )
        validation = validate_dag(dag, config, clusters)
        if not validation.is_valid:
            errors = "; ".join(validation.errors)
            raise GenerationError(f"Validation failed: {errors}")
        return GenerationResult(
            dag=dag,
            seed=config.seed,
            validation=validation,
            attempts=1,
        )

    # Auto-reroll mode
    base_rng = random.Random()

    for attempt in range(max_attempts):
        seed = base_rng.randint(1, 999999999)
        try:
            dag = generate_dag(config, clusters, seed, boss_candidates=boss_candidates)
            validation = validate_dag(dag, config, clusters)
            if not validation.is_valid:
                errors = "; ".join(validation.errors)
                raise GenerationError(f"Validation failed: {errors}")
            return GenerationResult(
                dag=dag,
                seed=seed,
                validation=validation,
                attempts=attempt + 1,
            )
        except GenerationError as e:
            print(f"Attempt {attempt + 1}: seed {seed} failed - {e}")
            continue

    raise GenerationError(f"Failed to generate DAG after {max_attempts} attempts")
