# Weight-Matched Cluster Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure parallel branches on the same DAG layer receive clusters of similar weight, so players face comparable difficulty regardless of which path they choose.

**Architecture:** Add `pick_cluster_weight_matched()` to `generator.py` with progressive tolerance (exact match first, then +/-1, +/-2, ..., then any). Replace the 5 secondary-cluster selection call sites to use it. Add `max_weight_tolerance` config field (default 3, 0 disables).

**Tech Stack:** Python 3.10+, pytest, speedfog package

**Spec:** `docs/specs/2026-03-25-weight-matched-cluster-selection.md`

---

## Chunk 1: Config

### Task 1: Add `max_weight_tolerance` to StructureConfig

**Files:**
- Modify: `speedfog/config.py:45-64` (StructureConfig dataclass)
- Modify: `speedfog/config.py:495-515` (Config.from_dict)
- Test: `tests/test_config.py`

- [ ] **Step 1: Write failing tests for config field**

```python
# Append to tests/test_config.py

def test_max_weight_tolerance_default():
    """max_weight_tolerance defaults to 3."""
    config = Config.from_dict({})
    assert config.structure.max_weight_tolerance == 3


def test_max_weight_tolerance_from_toml(tmp_path):
    """max_weight_tolerance parsed from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
max_weight_tolerance = 5
""")
    config = Config.from_toml(config_file)
    assert config.structure.max_weight_tolerance == 5


def test_max_weight_tolerance_disabled():
    """max_weight_tolerance = 0 disables weight matching."""
    config = Config.from_dict({"structure": {"max_weight_tolerance": 0}})
    assert config.structure.max_weight_tolerance == 0


def test_max_weight_tolerance_negative_raises():
    """Negative max_weight_tolerance raises ValueError."""
    with pytest.raises(ValueError, match="max_weight_tolerance"):
        Config.from_dict({"structure": {"max_weight_tolerance": -1}})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_config.py -k "max_weight_tolerance" -v`
Expected: FAIL (attribute not found)

- [ ] **Step 3: Add field to StructureConfig and from_dict**

In `speedfog/config.py`, add to `StructureConfig` dataclass (after `tier_curve_exponent`):

```python
    max_weight_tolerance: int = 3  # Max weight tolerance for parallel branch matching (0=disabled)
```

Add validation in `__post_init__` (after `max_branch_spacing` validation):

```python
        if self.max_weight_tolerance < 0:
            raise ValueError(
                f"max_weight_tolerance must be >= 0, got {self.max_weight_tolerance}"
            )
```

In `Config.from_dict`, add to the `StructureConfig(...)` constructor call:

```python
                max_weight_tolerance=structure_section.get("max_weight_tolerance", 3),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_config.py -k "max_weight_tolerance" -v`
Expected: 4 PASSED

- [ ] **Step 5: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -x -q`
Expected: all pass (no regressions)

- [ ] **Step 6: Commit**

```bash
cd /home/dev/src/games/ER/fog/speedfog && git add speedfog/config.py tests/test_config.py && git commit -m "feat: add max_weight_tolerance config field"
```

---

## Chunk 2: Core function

### Task 2: Implement `pick_cluster_weight_matched()`

**Files:**
- Modify: `speedfog/generator.py` (add function after `pick_cluster_with_filter`, ~line 461)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing tests**

```python
# Append to tests/test_generator.py
# Add pick_cluster_weight_matched to the import block at the top.

class TestPickClusterWeightMatched:
    """Tests for pick_cluster_weight_matched."""

    def _make_pool(self, weights: list[int]) -> list[ClusterData]:
        """Create clusters with distinct zones and specified weights."""
        return [
            make_cluster(f"c{i}", zones=[f"z{i}"], weight=w)
            for i, w in enumerate(weights)
        ]

    def test_exact_match_preferred(self):
        """When an exact weight match exists, it is chosen."""
        candidates = self._make_pool([1, 2, 3, 4, 5])
        rng = random.Random(42)
        # Run many times: anchor=3 should always pick weight-3 first
        results = set()
        for seed in range(50):
            r = pick_cluster_weight_matched(
                candidates, set(), random.Random(seed), anchor_weight=3,
            )
            assert r is not None
            results.add(r.weight)
        assert results == {3}  # Only exact match since only 1 candidate at w=3

    def test_tolerance_widening(self):
        """When no exact match, widens progressively."""
        # Weights [1, 5]: no exact match for anchor=3, no +/-1 match either.
        # At tol=2: weight 1 (|1-3|=2) and weight 5 (|5-3|=2) both match.
        # With max_tolerance=1, neither matches -> fallback to any.
        # With max_tolerance=2, both match at step 2.
        candidates = self._make_pool([1, 5])
        # Verify tol=1 is not enough (would need fallback)
        r1 = pick_cluster_weight_matched(
            candidates, set(), random.Random(42), anchor_weight=3,
            max_tolerance=1,
        )
        assert r1 is not None
        assert r1.weight in (1, 5)  # fallback: any

        # Verify tol=2 matches (both within range)
        r2 = pick_cluster_weight_matched(
            candidates, set(), random.Random(42), anchor_weight=3,
            max_tolerance=2,
        )
        assert r2 is not None
        assert r2.weight in (1, 5)  # matched at step 2

    def test_fallback_to_any(self):
        """When nothing within max_tolerance, falls back to any available."""
        candidates = self._make_pool([1, 1, 1])
        result = pick_cluster_weight_matched(
            candidates, set(), random.Random(42), anchor_weight=10,
            max_tolerance=2,
        )
        assert result is not None
        assert result.weight == 1

    def test_disabled_when_zero(self):
        """max_tolerance=0 returns uniform random (no weight preference)."""
        candidates = self._make_pool([1, 5, 10])
        weights_seen: set[int] = set()
        for seed in range(100):
            r = pick_cluster_weight_matched(
                candidates, set(), random.Random(seed), anchor_weight=5,
                max_tolerance=0,
            )
            assert r is not None
            weights_seen.add(r.weight)
        # With 100 seeds and 3 candidates, all weights should appear
        assert weights_seen == {1, 5, 10}

    def test_filter_fn_composed(self):
        """filter_fn is applied alongside weight matching."""
        c_passant = make_cluster(
            "ok", zones=["z_ok"], weight=3,
            entry_fogs=[{"fog_id": "e", "zone": "z_ok"}],
            exit_fogs=[{"fog_id": "x", "zone": "z_ok"}],
        )
        c_no_passant = make_cluster(
            "bad", zones=["z_bad"], weight=3,
            entry_fogs=[], exit_fogs=[],
        )
        candidates = [c_passant, c_no_passant]
        result = pick_cluster_weight_matched(
            candidates, set(), random.Random(42), anchor_weight=3,
            filter_fn=can_be_passant_node,
        )
        assert result is not None
        assert result.id == "ok"

    def test_zone_exclusion(self):
        """Candidates with overlapping zones are excluded."""
        candidates = self._make_pool([3, 3, 3])
        used = {"z0", "z1"}  # Exclude first two
        result = pick_cluster_weight_matched(
            candidates, used, random.Random(42), anchor_weight=3,
        )
        assert result is not None
        assert result.id == "c2"

    def test_returns_none_when_empty(self):
        """Returns None when no candidates available."""
        result = pick_cluster_weight_matched(
            [], set(), random.Random(42), anchor_weight=3,
        )
        assert result is None

    def test_reserved_zones_excluded(self):
        """Reserved zones are excluded like used zones."""
        candidates = self._make_pool([3])
        result = pick_cluster_weight_matched(
            candidates, set(), random.Random(42), anchor_weight=3,
            reserved_zones=frozenset({"z0"}),
        )
        assert result is None
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generator.py -k "TestPickClusterWeightMatched" -v`
Expected: FAIL (import error, function not defined)

- [ ] **Step 3: Implement the function**

Add to `speedfog/generator.py` after `pick_cluster_with_filter` (~line 461):

```python
def pick_cluster_weight_matched(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    anchor_weight: int,
    filter_fn: Callable[[ClusterData], bool] = lambda c: True,
    *,
    reserved_zones: frozenset[str] = frozenset(),
    max_tolerance: int = 3,
) -> ClusterData | None:
    """Pick a cluster with weight close to anchor_weight.

    Filters candidates once (zone availability + filter_fn), then applies
    progressive weight tolerance starting from exact match.
    Falls back to any available cluster if no match within max_tolerance.

    Args:
        candidates: List of candidate clusters.
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        anchor_weight: Target weight to match.
        filter_fn: Additional filter (e.g. can_be_passant_node).
        reserved_zones: Zones reserved for prerequisite placement.
        max_tolerance: Max tolerance steps (0 = disabled, uniform random).

    Returns:
        A cluster close to anchor_weight, or None if nothing available.
    """
    available = [
        c
        for c in candidates
        if not any(z in used_zones or z in reserved_zones for z in c.zones)
        and filter_fn(c)
    ]
    if not available:
        return None

    if max_tolerance <= 0:
        return rng.choice(available)

    for tol in range(0, max_tolerance + 1):
        matched = [c for c in available if abs(c.weight - anchor_weight) <= tol]
        if matched:
            return rng.choice(matched)

    return rng.choice(available)
```

- [ ] **Step 4: Add to import block in test file**

Add `pick_cluster_weight_matched` to the import from `speedfog.generator` in `tests/test_generator.py`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generator.py -k "TestPickClusterWeightMatched" -v`
Expected: 8 PASSED

- [ ] **Step 6: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -x -q`
Expected: all pass

- [ ] **Step 7: Commit**

```bash
cd /home/dev/src/games/ER/fog/speedfog && git add speedfog/generator.py tests/test_generator.py && git commit -m "feat: add pick_cluster_weight_matched with progressive tolerance"
```

---

## Chunk 3: Wire into generate_dag main loop (3 sites)

### Task 3: Replace secondary picks in generate_dag

**Files:**
- Modify: `speedfog/generator.py` (3 call sites in `generate_dag()`)
- Test: `tests/test_generator.py`

The 3 sites in `generate_dag()` all follow the same pattern: `pick_cluster_with_type_fallback(clusters, layer_type, used_zones, rng, reserved_zones=...)`. Replace with weight-matched pick + type fallback on None.

- [ ] **Step 1: Write integration test**

```python
# Append to tests/test_generator.py

def test_parallel_branches_weight_matched():
    """On a PASSANT layer with 2+ branches, clusters have similar weights.

    Uses a pool where weight-1 and weight-8 clusters coexist.
    With weight matching, if branch A gets weight-1, branch B should
    not get weight-8 (too far at tolerance 3).
    """
    # Build a pool with weight variety
    clusters_list = []
    for i in range(20):
        clusters_list.append(
            make_cluster(f"light_{i}", zones=[f"l{i}"], weight=1,
                         cluster_type="mini_dungeon")
        )
    for i in range(5):
        clusters_list.append(
            make_cluster(f"heavy_{i}", zones=[f"h{i}"], weight=8,
                         cluster_type="mini_dungeon")
        )
    # Need a start cluster and final boss
    start = make_cluster(
        "start_c", zones=["start_z"], cluster_type="start", weight=1,
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "sx1", "zone": "start_z"},
            {"fog_id": "sx2", "zone": "start_z"},
        ],
    )
    final = make_cluster(
        "final_c", zones=["final_z"], cluster_type="final_boss", weight=3,
        entry_fogs=[
            {"fog_id": "fe1", "zone": "final_z"},
            {"fog_id": "fe2", "zone": "final_z"},
        ],
        exit_fogs=[],
    )
    clusters_list.extend([start, final])

    pool = ClusterPool()
    for c in clusters_list:
        pool.add(c)

    config = Config.from_dict({
        "structure": {
            "max_parallel_paths": 3,
            "min_layers": 4,
            "max_layers": 6,
            "split_probability": 1.0,
            "max_weight_tolerance": 3,
            "max_branch_spacing": 0,
            "final_boss_candidates": {"final_z": 1},
        },
    })

    # Generate multiple DAGs, check weight spread on parallel layers
    max_spreads = []
    for seed in range(50):
        try:
            dag = generate_dag(pool, config, random.Random(seed))
        except GenerationError:
            continue
        # Find layers with multiple nodes
        layers: dict[int, list[int]] = {}
        for node in dag.nodes.values():
            weights = layers.setdefault(node.layer, [])
            weights.append(node.cluster.weight)
        for layer_idx, weights in layers.items():
            if len(weights) >= 2:
                max_spreads.append(max(weights) - min(weights))

    # At least some DAGs must have generated successfully
    assert max_spreads, "No DAGs generated successfully"

    # With weight matching (tolerance 3), most spreads should be <= 3
    # Allow some tolerance for fallback cases
    within_tolerance = sum(1 for s in max_spreads if s <= 3)
    ratio = within_tolerance / len(max_spreads)
    assert ratio >= 0.8, (
        f"Only {ratio:.0%} of parallel layers within tolerance 3. "
        f"Spreads: {sorted(set(max_spreads))}"
    )
```

- [ ] **Step 2: Run test to verify it fails (or shows poor ratio)**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generator.py::test_parallel_branches_weight_matched -v`
Expected: FAIL or poor ratio (currently no weight matching)

- [ ] **Step 3: Replace PASSANT multi-branch site (~line 2170)**

In `generate_dag()`, in the `if operation == LayerOperation.PASSANT:` block, replace the secondary pick loop. Currently:

```python
                else:
                    c = pick_cluster_with_type_fallback(
                        clusters,
                        layer_type,
                        used_zones,
                        rng,
                        reserved_zones=reserved_zones,
                    )
```

Replace with:

```python
                else:
                    c = pick_cluster_weight_matched(
                        clusters.get_by_type(layer_type),
                        used_zones,
                        rng,
                        anchor_weight=primary_cluster.weight,
                        max_tolerance=config.structure.max_weight_tolerance,
                        reserved_zones=reserved_zones,
                    )
                    if c is None:
                        c = pick_cluster_with_type_fallback(
                            clusters,
                            layer_type,
                            used_zones,
                            rng,
                            reserved_zones=reserved_zones,
                        )
```

- [ ] **Step 4: Replace SPLIT non-split branch site (~line 1969)**

In the `if operation == LayerOperation.SPLIT:` block, replace:

```python
                    pc = pick_cluster_with_type_fallback(
                        clusters,
                        layer_type,
                        used_zones,
                        rng,
                        reserved_zones=reserved_zones,
                    )
```

With:

```python
                    pc = pick_cluster_weight_matched(
                        clusters.get_by_type(layer_type),
                        used_zones,
                        rng,
                        anchor_weight=primary_cluster.weight,
                        max_tolerance=config.structure.max_weight_tolerance,
                        reserved_zones=reserved_zones,
                    )
                    if pc is None:
                        pc = pick_cluster_with_type_fallback(
                            clusters,
                            layer_type,
                            used_zones,
                            rng,
                            reserved_zones=reserved_zones,
                        )
```

- [ ] **Step 5: Replace MERGE non-merged branch site (~line 2118)**

In the `elif operation == LayerOperation.MERGE:` block, replace the non-merged branch pick:

```python
                    pc = pick_cluster_with_type_fallback(
                        clusters,
                        layer_type,
                        used_zones,
                        rng,
                        reserved_zones=reserved_zones,
                    )
```

With:

```python
                    pc = pick_cluster_weight_matched(
                        clusters.get_by_type(layer_type),
                        used_zones,
                        rng,
                        anchor_weight=primary_cluster.weight,
                        max_tolerance=config.structure.max_weight_tolerance,
                        reserved_zones=reserved_zones,
                    )
                    if pc is None:
                        pc = pick_cluster_with_type_fallback(
                            clusters,
                            layer_type,
                            used_zones,
                            rng,
                            reserved_zones=reserved_zones,
                        )
```

- [ ] **Step 6: Run integration test**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generator.py::test_parallel_branches_weight_matched -v`
Expected: PASS

- [ ] **Step 7: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -x -q`
Expected: all pass

- [ ] **Step 8: Commit**

```bash
cd /home/dev/src/games/ER/fog/speedfog && git add speedfog/generator.py tests/test_generator.py && git commit -m "feat: wire weight matching into generate_dag main loop (3 sites)"
```

---

## Chunk 4: Wire into convergence functions (2 sites)

### Task 4: Update `execute_passant_layer` and `execute_merge_layer`

**Files:**
- Modify: `speedfog/generator.py` (`execute_passant_layer` ~line 1287, `execute_merge_layer` ~line 1427)
- Test: `tests/test_generator.py`

Both functions need `config` threaded through (for `max_weight_tolerance`). `execute_passant_layer` has no primary cluster, so the first branch's cluster establishes the anchor. `execute_merge_layer` uses the merge cluster as anchor.

- [ ] **Step 1: Write failing tests**

```python
# Append to tests/test_generator.py

def _make_dag_with_start():
    """Helper: create a Dag with a start node and 2 branches."""
    dag = Dag(seed=1)
    start = DagNode(
        id="start",
        cluster=make_cluster(
            "s", zones=["sz"], cluster_type="start",
            entry_fogs=[], exit_fogs=[{"fog_id": "sx", "zone": "sz"}],
        ),
        layer=0, tier=1, entry_fogs=[],
        exit_fogs=[FogRef("sx", "sz")],
    )
    dag.add_node(start)
    dag.start_id = "start"
    return dag


def test_execute_passant_layer_weight_matched():
    """execute_passant_layer uses weight matching: first branch anchors the rest."""
    dag = _make_dag_with_start()
    branches = [
        Branch("b0", "start", FogRef("sx", "sz"), layers_since_last_split=0),
        Branch("b1", "start", FogRef("sx", "sz"), layers_since_last_split=0),
    ]
    # Pool: clusters at weight 1 and weight 2 only (all within tolerance)
    pool = ClusterPool()
    for i in range(5):
        pool.add(make_cluster(f"w1_{i}", zones=[f"w1z{i}"], weight=1, cluster_type="mini_dungeon"))
    for i in range(5):
        pool.add(make_cluster(f"w2_{i}", zones=[f"w2z{i}"], weight=2, cluster_type="mini_dungeon"))
    # Add one outlier at weight 8 that should NOT be picked when anchor is 1 or 2
    pool.add(make_cluster("outlier", zones=["oz"], weight=8, cluster_type="mini_dungeon"))

    config = Config.from_dict({"structure": {"max_weight_tolerance": 2}})

    # Run multiple seeds: the second branch should always be within tolerance
    # of the first branch (spread <= 2)
    for seed in range(20):
        test_dag = _make_dag_with_start()
        test_branches = [
            Branch("b0", "start", FogRef("sx", "sz"), layers_since_last_split=0),
            Branch("b1", "start", FogRef("sx", "sz"), layers_since_last_split=0),
        ]
        # Rebuild pool each iteration (clusters get consumed)
        test_pool = ClusterPool()
        for i in range(5):
            test_pool.add(make_cluster(f"w1_{i}", zones=[f"w1z{i}"], weight=1, cluster_type="mini_dungeon"))
        for i in range(5):
            test_pool.add(make_cluster(f"w2_{i}", zones=[f"w2z{i}"], weight=2, cluster_type="mini_dungeon"))
        test_pool.add(make_cluster("outlier", zones=["oz"], weight=8, cluster_type="mini_dungeon"))

        result = execute_passant_layer(
            test_dag, test_branches, 1, "mini_dungeon", test_pool, {"sz"},
            random.Random(seed), config=config,
        )
        node_a = test_dag.nodes[result[0].current_node_id]
        node_b = test_dag.nodes[result[1].current_node_id]
        spread = abs(node_a.cluster.weight - node_b.cluster.weight)
        # First pick: uniform from pool (w=1, w=2, or w=8).
        # If first is w=1 or w=2: second should match within tol=2 -> spread <= 2
        # If first is w=8 (rare, 1/11 chance): second has no match within tol=2,
        #   fallback picks any -> spread could be large. That's the accepted
        #   primary-is-unconstrained trade-off.
        if node_a.cluster.weight <= 2:
            assert spread <= 2, f"seed={seed}: spread={spread} (weights: {node_a.cluster.weight}, {node_b.cluster.weight})"


def test_execute_merge_layer_weight_matched():
    """execute_merge_layer: non-merged branches weight-match the merge cluster."""
    dag = Dag(seed=1)
    # 3 nodes on layer 0
    for i in range(3):
        n = DagNode(
            id=f"n{i}",
            cluster=make_cluster(
                f"c{i}", zones=[f"z{i}"],
                entry_fogs=[{"fog_id": f"e{i}", "zone": f"z{i}"}],
                exit_fogs=[{"fog_id": f"x{i}", "zone": f"z{i}"}],
            ),
            layer=0, tier=1, entry_fogs=[],
            exit_fogs=[FogRef(f"x{i}", f"z{i}")],
        )
        dag.add_node(n)

    branches = [
        Branch("b0", "n0", FogRef("x0", "z0"), birth_layer=0, layers_since_last_split=3),
        Branch("b1", "n1", FogRef("x1", "z1"), birth_layer=0, layers_since_last_split=3),
        Branch("b2", "n2", FogRef("x2", "z2"), birth_layer=0, layers_since_last_split=3),
    ]

    # Merge cluster (weight 2) + passant candidates
    merge_c = make_cluster(
        "merge", zones=["mz"], weight=2, cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "me1", "zone": "mz"},
            {"fog_id": "me2", "zone": "mz"},
        ],
        exit_fogs=[{"fog_id": "mx", "zone": "mz"}],
    )
    passant_close = make_cluster(
        "p_close", zones=["pz1"], weight=2, cluster_type="mini_dungeon",
    )
    passant_far = make_cluster(
        "p_far", zones=["pz2"], weight=10, cluster_type="mini_dungeon",
    )
    pool = ClusterPool()
    pool.add(merge_c)
    pool.add(passant_close)
    pool.add(passant_far)

    config = Config.from_dict({"structure": {"max_weight_tolerance": 2}})

    result = execute_merge_layer(
        dag, branches, 1, "mini_dungeon", pool,
        {"z0", "z1", "z2"}, random.Random(42), config,
    )
    # The non-merged branch should get passant_close (weight 2),
    # not passant_far (weight 10), since merge cluster anchor is weight 2
    passant_branches = [b for b in result if "merged" not in b.id]
    if passant_branches:
        passant_node = dag.nodes[passant_branches[0].current_node_id]
        assert passant_node.cluster.weight == 2
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generator.py -k "test_execute_passant_layer_weight_matched or test_execute_merge_layer_weight_matched" -v`
Expected: FAIL (config parameter not accepted)

- [ ] **Step 3: Update `execute_passant_layer` signature and body**

In `speedfog/generator.py`, modify `execute_passant_layer`:

Add `config: Config` parameter (after `rng`, before `*`):

```python
def execute_passant_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    *,
    config: Config | None = None,
    reserved_zones: frozenset[str] = frozenset(),
) -> list[Branch]:
```

Replace the cluster selection loop body. Currently:

```python
    for i, branch in enumerate(branches):
        cluster = pick_cluster_with_filter(
            candidates,
            used_zones,
            rng,
            can_be_passant_node,
            reserved_zones=reserved_zones,
        )
```

Replace with:

```python
    max_tol = config.structure.max_weight_tolerance if config else 0
    anchor_weight: int | None = None

    for i, branch in enumerate(branches):
        if anchor_weight is None or max_tol <= 0:
            cluster = pick_cluster_with_filter(
                candidates,
                used_zones,
                rng,
                can_be_passant_node,
                reserved_zones=reserved_zones,
            )
            if cluster is not None and anchor_weight is None:
                anchor_weight = cluster.weight
        else:
            cluster = pick_cluster_weight_matched(
                candidates,
                used_zones,
                rng,
                anchor_weight,
                filter_fn=can_be_passant_node,
                reserved_zones=reserved_zones,
                max_tolerance=max_tol,
            )
```

- [ ] **Step 4: Update `execute_merge_layer` body**

In the non-merged branches loop (~line 1577), replace:

```python
        passant_cluster = pick_cluster_with_filter(
            candidates,
            used_zones,
            rng,
            can_be_passant_node,
            reserved_zones=reserved_zones,
        )
```

With:

```python
        max_tol = config.structure.max_weight_tolerance
        if max_tol > 0:
            passant_cluster = pick_cluster_weight_matched(
                candidates,
                used_zones,
                rng,
                anchor_weight=cluster.weight,
                filter_fn=can_be_passant_node,
                reserved_zones=reserved_zones,
                max_tolerance=max_tol,
            )
        else:
            passant_cluster = pick_cluster_with_filter(
                candidates,
                used_zones,
                rng,
                can_be_passant_node,
                reserved_zones=reserved_zones,
            )
```

Note: `cluster` here refers to the merge cluster (already assigned earlier in the function), which serves as anchor.

- [ ] **Step 5: Update callers of `execute_passant_layer` to pass `config`**

There is exactly one caller in `generate_dag()` at line ~2312 (convergence phase):

```python
# Line ~2312 in generate_dag(), convergence phase:
branches = execute_passant_layer(
    dag, branches, current_layer, conv_layer_type, clusters, used_zones, rng,
    reserved_zones=reserved_zones,
)
```

Add `config=config`:

```python
branches = execute_passant_layer(
    dag, branches, current_layer, conv_layer_type, clusters, used_zones, rng,
    config=config,
    reserved_zones=reserved_zones,
)
```

This is critical: without this change, weight matching silently does not apply during convergence.

- [ ] **Step 6: Fix existing tests that call `execute_passant_layer` or `execute_merge_layer`**

Existing tests (`test_execute_passant_layer_carries_counter`, `test_execute_merge_layer_carries_counter`) may need adjustment if signatures changed. Since `config` is keyword-only with `None` default, existing tests should still work without changes. Verify by running them.

- [ ] **Step 7: Run targeted tests**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generator.py -k "execute_passant_layer or execute_merge_layer" -v`
Expected: all PASS (new + existing tests)

- [ ] **Step 8: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -x -q`
Expected: all pass

- [ ] **Step 9: Commit**

```bash
cd /home/dev/src/games/ER/fog/speedfog && git add speedfog/generator.py tests/test_generator.py && git commit -m "feat: wire weight matching into convergence functions (2 sites)"
```

---

## Chunk 5: Mypy + final validation

### Task 5: Type checking and full validation

**Files:**
- Possibly modify: `speedfog/generator.py` (type annotations if needed)

- [ ] **Step 1: Run mypy**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run mypy speedfog/`
Expected: no new errors

- [ ] **Step 2: Fix any type issues**

Address any mypy errors related to the new function or modified signatures.

- [ ] **Step 3: Run full test suite one final time**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v`
Expected: all pass

- [ ] **Step 4: Commit if any fixes were needed**

```bash
cd /home/dev/src/games/ER/fog/speedfog && git add -u && git commit -m "fix: type annotations for weight matching"
```
