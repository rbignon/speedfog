# tests/test_generation_log.py

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.config import Config
from speedfog.generation_log import (
    CrosslinkDetail,
    CrosslinkEvent,
    FallbackEntry,
    GenerationLog,
    LayerEvent,
    NodeEntry,
    PlanEvent,
    SummaryEvent,
    compute_pool_remaining,
    export_generation_log,
)
from speedfog.generator import generate_dag


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


def _make_cluster(
    cluster_id: str,
    ctype: str,
    zones: list[str],
    weight: int = 5,
    entry_fogs: list[dict] | None = None,
    exit_fogs: list[dict] | None = None,
) -> ClusterData:
    if entry_fogs is None:
        entry_fogs = [{"fog_id": f"{cluster_id}_entry", "zone": zones[0]}]
    if exit_fogs is None:
        exit_fogs = [
            {"fog_id": f"{cluster_id}_exit", "zone": zones[0]},
            {"fog_id": f"{cluster_id}_entry", "zone": zones[0]},
        ]
    return ClusterData(
        id=cluster_id,
        type=ctype,
        zones=zones,
        weight=weight,
        entry_fogs=entry_fogs,
        exit_fogs=exit_fogs,
    )


def _make_small_pool() -> tuple[ClusterPool, list[ClusterData]]:
    """Create a minimal ClusterPool and boss_candidates for log tests."""
    pool = ClusterPool()

    # Start cluster
    pool.add(
        _make_cluster(
            "chapel_start",
            "start",
            ["chapel"],
            weight=1,
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "start_exit_1", "zone": "chapel"},
                {"fog_id": "start_exit_2", "zone": "chapel"},
            ],
        )
    )

    # Final boss (zone name matches final_boss_candidates key in test config)
    pool.add(
        _make_cluster(
            "boss_zone",
            "final_boss",
            ["boss_zone"],
            weight=5,
            entry_fogs=[{"fog_id": "boss_zone_entry", "zone": "boss_zone"}],
            exit_fogs=[],
        )
    )

    # Mini dungeons (enough for all layers including convergence)
    for i in range(30):
        pool.add(
            _make_cluster(
                f"mini_{i}",
                "mini_dungeon",
                [f"mini_{i}_zone"],
                weight=3,
            )
        )

    boss_candidates = pool.get_by_type("final_boss")
    return pool, boss_candidates


def test_planned_layers_have_events():
    """Each planned layer emits a LayerEvent with operation and nodes."""
    pool, boss_candidates = _make_small_pool()
    config = Config.from_dict(
        {
            "structure": {
                "min_layers": 4,
                "max_layers": 5,
                "max_branches": 1,
                "split_probability": 0.0,
                "merge_probability": 0.0,
                "crosslinks": False,
                "final_boss_candidates": {"boss_zone": 1},
            },
            "requirements": {
                "legacy_dungeons": 0,
                "bosses": 0,
                "mini_dungeons": 2,
                "major_bosses": 0,
            },
        }
    )
    dag, log = generate_dag(config, pool, seed=42, boss_candidates=boss_candidates)
    planned = [le for le in log.layer_events if le.phase == "planned"]
    assert len(planned) >= 1
    for le in planned:
        assert le.operation in ("PASSANT", "SPLIT", "MERGE", "REBALANCE")
        assert len(le.nodes) >= 1
        assert le.planned_type is not None


def test_convergence_layers_have_events():
    """Convergence layers emit LayerEvents with phase='convergence'."""
    pool = ClusterPool()
    pool.add(
        ClusterData(
            id="chapel_start",
            type="start",
            zones=["chapel"],
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "chapel"},
                {"fog_id": "exit2", "zone": "chapel"},
            ],
            weight=1,
        )
    )
    boss = ClusterData(
        id="test_boss",
        type="final_boss",
        zones=["boss_zone"],
        entry_fogs=[{"fog_id": "entry1", "zone": "boss_zone"}],
        exit_fogs=[],
        weight=1,
    )
    pool.add(boss)
    for i in range(30):
        pool.add(
            ClusterData(
                id=f"mini_{i}",
                type="mini_dungeon",
                zones=[f"mini_{i}_zone"],
                entry_fogs=[
                    {"fog_id": f"e{i}a", "zone": f"mini_{i}_zone"},
                    {"fog_id": f"e{i}b", "zone": f"mini_{i}_zone"},
                ],
                exit_fogs=[
                    {"fog_id": f"x{i}a", "zone": f"mini_{i}_zone"},
                    {"fog_id": f"x{i}b", "zone": f"mini_{i}_zone"},
                ],
                weight=1,
            )
        )
    config = Config.from_dict(
        {
            "structure": {
                "min_layers": 4,
                "max_layers": 6,
                "max_parallel_paths": 2,
                "max_branches": 2,
                "crosslinks": False,
                "final_boss_candidates": {"boss_zone": 1},
                # Force a split on the first layer then let convergence handle merging
                "split_probability": 1.0,
                "merge_probability": 0.5,
            },
            "requirements": {
                "legacy_dungeons": 0,
                "bosses": 0,
                "mini_dungeons": 2,
                "major_bosses": 0,
            },
        }
    )
    dag, log = generate_dag(config, pool, seed=42, boss_candidates=[boss])
    convergence = [le for le in log.layer_events if le.phase == "convergence"]
    # With 2 branches, convergence must happen at least once
    assert len(convergence) >= 1
    for le in convergence:
        assert le.planned_type is not None
        assert le.operation in ("MERGE", "PASSANT", "REBALANCE")
    # First convergence layer should have pool_snapshot
    assert convergence[0].pool_snapshot is not None


def test_fallback_recorded_when_pool_exhausted():
    """When a type pool is exhausted, fallback is recorded in the log."""
    pool = ClusterPool()
    pool.add(
        ClusterData(
            id="start",
            type="start",
            zones=["start_zone"],
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "x1", "zone": "start_zone"},
                {"fog_id": "x2", "zone": "start_zone"},
            ],
            weight=1,
        )
    )
    boss = ClusterData(
        id="final",
        type="final_boss",
        zones=["final_zone"],
        entry_fogs=[{"fog_id": "e1", "zone": "final_zone"}],
        exit_fogs=[],
        weight=1,
    )
    pool.add(boss)
    pool.add(
        ClusterData(
            id="mb1",
            type="major_boss",
            zones=["mb1_zone"],
            entry_fogs=[{"fog_id": "mb1_e1", "zone": "mb1_zone"}],
            exit_fogs=[{"fog_id": "mb1_x1", "zone": "mb1_zone"}],
            weight=2,
        )
    )
    for i in range(30):
        pool.add(
            ClusterData(
                id=f"mini_{i}",
                type="mini_dungeon",
                zones=[f"mini_{i}_zone"],
                entry_fogs=[{"fog_id": f"e{i}", "zone": f"mini_{i}_zone"}],
                exit_fogs=[{"fog_id": f"x{i}", "zone": f"mini_{i}_zone"}],
                weight=2,
            )
        )
    config = Config.from_dict(
        {
            "structure": {
                "min_layers": 4,
                "max_layers": 5,
                "max_branches": 1,
                "split_probability": 0.0,
                "merge_probability": 0.0,
                "crosslinks": False,
                "final_boss_candidates": {"final_zone": 1},
            },
            "requirements": {
                "legacy_dungeons": 0,
                "bosses": 0,
                "mini_dungeons": 1,
                "major_bosses": 1,
            },
        }
    )
    dag, log = generate_dag(config, pool, seed=42, boss_candidates=[boss])
    all_fallbacks = [fb for le in log.layer_events for fb in le.fallbacks]
    assert log.summary is not None
    assert log.summary.fallback_count == len(all_fallbacks)


def test_fallback_count_matches_summary():
    """Summary fallback_count matches actual fallback entries across all layers."""
    pool, boss_candidates = _make_small_pool()
    config = Config.from_dict(
        {
            "structure": {
                "min_layers": 4,
                "max_layers": 5,
                "max_branches": 1,
                "split_probability": 0.0,
                "merge_probability": 0.0,
                "crosslinks": False,
                "final_boss_candidates": {"boss_zone": 1},
            },
            "requirements": {
                "legacy_dungeons": 0,
                "bosses": 0,
                "mini_dungeons": 2,
                "major_bosses": 0,
            },
        }
    )
    dag, log = generate_dag(config, pool, seed=42, boss_candidates=boss_candidates)
    all_fallbacks = [fb for le in log.layer_events for fb in le.fallbacks]
    assert log.summary is not None
    assert log.summary.fallback_count == len(all_fallbacks)
    # Verify fallback_summary matches too
    expected_summary = [
        (le.layer, fb.preferred_type) for le in log.layer_events for fb in le.fallbacks
    ]
    assert log.summary.fallback_summary == expected_summary
