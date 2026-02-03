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

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.config import Config
from speedfog.dag import Branch, Dag, DagNode
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

    for zone in config.structure.effective_final_boss_candidates:
        if zone not in all_boss_zones:
            errors.append(f"Unknown final_boss candidate zone: '{zone}'")

    return errors


class LayerOperation(Enum):
    """Type of operation to perform on a layer."""

    PASSANT = auto()  # 1 branch -> 1 branch
    SPLIT = auto()  # 1 branch -> 2 branches
    MERGE = auto()  # 2 branches -> 1 branch


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


def compute_net_exits(cluster: ClusterData, consumed_entries: list[str]) -> list[dict]:
    """Return exits remaining after consuming given entry fogs.

    Bidirectional fogs (appearing in both entry and exit) are removed
    from available exits when their entry side is consumed.

    Args:
        cluster: The cluster to check.
        consumed_entries: List of entry fog_ids that are being used.

    Returns:
        List of exit fog dicts remaining after consuming entries.
    """
    consumed_set = set(consumed_entries)
    return [f for f in cluster.exit_fogs if f["fog_id"] not in consumed_set]


def count_net_exits(cluster: ClusterData, num_entries: int) -> int:
    """Minimum net exits when consuming num_entries (greedy: prefer non-bidirectional).

    This calculates the worst-case net exits by greedily selecting entries
    that cost the least (non-bidirectional entries have zero cost).

    Args:
        cluster: The cluster to check.
        num_entries: Number of entry fogs to consume.

    Returns:
        Minimum number of exits remaining after consuming num_entries.
    """
    if num_entries > len(cluster.entry_fogs):
        return 0

    # Build set of exit fog IDs for checking bidirectionality
    exit_ids = {f["fog_id"] for f in cluster.exit_fogs}

    # Calculate cost for each entry (1 if bidirectional, 0 otherwise)
    entry_costs: list[tuple[str, int]] = []
    for entry in cluster.entry_fogs:
        fog_id = entry["fog_id"]
        cost = 1 if fog_id in exit_ids else 0
        entry_costs.append((fog_id, cost))

    # Sort by cost (cheapest first) and take num_entries
    entry_costs.sort(key=lambda x: x[1])
    consumed = [fog_id for fog_id, _ in entry_costs[:num_entries]]

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

    All fogs must be mapped: num_in entries consumed + 1 exit used.

    Args:
        cluster: The cluster to check.
        num_in: Number of entry fogs to consume.

    Returns:
        True if cluster has enough entries and exactly 1 net exit.
    """
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


def select_entries_for_merge(
    cluster: ClusterData, num: int, rng: random.Random
) -> list[str]:
    """Select entry fogs that maximize remaining exits.

    Prefers non-bidirectional entries to preserve more exits.

    Args:
        cluster: The cluster to select entries from.
        num: Number of entries to select.
        rng: Random number generator.

    Returns:
        List of selected entry fog_ids.
    """
    exit_ids = {f["fog_id"] for f in cluster.exit_fogs}

    # Separate entries by cost
    non_bidir = [e["fog_id"] for e in cluster.entry_fogs if e["fog_id"] not in exit_ids]
    bidir = [e["fog_id"] for e in cluster.entry_fogs if e["fog_id"] in exit_ids]

    # Shuffle each group
    rng.shuffle(non_bidir)
    rng.shuffle(bidir)

    # Take from non-bidir first, then bidir
    result = non_bidir[:num]
    remaining = num - len(result)
    if remaining > 0:
        result.extend(bidir[:remaining])

    return result


def pick_entry_with_max_exits(
    cluster: ClusterData, min_exits: int, rng: random.Random
) -> str | None:
    """Pick an entry fog that leaves at least min_exits available.

    Args:
        cluster: The cluster to pick from.
        min_exits: Minimum required exits after using the entry.
        rng: Random number generator.

    Returns:
        The fog_id of a valid entry, or None if no valid entry exists.
    """
    valid_entries: list[str] = []
    for entry in cluster.entry_fogs:
        entry_fog_id = entry["fog_id"]
        remaining = compute_net_exits(cluster, [entry_fog_id])
        if len(remaining) >= min_exits:
            valid_entries.append(entry_fog_id)

    if not valid_entries:
        return None

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
    max_branches = config.structure.max_branches
    split_prob = config.structure.split_probability
    merge_prob = config.structure.merge_probability

    if num_branches >= max_branches:
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

        exits = compute_net_exits(cluster, [entry])
        exit_fogs = [f["fog_id"] for f in exits]

        node_id = f"node_{layer_idx}_{chr(97 + i)}"
        node = DagNode(
            id=node_id,
            cluster=cluster,
            layer=layer_idx,
            tier=tier,
            entry_fogs=[entry],
            exit_fogs=exit_fogs,
        )
        dag.add_node(node)
        dag.add_edge(branch.current_node_id, node_id, branch.available_exit, entry)

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
) -> list[Branch]:
    """Execute a split layer where one branch splits into two.

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
        Updated list of branches (with one extra).

    Raises:
        GenerationError: If no suitable cluster found.
    """
    split_idx = rng.randrange(len(branches))
    new_branches: list[Branch] = []
    candidates = clusters.get_by_type(layer_type)
    letter_offset = 0

    for i, branch in enumerate(branches):
        if i == split_idx:
            # This branch splits into two
            cluster = pick_cluster_with_filter(
                candidates, used_zones, rng, lambda c: can_be_split_node(c, 2)
            )
            if cluster is None:
                raise GenerationError(
                    f"No split-compatible cluster for layer {layer_idx} (type: {layer_type})"
                )
            used_zones.update(cluster.zones)

            # Pick entry that leaves at least 2 exits
            entry = pick_entry_with_max_exits(cluster, 2, rng)
            if entry is None:
                raise GenerationError(
                    f"Cluster {cluster.id} has no valid entry fog with 2+ exits"
                )

            exits = compute_net_exits(cluster, [entry])
            exit_fogs = [f["fog_id"] for f in exits]

            node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
            node = DagNode(
                id=node_id,
                cluster=cluster,
                layer=layer_idx,
                tier=tier,
                entry_fogs=[entry],
                exit_fogs=exit_fogs,
            )
            dag.add_node(node)
            dag.add_edge(branch.current_node_id, node_id, branch.available_exit, entry)

            # Create two new branches
            rng.shuffle(exit_fogs)
            new_branches.append(Branch(f"{branch.id}_a", node_id, exit_fogs[0]))
            new_branches.append(Branch(f"{branch.id}_b", node_id, exit_fogs[1]))
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

            exits = compute_net_exits(cluster, [entry])
            exit_fogs = [f["fog_id"] for f in exits]

            node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
            node = DagNode(
                id=node_id,
                cluster=cluster,
                layer=layer_idx,
                tier=tier,
                entry_fogs=[entry],
                exit_fogs=exit_fogs,
            )
            dag.add_node(node)
            dag.add_edge(branch.current_node_id, node_id, branch.available_exit, entry)

            new_branches.append(Branch(branch.id, node_id, rng.choice(exit_fogs)))
            letter_offset += 1

    return new_branches


def execute_merge_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
) -> list[Branch]:
    """Execute a merge layer where two branches merge into one.

    Args:
        dag: The DAG being built.
        branches: Current branches (must have at least 2).
        layer_idx: Current layer index.
        tier: Difficulty tier for this layer.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.

    Returns:
        Updated list of branches (with one fewer).

    Raises:
        GenerationError: If no suitable cluster found.
    """
    if len(branches) < 2:
        raise GenerationError("Cannot merge with fewer than 2 branches")

    merge_indices = rng.sample(range(len(branches)), 2)
    merge_branches = [branches[i] for i in merge_indices]

    candidates = clusters.get_by_type(layer_type)
    new_branches: list[Branch] = []
    letter_offset = 0

    # Create merge node first
    cluster = pick_cluster_with_filter(
        candidates, used_zones, rng, lambda c: can_be_merge_node(c, 2)
    )
    if cluster is None:
        raise GenerationError(
            f"No merge-compatible cluster for layer {layer_idx} (type: {layer_type})"
        )
    used_zones.update(cluster.zones)

    entries = select_entries_for_merge(cluster, 2, rng)
    exits = compute_net_exits(cluster, entries)
    exit_fogs = [f["fog_id"] for f in exits]

    merge_node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
    merge_node = DagNode(
        id=merge_node_id,
        cluster=cluster,
        layer=layer_idx,
        tier=tier,
        entry_fogs=entries,
        exit_fogs=exit_fogs,
    )
    dag.add_node(merge_node)
    letter_offset += 1

    # Connect both merging branches to the merge node
    # Pair each branch with its corresponding entry fog
    for branch, entry in zip(merge_branches, entries, strict=False):
        dag.add_edge(
            branch.current_node_id, merge_node_id, branch.available_exit, entry
        )

    # Create single branch for merged path
    new_branches.append(
        Branch(f"merged_{layer_idx}", merge_node_id, rng.choice(exit_fogs))
    )

    # Handle non-merged branches as passant
    for i, branch in enumerate(branches):
        if i in merge_indices:
            continue

        cluster = pick_cluster_with_filter(
            candidates, used_zones, rng, can_be_passant_node
        )
        if cluster is None:
            raise GenerationError(
                f"No passant-compatible cluster for layer {layer_idx} branch {i} (type: {layer_type})"
            )
        used_zones.update(cluster.zones)

        passant_entry = pick_entry_with_max_exits(cluster, 1, rng)
        if passant_entry is None:
            raise GenerationError(
                f"Cluster {cluster.id} has no valid entry fog with exits"
            )

        exits = compute_net_exits(cluster, [passant_entry])
        exit_fogs = [f["fog_id"] for f in exits]

        node_id = f"node_{layer_idx}_{chr(97 + letter_offset)}"
        node = DagNode(
            id=node_id,
            cluster=cluster,
            layer=layer_idx,
            tier=tier,
            entry_fogs=[passant_entry],
            exit_fogs=exit_fogs,
        )
        dag.add_node(node)
        dag.add_edge(
            branch.current_node_id, node_id, branch.available_exit, passant_entry
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
) -> tuple[list[Branch], int]:
    """Force all branches to merge into one.

    Repeatedly merges until only 1 branch remains.

    Args:
        dag: The DAG being built.
        branches: Current branches.
        layer_idx: Starting layer index.
        tier: Difficulty tier.
        layer_type: Type of cluster to pick.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.

    Returns:
        Tuple of (list with single branch, final layer index used).

    Raises:
        GenerationError: If merging fails.
    """
    current_layer = layer_idx
    while len(branches) > 1:
        branches = execute_merge_layer(
            dag, branches, current_layer, tier, layer_type, clusters, used_zones, rng
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
        exit_fogs=[f["fog_id"] for f in start_cluster.exit_fogs],
    )
    dag.add_node(start_node)
    dag.start_id = "start"
    used_zones.update(start_cluster.zones)

    # 2. Initialize branches from start exits
    # Natural split at start based on available exits
    start_exits = start_node.exit_fogs
    num_initial_branches = min(len(start_exits), config.structure.max_branches)

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
        tier = compute_tier(current_layer, 10)  # Approximate, will be refined

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
        tier = compute_tier(current_layer, estimated_total)

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
                )
            elif operation == LayerOperation.MERGE:
                branches = execute_merge_layer(
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
        tier = compute_tier(current_layer, estimated_total)
        branches, current_layer = execute_forced_merge(
            dag,
            branches,
            current_layer,
            tier,
            last_layer_type,
            clusters,
            used_zones,
            rng,
        )

    # 7. Create end node (final_boss from candidates)
    final_zone_candidates = config.structure.effective_final_boss_candidates.copy()
    rng.shuffle(final_zone_candidates)

    # Find a cluster matching one of the candidate zones
    end_cluster = None
    all_boss_clusters = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )

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
    entry_fog_end = (
        rng.choice(end_cluster.entry_fogs)["fog_id"] if end_cluster.entry_fogs else None
    )
    entry_fogs_end = [entry_fog_end] if entry_fog_end else []

    end_node = DagNode(
        id="end",
        cluster=end_cluster,
        layer=current_layer,
        tier=28,
        entry_fogs=entry_fogs_end,
        exit_fogs=[],  # No exits from final boss
    )
    dag.add_node(end_node)
    dag.end_id = "end"

    # Connect the single remaining branch to end
    if not branches:
        raise GenerationError("No branches remaining to connect to end")

    branch = branches[0]
    # Use entry_fog_end, or empty string if None (final boss may have no entry fogs)
    dag.add_edge(
        branch.current_node_id, end_node.id, branch.available_exit, entry_fog_end or ""
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
