# Fog Reuse Model (Phase 1: Shared Entrance) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow multiple DAG branches to merge through a single shared entrance fog gate, breaking the merge pool bottleneck where only 2 mini_dungeons and 6 boss_arenas qualify as merge nodes.

**Architecture:** Add `allow_shared_entrance` / `allow_entry_as_exit` fields to `ClusterData`, computed in `generate_clusters.py` from type/fog-count defaults with per-zone TOML overrides. Update `can_be_merge_node()` to use shared-entrance logic. Update `execute_merge_layer()` to select 1 entry (not N). Update `ConnectionInjector.cs` to group connections by entrance and use `Graph.DuplicateEntrance()` for secondary merge connections.

**Tech Stack:** Python 3.10+, pytest, C# .NET 8.0, xUnit

**Design spec:** `docs/specs/fog-reuse-model.md`

---

## Context

The DAG generator's merge pools are tiny (2 mini_dungeons, 6 boss_arenas for merge(2)), causing clusters like Sage's Cave (60.8%) and Black Knife Catacombs (59.4%) to appear in most runs. The fog reuse model breaks this by allowing multiple branches to connect to the same entrance fog gate via `Graph.DuplicateEntrance()`.

Phase 1 covers shared-entrance merges only. Phase 2 (entry-as-exit splits for boss arenas) will follow after in-game validation.

---

### Task 1: Add reuse fields to ClusterData

**Files:**
- Modify: `speedfog/clusters.py:10-38`
- Test: `tests/test_clusters.py` (create)

**Step 1: Write the failing test**

Create `tests/test_clusters.py`:

```python
"""Tests for ClusterData fog reuse fields."""

from speedfog.clusters import ClusterData


class TestClusterDataReuseFields:
    """Tests for allow_shared_entrance and allow_entry_as_exit fields."""

    def test_default_values_false(self):
        """Reuse fields default to False when not in source dict."""
        data = {
            "id": "test_1234",
            "zones": ["zone_a"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [{"fog_id": "fog_a", "zone": "zone_a"}],
            "exit_fogs": [{"fog_id": "fog_b", "zone": "zone_a"}],
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.allow_shared_entrance is False
        assert cluster.allow_entry_as_exit is False

    def test_fields_loaded_from_dict(self):
        """Reuse fields are loaded from source dict when present."""
        data = {
            "id": "test_1234",
            "zones": ["zone_a"],
            "type": "boss_arena",
            "weight": 3,
            "entry_fogs": [
                {"fog_id": "fog_a", "zone": "zone_a"},
                {"fog_id": "fog_b", "zone": "zone_a"},
            ],
            "exit_fogs": [
                {"fog_id": "fog_c", "zone": "zone_a"},
                {"fog_id": "fog_d", "zone": "zone_a"},
            ],
            "allow_shared_entrance": True,
            "allow_entry_as_exit": True,
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.allow_shared_entrance is True
        assert cluster.allow_entry_as_exit is True
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_clusters.py -v`
Expected: FAIL — `ClusterData` has no `allow_shared_entrance` field.

**Step 3: Implement the fields**

In `speedfog/clusters.py`, add two fields to `ClusterData` after `defeat_flag` (line 23):

```python
    defeat_flag: int = 0  # Boss defeat event flag (from fog.txt DefeatFlag)
    allow_shared_entrance: bool = False  # Multiple branches can share one entry fog
    allow_entry_as_exit: bool = False  # Entry fog's return direction used as forward exit
```

Update `from_dict` to load them:

```python
    @classmethod
    def from_dict(cls, data: dict) -> ClusterData:
        """Create ClusterData from a dictionary."""
        all_exits = data.get("exit_fogs", [])
        return cls(
            id=data["id"],
            zones=data["zones"],
            type=data["type"],
            weight=data["weight"],
            entry_fogs=data.get("entry_fogs", []),
            exit_fogs=[f for f in all_exits if not f.get("unique")],
            unique_exit_fogs=[f for f in all_exits if f.get("unique")],
            defeat_flag=data.get("defeat_flag", 0),
            allow_shared_entrance=data.get("allow_shared_entrance", False),
            allow_entry_as_exit=data.get("allow_entry_as_exit", False),
        )
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_clusters.py -v`
Expected: PASS

**Step 5: Update make_cluster test helper**

In `tests/test_generator.py`, update the `make_cluster` helper to accept and forward the new fields:

```python
def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | object = _SENTINEL,
    exit_fogs: list[dict] | object = _SENTINEL,
    allow_shared_entrance: bool = False,
    allow_entry_as_exit: bool = False,
) -> ClusterData:
    """Helper to create test ClusterData objects."""
    if entry_fogs is _SENTINEL:
        entry_fogs = [{"fog_id": f"{cluster_id}_entry", "zone": cluster_id}]
    if exit_fogs is _SENTINEL:
        exit_fogs = [{"fog_id": f"{cluster_id}_exit", "zone": cluster_id}]
    return ClusterData(
        id=cluster_id,
        zones=zones or [f"{cluster_id}_zone"],
        type=cluster_type,
        weight=weight,
        entry_fogs=entry_fogs,
        exit_fogs=exit_fogs,
        allow_shared_entrance=allow_shared_entrance,
        allow_entry_as_exit=allow_entry_as_exit,
    )
```

**Step 6: Run all tests to verify nothing broke**

Run: `pytest tests/ -v`
Expected: All existing tests PASS

**Step 7: Commit**

```bash
git add speedfog/clusters.py tests/test_clusters.py tests/test_generator.py
git commit -m "feat: add allow_shared_entrance and allow_entry_as_exit to ClusterData"
```

---

### Task 2: Compute reuse defaults in generate_clusters.py

**Files:**
- Modify: `tools/generate_clusters.py:1425-1454` (clusters_to_json)
- Modify: `tools/generate_clusters.py:1269-1366` (filter_and_enrich_clusters)
- Test: `tools/test_generate_clusters.py`

**Step 1: Write the failing test**

Add to `tools/test_generate_clusters.py` (find the test file, add a new test class):

```python
class TestFogReuseDefaults:
    """Tests for allow_shared_entrance / allow_entry_as_exit defaults."""

    def test_shared_entrance_true_when_two_plus_entries(self):
        """Clusters with 2+ entry fogs get allow_shared_entrance=True."""
        cluster = make_test_cluster(
            entry_fogs=[
                {"fog_id": "fog_a", "zone": "z1"},
                {"fog_id": "fog_b", "zone": "z2"},
            ],
            exit_fogs=[{"fog_id": "fog_c", "zone": "z1"}],
            cluster_type="mini_dungeon",
        )
        assert cluster.allow_shared_entrance is True

    def test_shared_entrance_false_when_one_entry(self):
        """Clusters with 1 entry fog get allow_shared_entrance=False."""
        cluster = make_test_cluster(
            entry_fogs=[{"fog_id": "fog_a", "zone": "z1"}],
            exit_fogs=[{"fog_id": "fog_b", "zone": "z1"}],
            cluster_type="mini_dungeon",
        )
        assert cluster.allow_shared_entrance is False

    def test_shared_entrance_override_false(self):
        """zone_metadata.toml can override allow_shared_entrance to false."""
        cluster = make_test_cluster(
            entry_fogs=[
                {"fog_id": "fog_a", "zone": "z1"},
                {"fog_id": "fog_b", "zone": "z2"},
            ],
            exit_fogs=[{"fog_id": "fog_c", "zone": "z1"}],
            cluster_type="mini_dungeon",
            override_shared_entrance=False,  # explicitly disabled via TOML
        )
        assert cluster.allow_shared_entrance is False
```

Note: `make_test_cluster` is a test helper that needs to call the enrichment logic
(including `compute_reuse_flags`). The exact shape depends on the existing test
helpers in `tools/test_generate_clusters.py`. Read the file to find the appropriate
pattern and adapt. The `override_shared_entrance` param simulates a TOML override.

Note: `allow_entry_as_exit` defaults are NOT computed in Phase 1. The field exists
in ClusterData for forward compatibility but is never set to True until Phase 2.

**Step 2: Run test to verify it fails**

Run: `cd tools && pytest test_generate_clusters.py::TestFogReuseDefaults -v`
Expected: FAIL — no such test class yet / clusters don't have reuse fields.

**Step 3: Implement defaults computation**

In `tools/generate_clusters.py`, add a function after `get_zone_weight` (around line 1254):

```python
def compute_allow_shared_entrance(
    entry_fogs: list[dict],
    zones_meta: dict,
    zones: frozenset[str],
) -> bool:
    """Compute allow_shared_entrance default for a cluster.

    Default: True if 2+ entry fogs.
    Per-zone overrides from zone_metadata.toml take priority.

    Args:
        entry_fogs: List of entry fog dicts
        zones_meta: The [zones] section from zone_metadata.toml
        zones: Set of zone names in this cluster

    Returns:
        Whether this cluster allows shared entrance merges.
    """
    allow = len(entry_fogs) >= 2

    # Per-zone overrides (any zone in cluster can override)
    for zone_name in zones:
        if zone_name not in zones_meta:
            continue
        zm = zones_meta[zone_name]
        if isinstance(zm, dict) and "allow_shared_entrance" in zm:
            allow = bool(zm["allow_shared_entrance"])

    return allow
```

Note: `allow_entry_as_exit` is NOT computed in Phase 1. It will be added in
Phase 2 with its own `compute_allow_entry_as_exit` function. The field exists
in ClusterData but defaults to False.

**Step 4: Call it from filter_and_enrich_clusters**

In `filter_and_enrich_clusters`, after `cluster.weight = total_weight` (line 1344), add:

```python
        # Compute fog reuse flags (Phase 1: shared entrance only)
        cluster.allow_shared_entrance = compute_allow_shared_entrance(
            cluster.entry_fogs,
            zones_meta,
            cluster.zones,
        )
```

Note: The `Cluster` class in `generate_clusters.py` is different from `ClusterData` in `clusters.py`. Add `allow_shared_entrance` and `allow_entry_as_exit` as attributes to the `Cluster` dataclass in `generate_clusters.py` (find it near the top of the file, likely around line 50-80).

**Step 5: Write to clusters.json**

In `clusters_to_json`, add the fields to the output dict (after line 1444):

```python
        if c.allow_shared_entrance:
            entry["allow_shared_entrance"] = True
        if c.allow_entry_as_exit:
            entry["allow_entry_as_exit"] = True
```

Only write when `True` to keep clusters.json clean (False is the default).

**Step 6: Run test to verify it passes**

Run: `cd tools && pytest test_generate_clusters.py::TestFogReuseDefaults -v`
Expected: PASS

**Step 7: Run all tool tests**

Run: `cd tools && pytest test_generate_clusters.py -v`
Expected: All PASS

**Step 8: Commit**

```bash
git add tools/generate_clusters.py
git commit -m "feat: compute fog reuse flags in generate_clusters"
```

---

### Task 3: Update can_be_merge_node for shared entrance

**Files:**
- Modify: `speedfog/generator.py:179-191`
- Test: `tests/test_generator.py`

**Step 1: Write the failing test**

Add to `tests/test_generator.py`:

```python
class TestCanBeMergeNodeSharedEntrance:
    """Tests for can_be_merge_node with allow_shared_entrance."""

    def test_shared_entrance_two_entries_one_exit(self):
        """With shared entrance, 2 entries + 1 exit qualifies for merge(2+)."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[{"fog_id": "exit_a", "zone": "merge_test"}],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is True
        assert can_be_merge_node(cluster, 3) is True  # fan-in doesn't matter

    def test_shared_entrance_needs_at_least_two_entries(self):
        """Shared entrance still requires 2+ entries (spec constraint)."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[{"fog_id": "entry_a", "zone": "merge_test"}],
            exit_fogs=[{"fog_id": "exit_a", "zone": "merge_test"}],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is False

    def test_shared_entrance_needs_at_least_one_exit(self):
        """With shared entrance, still needs at least 1 exit."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is False

    def test_shared_entrance_bidirectional_entry(self):
        """Shared entrance with bidirectional entry still qualifies."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "bidir", "zone": "merge_test"},  # bidirectional
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[
                {"fog_id": "bidir", "zone": "merge_test"},  # pair of entry
                {"fog_id": "exit_a", "zone": "merge_test"},
            ],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is True

    def test_without_shared_entrance_original_behavior(self):
        """Without shared entrance, original strict merge rules apply."""
        # 1 entry, 1 exit (bidirectional) — net exits = 0 → not merge(2)
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[{"fog_id": "bidir", "zone": "merge_test"}],
            exit_fogs=[{"fog_id": "bidir", "zone": "merge_test"}],
            allow_shared_entrance=False,
        )
        assert can_be_merge_node(cluster, 2) is False
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_generator.py::TestCanBeMergeNodeSharedEntrance -v`
Expected: FAIL — `make_cluster` might already accept the param (Task 1 step 5), but `can_be_merge_node` doesn't use it.

**Step 3: Implement**

Update `can_be_merge_node` in `speedfog/generator.py`:

```python
def can_be_merge_node(cluster: ClusterData, num_in: int) -> bool:
    """Check if cluster can be a merge node (num_in entries -> 1 exit).

    With shared entrance enabled, multiple branches connect to the same
    entrance fog gate. Only needs 1 entry + 1 exit regardless of fan-in.

    Args:
        cluster: The cluster to check.
        num_in: Number of entry fogs to consume.

    Returns:
        True if cluster can serve as a merge node.
    """
    if cluster.allow_shared_entrance:
        # Defensive: require 2+ entries even with override, per spec constraint
        return len(cluster.entry_fogs) >= 2 and len(cluster.exit_fogs) >= 1
    return len(cluster.entry_fogs) >= num_in and count_net_exits(cluster, num_in) == 1
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_generator.py::TestCanBeMergeNodeSharedEntrance -v`
Expected: PASS

**Step 5: Run all generator tests**

Run: `pytest tests/test_generator.py -v`
Expected: All PASS (no existing tests break)

**Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: can_be_merge_node supports shared entrance"
```

---

### Task 4: Update execute_merge_layer for shared entrance

**Files:**
- Modify: `speedfog/generator.py:694-836`
- Test: `tests/test_generator.py`

**Step 1: Write the failing test**

Add to `tests/test_generator.py`:

```python
class TestExecuteMergeLayerSharedEntrance:
    """Tests for execute_merge_layer with shared entrance clusters."""

    def _make_merge_pool(self):
        """Build a minimal pool where the ONLY merge-capable mini_dungeon
        is a shared-entrance cluster. This ensures deterministic testing."""
        pool = ClusterPool()

        # Source clusters (passant-capable, 1 entry + 2 exits with bidir)
        for i in range(2):
            pool.add(make_cluster(
                f"src_{i}",
                zones=[f"src_{i}_zone"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"src_{i}_entry", "zone": f"src_{i}_zone"}],
                exit_fogs=[
                    {"fog_id": f"src_{i}_exit", "zone": f"src_{i}_zone"},
                    {"fog_id": f"src_{i}_entry", "zone": f"src_{i}_zone"},
                ],
            ))

        # The only merge-capable cluster (shared entrance: 2 entries + 1 exit)
        pool.add(make_cluster(
            "shared_merge",
            zones=["shared_merge_zone"],
            cluster_type="mini_dungeon",
            entry_fogs=[
                {"fog_id": "shared_entry_a", "zone": "shared_merge_zone"},
                {"fog_id": "shared_entry_b", "zone": "shared_merge_zone"},
            ],
            exit_fogs=[
                {"fog_id": "shared_exit", "zone": "shared_merge_zone"},
            ],
            allow_shared_entrance=True,
        ))

        return pool

    def test_shared_entrance_merge_creates_single_entry_node(self):
        """Shared entrance merge creates a node with 1 entry_fog, not N."""
        pool = self._make_merge_pool()
        dag = Dag()

        # Create two source nodes from different parents
        src_a_cluster = pool.get_by_id("src_0")
        src_b_cluster = pool.get_by_id("src_1")
        src_a = DagNode(id="src_a", cluster=src_a_cluster, layer=0, tier=1,
                        entry_fogs=[], exit_fogs=[FogRef("src_0_exit", "src_0_zone")])
        src_b = DagNode(id="src_b", cluster=src_b_cluster, layer=0, tier=1,
                        entry_fogs=[], exit_fogs=[FogRef("src_1_exit", "src_1_zone")])
        dag.add_node(src_a)
        dag.add_node(src_b)

        branches = [
            Branch("a", "src_a", FogRef("src_0_exit", "src_0_zone")),
            Branch("b", "src_b", FogRef("src_1_exit", "src_1_zone")),
        ]

        rng = random.Random(42)
        config = Config()
        used_zones = {"src_0_zone", "src_1_zone"}

        result = execute_merge_layer(
            dag, branches, 1, 2, "mini_dungeon", pool, used_zones, rng, config,
        )

        # Find the merge node
        merge_nodes = [n for n in dag.nodes.values()
                       if n.cluster.id == "shared_merge"]
        assert len(merge_nodes) == 1
        merge_node = merge_nodes[0]

        # Shared entrance: node has 1 entry_fog, not 2
        assert len(merge_node.entry_fogs) == 1

        # Both edges point to the same entry_fog
        merge_edges = [e for e in dag.edges if e.target_id == merge_node.id]
        assert len(merge_edges) == 2
        assert merge_edges[0].entry_fog == merge_edges[1].entry_fog
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_generator.py::TestExecuteMergeLayerSharedEntrance -v`
Expected: FAIL — merge layer still selects N distinct entries.

**Step 3: Implement shared entrance in execute_merge_layer**

In `speedfog/generator.py`, modify `execute_merge_layer` (lines 765-791). Replace the entry selection and connection block:

```python
    used_zones.update(cluster.zones)

    if cluster.allow_shared_entrance:
        # Shared entrance: all branches connect to the same entry fog
        entry = _stable_main_shuffle(cluster.entry_fogs, rng)[0]
        shared_entry_fog = FogRef(entry["fog_id"], entry["zone"])
        entry_fogs_list = [shared_entry_fog]
        # Consume the entry's bidirectional pair from exits (Phase 1: no entry-as-exit)
        exits = compute_net_exits(cluster, [entry])
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
                branch.current_node_id, merge_node_id,
                branch.available_exit, shared_entry_fog,
            )
    else:
        # Original model: each branch gets a distinct entry
        for branch, entry_fog in zip(merge_branches, entry_fogs_list, strict=False):
            dag.add_edge(
                branch.current_node_id, merge_node_id,
                branch.available_exit, entry_fog,
            )
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_generator.py::TestExecuteMergeLayerSharedEntrance -v`
Expected: PASS

**Step 5: Run all tests**

Run: `pytest tests/ -v`
Expected: All PASS

**Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: execute_merge_layer supports shared entrance merges"
```

---

### Task 5: Update ConnectionInjector for DuplicateEntrance

**Files:**
- Modify: `writer/FogModWrapper/ConnectionInjector.cs`
- Test: manual C# integration test (existing infrastructure)

**Step 1: Write the C# implementation**

Update `ConnectionInjector.cs` to group connections by entrance and use
`DuplicateEntrance()` for secondary connections to the same entrance:

```csharp
public static InjectionResult InjectAndExtract(
    Graph graph, List<Connection> connections, int finishEvent, int finalNodeFlag)
{
    Console.WriteLine($"Injecting {connections.Count} connections...");

    var result = new InjectionResult { FinishEvent = finishEvent };

    // Group connections by (entrance_area, entrance_gate) to detect shared entrances.
    // Shared entrance = multiple exits connecting to the same entrance fog gate.
    // Order matters: process in original order (sorted by source layer in graph.json).
    var entranceGroups = new Dictionary<string, List<Connection>>();
    foreach (var conn in connections)
    {
        var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
        if (!entranceGroups.ContainsKey(key))
            entranceGroups[key] = new List<Connection>();
        entranceGroups[key].Add(conn);
    }

    // Track which entrances have already been connected (for DuplicateEntrance)
    var connectedEntrances = new Dictionary<string, Graph.Edge>();

    foreach (var conn in connections)
    {
        try
        {
            var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
            bool isSharedEntrance = entranceGroups[key].Count > 1;
            bool isSecondaryConnection = connectedEntrances.ContainsKey(key);

            ConnectAndExtract(graph, conn, finalNodeFlag, result,
                isSharedEntrance, isSecondaryConnection, connectedEntrances);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to connect: {conn}\n{ex.Message}", ex);
        }
    }

    Console.WriteLine($"All connections injected successfully.");
    return result;
}
```

Update `ConnectAndExtract` to handle shared entrances:

```csharp
private static void ConnectAndExtract(
    Graph graph, Connection conn, int finalNodeFlag, InjectionResult result,
    bool isSharedEntrance, bool isSecondaryConnection,
    Dictionary<string, Graph.Edge> connectedEntrances)
{
    // Find exit edge (unchanged)
    if (!graph.Nodes.TryGetValue(conn.ExitArea, out var exitNode))
        throw new Exception($"Exit area not found: {conn.ExitArea}");

    var exitEdge = exitNode.To.Find(e => e.Name == conn.ExitGate);
    if (exitEdge == null)
    {
        var available = string.Join(", ", exitNode.To.Select(e => e.Name));
        throw new Exception($"Exit edge not found: {conn.ExitGate} in {conn.ExitArea}\nAvailable: {available}");
    }

    // Find entrance edge
    if (!graph.Nodes.TryGetValue(conn.EntranceArea, out var entranceNode))
        throw new Exception($"Entrance area not found: {conn.EntranceArea}");

    Graph.Edge? entranceEdge = null;
    Graph.Edge? destExitEdge = null;

    if (isSecondaryConnection)
    {
        // Shared entrance: duplicate the original entrance for this connection
        var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
        var originalEntrance = connectedEntrances[key];
        entranceEdge = graph.DuplicateEntrance(originalEntrance);
        Console.WriteLine($"  Duplicated entrance for shared merge: {conn.EntranceGate}");
    }
    else
    {
        // Strategy 1: For bidirectional fogs, find via To + Pair
        destExitEdge = entranceNode.To.Find(e => e.Name == conn.EntranceGate);
        if (destExitEdge != null)
            entranceEdge = destExitEdge.Pair;

        // Strategy 2: For one-way warps, entrance is directly in From
        if (entranceEdge == null)
            entranceEdge = entranceNode.From.Find(e => e.Name == conn.EntranceGate);

        if (entranceEdge == null)
        {
            var availableTo = string.Join(", ", entranceNode.To.Select(e => e.Name));
            var availableFrom = string.Join(", ", entranceNode.From.Select(e => e.Name));
            throw new Exception(
                $"Entrance edge not found: {conn.EntranceGate} in {conn.EntranceArea}\n" +
                $"Available in To: {availableTo}\n" +
                $"Available in From: {availableFrom}");
        }
    }

    // Always disconnect the exit edge if pre-connected (each connection has its own exit)
    if (exitEdge.Link != null)
    {
        Console.WriteLine($"  Disconnecting pre-connected exit: {conn.ExitGate}");
        graph.Disconnect(exitEdge);
    }

    // Entrance-side disconnect only for primary connections (duplicates are fresh edges)
    if (!isSecondaryConnection)
    {
        if (destExitEdge != null && destExitEdge.Link != null)
        {
            Console.WriteLine($"  Disconnecting pre-connected entrance: {conn.EntranceGate}");
            graph.Disconnect(destExitEdge);
        }
        if (entranceEdge.Link != null)
        {
            Console.WriteLine($"  Disconnecting pre-connected entrance link: {conn.EntranceGate}");
            graph.Disconnect(entranceEdge.Link);
        }
    }

    // Connect
    graph.Connect(exitEdge, entranceEdge);

    // Track connected entrance for shared entrance detection
    if (isSharedEntrance && !isSecondaryConnection)
    {
        var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
        connectedEntrances[key] = entranceEdge;
    }

    Console.WriteLine($"  Connected: {conn.ExitArea} --[{conn.ExitGate}]--> {conn.EntranceArea}" +
        (isSecondaryConnection ? " (shared entrance)" : ""));

    // Extract boss defeat flag for final boss connections
    if (conn.FlagId == finalNodeFlag && finalNodeFlag > 0)
    {
        var area = graph.Areas.GetValueOrDefault(conn.EntranceArea);
        if (area != null && area.DefeatFlag > 0)
            result.BossDefeatFlag = area.DefeatFlag;
        else
            Console.WriteLine($"Note: No DefeatFlag in FogMod Graph for finish area {conn.EntranceArea} " +
                "(will use graph.json finish_boss_defeat_flag if available)");
    }
}
```

**Step 2: Build to verify it compiles**

Run: `cd writer/FogModWrapper && dotnet build`
Expected: Build succeeded

**Step 3: Run C# tests**

Run: `cd writer/FogModWrapper.Tests && dotnet test`
Expected: All PASS (existing tests don't exercise shared entrance)

**Step 4: Commit**

```bash
git add writer/FogModWrapper/ConnectionInjector.cs
git commit -m "feat: ConnectionInjector supports shared entrance via DuplicateEntrance"
```

---

### Task 6: Bump clusters.json version and update generate_clusters

**Files:**
- Modify: `tools/generate_clusters.py:1447-1448` (version string)
- Modify: `speedfog/clusters.py` (if version validation exists)

**Step 1: Bump version**

In `tools/generate_clusters.py`, update the version in `clusters_to_json`:

```python
    return {
        "version": "1.6",
        ...
    }
```

**Step 2: Verify clusters.json version is checked on load**

Check if `ClusterPool.from_json` or `load_clusters` validates the version.
If so, update to accept "1.6". If not, no change needed.

**Step 3: Commit**

```bash
git add tools/generate_clusters.py
git commit -m "chore: bump clusters.json version to 1.6 for fog reuse fields"
```

---

### Task 7: End-to-end simulation test

**Files:**
- Test: `tests/test_generator.py`

**Step 1: Write a simulation test**

Add to `tests/test_generator.py`:

```python
def make_cluster_pool_with_shared_entrance() -> ClusterPool:
    """Create a cluster pool where multi-entry clusters have allow_shared_entrance=True.

    Based on make_cluster_pool() but enables shared entrance on clusters
    that have 2+ entry fogs (the Phase 1 default rule).
    """
    pool = make_cluster_pool()

    # Enable shared entrance on all clusters with 2+ entry fogs
    for cluster in pool.clusters:
        if len(cluster.entry_fogs) >= 2:
            cluster.allow_shared_entrance = True

    return pool


class TestSharedEntranceSimulation:
    """Verify shared entrance merges work in full DAG generation."""

    def test_generation_succeeds_with_shared_entrance_clusters(self):
        """DAG generation succeeds when merge pool includes shared-entrance clusters."""
        pool = make_cluster_pool_with_shared_entrance()
        config = Config()

        # Run 20 seeds — verify no GenerationError
        for seed in range(20):
            try:
                dag = generate_dag(config, pool, seed=seed)
                assert len(dag.nodes) >= 3  # at least start + 1 node + end
            except GenerationError:
                pytest.fail(f"Generation failed with seed {seed}")
```

**Step 2: Run test**

Run: `pytest tests/test_generator.py::TestSharedEntranceSimulation -v`
Expected: PASS

**Step 3: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add shared entrance merge simulation test"
```

---

### Task 8: Regenerate clusters.json with reuse flags

This task requires `data/fog.txt` (gitignored). Skip if not available.

**Step 1: Regenerate clusters.json**

Run:
```bash
python tools/generate_clusters.py data/fog.txt data/clusters.json --metadata data/zone_metadata.toml
```

**Step 2: Verify output**

Check that some clusters in `data/clusters.json` now have `allow_shared_entrance: true`.

Run: `grep -c "allow_shared_entrance" data/clusters.json`
Expected: Non-zero count

**Step 3: Run integration tests with new clusters.json**

Run: `pytest tests/test_integration.py -v`
Expected: PASS (if `data/clusters.json` is available)

**Step 4: Commit clusters.json** (if tracked)

Note: `data/clusters.json` is gitignored. Do not commit it. The user regenerates
it from `fog.txt` as part of setup.
