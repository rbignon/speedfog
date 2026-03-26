# tests/test_generation_log.py

from speedfog.generation_log import (
    CrosslinkDetail,
    CrosslinkEvent,
    FallbackEntry,
    GenerationLog,
    LayerEvent,
    NodeEntry,
    PlanEvent,
    SummaryEvent,
    export_generation_log,
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


def _make_minimal_log():
    """Build a log with one plan event and two layers for testing."""
    log = GenerationLog()
    log.plan_event = PlanEvent(
        seed=42,
        requirements={
            "legacy_dungeon": 2,
            "boss_arena": 3,
            "mini_dungeon": 4,
            "major_boss": 2,
        },
        target_total=15,
        merge_reserve=4,
        num_intermediate=12,
        first_layer_type="legacy_dungeon",
        planned_types=["legacy_dungeon", "boss_arena", "mini_dungeon"],
        pool_sizes={
            "boss_arena": 84,
            "legacy_dungeon": 32,
            "mini_dungeon": 69,
            "major_boss": 40,
        },
        final_boss="jaggedpeak_bayle_f21a",
        reserved_zones={"jaggedpeak_bayle"},
    )
    log.layer_events = [
        LayerEvent(
            layer=0,
            phase="start",
            planned_type=None,
            operation="START",
            branches_before=0,
            branches_after=2,
            nodes=[NodeEntry("chapel_start_abc", "start", 1, "start")],
        ),
        LayerEvent(
            layer=1,
            phase="first_layer",
            planned_type="legacy_dungeon",
            operation="PASSANT",
            branches_before=2,
            branches_after=2,
            nodes=[
                NodeEntry("stormveil_db4a", "legacy_dungeon", 8, "passant"),
                NodeEntry("caelid_a51c", "legacy_dungeon", 3, "passant"),
            ],
        ),
    ]
    log.summary = SummaryEvent(
        total_layers=3,
        total_nodes=5,
        planned_layers=1,
        convergence_layers=0,
        crosslinks=0,
        fallback_count=0,
        fallback_summary=[],
        pool_at_end={},
    )
    return log


def test_export_plan_section(tmp_path):
    log = _make_minimal_log()
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "GENERATION LOG" in text
    assert "seed: 42" in text
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


def test_export_fallback_details(tmp_path):
    log = _make_minimal_log()
    log.layer_events.append(
        LayerEvent(
            layer=22,
            phase="planned",
            planned_type="major_boss",
            operation="PASSANT",
            branches_before=4,
            branches_after=4,
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
        eligible_pairs=10,
        added=7,
        skipped=3,
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


def test_export_convergence_pool_snapshot(tmp_path):
    log = _make_minimal_log()
    log.layer_events.append(
        LayerEvent(
            layer=24,
            phase="convergence",
            planned_type="mini_dungeon",
            operation="MERGE",
            branches_before=4,
            branches_after=3,
            pool_snapshot={"boss_arena": 58, "mini_dungeon": 43},
        ),
    )
    path = tmp_path / "generation.log"
    export_generation_log(log, path)
    text = path.read_text()
    assert "CONVERGENCE (4 branches remaining)" in text
    assert "Pool: boss_arena=58, mini_dungeon=43" in text
    assert "convergence=mini_dungeon" in text
