# tests/test_generation_log.py

from speedfog.generation_log import (
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
