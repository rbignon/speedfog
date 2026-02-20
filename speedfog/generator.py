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

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.config import Config, resolve_final_boss_candidates
from speedfog.dag import Branch, Dag, DagNode, FogRef
from speedfog.planner import compute_tier, plan_layer_types
from speedfog.validator import ValidationResult, validate_dag


class GenerationError(Exception):
    """Error during DAG generation."""

    pass


# Valid cluster types for first_layer_type
VALID_FIRST_LAYER_TYPES = {"legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"}


def validate_config(config: Config, clusters: ClusterPool) -> list[str]:
    """Validate configuration options against available clusters.

    Args:
        config: Configuration to validate.
        clusters: Available cluster pool.

    Returns:
        List of error messages (empty if valid).
    """
    errors: list[str] = []

    # Validate first_layer_type
    if config.structure.first_layer_type:
        if config.structure.first_layer_type not in VALID_FIRST_LAYER_TYPES:
            errors.append(
                f"Invalid first_layer_type: '{config.structure.first_layer_type}'. "
                f"Valid options: {', '.join(sorted(VALID_FIRST_LAYER_TYPES))}"
            )

    # Validate major_boss_ratio
    if not 0.0 <= config.structure.major_boss_ratio <= 1.0:
        errors.append(
            f"major_boss_ratio must be between 0.0 and 1.0, "
            f"got {config.structure.major_boss_ratio}"
        )

    # Validate final_boss_candidates
    all_boss_clusters = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )
    all_boss_zones = {zone for cluster in all_boss_clusters for zone in cluster.zones}

    # Resolve "all" keyword and validate each zone
    resolved_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    for zone in resolved_candidates:
        if zone not in all_boss_zones:
            errors.append(f"Unknown final_boss candidate zone: '{zone}'")

    return errors


class LayerOperation(Enum):
    """Type of operation to perform on a layer."""

    PASSANT = auto()  # 1 branch -> 1 branch (per branch)
    SPLIT = auto()  # 1 branch -> N branches
    MERGE = auto()  # N branches -> 1 branch


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

    # Build set of exit (fog_id, zone) pairs for checking bidirectionality
    exit_keys = {(f["fog_id"], f["zone"]) for f in cluster.exit_fogs}

    # Calculate cost for each entry (1 if same side exists in exits, 0 otherwise)
    entry_costs: list[tuple[dict, int]] = []
    for entry in cluster.entry_fogs:
        key = (entry["fog_id"], entry["zone"])
        cost = 1 if key in exit_keys else 0
        entry_costs.append((entry, cost))

    # Sort by cost (cheapest first) and take num_entries
    entry_costs.sort(key=lambda x: x[1])
    consumed = [entry for entry, _ in entry_costs[:num_entries]]

    return len(compute_net_exits(cluster, consumed))


def can_be_split_node(cluster: ClusterData, num_out: int) -> bool:
    """Check if cluster can be a split node (1 entry -> num_out exits).

    All fogs must be mapped: 1 entry consumed + num_out exits used.

    Args:
        cluster: The cluster to check.
        num_out: Number of required exits after using 1 entry.

    Returns:
        True if cluster has exactly num_out net exits after using 1 entry.
    """
    return count_net_exits(cluster, 1) == num_out


def can_be_merge_node(cluster: ClusterData, num_in: int) -> bool:
    """Check if cluster can be a merge node (num_in entries -> 1 exit).

    With shared entrance enabled, multiple branches connect to the same
    entrance fog gate. Only needs 2+ entries + 1+ exit regardless of fan-in.

    Args:
        cluster: The cluster to check.
        num_in: Number of entry fogs to consume.

    Returns:
        True if cluster can serve as a merge node.
    """
    if cluster.allow_shared_entrance:
        # Shared entrance: require 2+ entries even with override, per spec constraint
        return len(cluster.entry_fogs) >= 2 and len(cluster.exit_fogs) >= 1
    return len(cluster.entry_fogs) >= num_in and count_net_exits(cluster, num_in) == 1


def can_be_passant_node(cluster: ClusterData) -> bool:
    """Check if cluster can be a passant node (1 entry -> 1 exit).

    All fogs must be mapped: 1 entry consumed + 1 exit used.

    Args:
        cluster: The cluster to check.

    Returns:
        True if cluster has exactly 1 net exit after using 1 entry.
    """
    return count_net_exits(cluster, 1) == 1


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
) -> ClusterData | None:
    """Pick a cluster that passes the filter function.

    Args:
        candidates: List of candidate clusters.
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        filter_fn: Function that takes a ClusterData and returns bool.

    Returns:
        A cluster that passes the filter, or None if none available.
    """
    available = []
    for cluster in candidates:
        # Check no zone overlap
        if any(z in used_zones for z in cluster.zones):
            continue

        # Check filter
        if not filter_fn(cluster):
            continue

        available.append(cluster)

    if not available:
        return None

    return rng.choice(available)


# =============================================================================
# Legacy Helper Functions (kept for compatibility)
# =============================================================================


def cluster_has_usable_exits(cluster: ClusterData) -> bool:
    """Check if cluster will have at least 1 exit after using any entry fog.

    A cluster is usable if for at least one entry_fog, there remains
    at least one exit_fog after removing the bidirectional entry.
    """
    if not cluster.entry_fogs:
        return False

    for entry in cluster.entry_fogs:
        entry_fog_id = entry["fog_id"]
        remaining_exits = [e for e in cluster.exit_fogs if e["fog_id"] != entry_fog_id]
        if remaining_exits:
            return True

    return False


def pick_entry_fog_with_exits(cluster: ClusterData, rng: random.Random) -> str | None:
    """Pick an entry fog that leaves at least one exit available.

    Returns the fog_id of a valid entry, or None if no valid entry exists.
    """
    valid_entries: list[str] = []
    for entry in cluster.entry_fogs:
        entry_fog_id = entry["fog_id"]
        remaining_exits = [e for e in cluster.exit_fogs if e["fog_id"] != entry_fog_id]
        if remaining_exits:
            valid_entries.append(entry_fog_id)

    if not valid_entries:
        return None

    return rng.choice(valid_entries)


def pick_cluster(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    require_exits: bool = True,
) -> ClusterData | None:
    """Pick a cluster whose zones don't overlap with used_zones.

    Args:
        candidates: List of candidate clusters
        used_zones: Set of zone IDs already used
        rng: Random number generator
        require_exits: If True, only pick clusters with usable exits
    """
    available = []
    for cluster in candidates:
        # Check no zone overlap
        if any(z in used_zones for z in cluster.zones):
            continue

        # Check cluster has usable exits (unless it's the final node)
        if require_exits and not cluster_has_usable_exits(cluster):
            continue

        available.append(cluster)

    if not available:
        return None

    return rng.choice(available)


# =============================================================================
# Layer Operation Logic
# =============================================================================


def decide_operation(
    num_branches: int, config: Config, rng: random.Random
) -> LayerOperation:
    """Decide which operation to perform based on current branch count.

    Args:
        num_branches: Current number of parallel branches.
        config: Configuration with probabilities.
        rng: Random number generator.

    Returns:
        The operation to perform.
    """
    max_paths = config.structure.max_parallel_paths
    max_branches = config.structure.max_branches
    split_prob = config.structure.split_probability
    merge_prob = config.structure.merge_probability

    # Splits and merges need fan-out >= 2
    if max_branches < 2:
        return LayerOperation.PASSANT

    if num_branches >= max_paths:
        # At max: can only merge or passant
        return (
            LayerOperation.MERGE
            if rng.random() < merge_prob
            else LayerOperation.PASSANT
        )
    elif num_branches == 1:
        # At min: can only split or passant
        return (
            LayerOperation.SPLIT
            if rng.random() < split_prob
            else LayerOperation.PASSANT
        )
    else:
        # Can do any operation
        roll = rng.random()
        if roll < split_prob:
            return LayerOperation.SPLIT
        elif roll < split_prob + merge_prob:
            return LayerOperation.MERGE
        else:
            return LayerOperation.PASSANT


def execute_passant_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
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

    Returns:
        Updated list of branches.

    Raises:
        GenerationError: If no suitable cluster found.
    """
    new_branches: list[Branch] = []
    candidates = clusters.get_by_type(layer_type)

    for i, branch in enumerate(branches):
        cluster = pick_cluster_with_filter(
            candidates, used_zones, rng, can_be_passant_node
        )
        if cluster is None:
            raise GenerationError(
                f"No passant-compatible cluster for layer {layer_idx} branch {i} (type: {layer_type})"
            )
        used_zones.update(cluster.zones)

        # Pick entry that leaves at least 1 exit
        entry = pick_entry_with_max_exits(cluster, 1, rng)
        if entry is None:
            raise GenerationError(
                f"Cluster {cluster.id} has no valid entry fog with exits"
            )

        entry_fog = FogRef(entry["fog_id"], entry["zone"])
        exits = compute_net_exits(cluster, [entry])
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits]

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

        new_branches.append(Branch(branch.id, node_id, rng.choice(exit_fogs)))

    return new_branches


def execute_split_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    config: Config,
) -> list[Branch]:
    """Execute a split layer where one branch splits into N.

    The fan-out N is controlled by config.structure.max_branches, capped by
    available room under max_parallel_paths. Tries from max down to 2.

    Args:
        dag: The DAG being built.
        branches: Current branches.
        layer_idx: Current layer index.
        tier: Difficulty tier for this layer.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        config: Configuration with max_branches and max_parallel_paths.

    Returns:
        Updated list of branches.

    Raises:
        GenerationError: If no suitable cluster found.
    """
    split_idx = rng.randrange(len(branches))
    new_branches: list[Branch] = []
    candidates = clusters.get_by_type(layer_type)
    letter_offset = 0

    # Max fan-out: limited by max_branches and room under max_parallel_paths
    # Splitting replaces 1 branch with N, so net increase is N-1
    room = config.structure.max_parallel_paths - len(branches) + 1
    max_fan_out = min(config.structure.max_branches, room)

    for i, branch in enumerate(branches):
        if i == split_idx:
            # Try N-ary split from max_fan_out down to 2
            cluster = None
            actual_fan_out = 2
            for n in range(max_fan_out, 1, -1):
                cluster = pick_cluster_with_filter(
                    candidates,
                    used_zones,
                    rng,
                    lambda c, n=n: can_be_split_node(c, n),  # type: ignore[misc]
                )
                if cluster is not None:
                    actual_fan_out = n
                    break

            if cluster is None:
                raise GenerationError(
                    f"No split-compatible cluster for layer {layer_idx} (type: {layer_type})"
                )
            used_zones.update(cluster.zones)

            entry = pick_entry_with_max_exits(cluster, actual_fan_out, rng)
            if entry is None:
                raise GenerationError(
                    f"Cluster {cluster.id} has no valid entry fog with {actual_fan_out}+ exits"
                )

            entry_fog = FogRef(entry["fog_id"], entry["zone"])
            exits = compute_net_exits(cluster, [entry])
            exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits]

            node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
            node = DagNode(
                id=node_id,
                cluster=cluster,
                layer=layer_idx,
                tier=tier,
                entry_fogs=[entry_fog],
                exit_fogs=exit_fogs,
            )
            dag.add_node(node)
            dag.add_edge(
                branch.current_node_id, node_id, branch.available_exit, entry_fog
            )

            # Create actual_fan_out new branches
            rng.shuffle(exit_fogs)
            for j in range(actual_fan_out):
                suffix = chr(97 + j)
                new_branches.append(
                    Branch(f"{branch.id}_{suffix}", node_id, exit_fogs[j])
                )
            letter_offset += 1
        else:
            # Regular passant for this branch
            cluster = pick_cluster_with_filter(
                candidates, used_zones, rng, can_be_passant_node
            )
            if cluster is None:
                raise GenerationError(
                    f"No passant-compatible cluster for layer {layer_idx} branch {i} (type: {layer_type})"
                )
            used_zones.update(cluster.zones)

            entry = pick_entry_with_max_exits(cluster, 1, rng)
            if entry is None:
                raise GenerationError(
                    f"Cluster {cluster.id} has no valid entry fog with exits"
                )

            entry_fog = FogRef(entry["fog_id"], entry["zone"])
            exits = compute_net_exits(cluster, [entry])
            exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits]

            node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
            node = DagNode(
                id=node_id,
                cluster=cluster,
                layer=layer_idx,
                tier=tier,
                entry_fogs=[entry_fog],
                exit_fogs=exit_fogs,
            )
            dag.add_node(node)
            dag.add_edge(
                branch.current_node_id, node_id, branch.available_exit, entry_fog
            )

            new_branches.append(Branch(branch.id, node_id, rng.choice(exit_fogs)))
            letter_offset += 1

    return new_branches


def _has_valid_merge_pair(branches: list[Branch]) -> bool:
    """Check if any two branches have different current nodes."""
    node_ids = {b.current_node_id for b in branches}
    return len(node_ids) >= 2


def _find_valid_merge_indices(
    branches: list[Branch], rng: random.Random, count: int = 2
) -> list[int] | None:
    """Select branch indices with at least 2 different parent nodes for merging.

    Prevents micro split-merge where all merging branches come from the same
    split node, creating a pointless fan-out/fan-in.

    Args:
        branches: Current branches.
        rng: Random number generator.
        count: Number of branches to select for merging.

    Returns:
        List of branch indices, or None if no valid selection exists.
    """
    if count > len(branches):
        return None

    valid_combos: list[list[int]] = []
    for combo in combinations(range(len(branches)), count):
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
) -> list[Branch]:
    """Execute a merge layer where N branches merge into one.

    The fan-in N is controlled by config.structure.max_branches.
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
        config: Configuration with max_branches.

    Returns:
        Updated list of branches (with fewer branches).

    Raises:
        GenerationError: If no suitable cluster found.
    """
    if len(branches) < 2:
        raise GenerationError("Cannot merge with fewer than 2 branches")

    max_merge = max(min(config.structure.max_branches, len(branches)), 2)
    candidates = clusters.get_by_type(layer_type)

    # Try from max_merge down to 2: find valid indices AND matching cluster
    merge_indices: list[int] | None = None
    merge_cluster: ClusterData | None = None
    actual_merge = 2
    for n in range(max_merge, 1, -1):
        indices = _find_valid_merge_indices(branches, rng, n)
        if indices is None:
            continue
        c = pick_cluster_with_filter(
            candidates,
            used_zones,
            rng,
            lambda c, n=n: can_be_merge_node(c, n),  # type: ignore[misc]
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
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits]
    else:
        # Original model: select N distinct entries
        entries = select_entries_for_merge(cluster, actual_merge, rng)
        entry_fogs_list = [FogRef(e["fog_id"], e["zone"]) for e in entries]
        exits = compute_net_exits(cluster, entries)
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits]

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
    new_branches.append(
        Branch(f"merged_{layer_idx}", merge_node_id, rng.choice(exit_fogs))
    )

    # Handle non-merged branches as passant
    merge_idx_set = set(merge_indices)
    for i, branch in enumerate(branches):
        if i in merge_idx_set:
            continue

        passant_cluster = pick_cluster_with_filter(
            candidates, used_zones, rng, can_be_passant_node
        )
        if passant_cluster is None:
            raise GenerationError(
                f"No passant-compatible cluster for layer {layer_idx} branch {i} (type: {layer_type})"
            )
        used_zones.update(passant_cluster.zones)

        passant_entry = pick_entry_with_max_exits(passant_cluster, 1, rng)
        if passant_entry is None:
            raise GenerationError(
                f"Cluster {passant_cluster.id} has no valid entry fog with exits"
            )

        passant_entry_fog = FogRef(passant_entry["fog_id"], passant_entry["zone"])
        exits = compute_net_exits(passant_cluster, [passant_entry])
        exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits]

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

        new_branches.append(Branch(branch.id, node_id, rng.choice(exit_fogs)))
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
) -> tuple[list[Branch], int]:
    """Force all branches to merge into one.

    Repeatedly merges until only 1 branch remains. With N-ary merges
    controlled by config.structure.max_branches, this may complete faster.

    Args:
        dag: The DAG being built.
        branches: Current branches.
        layer_idx: Starting layer index.
        tier: Difficulty tier.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        config: Configuration with max_branches.

    Returns:
        Tuple of (list with single branch, final layer index used).

    Raises:
        GenerationError: If merging fails.
    """
    current_layer = layer_idx
    while len(branches) > 1:
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
        )
        current_layer += 1

    return branches, current_layer


# =============================================================================
# Main DAG Generation
# =============================================================================


def generate_dag(
    config: Config,
    clusters: ClusterPool,
    seed: int | None = None,
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

    start_cluster = pick_cluster(start_candidates, used_zones, rng, require_exits=False)
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
        config.structure.max_branches,
    )

    if num_initial_branches == 0:
        raise GenerationError("Start cluster has no exits")

    # Shuffle exits for randomness
    rng.shuffle(start_exits)
    branches = [
        Branch(f"b{i}", "start", start_exits[i]) for i in range(num_initial_branches)
    ]

    # 3. Execute first layer if forced type
    current_layer = 1
    if config.structure.first_layer_type:
        first_type = config.structure.first_layer_type
        tier = compute_tier(current_layer, 10, config.structure.final_tier)

        branches = execute_passant_layer(
            dag,
            branches,
            current_layer,
            tier,
            first_type,
            clusters,
            used_zones,
            rng,
        )
        current_layer += 1

    # 4. Plan remaining layer types
    num_intermediate_layers = rng.randint(
        config.structure.min_layers, config.structure.max_layers
    )
    # Reduce layer count if first layer was forced
    if config.structure.first_layer_type:
        num_intermediate_layers = max(1, num_intermediate_layers - 1)

    layer_types = plan_layer_types(
        config.requirements,
        num_intermediate_layers,
        rng,
        major_boss_ratio=config.structure.major_boss_ratio,
    )

    # Calculate total layers for tier computation
    # We might add extra layers for forced merges
    first_layer_offset = 1 if config.structure.first_layer_type else 0
    estimated_total = (
        len(layer_types) + 2 + first_layer_offset
    )  # +1 start, +1 end, +1 if first forced

    # 5. Execute layers with dynamic topology
    for layer_idx, layer_type in enumerate(layer_types):
        is_near_end = layer_idx >= len(layer_types) - 2
        tier = compute_tier(current_layer, estimated_total, config.structure.final_tier)

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
            )
        else:
            # Decide operation based on current state
            operation = decide_operation(len(branches), config, rng)

            if operation == LayerOperation.PASSANT:
                branches = execute_passant_layer(
                    dag,
                    branches,
                    current_layer,
                    tier,
                    layer_type,
                    clusters,
                    used_zones,
                    rng,
                )
            elif operation == LayerOperation.SPLIT:
                branches = execute_split_layer(
                    dag,
                    branches,
                    current_layer,
                    tier,
                    layer_type,
                    clusters,
                    used_zones,
                    rng,
                    config,
                )
            elif operation == LayerOperation.MERGE:
                if _has_valid_merge_pair(branches):
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
                    )
                else:
                    # All branches share the same source; fall back to passant
                    branches = execute_passant_layer(
                        dag,
                        branches,
                        current_layer,
                        tier,
                        layer_type,
                        clusters,
                        used_zones,
                        rng,
                    )
            current_layer += 1

    # 5. Final merge if still multiple branches
    if len(branches) > 1:
        # Use the last layer type for final merge operations
        last_layer_type = layer_types[-1] if layer_types else "mini_dungeon"
        tier = compute_tier(current_layer, estimated_total, config.structure.final_tier)
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
        )

    # 7. Create end node (final_boss from candidates)
    all_boss_clusters = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )
    all_boss_zones = {zone for cluster in all_boss_clusters for zone in cluster.zones}

    # Resolve "all" keyword to actual zone names
    final_zone_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    final_zone_candidates = list(final_zone_candidates)  # Make a copy for shuffling
    rng.shuffle(final_zone_candidates)

    # Find a cluster matching one of the candidate zones
    end_cluster = None

    for zone_name in final_zone_candidates:
        for cluster in all_boss_clusters:
            if zone_name in cluster.zones:
                # Check no zone overlap with already used zones
                if not any(z in used_zones for z in cluster.zones):
                    end_cluster = cluster
                    break
        if end_cluster:
            break

    if end_cluster is None:
        raise GenerationError(
            f"No available final boss from candidates: {final_zone_candidates}"
        )

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

    return dag


def generate_with_retry(
    config: Config,
    clusters: ClusterPool,
    max_attempts: int = 100,
) -> GenerationResult:
    """Generate DAG with automatic retry on failure.

    If config.seed is 0, tries random seeds until success (generation + validation).
    If config.seed is non-zero, uses that seed (fails if generation or validation fails).

    Args:
        config: Configuration
        clusters: Cluster pool
        max_attempts: Maximum retry attempts (only for seed=0)

    Returns:
        GenerationResult with DAG, seed, validation, and attempt count.

    Raises:
        GenerationError: If generation fails after max_attempts
    """
    # Validate config before attempting generation
    config_errors = validate_config(config, clusters)
    if config_errors:
        raise GenerationError(f"Invalid configuration: {'; '.join(config_errors)}")

    if config.seed != 0:
        # Fixed seed - single attempt
        dag = generate_dag(config, clusters, config.seed)
        validation = validate_dag(dag, config)
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
            dag = generate_dag(config, clusters, seed)
            validation = validate_dag(dag, config)
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
