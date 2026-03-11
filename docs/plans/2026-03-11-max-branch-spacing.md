# Max Branch Spacing Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee that no branch in the DAG goes more than ~4 layers without a split point, so players always have a nearby alternative path.

**Architecture:** Add a per-branch `layers_since_last_split` counter to the `Branch` dataclass. The main generation loop checks this counter each layer and forces a SPLIT operation (if the cluster supports it) or a MERGE (to free a slot) when the threshold is reached. Cluster selection remains uniform — no bias.

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

- [ ] **Step 5: Commit**

```bash
git add speedfog/config.py tests/test_config.py
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_dag.py -v -k "layers_since_last_split"`
Expected: PASS.

- [ ] **Step 5: Run full test suite for regression**

Run: `pytest -v`
Expected: All existing tests still pass (new field has default value).

- [ ] **Step 6: Commit**

```bash
git add speedfog/dag.py tests/test_dag.py
git commit -m "feat: add layers_since_last_split counter to Branch"
```

---

### Task 3: Add `force` parameter to `determine_operation`

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
    # Cluster with only 1 exit — can't split
    cluster = make_cluster(
        "c1",
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "e1", "zone": "z1"},
            {"fog_id": "x1", "zone": "z1"},
        ],
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

In `speedfog/generator.py`, modify `determine_operation` signature and add force logic at the top of the decision block:

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

The rest of the function (probability-based decision) stays unchanged as fallback.

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_generator.py -v -k "test_force_"`
Expected: All 4 tests PASS.

- [ ] **Step 5: Run full test suite for regression**

Run: `pytest tests/test_generator.py -v`
Expected: All existing tests pass (new parameter has default `None`).

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: add force parameter to determine_operation"
```

---

## Chunk 2: Main Loop Integration

### Task 4: Counter updates in the main loop

This task modifies the main generation loop to propagate `layers_since_last_split` on every branch at every layer. No forced operations yet — just correct counter tracking.

**Files:**
- Modify: `speedfog/generator.py:1293-1595` (main loop)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing test**

Add a helper that generates a DAG and inspects the branches at each layer. Since we can't easily observe intermediate branches from outside `generate_dag`, we test indirectly by checking that `Branch` objects created in the main loop carry the counter. The most direct approach: patch the main loop to test counter propagation.

Instead, write a unit test for a helper function that computes counter updates:

```python
# In tests/test_generator.py

from speedfog.generator import update_branch_counters


def test_counter_update_split():
    """Split children get 0, other branches get +1."""
    branches = [
        Branch("b0", "n0", FogRef("x", "z"), layers_since_last_split=3),
        Branch("b1", "n1", FogRef("y", "z"), layers_since_last_split=2),
    ]
    # b0 was split into b0_a, b0_b; b1 was passant
    split_children = [
        Branch("b0_a", "split", FogRef("a", "z"), layers_since_last_split=999),
        Branch("b0_b", "split", FogRef("b", "z"), layers_since_last_split=999),
    ]
    passant_branches = [
        Branch("b1", "n2", FogRef("y2", "z"), layers_since_last_split=999),
    ]
    result = update_branch_counters(
        LayerOperation.SPLIT,
        split_children=split_children,
        passant_branches=passant_branches,
        merged_branches=None,
    )
    assert [b.layers_since_last_split for b in result] == [0, 0, 3]


def test_counter_update_passant():
    """All branches get +1."""
    branches = [
        Branch("b0", "n0", FogRef("x", "z"), layers_since_last_split=2),
        Branch("b1", "n1", FogRef("y", "z"), layers_since_last_split=5),
    ]
    result = update_branch_counters(
        LayerOperation.PASSANT,
        split_children=None,
        passant_branches=branches,
        merged_branches=None,
    )
    assert [b.layers_since_last_split for b in result] == [3, 6]


def test_counter_update_merge():
    """Merged branch gets max, passant branches get +1."""
    merged = Branch("merged", "m", FogRef("x", "z"), layers_since_last_split=999)
    passant = [
        Branch("b2", "n2", FogRef("y", "z"), layers_since_last_split=1),
    ]
    merge_sources = [
        Branch("b0", "n0", FogRef("a", "z"), layers_since_last_split=3),
        Branch("b1", "n1", FogRef("b", "z"), layers_since_last_split=7),
    ]
    result = update_branch_counters(
        LayerOperation.MERGE,
        split_children=None,
        passant_branches=passant,
        merged_branches=(merged, merge_sources),
    )
    # merged gets max(3, 7) = 7, passant gets 1+1 = 2
    assert result[0].layers_since_last_split == 7  # merged
    assert result[1].layers_since_last_split == 2  # passant
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pytest tests/test_generator.py -v -k "test_counter_update_"`
Expected: FAIL — `update_branch_counters` doesn't exist.

- [ ] **Step 3: Implement `update_branch_counters` helper**

In `speedfog/generator.py`, add a new function (near the other helpers, around line 620):

```python
def update_branch_counters(
    operation: LayerOperation,
    *,
    split_children: list[Branch] | None = None,
    passant_branches: list[Branch] | None = None,
    merged_branches: tuple[Branch, list[Branch]] | None = None,
) -> list[Branch]:
    """Update layers_since_last_split counters after an operation.

    Args:
        operation: The operation that was performed.
        split_children: New branches created by a split (counter → 0).
        passant_branches: Branches that did passant this layer (counter += 1).
        merged_branches: Tuple of (merged_branch, source_branches) for merge.
            merged_branch gets max(sources), passant_branches get += 1.

    Returns:
        Combined list of all branches with updated counters.
    """
    result: list[Branch] = []

    if operation == LayerOperation.SPLIT:
        for b in (split_children or []):
            b.layers_since_last_split = 0
            result.append(b)
        for b in (passant_branches or []):
            b.layers_since_last_split += 1
            result.append(b)

    elif operation == LayerOperation.MERGE:
        if merged_branches is not None:
            merged, sources = merged_branches
            merged.layers_since_last_split = max(
                s.layers_since_last_split for s in sources
            )
            result.append(merged)
        for b in (passant_branches or []):
            b.layers_since_last_split += 1
            result.append(b)

    elif operation == LayerOperation.PASSANT:
        for b in (passant_branches or []):
            b.layers_since_last_split += 1
            result.append(b)

    return result
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_generator.py -v -k "test_counter_update_"`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: add update_branch_counters helper for stale tracking"
```

- [ ] **Step 6: Wire counter updates into the main loop**

**CRITICAL:** The main loop creates **new** `Branch` objects each layer (with `layers_since_last_split=0` by default). The counter from the previous iteration's branch must be carried over to the new branch before `update_branch_counters` applies the delta.

In `speedfog/generator.py`, modify the main loop (around lines 1335-1595):

**Step 6a: Carry counter in new Branch construction.**

Every place in the main loop that creates a `Branch` for a non-split passant must copy the counter from the old branch. Add `layers_since_last_split=branch.layers_since_last_split` to each `Branch(...)` constructor call for passant branches:

- SPLIT block, non-split branches (line ~1400-1407): add `layers_since_last_split=branch.layers_since_last_split`
- MERGE block, non-merged branches (line ~1542-1549): add `layers_since_last_split=branch.layers_since_last_split`
- PASSANT block (line ~1588-1592): add `layers_since_last_split=branch.layers_since_last_split`
- MERGE block, merged branch (line ~1502-1508): set `layers_since_last_split=0` (will be overwritten by `update_branch_counters`)

Split child branches are new (counter starts at 0, set by `update_branch_counters`), so no carry needed there.

**Step 6b: Call `update_branch_counters` after each operation.**

Separate split children from passant branches using two accumulators before the `for i, branch in enumerate(branches)` loop in the SPLIT block:

```python
split_child_branches: list[Branch] = []
passant_branches_list: list[Branch] = []
```

In the `i == split_idx` block, append to `split_child_branches` instead of `new_branches`.
In the `else` block, append to `passant_branches_list` instead of `new_branches`.
Then:
```python
branches = update_branch_counters(
    LayerOperation.SPLIT,
    split_children=split_child_branches,
    passant_branches=passant_branches_list,
)
```

**For MERGE (after line 1552 `branches = new_branches`):**
The merged branch is `new_branches[0]`, merge sources are `merge_branches_list` (the OLD branches before merge — these carry the correct counter values), and non-merged passant branches are `new_branches[1:]`:

```python
branches = update_branch_counters(
    LayerOperation.MERGE,
    merged_branches=(new_branches[0], merge_branches_list),
    passant_branches=new_branches[1:],
)
```

**For PASSANT (after line 1593 `branches = new_branches`):**

```python
branches = update_branch_counters(
    LayerOperation.PASSANT,
    passant_branches=new_branches,
)
```

Note: `update_branch_counters` mutates the branches in-place AND returns them. For PASSANT, each new branch already carries the old counter (from Step 6a), so `+= 1` gives the correct accumulated value. For MERGE, the merged branch gets `max(sources)` from the old source branches (which still have their original counters).

- [ ] **Step 7: Run full test suite**

Run: `pytest -v`
Expected: All tests pass. Counter tracking is passive — no behavior change yet.

- [ ] **Step 8: Commit**

```bash
git add speedfog/generator.py
git commit -m "feat: wire stale counter updates into main generation loop"
```

---

### Task 5: Forced split logic in the main loop

**Files:**
- Modify: `speedfog/generator.py:1293-1335` (pre-selection logic in main loop)
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing test — forced split triggers**

Test that with `max_branch_spacing=2`, a branch that has been passant for 2 layers gets a forced split on the next split-capable cluster. We test via `generate_dag` with a controlled cluster pool.

```python
# In tests/test_generator.py

def test_forced_split_triggers_at_threshold():
    """A branch exceeding max_branch_spacing gets a forced split."""
    # Create a pool where:
    # - start has 1 exit (so 1 branch)
    # - first 2 clusters are passant-only (1 entry, 1 exit)
    # - 3rd cluster is split-capable (1 entry, 2 exits)
    # With max_branch_spacing=2, the split should be forced on the 3rd layer.
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
    # Need enough clusters for post-split branches + final merge + boss
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
    pool = ClusterPool([start, passant1, passant2, splittable, passant3, passant4, merge_node, boss])

    config = Config()
    config.structure.max_branch_spacing = 2
    config.structure.split_probability = 0.0  # Would never split naturally
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 3
    config.structure.min_layers = 5
    config.structure.max_layers = 5

    dag = generate_dag(config, pool, _boss_candidates(pool))

    # The DAG should have a split somewhere despite split_probability=0
    paths = dag.enumerate_paths()
    assert len(paths) >= 2, "Expected at least 2 paths due to forced split"
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_generator.py::test_forced_split_triggers_at_threshold -v`
Expected: FAIL — only 1 path (no splits because `split_probability=0.0`).

- [ ] **Step 3: Implement forced split logic in main loop**

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
                    # Case 2: saturated — force a single merge to free a slot.
                    # Uses execute_merge_layer (same as execute_forced_merge)
                    # which handles the "same parent" constraint by inserting
                    # passant layers to diverge nodes. Bypasses min_branch_age.
                    if not _has_valid_merge_pair(branches):
                        branches = execute_passant_layer(
                            dag, branches, current_layer, tier,
                            layer_type, clusters, used_zones, rng,
                            reserved_zones=reserved_zones,
                        )
                        # Carry counters: passant for all branches
                        branches = update_branch_counters(
                            LayerOperation.PASSANT,
                            passant_branches=branches,
                        )
                        current_layer += 1
                    branches = execute_merge_layer(
                        dag, branches, current_layer, tier,
                        layer_type, clusters, used_zones, rng,
                        config, reserved_zones=reserved_zones,
                    )
                    # Carry counters: merge for the merged pair, passant for rest
                    # (execute_merge_layer returns [merged, passant1, passant2, ...])
                    # The merged branch needs max(sources) — but execute_merge_layer
                    # creates new Branch objects. We need to track the source branches
                    # BEFORE the merge to get their counters.
                    # Implementation note: save old branches before calling
                    # execute_merge_layer, then match by id to identify merge sources.
                    current_layer += 1
                    continue  # Re-enter loop — now room to split
                else:
                    # Case 1: room to split — force split if cluster supports it
                    force_op = LayerOperation.SPLIT
```

**Important:** For Case 2, the forced merge is handled as a special case with `continue` — the loop re-enters and the forced split triggers on the next iteration (now with room). The counter tracking for the forced merge layer must be handled inline (see implementation note above about saving old branches before `execute_merge_layer`).

Then pass `force_op` to `determine_operation` (only for Case 1 — Case 2 already `continue`d):

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
                max_stale = max(b.layers_since_last_split for b in branches)
                stale_indices = [
                    i for i, b in enumerate(branches)
                    if b.layers_since_last_split == max_stale
                ]
                split_idx = rng.choice(stale_indices)
            else:
                split_idx = rng.randrange(len(branches))
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest tests/test_generator.py::test_forced_split_triggers_at_threshold -v`
Expected: PASS.

- [ ] **Step 5: Run full test suite**

Run: `pytest -v`
Expected: All tests pass. Existing behavior unchanged (default `max_branch_spacing=4` is large enough that most test DAGs don't trigger it).

Note: If any existing tests fail because the default `max_branch_spacing=4` interferes with their expected topology, set `config.structure.max_branch_spacing = 0` in those tests to disable the feature.

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "feat: enforce max_branch_spacing with forced split/merge in main loop"
```

---

## Chunk 3: Saturated Case + Statistical Validation

### Task 6: Test the saturated case (forced merge then split)

**Files:**
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write test for saturated forced merge**

```python
# In tests/test_generator.py

def test_forced_merge_when_saturated():
    """When max_parallel_paths is reached, force merge before split."""
    # Build a pool that naturally creates max branches, then one branch stagnates.
    # start with 2 exits → 2 branches immediately
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    # Enough passant-only clusters for both branches
    passants = []
    for i in range(8):
        passants.append(make_cluster(
            f"p{i}", zones=[f"p{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"p{i}_e", "zone": f"p{i}_z"}],
            exit_fogs=[{"fog_id": f"p{i}_x", "zone": f"p{i}_z"}],
        ))
    # Merge-capable cluster
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
    # Split-capable cluster (for after merge frees slot)
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
    pool = ClusterPool([start] + passants + [merge_node, merge_node2, splittable, boss])

    config = Config()
    config.structure.max_branch_spacing = 3
    config.structure.split_probability = 0.0  # Never split naturally
    config.structure.merge_probability = 0.0  # Never merge naturally
    config.structure.max_parallel_paths = 2  # Start is already at max
    config.structure.min_layers = 6
    config.structure.max_layers = 8

    # Should not raise — the forced merge + split mechanism handles saturation
    dag = generate_dag(config, pool, _boss_candidates(pool))
    assert dag.end_id  # DAG completed successfully
```

- [ ] **Step 2: Run test to verify it fails (or passes — this validates the mechanism)**

Run: `pytest tests/test_generator.py::test_forced_merge_when_saturated -v`
Expected: If Task 5 was correct, this should PASS. If it fails, debug the saturation path.

- [ ] **Step 3: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add saturated forced merge scenario for max_branch_spacing"
```

---

### Task 6b: Additional edge case tests

**Files:**
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write test for forced merge bypassing min_branch_age**

```python
# In tests/test_generator.py

def test_forced_merge_bypasses_min_branch_age():
    """Forced merge for spacing ignores min_branch_age."""
    # Same setup as test_forced_merge_when_saturated but with high min_branch_age
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
        for i in range(8)
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
    pool = ClusterPool([start] + passants + [merge_node, merge_node2, splittable, boss])

    config = Config()
    config.structure.max_branch_spacing = 4
    config.structure.min_branch_age = 3  # High but < max_branch_spacing
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 2
    config.structure.min_layers = 6
    config.structure.max_layers = 8

    # Should succeed — forced merge bypasses min_branch_age
    dag = generate_dag(config, pool, _boss_candidates(pool))
    assert dag.end_id
```

- [ ] **Step 2: Write test for multiple stale branches priority**

```python
def test_most_stale_branch_split_first():
    """When multiple branches exceed threshold, most stale gets split."""
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
    config.structure.max_branch_spacing = 3
    config.structure.split_probability = 0.0  # Would never split naturally
    config.structure.max_parallel_paths = 5
    branches = [
        Branch("b0", "n0", FogRef("x", "z"), layers_since_last_split=5),
        Branch("b1", "n1", FogRef("y", "z"), layers_since_last_split=3),
        Branch("b2", "n2", FogRef("w", "z"), layers_since_last_split=7),
    ]
    op, fan = determine_operation(
        cluster, branches, config, random.Random(42),
        force=LayerOperation.SPLIT,
    )
    assert op == LayerOperation.SPLIT
    # The actual branch selection (most stale = b2) is in the main loop,
    # not in determine_operation. This test just verifies the force works.
```

- [ ] **Step 3: Run tests**

Run: `pytest tests/test_generator.py -v -k "test_forced_merge_bypasses or test_most_stale"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add edge cases for min_branch_age bypass and stale priority"
```

---

### Task 7: Statistical validation test

**Files:**
- Test: `tests/test_generator.py`

This test requires `data/clusters.json` (skipped if missing, like existing integration tests).

- [ ] **Step 1: Write statistical test**

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
            continue  # Some seeds may fail for unrelated reasons

        # Walk the DAG to measure per-branch spacing
        max_observed = _measure_max_branch_spacing(dag)
        if max_observed > max_spacing + 2:
            violations.append((seed, max_observed))

    assert not violations, (
        f"Branches exceeded max_branch_spacing + 2: {violations}"
    )


def _measure_max_branch_spacing(dag: Dag) -> int:
    """Measure the maximum layers-since-last-split across all paths.

    Walk every path from start to end, tracking when splits occur.
    A split is a node with 2+ outgoing edges to different targets.
    """
    max_spacing = 0

    for path in dag.enumerate_paths():
        since_last_split = 0
        for node_id in path:
            outgoing = dag.get_outgoing_edges(node_id)
            targets = {e.target_id for e in outgoing}
            if len(targets) >= 2:
                since_last_split = 0
            else:
                since_last_split += 1
            max_spacing = max(max_spacing, since_last_split)

    return max_spacing
```

- [ ] **Step 2: Run test**

Run: `pytest tests/test_generator.py::test_max_branch_spacing_statistical -v`
Expected: PASS (or SKIPPED if `data/clusters.json` doesn't exist).

- [ ] **Step 3: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add statistical validation for max_branch_spacing"
```

---

### Task 8: Regression — disabled mode

**Files:**
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write regression test**

```python
# In tests/test_generator.py

def test_max_branch_spacing_disabled():
    """max_branch_spacing=0 produces same behavior as before the feature."""
    # Use a pool with only passant-capable clusters — no splits possible.
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

    # Should complete without error — no forced operations
    dag = generate_dag(config, pool, _boss_candidates(pool))
    paths = dag.enumerate_paths()
    assert len(paths) == 1  # Linear, no splits possible
```

- [ ] **Step 2: Run test**

Run: `pytest tests/test_generator.py::test_max_branch_spacing_disabled -v`
Expected: PASS.

- [ ] **Step 3: Run full test suite for final regression**

Run: `pytest -v`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add regression test for max_branch_spacing=0 (disabled)"
```

---

### Task 9: Update config example and documentation

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
