# Phase 2: DAG Generation - Detailed Implementation Spec

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Prerequisite**: [Phase 1: Foundations](./phase-1-foundations.md), [Cluster Generation](./generate-clusters-spec.md)
**Status**: Ready for implementation

## Objective

Implement the DAG (Directed Acyclic Graph) generation algorithm that creates balanced, randomized cluster sequences for SpeedFog runs.

## Key Concept: Clusters, Not Zones

SpeedFog uses **clusters** as the atomic unit for DAG generation, not individual zones.

A **cluster** is a group of zones connected by world connections. Once a player enters a cluster via an `entry_fog`, they have access to all zones within that cluster and can exit via any `exit_fog`.

See [generate-clusters-spec.md](./generate-clusters-spec.md) for details on how clusters are computed from `fog.txt`.

### Why Clusters?

| Individual Zones (wrong) | Clusters (correct) |
|--------------------------|-------------------|
| `stormveil_start` and `stormveil` as separate nodes | Single cluster `[stormveil_start, stormveil]` |
| Need to track internal connections | Internal connections pre-computed |
| Risk of creating impossible paths | Guaranteed valid entry→exit paths |

## Prerequisites

- Phase 1 completed (config.py, clusters.py)
- `clusters.json` generated via `generate_clusters.py`
- Python 3.10+

## Deliverables

```
speedfog/core/speedfog_core/
├── clusters.py      # ✅ Already exists - ClusterData, ClusterPool
├── dag.py           # Task 2.1: DAG data structures
├── planner.py       # Task 2.2: Layer planning
├── generator.py     # Task 2.3: Generation algorithm (update existing)
├── balance.py       # Task 2.4: Path balancing
├── validator.py     # Task 2.5: Constraint validation
├── output.py        # Task 2.6: JSON export
└── main.py          # CLI entry point
```

---

## Data Model Recap

### ClusterData (from clusters.py)

```python
@dataclass
class ClusterData:
    id: str                    # e.g., "stormveil_start_c1d3"
    zones: list[str]           # e.g., ["stormveil_start", "stormveil"]
    type: str                  # start, final_boss, legacy_dungeon, mini_dungeon, boss_arena
    weight: int                # Total weight (sum of zone weights)
    entry_fogs: list[dict]     # [{"fog_id": str, "zone": str}, ...]
    exit_fogs: list[dict]      # [{"fog_id": str, "zone": str, "unique"?: bool}, ...]
```

### Available Exits Calculation

When a player enters a cluster via an `entry_fog`:
- If the entry_fog is **bidirectional** (appears in both `entry_fogs` and `exit_fogs`), it's removed from available exits
- If the entry_fog is **unique** (unidirectional), it doesn't appear in `exit_fogs` anyway

```python
def available_exits(self, used_entry_fog: str | None) -> list[dict]:
    if used_entry_fog is None:
        return list(self.exit_fogs)
    return [f for f in self.exit_fogs if f["fog_id"] != used_entry_fog]
```

---

## Task 2.1: DAG Data Structures (dag.py)

### Core Classes

```python
"""DAG data structures for SpeedFog."""

from dataclasses import dataclass, field
from speedfog_core.clusters import ClusterData


@dataclass
class DagNode:
    """A node in the DAG representing a cluster instance."""
    id: str                          # Unique node ID (e.g., "node_1a")
    cluster: ClusterData             # The cluster this node represents
    layer: int                       # Layer index (0 = start, N = end)
    tier: int                        # Difficulty tier for scaling (1-28)
    entry_fog: str | None            # fog_id used to enter (None for start)
    exit_fogs: list[str] = field(default_factory=list)  # Available exit fog_ids

    def __hash__(self) -> int:
        return hash(self.id)

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, DagNode):
            return False
        return self.id == other.id


@dataclass
class DagEdge:
    """A directed edge between two nodes."""
    source_id: str
    target_id: str
    fog_id: str  # The fog gate connecting them

    def __hash__(self) -> int:
        return hash((self.source_id, self.target_id, self.fog_id))


@dataclass
class Dag:
    """
    Directed Acyclic Graph representing a SpeedFog run.

    Structure:
        Layer 0: Start cluster (chapel_start)
        Layer 1..N-1: Intermediate clusters with splits/merges
        Layer N: End cluster (final_boss)
    """
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

    def get_node(self, node_id: str) -> DagNode | None:
        """Get node by ID."""
        return self.nodes.get(node_id)

    def get_outgoing_edges(self, node_id: str) -> list[DagEdge]:
        """Get all edges originating from a node."""
        return [e for e in self.edges if e.source_id == node_id]

    def get_incoming_edges(self, node_id: str) -> list[DagEdge]:
        """Get all edges targeting a node."""
        return [e for e in self.edges if e.target_id == node_id]

    def enumerate_paths(self) -> list[list[str]]:
        """
        Enumerate all possible paths from start to end.
        Returns list of paths, where each path is a list of node IDs.
        """
        if not self.start_id or not self.end_id:
            return []

        paths: list[list[str]] = []

        def dfs(node_id: str, current_path: list[str]) -> None:
            current_path = current_path + [node_id]

            if node_id == self.end_id:
                paths.append(current_path)
                return

            for edge in self.get_outgoing_edges(node_id):
                dfs(edge.target_id, current_path)

        dfs(self.start_id, [])
        return paths

    def path_weight(self, path: list[str]) -> int:
        """Calculate total weight of a path."""
        return sum(self.nodes[nid].cluster.weight for nid in path)

    def total_nodes(self) -> int:
        """Count total nodes in the DAG."""
        return len(self.nodes)

    def total_zones(self) -> int:
        """Count total unique zones across all clusters."""
        zones = set()
        for node in self.nodes.values():
            zones.update(node.cluster.zones)
        return len(zones)

    def count_by_type(self, cluster_type: str) -> int:
        """Count nodes of a specific cluster type."""
        return sum(1 for node in self.nodes.values() if node.cluster.type == cluster_type)

    def validate_structure(self) -> list[str]:
        """
        Validate DAG structure.
        Returns list of error messages (empty if valid).
        """
        errors = []

        if not self.start_id:
            errors.append("No start node defined")

        if not self.end_id:
            errors.append("No end node defined")

        # Check all nodes are reachable from start
        if self.start_id:
            reachable = set()
            to_visit = [self.start_id]
            while to_visit:
                node_id = to_visit.pop()
                if node_id in reachable:
                    continue
                reachable.add(node_id)
                for edge in self.get_outgoing_edges(node_id):
                    to_visit.append(edge.target_id)

            unreachable = set(self.nodes.keys()) - reachable
            if unreachable:
                errors.append(f"Unreachable nodes: {list(unreachable)}")

        # Check all nodes can reach end
        if self.end_id:
            can_reach_end = set()
            to_visit = [self.end_id]
            while to_visit:
                node_id = to_visit.pop()
                if node_id in can_reach_end:
                    continue
                can_reach_end.add(node_id)
                for edge in self.get_incoming_edges(node_id):
                    to_visit.append(edge.source_id)

            dead_ends = set(self.nodes.keys()) - can_reach_end
            if dead_ends:
                errors.append(f"Dead end nodes: {list(dead_ends)}")

        # Check no cycles (all edges go to higher layers)
        for edge in self.edges:
            source_layer = self.nodes[edge.source_id].layer
            target_layer = self.nodes[edge.target_id].layer
            if source_layer >= target_layer:
                errors.append(f"Invalid edge (not forward): {edge.source_id} -> {edge.target_id}")

        return errors
```

---

## Task 2.2: Layer Planning (planner.py)

The generation uses a **uniform layer** approach: each layer has a single cluster type, ensuring competitive fairness.

### Layer Spec

```python
"""Layer planning for SpeedFog DAG generation."""

from dataclasses import dataclass
import random

from speedfog_core.config import RequirementsConfig


@dataclass
class LayerSpec:
    """Specification for a single layer."""
    cluster_type: str          # Type of cluster for this layer
    branch_count: int          # Number of parallel branches at this layer


def plan_layer_types(
    requirements: RequirementsConfig,
    total_layers: int,
    rng: random.Random,
) -> list[str]:
    """
    Plan the sequence of cluster types for intermediate layers.

    Ensures requirements are met:
    - At least N legacy_dungeons
    - At least M mini_dungeons
    - At least K bosses (boss_arena)

    Args:
        requirements: Configuration requirements
        total_layers: Number of intermediate layers (excluding start/end)
        rng: Random number generator

    Returns:
        List of cluster type strings for each layer
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

    # Shuffle to randomize order
    rng.shuffle(types)

    return types


def compute_tier(layer_idx: int, total_layers: int) -> int:
    """
    Map layer index to difficulty tier (1-28).

    Uses linear progression for smooth difficulty curve.

    Args:
        layer_idx: Current layer index (0 = start)
        total_layers: Total number of layers

    Returns:
        Difficulty tier between 1 and 28
    """
    if total_layers <= 1:
        return 1
    progress = layer_idx / (total_layers - 1)
    return max(1, min(28, int(1 + progress * 27)))
```

---

## Task 2.3: Generation Algorithm (generator.py)

### Core Algorithm

```python
"""DAG generation algorithm for SpeedFog."""

import random
from dataclasses import dataclass

from speedfog_core.clusters import ClusterData, ClusterPool
from speedfog_core.config import Config
from speedfog_core.dag import Dag, DagNode
from speedfog_core.planner import plan_layer_types, compute_tier


class GenerationError(Exception):
    """Error during DAG generation."""
    pass


@dataclass
class Branch:
    """Tracks a branch during generation."""
    node: DagNode
    exit_fog: str  # The fog_id to use when connecting to next layer


def cluster_has_usable_exits(cluster: ClusterData) -> bool:
    """
    Check if cluster will have at least 1 exit after using any entry fog.

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
    """
    Pick an entry fog that leaves at least one exit available.

    Returns the fog_id of a valid entry, or None if no valid entry exists.
    """
    valid_entries = []
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
    """
    Pick a cluster whose zones don't overlap with used_zones.

    Args:
        candidates: List of candidate clusters
        used_zones: Set of zone IDs already used
        rng: Random number generator
        require_exits: If True, only pick clusters with usable exits

    Returns:
        Selected cluster or None if no valid cluster found
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
    """
    Generate a randomized DAG with parallel branches.

    Algorithm:
    1. Create start node (type: start)
    2. Plan layer types to satisfy requirements
    3. For each layer:
       a. Determine branch count based on previous layer's exit count
       b. Pick clusters of the planned type (one per branch)
       c. Connect from previous layer
    4. Converge all branches to final_boss cluster

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
        config.structure.min_layers,
        config.structure.max_layers
    )
    layer_types = plan_layer_types(config.requirements, num_intermediate_layers, rng)
    total_layers = len(layer_types) + 2  # +1 for start, +1 for end

    # 3. Build intermediate layers
    # Start with branches from start node's exits
    current_branches: list[Branch] = []
    for exit_fog in start_node.exit_fogs[:config.structure.max_parallel_paths]:
        current_branches.append(Branch(node=start_node, exit_fog=exit_fog))

    # If start has only 1 exit, we still want 2 branches for variety
    # (duplicate the branch - both will connect to different clusters)
    if len(current_branches) == 1:
        current_branches.append(Branch(node=start_node, exit_fog=current_branches[0].exit_fog))

    for layer_idx, layer_type in enumerate(layer_types, start=1):
        tier = compute_tier(layer_idx, total_layers)
        next_branches: list[Branch] = []
        layer_used_zones: set[str] = set()

        for branch_idx, branch in enumerate(current_branches):
            # Pick cluster for this branch
            candidates = clusters.get_by_type(layer_type)
            cluster = pick_cluster(
                candidates,
                used_zones | layer_used_zones,
                rng,
                require_exits=True,
            )

            if cluster is None:
                raise GenerationError(
                    f"No available cluster for layer {layer_idx} branch {branch_idx} "
                    f"(type: {layer_type})"
                )

            layer_used_zones.update(cluster.zones)

            # Pick entry fog that leaves exits available
            entry_fog = pick_entry_fog_with_exits(cluster, rng)
            if entry_fog is None:
                raise GenerationError(
                    f"Cluster {cluster.id} has no valid entry fog with exits"
                )

            # Create node
            node_id = f"node_{layer_idx}{chr(ord('a') + branch_idx)}"
            available_exits = cluster.available_exits(entry_fog)

            node = DagNode(
                id=node_id,
                cluster=cluster,
                layer=layer_idx,
                tier=tier,
                entry_fog=entry_fog,
                exit_fogs=[f["fog_id"] for f in available_exits],
            )
            dag.add_node(node)

            # Connect from previous node
            dag.add_edge(branch.node.id, node.id, branch.exit_fog)

            # Create branches for next layer
            # Limit branches based on max_parallel_paths
            exits_to_use = node.exit_fogs[:config.structure.max_parallel_paths - len(next_branches) + 1]
            for exit_fog in exits_to_use:
                if len(next_branches) < config.structure.max_parallel_paths:
                    next_branches.append(Branch(node=node, exit_fog=exit_fog))

        # Commit layer zones
        used_zones.update(layer_used_zones)
        current_branches = next_branches

        # Ensure we have at least 1 branch
        if not current_branches:
            raise GenerationError(f"No branches remaining after layer {layer_idx}")

    # 4. Create end node (final_boss)
    end_candidates = clusters.get_by_type("final_boss")
    if not end_candidates:
        raise GenerationError("No final_boss cluster found")

    end_cluster = pick_cluster(end_candidates, used_zones, rng, require_exits=False)
    if end_cluster is None:
        raise GenerationError("Could not pick final_boss cluster")

    entry_fog_end = None
    if end_cluster.entry_fogs:
        entry_fog_end = rng.choice(end_cluster.entry_fogs)["fog_id"]

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

    # Connect all branches to end
    for branch in current_branches:
        dag.add_edge(branch.node.id, end_node.id, branch.exit_fog)

    return dag


def generate_with_retry(
    config: Config,
    clusters: ClusterPool,
    max_attempts: int = 100,
) -> tuple[Dag, int]:
    """
    Generate DAG with automatic retry on failure.

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
```

---

## Task 2.4: Path Balancing (balance.py)

### Balancing Algorithm

```python
"""Path balancing for SpeedFog DAGs."""

from dataclasses import dataclass
from speedfog_core.config import BudgetConfig
from speedfog_core.dag import Dag


@dataclass
class PathStats:
    """Statistics about paths in a DAG."""
    paths: list[list[str]]      # List of paths (node ID sequences)
    weights: list[int]          # Weight of each path
    min_weight: int
    max_weight: int
    avg_weight: float

    @classmethod
    def from_dag(cls, dag: Dag) -> 'PathStats':
        paths = dag.enumerate_paths()
        weights = [dag.path_weight(p) for p in paths]
        return cls(
            paths=paths,
            weights=weights,
            min_weight=min(weights) if weights else 0,
            max_weight=max(weights) if weights else 0,
            avg_weight=sum(weights) / len(weights) if weights else 0,
        )


def analyze_balance(dag: Dag, budget: BudgetConfig) -> dict:
    """
    Analyze path balance in a DAG.

    Returns dict with:
        - is_balanced: bool
        - stats: PathStats
        - underweight_paths: list of (path, weight) tuples below budget.min_weight
        - overweight_paths: list of (path, weight) tuples above budget.max_weight
        - weight_spread: difference between max and min weights
    """
    stats = PathStats.from_dag(dag)

    underweight = []
    overweight = []

    for path, weight in zip(stats.paths, stats.weights):
        if weight < budget.min_weight:
            underweight.append((path, weight))
        elif weight > budget.max_weight:
            overweight.append((path, weight))

    return {
        'is_balanced': len(underweight) == 0 and len(overweight) == 0,
        'stats': stats,
        'underweight_paths': underweight,
        'overweight_paths': overweight,
        'weight_spread': stats.max_weight - stats.min_weight,
    }


def report_balance(dag: Dag, budget: BudgetConfig) -> str:
    """Generate a human-readable balance report."""
    analysis = analyze_balance(dag, budget)
    stats = analysis['stats']

    lines = [
        "=== Path Balance Report ===",
        f"Total paths: {len(stats.paths)}",
        f"Weight range: {stats.min_weight} - {stats.max_weight} (spread: {analysis['weight_spread']})",
        f"Average weight: {stats.avg_weight:.1f}",
        f"Target budget: {budget.total_weight} (+/- {budget.tolerance})",
        f"Acceptable range: {budget.min_weight} - {budget.max_weight}",
        "",
    ]

    if analysis['is_balanced']:
        lines.append("✓ All paths are within budget!")
    else:
        if analysis['underweight_paths']:
            lines.append(f"✗ {len(analysis['underweight_paths'])} underweight paths")
        if analysis['overweight_paths']:
            lines.append(f"✗ {len(analysis['overweight_paths'])} overweight paths")

    lines.append("")
    lines.append("Path details:")
    for i, (path, weight) in enumerate(zip(stats.paths, stats.weights)):
        status = "✓" if budget.min_weight <= weight <= budget.max_weight else "✗"
        # Show cluster IDs for each node
        cluster_ids = [dag.nodes[nid].cluster.id[:20] for nid in path]
        path_str = " → ".join(cluster_ids)
        lines.append(f"  {status} Path {i+1}: weight={weight}, {path_str}")

    return "\n".join(lines)
```

### Note on Balancing Strategy

With the uniform layer design, path balance is largely automatic:
- All branches in a layer have the same cluster type
- Cluster weights within a type are similar
- Post-generation validation catches outliers

For v1, we report imbalance but don't attempt automatic rebalancing. If paths are too unbalanced, the user can try a different seed.

---

## Task 2.5: Constraint Validation (validator.py)

```python
"""Validation of SpeedFog DAG constraints."""

from dataclasses import dataclass
from speedfog_core.config import Config
from speedfog_core.dag import Dag


@dataclass
class ValidationResult:
    """Result of DAG validation."""
    is_valid: bool
    errors: list[str]
    warnings: list[str]


def validate_dag(dag: Dag, config: Config) -> ValidationResult:
    """
    Validate a DAG against all constraints.

    Checks:
    - Structural validity (no dead ends, all reachable)
    - Minimum requirements (bosses, legacy dungeons, etc.)
    - Path count limits
    - Weight balance
    """
    errors = []
    warnings = []

    # Structural validation
    structural_errors = dag.validate_structure()
    errors.extend(structural_errors)

    # Requirement validation
    req = config.requirements

    legacy_count = dag.count_by_type("legacy_dungeon")
    if legacy_count < req.legacy_dungeons:
        errors.append(f"Insufficient legacy dungeons: {legacy_count} < {req.legacy_dungeons}")

    mini_count = dag.count_by_type("mini_dungeon")
    if mini_count < req.mini_dungeons:
        errors.append(f"Insufficient mini-dungeons: {mini_count} < {req.mini_dungeons}")

    boss_count = dag.count_by_type("boss_arena")
    if boss_count < req.bosses:
        errors.append(f"Insufficient boss arenas: {boss_count} < {req.bosses}")

    # Path count validation
    paths = dag.enumerate_paths()
    if len(paths) == 0:
        errors.append("No valid paths from start to end")
    elif len(paths) == 1:
        warnings.append("Only one path exists - no branching variety")

    # Weight validation
    budget = config.budget
    for i, path in enumerate(paths):
        weight = dag.path_weight(path)
        if weight < budget.min_weight:
            warnings.append(f"Path {i+1} underweight: {weight} < {budget.min_weight}")
        elif weight > budget.max_weight:
            warnings.append(f"Path {i+1} overweight: {weight} > {budget.max_weight}")

    # Layer count validation
    max_layer = max((n.layer for n in dag.nodes.values()), default=0)
    if max_layer < config.structure.min_layers:
        warnings.append(f"Few layers: {max_layer} < {config.structure.min_layers}")

    return ValidationResult(
        is_valid=len(errors) == 0,
        errors=errors,
        warnings=warnings,
    )
```

---

## Task 2.6: JSON Export (output.py)

```python
"""Export SpeedFog DAG to JSON format for C# writer."""

import json
from pathlib import Path
from typing import Any

from speedfog_core.dag import Dag


def dag_to_dict(dag: Dag) -> dict[str, Any]:
    """Convert DAG to JSON-serializable dict."""
    nodes_dict = {}
    for nid, node in dag.nodes.items():
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
        {
            "source": e.source_id,
            "target": e.target_id,
            "fog_id": e.fog_id,
        }
        for e in dag.edges
    ]

    paths = dag.enumerate_paths()

    return {
        "seed": dag.seed,
        "total_layers": max((n.layer for n in dag.nodes.values()), default=0) + 1,
        "total_nodes": len(dag.nodes),
        "total_zones": dag.total_zones(),
        "total_paths": len(paths),
        "path_weights": [dag.path_weight(p) for p in paths],
        "nodes": nodes_dict,
        "edges": edges_list,
        "start_id": dag.start_id,
        "end_id": dag.end_id,
    }


def export_json(dag: Dag, output_path: Path) -> None:
    """Export DAG to JSON file."""
    data = dag_to_dict(dag)
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)


def export_spoiler_log(dag: Dag, output_path: Path) -> None:
    """Export human-readable spoiler log."""
    lines = [
        "=" * 60,
        f"SPEEDFOG SPOILER LOG (seed: {dag.seed})",
        "=" * 60,
        "",
        f"Total nodes: {dag.total_nodes()}",
        f"Total zones: {dag.total_zones()}",
        f"Total paths: {len(dag.enumerate_paths())}",
        "",
    ]

    # Group nodes by layer
    by_layer: dict[int, list] = {}
    for node in dag.nodes.values():
        if node.layer not in by_layer:
            by_layer[node.layer] = []
        by_layer[node.layer].append(node)

    for layer_idx in sorted(by_layer.keys()):
        nodes = by_layer[layer_idx]
        tier = nodes[0].tier if nodes else 0
        lines.append(f"--- Layer {layer_idx} (tier {tier}) ---")
        for node in nodes:
            zones_str = ", ".join(node.cluster.zones)
            lines.append(
                f"  [{node.id}] {node.cluster.type}: {zones_str} "
                f"(w:{node.cluster.weight})"
            )
            if node.entry_fog:
                lines.append(f"    entry: {node.entry_fog}")
            if node.exit_fogs:
                lines.append(f"    exits: {', '.join(node.exit_fogs[:3])}{'...' if len(node.exit_fogs) > 3 else ''}")
        lines.append("")

    lines.append("=" * 60)
    lines.append("PATHS")
    lines.append("=" * 60)

    all_paths = dag.enumerate_paths()
    for i, node_path in enumerate(all_paths):
        weight = dag.path_weight(node_path)
        path_str = " -> ".join(node_path)
        lines.append(f"Path {i + 1} (weight {weight}): {path_str}")

    lines.append("")

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(lines))
```

---

## Task 2.7: CLI Entry Point (main.py)

```python
"""SpeedFog CLI entry point."""

import argparse
import sys
from pathlib import Path

from speedfog_core.config import load_config
from speedfog_core.clusters import load_clusters
from speedfog_core.generator import generate_with_retry
from speedfog_core.balance import report_balance
from speedfog_core.validator import validate_dag
from speedfog_core.output import export_json, export_spoiler_log


def main() -> int:
    parser = argparse.ArgumentParser(
        description="SpeedFog - Elden Ring zone randomizer DAG generator"
    )
    parser.add_argument(
        "config",
        type=Path,
        help="Path to config.toml",
    )
    parser.add_argument(
        "-o", "--output",
        type=Path,
        default=Path("graph.json"),
        help="Output JSON file (default: graph.json)",
    )
    parser.add_argument(
        "--spoiler",
        type=Path,
        help="Output spoiler log file",
    )
    parser.add_argument(
        "-v", "--verbose",
        action="store_true",
        help="Verbose output",
    )

    args = parser.parse_args()

    # Load configuration
    try:
        config = load_config(args.config)
    except FileNotFoundError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

    if args.verbose:
        print(f"Loaded config: seed={config.seed}")

    # Load clusters
    clusters_path = config.paths.clusters_file
    try:
        clusters = load_clusters(clusters_path)
    except FileNotFoundError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

    if args.verbose:
        print(f"Loaded {len(clusters.clusters)} clusters")

    # Generate DAG
    if args.verbose:
        print("Generating DAG...")

    try:
        dag, actual_seed = generate_with_retry(config, clusters)
    except Exception as e:
        print(f"Generation failed: {e}", file=sys.stderr)
        return 1

    if args.verbose:
        print(f"Generated DAG with seed {actual_seed}")
        print(f"  Nodes: {dag.total_nodes()}")
        print(f"  Zones: {dag.total_zones()}")
        print(f"  Paths: {len(dag.enumerate_paths())}")

    # Validate
    validation = validate_dag(dag, config)

    if validation.warnings:
        for warning in validation.warnings:
            print(f"Warning: {warning}", file=sys.stderr)

    if not validation.is_valid:
        for error in validation.errors:
            print(f"Error: {error}", file=sys.stderr)
        return 1

    # Report balance
    if args.verbose:
        print()
        print(report_balance(dag, config.budget))

    # Export
    export_json(dag, args.output)
    print(f"Written: {args.output}")

    if args.spoiler:
        export_spoiler_log(dag, args.spoiler)
        print(f"Written: {args.spoiler}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
```

---

## Acceptance Criteria

### Task 2.1 (DAG Data Structures)
- [ ] `DagNode`, `DagEdge`, `Dag` classes implemented
- [ ] `dag.enumerate_paths()` correctly finds all paths
- [ ] `dag.validate_structure()` detects dead ends and unreachable nodes

### Task 2.2 (Layer Planning)
- [ ] `plan_layer_types()` generates valid layer sequence
- [ ] Layer sequence satisfies requirements (legacy dungeons, bosses, mini-dungeons)
- [ ] `compute_tier()` returns values in range [1, 28]

### Task 2.3 (Generation)
- [ ] `generate_dag()` produces valid DAGs
- [ ] Start node uses cluster type "start"
- [ ] End node uses cluster type "final_boss"
- [ ] Each layer has uniform cluster type (same type for all branches)
- [ ] No cluster zone overlap (used_zones constraint)
- [ ] Entry fogs are selected to leave exits available
- [ ] `generate_with_retry()` handles seed=0 (auto-reroll) correctly

### Task 2.4 (Balancing)
- [ ] `analyze_balance()` correctly identifies under/overweight paths
- [ ] `report_balance()` produces human-readable output

### Task 2.5 (Validation)
- [ ] All structural errors detected
- [ ] Requirement shortfalls detected
- [ ] Warnings for edge cases (single path, weight issues)

### Task 2.6 (Export)
- [ ] JSON output matches expected schema
- [ ] Spoiler log is readable
- [ ] CLI works end-to-end

---

## Testing

### Manual Testing

```bash
# Generate with auto-reroll
cd core
python -m speedfog_core.main config.toml -o graph.json --spoiler spoiler.txt -v

# Generate with fixed seed
python -m speedfog_core.main config.toml -o graph.json --spoiler spoiler.txt -v
# (with seed = 12345 in config.toml)

# Verify JSON is valid
python -c "import json; json.load(open('graph.json'))"
```

### Unit Tests

```bash
pytest tests/ -v
```

Key test cases:
- Empty cluster pool → GenerationError
- Single-exit clusters → proper handling
- Zone overlap detection
- Path enumeration correctness
- Weight calculation

---

## Next Phase

After completing Phase 2, proceed to [Phase 3: C# Writer](./phase-3-csharp-writer.md).
