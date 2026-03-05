# Crosslinks Proximity Filtering — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make cross-link generation respect `proximity_groups` so spatially adjacent fogs are never paired as entry+exit on the same node.

**Architecture:** Add a `_blocked_by_proximity` helper in `crosslinks.py` that checks whether a `FogRef` shares a proximity group with any consumed `FogRef`s. Call it from `_surplus_exits` and `_surplus_entries` to filter out proximity-violating fogs.

**Tech Stack:** Python, pytest

---

### Task 1: Add proximity helper and wire into `_surplus_exits`

**Files:**
- Modify: `speedfog/crosslinks.py:1-44`
- Test: `tests/test_crosslinks.py`

**Step 1: Write the failing test**

Add to `tests/test_crosslinks.py`. The test creates a node whose cluster has `proximity_groups` linking the entry fog to a surplus exit fog, then asserts the surplus exit is excluded.

```python
from speedfog.clusters import ClusterData


def make_cluster_with_proximity(
    cluster_id: str,
    entry_fogs: list[dict],
    exit_fogs: list[dict],
    proximity_groups: list[list[str]],
) -> ClusterData:
    return ClusterData(
        id=cluster_id,
        zones=[f"{cluster_id}_zone"],
        type="mini_dungeon",
        weight=5,
        entry_fogs=entry_fogs,
        exit_fogs=exit_fogs,
        proximity_groups=proximity_groups,
    )


class TestProximityFiltering:
    def test_surplus_exit_blocked_by_proximity_to_entry(self):
        """Surplus exit sharing a proximity group with consumed entry is excluded."""
        dag = Dag(seed=1)

        # Cluster with proximity_groups: entry fog_A and exit fog_B are proximate.
        # fog_C is an independent exit not in any group.
        c = make_cluster_with_proximity(
            "prox",
            entry_fogs=[
                {"fog_id": "fog_A", "zone": "prox"},
                {"fog_id": "fog_D", "zone": "prox"},
            ],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "prox"},
                {"fog_id": "fog_C", "zone": "prox"},
            ],
            proximity_groups=[["fog_A", "fog_B"]],
        )

        # Node uses fog_A as entry (incoming edge) and fog_B as potential surplus exit
        dag.add_node(DagNode("n", c, 1, 2, [FogRef("fog_A", "prox")], []))

        # Wire a dummy incoming edge so _surplus_exits sees fog_A as consumed entry
        s_c = make_cluster("s", "start", entry_fogs=[], exit_fogs=[
            {"fog_id": "s_exit", "zone": "s"},
        ])
        dag.add_node(DagNode("s", s_c, 0, 1, [], [FogRef("s_exit", "s")]))
        dag.add_edge("s", "n", FogRef("s_exit", "s"), FogRef("fog_A", "prox"))
        dag.start_id = "s"

        from speedfog.crosslinks import _surplus_exits

        surplus = _surplus_exits(dag, "n")
        # fog_B blocked by proximity to fog_A, fog_C is fine
        assert FogRef("fog_B", "prox") not in surplus
        assert FogRef("fog_C", "prox") in surplus
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_crosslinks.py::TestProximityFiltering::test_surplus_exit_blocked_by_proximity_to_entry -v`
Expected: FAIL — `FogRef("fog_B", "prox")` is still in surplus (no proximity filtering yet).

**Step 3: Implement `_blocked_by_proximity` and update `_surplus_exits`**

In `speedfog/crosslinks.py`, add a helper and update `_surplus_exits`:

```python
from speedfog.clusters import parse_qualified_fog_id
from speedfog.generator import _fog_matches_spec


def _blocked_by_proximity(
    cluster_data: "ClusterData",
    candidate: FogRef,
    consumed: set[FogRef],
) -> bool:
    """Check if candidate FogRef shares a proximity group with any consumed FogRef."""
    if not cluster_data.proximity_groups:
        return False

    for group in cluster_data.proximity_groups:
        candidate_in = any(
            _fog_matches_spec(candidate.fog_id, candidate.zone, spec)
            for spec in group
        )
        if not candidate_in:
            continue
        for ref in consumed:
            if any(
                _fog_matches_spec(ref.fog_id, ref.zone, spec)
                for spec in group
            ):
                return True
    return False
```

Then update `_surplus_exits` — add proximity filtering after the existing list comprehension (line 44):

Replace the return statement at line 44:
```python
    return [f for f in all_exits if f not in used and f not in entry_fogrefs]
```
with:
```python
    result = [f for f in all_exits if f not in used and f not in entry_fogrefs]
    if node.cluster.proximity_groups:
        result = [
            f for f in result
            if not _blocked_by_proximity(node.cluster, f, entry_fogrefs)
        ]
    return result
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_crosslinks.py::TestProximityFiltering::test_surplus_exit_blocked_by_proximity_to_entry -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/crosslinks.py tests/test_crosslinks.py
git commit -m "fix: respect proximity_groups in crosslinks surplus exits"
```

---

### Task 2: Wire proximity filtering into `_surplus_entries`

**Files:**
- Modify: `speedfog/crosslinks.py:47-61`
- Test: `tests/test_crosslinks.py`

**Step 1: Write the failing test**

```python
    def test_surplus_entry_blocked_by_proximity_to_exit(self):
        """Surplus entry sharing a proximity group with consumed exit is excluded."""
        dag = Dag(seed=1)

        # Cluster with proximity_groups: exit fog_X and entry fog_Y are proximate.
        # fog_Z is an independent entry not in any group.
        c = make_cluster_with_proximity(
            "prox2",
            entry_fogs=[
                {"fog_id": "fog_Y", "zone": "prox2"},
                {"fog_id": "fog_Z", "zone": "prox2"},
            ],
            exit_fogs=[
                {"fog_id": "fog_X", "zone": "prox2"},
            ],
            proximity_groups=[["fog_X", "fog_Y"]],
        )

        # Node uses fog_X as exit (outgoing edge) and fog_Y as potential surplus entry
        dag.add_node(DagNode("n", c, 1, 2, [], [FogRef("fog_X", "prox2")]))

        # Wire a dummy outgoing edge so _surplus_entries sees fog_X as consumed exit
        e_c = make_cluster("e", "final_boss", entry_fogs=[
            {"fog_id": "e_entry", "zone": "e"},
        ], exit_fogs=[])
        dag.add_node(DagNode("e", e_c, 2, 3, [FogRef("e_entry", "e")], []))
        dag.add_edge("n", "e", FogRef("fog_X", "prox2"), FogRef("e_entry", "e"))
        dag.start_id = "n"
        dag.end_id = "e"

        from speedfog.crosslinks import _surplus_entries

        surplus = _surplus_entries(dag, "n")
        # fog_Y blocked by proximity to fog_X, fog_Z is fine
        assert FogRef("fog_Y", "prox2") not in surplus
        assert FogRef("fog_Z", "prox2") in surplus
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_crosslinks.py::TestProximityFiltering::test_surplus_entry_blocked_by_proximity_to_exit -v`
Expected: FAIL — `FogRef("fog_Y", "prox2")` is still in surplus.

**Step 3: Update `_surplus_entries`**

Replace the return statement at line 61:
```python
    return [f for f in all_entries if f not in used and f not in exit_fogrefs]
```
with:
```python
    result = [f for f in all_entries if f not in used and f not in exit_fogrefs]
    if node.cluster.proximity_groups:
        result = [
            f for f in result
            if not _blocked_by_proximity(node.cluster, f, exit_fogrefs)
        ]
    return result
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_crosslinks.py::TestProximityFiltering::test_surplus_entry_blocked_by_proximity_to_exit -v`
Expected: PASS

**Step 5: Commit**

```bash
git add speedfog/crosslinks.py tests/test_crosslinks.py
git commit -m "fix: respect proximity_groups in crosslinks surplus entries"
```

---

### Task 3: Add edge-case test — no false blocking across groups

**Files:**
- Test: `tests/test_crosslinks.py`

**Step 1: Write the test**

```python
    def test_no_false_blocking_across_groups(self):
        """Fogs in different proximity groups are not blocked by each other."""
        dag = Dag(seed=1)

        # Two independent groups: [fog_A, fog_B] and [fog_C, fog_D]
        # Entry uses fog_A, so fog_B is blocked but fog_D is NOT.
        c = make_cluster_with_proximity(
            "multi",
            entry_fogs=[
                {"fog_id": "fog_A", "zone": "multi"},
            ],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "multi"},
                {"fog_id": "fog_D", "zone": "multi"},
            ],
            proximity_groups=[["fog_A", "fog_B"], ["fog_C", "fog_D"]],
        )

        dag.add_node(DagNode("n", c, 1, 2, [FogRef("fog_A", "multi")], []))

        s_c = make_cluster("s", "start", entry_fogs=[], exit_fogs=[
            {"fog_id": "s_exit", "zone": "s"},
        ])
        dag.add_node(DagNode("s", s_c, 0, 1, [], [FogRef("s_exit", "s")]))
        dag.add_edge("s", "n", FogRef("s_exit", "s"), FogRef("fog_A", "multi"))
        dag.start_id = "s"

        from speedfog.crosslinks import _surplus_exits

        surplus = _surplus_exits(dag, "n")
        assert FogRef("fog_B", "multi") not in surplus  # blocked (same group as A)
        assert FogRef("fog_D", "multi") in surplus       # not blocked (different group)
```

**Step 2: Run test**

Run: `pytest tests/test_crosslinks.py::TestProximityFiltering::test_no_false_blocking_across_groups -v`
Expected: PASS (already handled by group-level matching logic).

**Step 3: Commit**

```bash
git add tests/test_crosslinks.py
git commit -m "test: add edge-case for cross-group proximity non-blocking"
```

---

### Task 4: Run full test suite and verify

**Step 1: Run all crosslinks tests**

Run: `pytest tests/test_crosslinks.py -v`
Expected: All pass.

**Step 2: Run full Python test suite**

Run: `pytest -v`
Expected: All pass. No regressions.

**Step 3: Final commit if any formatting fixes needed**

If ruff/ruff-format made changes during hook:
```bash
git add -u && git commit -m "style: formatting fixes"
```
