"""DAG generation algorithm for SpeedFog.

Generates a randomized DAG with:
- Start: chapel_start cluster
- 2 parallel branches with uniform layer types
- End: final_boss cluster (leyndell_erdtree)
"""

from __future__ import annotations

import random

from speedfog_core.clusters import ClusterData, ClusterPool
from speedfog_core.config import Config
from speedfog_core.dag import Dag, DagNode
from speedfog_core.planner import compute_tier, plan_layer_types


class GenerationError(Exception):
    """Error during DAG generation."""

    pass


def cluster_has_usable_exits(cluster: ClusterData) -> bool:
    """Check if cluster will have at least 1 exit after using any entry fog.

    A cluster is usable if for at least one entry_fog, there remains
    at least one exit_fog after removing the bidirectional entry.
    """
    if not cluster.entry_fogs:
        return False

    for entry in cluster.entry_fogs:
        entry_fog_id = entry["fog_id"]
        # Count exits that would remain after using this entry
        remaining_exits = [e for e in cluster.exit_fogs if e["fog_id"] != entry_fog_id]
        if remaining_exits:
            return True

    # No entry fog leaves any exits - cluster is a dead end
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


def generate_dag(
    config: Config,
    clusters: ClusterPool,
    seed: int | None = None,
) -> Dag:
    """Generate a randomized DAG with 2 parallel branches.

    Algorithm:
    1. Pick start cluster (type: start)
    2. Plan layer types to satisfy requirements
    3. For each layer, pick 2 clusters of the planned type (one per branch)
    4. Connect all to final_boss cluster

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

    # Start cluster doesn't need require_exits=True since we spawn there
    start_cluster = pick_cluster(start_candidates, used_zones, rng, require_exits=False)
    if start_cluster is None:
        raise GenerationError("Could not pick start cluster")

    start_node = DagNode(
        id="start",
        cluster=start_cluster,
        layer=0,
        tier=1,
        entry_fog=None,
        exit_fogs=[f["fog_id"] for f in start_cluster.exit_fogs],
    )
    dag.add_node(start_node)
    dag.start_id = "start"
    used_zones.update(start_cluster.zones)

    # 2. Plan layer types
    num_intermediate_layers = rng.randint(
        config.structure.min_layers, config.structure.max_layers
    )
    layer_types = plan_layer_types(config.requirements, num_intermediate_layers, rng)

    total_layers = len(layer_types) + 2  # +1 for start, +1 for end

    # 3. Build intermediate layers (2 parallel branches)
    prev_node_a: DagNode = start_node
    prev_node_b: DagNode = start_node

    for layer_idx, layer_type in enumerate(layer_types, start=1):
        tier = compute_tier(layer_idx, total_layers)

        # Pick cluster for branch A
        candidates = clusters.get_by_type(layer_type)
        cluster_a = pick_cluster(candidates, used_zones, rng)
        if cluster_a is None:
            raise GenerationError(
                f"No available cluster for layer {layer_idx} branch A (type: {layer_type})"
            )
        used_zones.update(cluster_a.zones)

        # Pick cluster for branch B (different from A)
        cluster_b = pick_cluster(candidates, used_zones, rng)
        if cluster_b is None:
            raise GenerationError(
                f"No available cluster for layer {layer_idx} branch B (type: {layer_type})"
            )
        used_zones.update(cluster_b.zones)

        # Determine entry fogs (pick randomly from available)
        # Pick entry fogs that leave exits available
        entry_fog_a = pick_entry_fog_with_exits(cluster_a, rng)
        entry_fog_b = pick_entry_fog_with_exits(cluster_b, rng)

        if entry_fog_a is None:
            raise GenerationError(
                f"Cluster {cluster_a.id} has no valid entry fog with exits"
            )
        if entry_fog_b is None:
            raise GenerationError(
                f"Cluster {cluster_b.id} has no valid entry fog with exits"
            )

        # Create nodes
        node_a = DagNode(
            id=f"node_{layer_idx}a",
            cluster=cluster_a,
            layer=layer_idx,
            tier=tier,
            entry_fog=entry_fog_a,
            exit_fogs=[f["fog_id"] for f in cluster_a.available_exits(entry_fog_a)],
        )
        node_b = DagNode(
            id=f"node_{layer_idx}b",
            cluster=cluster_b,
            layer=layer_idx,
            tier=tier,
            entry_fog=entry_fog_b,
            exit_fogs=[f["fog_id"] for f in cluster_b.available_exits(entry_fog_b)],
        )

        dag.add_node(node_a)
        dag.add_node(node_b)

        # Connect from previous layer
        # Pick an exit fog from the previous node
        if not prev_node_a.exit_fogs:
            raise GenerationError(f"Node {prev_node_a.id} has no exit fogs")
        if not prev_node_b.exit_fogs:
            raise GenerationError(f"Node {prev_node_b.id} has no exit fogs")
        exit_fog_a = rng.choice(prev_node_a.exit_fogs)
        exit_fog_b = rng.choice(prev_node_b.exit_fogs)

        dag.add_edge(prev_node_a.id, node_a.id, exit_fog_a)
        dag.add_edge(prev_node_b.id, node_b.id, exit_fog_b)

        prev_node_a = node_a
        prev_node_b = node_b

    # 4. Create end node (final_boss)
    end_candidates = clusters.get_by_type("final_boss")
    if not end_candidates:
        raise GenerationError("No final_boss cluster found")

    end_cluster = pick_cluster(end_candidates, used_zones, rng, require_exits=False)
    if end_cluster is None:
        raise GenerationError("Could not pick final_boss cluster")

    entry_fog_end = (
        rng.choice(end_cluster.entry_fogs)["fog_id"] if end_cluster.entry_fogs else None
    )

    end_node = DagNode(
        id="end",
        cluster=end_cluster,
        layer=len(layer_types) + 1,
        tier=28,
        entry_fog=entry_fog_end,
        exit_fogs=[],  # No exits from final boss
    )
    dag.add_node(end_node)
    dag.end_id = "end"

    # Connect both branches to end
    if not prev_node_a.exit_fogs:
        raise GenerationError(
            f"Node {prev_node_a.id} has no exit fogs for final connection"
        )
    if not prev_node_b.exit_fogs:
        raise GenerationError(
            f"Node {prev_node_b.id} has no exit fogs for final connection"
        )
    exit_fog_a = rng.choice(prev_node_a.exit_fogs)
    exit_fog_b = rng.choice(prev_node_b.exit_fogs)

    dag.add_edge(prev_node_a.id, end_node.id, exit_fog_a)
    dag.add_edge(prev_node_b.id, end_node.id, exit_fog_b)

    return dag


def generate_with_retry(
    config: Config,
    clusters: ClusterPool,
    max_attempts: int = 100,
) -> tuple[Dag, int]:
    """Generate DAG with automatic retry on failure.

    If config.seed is 0, tries random seeds until success.
    If config.seed is non-zero, uses that seed (fails if generation fails).

    Args:
        config: Configuration
        clusters: Cluster pool
        max_attempts: Maximum retry attempts (only for seed=0)

    Returns:
        Tuple of (generated DAG, actual seed used)

    Raises:
        GenerationError: If generation fails after max_attempts
    """
    if config.seed != 0:
        # Fixed seed - single attempt
        dag = generate_dag(config, clusters, config.seed)
        return dag, config.seed

    # Auto-reroll mode
    base_rng = random.Random()

    for attempt in range(max_attempts):
        seed = base_rng.randint(1, 999999999)
        try:
            dag = generate_dag(config, clusters, seed)
            return dag, seed
        except GenerationError as e:
            print(f"Attempt {attempt + 1}: seed {seed} failed - {e}")
            continue

    raise GenerationError(f"Failed to generate DAG after {max_attempts} attempts")
