# Generation Log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add diagnostic logging to DAG generation that captures planned vs actual types, operations, fallbacks with pool state, crosslink decisions, and a summary, serialized to `logs/generation.log`.

**Architecture:** A `GenerationLog` dataclass accumulates events during `generate_dag`. Events are emitted at layer boundaries using compare-type-at-callsite fallback detection. Helper functions (`execute_merge_layer`, `execute_passant_layer`, `execute_rebalance_layer`) accept an optional `log_event` parameter. The log is serialized by `export_generation_log()` in `speedfog/generation_log.py`. The CLI renames `--spoiler` to `--logs` and writes both `spoiler.txt` and `generation.log` into a `logs/` subdirectory.

**Tech Stack:** Python 3.10+, pytest, speedfog package

**Spec:** `docs/plans/2026-03-26-generation-log.md`

---

## Chunk 1: Data Model

### Task 1: Create `generation_log.py` with dataclasses

**Files:**
- Create: `speedfog/generation_log.py`
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write test for dataclass construction**

```python
# tests/test_generation_log.py
import pytest

from speedfog.generation_log import (
    CrosslinkDetail,
    CrosslinkEvent,
    FallbackEntry,
    GenerationLog,
    LayerEvent,
    NodeEntry,
    PlanEvent,
    SummaryEvent,
)


def test_generation_log_defaults():
    log = GenerationLog()
    assert log.plan_event is None
    assert log.layer_events == []
    assert log.crosslink_event is None
    assert log.summary is None


def test_plan_event_construction():
    pe = PlanEvent(
        seed=12345,
        requirements={"legacy_dungeon": 2, "boss_arena": 7},
        target_total=25,
        merge_reserve=6,
        num_intermediate=22,
        first_layer_type="legacy_dungeon",
        planned_types=["legacy_dungeon", "boss_arena"],
        pool_sizes={"boss_arena": 84, "major_boss": 40},
        final_boss="jaggedpeak_bayle_f21a",
        reserved_zones={"jaggedpeak_bayle"},
    )
    assert pe.target_total == 25
    assert pe.first_layer_type == "legacy_dungeon"


def test_layer_event_construction():
    le = LayerEvent(
        layer=0,
        phase="start",
        planned_type=None,
        operation="START",
        branches_before=0,
        branches_after=2,
    )
    assert le.nodes == []
    assert le.fallbacks == []


def test_node_entry():
    ne = NodeEntry(
        cluster_id="stormveil_db4a",
        cluster_type="legacy_dungeon",
        weight=8,
        role="primary",
    )
    assert ne.role == "primary"


def test_fallback_entry():
    fe = FallbackEntry(
        branch_index=1,
        preferred_type="major_boss",
        actual_type="boss_arena",
        reason="pool_exhausted",
        pool_remaining={"major_boss": 0, "boss_arena": 61},
    )
    assert fe.reason == "pool_exhausted"


def test_crosslink_event():
    ce = CrosslinkEvent(
        eligible_pairs=45,
        added=32,
        skipped=13,
    )
    assert ce.added_details == []
    assert ce.skipped_details == []


def test_summary_event():
    se = SummaryEvent(
        total_layers=29,
        total_nodes=92,
        planned_layers=22,
        convergence_layers=4,
        crosslinks=32,
        fallback_count=3,
        fallback_summary=[(22, "major_boss")],
        pool_at_end={"boss_arena": 58},
    )
    assert se.fallback_count == 3
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -v`
Expected: FAIL (module not found)

- [ ] **Step 3: Create `generation_log.py` with all dataclasses**

```python
# speedfog/generation_log.py
"""Generation log data model and serialization for SpeedFog.

Captures diagnostic events during DAG generation: planner decisions,
per-layer operations, type fallbacks with pool state, crosslink
decisions, and summary statistics.
"""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class NodeEntry:
    """A node created at a layer."""

    cluster_id: str
    cluster_type: str
    weight: int
    role: str  # start, primary, passant, split_child, merge_target,
    # rebalance_split, rebalance_merge, rebalance_passant, final_boss


@dataclass
class FallbackEntry:
    """A type fallback event at a layer."""

    branch_index: int
    preferred_type: str
    actual_type: str
    reason: str  # pool_exhausted, zone_conflict
    pool_remaining: dict[str, int]


@dataclass
class LayerEvent:
    """What happened at a single layer during generation."""

    layer: int
    phase: str  # start, first_layer, planned, convergence, prerequisite, final_boss
    planned_type: str | None
    operation: str  # START, PASSANT, SPLIT, MERGE, REBALANCE
    branches_before: int
    branches_after: int
    nodes: list[NodeEntry] = field(default_factory=list)
    fallbacks: list[FallbackEntry] = field(default_factory=list)
    pool_snapshot: dict[str, int] | None = None  # pool state at convergence start


@dataclass
class PlanEvent:
    """Planner decisions captured before layer execution."""

    seed: int
    requirements: dict[str, int]
    target_total: int
    merge_reserve: int
    num_intermediate: int
    first_layer_type: str | None
    planned_types: list[str]
    pool_sizes: dict[str, int]
    final_boss: str
    reserved_zones: set[str]


@dataclass
class CrosslinkDetail:
    """A single crosslink attempt (added or skipped)."""

    source_id: str
    target_id: str
    reason: str | None = None  # None=added, "no_surplus_exits", "no_available_entries"


@dataclass
class CrosslinkEvent:
    """Summary of crosslink pass."""

    eligible_pairs: int
    added: int
    skipped: int
    added_details: list[CrosslinkDetail] = field(default_factory=list)
    skipped_details: list[CrosslinkDetail] = field(default_factory=list)


@dataclass
class SummaryEvent:
    """End-of-generation summary statistics."""

    total_layers: int
    total_nodes: int
    planned_layers: int
    convergence_layers: int
    crosslinks: int
    fallback_count: int
    fallback_summary: list[tuple[int, str]]  # (layer, preferred_type)
    pool_at_end: dict[str, int]


@dataclass
class GenerationLog:
    """Accumulates structured events during DAG generation."""

    plan_event: PlanEvent | None = None
    layer_events: list[LayerEvent] = field(default_factory=list)
    crosslink_event: CrosslinkEvent | None = None
    summary: SummaryEvent | None = None
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -v`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add speedfog/generation_log.py tests/test_generation_log.py
git commit -m "feat: add generation log data model"
```

---

## Chunk 2: Serialization

### Task 2: Implement `export_generation_log`

**Files:**
- Modify: `speedfog/generation_log.py`
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write test for PLAN section serialization**

Append to `tests/test_generation_log.py`:

```python
from speedfog.generation_log import export_generation_log


def _make_minimal_log():
    """Build a log with one plan event and two layers for testing."""
    log = GenerationLog()
    log.plan_event = PlanEvent(
        seed=42,
        requirements={"legacy_dungeon": 2, "boss_arena": 3, "mini_dungeon": 4, "major_boss": 2},
        target_total=15,
        merge_reserve=4,
        num_intermediate=12,
        first_layer_type="legacy_dungeon",
        planned_types=["legacy_dungeon", "boss_arena", "mini_dungeon"],
        pool_sizes={"boss_arena": 84, "legacy_dungeon": 32, "mini_dungeon": 69, "major_boss": 40},
        final_boss="jaggedpeak_bayle_f21a",
        reserved_zones={"jaggedpeak_bayle"},
    )
    log.layer_events = [
        LayerEvent(
            layer=0, phase="start", planned_type=None, operation="START",
            branches_before=0, branches_after=2,
            nodes=[NodeEntry("chapel_start_abc", "start", 1, "start")],
        ),
        LayerEvent(
            layer=1, phase="first_layer", planned_type="legacy_dungeon", operation="PASSANT",
            branches_before=2, branches_after=2,
            nodes=[
                NodeEntry("stormveil_db4a", "legacy_dungeon", 8, "passant"),
                NodeEntry("caelid_a51c", "legacy_dungeon", 3, "passant"),
            ],
        ),
    ]
    log.summary = SummaryEvent(
        total_layers=3, total_nodes=5, planned_layers=1, convergence_layers=0,
        crosslinks=0, fallback_count=0, fallback_summary=[], pool_at_end={},
    )
    return log


def test_export_plan_section(tmp_path):
    log = _make_minimal_log()
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "GENERATION LOG" in text
    assert "PLAN" in text
    assert "Final boss: jaggedpeak_bayle_f21a" in text
    assert "legacy_dungeon=2" in text
    assert "Target layers: 15" in text
    assert "merge_reserve=4" in text
    assert "Intermediate layers: 12" in text
    assert "major_boss=40" in text
    assert "First layer type: legacy_dungeon" in text


def test_export_layer_section(tmp_path):
    log = _make_minimal_log()
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "LAYERS" in text
    assert "L0 [start]" in text
    assert "START 0->2" in text
    assert "chapel_start_abc [start, w=1] (start)" in text
    assert "L1 [first_layer=legacy_dungeon]" in text
    assert "PASSANT 2->2" in text
    assert "stormveil_db4a [legacy_dungeon, w=8] (passant)" in text


def test_export_summary_section(tmp_path):
    log = _make_minimal_log()
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "SUMMARY" in text
    assert "Layers: 3" in text
    assert "Nodes: 5" in text
    assert "Fallbacks: 0" in text
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -k "export" -v`
Expected: FAIL (export_generation_log not found)

- [ ] **Step 3: Implement `export_generation_log`**

Add to `speedfog/generation_log.py`:

```python
from pathlib import Path

from speedfog.dag import Dag


def export_generation_log(
    log: GenerationLog,
    output_path: Path,
    dag: Dag | None = None,
) -> None:
    """Serialize a GenerationLog to a human-readable text file.

    Args:
        log: The generation log to serialize.
        output_path: Path to write the log file.
        dag: Optional DAG for resolving node layer numbers in crosslinks.
    """
    lines: list[str] = []

    # Header
    lines.append("=" * 64)
    if log.plan_event:
        lines.append(f"GENERATION LOG (seed: {log.plan_event.seed})")
    else:
        lines.append("GENERATION LOG")
    lines.append("=" * 64)
    lines.append("")

    # PLAN section
    if log.plan_event:
        pe = log.plan_event
        lines.append("PLAN")
        lines.append(f"  Final boss: {pe.final_boss}")
        if pe.reserved_zones:
            lines.append(f"  Reserved zones: {', '.join(sorted(pe.reserved_zones))}")
        req_parts = [f"{t}={c}" for t, c in sorted(pe.requirements.items())]
        lines.append(f"  Requirements: {', '.join(req_parts)}")
        lines.append(
            f"  Target layers: {pe.target_total} "
            f"(min={pe.target_total}, merge_reserve={pe.merge_reserve})"
        )
        lines.append(f"  Intermediate layers: {pe.num_intermediate}")
        if pe.first_layer_type:
            lines.append(f"  First layer type: {pe.first_layer_type}")
        seq = ", ".join(pe.planned_types)
        lines.append(f"  Planned sequence: [{seq}]")
        pool_parts = [f"{t}={c}" for t, c in sorted(pe.pool_sizes.items())]
        lines.append(f"  Pool sizes: {', '.join(pool_parts)}")
        lines.append("")

    # LAYERS section
    if log.layer_events:
        lines.append("LAYERS")
        convergence_started = False
        for le in log.layer_events:
            if le.phase == "convergence" and not convergence_started:
                convergence_started = True
                lines.append("")
                lines.append(f"  --- CONVERGENCE ({le.branches_before} branches remaining) ---")
                if le.pool_snapshot:
                    pool_parts = [f"{t}={c}" for t, c in sorted(le.pool_snapshot.items())]
                    lines.append(f"  Pool: {', '.join(pool_parts)}")

            # Layer header
            if le.phase == "start":
                phase_str = "start"
            elif le.phase == "first_layer":
                phase_str = f"first_layer={le.planned_type}" if le.planned_type else "first_layer"
            elif le.phase == "planned":
                phase_str = f"planned={le.planned_type}" if le.planned_type else "planned"
            elif le.phase == "convergence":
                phase_str = f"convergence={le.planned_type}" if le.planned_type else "convergence"
            elif le.phase == "prerequisite":
                phase_str = "prerequisite"
            elif le.phase == "final_boss":
                phase_str = "final_boss"
            else:
                phase_str = le.phase

            lines.append(
                f"  L{le.layer} [{phase_str}] {le.operation} "
                f"{le.branches_before}->{le.branches_after} branches"
            )

            # Nodes
            for node in le.nodes:
                fallback_mark = ""
                if le.planned_type and node.cluster_type != le.planned_type:
                    # Check if this node has a corresponding fallback
                    for fb in le.fallbacks:
                        if fb.actual_type == node.cluster_type:
                            fallback_mark = " *** FALLBACK ***"
                            break
                lines.append(
                    f"    {node.cluster_id} [{node.cluster_type}, w={node.weight}] "
                    f"({node.role}){fallback_mark}"
                )

            # Fallback details
            if le.fallbacks:
                lines.append("    Fallbacks:")
                for fb in le.fallbacks:
                    pool_parts = [f"{t}={c}" for t, c in sorted(fb.pool_remaining.items())]
                    lines.append(
                        f"      b{fb.branch_index}: wanted {fb.preferred_type}, "
                        f"got {fb.actual_type} ({fb.reason}: {', '.join(pool_parts)})"
                    )

            lines.append("")
        lines.append("")

    # CROSSLINKS section
    if log.crosslink_event:
        ce = log.crosslink_event
        lines.append("CROSSLINKS")
        lines.append(
            f"  Eligible pairs: {ce.eligible_pairs}, "
            f"Added: {ce.added}, Skipped: {ce.skipped}"
        )
        if ce.added_details:
            lines.append("  Added:")
            for d in ce.added_details:
                src_layer = f"L{dag.nodes[d.source_id].layer} " if dag and d.source_id in dag.nodes else ""
                tgt_layer = f"L{dag.nodes[d.target_id].layer} " if dag and d.target_id in dag.nodes else ""
                lines.append(f"    {src_layer}{d.source_id} -> {tgt_layer}{d.target_id}")
        if ce.skipped_details:
            lines.append("  Skipped:")
            for d in ce.skipped_details:
                src_layer = f"L{dag.nodes[d.source_id].layer} " if dag and d.source_id in dag.nodes else ""
                tgt_layer = f"L{dag.nodes[d.target_id].layer} " if dag and d.target_id in dag.nodes else ""
                lines.append(f"    {src_layer}{d.source_id} -> {tgt_layer}{d.target_id}: {d.reason}")
        lines.append("")

    # SUMMARY section
    if log.summary:
        s = log.summary
        lines.append("SUMMARY")
        lines.append(f"  Layers: {s.total_layers}")
        lines.append(f"  Nodes: {s.total_nodes}")
        if s.crosslinks:
            lines.append(f"  Crosslinks: {s.crosslinks}")
        lines.append(f"  Fallbacks: {s.fallback_count}")
        if s.fallback_summary:
            parts = [f"L{layer}: {ptype}" for layer, ptype in s.fallback_summary]
            lines.append(f"    {', '.join(parts)}")
        if s.pool_at_end:
            pool_parts = [f"{t}={c}" for t, c in sorted(s.pool_at_end.items())]
            lines.append(f"  Pool at end: {', '.join(pool_parts)}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -v`
Expected: All PASS

- [ ] **Step 5: Write test for fallback serialization**

Append to `tests/test_generation_log.py`:

```python
def test_export_fallback_details(tmp_path):
    log = _make_minimal_log()
    log.layer_events.append(
        LayerEvent(
            layer=22, phase="planned", planned_type="major_boss", operation="PASSANT",
            branches_before=4, branches_after=4,
            nodes=[
                NodeEntry("malenia_8b07", "major_boss", 5, "primary"),
                NodeEntry("lakeside_1dc8", "boss_arena", 1, "passant"),
            ],
            fallbacks=[
                FallbackEntry(
                    branch_index=1,
                    preferred_type="major_boss",
                    actual_type="boss_arena",
                    reason="pool_exhausted",
                    pool_remaining={"major_boss": 0, "boss_arena": 61},
                ),
            ],
        ),
    )
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "*** FALLBACK ***" in text
    assert "b1: wanted major_boss, got boss_arena" in text
    assert "pool_exhausted" in text
    assert "major_boss=0" in text


def test_export_crosslinks(tmp_path):
    log = _make_minimal_log()
    log.crosslink_event = CrosslinkEvent(
        eligible_pairs=10, added=7, skipped=3,
        added_details=[CrosslinkDetail("node_a", "node_b")],
        skipped_details=[CrosslinkDetail("node_c", "node_d", "no_surplus_exits")],
    )
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "CROSSLINKS" in text
    assert "Eligible pairs: 10, Added: 7, Skipped: 3" in text
    assert "node_a -> node_b" in text
    assert "node_c -> node_d: no_surplus_exits" in text
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -v`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add speedfog/generation_log.py tests/test_generation_log.py
git commit -m "feat: add generation log serialization"
```

---

## Chunk 3: GenerationResult + Pool Helper

### Task 3: Extend `GenerationResult` and add pool counting helper

**Files:**
- Modify: `speedfog/generator.py:168-182` (GenerationResult)
- Modify: `speedfog/generation_log.py`
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write test for pool counting helper**

Append to `tests/test_generation_log.py`:

```python
from speedfog.generation_log import compute_pool_remaining
from speedfog.clusters import ClusterData


def _make_test_cluster(cluster_id, ctype, zones):
    return ClusterData(
        id=cluster_id,
        type=ctype,
        zones=zones,
        entry_fogs=[],
        exit_fogs=[],
        weight=1,
    )


def test_compute_pool_remaining():
    pool = [
        _make_test_cluster("a", "major_boss", ["zone_a"]),
        _make_test_cluster("b", "major_boss", ["zone_b"]),
        _make_test_cluster("c", "boss_arena", ["zone_c"]),
    ]
    used_zones = {"zone_a"}
    reserved_zones = frozenset()
    result = compute_pool_remaining(pool, used_zones, reserved_zones)
    assert result == {"major_boss": 1, "boss_arena": 1}


def test_compute_pool_remaining_with_reserved():
    pool = [
        _make_test_cluster("a", "major_boss", ["zone_a"]),
        _make_test_cluster("b", "boss_arena", ["zone_b"]),
    ]
    used_zones: set[str] = set()
    reserved_zones = frozenset({"zone_a"})
    result = compute_pool_remaining(pool, used_zones, reserved_zones)
    assert result == {"major_boss": 0, "boss_arena": 1}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -k "pool_remaining" -v`
Expected: FAIL

- [ ] **Step 3: Add `compute_pool_remaining` to `generation_log.py`**

Add to `speedfog/generation_log.py` (before `export_generation_log`):

```python
from speedfog.clusters import ClusterData


def compute_pool_remaining(
    clusters: list[ClusterData],
    used_zones: set[str],
    reserved_zones: frozenset[str],
) -> dict[str, int]:
    """Count available clusters per type, filtering by used/reserved zones.

    Args:
        clusters: All clusters to count.
        used_zones: Zones already consumed.
        reserved_zones: Zones reserved for final boss / prerequisite.

    Returns:
        Dict of type -> available count.
    """
    counts: dict[str, int] = {}
    for c in clusters:
        if c.type not in counts:
            counts[c.type] = 0
        if not any(z in used_zones or z in reserved_zones for z in c.zones):
            counts[c.type] += 1
    return counts
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -k "pool_remaining" -v`
Expected: PASS

- [ ] **Step 5: Add `log` field to `GenerationResult`**

In `speedfog/generator.py`, add import at the top (around line 20):

```python
from speedfog.generation_log import GenerationLog
```

Modify the `GenerationResult` dataclass (line 168-182) to add the `log` field:

```python
@dataclass
class GenerationResult:
    """Result of DAG generation.

    Attributes:
        dag: The generated DAG.
        seed: The actual seed used for generation.
        validation: Validation result (with any warnings).
        attempts: Number of generation attempts made.
        log: Generation diagnostic log.
    """

    dag: Dag
    seed: int
    validation: ValidationResult
    attempts: int
    log: GenerationLog = field(default_factory=GenerationLog)
```

Also update `generate_with_retry` (line 2547 and 2565) to pass `log=GenerationLog()` in both `GenerationResult(...)` constructors. The actual log population will be wired in later tasks.

- [ ] **Step 6: Run full test suite to verify no regressions**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All existing tests PASS

- [ ] **Step 7: Commit**

```bash
git add speedfog/generation_log.py speedfog/generator.py tests/test_generation_log.py
git commit -m "feat: add pool counting helper and log field to GenerationResult"
```

---

## Chunk 4: Wire Log into `generate_dag`

### Task 4: Emit PlanEvent + start/first_layer/final_boss LayerEvents

This task wires the `GenerationLog` into `generate_dag` for the non-contentious events: plan, start, first_layer, end node, and summary. No fallback detection yet.

**Files:**
- Modify: `speedfog/generator.py:1775-2504` (generate_dag function)
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write integration test for PlanEvent emission**

Append to `tests/test_generation_log.py`:

```python
from speedfog.generator import generate_dag, GenerationResult
from speedfog.clusters import ClusterPool
from speedfog.config import Config


def _make_small_pool():
    """Create a minimal cluster pool for log testing."""
    start = _make_test_cluster("chapel_start", "start", ["chapel"])
    # Add entry/exit fogs manually
    start = ClusterData(
        id="chapel_start",
        type="start",
        zones=["chapel"],
        entry_fogs=[],
        exit_fogs=[{"fog_id": "exit1", "zone": "chapel"}],
        weight=1,
    )
    boss = ClusterData(
        id="test_boss",
        type="final_boss",
        zones=["boss_zone"],
        entry_fogs=[{"fog_id": "entry1", "zone": "boss_zone"}],
        exit_fogs=[],
        weight=1,
    )
    mini1 = ClusterData(
        id="mini1",
        type="mini_dungeon",
        zones=["mini1_zone"],
        entry_fogs=[{"fog_id": "e1", "zone": "mini1_zone"}],
        exit_fogs=[{"fog_id": "x1", "zone": "mini1_zone"}],
        weight=2,
    )
    mini2 = ClusterData(
        id="mini2",
        type="mini_dungeon",
        zones=["mini2_zone"],
        entry_fogs=[{"fog_id": "e2", "zone": "mini2_zone"}],
        exit_fogs=[{"fog_id": "x2", "zone": "mini2_zone"}],
        weight=2,
    )
    mini3 = ClusterData(
        id="mini3",
        type="mini_dungeon",
        zones=["mini3_zone"],
        entry_fogs=[{"fog_id": "e3", "zone": "mini3_zone"}],
        exit_fogs=[{"fog_id": "x3", "zone": "mini3_zone"}],
        weight=2,
    )
    pool = ClusterPool(clusters=[start, boss, mini1, mini2, mini3])
    boss_candidates = [boss]
    return pool, boss_candidates


def test_generate_dag_emits_plan_event():
    """generate_dag populates a GenerationLog with PlanEvent."""
    pool, boss_candidates = _make_small_pool()
    config = Config.from_dict({
        "structure": {
            "min_layers": 3,
            "max_layers": 5,
            "max_parallel_paths": 1,
            "crosslinks": False,
            "final_boss_candidates": {"boss_zone": 1},
        },
        "requirements": {
            "legacy_dungeons": 0,
            "bosses": 0,
            "mini_dungeons": 1,
            "major_bosses": 0,
        },
    })
    dag, log = generate_dag(
        config, pool, seed=42, boss_candidates=boss_candidates
    )
    assert log is not None
    assert log.plan_event is not None
    assert log.plan_event.final_boss == "test_boss"
    assert len(log.layer_events) >= 3  # start + at least 1 planned + final_boss
    assert log.layer_events[0].phase == "start"
    assert log.layer_events[-1].phase == "final_boss"
    assert log.summary is not None
    assert log.summary.total_nodes == len(dag.nodes)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py::test_generate_dag_emits_plan_event -v`
Expected: FAIL (generate_dag returns Dag, not tuple)

- [ ] **Step 3: Modify `generate_dag` to create and return the log**

This is the core wiring. In `speedfog/generator.py`:

**a) Change `generate_dag` return type** (line 1775): from `-> Dag` to `-> tuple[Dag, GenerationLog]`.

**b) Add imports** at the top of generator.py:

```python
from speedfog.generation_log import (
    GenerationLog,
    LayerEvent,
    NodeEntry,
    PlanEvent,
    SummaryEvent,
    compute_pool_remaining,
)
```

**c) At the top of `generate_dag`** (after line 1809, `used_zones: set[str] = set()`), create the log:

```python
log = GenerationLog()
```

**d) After step 1 (start node creation, ~line 1833)**, emit start LayerEvent:

```python
log.layer_events.append(LayerEvent(
    layer=0, phase="start", planned_type=None, operation="START",
    branches_before=0, branches_after=len(branches),
    nodes=[NodeEntry(start_cluster.id, start_cluster.type, start_cluster.weight, "start")],
))
```

Note: Insert AFTER the branches list is constructed (after line 1852).

**e) After step 4 (first_layer_type, ~line 1914)**, emit first_layer LayerEvent:

```python
if config.structure.first_layer_type:
    # ... existing code ...
    # After the loop and update_branch_counters:
    first_layer_event = LayerEvent(
        layer=1, phase="first_layer",
        planned_type=config.structure.first_layer_type,
        operation="PASSANT",
        branches_before=num_initial_branches,
        branches_after=len(branches),
    )
    for b in new_branches:
        node = dag.nodes[b.current_node_id]
        first_layer_event.nodes.append(
            NodeEntry(node.cluster.id, node.cluster.type, node.cluster.weight, "passant")
        )
    log.layer_events.append(first_layer_event)
```

**f) After step 5 (plan_layer_types, ~line 1940)**, emit PlanEvent:

```python
all_pool_sizes = {
    t: len(clusters.get_by_type(t))
    for t in ("mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss")
}
log.plan_event = PlanEvent(
    requirements={
        "legacy_dungeon": config.requirements.legacy_dungeons,
        "boss_arena": config.requirements.bosses,
        "mini_dungeon": config.requirements.mini_dungeons,
        "major_boss": config.requirements.major_bosses,
    },
    target_total=target_total,
    merge_reserve=merge_reserve,
    num_intermediate=num_intermediate_layers,
    first_layer_type=config.structure.first_layer_type,
    planned_types=list(layer_types),
    pool_sizes=all_pool_sizes,
    final_boss=end_cluster.id,
    reserved_zones=set(reserved_zones),
)
```

**g) After step 8 (prerequisite injection, ~line 2445)**, emit prerequisite LayerEvent if injected:

```python
# _inject_prerequisite returns (branches, current_layer).
# If current_layer changed, a prerequisite was injected.
old_layer = current_layer
branches, current_layer = _inject_prerequisite(...)
if current_layer != old_layer:
    prereq_node = dag.nodes[branches[0].current_node_id]
    log.layer_events.append(LayerEvent(
        layer=old_layer, phase="prerequisite", planned_type=None,
        operation="PASSANT", branches_before=1, branches_after=1,
        nodes=[NodeEntry(prereq_node.cluster.id, prereq_node.cluster.type,
                         prereq_node.cluster.weight, "passant")],
    ))
```

**h) After step 9 (end node, ~line 2483)**, emit final_boss LayerEvent:

```python
log.layer_events.append(LayerEvent(
    layer=end_node.layer, phase="final_boss", planned_type=None,
    operation="FINAL", branches_before=1, branches_after=0,
    nodes=[NodeEntry(end_cluster.id, end_cluster.type, end_cluster.weight, "final_boss")],
))
```

**h) Before the return** (before `return dag`, ~line 2504), emit SummaryEvent and return tuple:

```python
# Compute summary
all_clusters = []
for t in ("mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"):
    all_clusters.extend(clusters.get_by_type(t))
pool_at_end = compute_pool_remaining(all_clusters, used_zones, reserved_zones)

convergence_count = sum(1 for le in log.layer_events if le.phase == "convergence")
planned_count = sum(1 for le in log.layer_events if le.phase == "planned")
fallback_count = sum(len(le.fallbacks) for le in log.layer_events)
fallback_summary = [
    (le.layer, fb.preferred_type)
    for le in log.layer_events
    for fb in le.fallbacks
]

log.summary = SummaryEvent(
    total_layers=total_layers,
    total_nodes=len(dag.nodes),
    planned_layers=planned_count,
    convergence_layers=convergence_count,
    crosslinks=dag.crosslinks_added,
    fallback_count=fallback_count,
    fallback_summary=fallback_summary,
    pool_at_end=pool_at_end,
)

return dag, log
```

**i) Update all callers of `generate_dag`:**

In `generate_with_retry` (~line 2540 and 2560), update the calls:

```python
dag, log = generate_dag(config, clusters, seed, boss_candidates=boss_candidates)
```

And pass `log=log` to `GenerationResult(...)`.

- [ ] **Step 4: Update ALL existing callers of `generate_dag`**

`generate_dag` now returns `tuple[Dag, GenerationLog]` instead of `Dag`. Every caller must be updated to unpack: `dag, _log = generate_dag(...)`.

Run this to find all callers:

```bash
cd /home/dev/src/games/ER/fog/speedfog && grep -rn "generate_dag(" tests/ speedfog/
```

Expected locations (~35+ call sites in test_generator.py, ~5 in test_integration.py, 2 in generate_with_retry). Update every single one. For tests, use `dag, _log = generate_dag(...)`. For `generate_with_retry`, use `dag, log = generate_dag(...)` and pass `log=log` to GenerationResult.

Also add `from dataclasses import dataclass, field` import in generator.py (currently only imports `dataclass`).

- [ ] **Step 5: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/
git commit -m "feat: wire GenerationLog into generate_dag (plan, start, first_layer, final_boss, summary)"
```

---

### Task 5: Emit LayerEvents for planned layers (main loop)

**Files:**
- Modify: `speedfog/generator.py:1942-2314` (step 6 main loop)
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write test for planned layer logging**

Append to `tests/test_generation_log.py`:

```python
def test_planned_layers_have_events():
    """Each planned layer emits a LayerEvent with operation and nodes."""
    pool, boss_candidates = _make_small_pool()
    config = Config.from_dict({
        "structure": {
            "min_layers": 4,
            "max_layers": 5,
            "max_parallel_paths": 1,
            "crosslinks": False,
            "final_boss_candidates": {"boss_zone": 1},
        },
        "requirements": {
            "legacy_dungeons": 0,
            "bosses": 0,
            "mini_dungeons": 2,
            "major_bosses": 0,
        },
    })
    dag, log = generate_dag(
        config, pool, seed=42, boss_candidates=boss_candidates
    )
    planned = [le for le in log.layer_events if le.phase == "planned"]
    assert len(planned) >= 1
    for le in planned:
        assert le.operation in ("PASSANT", "SPLIT", "MERGE", "REBALANCE")
        assert len(le.nodes) >= 1
        assert le.planned_type is not None
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py::test_planned_layers_have_events -v`
Expected: FAIL (no planned LayerEvents emitted yet)

- [ ] **Step 3: Add LayerEvent emission at end of each main loop iteration**

In `speedfog/generator.py`, inside the `for layer_type in layer_types:` loop (step 6), at each operation branch (REBALANCE, SPLIT, MERGE, PASSANT), build and append a `LayerEvent`.

The approach: create a `layer_event` at the top of each iteration, populate it in each operation branch, and append it after `current_layer += 1`.

At the **top of the loop** (after `primary_cluster` is picked, ~line 1957):

```python
layer_event = LayerEvent(
    layer=current_layer, phase="planned", planned_type=layer_type,
    operation="PASSANT",  # default, overridden below
    branches_before=len(branches), branches_after=0,
)
```

For **REBALANCE** (line 1968-1993): Modify `execute_rebalance_layer` (and `_rebalance_merge_first`, `_rebalance_split_first`) to accept an optional `log_events: list[LayerEvent] | None` parameter. When provided, the function appends LayerEvent(s) to this list:
- **Split-first (N>=3):** Appends 1 LayerEvent at `layer_idx` with nodes having roles `rebalance_split`, `rebalance_merge`, `rebalance_passant`.
- **Merge-first (N=2):** Appends 2 LayerEvents: one at `layer_idx` (merge, role `rebalance_merge`) and one at `layer_idx+1` (split, role `rebalance_split`).

In the main loop, if rebalance succeeds, set `layer_event = None` (skip default append) and extend `log.layer_events` with the returned events. If rebalance fails (fallthrough), continue with re-decided operation.

```python
if operation == LayerOperation.REBALANCE:
    rebal_log_events: list[LayerEvent] = []
    rebal_result = execute_rebalance_layer(
        ..., log_events=rebal_log_events,
    )
    if rebal_result is not None:
        branches = rebal_result[0]
        current_layer += rebal_result[1]
        log.layer_events.extend(rebal_log_events)
        continue
```

Same pattern applies in the convergence loop for REBALANCE.

For **SPLIT** (line 1995-2096): Set `layer_event.operation = "SPLIT"`. Add nodes: primary as `"primary"`, split children, passant nodes.

For **MERGE** (line 2098-2254): Set `layer_event.operation = "MERGE"`. If merge succeeds, add merge_target and passant nodes. If merge falls back to PASSANT (line 2129), `layer_event.operation = "PASSANT"`.

For **PASSANT** (line 2256-2312): Keep operation as `"PASSANT"`. Add primary and passant nodes.

**After `current_layer += 1`** (line 2314):

```python
if layer_event is not None:
    layer_event.branches_after = len(branches)
    log.layer_events.append(layer_event)
```

The detailed node population for each branch:
- In SPLIT: after line 2026 (dag.add_node(node)), append `NodeEntry(primary_cluster.id, primary_cluster.type, primary_cluster.weight, "primary")`. After passant node creation (line 2078), append with role `"passant"`. Split children don't create separate nodes (they share the split node).
- In MERGE: after merge_node creation (line 2162), append with role `"merge_target"`. After passant node creation (line 2236), append with role `"passant"`.
- In PASSANT: first branch gets role `"primary"`, rest get `"passant"`.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_generation_log.py -v`
Expected: All PASS

- [ ] **Step 5: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add speedfog/generator.py tests/test_generation_log.py
git commit -m "feat: emit LayerEvents for planned layers in main loop"
```

---

### Task 6: Emit LayerEvents for convergence layers

**Files:**
- Modify: `speedfog/generator.py:2316-2434` (step 7 convergence loop)
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write test for convergence logging**

Append to `tests/test_generation_log.py`:

```python
def test_convergence_layers_have_events():
    """Convergence layers emit LayerEvents with dynamically chosen type."""
    pool, boss_candidates = _make_small_pool()
    # Need at least 2 branches to trigger convergence
    # Add more clusters for a wider pool
    extra = [
        ClusterData(
            id=f"extra_{i}", type="mini_dungeon", zones=[f"extra_{i}_zone"],
            entry_fogs=[{"fog_id": f"e{i}", "zone": f"extra_{i}_zone"}],
            exit_fogs=[{"fog_id": f"x{i}", "zone": f"extra_{i}_zone"}],
            weight=1,
        )
        for i in range(20)
    ]
    start = pool.clusters[0]  # chapel_start
    boss = pool.clusters[1]   # test_boss
    all_clusters = [start, boss] + extra
    # Add a start with 2 exits for branching
    start2 = ClusterData(
        id="chapel_start", type="start", zones=["chapel"],
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "exit1", "zone": "chapel"},
            {"fog_id": "exit2", "zone": "chapel"},
        ],
        weight=1,
    )
    all_clusters[0] = start2
    pool2 = ClusterPool(clusters=all_clusters)

    config = Config.from_dict({
        "structure": {
            "min_layers": 5,
            "max_layers": 8,
            "max_parallel_paths": 2,
            "crosslinks": False,
            "final_boss_candidates": {"boss_zone": 1},
        },
        "requirements": {
            "legacy_dungeons": 0,
            "bosses": 0,
            "mini_dungeons": 2,
            "major_bosses": 0,
        },
    })
    dag, log = generate_dag(
        config, pool2, seed=42, boss_candidates=[boss],
    )
    convergence = [le for le in log.layer_events if le.phase == "convergence"]
    # May or may not have convergence depending on seed, but structure is valid
    for le in convergence:
        assert le.planned_type is not None  # dynamically chosen type
        assert le.operation in ("MERGE", "PASSANT", "REBALANCE")
```

- [ ] **Step 2: Add convergence LayerEvent emission**

In the convergence `while len(branches) > 1:` loop (line 2323-2434), emit a `LayerEvent` at the end of each iteration, similar to the main loop. Set `phase="convergence"` and `planned_type=conv_layer_type` (the type chosen by `pick_weighted_type` at line 2330).

Create the `layer_event` after `conv_layer_type` is determined. For REBALANCE, MERGE, and PASSANT branches within convergence, populate nodes the same way as in the main loop.

- [ ] **Step 3: Run tests**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All PASS

- [ ] **Step 4: Commit**

```bash
git add speedfog/generator.py tests/test_generation_log.py
git commit -m "feat: emit LayerEvents for convergence layers"
```

---

## Chunk 5: Fallback Detection

### Task 7: Add fallback detection at all call sites

This is the most complex task. Fallback detection happens at 6+ sites in the code. The approach: at each site where a cluster is picked for a branch, compare `cluster.type != expected_type` and record a `FallbackEntry`.

**Files:**
- Modify: `speedfog/generator.py` (main loop SPLIT/MERGE/PASSANT branches, convergence helpers)
- Modify: `speedfog/generator.py:1335-1424` (execute_passant_layer: add `log_event` param)
- Modify: `speedfog/generator.py:1484-1696` (execute_merge_layer: add `log_event` param)
- Modify: `speedfog/generator.py:922-991,1127-1332` (execute_rebalance_layer + _rebalance_split_first: add `log_event` param)
- Test: `tests/test_generation_log.py`

- [ ] **Step 1: Write test for fallback detection**

Append to `tests/test_generation_log.py`:

```python
def test_fallback_recorded_when_pool_exhausted():
    """When a type pool is exhausted, fallback is recorded in the log."""
    # Create a pool with only 1 major_boss cluster, but require 1 major_boss layer
    # with 2 parallel branches -> second branch must fallback
    start = ClusterData(
        id="start", type="start", zones=["start_zone"],
        entry_fogs=[], exit_fogs=[
            {"fog_id": "x1", "zone": "start_zone"},
            {"fog_id": "x2", "zone": "start_zone"},
        ],
        weight=1,
    )
    boss = ClusterData(
        id="final", type="final_boss", zones=["final_zone"],
        entry_fogs=[{"fog_id": "e1", "zone": "final_zone"}],
        exit_fogs=[], weight=1,
    )
    mb1 = ClusterData(
        id="mb1", type="major_boss", zones=["mb1_zone"],
        entry_fogs=[{"fog_id": "e1", "zone": "mb1_zone"}],
        exit_fogs=[{"fog_id": "x1", "zone": "mb1_zone"}],
        weight=2,
    )
    # Fallback targets
    minis = [
        ClusterData(
            id=f"mini_{i}", type="mini_dungeon", zones=[f"mini_{i}_zone"],
            entry_fogs=[{"fog_id": f"e{i}", "zone": f"mini_{i}_zone"}],
            exit_fogs=[{"fog_id": f"x{i}", "zone": f"mini_{i}_zone"}],
            weight=2,
        )
        for i in range(20)
    ]
    pool = ClusterPool(clusters=[start, boss, mb1] + minis)
    config = Config.from_dict({
        "structure": {
            "min_layers": 4,
            "max_layers": 5,
            "max_parallel_paths": 2,
            "crosslinks": False,
            "final_boss_candidates": {"final_zone": 1},
        },
        "requirements": {
            "legacy_dungeons": 0,
            "bosses": 0,
            "mini_dungeons": 1,
            "major_bosses": 1,
        },
    })
    dag, log = generate_dag(config, pool, seed=42, boss_candidates=[boss])
    # Find fallback events
    all_fallbacks = [fb for le in log.layer_events for fb in le.fallbacks]
    # With only 1 major_boss cluster and 2 branches, at least one branch should fallback
    # (unless the major_boss layer happens to have only 1 branch)
    # Check the summary reflects fallbacks correctly
    assert log.summary is not None
    assert log.summary.fallback_count == len(all_fallbacks)
```

- [ ] **Step 2: Add `log_event` parameter to helper functions**

Add `log_event: LayerEvent | None = None` parameter to:

- `execute_passant_layer` (line 1345): add after `reserved_zones` parameter
- `execute_merge_layer` (line 1495): add after `min_age` parameter

Import `FallbackEntry` and `NodeEntry` at the top of generator.py.

In `execute_passant_layer`, after each cluster is picked (line 1398), add fallback detection:

```python
if log_event is not None:
    role = "passant"
    log_event.nodes.append(NodeEntry(cluster.id, cluster.type, cluster.weight, role))
    if cluster.type != layer_type:
        all_typed = []
        for t in ("mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"):
            all_typed.extend(clusters.get_by_type(t))
        log_event.fallbacks.append(FallbackEntry(
            branch_index=i,
            preferred_type=layer_type,
            actual_type=cluster.type,
            reason="pool_exhausted",
            pool_remaining=compute_pool_remaining(
                all_typed, used_zones, reserved_zones,
            ),
        ))
```

Apply the same pattern in `execute_merge_layer` for passant branches within merge (line 1638-1695).

In `_rebalance_split_first` (line 1127), add `log_event` parameter. After the passant cluster pick at line 1294, add fallback detection.

- [ ] **Step 3: Add fallback detection in the main loop**

In the main loop (step 6), at each of the 6 fallback sites identified in the spec:

**PASSANT primary** (line ~2262): After `primary_cluster` is selected by `_pick_cluster_biased_for_split`, check `primary_cluster.type != layer_type`.

**PASSANT secondary** (line ~2265-2280): After each passant cluster is picked, check type mismatch.

**SPLIT passant** (line ~2046-2061): After each passant cluster in split, check type mismatch.

**MERGE passant** (line ~2204-2219): After each passant cluster in merge, check type mismatch.

For convergence helpers, pass `log_event` when calling `execute_merge_layer` and `execute_passant_layer` from the convergence loop.

- [ ] **Step 4: Run tests**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add speedfog/generator.py tests/test_generation_log.py
git commit -m "feat: add fallback detection at all cluster selection sites"
```

---

## Chunk 6: Crosslinks Logging

### Task 8: Return `CrosslinkEvent` from `add_crosslinks`

**Files:**
- Modify: `speedfog/crosslinks.py:178-216` (add_crosslinks)
- Modify: `speedfog/generator.py:2485-2487` (call site)
- Test: `tests/test_crosslinks.py`

- [ ] **Step 1: Write test for CrosslinkEvent return**

Add to `tests/test_crosslinks.py`:

```python
def test_add_crosslinks_returns_event():
    """add_crosslinks returns (count, CrosslinkEvent) tuple."""
    from speedfog.generation_log import CrosslinkEvent
    dag = make_diamond_dag()  # use existing fixture
    rng = random.Random(42)
    count, event = add_crosslinks(dag, rng)
    assert isinstance(event, CrosslinkEvent)
    assert event.added == count
    assert event.added + event.skipped <= event.eligible_pairs
    assert len(event.added_details) == event.added
    assert len(event.skipped_details) == event.skipped
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_crosslinks.py::test_add_crosslinks_returns_event -v`
Expected: FAIL (returns int, not tuple)

- [ ] **Step 3: Modify `add_crosslinks` to return `(int, CrosslinkEvent)`**

In `speedfog/crosslinks.py`:

```python
from speedfog.generation_log import CrosslinkDetail, CrosslinkEvent

def add_crosslinks(
    dag: Dag,
    rng: random.Random,
    clusters: ClusterPool | None = None,
) -> tuple[int, CrosslinkEvent]:
    pairs = find_eligible_pairs(dag)
    event = CrosslinkEvent(eligible_pairs=len(pairs), added=0, skipped=0)

    if not pairs:
        return 0, event

    rng.shuffle(pairs)

    added = 0
    for src_id, tgt_id in pairs:
        src_surplus = _surplus_exits(dag, src_id)
        tgt_surplus = _available_entries(dag, tgt_id)
        if not src_surplus or not tgt_surplus:
            reason = "no_surplus_exits" if not src_surplus else "no_available_entries"
            event.skipped += 1
            event.skipped_details.append(CrosslinkDetail(src_id, tgt_id, reason))
            continue

        exit_fog = rng.choice(src_surplus)
        entry_fog = rng.choice(tgt_surplus)
        dag.add_edge(src_id, tgt_id, exit_fog, entry_fog)
        dag.nodes[src_id].exit_fogs.append(exit_fog)
        dag.nodes[tgt_id].entry_fogs.append(entry_fog)
        added += 1
        event.added += 1
        event.added_details.append(CrosslinkDetail(src_id, tgt_id))

    return added, event
```

- [ ] **Step 4: Update call site in `generator.py`**

At line 2487, change:

```python
dag.crosslinks_added = add_crosslinks(dag, rng, clusters)
```

to:

```python
crosslink_count, crosslink_event = add_crosslinks(dag, rng, clusters)
dag.crosslinks_added = crosslink_count
log.crosslink_event = crosslink_event
```

- [ ] **Step 5: Update ALL callers of `add_crosslinks`**

`add_crosslinks` now returns `tuple[int, CrosslinkEvent]` instead of `int`. Every caller must be updated.

Run: `cd /home/dev/src/games/ER/fog/speedfog && grep -rn "add_crosslinks(" tests/ speedfog/`

Expected locations: ~8 call sites in test_crosslinks.py, 1 in generator.py. Update each to unpack: `count, _event = add_crosslinks(...)` (in tests) or `count, event = add_crosslinks(...)` (in generator.py).

- [ ] **Step 6: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add speedfog/crosslinks.py speedfog/generator.py tests/test_crosslinks.py
git commit -m "feat: return CrosslinkEvent from add_crosslinks"
```

---

## Chunk 7: CLI and File Layout

### Task 9: Rename `--spoiler` to `--logs` and create `logs/` directory

**Files:**
- Modify: `speedfog/main.py:85-89,286-289,364-365`
- Test: `tests/test_integration.py`

- [ ] **Step 1: Modify CLI argument**

In `speedfog/main.py`, change the argument definition (line 85-89):

```python
parser.add_argument(
    "--logs",
    action="store_true",
    help="Generate diagnostic logs (spoiler.txt, generation.log)",
)
```

- [ ] **Step 2: Update spoiler generation block**

Change line 286-289:

```python
if args.logs:
    logs_dir = seed_dir / "logs"
    logs_dir.mkdir(parents=True, exist_ok=True)
    spoiler_path = logs_dir / "spoiler.txt"
    export_spoiler_log(dag, spoiler_path, care_package=care_package_items)
    print(f"Written: {spoiler_path}")
```

- [ ] **Step 3: Add generation.log export**

After the spoiler export, add:

```python
    from speedfog.generation_log import export_generation_log
    gen_log_path = logs_dir / "generation.log"
    export_generation_log(result.log, gen_log_path, dag=dag)
    print(f"Written: {gen_log_path}")
```

Note: `result` is the `GenerationResult` from line 198. `result.log` contains the `GenerationLog`.

- [ ] **Step 4: Update boss placements block**

Change line 364-365:

```python
if args.logs:
    append_boss_placements_to_spoiler(spoiler_path, boss_placements)
```

- [ ] **Step 5: Update any reference to `args.spoiler`**

Search `main.py` for `args.spoiler` and replace all with `args.logs`.

- [ ] **Step 6: Run full test suite**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/ -v --tb=short`
Expected: All PASS (integration tests may need updating if they reference `--spoiler`)

- [ ] **Step 7: Commit**

```bash
git add speedfog/main.py
git commit -m "feat: rename --spoiler to --logs, write to logs/ subdirectory"
```

---

## Chunk 8: Documentation

### Task 10: Update documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `docs/architecture.md`
- Modify: `docs/care-package.md`

- [ ] **Step 1: Update CLAUDE.md**

Update the directory structure diagram to show `logs/` directory. Change `--spoiler` to `--logs` in the Commands section. Add `generation_log.py` to the directory listing.

- [ ] **Step 2: Update README.md**

Change `--spoiler` to `--logs` in usage examples. Update the output description.

- [ ] **Step 3: Update `docs/architecture.md`**

Update the output diagram and seed directory layout. Add `generation_log.py` to the module table.

- [ ] **Step 4: Update `docs/care-package.md`**

Change reference from `--spoiler` to `--logs`.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md README.md docs/architecture.md docs/care-package.md
git commit -m "docs: update references from --spoiler to --logs, add generation_log.py"
```

---

## Chunk 9: Final Integration Test

### Task 11: End-to-end integration test

**Files:**
- Modify: `tests/test_integration.py`

- [ ] **Step 1: Write integration test with real clusters**

```python
def test_generation_log_with_real_clusters(real_clusters, real_boss_candidates, tmp_path):
    """Full generation produces a valid log with all sections."""
    from speedfog.generation_log import export_generation_log

    config = Config.from_dict({
        "structure": {
            "min_layers": 10,
            "max_layers": 15,
            "max_parallel_paths": 2,
            "crosslinks": True,
            "final_boss_candidates": {"haligtree_malenia": 1},
        },
        "requirements": {
            "legacy_dungeons": 1,
            "bosses": 2,
            "mini_dungeons": 2,
            "major_bosses": 2,
        },
    })
    dag, log = generate_dag(
        config, real_clusters, seed=42, boss_candidates=real_boss_candidates
    )

    # Verify log structure
    assert log.plan_event is not None
    assert len(log.layer_events) >= 10
    assert log.crosslink_event is not None
    assert log.summary is not None
    assert log.summary.total_nodes == len(dag.nodes)

    # Verify serialization
    log_path = tmp_path / "generation.log"
    export_generation_log(log, log_path)
    text = log_path.read_text()
    assert "PLAN" in text
    assert "LAYERS" in text
    assert "CROSSLINKS" in text
    assert "SUMMARY" in text

    # Every layer in the DAG should have a corresponding LayerEvent
    max_layer = max(n.layer for n in dag.nodes.values())
    logged_layers = {le.layer for le in log.layer_events}
    for layer in range(max_layer + 1):
        assert layer in logged_layers, f"Layer {layer} missing from log"
```

- [ ] **Step 2: Run the integration test**

Run: `cd /home/dev/src/games/ER/fog/speedfog && uv run pytest tests/test_integration.py::test_generation_log_with_real_clusters -v`
Expected: PASS (requires `data/clusters.json` to exist)

- [ ] **Step 3: Commit**

```bash
git add tests/test_integration.py
git commit -m "test: add end-to-end integration test for generation log"
```
