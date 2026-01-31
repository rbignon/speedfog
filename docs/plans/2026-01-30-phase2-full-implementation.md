# Phase 2: Full DAG Generation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete the Phase 2 DAG generation implementation with proper module separation, dynamic branching, split/merge logic, validation, and comprehensive tests.

**Architecture:** Refactor existing prototype into separate modules following the spec. The generator will support dynamic branch counts based on cluster exit fogs, proper split/merge mechanics, path balancing analysis, and constraint validation.

**Tech Stack:** Python 3.10+, pytest, dataclasses

---

## Current State

The prototype has:
- `config.py` ✅ Complete
- `clusters.py` ✅ Complete
- `generator.py` - Has `Dag`, `DagNode`, `DagEdge` mixed with generation logic, fixed 2-branch design
- `main.py` ✅ Working CLI
- `test_config.py` ✅ Basic tests

## Target State

```
speedfog_core/
├── __init__.py
├── config.py         # ✅ No changes needed
├── clusters.py       # ✅ No changes needed
├── dag.py            # NEW: DAG data structures only
├── planner.py        # NEW: Layer type planning
├── generator.py      # REFACTOR: Generation algorithm only
├── balance.py        # NEW: Path balancing analysis
├── validator.py      # NEW: Constraint validation
├── output.py         # NEW: JSON/spoiler export
└── main.py           # UPDATE: Use new modules

tests/
├── __init__.py
├── test_config.py    # ✅ Exists
├── test_dag.py       # NEW
├── test_planner.py   # NEW
├── test_generator.py # NEW
├── test_balance.py   # NEW
├── test_validator.py # NEW
└── test_output.py    # NEW
```

---

## Task 1: Extract DAG Data Structures (dag.py)

**Files:**
- Create: `core/speedfog_core/dag.py`
- Test: `core/tests/test_dag.py`

**Step 1: Write the failing tests for dag.py**

```python
# core/tests/test_dag.py
"""Tests for DAG data structures."""

import pytest

from speedfog_core.dag import Dag, DagEdge, DagNode
from speedfog_core.clusters import ClusterData


def make_cluster(cluster_id: str, zones: list[str], weight: int = 10) -> ClusterData:
    """Helper to create test clusters."""
    return ClusterData(
        id=cluster_id,
        zones=zones,
        type="mini_dungeon",
        weight=weight,
        entry_fogs=[{"fog_id": "entry1", "zone": zones[0]}],
        exit_fogs=[{"fog_id": "exit1", "zone": zones[0]}],
    )


class TestDagNode:
    """Tests for DagNode."""

    def test_node_hash_by_id(self):
        """Nodes with same ID have same hash."""
        cluster = make_cluster("c1", ["z1"])
        node1 = DagNode(id="n1", cluster=cluster, layer=0, tier=1, entry_fog=None)
        node2 = DagNode(id="n1", cluster=cluster, layer=0, tier=1, entry_fog=None)
        assert hash(node1) == hash(node2)

    def test_node_equality_by_id(self):
        """Nodes are equal if they have same ID."""
        cluster1 = make_cluster("c1", ["z1"])
        cluster2 = make_cluster("c2", ["z2"])
        node1 = DagNode(id="n1", cluster=cluster1, layer=0, tier=1, entry_fog=None)
        node2 = DagNode(id="n1", cluster=cluster2, layer=1, tier=5, entry_fog="fog")
        assert node1 == node2

    def test_node_inequality(self):
        """Nodes with different IDs are not equal."""
        cluster = make_cluster("c1", ["z1"])
        node1 = DagNode(id="n1", cluster=cluster, layer=0, tier=1, entry_fog=None)
        node2 = DagNode(id="n2", cluster=cluster, layer=0, tier=1, entry_fog=None)
        assert node1 != node2


class TestDag:
    """Tests for Dag structure."""

    def test_add_node(self):
        """Can add nodes to DAG."""
        dag = Dag(seed=42)
        cluster = make_cluster("c1", ["z1"])
        node = DagNode(id="n1", cluster=cluster, layer=0, tier=1, entry_fog=None)
        dag.add_node(node)
        assert "n1" in dag.nodes
        assert dag.get_node("n1") == node

    def test_add_edge(self):
        """Can add edges to DAG."""
        dag = Dag(seed=42)
        dag.add_edge("n1", "n2", "fog1")
        assert len(dag.edges) == 1
        assert dag.edges[0].source_id == "n1"
        assert dag.edges[0].target_id == "n2"

    def test_get_outgoing_edges(self):
        """get_outgoing_edges returns edges from a node."""
        dag = Dag(seed=42)
        dag.add_edge("n1", "n2", "fog1")
        dag.add_edge("n1", "n3", "fog2")
        dag.add_edge("n2", "n3", "fog3")

        outgoing = dag.get_outgoing_edges("n1")
        assert len(outgoing) == 2
        targets = {e.target_id for e in outgoing}
        assert targets == {"n2", "n3"}

    def test_get_incoming_edges(self):
        """get_incoming_edges returns edges to a node."""
        dag = Dag(seed=42)
        dag.add_edge("n1", "n3", "fog1")
        dag.add_edge("n2", "n3", "fog2")
        dag.add_edge("n1", "n2", "fog3")

        incoming = dag.get_incoming_edges("n3")
        assert len(incoming) == 2
        sources = {e.source_id for e in incoming}
        assert sources == {"n1", "n2"}


class TestDagPathEnumeration:
    """Tests for path enumeration."""

    def _build_simple_dag(self) -> Dag:
        """Build: start -> n1 -> end."""
        dag = Dag(seed=42)
        c_start = make_cluster("start_c", ["start_z"], weight=0)
        c1 = make_cluster("c1", ["z1"], weight=10)
        c_end = make_cluster("end_c", ["end_z"], weight=0)

        dag.add_node(DagNode(id="start", cluster=c_start, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="n1", cluster=c1, layer=1, tier=5, entry_fog="e1"))
        dag.add_node(DagNode(id="end", cluster=c_end, layer=2, tier=28, entry_fog="e2"))
        dag.start_id = "start"
        dag.end_id = "end"

        dag.add_edge("start", "n1", "fog1")
        dag.add_edge("n1", "end", "fog2")
        return dag

    def _build_forked_dag(self) -> Dag:
        """Build: start -> n1a, n1b -> end (2 paths)."""
        dag = Dag(seed=42)
        c_start = make_cluster("start_c", ["start_z"], weight=0)
        c1a = make_cluster("c1a", ["z1a"], weight=10)
        c1b = make_cluster("c1b", ["z1b"], weight=15)
        c_end = make_cluster("end_c", ["end_z"], weight=0)

        dag.add_node(DagNode(id="start", cluster=c_start, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="n1a", cluster=c1a, layer=1, tier=5, entry_fog="e1"))
        dag.add_node(DagNode(id="n1b", cluster=c1b, layer=1, tier=5, entry_fog="e2"))
        dag.add_node(DagNode(id="end", cluster=c_end, layer=2, tier=28, entry_fog="e3"))
        dag.start_id = "start"
        dag.end_id = "end"

        dag.add_edge("start", "n1a", "fog1")
        dag.add_edge("start", "n1b", "fog2")
        dag.add_edge("n1a", "end", "fog3")
        dag.add_edge("n1b", "end", "fog4")
        return dag

    def test_enumerate_paths_linear(self):
        """enumerate_paths finds single path in linear DAG."""
        dag = self._build_simple_dag()
        paths = dag.enumerate_paths()
        assert len(paths) == 1
        assert paths[0] == ["start", "n1", "end"]

    def test_enumerate_paths_forked(self):
        """enumerate_paths finds both paths in forked DAG."""
        dag = self._build_forked_dag()
        paths = dag.enumerate_paths()
        assert len(paths) == 2
        assert ["start", "n1a", "end"] in paths
        assert ["start", "n1b", "end"] in paths

    def test_path_weight(self):
        """path_weight sums cluster weights."""
        dag = self._build_forked_dag()
        paths = dag.enumerate_paths()
        weights = {tuple(p): dag.path_weight(p) for p in paths}
        # start(0) + n1a(10) + end(0) = 10
        assert weights[("start", "n1a", "end")] == 10
        # start(0) + n1b(15) + end(0) = 15
        assert weights[("start", "n1b", "end")] == 15

    def test_total_nodes(self):
        """total_nodes counts all nodes."""
        dag = self._build_forked_dag()
        assert dag.total_nodes() == 4

    def test_total_zones(self):
        """total_zones counts unique zones across all clusters."""
        dag = self._build_forked_dag()
        assert dag.total_zones() == 4  # start_z, z1a, z1b, end_z

    def test_count_by_type(self):
        """count_by_type counts nodes of specific cluster type."""
        dag = self._build_forked_dag()
        # All our test clusters are mini_dungeon
        assert dag.count_by_type("mini_dungeon") == 4
        assert dag.count_by_type("legacy_dungeon") == 0


class TestDagValidation:
    """Tests for DAG structural validation."""

    def test_validate_missing_start(self):
        """validate_structure detects missing start node."""
        dag = Dag(seed=42)
        dag.end_id = "end"
        errors = dag.validate_structure()
        assert any("start" in e.lower() for e in errors)

    def test_validate_missing_end(self):
        """validate_structure detects missing end node."""
        dag = Dag(seed=42)
        dag.start_id = "start"
        errors = dag.validate_structure()
        assert any("end" in e.lower() for e in errors)

    def test_validate_unreachable_node(self):
        """validate_structure detects unreachable nodes."""
        dag = Dag(seed=42)
        c = make_cluster("c1", ["z1"])
        dag.add_node(DagNode(id="start", cluster=c, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="orphan", cluster=c, layer=1, tier=5, entry_fog="e"))
        dag.add_node(DagNode(id="end", cluster=c, layer=2, tier=28, entry_fog="e"))
        dag.start_id = "start"
        dag.end_id = "end"
        dag.add_edge("start", "end", "fog1")  # orphan not connected

        errors = dag.validate_structure()
        assert any("unreachable" in e.lower() for e in errors)

    def test_validate_dead_end(self):
        """validate_structure detects dead ends (nodes that can't reach end)."""
        dag = Dag(seed=42)
        c = make_cluster("c1", ["z1"])
        dag.add_node(DagNode(id="start", cluster=c, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="dead", cluster=c, layer=1, tier=5, entry_fog="e"))
        dag.add_node(DagNode(id="end", cluster=c, layer=2, tier=28, entry_fog="e"))
        dag.start_id = "start"
        dag.end_id = "end"
        dag.add_edge("start", "dead", "fog1")  # dead doesn't connect to end
        dag.add_edge("start", "end", "fog2")

        errors = dag.validate_structure()
        assert any("dead end" in e.lower() for e in errors)

    def test_validate_backward_edge(self):
        """validate_structure detects backward edges (not forward layers)."""
        dag = Dag(seed=42)
        c = make_cluster("c1", ["z1"])
        dag.add_node(DagNode(id="start", cluster=c, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="n1", cluster=c, layer=1, tier=5, entry_fog="e"))
        dag.add_node(DagNode(id="end", cluster=c, layer=2, tier=28, entry_fog="e"))
        dag.start_id = "start"
        dag.end_id = "end"
        dag.add_edge("start", "n1", "fog1")
        dag.add_edge("n1", "end", "fog2")
        dag.add_edge("n1", "start", "fog3")  # backward edge!

        errors = dag.validate_structure()
        assert any("not forward" in e.lower() for e in errors)

    def test_validate_valid_dag(self):
        """validate_structure returns empty list for valid DAG."""
        dag = Dag(seed=42)
        c = make_cluster("c1", ["z1"])
        dag.add_node(DagNode(id="start", cluster=c, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="n1", cluster=c, layer=1, tier=5, entry_fog="e"))
        dag.add_node(DagNode(id="end", cluster=c, layer=2, tier=28, entry_fog="e"))
        dag.start_id = "start"
        dag.end_id = "end"
        dag.add_edge("start", "n1", "fog1")
        dag.add_edge("n1", "end", "fog2")

        errors = dag.validate_structure()
        assert errors == []
```

**Step 2: Run tests to verify they fail**

Run: `cd core && uv run pytest tests/test_dag.py -v`
Expected: ImportError or failures (dag.py doesn't exist)

**Step 3: Create dag.py with the data structures**

```python
# core/speedfog_core/dag.py
"""DAG data structures for SpeedFog."""

from __future__ import annotations

from dataclasses import dataclass, field

from speedfog_core.clusters import ClusterData


@dataclass
class DagNode:
    """A node in the DAG representing a cluster instance."""

    id: str
    cluster: ClusterData
    layer: int
    tier: int  # Difficulty scaling (1-28)
    entry_fog: str | None  # fog_id used to enter (None for start)
    exit_fogs: list[str] = field(default_factory=list)  # Available exits

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
    """Directed Acyclic Graph representing a SpeedFog run.

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
        """Enumerate all possible paths from start to end.

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
        zones: set[str] = set()
        for node in self.nodes.values():
            zones.update(node.cluster.zones)
        return len(zones)

    def count_by_type(self, cluster_type: str) -> int:
        """Count nodes of a specific cluster type."""
        return sum(1 for node in self.nodes.values() if node.cluster.type == cluster_type)

    def validate_structure(self) -> list[str]:
        """Validate DAG structure.

        Returns list of error messages (empty if valid).
        """
        errors: list[str] = []

        if not self.start_id:
            errors.append("No start node defined")

        if not self.end_id:
            errors.append("No end node defined")

        # Check all nodes are reachable from start
        if self.start_id:
            reachable: set[str] = set()
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
                errors.append(f"Unreachable nodes: {sorted(unreachable)}")

        # Check all nodes can reach end
        if self.end_id:
            can_reach_end: set[str] = set()
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
                errors.append(f"Dead end nodes: {sorted(dead_ends)}")

        # Check no cycles (all edges go to higher layers)
        for edge in self.edges:
            source = self.nodes.get(edge.source_id)
            target = self.nodes.get(edge.target_id)
            if source and target and source.layer >= target.layer:
                errors.append(
                    f"Invalid edge (not forward): {edge.source_id} -> {edge.target_id}"
                )

        return errors
```

**Step 4: Run tests to verify they pass**

Run: `cd core && uv run pytest tests/test_dag.py -v`
Expected: All tests pass

**Step 5: Commit**

```bash
git add core/speedfog_core/dag.py core/tests/test_dag.py
git commit -m "$(cat <<'EOF'
feat(dag): extract DAG data structures to separate module

- Move DagNode, DagEdge, Dag classes from generator.py to dag.py
- Add hash/equality methods to DagNode and DagEdge
- Add enumerate_paths, path_weight, validate_structure methods
- Add comprehensive tests for all DAG operations
EOF
)"
```

---

## Task 2: Create Layer Planner (planner.py)

**Files:**
- Create: `core/speedfog_core/planner.py`
- Test: `core/tests/test_planner.py`

**Step 1: Write the failing tests**

```python
# core/tests/test_planner.py
"""Tests for layer planning."""

import random

import pytest

from speedfog_core.config import RequirementsConfig
from speedfog_core.planner import compute_tier, plan_layer_types


class TestComputeTier:
    """Tests for difficulty tier calculation."""

    def test_first_layer_tier_1(self):
        """First layer should be tier 1."""
        assert compute_tier(0, 10) == 1

    def test_last_layer_tier_28(self):
        """Last layer should be tier 28."""
        assert compute_tier(9, 10) == 28

    def test_middle_layer_intermediate(self):
        """Middle layers have intermediate tiers."""
        tier = compute_tier(5, 10)
        assert 1 < tier < 28

    def test_single_layer_tier_1(self):
        """Single layer edge case returns tier 1."""
        assert compute_tier(0, 1) == 1

    def test_tier_bounds(self):
        """All tiers are within [1, 28]."""
        for total in [2, 5, 10, 20]:
            for layer in range(total):
                tier = compute_tier(layer, total)
                assert 1 <= tier <= 28


class TestPlanLayerTypes:
    """Tests for layer type planning."""

    def test_includes_required_legacy_dungeons(self):
        """Plan includes at least N legacy dungeons."""
        req = RequirementsConfig(legacy_dungeons=2, bosses=1, mini_dungeons=1)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=10, rng=rng)
        assert types.count("legacy_dungeon") >= 2

    def test_includes_required_bosses(self):
        """Plan includes at least N boss arenas."""
        req = RequirementsConfig(legacy_dungeons=1, bosses=3, mini_dungeons=1)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=10, rng=rng)
        assert types.count("boss_arena") >= 3

    def test_includes_required_mini_dungeons(self):
        """Plan includes at least N mini dungeons."""
        req = RequirementsConfig(legacy_dungeons=1, bosses=1, mini_dungeons=4)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=10, rng=rng)
        assert types.count("mini_dungeon") >= 4

    def test_output_length_matches_total(self):
        """Plan output has exactly total_layers entries."""
        req = RequirementsConfig(legacy_dungeons=1, bosses=1, mini_dungeons=1)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=8, rng=rng)
        assert len(types) == 8

    def test_pads_with_mini_dungeons(self):
        """When requirements don't fill layers, pad with mini_dungeons."""
        req = RequirementsConfig(legacy_dungeons=1, bosses=1, mini_dungeons=1)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=10, rng=rng)
        # 1 legacy + 1 boss + 1 mini = 3, needs 7 more mini_dungeons
        assert types.count("mini_dungeon") >= 8  # 1 required + 7 padding

    def test_trims_if_too_many_requirements(self):
        """When requirements exceed layers, trim to fit."""
        req = RequirementsConfig(legacy_dungeons=5, bosses=5, mini_dungeons=5)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=5, rng=rng)
        assert len(types) == 5

    def test_shuffled_order(self):
        """Types are shuffled (not in insertion order)."""
        req = RequirementsConfig(legacy_dungeons=3, bosses=3, mini_dungeons=3)
        rng = random.Random(42)
        types = plan_layer_types(req, total_layers=9, rng=rng)
        # Check it's not perfectly sorted (highly unlikely with random)
        sorted_types = sorted(types)
        assert types != sorted_types or len(set(types)) == 1

    def test_different_seeds_different_order(self):
        """Different seeds produce different orderings."""
        req = RequirementsConfig(legacy_dungeons=2, bosses=2, mini_dungeons=4)
        types1 = plan_layer_types(req, total_layers=8, rng=random.Random(1))
        types2 = plan_layer_types(req, total_layers=8, rng=random.Random(2))
        # Same composition but different order
        assert sorted(types1) == sorted(types2)
        assert types1 != types2
```

**Step 2: Run tests to verify they fail**

Run: `cd core && uv run pytest tests/test_planner.py -v`
Expected: ImportError (planner.py doesn't exist)

**Step 3: Create planner.py**

```python
# core/speedfog_core/planner.py
"""Layer planning for SpeedFog DAG generation."""

from __future__ import annotations

import random

from speedfog_core.config import RequirementsConfig


def compute_tier(layer_idx: int, total_layers: int) -> int:
    """Map layer index to difficulty tier (1-28).

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


def plan_layer_types(
    requirements: RequirementsConfig,
    total_layers: int,
    rng: random.Random,
) -> list[str]:
    """Plan the sequence of cluster types for intermediate layers.

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
```

**Step 4: Run tests to verify they pass**

Run: `cd core && uv run pytest tests/test_planner.py -v`
Expected: All tests pass

**Step 5: Commit**

```bash
git add core/speedfog_core/planner.py core/tests/test_planner.py
git commit -m "$(cat <<'EOF'
feat(planner): add layer type planning module

- compute_tier: map layer index to difficulty tier (1-28)
- plan_layer_types: generate shuffled layer type sequence
- Ensures minimum requirements are met before padding
EOF
)"
```

---

## Task 3: Create Balance Analysis (balance.py)

**Files:**
- Create: `core/speedfog_core/balance.py`
- Test: `core/tests/test_balance.py`

**Step 1: Write the failing tests**

```python
# core/tests/test_balance.py
"""Tests for path balancing analysis."""

import pytest

from speedfog_core.balance import PathStats, analyze_balance, report_balance
from speedfog_core.clusters import ClusterData
from speedfog_core.config import BudgetConfig
from speedfog_core.dag import Dag, DagNode


def make_cluster(cluster_id: str, zones: list[str], weight: int) -> ClusterData:
    """Helper to create test clusters."""
    return ClusterData(
        id=cluster_id,
        zones=zones,
        type="mini_dungeon",
        weight=weight,
        entry_fogs=[{"fog_id": "entry1", "zone": zones[0]}],
        exit_fogs=[{"fog_id": "exit1", "zone": zones[0]}],
    )


def build_two_path_dag(weight_a: int, weight_b: int) -> Dag:
    """Build DAG with two paths of different weights.

    Path A: start(0) -> n1a(weight_a) -> end(0)
    Path B: start(0) -> n1b(weight_b) -> end(0)
    """
    dag = Dag(seed=42)

    c_start = make_cluster("start_c", ["start_z"], weight=0)
    c1a = make_cluster("c1a", ["z1a"], weight=weight_a)
    c1b = make_cluster("c1b", ["z1b"], weight=weight_b)
    c_end = make_cluster("end_c", ["end_z"], weight=0)

    dag.add_node(DagNode(id="start", cluster=c_start, layer=0, tier=1, entry_fog=None))
    dag.add_node(DagNode(id="n1a", cluster=c1a, layer=1, tier=5, entry_fog="e1"))
    dag.add_node(DagNode(id="n1b", cluster=c1b, layer=1, tier=5, entry_fog="e2"))
    dag.add_node(DagNode(id="end", cluster=c_end, layer=2, tier=28, entry_fog="e3"))
    dag.start_id = "start"
    dag.end_id = "end"

    dag.add_edge("start", "n1a", "fog1")
    dag.add_edge("start", "n1b", "fog2")
    dag.add_edge("n1a", "end", "fog3")
    dag.add_edge("n1b", "end", "fog4")

    return dag


class TestPathStats:
    """Tests for PathStats dataclass."""

    def test_from_dag(self):
        """PathStats.from_dag computes correct statistics."""
        dag = build_two_path_dag(weight_a=10, weight_b=20)
        stats = PathStats.from_dag(dag)

        assert len(stats.paths) == 2
        assert len(stats.weights) == 2
        assert stats.min_weight == 10
        assert stats.max_weight == 20
        assert stats.avg_weight == 15.0

    def test_empty_dag(self):
        """PathStats handles empty DAG."""
        dag = Dag(seed=42)
        stats = PathStats.from_dag(dag)

        assert stats.paths == []
        assert stats.weights == []
        assert stats.min_weight == 0
        assert stats.max_weight == 0
        assert stats.avg_weight == 0


class TestAnalyzeBalance:
    """Tests for analyze_balance function."""

    def test_balanced_paths(self):
        """Paths within budget are balanced."""
        dag = build_two_path_dag(weight_a=28, weight_b=32)
        budget = BudgetConfig(total_weight=30, tolerance=5)  # 25-35

        analysis = analyze_balance(dag, budget)

        assert analysis["is_balanced"] is True
        assert analysis["underweight_paths"] == []
        assert analysis["overweight_paths"] == []

    def test_underweight_path(self):
        """Path below budget.min_weight is underweight."""
        dag = build_two_path_dag(weight_a=20, weight_b=30)
        budget = BudgetConfig(total_weight=30, tolerance=5)  # 25-35

        analysis = analyze_balance(dag, budget)

        assert analysis["is_balanced"] is False
        assert len(analysis["underweight_paths"]) == 1
        assert analysis["underweight_paths"][0][1] == 20  # weight

    def test_overweight_path(self):
        """Path above budget.max_weight is overweight."""
        dag = build_two_path_dag(weight_a=30, weight_b=40)
        budget = BudgetConfig(total_weight=30, tolerance=5)  # 25-35

        analysis = analyze_balance(dag, budget)

        assert analysis["is_balanced"] is False
        assert len(analysis["overweight_paths"]) == 1
        assert analysis["overweight_paths"][0][1] == 40  # weight

    def test_weight_spread(self):
        """weight_spread is max - min."""
        dag = build_two_path_dag(weight_a=10, weight_b=25)
        budget = BudgetConfig(total_weight=20, tolerance=10)

        analysis = analyze_balance(dag, budget)

        assert analysis["weight_spread"] == 15


class TestReportBalance:
    """Tests for report_balance function."""

    def test_report_contains_stats(self):
        """Report contains key statistics."""
        dag = build_two_path_dag(weight_a=28, weight_b=32)
        budget = BudgetConfig(total_weight=30, tolerance=5)

        report = report_balance(dag, budget)

        assert "Total paths: 2" in report
        assert "Weight range: 28 - 32" in report
        assert "Target budget: 30" in report

    def test_report_shows_balanced(self):
        """Report indicates when balanced."""
        dag = build_two_path_dag(weight_a=30, weight_b=30)
        budget = BudgetConfig(total_weight=30, tolerance=5)

        report = report_balance(dag, budget)

        assert "within budget" in report.lower() or "✓" in report

    def test_report_shows_imbalanced(self):
        """Report indicates when imbalanced."""
        dag = build_two_path_dag(weight_a=10, weight_b=50)
        budget = BudgetConfig(total_weight=30, tolerance=5)

        report = report_balance(dag, budget)

        assert "underweight" in report.lower() or "✗" in report
        assert "overweight" in report.lower() or "✗" in report
```

**Step 2: Run tests to verify they fail**

Run: `cd core && uv run pytest tests/test_balance.py -v`
Expected: ImportError (balance.py doesn't exist)

**Step 3: Create balance.py**

```python
# core/speedfog_core/balance.py
"""Path balancing analysis for SpeedFog DAGs."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from speedfog_core.config import BudgetConfig
from speedfog_core.dag import Dag


@dataclass
class PathStats:
    """Statistics about paths in a DAG."""

    paths: list[list[str]]  # List of paths (node ID sequences)
    weights: list[int]  # Weight of each path
    min_weight: int
    max_weight: int
    avg_weight: float

    @classmethod
    def from_dag(cls, dag: Dag) -> PathStats:
        """Compute path statistics from a DAG."""
        paths = dag.enumerate_paths()
        weights = [dag.path_weight(p) for p in paths]
        return cls(
            paths=paths,
            weights=weights,
            min_weight=min(weights) if weights else 0,
            max_weight=max(weights) if weights else 0,
            avg_weight=sum(weights) / len(weights) if weights else 0,
        )


def analyze_balance(dag: Dag, budget: BudgetConfig) -> dict[str, Any]:
    """Analyze path balance in a DAG.

    Returns dict with:
        - is_balanced: bool
        - stats: PathStats
        - underweight_paths: list of (path, weight) tuples below budget.min_weight
        - overweight_paths: list of (path, weight) tuples above budget.max_weight
        - weight_spread: difference between max and min weights
    """
    stats = PathStats.from_dag(dag)

    underweight: list[tuple[list[str], int]] = []
    overweight: list[tuple[list[str], int]] = []

    for path, weight in zip(stats.paths, stats.weights):
        if weight < budget.min_weight:
            underweight.append((path, weight))
        elif weight > budget.max_weight:
            overweight.append((path, weight))

    return {
        "is_balanced": len(underweight) == 0 and len(overweight) == 0,
        "stats": stats,
        "underweight_paths": underweight,
        "overweight_paths": overweight,
        "weight_spread": stats.max_weight - stats.min_weight,
    }


def report_balance(dag: Dag, budget: BudgetConfig) -> str:
    """Generate a human-readable balance report."""
    analysis = analyze_balance(dag, budget)
    stats = analysis["stats"]

    lines = [
        "=== Path Balance Report ===",
        f"Total paths: {len(stats.paths)}",
        f"Weight range: {stats.min_weight} - {stats.max_weight} (spread: {analysis['weight_spread']})",
        f"Average weight: {stats.avg_weight:.1f}",
        f"Target budget: {budget.total_weight} (+/- {budget.tolerance})",
        f"Acceptable range: {budget.min_weight} - {budget.max_weight}",
        "",
    ]

    if analysis["is_balanced"]:
        lines.append("✓ All paths are within budget!")
    else:
        if analysis["underweight_paths"]:
            lines.append(f"✗ {len(analysis['underweight_paths'])} underweight paths")
        if analysis["overweight_paths"]:
            lines.append(f"✗ {len(analysis['overweight_paths'])} overweight paths")

    lines.append("")
    lines.append("Path details:")
    for i, (path, weight) in enumerate(zip(stats.paths, stats.weights)):
        status = "✓" if budget.min_weight <= weight <= budget.max_weight else "✗"
        # Show cluster IDs for each node (truncated)
        cluster_ids = [dag.nodes[nid].cluster.id[:20] for nid in path]
        path_str = " → ".join(cluster_ids)
        lines.append(f"  {status} Path {i + 1}: weight={weight}, {path_str}")

    return "\n".join(lines)
```

**Step 4: Run tests to verify they pass**

Run: `cd core && uv run pytest tests/test_balance.py -v`
Expected: All tests pass

**Step 5: Commit**

```bash
git add core/speedfog_core/balance.py core/tests/test_balance.py
git commit -m "$(cat <<'EOF'
feat(balance): add path balancing analysis module

- PathStats: compute path statistics from DAG
- analyze_balance: identify under/overweight paths
- report_balance: generate human-readable report
EOF
)"
```

---

## Task 4: Create Validator (validator.py)

**Files:**
- Create: `core/speedfog_core/validator.py`
- Test: `core/tests/test_validator.py`

**Step 1: Write the failing tests**

```python
# core/tests/test_validator.py
"""Tests for DAG constraint validation."""

import pytest

from speedfog_core.clusters import ClusterData
from speedfog_core.config import BudgetConfig, Config, RequirementsConfig, StructureConfig
from speedfog_core.dag import Dag, DagNode
from speedfog_core.validator import ValidationResult, validate_dag


def make_cluster(
    cluster_id: str,
    zones: list[str],
    weight: int,
    cluster_type: str = "mini_dungeon",
) -> ClusterData:
    """Helper to create test clusters."""
    return ClusterData(
        id=cluster_id,
        zones=zones,
        type=cluster_type,
        weight=weight,
        entry_fogs=[{"fog_id": "entry1", "zone": zones[0]}],
        exit_fogs=[{"fog_id": "exit1", "zone": zones[0]}],
    )


def make_config(
    legacy_dungeons: int = 0,
    bosses: int = 0,
    mini_dungeons: int = 0,
    total_weight: int = 30,
    tolerance: int = 10,
    min_layers: int = 2,
) -> Config:
    """Helper to create test config."""
    return Config(
        seed=42,
        budget=BudgetConfig(total_weight=total_weight, tolerance=tolerance),
        requirements=RequirementsConfig(
            legacy_dungeons=legacy_dungeons,
            bosses=bosses,
            mini_dungeons=mini_dungeons,
        ),
        structure=StructureConfig(min_layers=min_layers),
    )


def build_simple_dag(
    clusters: list[tuple[str, str, int]],  # (id, type, weight)
) -> Dag:
    """Build linear DAG: start -> c1 -> c2 -> ... -> end."""
    dag = Dag(seed=42)

    # Start node
    c_start = make_cluster("start_c", ["start_z"], weight=0, cluster_type="start")
    dag.add_node(DagNode(id="start", cluster=c_start, layer=0, tier=1, entry_fog=None))
    dag.start_id = "start"

    prev_id = "start"
    for i, (cid, ctype, weight) in enumerate(clusters):
        cluster = make_cluster(cid, [f"z{i}"], weight=weight, cluster_type=ctype)
        node_id = f"n{i+1}"
        dag.add_node(DagNode(id=node_id, cluster=cluster, layer=i+1, tier=5, entry_fog="e"))
        dag.add_edge(prev_id, node_id, f"fog{i}")
        prev_id = node_id

    # End node
    c_end = make_cluster("end_c", ["end_z"], weight=0, cluster_type="final_boss")
    end_layer = len(clusters) + 1
    dag.add_node(DagNode(id="end", cluster=c_end, layer=end_layer, tier=28, entry_fog="e"))
    dag.end_id = "end"
    dag.add_edge(prev_id, "end", "fog_end")

    return dag


class TestValidationResult:
    """Tests for ValidationResult dataclass."""

    def test_valid_result(self):
        """Valid result has is_valid=True and no errors."""
        result = ValidationResult(is_valid=True, errors=[], warnings=[])
        assert result.is_valid
        assert result.errors == []

    def test_invalid_result(self):
        """Invalid result has is_valid=False and errors."""
        result = ValidationResult(is_valid=False, errors=["oops"], warnings=[])
        assert not result.is_valid
        assert "oops" in result.errors


class TestValidateDagStructure:
    """Tests for structural validation."""

    def test_valid_structure(self):
        """Valid DAG passes structural validation."""
        dag = build_simple_dag([("c1", "mini_dungeon", 10)])
        config = make_config()

        result = validate_dag(dag, config)

        # No structural errors
        structural_errors = [e for e in result.errors if "structure" in e.lower() or "dead" in e.lower() or "unreachable" in e.lower()]
        assert structural_errors == []


class TestValidateDagRequirements:
    """Tests for requirement validation."""

    def test_insufficient_legacy_dungeons(self):
        """Missing legacy dungeons produces error."""
        dag = build_simple_dag([
            ("c1", "mini_dungeon", 10),
            ("c2", "mini_dungeon", 10),
        ])
        config = make_config(legacy_dungeons=2)

        result = validate_dag(dag, config)

        assert not result.is_valid
        assert any("legacy" in e.lower() for e in result.errors)

    def test_sufficient_legacy_dungeons(self):
        """Meeting legacy dungeon requirement passes."""
        dag = build_simple_dag([
            ("c1", "legacy_dungeon", 10),
            ("c2", "legacy_dungeon", 10),
        ])
        config = make_config(legacy_dungeons=2)

        result = validate_dag(dag, config)

        assert not any("legacy" in e.lower() for e in result.errors)

    def test_insufficient_bosses(self):
        """Missing boss arenas produces error."""
        dag = build_simple_dag([("c1", "mini_dungeon", 10)])
        config = make_config(bosses=2)

        result = validate_dag(dag, config)

        assert not result.is_valid
        assert any("boss" in e.lower() for e in result.errors)

    def test_insufficient_mini_dungeons(self):
        """Missing mini dungeons produces error."""
        dag = build_simple_dag([("c1", "legacy_dungeon", 10)])
        config = make_config(mini_dungeons=2)

        result = validate_dag(dag, config)

        assert not result.is_valid
        assert any("mini" in e.lower() for e in result.errors)


class TestValidateDagPaths:
    """Tests for path validation."""

    def test_no_paths_error(self):
        """DAG with no paths produces error."""
        dag = Dag(seed=42)
        c = make_cluster("c1", ["z1"], weight=10)
        dag.add_node(DagNode(id="start", cluster=c, layer=0, tier=1, entry_fog=None))
        dag.add_node(DagNode(id="end", cluster=c, layer=1, tier=28, entry_fog="e"))
        dag.start_id = "start"
        dag.end_id = "end"
        # No edge connecting them!

        config = make_config()
        result = validate_dag(dag, config)

        assert not result.is_valid
        assert any("path" in e.lower() for e in result.errors)

    def test_single_path_warning(self):
        """DAG with only one path produces warning."""
        dag = build_simple_dag([("c1", "mini_dungeon", 10)])
        config = make_config()

        result = validate_dag(dag, config)

        assert any("one path" in w.lower() or "single" in w.lower() for w in result.warnings)


class TestValidateDagWeight:
    """Tests for weight validation."""

    def test_underweight_path_warning(self):
        """Path below budget produces warning."""
        dag = build_simple_dag([("c1", "mini_dungeon", 10)])
        config = make_config(total_weight=50, tolerance=5)  # min=45

        result = validate_dag(dag, config)

        assert any("underweight" in w.lower() for w in result.warnings)

    def test_overweight_path_warning(self):
        """Path above budget produces warning."""
        dag = build_simple_dag([("c1", "mini_dungeon", 100)])
        config = make_config(total_weight=30, tolerance=5)  # max=35

        result = validate_dag(dag, config)

        assert any("overweight" in w.lower() for w in result.warnings)

    def test_balanced_path_no_warning(self):
        """Path within budget produces no weight warnings."""
        dag = build_simple_dag([("c1", "mini_dungeon", 30)])
        config = make_config(total_weight=30, tolerance=10)  # 20-40

        result = validate_dag(dag, config)

        weight_warnings = [w for w in result.warnings if "weight" in w.lower()]
        assert weight_warnings == []


class TestValidateDagLayers:
    """Tests for layer count validation."""

    def test_few_layers_warning(self):
        """DAG with fewer layers than min produces warning."""
        dag = build_simple_dag([("c1", "mini_dungeon", 10)])  # 3 layers total
        config = make_config(min_layers=6)

        result = validate_dag(dag, config)

        assert any("layer" in w.lower() for w in result.warnings)
```

**Step 2: Run tests to verify they fail**

Run: `cd core && uv run pytest tests/test_validator.py -v`
Expected: ImportError (validator.py doesn't exist)

**Step 3: Create validator.py**

```python
# core/speedfog_core/validator.py
"""Validation of SpeedFog DAG constraints."""

from __future__ import annotations

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
    """Validate a DAG against all constraints.

    Checks:
    - Structural validity (no dead ends, all reachable)
    - Minimum requirements (bosses, legacy dungeons, etc.)
    - Path count limits
    - Weight balance

    Args:
        dag: The DAG to validate
        config: Configuration with requirements and budget

    Returns:
        ValidationResult with errors and warnings
    """
    errors: list[str] = []
    warnings: list[str] = []

    # Structural validation
    structural_errors = dag.validate_structure()
    errors.extend(structural_errors)

    # Requirement validation
    req = config.requirements

    legacy_count = dag.count_by_type("legacy_dungeon")
    if legacy_count < req.legacy_dungeons:
        errors.append(
            f"Insufficient legacy dungeons: {legacy_count} < {req.legacy_dungeons}"
        )

    mini_count = dag.count_by_type("mini_dungeon")
    if mini_count < req.mini_dungeons:
        errors.append(
            f"Insufficient mini-dungeons: {mini_count} < {req.mini_dungeons}"
        )

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
            warnings.append(f"Path {i + 1} underweight: {weight} < {budget.min_weight}")
        elif weight > budget.max_weight:
            warnings.append(f"Path {i + 1} overweight: {weight} > {budget.max_weight}")

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

**Step 4: Run tests to verify they pass**

Run: `cd core && uv run pytest tests/test_validator.py -v`
Expected: All tests pass

**Step 5: Commit**

```bash
git add core/speedfog_core/validator.py core/tests/test_validator.py
git commit -m "$(cat <<'EOF'
feat(validator): add DAG constraint validation module

- ValidationResult: errors vs warnings distinction
- validate_dag: check structure, requirements, paths, weights
- Errors block generation, warnings are informational
EOF
)"
```

---

## Task 5: Create Output Module (output.py)

**Files:**
- Create: `core/speedfog_core/output.py`
- Test: `core/tests/test_output.py`

**Step 1: Write the failing tests**

```python
# core/tests/test_output.py
"""Tests for output generation."""

import json

import pytest

from speedfog_core.clusters import ClusterData
from speedfog_core.dag import Dag, DagNode
from speedfog_core.output import dag_to_dict, export_json, export_spoiler_log


def make_cluster(cluster_id: str, zones: list[str], weight: int) -> ClusterData:
    """Helper to create test clusters."""
    return ClusterData(
        id=cluster_id,
        zones=zones,
        type="mini_dungeon",
        weight=weight,
        entry_fogs=[{"fog_id": "entry1", "zone": zones[0]}],
        exit_fogs=[{"fog_id": "exit1", "zone": zones[0]}],
    )


def build_test_dag() -> Dag:
    """Build test DAG: start -> n1a, n1b -> end."""
    dag = Dag(seed=12345)

    c_start = make_cluster("chapel_start", ["chapel"], weight=0)
    c1a = make_cluster("catacomb_a", ["zone_a"], weight=10)
    c1b = make_cluster("catacomb_b", ["zone_b"], weight=15)
    c_end = make_cluster("elden_throne", ["throne"], weight=0)

    dag.add_node(DagNode(
        id="start", cluster=c_start, layer=0, tier=1,
        entry_fog=None, exit_fogs=["exit_chapel"]
    ))
    dag.add_node(DagNode(
        id="n1a", cluster=c1a, layer=1, tier=10,
        entry_fog="entry_a", exit_fogs=["exit_a"]
    ))
    dag.add_node(DagNode(
        id="n1b", cluster=c1b, layer=1, tier=10,
        entry_fog="entry_b", exit_fogs=["exit_b"]
    ))
    dag.add_node(DagNode(
        id="end", cluster=c_end, layer=2, tier=28,
        entry_fog="entry_throne", exit_fogs=[]
    ))
    dag.start_id = "start"
    dag.end_id = "end"

    dag.add_edge("start", "n1a", "exit_chapel")
    dag.add_edge("start", "n1b", "exit_chapel")
    dag.add_edge("n1a", "end", "exit_a")
    dag.add_edge("n1b", "end", "exit_b")

    return dag


class TestDagToDict:
    """Tests for dag_to_dict function."""

    def test_contains_seed(self):
        """Output contains seed value."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        assert data["seed"] == 12345

    def test_contains_nodes(self):
        """Output contains all nodes."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        assert "nodes" in data
        assert "start" in data["nodes"]
        assert "n1a" in data["nodes"]
        assert "end" in data["nodes"]

    def test_node_has_cluster_info(self):
        """Node data includes cluster information."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        node = data["nodes"]["n1a"]
        assert node["cluster_id"] == "catacomb_a"
        assert node["zones"] == ["zone_a"]
        assert node["weight"] == 10
        assert node["tier"] == 10

    def test_contains_edges(self):
        """Output contains all edges."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        assert "edges" in data
        assert len(data["edges"]) == 4

    def test_edge_structure(self):
        """Edge data has correct structure."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        edge = data["edges"][0]
        assert "source" in edge
        assert "target" in edge
        assert "fog_id" in edge

    def test_contains_path_stats(self):
        """Output contains path statistics."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        assert data["total_paths"] == 2
        assert data["path_weights"] == [10, 15]

    def test_contains_totals(self):
        """Output contains totals."""
        dag = build_test_dag()
        data = dag_to_dict(dag)
        assert data["total_nodes"] == 4
        assert data["total_layers"] == 3


class TestExportJson:
    """Tests for export_json function."""

    def test_creates_valid_json(self, tmp_path):
        """export_json creates valid JSON file."""
        dag = build_test_dag()
        output_path = tmp_path / "graph.json"

        export_json(dag, output_path)

        assert output_path.exists()
        with open(output_path) as f:
            data = json.load(f)
        assert data["seed"] == 12345

    def test_json_is_formatted(self, tmp_path):
        """export_json produces formatted (indented) JSON."""
        dag = build_test_dag()
        output_path = tmp_path / "graph.json"

        export_json(dag, output_path)

        content = output_path.read_text()
        assert "\n" in content  # Has newlines
        assert "  " in content  # Has indentation


class TestExportSpoilerLog:
    """Tests for export_spoiler_log function."""

    def test_creates_file(self, tmp_path):
        """export_spoiler_log creates output file."""
        dag = build_test_dag()
        output_path = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_path)

        assert output_path.exists()

    def test_contains_seed(self, tmp_path):
        """Spoiler log contains seed."""
        dag = build_test_dag()
        output_path = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_path)

        content = output_path.read_text()
        assert "12345" in content

    def test_contains_layers(self, tmp_path):
        """Spoiler log shows layers."""
        dag = build_test_dag()
        output_path = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_path)

        content = output_path.read_text()
        assert "Layer 0" in content
        assert "Layer 1" in content
        assert "Layer 2" in content

    def test_contains_paths(self, tmp_path):
        """Spoiler log shows paths."""
        dag = build_test_dag()
        output_path = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_path)

        content = output_path.read_text()
        assert "Path 1" in content
        assert "Path 2" in content
```

**Step 2: Run tests to verify they fail**

Run: `cd core && uv run pytest tests/test_output.py -v`
Expected: ImportError (output.py doesn't exist)

**Step 3: Create output.py**

```python
# core/speedfog_core/output.py
"""Export SpeedFog DAG to JSON and spoiler log formats."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from speedfog_core.dag import Dag


def dag_to_dict(dag: Dag) -> dict[str, Any]:
    """Convert DAG to JSON-serializable dict."""
    nodes_dict: dict[str, Any] = {}
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
    with open(output_path, "w", encoding="utf-8") as f:
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
                exits_preview = ", ".join(node.exit_fogs[:3])
                suffix = "..." if len(node.exit_fogs) > 3 else ""
                lines.append(f"    exits: {exits_preview}{suffix}")
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

    with open(output_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
```

**Step 4: Run tests to verify they pass**

Run: `cd core && uv run pytest tests/test_output.py -v`
Expected: All tests pass

**Step 5: Commit**

```bash
git add core/speedfog_core/output.py core/tests/test_output.py
git commit -m "$(cat <<'EOF'
feat(output): add JSON and spoiler log export module

- dag_to_dict: convert DAG to JSON-serializable dictionary
- export_json: write formatted JSON to file
- export_spoiler_log: write human-readable text summary
EOF
)"
```

---

## Task 6: Refactor Generator to Use New Modules

**Files:**
- Modify: `core/speedfog_core/generator.py`
- Modify: `core/speedfog_core/main.py`
- Test: `core/tests/test_generator.py`

**Step 1: Write the failing tests for generator**

```python
# core/tests/test_generator.py
"""Tests for DAG generation algorithm."""

import pytest

from speedfog_core.clusters import ClusterData, ClusterPool
from speedfog_core.config import (
    BudgetConfig,
    Config,
    RequirementsConfig,
    StructureConfig,
)
from speedfog_core.generator import (
    GenerationError,
    cluster_has_usable_exits,
    generate_dag,
    generate_with_retry,
    pick_cluster,
    pick_entry_fog_with_exits,
)


def make_cluster(
    cluster_id: str,
    zones: list[str],
    weight: int = 10,
    cluster_type: str = "mini_dungeon",
    entry_fogs: list[dict] | None = None,
    exit_fogs: list[dict] | None = None,
) -> ClusterData:
    """Helper to create test clusters."""
    if entry_fogs is None:
        entry_fogs = [{"fog_id": "entry1", "zone": zones[0]}]
    if exit_fogs is None:
        exit_fogs = [
            {"fog_id": "entry1", "zone": zones[0]},  # bidirectional
            {"fog_id": "exit1", "zone": zones[0]},
        ]
    return ClusterData(
        id=cluster_id,
        zones=zones,
        type=cluster_type,
        weight=weight,
        entry_fogs=entry_fogs,
        exit_fogs=exit_fogs,
    )


def make_test_pool() -> ClusterPool:
    """Create a test cluster pool with all required types."""
    pool = ClusterPool()

    # Start cluster
    pool.add(make_cluster(
        "chapel_start", ["chapel"], weight=0, cluster_type="start",
        entry_fogs=[],
        exit_fogs=[{"fog_id": "chapel_exit", "zone": "chapel"}],
    ))

    # Final boss cluster
    pool.add(make_cluster(
        "elden_throne", ["throne"], weight=0, cluster_type="final_boss",
        entry_fogs=[{"fog_id": "throne_entry", "zone": "throne"}],
        exit_fogs=[],
    ))

    # Legacy dungeons (3)
    for i in range(3):
        pool.add(make_cluster(
            f"legacy_{i}", [f"legacy_zone_{i}"], weight=15,
            cluster_type="legacy_dungeon",
        ))

    # Mini dungeons (10)
    for i in range(10):
        pool.add(make_cluster(
            f"mini_{i}", [f"mini_zone_{i}"], weight=5,
            cluster_type="mini_dungeon",
        ))

    # Boss arenas (5)
    for i in range(5):
        pool.add(make_cluster(
            f"boss_{i}", [f"boss_zone_{i}"], weight=3,
            cluster_type="boss_arena",
        ))

    return pool


def make_config(
    seed: int = 0,
    legacy_dungeons: int = 1,
    bosses: int = 1,
    mini_dungeons: int = 2,
    min_layers: int = 3,
    max_layers: int = 5,
    max_parallel_paths: int = 2,
) -> Config:
    """Create test configuration."""
    return Config(
        seed=seed,
        budget=BudgetConfig(total_weight=50, tolerance=30),
        requirements=RequirementsConfig(
            legacy_dungeons=legacy_dungeons,
            bosses=bosses,
            mini_dungeons=mini_dungeons,
        ),
        structure=StructureConfig(
            min_layers=min_layers,
            max_layers=max_layers,
            max_parallel_paths=max_parallel_paths,
        ),
    )


class TestClusterHasUsableExits:
    """Tests for cluster_has_usable_exits helper."""

    def test_cluster_with_exits(self):
        """Cluster with exits after entry is usable."""
        cluster = make_cluster("c1", ["z1"])
        assert cluster_has_usable_exits(cluster) is True

    def test_cluster_without_exits(self):
        """Cluster with no exits is not usable."""
        cluster = make_cluster(
            "c1", ["z1"],
            entry_fogs=[{"fog_id": "only_fog", "zone": "z1"}],
            exit_fogs=[{"fog_id": "only_fog", "zone": "z1"}],  # Same as entry
        )
        assert cluster_has_usable_exits(cluster) is False

    def test_cluster_no_entry_fogs(self):
        """Cluster with no entry fogs is not usable."""
        cluster = make_cluster(
            "c1", ["z1"],
            entry_fogs=[],
            exit_fogs=[{"fog_id": "exit1", "zone": "z1"}],
        )
        assert cluster_has_usable_exits(cluster) is False


class TestPickEntryFogWithExits:
    """Tests for pick_entry_fog_with_exits helper."""

    def test_picks_valid_entry(self):
        """Picks an entry that leaves exits available."""
        import random
        cluster = make_cluster("c1", ["z1"])
        rng = random.Random(42)

        entry = pick_entry_fog_with_exits(cluster, rng)

        assert entry is not None
        # After using this entry, there should be exits left
        remaining = [e for e in cluster.exit_fogs if e["fog_id"] != entry]
        assert len(remaining) > 0

    def test_returns_none_if_no_valid_entry(self):
        """Returns None if no entry leaves exits."""
        import random
        cluster = make_cluster(
            "c1", ["z1"],
            entry_fogs=[{"fog_id": "only_fog", "zone": "z1"}],
            exit_fogs=[{"fog_id": "only_fog", "zone": "z1"}],
        )
        rng = random.Random(42)

        entry = pick_entry_fog_with_exits(cluster, rng)

        assert entry is None


class TestPickCluster:
    """Tests for pick_cluster helper."""

    def test_picks_from_candidates(self):
        """Picks a cluster from candidates."""
        import random
        candidates = [make_cluster(f"c{i}", [f"z{i}"]) for i in range(3)]
        rng = random.Random(42)

        cluster = pick_cluster(candidates, used_zones=set(), rng=rng)

        assert cluster is not None
        assert cluster in candidates

    def test_excludes_used_zones(self):
        """Does not pick clusters with used zones."""
        import random
        c1 = make_cluster("c1", ["z1"])
        c2 = make_cluster("c2", ["z2"])
        candidates = [c1, c2]
        rng = random.Random(42)

        cluster = pick_cluster(candidates, used_zones={"z1"}, rng=rng)

        assert cluster == c2

    def test_returns_none_if_all_used(self):
        """Returns None if all candidates have used zones."""
        import random
        c1 = make_cluster("c1", ["z1"])
        rng = random.Random(42)

        cluster = pick_cluster([c1], used_zones={"z1"}, rng=rng)

        assert cluster is None


class TestGenerateDag:
    """Tests for generate_dag function."""

    def test_generates_dag_with_fixed_seed(self):
        """generate_dag produces DAG with specified seed."""
        pool = make_test_pool()
        config = make_config(seed=42)

        dag = generate_dag(config, pool, seed=42)

        assert dag.seed == 42
        assert dag.start_id == "start"
        assert dag.end_id == "end"

    def test_dag_has_start_and_end(self):
        """Generated DAG has start and end nodes."""
        pool = make_test_pool()
        config = make_config(seed=42)

        dag = generate_dag(config, pool, seed=42)

        assert dag.start_id in dag.nodes
        assert dag.end_id in dag.nodes
        start = dag.nodes[dag.start_id]
        end = dag.nodes[dag.end_id]
        assert start.cluster.type == "start"
        assert end.cluster.type == "final_boss"

    def test_dag_has_paths(self):
        """Generated DAG has at least one path."""
        pool = make_test_pool()
        config = make_config(seed=42)

        dag = generate_dag(config, pool, seed=42)

        paths = dag.enumerate_paths()
        assert len(paths) >= 1

    def test_dag_respects_max_parallel_paths(self):
        """DAG doesn't exceed max_parallel_paths."""
        pool = make_test_pool()
        config = make_config(seed=42, max_parallel_paths=2)

        dag = generate_dag(config, pool, seed=42)

        paths = dag.enumerate_paths()
        assert len(paths) <= config.structure.max_parallel_paths

    def test_no_zone_overlap(self):
        """No two nodes share the same zone."""
        pool = make_test_pool()
        config = make_config(seed=42)

        dag = generate_dag(config, pool, seed=42)

        all_zones: list[str] = []
        for node in dag.nodes.values():
            all_zones.extend(node.cluster.zones)
        assert len(all_zones) == len(set(all_zones))

    def test_raises_if_no_start(self):
        """Raises GenerationError if no start cluster."""
        pool = ClusterPool()
        pool.add(make_cluster("c1", ["z1"]))
        config = make_config()

        with pytest.raises(GenerationError, match="start"):
            generate_dag(config, pool, seed=42)

    def test_raises_if_no_final_boss(self):
        """Raises GenerationError if no final_boss cluster."""
        pool = ClusterPool()
        pool.add(make_cluster(
            "chapel_start", ["chapel"], cluster_type="start",
            entry_fogs=[],
            exit_fogs=[{"fog_id": "exit", "zone": "chapel"}],
        ))
        config = make_config()

        with pytest.raises(GenerationError, match="final_boss"):
            generate_dag(config, pool, seed=42)


class TestGenerateWithRetry:
    """Tests for generate_with_retry function."""

    def test_fixed_seed_single_attempt(self):
        """Non-zero seed uses single attempt."""
        pool = make_test_pool()
        config = make_config(seed=42)

        dag, actual_seed = generate_with_retry(config, pool)

        assert actual_seed == 42
        assert dag.seed == 42

    def test_auto_reroll_finds_valid_seed(self):
        """seed=0 tries seeds until success."""
        pool = make_test_pool()
        config = make_config(seed=0)

        dag, actual_seed = generate_with_retry(config, pool, max_attempts=100)

        assert actual_seed != 0
        assert dag.seed == actual_seed

    def test_raises_after_max_attempts(self):
        """Raises after max_attempts failures."""
        pool = ClusterPool()  # Empty pool = always fails
        config = make_config(seed=0)

        with pytest.raises(GenerationError, match="attempts"):
            generate_with_retry(config, pool, max_attempts=3)
```

**Step 2: Run tests to verify they fail**

Run: `cd core && uv run pytest tests/test_generator.py -v`
Expected: Some tests may pass with current prototype, others will fail

**Step 3: Refactor generator.py to use new modules**

```python
# core/speedfog_core/generator.py
"""DAG generation algorithm for SpeedFog.

Generates a randomized DAG with:
- Start: chapel_start cluster
- Dynamic parallel branches based on exit fogs
- End: final_boss cluster (leyndell_erdtree)
"""

from __future__ import annotations

import random
from dataclasses import dataclass

from speedfog_core.clusters import ClusterData, ClusterPool
from speedfog_core.config import Config
from speedfog_core.dag import Dag, DagNode
from speedfog_core.planner import compute_tier, plan_layer_types


class GenerationError(Exception):
    """Error during DAG generation."""

    pass


@dataclass
class Branch:
    """Tracks a branch during generation."""

    node: DagNode
    exit_fog: str  # The fog_id to use when connecting to next layer


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
    """Generate a randomized DAG with dynamic parallel branches.

    Algorithm:
    1. Pick start cluster (type: start)
    2. Plan layer types to satisfy requirements
    3. For each layer:
       - For each active branch, pick a cluster
       - Create branches based on available exits
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

    # 3. Build intermediate layers with dynamic branches
    # Start with branches from start node's exits
    current_branches: list[Branch] = []
    max_branches = min(len(start_node.exit_fogs), config.structure.max_parallel_paths)
    for exit_fog in start_node.exit_fogs[:max_branches]:
        current_branches.append(Branch(node=start_node, exit_fog=exit_fog))

    # Ensure at least one branch
    if not current_branches and start_node.exit_fogs:
        current_branches.append(Branch(node=start_node, exit_fog=start_node.exit_fogs[0]))

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

            # Compute available exits after using entry
            available_exits = cluster.available_exits(entry_fog)

            # Create node
            node_id = f"node_{layer_idx}{chr(ord('a') + branch_idx)}"
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

            # Create branches for next layer (respect max_parallel_paths)
            for exit_fog in node.exit_fogs:
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

    # Connect all branches to end
    for branch in current_branches:
        dag.add_edge(branch.node.id, end_node.id, branch.exit_fog)

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
        except GenerationError:
            continue

    raise GenerationError(f"Failed to generate DAG after {max_attempts} attempts")
```

**Step 4: Run tests to verify they pass**

Run: `cd core && uv run pytest tests/test_generator.py -v`
Expected: All tests pass

**Step 5: Update main.py to use new modules**

```python
# core/speedfog_core/main.py
"""SpeedFog CLI entry point."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from speedfog_core.balance import report_balance
from speedfog_core.clusters import load_clusters
from speedfog_core.config import Config, load_config
from speedfog_core.generator import GenerationError, generate_with_retry
from speedfog_core.output import export_json, export_spoiler_log
from speedfog_core.validator import validate_dag


def main() -> int:
    """Main entry point for the speedfog command."""
    parser = argparse.ArgumentParser(
        description="SpeedFog - Generate randomized Elden Ring run DAGs",
    )
    parser.add_argument(
        "config",
        type=Path,
        nargs="?",
        default=None,
        help="Path to config.toml (optional, uses defaults if not provided)",
    )
    parser.add_argument(
        "-o",
        "--output",
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
        "--clusters",
        type=Path,
        help="Path to clusters.json (overrides config)",
    )
    parser.add_argument(
        "--seed",
        type=int,
        help="Random seed (overrides config, 0 = auto-reroll)",
    )
    parser.add_argument(
        "--max-attempts",
        type=int,
        default=100,
        help="Max generation attempts for auto-reroll (default: 100)",
    )
    parser.add_argument(
        "-v",
        "--verbose",
        action="store_true",
        help="Verbose output",
    )

    args = parser.parse_args()

    # Load or create config
    if args.config:
        try:
            config = load_config(args.config)
            if args.verbose:
                print(f"Loaded config from {args.config}")
        except FileNotFoundError:
            print(f"Error: Config file not found: {args.config}", file=sys.stderr)
            return 1
    else:
        config = Config()
        if args.verbose:
            print("Using default configuration")

    # Override seed if provided
    if args.seed is not None:
        config.seed = args.seed

    # Determine clusters file path
    if args.clusters:
        clusters_path = args.clusters
    else:
        # Resolve relative to config file or current directory
        if args.config:
            base_dir = args.config.parent
        else:
            base_dir = Path.cwd()

        clusters_path = base_dir / config.paths.clusters_file

        # Also check in core/data relative to script location
        if not clusters_path.exists():
            script_dir = Path(__file__).parent.parent
            alt_path = script_dir / "data" / "clusters.json"
            if alt_path.exists():
                clusters_path = alt_path

    # Load clusters
    try:
        clusters = load_clusters(clusters_path)
        if args.verbose:
            print(f"Loaded {len(clusters.clusters)} clusters from {clusters_path}")
            for ctype, clist in clusters.by_type.items():
                print(f"  {ctype}: {len(clist)}")
    except FileNotFoundError:
        print(f"Error: Clusters file not found: {clusters_path}", file=sys.stderr)
        return 1

    # Generate DAG
    if args.verbose:
        mode = "fixed seed" if config.seed != 0 else "auto-reroll"
        print(f"Generating DAG ({mode})...")

    try:
        dag, actual_seed = generate_with_retry(
            config, clusters, max_attempts=args.max_attempts
        )
    except GenerationError as e:
        print(f"Error: Generation failed: {e}", file=sys.stderr)
        return 1

    if args.verbose or config.seed == 0:
        print(f"Generated DAG with seed {actual_seed}")
        paths = dag.enumerate_paths()
        max_layer = max((n.layer for n in dag.nodes.values()), default=0)
        print(f"  Layers: {max_layer + 1}")
        print(f"  Nodes: {len(dag.nodes)}")
        print(f"  Paths: {len(paths)}")
        if paths:
            weights = [dag.path_weight(p) for p in paths]
            print(f"  Path weights: {weights}")

    # Validate
    validation = validate_dag(dag, config)

    if validation.warnings:
        for warning in validation.warnings:
            print(f"Warning: {warning}", file=sys.stderr)

    if not validation.is_valid:
        for error in validation.errors:
            print(f"Error: {error}", file=sys.stderr)
        return 1

    # Report balance in verbose mode
    if args.verbose:
        print()
        print(report_balance(dag, config.budget))

    # Export JSON
    export_json(dag, args.output)
    print(f"Written: {args.output}")

    # Export spoiler if requested
    if args.spoiler:
        export_spoiler_log(dag, args.spoiler)
        print(f"Written: {args.spoiler}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
```

**Step 6: Run all tests to verify everything works**

Run: `cd core && uv run pytest tests/ -v`
Expected: All tests pass

**Step 7: Commit**

```bash
git add core/speedfog_core/generator.py core/speedfog_core/main.py core/tests/test_generator.py
git commit -m "$(cat <<'EOF'
refactor(generator): use new modules and dynamic branching

- Import DAG structures from dag.py
- Import planner functions from planner.py
- Dynamic branch count based on exit fogs
- Respect max_parallel_paths config
- Update main.py to use validator and balance modules
- Add comprehensive generator tests
EOF
)"
```

---

## Task 7: Update __init__.py Exports

**Files:**
- Modify: `core/speedfog_core/__init__.py`

**Step 1: Update __init__.py**

```python
# core/speedfog_core/__init__.py
"""SpeedFog Core - DAG generation for Elden Ring randomized runs."""

from speedfog_core.balance import PathStats, analyze_balance, report_balance
from speedfog_core.clusters import ClusterData, ClusterPool, load_clusters
from speedfog_core.config import (
    BudgetConfig,
    Config,
    PathsConfig,
    RequirementsConfig,
    StructureConfig,
    load_config,
)
from speedfog_core.dag import Dag, DagEdge, DagNode
from speedfog_core.generator import GenerationError, generate_dag, generate_with_retry
from speedfog_core.output import dag_to_dict, export_json, export_spoiler_log
from speedfog_core.planner import compute_tier, plan_layer_types
from speedfog_core.validator import ValidationResult, validate_dag

__all__ = [
    # Config
    "BudgetConfig",
    "Config",
    "PathsConfig",
    "RequirementsConfig",
    "StructureConfig",
    "load_config",
    # Clusters
    "ClusterData",
    "ClusterPool",
    "load_clusters",
    # DAG
    "Dag",
    "DagEdge",
    "DagNode",
    # Planner
    "compute_tier",
    "plan_layer_types",
    # Generator
    "GenerationError",
    "generate_dag",
    "generate_with_retry",
    # Balance
    "PathStats",
    "analyze_balance",
    "report_balance",
    # Validator
    "ValidationResult",
    "validate_dag",
    # Output
    "dag_to_dict",
    "export_json",
    "export_spoiler_log",
]
```

**Step 2: Run all tests**

Run: `cd core && uv run pytest tests/ -v`
Expected: All tests pass

**Step 3: Commit**

```bash
git add core/speedfog_core/__init__.py
git commit -m "$(cat <<'EOF'
chore(core): update __init__.py exports

Export all public API from new modules:
- dag, planner, balance, validator, output
EOF
)"
```

---

## Task 8: Integration Test

**Files:**
- Create: `core/tests/test_integration.py`

**Step 1: Write integration test**

```python
# core/tests/test_integration.py
"""Integration tests for full DAG generation pipeline."""

import json

import pytest

from speedfog_core import (
    Config,
    ClusterPool,
    generate_with_retry,
    validate_dag,
    export_json,
    export_spoiler_log,
    load_clusters,
)


@pytest.fixture
def real_clusters(tmp_path):
    """Load the actual clusters.json if available."""
    from pathlib import Path

    # Try to find clusters.json
    possible_paths = [
        Path(__file__).parent.parent / "data" / "clusters.json",
        Path(__file__).parent.parent.parent / "data" / "clusters.json",
    ]

    for path in possible_paths:
        if path.exists():
            return load_clusters(path)

    pytest.skip("clusters.json not found")


class TestFullPipeline:
    """End-to-end tests for the generation pipeline."""

    def test_generate_validate_export(self, real_clusters, tmp_path):
        """Full pipeline: generate -> validate -> export."""
        config = Config(seed=42)

        # Generate
        dag, seed = generate_with_retry(config, real_clusters, max_attempts=50)
        assert seed == 42

        # Validate
        result = validate_dag(dag, config)
        assert result.is_valid, f"Validation failed: {result.errors}"

        # Export JSON
        json_path = tmp_path / "graph.json"
        export_json(dag, json_path)
        assert json_path.exists()

        # Verify JSON is valid
        with open(json_path) as f:
            data = json.load(f)
        assert data["seed"] == 42
        assert data["total_paths"] >= 1

        # Export spoiler
        spoiler_path = tmp_path / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path)
        assert spoiler_path.exists()
        content = spoiler_path.read_text()
        assert "SPEEDFOG" in content

    def test_auto_reroll_finds_valid_seed(self, real_clusters, tmp_path):
        """seed=0 finds a working seed automatically."""
        config = Config(seed=0)

        dag, seed = generate_with_retry(config, real_clusters, max_attempts=100)

        assert seed != 0
        result = validate_dag(dag, config)
        assert result.is_valid

    def test_multiple_seeds_produce_different_dags(self, real_clusters):
        """Different seeds produce different DAGs."""
        config1 = Config(seed=1)
        config2 = Config(seed=2)

        dag1, _ = generate_with_retry(config1, real_clusters)
        dag2, _ = generate_with_retry(config2, real_clusters)

        # Node IDs might be same, but cluster IDs should differ
        nodes1 = {n.cluster.id for n in dag1.nodes.values()}
        nodes2 = {n.cluster.id for n in dag2.nodes.values()}

        # At least some clusters should be different
        assert nodes1 != nodes2
```

**Step 2: Run integration tests**

Run: `cd core && uv run pytest tests/test_integration.py -v`
Expected: All tests pass (if clusters.json exists)

**Step 3: Commit**

```bash
git add core/tests/test_integration.py
git commit -m "$(cat <<'EOF'
test(integration): add end-to-end pipeline tests

- Test full generate->validate->export pipeline
- Test auto-reroll with seed=0
- Test different seeds produce different DAGs
EOF
)"
```

---

## Task 9: Final Cleanup and Run Full Test Suite

**Step 1: Run full test suite**

Run: `cd core && uv run pytest tests/ -v --tb=short`
Expected: All tests pass

**Step 2: Run the CLI manually to verify**

Run: `cd core && uv run python -m speedfog_core.main -v --spoiler spoiler.txt -o graph.json`
Expected: Generates graph.json and spoiler.txt without errors

**Step 3: Verify pre-commit hooks pass**

Run: `pre-commit run --all-files`
Expected: All checks pass

**Step 4: Create final commit if needed**

```bash
git status
# If any uncommitted changes:
git add -A
git commit -m "chore: final cleanup for Phase 2 implementation"
```

---

## Summary

After completing all tasks, you will have:

1. **dag.py** - Clean DAG data structures with validation
2. **planner.py** - Layer type planning with tier calculation
3. **balance.py** - Path weight analysis and reporting
4. **validator.py** - Comprehensive constraint validation
5. **output.py** - JSON and spoiler log export
6. **generator.py** - Refactored with dynamic branching
7. **main.py** - CLI using all new modules
8. **Tests** - Comprehensive coverage for all modules

The implementation follows TDD, has frequent commits, and maintains clean separation of concerns.
