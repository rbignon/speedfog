# Max Branch Spacing Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee that no branch in the DAG goes more than ~4 layers without a split point, so players always have a nearby alternative path.

**Architecture:** Add a per-branch `layers_since_last_split` counter to the `Branch` dataclass. Propagate it through all helpers (`execute_passant_layer`, `execute_merge_layer`, `execute_forced_merge`). The main generation loop checks this counter each layer and forces a SPLIT operation (if the cluster supports it) or delegates to `execute_forced_merge` (to free a slot) when the threshold is reached. Cluster selection remains uniform — no bias.

**Tech Stack:** Python 3.10+, pytest, speedfog package

**Spec:** `docs/specs/2026-03-11-max-branch-spacing-design.md`

---

## Chunk 1: Config + Branch Counter + determine_operation

### Task 1: Add `max_branch_spacing` to config

**Files:**
- Modify: `speedfog/config.py:45-117` (StructureConfig)
- Test: `tests/test_config.py`

- [ ] **Step 1: Write failing test for config field**

```python
# In tests/test_config.py

def test_max_branch_spacing_default():
    """max_branch_spacing defaults to 4."""
    config = Config.from_dict({})
    assert config.structure.max_branch_spacing == 4


def test_max_branch_spacing_from_toml(tmp_path):
    """max_branch_spacing parsed from TOML."""
    config_file = tmp_path / "config.toml"
    config_file.write_text("""
[structure]
max_branch_spacing = 6
""")
    config = Config.from_toml(config_file)
    assert config.structure.max_branch_spacing == 6


def test_max_branch_spacing_disabled():
    """max_branch_spacing = 0 disables enforcement."""
    config = Config.from_dict({"structure": {"max_branch_spacing": 0}})
    assert config.structure.max_branch_spacing == 0


def test_max_branch_spacing_validation():
    """min_branch_age >= max_branch_spacing raises ValueError."""
    with pytest.raises(ValueError, match="min_branch_age"):
        Config.from_dict({
            "structure": {
                "min_branch_age": 4,
                "max_branch_spacing": 4,
            }
        })


def test_max_branch_spacing_negative():
    """Negative max_branch_spacing raises ValueError."""
    with pytest.raises(ValueError, match="max_branch_spacing"):
        Config.from_dict({"structure": {"max_branch_spacing": -1}})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_config.py -v -k "max_branch_spacing"`
Expected: FAIL — `StructureConfig` has no `max_branch_spacing` field.

- [ ] **Step 3: Implement config field**

In `speedfog/config.py`, add to `StructureConfig` (after `min_branch_age` line 56):

```python
max_branch_spacing: int = 4  # Max layers between splits per branch (0=disabled)
```

In `__post_init__` (after `min_branch_age` validation at line 117), add:

```python
if self.max_branch_spacing < 0:
    raise ValueError(
        f"max_branch_spacing must be >= 0, got {self.max_branch_spacing}"
    )
if self.max_branch_spacing > 0 and self.min_branch_age >= self.max_branch_spacing:
    raise ValueError(
        f"min_branch_age ({self.min_branch_age}) must be < "
        f"max_branch_spacing ({self.max_branch_spacing})"
    )
```

In `from_dict` (around line 457, after `min_branch_age`):

```python
max_branch_spacing=structure_section.get("max_branch_spacing", 4),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_config.py -v -k "max_branch_spacing"`
Expected: All 5 tests PASS.

- [ ] **Step 5: Audit existing tests for interference with default max_branch_spacing=4**

Run: `pytest -v`

If any existing tests fail because `max_branch_spacing=4` interferes with their expected topology, add `config.structure.max_branch_spacing = 0` to those tests to preserve their behavior. The default value changes the algorithm's behavior for any DAG that would have a branch going 4+ layers without a split — this may affect tests with small pools or deterministic seeds.

- [ ] **Step 6: Commit**

```bash
git add speedfog/config.py tests/test_config.py tests/test_generator.py
git commit -m "feat: add max_branch_spacing config field (default 4)"
```

---

### Task 2: Add `layers_since_last_split` to Branch

**Files:**
- Modify: `speedfog/dag.py:24-33` (Branch dataclass)
- Test: `tests/test_dag.py`

- [ ] **Step 1: Write failing test**

```python
# In tests/test_dag.py

from speedfog.dag import Branch, FogRef


def test_branch_layers_since_last_split_default():
    """Branch.layers_since_last_split defaults to 0."""
    branch = Branch("b0", "start", FogRef("x", "z"))
    assert branch.layers_since_last_split == 0


def test_branch_layers_since_last_split_custom():
    """Branch.layers_since_last_split can be set."""
    branch = Branch("b0", "start", FogRef("x", "z"), layers_since_last_split=3)
    assert branch.layers_since_last_split == 3
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_dag.py -v -k "layers_since_last_split"`
Expected: FAIL — `Branch` doesn't accept `layers_since_last_split`.

- [ ] **Step 3: Implement field**

In `speedfog/dag.py`, add to `Branch` dataclass (after `birth_layer` line 33):

```python
layers_since_last_split: int = 0  # Layers since last split on this branch
```

- [ ] **Step 4: Run full test suite for regression**

Run: `pytest -v`
Expected: All existing tests still pass (new field has default value).

- [ ] **Step 5: Commit**

```bash
git add speedfog/dag.py tests/test_dag.py
git commit -m "feat: add layers_since_last_split counter to Branch"
```

---

### Task 3: Propagate counter in helpers

The helpers `execute_passant_layer` and `execute_merge_layer` create new `Branch` objects each layer. They already propagate `birth_layer` — we must also propagate `layers_since_last_split`. Without this, the counter resets to 0 every time a helper is called.

**Files:**
- Modify: `speedfog/generator.py:620-691` (execute_passant_layer)
- Modify: `speedfog/generator.py:751-948` (execute_merge_layer)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing tests**

```python
# In tests/test_generator.py

def test_execute_passant_layer_carries_counter():
    """execute_passant_layer preserves layers_since_last_split."""
    dag = Dag(seed=1)
    start = DagNode(
        id="start", cluster=make_cluster("s", zones=["sz"], cluster_type="start",
            entry_fogs=[], exit_fogs=[{"fog_id": "sx", "zone": "sz"}]),
        layer=0, tier=1,
        entry_fogs=[], exit_fogs=[FogRef("sx", "sz")],
    )
    dag.add_node(start)
    dag.start_id = "start"

    branches = [
        Branch("b0", "start", FogRef("sx", "sz"), layers_since_last_split=5),
    ]
    passant_cluster = make_cluster(
        "p1", zones=["p1z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "p1e", "zone": "p1z"}],
        exit_fogs=[{"fog_id": "p1x", "zone": "p1z"}],
    )
    pool = ClusterPool([passant_cluster])
    used_zones: set[str] = {"sz"}

    result = execute_passant_layer(
        dag, branches, 1, 1, "mini_dungeon", pool, used_zones, random.Random(42),
    )
    assert result[0].layers_since_last_split == 5


def test_execute_merge_layer_carries_counter():
    """execute_merge_layer: merged branch gets max(sources), passant gets carry."""
    dag = Dag(seed=1)
    n0 = DagNode(
        id="n0", cluster=make_cluster("c0", zones=["z0"],
            entry_fogs=[{"fog_id": "e0", "zone": "z0"}],
            exit_fogs=[{"fog_id": "x0", "zone": "z0"}]),
        layer=0, tier=1,
        entry_fogs=[], exit_fogs=[FogRef("x0", "z0")],
    )
    n1 = DagNode(
        id="n1", cluster=make_cluster("c1", zones=["z1"],
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[{"fog_id": "x1", "zone": "z1"}]),
        layer=0, tier=1,
        entry_fogs=[], exit_fogs=[FogRef("x1", "z1")],
    )
    n2 = DagNode(
        id="n2", cluster=make_cluster("c2", zones=["z2"],
            entry_fogs=[{"fog_id": "e2", "zone": "z2"}],
            exit_fogs=[{"fog_id": "x2", "zone": "z2"}]),
        layer=0, tier=1,
        entry_fogs=[], exit_fogs=[FogRef("x2", "z2")],
    )
    dag.add_node(n0)
    dag.add_node(n1)
    dag.add_node(n2)

    branches = [
        Branch("b0", "n0", FogRef("x0", "z0"), layers_since_last_split=3),
        Branch("b1", "n1", FogRef("x1", "z1"), layers_since_last_split=7),
        Branch("b2", "n2", FogRef("x2", "z2"), layers_since_last_split=2),
    ]
    merge_cluster = make_cluster(
        "mg", zones=["mgz"], cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mge1", "zone": "mgz"},
            {"fog_id": "mge2", "zone": "mgz"},
        ],
        exit_fogs=[{"fog_id": "mgx", "zone": "mgz"}],
        allow_shared_entrance=True,
    )
    passant_cluster = make_cluster(
        "pc", zones=["pcz"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "pce", "zone": "pcz"}],
        exit_fogs=[{"fog_id": "pcx", "zone": "pcz"}],
    )
    pool = ClusterPool([merge_cluster, passant_cluster])
    used_zones: set[str] = {"z0", "z1", "z2"}
    config = Config()
    config.structure.max_branch_spacing = 0  # Disabled, just testing carry

    result = execute_merge_layer(
        dag, branches, 1, 1, "mini_dungeon", pool, used_zones,
        random.Random(42), config,
    )
    # Result: [merged_branch, passant_for_b2]
    assert len(result) == 2
    # Merged branch gets max(3, 7) = 7
    assert result[0].layers_since_last_split == 7
    # Non-merged branch carries its counter
    assert result[1].layers_since_last_split == 2
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_generator.py -v -k "test_execute_passant_layer_carries or test_execute_merge_layer_carries"`
Expected: FAIL — counters are 0 (default) instead of carried values.

- [ ] **Step 3: Implement counter propagation in `execute_passant_layer`**

In `speedfog/generator.py`, `execute_passant_layer` line 682-689, add `layers_since_last_split` to the new Branch construction:

```python
        new_branches.append(
            Branch(
                branch.id,
                node_id,
                rng.choice(exit_fogs),
                birth_layer=branch.birth_layer,
                layers_since_last_split=branch.layers_since_last_split,
            )
        )
```

- [ ] **Step 4: Implement counter propagation in `execute_merge_layer`**

Two places in `execute_merge_layer`:

**Merged branch (line 892-899):** Set counter to `max` of merged source branches:

```python
    merged_counter = max(b.layers_since_last_split for b in merge_branches)
    new_branches.append(
        Branch(
            f"merged_{layer_idx}",
            merge_node_id,
            rng.choice(exit_fogs),
            birth_layer=layer_idx,
            layers_since_last_split=merged_counter,
        )
    )
```

**Non-merged passant branches (line 938-945):** Carry the counter:

```python
        new_branches.append(
            Branch(
                branch.id,
                node_id,
                rng.choice(exit_fogs),
                birth_layer=branch.birth_layer,
                layers_since_last_split=branch.layers_since_last_split,
            )
        )
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pytest tests/test_generator.py -v -k "test_execute_passant_layer_carries or test_execute_merge_layer_carries"`
Expected: PASS.

- [ ] **Step 6: Run full test suite**

Run: `pytest -v`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: propagate layers_since_last_split in helper functions"
```

---

### Task 4: Add `force` parameter to `determine_operation`

**Files:**
- Modify: `speedfog/generator.py:489-561` (determine_operation)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing tests**

```python
# In tests/test_generator.py, add to the DetermineOperationTests class

def test_force_split_overrides_probability(self):
    """force=SPLIT bypasses probability roll when cluster can split."""
    cluster = make_cluster(
        "c1",
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
            {"fog_id": "x3", "zone": "z1"},
        ],
    )
    config = Config()
    config.structure.split_probability = 0.0  # Would normally never split
    config.structure.max_parallel_paths = 3
    branches = [Branch("b0", "start", FogRef("x", "z"))]
    op, fan = determine_operation(
        cluster, branches, config, random.Random(42),
        force=LayerOperation.SPLIT,
    )
    assert op == LayerOperation.SPLIT
    assert fan >= 2

def test_force_split_fallback_when_cant_split(self):
    """force=SPLIT falls back to normal logic when cluster can't split."""
    # Cluster with 1 exit — can't split
    cluster = make_cluster(
        "c1",
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
    )
    config = Config()
    config.structure.split_probability = 0.0
    branches = [Branch("b0", "start", FogRef("x", "z"))]
    op, fan = determine_operation(
        cluster, branches, config, random.Random(42),
        force=LayerOperation.SPLIT,
    )
    assert op == LayerOperation.PASSANT

def test_force_merge_overrides_probability(self):
    """force=MERGE bypasses probability roll when cluster can merge."""
    cluster = make_cluster(
        "c1",
        entry_fogs=[
            {"fog_id": "e1", "zone": "z1"},
            {"fog_id": "e2", "zone": "z1"},
        ],
        exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
        allow_shared_entrance=True,
    )
    config = Config()
    config.structure.merge_probability = 0.0  # Would normally never merge
    config.structure.max_parallel_paths = 3
    branches = [
        Branch("b0", "n0", FogRef("x", "z")),
        Branch("b1", "n1", FogRef("y", "z")),
    ]
    op, fan = determine_operation(
        cluster, branches, config, random.Random(42),
        force=LayerOperation.MERGE,
    )
    assert op == LayerOperation.MERGE

def test_force_none_uses_normal_logic(self):
    """force=None (default) uses normal probability logic."""
    cluster = make_cluster(
        "c1",
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
            {"fog_id": "x3", "zone": "z1"},
        ],
    )
    config = Config()
    config.structure.split_probability = 0.0  # Never split
    config.structure.max_parallel_paths = 3
    branches = [Branch("b0", "start", FogRef("x", "z"))]
    op, fan = determine_operation(
        cluster, branches, config, random.Random(42),
    )
    assert op == LayerOperation.PASSANT
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_generator.py -v -k "test_force_"`
Expected: FAIL — `determine_operation` doesn't accept `force` parameter.

- [ ] **Step 3: Implement force parameter**

In `speedfog/generator.py`, modify `determine_operation` signature:

```python
def determine_operation(
    cluster: ClusterData,
    branches: list[Branch],
    config: Config,
    rng: random.Random,
    *,
    current_layer: int = 0,
    force: LayerOperation | None = None,
) -> tuple[LayerOperation, int]:
```

After computing `can_split` and `can_merge` (before the existing decision block), insert:

```python
    # Forced operation: bypass probability when spacing threshold exceeded
    if force == LayerOperation.SPLIT and can_split:
        return LayerOperation.SPLIT, split_fan
    if force == LayerOperation.MERGE and can_merge:
        return LayerOperation.MERGE, 2
```

The rest of the function stays unchanged as fallback.

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_generator.py -v -k "test_force_"`
Expected: All 4 tests PASS.

- [ ] **Step 5: Run full test suite for regression**

Run: `pytest -v`
Expected: All existing tests pass (new parameter has default `None`).

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: add force parameter to determine_operation"
```

---

## Chunk 2: Main Loop Integration

### Task 5: Counter updates + forced split in the main loop

This is the core task. It adds counter tracking and forced split/merge logic to the main generation loop.

**Files:**
- Modify: `speedfog/generator.py:1293-1595` (main loop)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write `update_branch_counters` helper tests**

```python
# In tests/test_generator.py

from speedfog.generator import update_branch_counters


def test_counter_update_split():
    """Split children get 0, other branches get +1."""
    split_children = [
        Branch("b0_a", "split", FogRef("a", "z"), layers_since_last_split=999),
        Branch("b0_b", "split", FogRef("b", "z"), layers_since_last_split=999),
    ]
    passant_branches = [
        Branch("b1", "n2", FogRef("y2", "z"), layers_since_last_split=2),
    ]
    update_branch_counters(
        LayerOperation.SPLIT,
        split_children=split_children,
        passant_branches=passant_branches,
    )
    assert [b.layers_since_last_split for b in split_children] == [0, 0]
    assert passant_branches[0].layers_since_last_split == 3


def test_counter_update_passant():
    """All branches get +1."""
    branches = [
        Branch("b0", "n0", FogRef("x", "z"), layers_since_last_split=2),
        Branch("b1", "n1", FogRef("y", "z"), layers_since_last_split=5),
    ]
    update_branch_counters(
        LayerOperation.PASSANT,
        passant_branches=branches,
    )
    assert [b.layers_since_last_split for b in branches] == [3, 6]


def test_counter_update_merge():
    """Merged branch gets max(sources), passant branches get +1."""
    merged = Branch("merged", "m", FogRef("x", "z"), layers_since_last_split=999)
    passant = [
        Branch("b2", "n2", FogRef("y", "z"), layers_since_last_split=1),
    ]
    merge_sources = [
        Branch("b0", "n0", FogRef("a", "z"), layers_since_last_split=3),
        Branch("b1", "n1", FogRef("b", "z"), layers_since_last_split=7),
    ]
    update_branch_counters(
        LayerOperation.MERGE,
        passant_branches=passant,
        merged_branches=(merged, merge_sources),
    )
    assert merged.layers_since_last_split == 7
    assert passant[0].layers_since_last_split == 2
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_generator.py -v -k "test_counter_update_"`
Expected: FAIL — `update_branch_counters` doesn't exist.

- [ ] **Step 3: Implement `update_branch_counters` helper**

In `speedfog/generator.py`, add near the other helpers (around line 620):

```python
def update_branch_counters(
    operation: LayerOperation,
    *,
    split_children: list[Branch] | None = None,
    passant_branches: list[Branch] | None = None,
    merged_branches: tuple[Branch, list[Branch]] | None = None,
) -> None:
    """Update layers_since_last_split counters in-place after an operation.

    Args:
        operation: The operation that was performed.
        split_children: New branches created by a split (counter set to 0).
        passant_branches: Branches that did passant this layer (counter += 1).
        merged_branches: Tuple of (merged_branch, source_branches) for merge.
            merged_branch gets max(sources). passant_branches get += 1.
    """
    if operation == LayerOperation.SPLIT:
        for b in (split_children or []):
            b.layers_since_last_split = 0
        for b in (passant_branches or []):
            b.layers_since_last_split += 1

    elif operation == LayerOperation.MERGE:
        if merged_branches is not None:
            merged, sources = merged_branches
            merged.layers_since_last_split = max(
                s.layers_since_last_split for s in sources
            )
        for b in (passant_branches or []):
            b.layers_since_last_split += 1

    elif operation == LayerOperation.PASSANT:
        for b in (passant_branches or []):
            b.layers_since_last_split += 1
```

Note: pure mutation, no return value. Callers already hold references to the branch objects.

- [ ] **Step 4: Run counter update tests**

Run: `pytest tests/test_generator.py -v -k "test_counter_update_"`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: add update_branch_counters helper for stale tracking"
```

- [ ] **Step 6: Write failing end-to-end test for forced split**

```python
# In tests/test_generator.py

def test_forced_split_triggers_at_threshold():
    """A branch exceeding max_branch_spacing gets a forced split."""
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[], exit_fogs=[{"fog_id": "s_x1", "zone": "start_z"}],
    )
    passant1 = make_cluster(
        "p1", zones=["p1_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "p1_e", "zone": "p1_z"}],
        exit_fogs=[{"fog_id": "p1_x", "zone": "p1_z"}],
    )
    passant2 = make_cluster(
        "p2", zones=["p2_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "p2_e", "zone": "p2_z"}],
        exit_fogs=[{"fog_id": "p2_x", "zone": "p2_z"}],
    )
    splittable = make_cluster(
        "sp1", zones=["sp1_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "sp1_e", "zone": "sp1_z"}],
        exit_fogs=[
            {"fog_id": "sp1_x1", "zone": "sp1_z"},
            {"fog_id": "sp1_x2", "zone": "sp1_z"},
        ],
    )
    passant3 = make_cluster(
        "p3", zones=["p3_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "p3_e", "zone": "p3_z"}],
        exit_fogs=[{"fog_id": "p3_x", "zone": "p3_z"}],
    )
    passant4 = make_cluster(
        "p4", zones=["p4_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "p4_e", "zone": "p4_z"}],
        exit_fogs=[{"fog_id": "p4_x", "zone": "p4_z"}],
    )
    merge_node = make_cluster(
        "mg1", zones=["mg1_z"], cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mg1_e1", "zone": "mg1_z"},
            {"fog_id": "mg1_e2", "zone": "mg1_z"},
        ],
        exit_fogs=[{"fog_id": "mg1_x", "zone": "mg1_z"}],
        allow_shared_entrance=True,
    )
    boss = make_cluster(
        "boss1", zones=["boss_z"], cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool([start, passant1, passant2, splittable,
                        passant3, passant4, merge_node, boss])

    config = Config()
    config.structure.max_branch_spacing = 2
    config.structure.split_probability = 0.0  # Would never split naturally
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 3
    config.structure.min_layers = 5
    config.structure.max_layers = 5
    # Match requirements to pool contents
    config.requirements.mini_dungeons = 5
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    dag = generate_dag(config, pool, _boss_candidates(pool))

    # The DAG should have a split somewhere despite split_probability=0
    paths = dag.enumerate_paths()
    assert len(paths) >= 2, "Expected at least 2 paths due to forced split"
```

- [ ] **Step 7: Run test to verify it fails**

Run: `pytest tests/test_generator.py::test_forced_split_triggers_at_threshold -v`
Expected: FAIL — only 1 path (no splits because `split_probability=0.0`).

- [ ] **Step 8: Implement forced split/merge logic in main loop**

In `speedfog/generator.py`, in the main loop (around line 1293), after the `is_near_end` check and before `pick_cluster_with_type_fallback`:

```python
        # --- Max branch spacing enforcement ---
        max_spacing = config.structure.max_branch_spacing
        force_op: LayerOperation | None = None

        if max_spacing > 0 and not is_near_end:
            max_stale = max(b.layers_since_last_split for b in branches)
            needs_forced_split = max_stale >= max_spacing

            if needs_forced_split:
                if len(branches) >= config.structure.max_parallel_paths:
                    # Case 2: saturated — use execute_forced_merge to free a slot.
                    # This handles same-parent divergence and min_age bypass.
                    # Save counters before merge (execute_forced_merge creates new Branch objects).
                    old_counters = {b.id: b.layers_since_last_split for b in branches}
                    branches, current_layer = execute_forced_merge(
                        dag, branches, current_layer, tier,
                        layer_type, clusters, used_zones, rng, config,
                        reserved_zones=reserved_zones,
                    )
                    # Restore counters: merged branch gets max of sources,
                    # but execute_forced_merge may have consumed multiple layers.
                    # Since all branches merged to one, set counter to max of all old counters.
                    max_old = max(old_counters.values())
                    branches[0].layers_since_last_split = max_old
                    continue  # Re-enter loop — now only 1 branch, room to split
                else:
                    # Case 1: room to split — force split if cluster supports it
                    force_op = LayerOperation.SPLIT
```

Then pass `force_op` to `determine_operation`:

```python
        operation, fan = determine_operation(
            primary_cluster, branches, config, rng,
            current_layer=current_layer, force=force_op,
        )
```

**For forced split target branch selection** — modify the split execution block. Replace `split_idx = rng.randrange(len(branches))` with:

```python
            if force_op == LayerOperation.SPLIT:
                # Pick the most stale branch (ties broken randomly)
                max_stale_val = max(b.layers_since_last_split for b in branches)
                stale_indices = [
                    i for i, b in enumerate(branches)
                    if b.layers_since_last_split == max_stale_val
                ]
                split_idx = rng.choice(stale_indices)
            else:
                split_idx = rng.randrange(len(branches))
```

**Wire counter updates after each operation in the main loop.**

Since helpers now carry the counter (Task 3), the new Branch objects in the main loop must also carry it. Add `layers_since_last_split=branch.layers_since_last_split` to every `Branch(...)` constructor in the main loop that creates a passant branch (SPLIT else-block line ~1400, MERGE non-merged block line ~1542, PASSANT block line ~1588).

Then call `update_branch_counters` after each operation:

**After SPLIT (line ~1410):** Separate split children from passant branches using two accumulators (`split_child_branches`, `passant_branches_list`), then:
```python
update_branch_counters(
    LayerOperation.SPLIT,
    split_children=split_child_branches,
    passant_branches=passant_branches_list,
)
branches = split_child_branches + passant_branches_list
```

**After MERGE (line ~1552):** `merge_branches_list` (old branches) still has the original counters:
```python
update_branch_counters(
    LayerOperation.MERGE,
    merged_branches=(new_branches[0], merge_branches_list),
    passant_branches=new_branches[1:],
)
branches = new_branches
```

**After PASSANT (line ~1593):**
```python
update_branch_counters(
    LayerOperation.PASSANT,
    passant_branches=new_branches,
)
branches = new_branches
```

- [ ] **Step 9: Run test to verify it passes**

Run: `pytest tests/test_generator.py::test_forced_split_triggers_at_threshold -v`
Expected: PASS.

- [ ] **Step 10: Run full test suite**

Run: `pytest -v`
Expected: All tests pass.

- [ ] **Step 11: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: enforce max_branch_spacing with forced split/merge in main loop"
```

---

## Chunk 3: Edge Cases + Statistical Validation

### Task 6: Saturated case test

**Files:**
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write test for saturated forced merge**

```python
# In tests/test_generator.py

def test_forced_merge_when_saturated():
    """When max_parallel_paths is reached, force merge before split."""
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    passants = [
        make_cluster(
            f"p{i}", zones=[f"p{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"p{i}_e", "zone": f"p{i}_z"}],
            exit_fogs=[{"fog_id": f"p{i}_x", "zone": f"p{i}_z"}],
        )
        for i in range(12)  # Extra clusters for forced merge overhead
    ]
    merge_node = make_cluster(
        "mg1", zones=["mg1_z"], cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mg1_e1", "zone": "mg1_z"},
            {"fog_id": "mg1_e2", "zone": "mg1_z"},
        ],
        exit_fogs=[{"fog_id": "mg1_x", "zone": "mg1_z"}],
        allow_shared_entrance=True,
    )
    merge_node2 = make_cluster(
        "mg2", zones=["mg2_z"], cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mg2_e1", "zone": "mg2_z"},
            {"fog_id": "mg2_e2", "zone": "mg2_z"},
        ],
        exit_fogs=[{"fog_id": "mg2_x", "zone": "mg2_z"}],
        allow_shared_entrance=True,
    )
    splittable = make_cluster(
        "sp1", zones=["sp1_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "sp1_e", "zone": "sp1_z"}],
        exit_fogs=[
            {"fog_id": "sp1_x1", "zone": "sp1_z"},
            {"fog_id": "sp1_x2", "zone": "sp1_z"},
        ],
    )
    boss = make_cluster(
        "boss1", zones=["boss_z"], cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool(
        [start] + passants + [merge_node, merge_node2, splittable, boss]
    )

    config = Config()
    config.structure.max_branch_spacing = 3
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 2  # Start is already at max
    config.structure.min_layers = 6
    config.structure.max_layers = 12  # Extra room for forced merge overhead
    # Match requirements to pool
    config.requirements.mini_dungeons = 6
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    dag = generate_dag(config, pool, _boss_candidates(pool))
    assert dag.end_id  # DAG completed successfully
```

- [ ] **Step 2: Run test**

Run: `pytest tests/test_generator.py::test_forced_merge_when_saturated -v`
Expected: PASS.

- [ ] **Step 3: Write test for forced merge bypassing min_branch_age**

```python
def test_forced_merge_bypasses_min_branch_age():
    """Forced merge for spacing ignores min_branch_age."""
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    passants = [
        make_cluster(
            f"p{i}", zones=[f"p{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"p{i}_e", "zone": f"p{i}_z"}],
            exit_fogs=[{"fog_id": f"p{i}_x", "zone": f"p{i}_z"}],
        )
        for i in range(12)
    ]
    merge_node = make_cluster(
        "mg1", zones=["mg1_z"], cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mg1_e1", "zone": "mg1_z"},
            {"fog_id": "mg1_e2", "zone": "mg1_z"},
        ],
        exit_fogs=[{"fog_id": "mg1_x", "zone": "mg1_z"}],
        allow_shared_entrance=True,
    )
    merge_node2 = make_cluster(
        "mg2", zones=["mg2_z"], cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mg2_e1", "zone": "mg2_z"},
            {"fog_id": "mg2_e2", "zone": "mg2_z"},
        ],
        exit_fogs=[{"fog_id": "mg2_x", "zone": "mg2_z"}],
        allow_shared_entrance=True,
    )
    splittable = make_cluster(
        "sp1", zones=["sp1_z"], cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "sp1_e", "zone": "sp1_z"}],
        exit_fogs=[
            {"fog_id": "sp1_x1", "zone": "sp1_z"},
            {"fog_id": "sp1_x2", "zone": "sp1_z"},
        ],
    )
    boss = make_cluster(
        "boss1", zones=["boss_z"], cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool(
        [start] + passants + [merge_node, merge_node2, splittable, boss]
    )

    config = Config()
    config.structure.max_branch_spacing = 4
    config.structure.min_branch_age = 3  # High but < max_branch_spacing
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 2
    config.structure.min_layers = 6
    config.structure.max_layers = 12
    config.requirements.mini_dungeons = 6
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    # Should succeed — forced merge bypasses min_branch_age
    dag = generate_dag(config, pool, _boss_candidates(pool))
    assert dag.end_id
```

- [ ] **Step 4: Run tests**

Run: `pytest tests/test_generator.py -v -k "test_forced_merge"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add saturated and min_branch_age bypass tests"
```

---

### Task 7: Statistical validation + regression

**Files:**
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write statistical test**

This test requires `data/clusters.json` (skipped if missing).

```python
# In tests/test_generator.py

import os

@pytest.mark.skipif(
    not os.path.exists("data/clusters.json"),
    reason="Requires data/clusters.json",
)
def test_max_branch_spacing_statistical():
    """No branch exceeds max_branch_spacing + 2 across many seeds."""
    from speedfog.clusters import ClusterPool

    pool = ClusterPool.from_json("data/clusters.json")
    boss_candidates = _boss_candidates(pool)
    max_spacing = 4
    violations = []

    for seed in range(50):
        config = Config()
        config.seed = seed
        config.structure.max_branch_spacing = max_spacing
        try:
            dag = generate_dag(config, pool, boss_candidates)
        except GenerationError:
            continue

        max_observed = _measure_max_branch_spacing(dag)
        if max_observed > max_spacing + 2:
            violations.append((seed, max_observed))

    assert not violations, (
        f"Branches exceeded max_branch_spacing + 2: {violations}"
    )


def _measure_max_branch_spacing(dag: Dag) -> int:
    """Measure the maximum layers-since-last-split across all paths.

    Walk every path from start to end. A split is a node with 2+
    outgoing edges to different targets. Counter resets at split nodes,
    increments at non-split nodes.
    """
    max_spacing = 0

    for path in dag.enumerate_paths():
        since_last_split = 0
        for node_id in path:
            outgoing = dag.get_outgoing_edges(node_id)
            targets = {e.target_id for e in outgoing}
            if len(targets) >= 2:
                # This is a split point — counter resets for next node
                since_last_split = 0
            else:
                since_last_split += 1
            max_spacing = max(max_spacing, since_last_split)

    return max_spacing
```

- [ ] **Step 2: Write regression test for disabled mode**

```python
def test_max_branch_spacing_disabled():
    """max_branch_spacing=0 produces same behavior as before the feature."""
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[], exit_fogs=[{"fog_id": "s_x1", "zone": "start_z"}],
    )
    passants = [
        make_cluster(
            f"p{i}", zones=[f"p{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"p{i}_e", "zone": f"p{i}_z"}],
            exit_fogs=[{"fog_id": f"p{i}_x", "zone": f"p{i}_z"}],
        )
        for i in range(6)
    ]
    boss = make_cluster(
        "boss1", zones=["boss_z"], cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool([start] + passants + [boss])

    config = Config()
    config.structure.max_branch_spacing = 0  # Disabled
    config.structure.split_probability = 1.0
    config.structure.min_layers = 4
    config.structure.max_layers = 4
    config.requirements.mini_dungeons = 3
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    dag = generate_dag(config, pool, _boss_candidates(pool))
    paths = dag.enumerate_paths()
    assert len(paths) == 1  # Linear, no splits possible
```

- [ ] **Step 3: Run tests**

Run: `pytest tests/test_generator.py -v -k "test_max_branch_spacing"`
Expected: PASS (statistical may be SKIPPED without clusters.json).

- [ ] **Step 4: Run full test suite**

Run: `pytest -v`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add statistical validation and disabled mode regression"
```

---

### Task 8: Update config example and documentation

**Files:**
- Modify: `config.example.toml`
- Modify: `docs/specs/2026-03-11-max-branch-spacing-design.md` (status → Implemented)

- [ ] **Step 1: Add to config.example.toml**

Add under `[structure]`:

```toml
# max_branch_spacing = 4  # Max layers a branch can go without a split (0=disabled)
```

- [ ] **Step 2: Update spec status**

Change `**Status:** Draft` to `**Status:** Implemented` in the spec file.

- [ ] **Step 3: Commit**

```bash
git add config.example.toml docs/specs/2026-03-11-max-branch-spacing-design.md
git commit -m "docs: update config example and spec status for max_branch_spacing"
```
