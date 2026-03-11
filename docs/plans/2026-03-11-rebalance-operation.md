# REBALANCE Operation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace three separate spacing enforcement mechanisms with a single native REBALANCE operation, simplifying the code by ~250 lines while fixing convergence-phase linear stretches.

**Architecture:** Add `REBALANCE` as a 4th `LayerOperation` enum value. `determine_operation` returns REBALANCE when branches are saturated and a branch exceeds the staleness threshold. `execute_rebalance_layer` replaces `_execute_spacing_rebalance`. Unified convergence loop replaces `execute_forced_merge` and `is_near_end`.

**Tech Stack:** Python 3.10+, pytest

**Spec:** `docs/specs/2026-03-11-rebalance-operation-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `speedfog/generator.py` | Modify | Add REBALANCE to enum, rewrite `determine_operation`, add `execute_rebalance_layer`, unify convergence, delete old code |
| `tests/test_generator.py` | Modify | Rewrite spacing tests for new interface, add REBALANCE-specific tests |
| `docs/dag-generation.md` | Modify | Update operation documentation |

---

## Chunk 1: Core REBALANCE Implementation

### Task 1: Commit current working state

The generator.py has ~310 lines of uncommitted spacing enforcement changes from the previous session. Commit them as a baseline before refactoring.

**Files:**
- Commit: `speedfog/generator.py`

- [ ] **Step 1: Run tests to confirm current state is green**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All tests pass (526+)

- [ ] **Step 2: Commit current state**

```bash
git add speedfog/generator.py
git commit -m "feat: add max_branch_spacing enforcement with spacing rebalance

Implements per-branch staleness tracking and forced split/merge/rebalance
operations to prevent long linear stretches. This is the pre-refactor
baseline that will be simplified by the REBALANCE operation."
```

---

### Task 2: Add REBALANCE to LayerOperation enum

**Files:**
- Modify: `speedfog/generator.py:93-99`

- [ ] **Step 1: Add REBALANCE to the enum**

In `speedfog/generator.py`, change the `LayerOperation` class:

```python
class LayerOperation(Enum):
    """Type of operation to perform on a layer."""

    PASSANT = auto()  # 1 branch -> 1 branch (per branch)
    SPLIT = auto()  # 1 branch -> N branches
    MERGE = auto()  # N branches -> 1 branch
    REBALANCE = auto()  # merge 2 + split 1 stale (same layer, N -> N)
```

- [ ] **Step 2: Run tests to confirm nothing breaks**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All pass (adding an unused enum value changes nothing)

- [ ] **Step 3: Commit**

```bash
git add speedfog/generator.py
git commit -m "refactor: add REBALANCE to LayerOperation enum"
```

---

### Task 3: Write execute_rebalance_layer

Replace `_execute_spacing_rebalance` (lines 675-887) with a cleaner `execute_rebalance_layer` that follows the same pattern as `execute_merge_layer` and `execute_passant_layer`: always returns `list[Branch]`, raises `GenerationError` on failure.

**Files:**
- Modify: `speedfog/generator.py:675-887`
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing test for execute_rebalance_layer**

Add to `tests/test_generator.py`:

```python
def test_execute_rebalance_layer_basic():
    """execute_rebalance_layer splits stale branch and merges another pair."""
    from speedfog.generator import execute_rebalance_layer

    dag = Dag(seed=1)

    # 3 branches: A (stale), B and C (fresh, different parent nodes)
    n_a = DagNode(
        id="n_a",
        cluster=make_cluster(
            "ca", zones=["za"],
            entry_fogs=[{"fog_id": "ea", "zone": "za"}],
            exit_fogs=[{"fog_id": "xa", "zone": "za"}],
        ),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xa", "za")],
    )
    n_b = DagNode(
        id="n_b",
        cluster=make_cluster(
            "cb", zones=["zb"],
            entry_fogs=[{"fog_id": "eb", "zone": "zb"}],
            exit_fogs=[{"fog_id": "xb", "zone": "zb"}],
        ),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xb", "zb")],
    )
    n_c = DagNode(
        id="n_c",
        cluster=make_cluster(
            "cc", zones=["zc"],
            entry_fogs=[{"fog_id": "ec", "zone": "zc"}],
            exit_fogs=[{"fog_id": "xc", "zone": "zc"}],
        ),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xc", "zc")],
    )
    dag.add_node(n_a)
    dag.add_node(n_b)
    dag.add_node(n_c)

    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),  # stale
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]

    # Pool: split-capable + merge-capable clusters
    pool = ClusterPool()
    for i in range(5):
        pool.add(make_cluster(
            f"split{i}", zones=[f"s{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
            exit_fogs=[
                {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
            ],
        ))
    for i in range(5):
        pool.add(make_cluster(
            f"merge{i}", zones=[f"m{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[
                {"fog_id": f"m{i}_e1", "zone": f"m{i}_z"},
                {"fog_id": f"m{i}_e2", "zone": f"m{i}_z"},
            ],
            exit_fogs=[{"fog_id": f"m{i}_x", "zone": f"m{i}_z"}],
            allow_shared_entrance=True,
        ))

    config = Config()
    config.structure.max_parallel_paths = 3

    result = execute_rebalance_layer(
        dag, branches, layer_idx=1, tier=2, layer_type="mini_dungeon",
        clusters=pool, used_zones=set(), rng=random.Random(42), config=config,
    )

    # Same number of branches (rebalance is N -> N)
    assert len(result) == 3
    # At least one branch has counter = 0 (from the split)
    assert any(b.layers_since_last_split == 0 for b in result)
    # No branch named "a" remains (it was split into children)
    assert not any(b.id == "a" for b in result)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `uv run pytest tests/test_generator.py::test_execute_rebalance_layer_basic -x -v`
Expected: FAIL with `ImportError: cannot import name 'execute_rebalance_layer'`

- [ ] **Step 3: Implement execute_rebalance_layer**

Replace `_execute_spacing_rebalance` (lines 675-887) with `execute_rebalance_layer`. Key changes:
- Rename from `_execute_spacing_rebalance` to `execute_rebalance_layer` (public, like other helpers)
- Return type changes from `list[Branch] | None` to `list[Branch]`
- Raise `GenerationError` instead of returning `None` on failure
- Same internal logic: identify stale branch, find merge pair, pick 2 clusters, execute split+merge+passant, update counters

```python
def execute_rebalance_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    config: Config,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> list[Branch]:
    """Combined merge + split on the same layer (REBALANCE operation).

    Merges 2 branches and splits the most stale branch on the same layer,
    keeping total branch count constant (N -> N). Uses 2 clusters: one
    split-capable, one merge-capable.

    Args:
        dag: The DAG being built.
        branches: Current active branches.
        layer_idx: Current layer index.
        tier: Difficulty tier.
        layer_type: Preferred cluster type.
        clusters: Pool of available clusters.
        used_zones: Set of already used zones.
        rng: Random number generator.
        config: Configuration.
        reserved_zones: Zones excluded from selection.

    Returns:
        Updated list of branches (same count as input).

    Raises:
        GenerationError: If no valid merge pair or no capable clusters found.
    """
    current_layer = layer_idx

    # 1. Identify the most stale branch (split target)
    stale_idx = max(
        range(len(branches)),
        key=lambda i: branches[i].layers_since_last_split,
    )

    # 2. Find 2 merge candidates among other branches (bypass min_age,
    #    enforce anti-micro-merge: different parent nodes)
    other_indices = [i for i in range(len(branches)) if i != stale_idx]
    rng.shuffle(other_indices)
    merge_pair: tuple[int, int] | None = None
    for i in range(len(other_indices)):
        for j in range(i + 1, len(other_indices)):
            a, b = other_indices[i], other_indices[j]
            if branches[a].current_node_id != branches[b].current_node_id:
                merge_pair = (a, b)
                break
        if merge_pair:
            break
    if merge_pair is None:
        raise GenerationError(
            f"Rebalance failed at layer {current_layer}: "
            "no valid merge pair (anti-micro-merge)"
        )

    # 3. Pick a split-capable cluster (try preferred type, then others)
    split_cluster = None
    all_types = [layer_type] + [t for t in clusters.by_type if t != layer_type]
    for t in all_types:
        split_cluster = pick_cluster_with_filter(
            clusters.get_by_type(t),
            used_zones,
            rng,
            lambda c: can_be_split_node(c, 2),
            reserved_zones=reserved_zones,
        )
        if split_cluster is not None:
            break
    if split_cluster is None:
        raise GenerationError(
            f"Rebalance failed at layer {current_layer}: "
            "no split-capable cluster available"
        )

    # 4. Pick a merge-capable cluster (try preferred type, then others)
    used_after_split = used_zones | set(split_cluster.zones)
    merge_cluster = None
    for t in all_types:
        merge_cluster = pick_cluster_with_filter(
            clusters.get_by_type(t),
            used_after_split,
            rng,
            lambda c: can_be_merge_node(c, 2),
            reserved_zones=reserved_zones,
        )
        if merge_cluster is not None:
            break
    if merge_cluster is None:
        raise GenerationError(
            f"Rebalance failed at layer {current_layer}: "
            "no merge-capable cluster available"
        )

    new_branches: list[Branch] = []
    letter = 0

    # A. Split the stale branch
    stale_branch = branches[stale_idx]
    used_zones.update(split_cluster.zones)
    entry_fog, exit_fogs = _pick_entry_and_exits_for_node(split_cluster, 2, rng)
    split_node_id = f"node_{current_layer}_{chr(97 + letter)}"
    split_node = DagNode(
        id=split_node_id,
        cluster=split_cluster,
        layer=current_layer,
        tier=tier,
        entry_fogs=[entry_fog],
        exit_fogs=exit_fogs,
    )
    dag.add_node(split_node)
    dag.add_edge(
        stale_branch.current_node_id,
        split_node_id,
        stale_branch.available_exit,
        entry_fog,
    )
    split_children: list[Branch] = []
    for j in range(2):
        split_children.append(
            Branch(
                f"{stale_branch.id}_{chr(97 + j)}",
                split_node_id,
                exit_fogs[j],
                birth_layer=current_layer,
                layers_since_last_split=0,
            )
        )
    new_branches.extend(split_children)
    letter += 1

    # B. Merge the pair
    merge_a, merge_b = merge_pair
    merge_branches_list = [branches[merge_a], branches[merge_b]]
    used_zones.update(merge_cluster.zones)

    if merge_cluster.allow_shared_entrance:
        entries = select_entries_for_merge(merge_cluster, 1, rng)
        shared_entry = FogRef(entries[0]["fog_id"], entries[0]["zone"])
        entry_fogs_list = [shared_entry]
        exits = compute_net_exits(merge_cluster, entries)
        for e in entries:
            exits = _filter_exits_by_proximity(merge_cluster, e, exits)
    else:
        entries = select_entries_for_merge(merge_cluster, 2, rng)
        entry_fogs_list = [FogRef(e["fog_id"], e["zone"]) for e in entries]
        exits = compute_net_exits(merge_cluster, entries)
        for e in entries:
            exits = _filter_exits_by_proximity(merge_cluster, e, exits)

    rng.shuffle(exits)
    merge_exit_fogs = [FogRef(f["fog_id"], f["zone"]) for f in exits[:1]]
    if not merge_exit_fogs:
        raise GenerationError(
            f"Rebalance merge at layer {current_layer}: no exits remaining"
        )

    merge_node_id = f"node_{current_layer}_{chr(97 + letter)}"
    merge_node = DagNode(
        id=merge_node_id,
        cluster=merge_cluster,
        layer=current_layer,
        tier=tier,
        entry_fogs=entry_fogs_list,
        exit_fogs=merge_exit_fogs,
    )
    dag.add_node(merge_node)

    if merge_cluster.allow_shared_entrance:
        for mb in merge_branches_list:
            dag.add_edge(
                mb.current_node_id, merge_node_id, mb.available_exit, shared_entry
            )
    else:
        for mb, ef in zip(merge_branches_list, entry_fogs_list, strict=False):
            dag.add_edge(mb.current_node_id, merge_node_id, mb.available_exit, ef)

    # Merged branch inherits max counter; update_branch_counters will += 1
    merged_counter = max(b.layers_since_last_split for b in merge_branches_list)
    merged_branch = Branch(
        f"merged_{current_layer}",
        merge_node_id,
        rng.choice(merge_exit_fogs),
        birth_layer=current_layer,
        layers_since_last_split=merged_counter,
    )
    new_branches.append(merged_branch)
    letter += 1

    # C. Passant for remaining branches
    handled = {stale_idx, merge_a, merge_b}
    for i, branch in enumerate(branches):
        if i in handled:
            continue
        pc = pick_cluster_with_type_fallback(
            clusters, layer_type, used_zones, rng, reserved_zones=reserved_zones
        )
        if pc is None:
            raise GenerationError(
                f"Rebalance passant at layer {current_layer}: "
                f"no cluster for branch {i}"
            )
        used_zones.update(pc.zones)
        ef, exf = _pick_entry_and_exits_for_node(pc, 1, rng)
        nid = f"node_{current_layer}_{chr(97 + letter)}"
        n = DagNode(
            id=nid, cluster=pc, layer=current_layer, tier=tier,
            entry_fogs=[ef], exit_fogs=exf,
        )
        dag.add_node(n)
        dag.add_edge(branch.current_node_id, nid, branch.available_exit, ef)
        new_branches.append(
            Branch(
                branch.id, nid, rng.choice(exf),
                birth_layer=branch.birth_layer,
                layers_since_last_split=branch.layers_since_last_split,
            )
        )
        letter += 1

    # D. Update counters: split children = 0, everyone else += 1
    update_branch_counters(
        LayerOperation.SPLIT,
        split_children=split_children,
        passant_branches=[b for b in new_branches if b not in split_children],
    )

    return new_branches
```

- [ ] **Step 4: Write edge-case tests for execute_rebalance_layer**

Add to `tests/test_generator.py`:

```python
def test_execute_rebalance_layer_no_merge_pair():
    """Raises GenerationError when no valid merge pair exists."""
    from speedfog.generator import execute_rebalance_layer

    dag = Dag(seed=1)
    # All branches share same parent node → anti-micro-merge blocks merge
    n = DagNode(
        id="n_shared",
        cluster=make_cluster(
            "cs", zones=["zs"],
            entry_fogs=[{"fog_id": "es", "zone": "zs"}],
            exit_fogs=[{"fog_id": "xs", "zone": "zs"}],
        ),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xs", "zs")],
    )
    dag.add_node(n)
    branches = [
        Branch("a", "n_shared", FogRef("xs", "zs"), layers_since_last_split=5),
        Branch("b", "n_shared", FogRef("xs", "zs"), layers_since_last_split=1),
        Branch("c", "n_shared", FogRef("xs", "zs"), layers_since_last_split=1),
    ]
    pool = ClusterPool()
    for i in range(5):
        pool.add(make_cluster(
            f"sp{i}", zones=[f"s{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
            exit_fogs=[
                {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
            ],
        ))
    config = Config()
    config.structure.max_parallel_paths = 3

    with pytest.raises(GenerationError, match="no valid merge pair"):
        execute_rebalance_layer(
            dag, branches, layer_idx=1, tier=2, layer_type="mini_dungeon",
            clusters=pool, used_zones=set(), rng=random.Random(42), config=config,
        )


def test_execute_rebalance_layer_counter_propagation():
    """Merged branch counter ends at max(A, B) + 1 after update."""
    from speedfog.generator import execute_rebalance_layer

    dag = Dag(seed=1)
    n_a = DagNode(
        id="n_a",
        cluster=make_cluster("ca", zones=["za"],
            entry_fogs=[{"fog_id": "ea", "zone": "za"}],
            exit_fogs=[{"fog_id": "xa", "zone": "za"}]),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xa", "za")],
    )
    n_b = DagNode(
        id="n_b",
        cluster=make_cluster("cb", zones=["zb"],
            entry_fogs=[{"fog_id": "eb", "zone": "zb"}],
            exit_fogs=[{"fog_id": "xb", "zone": "zb"}]),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xb", "zb")],
    )
    n_c = DagNode(
        id="n_c",
        cluster=make_cluster("cc", zones=["zc"],
            entry_fogs=[{"fog_id": "ec", "zone": "zc"}],
            exit_fogs=[{"fog_id": "xc", "zone": "zc"}]),
        layer=0, tier=1, entry_fogs=[], exit_fogs=[FogRef("xc", "zc")],
    )
    dag.add_node(n_a)
    dag.add_node(n_b)
    dag.add_node(n_c)

    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=8),  # stale (split)
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=3),  # merge candidate
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),  # merge candidate
    ]

    pool = ClusterPool()
    for i in range(5):
        pool.add(make_cluster(
            f"split{i}", zones=[f"s{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
            exit_fogs=[
                {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
            ],
        ))
    for i in range(5):
        pool.add(make_cluster(
            f"merge{i}", zones=[f"m{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[
                {"fog_id": f"m{i}_e1", "zone": f"m{i}_z"},
                {"fog_id": f"m{i}_e2", "zone": f"m{i}_z"},
            ],
            exit_fogs=[{"fog_id": f"m{i}_x", "zone": f"m{i}_z"}],
            allow_shared_entrance=True,
        ))

    config = Config()
    config.structure.max_parallel_paths = 3

    result = execute_rebalance_layer(
        dag, branches, layer_idx=1, tier=2, layer_type="mini_dungeon",
        clusters=pool, used_zones=set(), rng=random.Random(42), config=config,
    )

    # Split children have counter = 0
    split_children = [b for b in result if b.layers_since_last_split == 0]
    assert len(split_children) == 2

    # Merged branch has counter = max(3, 1) + 1 = 4
    merged = [b for b in result if "merged" in b.id]
    assert len(merged) == 1
    assert merged[0].layers_since_last_split == 4  # max(3, 1) + 1
```

Run: `uv run pytest tests/test_generator.py::test_execute_rebalance_layer_no_merge_pair tests/test_generator.py::test_execute_rebalance_layer_counter_propagation -x -v`
Expected: FAIL (function not yet renamed/created)

- [ ] **Step 5: Update references — replace old calls to `_execute_spacing_rebalance` with `execute_rebalance_layer`**

In the main loop (around line 1612), change:
```python
                    result = _execute_spacing_rebalance(
```
to:
```python
                    result = execute_rebalance_layer(
```

And update the result handling — `execute_rebalance_layer` never returns `None`, it raises on failure. Wrap in try/except:
```python
                    try:
                        branches = execute_rebalance_layer(
                            dag, branches, current_layer, tier, layer_type,
                            clusters, used_zones, rng, config,
                            reserved_zones=reserved_zones,
                        )
                        current_layer += 1
                        continue
                    except GenerationError:
                        force_op = LayerOperation.MERGE
```

Note: This is a temporary bridge. The `force_op` block will be removed in Task 5.

- [ ] **Step 5: Run tests**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "refactor: replace _execute_spacing_rebalance with execute_rebalance_layer

Public helper that raises GenerationError on failure instead of returning
None. Same internal logic, consistent with execute_merge_layer pattern."
```

---

### Task 4: Rewrite determine_operation

Replace `force` parameter with internal REBALANCE logic and `prefer_merge` parameter.

**Files:**
- Modify: `speedfog/generator.py:489-580`
- Test: `tests/test_generator.py`

- [ ] **Step 1: Write failing test for REBALANCE detection**

Add to `tests/test_generator.py`:

```python
def test_determine_operation_returns_rebalance():
    """determine_operation returns REBALANCE when saturated + stale."""
    cluster = make_cluster(
        "c1", zones=["z1"],
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
        ],
    )
    # 3 branches at max_parallel_paths=3, one stale
    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]
    config = Config()
    config.structure.max_parallel_paths = 3
    config.structure.max_branch_spacing = 4
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0

    op, fan = determine_operation(cluster, branches, config, random.Random(42))
    assert op == LayerOperation.REBALANCE


def test_determine_operation_no_rebalance_when_not_saturated():
    """REBALANCE only triggers when branches == max_parallel_paths."""
    cluster = make_cluster(
        "c1", zones=["z1"],
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
        ],
    )
    # 2 branches, max=3 — not saturated, even though stale
    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
    ]
    config = Config()
    config.structure.max_parallel_paths = 3
    config.structure.max_branch_spacing = 4
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0

    op, fan = determine_operation(cluster, branches, config, random.Random(42))
    # Not saturated → should get SPLIT (forced, not rebalance) or PASSANT
    assert op != LayerOperation.REBALANCE


def test_determine_operation_prefer_merge():
    """prefer_merge=True bypasses probability roll in favor of MERGE."""
    cluster = make_cluster(
        "c1", zones=["z1"],
        entry_fogs=[
            {"fog_id": "e1", "zone": "z1"},
            {"fog_id": "e2", "zone": "z1"},
        ],
        exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
        allow_shared_entrance=True,
    )
    # 2 branches, different parent nodes
    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=0),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=0),
    ]
    config = Config()
    config.structure.max_parallel_paths = 4
    config.structure.split_probability = 1.0  # Would always split
    config.structure.merge_probability = 0.0  # Would never merge

    # Without prefer_merge: should split (probability 1.0)
    op1, _ = determine_operation(cluster, branches, config, random.Random(42))
    # With prefer_merge: should merge despite split_probability=1.0
    op2, _ = determine_operation(
        cluster, branches, config, random.Random(42), prefer_merge=True,
    )
    assert op2 == LayerOperation.MERGE
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `uv run pytest tests/test_generator.py::test_determine_operation_returns_rebalance tests/test_generator.py::test_determine_operation_prefer_merge -x -v`
Expected: FAIL (REBALANCE not returned, prefer_merge not accepted)

- [ ] **Step 3: Implement new determine_operation**

Replace `determine_operation` (lines 489-580) with the new version:

```python
def determine_operation(
    cluster: ClusterData,
    branches: list[Branch],
    config: Config,
    rng: random.Random,
    *,
    current_layer: int = 0,
    prefer_merge: bool = False,
) -> tuple[LayerOperation, int]:
    """Determine what operation to perform given a pre-selected cluster.

    Priority hierarchy:
    1. REBALANCE — if saturated + stale + merge pair available (defensive override)
    2. prefer_merge — if True, bypass probability roll, return MERGE
    3. Normal probability roll — split_prob / merge_prob / passant

    Args:
        cluster: Pre-selected cluster.
        branches: Current active branches.
        config: Configuration with probabilities and limits.
        rng: Random number generator.
        current_layer: Current layer index (for min_branch_age check).
        prefer_merge: If True, bypass probability in favor of MERGE
            (used during convergence). Also bypasses min_branch_age.

    Returns:
        Tuple of (operation, fan_out/fan_in). fan is 1 for PASSANT/REBALANCE.
    """
    num_branches = len(branches)
    max_paths = config.structure.max_parallel_paths
    max_ex = config.structure.max_exits
    max_en = config.structure.max_entrances
    split_prob = config.structure.split_probability
    merge_prob = config.structure.merge_probability
    min_age = 0 if prefer_merge else config.structure.min_branch_age
    max_spacing = config.structure.max_branch_spacing

    # --- Priority 1: REBALANCE (saturated + stale) ---
    if max_spacing > 0 and num_branches >= max_paths:
        max_stale = max(b.layers_since_last_split for b in branches)
        if max_stale >= max_spacing:
            # Check merge pair exists among non-stale branches
            stale_idx = max(
                range(num_branches),
                key=lambda i: branches[i].layers_since_last_split,
            )
            other = [i for i in range(num_branches) if i != stale_idx]
            has_pair = any(
                branches[other[i]].current_node_id
                != branches[other[j]].current_node_id
                for i in range(len(other))
                for j in range(i + 1, len(other))
            )
            if has_pair:
                return LayerOperation.REBALANCE, 1

    # --- Priority 2: prefer_merge (convergence) ---
    if prefer_merge:
        can_merge_preferred = (
            max_en >= 2
            and num_branches >= 2
            and _has_valid_merge_pair(
                branches, min_age=0, current_layer=current_layer
            )
            and can_be_merge_node(cluster, 2)
        )
        if can_merge_preferred:
            return LayerOperation.MERGE, 2
        # Can't merge — fall through to normal logic (will likely be PASSANT)

    # --- Priority 3: Normal probability roll ---

    # Determine split capability
    can_split = False
    split_fan = 2
    if max_ex >= 2 and num_branches < max_paths:
        room = max_paths - num_branches + 1
        max_fan = min(max_ex, room)
        for n in range(max_fan, 1, -1):
            if can_be_split_node(cluster, n):
                can_split = True
                split_fan = n
                break

    # Determine merge capability (respects min_branch_age)
    can_merge = (
        max_en >= 2
        and num_branches >= 2
        and _has_valid_merge_pair(
            branches, min_age=min_age, current_layer=current_layer
        )
        and can_be_merge_node(cluster, 2)
    )

    # Forced split when not saturated but stale
    if (
        max_spacing > 0
        and num_branches < max_paths
        and can_split
    ):
        max_stale = max(b.layers_since_last_split for b in branches)
        if max_stale >= max_spacing:
            return LayerOperation.SPLIT, split_fan

    # Decide based on capabilities
    if can_split and can_merge:
        roll = rng.random()
        if roll < split_prob:
            return LayerOperation.SPLIT, split_fan
        elif roll < split_prob + merge_prob:
            return LayerOperation.MERGE, 2
        else:
            return LayerOperation.PASSANT, 1
    elif can_split:
        if rng.random() < split_prob:
            return LayerOperation.SPLIT, split_fan
    elif can_merge:
        if rng.random() < merge_prob:
            return LayerOperation.MERGE, 2

    return LayerOperation.PASSANT, 1
```

- [ ] **Step 4: Run all tests**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: Some existing tests that use `force=` parameter will fail. That's expected — we'll fix them in the next step.

- [ ] **Step 5: Fix existing tests that use `force=` parameter**

The test `test_forced_split_targets_most_stale_branch` (line 4127) calls `determine_operation(..., force=LayerOperation.SPLIT)`. This parameter no longer exists.

This test tested the old interface. The stale-targeting logic is now inside `determine_operation` itself (for non-saturated case → forced SPLIT) and inside `execute_rebalance_layer` (for saturated case). Replace the test:

```python
def test_forced_split_targets_most_stale_branch():
    """When max_branch_spacing triggers a forced split, the stale branch is split.

    With non-saturated branches, determine_operation returns SPLIT when
    a branch exceeds the threshold. The main loop's split logic should
    target the most stale branch.
    """
    split_cluster = make_cluster(
        "split_c", zones=["sc_z"],
        entry_fogs=[{"fog_id": "sc_e", "zone": "sc_z"}],
        exit_fogs=[
            {"fog_id": "sc_x1", "zone": "sc_z"},
            {"fog_id": "sc_x2", "zone": "sc_z"},
        ],
    )
    # 2 branches (not saturated at max=4), one stale exceeding threshold
    branches = [
        Branch("b_fresh", "n_fresh", FogRef("xf", "zf"), layers_since_last_split=0),
        Branch("b_stale", "n_stale", FogRef("xs", "zs"), layers_since_last_split=5),
    ]
    config = Config()
    config.structure.split_probability = 0.0  # Would never split normally
    config.structure.max_parallel_paths = 4
    config.structure.max_branch_spacing = 4  # Threshold at 4, stale has 5

    # determine_operation returns forced SPLIT (not REBALANCE — not saturated)
    op, fan = determine_operation(
        split_cluster, branches, config, random.Random(42),
    )
    assert op == LayerOperation.SPLIT
```

- [ ] **Step 6: Run all tests**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All pass

- [ ] **Step 7: Commit**

```bash
git add speedfog/generator.py tests/test_generator.py
git commit -m "refactor: determine_operation returns REBALANCE, add prefer_merge

Replace force parameter with internal REBALANCE detection (saturated +
stale) and prefer_merge for convergence. Priority: REBALANCE > prefer_merge
> normal probability roll."
```

---

## Chunk 2: Unified Convergence and Cleanup

### Task 5: Rewrite main loop — remove force_op, is_near_end, unify convergence

This is the core simplification. Remove the `force_op` block, `is_near_end` guard, biased cluster selection, and `execute_forced_merge` calls. Replace with the REBALANCE-aware unified flow.

**Files:**
- Modify: `speedfog/generator.py` (main loop, lines ~1573-2022)

- [ ] **Step 1: Remove is_near_end flag and its forced merge block**

Delete lines 1575 and 1585-1598 (the `is_near_end` check and forced merge block). The main loop should now run all planned layers without early convergence.

- [ ] **Step 2: Remove the force_op spacing enforcement block**

Delete lines 1600-1631 (the `max_spacing > 0 and not is_near_end` block that sets `force_op`). The spacing enforcement is now handled inside `determine_operation`.

- [ ] **Step 3: Remove biased cluster selection for force_op**

Delete lines 1638-1688 (the `if force_op == LayerOperation.SPLIT` / `elif force_op == LayerOperation.MERGE` blocks). Replace with normal cluster selection always:

```python
        primary_cluster = pick_cluster_with_type_fallback(
            clusters, layer_type, used_zones, rng, reserved_zones=reserved_zones
        )
```

- [ ] **Step 4: Simplify determine_operation call**

Change the call (around line 1696) to remove `force=force_op`:

```python
        operation, fan = determine_operation(
            primary_cluster,
            branches,
            config,
            rng,
            current_layer=current_layer,
        )
```

- [ ] **Step 5: Add REBALANCE case in the main loop**

After the `determine_operation` call, add a REBALANCE handler before the SPLIT handler:

```python
        if operation == LayerOperation.REBALANCE:
            branches = execute_rebalance_layer(
                dag, branches, current_layer, tier, layer_type,
                clusters, used_zones, rng, config,
                reserved_zones=reserved_zones,
            )
            current_layer += 1
            continue
```

- [ ] **Step 6: Simplify SPLIT branch targeting**

In the SPLIT handler (around line 1707), remove the `if force_op == LayerOperation.SPLIT` guard. Replace with simpler logic: `determine_operation` decides WHAT (SPLIT), the executor decides WHO (which branch). When spacing is enabled, always prefer the most stale branch for splits — this is cheap and harmless even for non-forced splits.

```python
        if operation == LayerOperation.SPLIT:
            # Prefer splitting the most stale branch (best for spacing)
            max_stale_val = max(b.layers_since_last_split for b in branches)
            stale_indices = [
                i for i, b in enumerate(branches)
                if b.layers_since_last_split == max_stale_val
            ]
            split_idx = rng.choice(stale_indices)
```

Note: This targets the most stale branch for ALL splits, not just forced ones. This is intentional — splitting the most stale branch is always the best choice for spacing, and doesn't harm random splits (when all counters are similar, all branches are equally likely).

- [ ] **Step 7: Remove min_age bypass in MERGE handler**

In the MERGE handler (around line 1802), remove the `force_op`-dependent min_age bypass:

```python
            min_age = config.structure.min_branch_age
```

(The `prefer_merge` path in `determine_operation` already bypasses min_age internally when checking merge eligibility. The main loop MERGE handler uses normal min_age.)

- [ ] **Step 8: Update merge_reserve**

Change line 1547:

```python
    merge_reserve = config.structure.max_parallel_paths + 2
```

- [ ] **Step 9: Replace post-loop convergence (step 7) with unified loop**

Replace lines 2000-2022 (`if len(branches) > 1: ... execute_forced_merge(...)`) with:

```python
    # 7. Converge remaining branches
    convergence_layers = 0
    convergence_limit = merge_reserve * 2
    while len(branches) > 1:
        tier = compute_tier(
            current_layer,
            estimated_total,
            config.structure.final_tier,
            curve=config.structure.tier_curve,
            exponent=config.structure.tier_curve_exponent,
        )
        last_layer_type = layer_types[-1] if layer_types else "mini_dungeon"

        # Pick cluster normally
        conv_cluster = pick_cluster_with_type_fallback(
            clusters, last_layer_type, used_zones, rng,
            reserved_zones=reserved_zones,
        )
        if conv_cluster is None:
            raise GenerationError(
                f"No cluster for convergence layer {current_layer}"
            )

        operation, fan = determine_operation(
            conv_cluster, branches, config, rng,
            current_layer=current_layer,
            prefer_merge=True,
        )

        if operation == LayerOperation.REBALANCE:
            branches = execute_rebalance_layer(
                dag, branches, current_layer, tier, last_layer_type,
                clusters, used_zones, rng, config,
                reserved_zones=reserved_zones,
            )
        elif operation == LayerOperation.MERGE:
            branches = execute_merge_layer(
                dag, branches, current_layer, tier, last_layer_type,
                clusters, used_zones, rng, config,
                reserved_zones=reserved_zones,
            )
        else:
            # Can't merge yet (anti-micro-merge) — passant to diverge
            branches = execute_passant_layer(
                dag, branches, current_layer, tier, last_layer_type,
                clusters, used_zones, rng,
                reserved_zones=reserved_zones,
            )

        current_layer += 1
        convergence_layers += 1
        if convergence_layers > convergence_limit:
            raise GenerationError(
                f"Convergence failed after {convergence_layers} layers "
                f"(limit: {convergence_limit})"
            )
```

- [ ] **Step 10: Run all tests**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All pass (or close — some integration tests may need minor adjustments)

- [ ] **Step 11: Commit**

```bash
git add speedfog/generator.py
git commit -m "refactor: unify convergence loop, remove force_op and is_near_end

Main loop uses determine_operation natively for REBALANCE. Convergence
uses prefer_merge=True with REBALANCE override for stale branches.
Removes ~200 lines of force_op block, biased selection, and
execute_forced_merge calls."
```

---

### Task 6: Delete dead code

**Files:**
- Modify: `speedfog/generator.py`

- [ ] **Step 1: Delete execute_forced_merge**

Remove the `execute_forced_merge` function (lines ~1225-1293). It's no longer called.

- [ ] **Step 2: Verify no remaining references**

Run: `grep -n "execute_forced_merge\|_execute_spacing_rebalance\|force_op\|is_near_end" speedfog/generator.py`
Expected: No matches (or only in comments that should also be cleaned)

- [ ] **Step 3: Run all tests**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add speedfog/generator.py
git commit -m "refactor: delete execute_forced_merge and dead code

Removed by unified convergence loop. Net reduction: ~70 lines."
```

---

### Task 7: Update and add integration tests

**Files:**
- Modify: `tests/test_generator.py`

- [ ] **Step 1: Add test_rebalance_during_convergence**

```python
def test_rebalance_during_convergence():
    """Convergence phase doesn't create linear stretches > threshold + 2."""
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    clusters_list = []
    for i in range(30):
        clusters_list.append(make_cluster(
            f"sp{i}", zones=[f"sp{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
            exit_fogs=[
                {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
            ],
        ))
    for i in range(10):
        clusters_list.append(make_cluster(
            f"mg{i}", zones=[f"mg{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[
                {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
            ],
            exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
            allow_shared_entrance=True,
        ))
    boss = make_cluster(
        "boss1", zones=["boss_z"], cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters_list:
        pool.add(c)
    pool.add(boss)

    max_spacing = 4
    violations = []
    for seed in range(30):
        config = Config()
        config.structure.final_boss_candidates = ["boss_z"]
        config.structure.max_branch_spacing = max_spacing
        config.structure.split_probability = 0.9
        config.structure.merge_probability = 0.5
        config.structure.max_parallel_paths = 4
        config.structure.min_layers = 12
        config.structure.max_layers = 18
        config.requirements.mini_dungeons = 10
        config.requirements.bosses = 0
        config.requirements.legacy_dungeons = 0
        config.requirements.major_bosses = 0

        try:
            dag = generate_dag(
                config, pool, seed=seed, boss_candidates=_boss_candidates(pool),
            )
            max_observed = _measure_max_branch_spacing(dag)
            if max_observed > max_spacing + 2:
                violations.append((seed, max_observed))
        except GenerationError:
            continue

    assert not violations, (
        f"Convergence stretches exceeded max_branch_spacing + 2: {violations}"
    )
```

- [ ] **Step 2: Add test_convergence_terminates**

```python
def test_convergence_terminates():
    """Convergence loop always terminates (no infinite REBALANCE loop)."""
    start = make_cluster(
        "start", zones=["start_z"], cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    clusters_list = []
    for i in range(40):
        clusters_list.append(make_cluster(
            f"sp{i}", zones=[f"sp{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
            exit_fogs=[
                {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
            ],
        ))
    for i in range(10):
        clusters_list.append(make_cluster(
            f"mg{i}", zones=[f"mg{i}_z"], cluster_type="mini_dungeon",
            entry_fogs=[
                {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
            ],
            exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
            allow_shared_entrance=True,
        ))
    boss = make_cluster(
        "boss1", zones=["boss_z"], cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters_list:
        pool.add(c)
    pool.add(boss)

    # Generate with high split_probability to maximize branches at convergence
    for seed in range(20):
        config = Config()
        config.structure.final_boss_candidates = ["boss_z"]
        config.structure.max_branch_spacing = 3
        config.structure.split_probability = 1.0
        config.structure.merge_probability = 0.0
        config.structure.max_parallel_paths = 4
        config.structure.min_layers = 10
        config.structure.max_layers = 14
        config.requirements.mini_dungeons = 8
        config.requirements.bosses = 0
        config.requirements.legacy_dungeons = 0
        config.requirements.major_bosses = 0

        try:
            dag = generate_dag(
                config, pool, seed=seed, boss_candidates=_boss_candidates(pool),
            )
            # If we get here, convergence terminated
            assert dag.end_id
        except GenerationError:
            # Generation failure is acceptable (cluster exhaustion etc.)
            # but NOT convergence timeout
            pass
```

- [ ] **Step 3: Run all tests**

Run: `uv run pytest tests/test_generator.py -x -q`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add tests/test_generator.py
git commit -m "test: add convergence REBALANCE and termination tests"
```

---

### Task 8: Update documentation

**Files:**
- Modify: `docs/dag-generation.md`

- [ ] **Step 1: Update dag-generation.md**

In the "Max Branch Spacing" section:
- Replace the forced merge description with REBALANCE operation description
- Update the convergence section to reflect unified convergence
- Add REBALANCE to the operations list
- Update `merge_reserve` description

- [ ] **Step 2: Commit**

```bash
git add docs/dag-generation.md
git commit -m "docs: update dag-generation for REBALANCE operation"
```

---

### Task 9: Final validation

- [ ] **Step 1: Run full test suite**

Run: `uv run pytest -x -q`
Expected: All pass

- [ ] **Step 2: Run mypy**

Run: `uv run mypy speedfog/generator.py`
Expected: No errors

- [ ] **Step 3: Run statistical test (if data available)**

Run: `uv run pytest tests/test_generator.py::test_max_branch_spacing_statistical -v`
Expected: Pass (no violations > max_branch_spacing + 2)

- [ ] **Step 4: Generate a sample DAG to verify**

Run: `uv run python -c "
from speedfog.config import Config
from speedfog.clusters import ClusterPool
from speedfog.generator import generate_dag
pool = ClusterPool.from_json('data/clusters.json')
bosses = pool.get_by_type('major_boss') + pool.get_by_type('final_boss')
config = Config()
config.structure.max_branch_spacing = 4
config.structure.max_parallel_paths = 4
dag = generate_dag(config, pool, seed=117945953, boss_candidates=bosses)
paths = dag.enumerate_paths()
print(f'Paths: {len(paths)}, Nodes: {len(dag.nodes)}')
"`
Expected: Runs without error, produces a DAG with multiple paths
