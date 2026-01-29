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
├── generator.py    # Task 2.2-2.3: Generation algorithm
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

## Task 2.2-2.3: Generation Algorithm (generator.py)

### Generator Class

```python
"""
DAG generation algorithm for SpeedFog.
"""

import random
from dataclasses import dataclass
from enum import Enum, auto
from typing import Callable

from speedfog_core.config import Config
from speedfog_core.zones import Zone, ZonePool, ZoneType
from speedfog_core.dag import DAG, Node, Layer


class Action(Enum):
    """Actions that can be taken at each node."""
    CONTINUE = auto()   # Single exit to next layer
    SPLIT = auto()      # Two exits (branch)
    # MERGE is handled separately (multiple nodes converge)


@dataclass
class GenerationContext:
    """Tracks state during generation."""
    config: Config
    zones: ZonePool
    rng: random.Random
    dag: DAG
    used_zones: set[str]  # Zone IDs already used (no repeats)

    # Tracking requirements
    legacy_count: int = 0
    boss_count: int = 0
    mini_dungeon_count: int = 0

    def zone_available(self, zone: Zone) -> bool:
        """Check if a zone can be used."""
        return zone.id not in self.used_zones

    def mark_used(self, zone: Zone) -> None:
        """Mark a zone as used."""
        self.used_zones.add(zone.id)
        if zone.type == ZoneType.LEGACY_DUNGEON:
            self.legacy_count += 1
        if zone.boss:
            self.boss_count += 1
        if zone.type.is_mini_dungeon():
            self.mini_dungeon_count += 1

    def requirements_met(self) -> bool:
        """Check if minimum requirements are satisfied."""
        req = self.config.requirements
        return (
            self.legacy_count >= req.legacy_dungeons and
            self.boss_count >= req.bosses and
            self.mini_dungeon_count >= req.mini_dungeons
        )

    def remaining_requirements(self) -> dict[str, int]:
        """Get remaining requirements to fulfill."""
        req = self.config.requirements
        return {
            'legacy_dungeons': max(0, req.legacy_dungeons - self.legacy_count),
            'bosses': max(0, req.bosses - self.boss_count),
            'mini_dungeons': max(0, req.mini_dungeons - self.mini_dungeon_count),
        }


def layer_to_tier(layer_index: int, total_layers: int) -> int:
    """
    Map layer index to difficulty tier (1-28).

    Uses smooth progression to avoid difficulty spikes.
    """
    if total_layers <= 1:
        return 1

    progress = layer_index / (total_layers - 1)
    # Tiers 1-28 (avoiding 29+ which are late DLC)
    return int(1 + progress * 27)


def select_zone(
    ctx: GenerationContext,
    layer_index: int,
    tier: int,
    prefer_type: ZoneType | None = None,
    require_split: bool = False,
) -> Zone | None:
    """
    Select a zone for a node.

    Args:
        ctx: Generation context
        layer_index: Current layer
        tier: Difficulty tier
        prefer_type: Preferred zone type (for requirements)
        require_split: If True, zone must have 2+ exits

    Returns:
        Selected zone or None if no valid zone found
    """
    candidates = []

    for zone in ctx.zones.all_zones():
        # Skip used zones
        if not ctx.zone_available(zone):
            continue

        # Skip if tier out of range
        if not (zone.min_tier <= tier <= zone.max_tier):
            continue

        # Skip if split required but zone can't split
        if require_split and not zone.can_split():
            continue

        # Skip start/final zones
        if zone.type in {ZoneType.START, ZoneType.FINAL_BOSS}:
            continue

        candidates.append(zone)

    if not candidates:
        return None

    # Weighting
    weights = []
    for zone in candidates:
        weight = 1.0

        # Prefer requested type
        if prefer_type and zone.type == prefer_type:
            weight *= 3.0

        # Prefer zones with bosses if boss count is low
        remaining = ctx.remaining_requirements()
        if remaining['bosses'] > 0 and zone.boss:
            weight *= 2.0

        # Prefer mini-dungeons if count is low
        if remaining['mini_dungeons'] > 0 and zone.type.is_mini_dungeon():
            weight *= 1.5

        # Prefer legacy dungeons if count is low
        if remaining['legacy_dungeons'] > 0 and zone.type == ZoneType.LEGACY_DUNGEON:
            weight *= 2.5

        weights.append(weight)

    return ctx.rng.choices(candidates, weights=weights, k=1)[0]


def decide_action(
    ctx: GenerationContext,
    current_layer: list[Node],
    layer_index: int,
    total_layers: int,
) -> list[Action]:
    """
    Decide actions for each node in current layer.

    Returns list of actions, one per node.
    """
    actions = []
    current_paths = len(current_layer)
    max_paths = ctx.config.structure.max_parallel_paths

    for node in current_layer:
        # Near the end, prefer continuing to allow convergence
        if layer_index >= total_layers - 2:
            actions.append(Action.CONTINUE)
            continue

        # Can this node split?
        can_split = node.zone.can_split() and current_paths < max_paths

        if can_split and ctx.rng.random() < ctx.config.structure.split_probability:
            actions.append(Action.SPLIT)
            current_paths += 1  # Track for max_paths limit
        else:
            actions.append(Action.CONTINUE)

    return actions


def should_merge(
    ctx: GenerationContext,
    layer_index: int,
    current_path_count: int,
) -> bool:
    """Decide if paths should merge at this layer."""
    if current_path_count <= 1:
        return False

    return ctx.rng.random() < ctx.config.structure.merge_probability


def generate_dag(config: Config, zones: ZonePool) -> DAG:
    """
    Generate a randomized DAG.

    Algorithm:
    1. Create start node (Chapel of Anticipation)
    2. For each layer:
       a. Decide split/continue/merge actions
       b. Select zones for new nodes
       c. Connect edges
    3. Converge all paths to end node (Radagon)
    4. Validate structure

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

    # Determine total layers
    total_layers = rng.randint(
        config.structure.min_layers,
        config.structure.max_layers
    )

    # --- Layer 0: Start ---
    start_zone = zones.get("chapel_of_anticipation")
    if start_zone is None:
        # Fallback: create a minimal start zone
        start_zone = Zone(
            id="chapel_of_anticipation",
            map="m10_01_00_00",
            name="Chapel of Anticipation",
            type=ZoneType.START,
            weight=0,
        )

    start_node = dag.add_node(start_zone, layer_index=0, tier=1, node_id="start")
    dag.start_node = start_node
    ctx.mark_used(start_zone)

    current_layer_nodes = [start_node]

    # --- Layers 1 to N-1: Generation ---
    for layer_index in range(1, total_layers):
        tier = layer_to_tier(layer_index, total_layers)
        next_layer_nodes: list[Node] = []

        # Check for merge opportunity
        if should_merge(ctx, layer_index, len(current_layer_nodes)):
            # Merge: all current nodes connect to a single new node
            merge_zone = select_zone(ctx, layer_index, tier)
            if merge_zone:
                merge_node = dag.add_node(merge_zone, layer_index, tier)
                ctx.mark_used(merge_zone)

                for prev_node in current_layer_nodes:
                    dag.connect(prev_node, merge_node)

                next_layer_nodes = [merge_node]
                current_layer_nodes = next_layer_nodes
                continue

        # Decide actions for each node
        actions = decide_action(ctx, current_layer_nodes, layer_index, total_layers)

        for node, action in zip(current_layer_nodes, actions):
            if action == Action.SPLIT:
                # Create two child nodes
                # Determine if we need specific types
                remaining = ctx.remaining_requirements()

                zone1 = select_zone(ctx, layer_index, tier)
                if zone1:
                    node1 = dag.add_node(zone1, layer_index, tier)
                    ctx.mark_used(zone1)
                    dag.connect(node, node1)
                    next_layer_nodes.append(node1)

                zone2 = select_zone(ctx, layer_index, tier)
                if zone2:
                    node2 = dag.add_node(zone2, layer_index, tier)
                    ctx.mark_used(zone2)
                    dag.connect(node, node2)
                    next_layer_nodes.append(node2)

            else:  # CONTINUE
                zone = select_zone(ctx, layer_index, tier)
                if zone:
                    new_node = dag.add_node(zone, layer_index, tier)
                    ctx.mark_used(zone)
                    dag.connect(node, new_node)
                    next_layer_nodes.append(new_node)

        # Fallback if no nodes created (shouldn't happen with proper zone pool)
        if not next_layer_nodes:
            raise RuntimeError(f"Failed to create nodes for layer {layer_index}")

        current_layer_nodes = next_layer_nodes

    # --- Final Layer: End (Radagon) ---
    end_zone = zones.get("radagon_arena")
    if end_zone is None:
        end_zone = Zone(
            id="radagon_arena",
            map="m19_00_00_00",
            name="Elden Throne",
            type=ZoneType.FINAL_BOSS,
            weight=5,
            boss="Radagon / Elden Beast",
        )

    end_node = dag.add_node(end_zone, layer_index=total_layers, tier=28, node_id="radagon")
    dag.end_node = end_node

    # Connect all remaining nodes to end
    for node in current_layer_nodes:
        dag.connect(node, end_node)

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
    """Export human-readable spoiler log."""
    lines = [
        "=" * 60,
        "SPEEDFOG SPOILER LOG",
        "=" * 60,
        f"Seed: {seed}",
        f"Total zones: {dag.total_zones()}",
        f"Total paths: {len(dag.enumerate_paths())}",
        "",
        "LAYER STRUCTURE:",
        "-" * 40,
    ]

    for layer in dag.layers:
        lines.append(f"\nLayer {layer.index} (Tier {layer.tier}):")
        for node in layer.nodes:
            boss_str = f" [BOSS: {node.zone.boss}]" if node.zone.boss else ""
            lines.append(f"  - {node.id}: {node.zone.name}{boss_str}")
            if node.entries:
                entries = ", ".join(n.id for n in node.entries)
                lines.append(f"      ← from: {entries}")
            if node.exits:
                exits = ", ".join(n.id for n in node.exits)
                lines.append(f"      → to: {exits}")

    lines.append("")
    lines.append("ALL PATHS:")
    lines.append("-" * 40)

    for i, path in enumerate(dag.enumerate_paths()):
        weight = dag.path_weight(path)
        path_str = " → ".join(n.zone.name[:20] for n in path)
        lines.append(f"\nPath {i+1} (weight {weight}):")
        lines.append(f"  {path_str}")

    with open(output_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(lines))
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

### Task 2.2-2.3 (Generation)
- [ ] `generate_dag()` produces valid DAGs
- [ ] Start node is Chapel of Anticipation
- [ ] End node is Radagon
- [ ] Split probability is respected
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
