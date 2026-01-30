"""DAG generation algorithm for SpeedFog.

Generates a randomized DAG with:
- Start: chapel_start cluster
- 2 parallel branches with uniform layer types
- End: final_boss cluster (leyndell_erdtree)
"""

from __future__ import annotations

import json
import random
from dataclasses import dataclass, field
from pathlib import Path

from speedfog_core.clusters import ClusterData, ClusterPool
from speedfog_core.config import Config


@dataclass
class DagNode:
    """A node in the DAG representing a cluster instance."""

    id: str
    cluster: ClusterData
    layer: int
    tier: int  # Difficulty scaling (1-28)
    entry_fog: str | None  # fog_id used to enter (None for start)
    exit_fogs: list[str] = field(default_factory=list)  # Available exits


@dataclass
class DagEdge:
    """A directed edge between two nodes."""

    source_id: str
    target_id: str
    fog_id: str  # The fog gate connecting them


@dataclass
class Dag:
    """The complete DAG structure."""

    seed: int
    nodes: dict[str, DagNode] = field(default_factory=dict)
    edges: list[DagEdge] = field(default_factory=list)
    start_id: str = ""
    end_id: str = ""

    def add_node(self, node: DagNode) -> None:
        """Add a node to the DAG."""
        self.nodes[node.id] = node

    def add_edge(self, source_id: str, target_id: str, fog_id: str) -> None:
        """Add an edge to the DAG."""
        self.edges.append(DagEdge(source_id, target_id, fog_id))

    def get_paths(self) -> list[list[str]]:
        """Enumerate all paths from start to end (returns node IDs)."""
        if not self.start_id or not self.end_id:
            return []

        paths: list[list[str]] = []

        def dfs(node_id: str, current_path: list[str]) -> None:
            current_path = current_path + [node_id]
            if node_id == self.end_id:
                paths.append(current_path)
                return
            # Find outgoing edges
            for edge in self.edges:
                if edge.source_id == node_id:
                    dfs(edge.target_id, current_path)

        dfs(self.start_id, [])
        return paths

    def path_weight(self, path: list[str]) -> int:
        """Calculate total weight of a path."""
        return sum(self.nodes[nid].cluster.weight for nid in path)

    def to_dict(self) -> dict:
        """Convert to JSON-serializable dictionary."""
        nodes_dict = {}
        for nid, node in self.nodes.items():
            nodes_dict[nid] = {
                "cluster_id": node.cluster.id,
                "zones": node.cluster.zones,
                "type": node.cluster.type,
                "weight": node.cluster.weight,
                "layer": node.layer,
                "tier": node.tier,
                "entry_fog": node.entry_fog,
                "exit_fogs": node.exit_fogs,
            }

        edges_list = [
            {"source": e.source_id, "target": e.target_id, "fog_id": e.fog_id}
            for e in self.edges
        ]

        paths = self.get_paths()

        return {
            "seed": self.seed,
            "total_layers": max((n.layer for n in self.nodes.values()), default=0) + 1,
            "total_nodes": len(self.nodes),
            "total_paths": len(paths),
            "path_weights": [self.path_weight(p) for p in paths],
            "nodes": nodes_dict,
            "edges": edges_list,
            "start_id": self.start_id,
            "end_id": self.end_id,
        }

    def export_json(self, path: Path) -> None:
        """Export DAG to JSON file."""
        with open(path, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, indent=2)

    def export_spoiler(self, path: Path) -> None:
        """Export human-readable spoiler log."""
        lines = [
            "=" * 60,
            f"SPEEDFOG SPOILER LOG (seed: {self.seed})",
            "=" * 60,
            "",
        ]

        # Group nodes by layer
        by_layer: dict[int, list[DagNode]] = {}
        for node in self.nodes.values():
            if node.layer not in by_layer:
                by_layer[node.layer] = []
            by_layer[node.layer].append(node)

        for layer_idx in sorted(by_layer.keys()):
            nodes = by_layer[layer_idx]
            lines.append(f"--- Layer {layer_idx} (tier {nodes[0].tier}) ---")
            for node in nodes:
                zones_str = ", ".join(node.cluster.zones)
                lines.append(
                    f"  [{node.id}] {node.cluster.type}: {zones_str} (w:{node.cluster.weight})"
                )
            lines.append("")

        lines.append("=" * 60)
        lines.append("PATHS")
        lines.append("=" * 60)

        all_paths = self.get_paths()
        for i, node_path in enumerate(all_paths):
            weight = self.path_weight(node_path)
            path_str = " -> ".join(node_path)
            lines.append(f"Path {i + 1} (weight {weight}): {path_str}")

        lines.append("")

        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(lines))


class GenerationError(Exception):
    """Error during DAG generation."""

    pass


def compute_tier(layer_idx: int, total_layers: int) -> int:
    """Map layer index to difficulty tier (1-28)."""
    if total_layers <= 1:
        return 1
    progress = layer_idx / (total_layers - 1)
    return max(1, min(28, int(1 + progress * 27)))


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


def plan_layer_types(
    requirements,  # RequirementsConfig
    total_layers: int,
    rng: random.Random,
) -> list[str]:
    """Plan the sequence of cluster types for intermediate layers.

    Ensures requirements are met:
    - At least N legacy_dungeons
    - At least M mini_dungeons
    - At least K bosses (boss_arena)
    """
    types: list[str] = []

    # Add required types
    for _ in range(requirements.legacy_dungeons):
        types.append("legacy_dungeon")

    for _ in range(requirements.mini_dungeons):
        types.append("mini_dungeon")

    for _ in range(requirements.bosses):
        types.append("boss_arena")

    # Pad with mini_dungeons if needed (most common filler)
    while len(types) < total_layers:
        types.append("mini_dungeon")

    # Trim if too many
    types = types[:total_layers]

    # Shuffle
    rng.shuffle(types)

    return types


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

    start_cluster = pick_cluster(start_candidates, used_zones, rng)
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
