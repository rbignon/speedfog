# Phase 2: DAG Generation - Detailed Implementation Spec

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Prerequisite**: [Phase 1: Foundations](./phase-1-foundations.md)
**Status**: Ready for implementation

## Objective

Implement the DAG (Directed Acyclic Graph) generation algorithm that creates balanced, randomized zone sequences for SpeedFog runs.

## Prerequisites

- Phase 1 completed (config.py, zones.py, zones.toml populated)
- Python 3.10+

## Deliverables

```
speedfog/core/speedfog_core/
├── dag.py          # Task 2.1: DAG data structures
├── planner.py      # Task 2.2: Layer planning
├── generator.py    # Task 2.3: Generation algorithm
├── balance.py      # Task 2.4: Path balancing
├── validator.py    # Task 2.5: Constraint validation
├── output.py       # Task 2.6: JSON export
└── main.py         # CLI entry point
```

---

## Task 2.1: DAG Data Structures (dag.py)

### Core Classes

```python
"""
DAG data structures for SpeedFog zone graphs.
"""

from dataclasses import dataclass, field
from typing import Iterator
from speedfog_core.zones import Zone


@dataclass
class Node:
    """A node in the DAG representing a zone instance."""
    id: str                          # Unique node ID (e.g., "node_1a")
    zone: Zone                       # The zone this node represents
    layer: int                       # Layer index (0 = start, N = end)
    tier: int                        # Difficulty tier for scaling

    # Connections (populated during generation)
    entries: list['Node'] = field(default_factory=list)  # Nodes that lead here
    exits: list['Node'] = field(default_factory=list)    # Nodes we lead to

    def __hash__(self) -> int:
        return hash(self.id)

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, Node):
            return False
        return self.id == other.id


@dataclass
class Edge:
    """A directed edge between two nodes."""
    source: Node
    target: Node
    # Future: could add fog gate type, warp ID, etc.

    def __hash__(self) -> int:
        return hash((self.source.id, self.target.id))


@dataclass
class Layer:
    """A layer in the DAG (all nodes at the same depth)."""
    index: int
    tier: int
    nodes: list[Node] = field(default_factory=list)

    def __len__(self) -> int:
        return len(self.nodes)

    def __iter__(self) -> Iterator[Node]:
        return iter(self.nodes)


@dataclass
class DAG:
    """
    Directed Acyclic Graph representing a SpeedFog run.

    Structure:
        Layer 0: Start (Chapel of Anticipation)
        Layer 1..N-1: Zone layers with splits/merges
        Layer N: End (Radagon)
    """
    layers: list[Layer] = field(default_factory=list)
    edges: list[Edge] = field(default_factory=list)

    # Special nodes
    start_node: Node | None = None
    end_node: Node | None = None

    # Node registry
    _nodes: dict[str, Node] = field(default_factory=dict, repr=False)
    _node_counter: int = field(default=0, repr=False)

    def _generate_node_id(self, layer: int) -> str:
        """Generate a unique node ID."""
        self._node_counter += 1
        suffix = chr(ord('a') + (self._node_counter - 1) % 26)
        return f"node_{layer}_{suffix}"

    def add_node(self, zone: Zone, layer_index: int, tier: int, node_id: str | None = None) -> Node:
        """Add a new node to the DAG."""
        if node_id is None:
            node_id = self._generate_node_id(layer_index)

        node = Node(
            id=node_id,
            zone=zone,
            layer=layer_index,
            tier=tier,
        )

        self._nodes[node_id] = node

        # Ensure layer exists
        while len(self.layers) <= layer_index:
            self.layers.append(Layer(index=len(self.layers), tier=tier, nodes=[]))

        self.layers[layer_index].nodes.append(node)
        self.layers[layer_index].tier = tier

        return node

    def connect(self, source: Node, target: Node) -> Edge:
        """Connect two nodes with a directed edge."""
        edge = Edge(source=source, target=target)
        self.edges.append(edge)
        source.exits.append(target)
        target.entries.append(source)
        return edge

    def get_node(self, node_id: str) -> Node | None:
        """Get node by ID."""
        return self._nodes.get(node_id)

    def all_nodes(self) -> list[Node]:
        """Get all nodes in the DAG."""
        return list(self._nodes.values())

    def enumerate_paths(self) -> list[list[Node]]:
        """
        Enumerate all possible paths from start to end.

        Returns list of paths, where each path is a list of nodes.
        """
        if self.start_node is None or self.end_node is None:
            return []

        paths: list[list[Node]] = []

        def dfs(node: Node, current_path: list[Node]) -> None:
            current_path = current_path + [node]

            if node == self.end_node:
                paths.append(current_path)
                return

            for next_node in node.exits:
                dfs(next_node, current_path)

        dfs(self.start_node, [])
        return paths

    def path_weight(self, path: list[Node]) -> int:
        """Calculate total weight of a path."""
        return sum(node.zone.weight for node in path)

    def total_zones(self) -> int:
        """Count total unique zones in the DAG."""
        return len(self._nodes)

    def count_bosses(self) -> int:
        """Count nodes with bosses."""
        return sum(1 for node in self._nodes.values() if node.zone.boss)

    def count_by_type(self, zone_type) -> int:
        """Count nodes of a specific zone type."""
        return sum(1 for node in self._nodes.values() if node.zone.type == zone_type)

    def validate_structure(self) -> list[str]:
        """
        Validate DAG structure.

        Returns list of error messages (empty if valid).
        """
        errors = []

        if self.start_node is None:
            errors.append("No start node defined")

        if self.end_node is None:
            errors.append("No end node defined")

        # Check all nodes are reachable from start
        if self.start_node:
            reachable = set()
            to_visit = [self.start_node]
            while to_visit:
                node = to_visit.pop()
                if node in reachable:
                    continue
                reachable.add(node)
                to_visit.extend(node.exits)

            unreachable = set(self._nodes.values()) - reachable
            if unreachable:
                errors.append(f"Unreachable nodes: {[n.id for n in unreachable]}")

        # Check all nodes can reach end
        if self.end_node:
            can_reach_end = set()
            to_visit = [self.end_node]
            while to_visit:
                node = to_visit.pop()
                if node in can_reach_end:
                    continue
                can_reach_end.add(node)
                to_visit.extend(node.entries)

            dead_ends = set(self._nodes.values()) - can_reach_end
            if dead_ends:
                errors.append(f"Dead end nodes: {[n.id for n in dead_ends]}")

        # Check no cycles (all edges go to higher layers)
        for edge in self.edges:
            if edge.source.layer >= edge.target.layer:
                errors.append(f"Invalid edge (not forward): {edge.source.id} -> {edge.target.id}")

        return errors
```

---

## Task 2.2: Layer Planning (planner.py)

The generation uses a **uniform layer** approach: each layer has a single zone type, ensuring competitive fairness.

### Layer Spec

```python
"""
Layer planning for SpeedFog DAG generation.
"""

from dataclasses import dataclass
from enum import Enum, auto
import random

from speedfog_core.config import Config
from speedfog_core.zones import ZoneType


class LayerStructure(Enum):
    """Structure of a layer (how branches flow through it)."""
    CONTINUE = auto()  # Each branch continues independently (N → N)
    SPLIT = auto()      # A branch splits into two (N → N+1), requires 3-fog zone
    MERGE = auto()      # Multiple branches merge (N → N-1), requires 3-fog zone


@dataclass
class LayerSpec:
    """Specification for a single layer."""
    zone_type: ZoneType       # Type of zone for this layer
    structure: LayerStructure  # How branches flow
    target_weight: int         # Target weight for zones in this layer


def plan_layers(config: Config, rng: random.Random) -> list[LayerSpec]:
    """
    Plan the sequence of layers to satisfy requirements.

    Returns:
        List of LayerSpec from start to end (excluding start/end nodes)
    """
    req = config.requirements
    struct = config.structure

    # Determine total layers (excluding start/end)
    total_layers = rng.randint(struct.min_layers, struct.max_layers)

    # Build layer type sequence to satisfy requirements
    layer_types: list[ZoneType] = []

    # Must include: legacy_dungeons, bosses, mini_dungeons
    for _ in range(req.legacy_dungeons):
        layer_types.append(ZoneType.LEGACY_DUNGEON)
    for _ in range(req.bosses):
        layer_types.append(ZoneType.BOSS_ARENA)
    for _ in range(req.mini_dungeons):
        # Distribute among mini-dungeon types
        mini_type = rng.choice([
            ZoneType.CATACOMB_MEDIUM,
            ZoneType.CAVE_MEDIUM,
            ZoneType.TUNNEL,
            ZoneType.GAOL,
        ])
        layer_types.append(mini_type)

    # Pad to total_layers if needed
    while len(layer_types) < total_layers:
        filler = rng.choice([
            ZoneType.CATACOMB_SHORT,
            ZoneType.CAVE_SHORT,
            ZoneType.BOSS_ARENA,
        ])
        layer_types.append(filler)

    # Trim if too many
    layer_types = layer_types[:total_layers]

    # Shuffle to randomize order
    rng.shuffle(layer_types)

    # Plan structure (splits/merges)
    specs: list[LayerSpec] = []
    current_branches = 1

    for i, zone_type in enumerate(layer_types):
        # Decide structure based on current branch count and limits
        structure = _decide_structure(
            rng, current_branches, struct.max_parallel_paths,
            is_near_end=(i >= len(layer_types) - 2)
        )

        # Update branch count
        if structure == LayerStructure.SPLIT:
            current_branches += 1
        elif structure == LayerStructure.MERGE and current_branches > 1:
            current_branches -= 1

        # Estimate target weight for this zone type
        target_weight = _estimate_weight(zone_type)

        specs.append(LayerSpec(
            zone_type=zone_type,
            structure=structure,
            target_weight=target_weight,
        ))

    return specs


def _decide_structure(
    rng: random.Random,
    current_branches: int,
    max_branches: int,
    is_near_end: bool,
) -> LayerStructure:
    """
    Decide layer structure based on current state.

    Note: Probabilities are intentionally hardcoded for v1 simplicity.
    The uniform layer design already ensures fairness - the exact
    split/merge frequency is a tuning parameter that can be exposed
    in config later if needed.
    """
    # Near the end, prefer merging to converge
    if is_near_end and current_branches > 1:
        return LayerStructure.MERGE

    # At max branches, can only continue or merge
    if current_branches >= max_branches:
        if current_branches > 1 and rng.random() < 0.3:
            return LayerStructure.MERGE
        return LayerStructure.CONTINUE

    # Single branch - consider splitting
    if current_branches == 1:
        if rng.random() < 0.4:
            return LayerStructure.SPLIT
        return LayerStructure.CONTINUE

    # Multiple branches - can split, continue, or merge
    roll = rng.random()
    if roll < 0.2:
        return LayerStructure.SPLIT
    elif roll < 0.4:
        return LayerStructure.MERGE
    return LayerStructure.CONTINUE


def _estimate_weight(zone_type: ZoneType) -> int:
    """Estimate typical weight for a zone type."""
    estimates = {
        ZoneType.LEGACY_DUNGEON: 15,
        ZoneType.CATACOMB_SHORT: 4,
        ZoneType.CATACOMB_MEDIUM: 6,
        ZoneType.CATACOMB_LONG: 9,
        ZoneType.CAVE_SHORT: 4,
        ZoneType.CAVE_MEDIUM: 6,
        ZoneType.CAVE_LONG: 9,
        ZoneType.TUNNEL: 5,
        ZoneType.GAOL: 3,
        ZoneType.BOSS_ARENA: 4,
    }
    return estimates.get(zone_type, 5)
```

---

## Task 2.3: Generation Algorithm (generator.py)

### Generator Class

```python
"""
DAG generation algorithm for SpeedFog.

Uses uniform layer design: each layer has a single zone type,
ensuring all paths face the same type of challenge.
"""

import random
from dataclasses import dataclass

from speedfog_core.config import Config
from speedfog_core.zones import Zone, ZonePool, ZoneType
from speedfog_core.dag import DAG, Node
from speedfog_core.planner import plan_layers, LayerSpec, LayerStructure


@dataclass
class GenerationContext:
    """Tracks state during generation."""
    config: Config
    zones: ZonePool
    rng: random.Random
    dag: DAG
    used_zones: set[str]  # Zone IDs already used (no repeats)

    def zone_available(self, zone: Zone) -> bool:
        """Check if a zone can be used."""
        return zone.id not in self.used_zones

    def mark_used(self, zone: Zone) -> None:
        """Mark a zone as used."""
        self.used_zones.add(zone.id)


def layer_to_tier(layer_index: int, total_layers: int) -> int:
    """
    Map layer index to difficulty tier (1-28).

    Uses smooth progression to avoid difficulty spikes.
    """
    if total_layers <= 1:
        return 1

    progress = layer_index / (total_layers - 1)
    return int(1 + progress * 27)


def select_zone(
    ctx: GenerationContext,
    zone_type: ZoneType,
    target_weight: int,
    weight_tolerance: int = 2,
    require_3_fogs: bool = False,
) -> Zone | None:
    """
    Select a zone matching criteria.

    Args:
        ctx: Generation context
        zone_type: Required zone type
        target_weight: Target weight
        weight_tolerance: Allowed deviation from target
        require_3_fogs: If True, zone must have 3 fog gates (for split/merge)

    Returns:
        Selected zone or None if no valid zone found
    """
    candidates = []

    for zone in ctx.zones.all_zones():
        # Skip used zones
        if not ctx.zone_available(zone):
            continue

        # Must match type
        if zone.type != zone_type:
            continue

        # Check weight is within tolerance
        if abs(zone.weight - target_weight) > weight_tolerance:
            continue

        # Check fog count if required
        if require_3_fogs and zone.fog_count < 3:
            continue

        # Skip start/final zones
        if zone.type in {ZoneType.START, ZoneType.FINAL_BOSS}:
            continue

        candidates.append(zone)

    if not candidates:
        # Relax weight tolerance and try again
        candidates = []  # Clear for fresh search without weight constraint
        for zone in ctx.zones.all_zones():
            if not ctx.zone_available(zone):
                continue
            if zone.type != zone_type:
                continue
            if require_3_fogs and zone.fog_count < 3:
                continue
            if zone.type in {ZoneType.START, ZoneType.FINAL_BOSS}:
                continue
            candidates.append(zone)

    if not candidates:
        return None

    return ctx.rng.choice(candidates)


def generate_dag(config: Config, zones: ZonePool) -> DAG:
    """
    Generate a randomized DAG with uniform layers.

    Algorithm:
    1. Plan layer sequence (types and structure)
    2. Create start node (Chapel of Anticipation)
    3. For each layer:
       a. Select zones of the planned type
       b. Handle splits/merges based on structure
       c. Ensure all branches have similar-weight zones
    4. Converge all paths to end node (Radagon)
    5. Validate structure

    Returns:
        Generated DAG
    """
    rng = random.Random(config.seed)
    dag = DAG()

    ctx = GenerationContext(
        config=config,
        zones=zones,
        rng=rng,
        dag=dag,
        used_zones=set(),
    )

    # 1. Plan layers
    layer_specs = plan_layers(config, rng)
    total_layers = len(layer_specs) + 2  # +2 for start and end

    # 2. Create start node
    start_zone = zones.get("chapel_of_anticipation")
    if start_zone is None:
        start_zone = Zone(
            id="chapel_of_anticipation",
            map="m10_01_00_00",
            name="Chapel of Anticipation",
            type=ZoneType.START,
            weight=0,
            fog_count=2,
        )

    start_node = dag.add_node(start_zone, layer_index=0, tier=1, node_id="start")
    dag.start_node = start_node
    ctx.mark_used(start_zone)

    current_branches: list[Node] = [start_node]

    # 3. Build each layer
    #
    # Key insight: SPLIT and MERGE affect the NUMBER of branches, not the zones themselves.
    # - CONTINUE: N branches → N branches (each branch gets 1 zone)
    # - SPLIT: N branches → N+1 branches (one zone has 3 fogs, connects to 2 children)
    # - MERGE: N branches → N-1 branches (multiple branches connect to 1 zone with 3 fogs)
    #
    # We track "pending splits" - nodes that need to spawn 2 children instead of 1.

    pending_splits: set[Node] = set()  # Nodes that will branch into 2 in next layer

    for layer_index, spec in enumerate(layer_specs, start=1):
        tier = layer_to_tier(layer_index, total_layers)
        next_branches: list[Node] = []
        next_pending_splits: set[Node] = set()

        if spec.structure == LayerStructure.MERGE and len(current_branches) > 1:
            # MERGE: all current branches converge to one node (3-fog zone)
            merge_zone = select_zone(
                ctx, spec.zone_type, spec.target_weight,
                require_3_fogs=True
            )
            if merge_zone:
                merge_node = dag.add_node(merge_zone, layer_index, tier)
                ctx.mark_used(merge_zone)
                for branch in current_branches:
                    dag.connect(branch, merge_node)
                next_branches = [merge_node]

        else:
            # CONTINUE or SPLIT: create zones for each branch
            # Handle pending splits from previous layer first
            branches_to_process = []
            for branch in current_branches:
                if branch in pending_splits:
                    # This branch spawns 2 children (it was a split point)
                    branches_to_process.append(branch)
                    branches_to_process.append(branch)  # Add twice for 2 children
                else:
                    branches_to_process.append(branch)

            # Now create zones for all branches
            for i, branch in enumerate(branches_to_process):
                # For SPLIT structure, one zone needs 3 fogs to become next split point
                need_split_zone = (spec.structure == LayerStructure.SPLIT and i == 0)

                zone = select_zone(
                    ctx, spec.zone_type, spec.target_weight,
                    require_3_fogs=need_split_zone
                )
                if zone:
                    node = dag.add_node(zone, layer_index, tier)
                    ctx.mark_used(zone)
                    dag.connect(branch, node)
                    next_branches.append(node)

                    # Mark as split point for next layer
                    if need_split_zone and zone.fog_count >= 3:
                        next_pending_splits.add(node)

        if not next_branches:
            raise RuntimeError(f"Failed to create nodes for layer {layer_index}")

        pending_splits = next_pending_splits

        current_branches = next_branches

    # 4. Create end node (Radagon)
    end_zone = zones.get("radagon_arena")
    if end_zone is None:
        end_zone = Zone(
            id="radagon_arena",
            map="m19_00_00_00",
            name="Elden Throne",
            type=ZoneType.FINAL_BOSS,
            weight=5,
            fog_count=2,
            boss="Radagon / Elden Beast",
        )

    end_node = dag.add_node(end_zone, layer_index=total_layers-1, tier=28, node_id="radagon")
    dag.end_node = end_node

    # Connect all remaining branches to end
    # Use set() to avoid duplicate connections from split nodes
    for branch in set(current_branches):
        dag.connect(branch, end_node)

    return dag
```

---

## Task 2.4: Path Balancing (balance.py)

### Balancing Algorithm

```python
"""
Path balancing for SpeedFog DAGs.

Ensures all paths through the DAG have similar total weights.
"""

from dataclasses import dataclass
from speedfog_core.config import BudgetConfig
from speedfog_core.dag import DAG, Node
from speedfog_core.zones import ZonePool, Zone


@dataclass
class PathStats:
    """Statistics about paths in a DAG."""
    paths: list[list[Node]]
    weights: list[int]
    min_weight: int
    max_weight: int
    avg_weight: float

    @classmethod
    def from_dag(cls, dag: DAG) -> 'PathStats':
        paths = dag.enumerate_paths()
        weights = [dag.path_weight(p) for p in paths]
        return cls(
            paths=paths,
            weights=weights,
            min_weight=min(weights) if weights else 0,
            max_weight=max(weights) if weights else 0,
            avg_weight=sum(weights) / len(weights) if weights else 0,
        )


def analyze_balance(dag: DAG, budget: BudgetConfig) -> dict:
    """
    Analyze path balance in a DAG.

    Returns dict with:
        - is_balanced: bool
        - stats: PathStats
        - underweight_paths: list of paths below budget.min_weight
        - overweight_paths: list of paths above budget.max_weight
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
    }


def find_swap_candidates(
    dag: DAG,
    zones: ZonePool,
    path: list[Node],
    target_delta: int,  # Positive = need heavier, negative = need lighter
) -> list[tuple[Node, Zone, int]]:
    """
    Find zones that could be swapped to adjust path weight.

    Returns list of (node_to_replace, replacement_zone, weight_delta).
    """
    candidates = []
    used_zone_ids = {n.zone.id for n in dag.all_nodes()}

    for node in path:
        # Skip start/end nodes
        if node == dag.start_node or node == dag.end_node:
            continue

        current_weight = node.zone.weight

        for zone in zones.all_zones():
            # Skip if already used
            if zone.id in used_zone_ids:
                continue

            # Skip if wrong tier range
            if not (zone.min_tier <= node.tier <= zone.max_tier):
                continue

            # Skip if zone type incompatible (e.g., can't replace legacy with catacomb)
            if zone.type != node.zone.type:
                continue

            delta = zone.weight - current_weight

            # Check if this swap moves us toward target
            if target_delta > 0 and delta > 0:
                candidates.append((node, zone, delta))
            elif target_delta < 0 and delta < 0:
                candidates.append((node, zone, delta))

    # Sort by how close delta is to target
    candidates.sort(key=lambda x: abs(x[2] - target_delta))

    return candidates


def balance_dag(
    dag: DAG,
    zones: ZonePool,
    budget: BudgetConfig,
    max_iterations: int = 100,
) -> bool:
    """
    Attempt to balance the DAG by swapping zones.

    Returns True if balancing succeeded, False otherwise.

    Note: Modifies the DAG in place.
    """
    for _ in range(max_iterations):
        analysis = analyze_balance(dag, budget)

        if analysis['is_balanced']:
            return True

        # Try to fix underweight paths first
        for path, weight in analysis['underweight_paths']:
            target_delta = budget.total_weight - weight
            candidates = find_swap_candidates(dag, zones, path, target_delta)

            if candidates:
                node, new_zone, _ = candidates[0]
                # Perform swap
                old_zone = node.zone
                node.zone = new_zone
                # Note: In a full implementation, we'd update used_zones tracking
                break

        # Then overweight paths
        for path, weight in analysis['overweight_paths']:
            target_delta = budget.total_weight - weight  # Negative
            candidates = find_swap_candidates(dag, zones, path, target_delta)

            if candidates:
                node, new_zone, _ = candidates[0]
                node.zone = new_zone
                break

    # If we get here, balancing failed
    return False


def report_balance(dag: DAG, budget: BudgetConfig) -> str:
    """Generate a human-readable balance report."""
    analysis = analyze_balance(dag, budget)
    stats = analysis['stats']

    lines = [
        "=== Path Balance Report ===",
        f"Total paths: {len(stats.paths)}",
        f"Weight range: {stats.min_weight} - {stats.max_weight}",
        f"Average weight: {stats.avg_weight:.1f}",
        f"Target budget: {budget.total_weight} (+/- {budget.tolerance})",
        f"Acceptable range: {budget.min_weight} - {budget.max_weight}",
        "",
    ]

    if analysis['is_balanced']:
        lines.append("✓ All paths are balanced!")
    else:
        if analysis['underweight_paths']:
            lines.append(f"✗ {len(analysis['underweight_paths'])} underweight paths")
        if analysis['overweight_paths']:
            lines.append(f"✗ {len(analysis['overweight_paths'])} overweight paths")

    lines.append("")
    lines.append("Path details:")
    for i, (path, weight) in enumerate(zip(stats.paths, stats.weights)):
        status = "✓" if budget.min_weight <= weight <= budget.max_weight else "✗"
        zones = " → ".join(n.zone.id[:15] for n in path)
        lines.append(f"  {status} Path {i+1}: weight={weight}, {zones}")

    return "\n".join(lines)
```

---

## Task 2.5: Constraint Validation (validator.py)

```python
"""
Validation of SpeedFog DAG constraints.
"""

from dataclasses import dataclass
from speedfog_core.config import Config
from speedfog_core.dag import DAG
from speedfog_core.zones import ZoneType


@dataclass
class ValidationResult:
    """Result of DAG validation."""
    is_valid: bool
    errors: list[str]
    warnings: list[str]


def validate_dag(dag: DAG, config: Config) -> ValidationResult:
    """
    Validate a DAG against all constraints.

    Checks:
    - Structural validity (no dead ends, all reachable)
    - Minimum requirements (bosses, legacy dungeons, etc.)
    - Path count limits
    """
    errors = []
    warnings = []

    # Structural validation
    structural_errors = dag.validate_structure()
    errors.extend(structural_errors)

    # Requirement validation
    req = config.requirements

    boss_count = dag.count_bosses()
    if boss_count < req.bosses:
        errors.append(f"Insufficient bosses: {boss_count} < {req.bosses}")

    legacy_count = dag.count_by_type(ZoneType.LEGACY_DUNGEON)
    if legacy_count < req.legacy_dungeons:
        errors.append(f"Insufficient legacy dungeons: {legacy_count} < {req.legacy_dungeons}")

    mini_count = sum(
        dag.count_by_type(zt) for zt in ZoneType
        if zt.is_mini_dungeon()
    )
    if mini_count < req.mini_dungeons:
        errors.append(f"Insufficient mini-dungeons: {mini_count} < {req.mini_dungeons}")

    # Path count validation
    paths = dag.enumerate_paths()
    if len(paths) == 0:
        errors.append("No valid paths from start to end")
    elif len(paths) == 1:
        warnings.append("Only one path exists - no branching")

    # Layer count validation
    if len(dag.layers) < config.structure.min_layers:
        warnings.append(f"Few layers: {len(dag.layers)} < {config.structure.min_layers}")
    if len(dag.layers) > config.structure.max_layers:
        warnings.append(f"Many layers: {len(dag.layers)} > {config.structure.max_layers}")

    return ValidationResult(
        is_valid=len(errors) == 0,
        errors=errors,
        warnings=warnings,
    )
```

---

## Task 2.6: JSON Export (output.py)

```python
"""
Export SpeedFog DAG to JSON format for C# writer.
"""

import json
from pathlib import Path
from typing import Any

from speedfog_core.dag import DAG, Node


def node_to_dict(node: Node) -> dict[str, Any]:
    """Convert a node to JSON-serializable dict."""
    return {
        'id': node.id,
        'zone': node.zone.id,
        'zone_name': node.zone.name,
        'zone_map': node.zone.map,
        'zone_type': node.zone.type.name.lower(),
        'weight': node.zone.weight,
        'boss': node.zone.boss or None,
        'entries': [n.id for n in node.entries],
        'exits': [n.id for n in node.exits],
    }


def dag_to_dict(dag: DAG, seed: int) -> dict[str, Any]:
    """Convert DAG to JSON-serializable dict."""
    layers = []

    for layer in dag.layers:
        layer_dict = {
            'index': layer.index,
            'tier': layer.tier,
            'nodes': [node_to_dict(n) for n in layer.nodes],
        }
        layers.append(layer_dict)

    return {
        'seed': seed,
        'total_layers': len(dag.layers),
        'total_nodes': dag.total_zones(),
        'total_paths': len(dag.enumerate_paths()),
        'layers': layers,
        'start': dag.start_node.id if dag.start_node else None,
        'end': dag.end_node.id if dag.end_node else None,
    }


def export_json(dag: DAG, seed: int, output_path: Path) -> None:
    """Export DAG to JSON file."""
    data = dag_to_dict(dag, seed)

    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)


def export_spoiler_log(dag: DAG, seed: int, output_path: Path) -> None:
    """
    Export human-readable spoiler log with ASCII graph visualization.

    Output format shows branches visually:

        === SPEEDFOG SPOILER (seed: 12345) ===

                  Chapel of Anticipation
                            │
                       ┌────┴────┐
                       │         │
                  Murkwater   Tombsward
                  Catacombs   Catacombs
                   (w:4)       (w:4)
                       │         │
                      ...       ...
                       │
                 Elden Throne
                  [Radagon]
    """
    lines = [
        "=" * 60,
        f"SPEEDFOG SPOILER (seed: {seed})",
        "=" * 60,
        f"Total zones: {dag.total_zones()}",
        f"Total paths: {len(dag.enumerate_paths())}",
        "",
    ]

    # Build ASCII graph visualization
    for layer in dag.layers:
        nodes = layer.nodes
        n_nodes = len(nodes)

        # Calculate column width based on longest zone name
        col_width = max(len(n.zone.name) for n in nodes) + 4 if nodes else 20
        col_width = max(col_width, 16)  # Minimum width

        # Draw connection lines from previous layer
        if layer.index > 0:
            # Determine merge/split structure
            prev_layer = dag.layers[layer.index - 1]
            if len(prev_layer.nodes) < n_nodes:
                # Split: draw branching lines
                lines.append(_center_text("┌────┴────┐", col_width * n_nodes))
            elif len(prev_layer.nodes) > n_nodes:
                # Merge: draw converging lines
                lines.append(_center_text("└────┬────┘", col_width * n_nodes))
            else:
                # Continue: straight lines
                pipe_line = "│".center(col_width) * n_nodes
                lines.append(pipe_line)

        # Draw zone names
        name_parts = []
        for node in nodes:
            name_parts.append(node.zone.name.center(col_width))
        lines.append("".join(name_parts))

        # Draw weights and boss info
        info_parts = []
        for node in nodes:
            boss_str = f"[{node.zone.boss}]" if node.zone.boss else ""
            info = f"(w:{node.zone.weight}) {boss_str}".strip()
            info_parts.append(info.center(col_width))
        lines.append("".join(info_parts))

        # Draw vertical lines to next layer (except for last layer)
        if layer.index < len(dag.layers) - 1:
            pipe_line = "│".center(col_width) * n_nodes
            lines.append(pipe_line)

    lines.append("")
    lines.append("=" * 60)
    lines.append("PATH SUMMARY")
    lines.append("=" * 60)

    for i, path in enumerate(dag.enumerate_paths()):
        weight = dag.path_weight(path)
        path_str = " → ".join(n.zone.name[:15] for n in path)
        lines.append(f"Path {i+1} (weight {weight}): {path_str}")

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(lines))


def _center_text(text: str, width: int) -> str:
    """Center text within given width."""
    return text.center(width)
```

---

## Task: main.py (CLI Entry Point)

```python
"""
SpeedFog CLI entry point.
"""

import argparse
import sys
from pathlib import Path

from speedfog_core.config import load_config
from speedfog_core.zones import load_zones
from speedfog_core.generator import generate_dag
from speedfog_core.balance import balance_dag, report_balance
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

    # Load zones
    try:
        zones = load_zones(config.paths.zones_file)
    except FileNotFoundError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

    if args.verbose:
        print(f"Loaded {len(zones.all_zones())} zones")

    # Generate DAG
    if args.verbose:
        print("Generating DAG...")

    dag = generate_dag(config, zones)

    if args.verbose:
        print(f"Generated DAG with {dag.total_zones()} zones, {len(dag.enumerate_paths())} paths")

    # Balance paths
    if args.verbose:
        print("Balancing paths...")

    balanced = balance_dag(dag, zones, config.budget)

    if args.verbose:
        print(report_balance(dag, config.budget))

    if not balanced:
        print("Warning: Could not fully balance paths", file=sys.stderr)

    # Validate
    validation = validate_dag(dag, config)

    if validation.warnings:
        for warning in validation.warnings:
            print(f"Warning: {warning}", file=sys.stderr)

    if not validation.is_valid:
        for error in validation.errors:
            print(f"Error: {error}", file=sys.stderr)
        return 1

    # Export
    export_json(dag, config.seed, args.output)
    print(f"Written: {args.output}")

    if args.spoiler:
        export_spoiler_log(dag, config.seed, args.spoiler)
        print(f"Written: {args.spoiler}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
```

---

## Acceptance Criteria

### Task 2.1 (DAG Data Structures)
- [ ] `Node`, `Edge`, `Layer`, `DAG` classes implemented
- [ ] `dag.enumerate_paths()` correctly finds all paths
- [ ] `dag.validate_structure()` detects dead ends and unreachable nodes

### Task 2.2 (Layer Planning)
- [ ] `plan_layers()` generates valid layer sequence
- [ ] Layer sequence satisfies requirements (legacy dungeons, bosses, mini-dungeons)
- [ ] Split/merge points are planned within max_parallel_paths limit

### Task 2.3 (Generation)
- [ ] `generate_dag()` produces valid DAGs
- [ ] Start node is Chapel of Anticipation
- [ ] End node is Radagon
- [ ] Each layer has uniform zone type (same type for all branches)
- [ ] Zones in same layer have similar weights
- [ ] Splits/merges only use zones with fog_count >= 3
- [ ] No zone is used twice

### Task 2.4 (Balancing)
- [ ] `analyze_balance()` correctly identifies under/overweight paths
- [ ] `balance_dag()` improves path weight distribution
- [ ] Report is human-readable

### Task 2.5 (Validation)
- [ ] All structural errors detected
- [ ] Requirement shortfalls detected
- [ ] Warnings for edge cases

### Task 2.6 (Export)
- [ ] JSON output matches expected schema
- [ ] Spoiler log is readable
- [ ] CLI works end-to-end

---

## Testing

Run with various seeds and verify:

```bash
# Generate with seed 12345
speedfog config.toml -o graph.json --spoiler spoiler.txt -v

# Verify JSON is valid
python -c "import json; json.load(open('graph.json'))"

# Run tests
pytest tests/ -v
```

---

## Next Phase

After completing Phase 2, proceed to [Phase 3: C# Writer](./phase-3-csharp-writer.md).
