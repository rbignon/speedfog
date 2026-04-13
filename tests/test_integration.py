"""Integration tests for full DAG generation pipeline."""

import json
from pathlib import Path

import pytest

from speedfog import (
    BudgetConfig,
    Config,
    RequirementsConfig,
    StructureConfig,
    ValidationResult,
    export_spoiler_log,
    generate_dag,
    generate_with_retry,
    load_clusters,
)
from speedfog.output import export_json


@pytest.fixture
def real_clusters_and_bosses():
    """Load clusters.json, snapshot boss candidates, then filter.

    Returns (clusters, boss_candidates) where boss_candidates is captured
    BEFORE filter_passant_incompatible() removes dead-end arenas.
    """
    clusters_path = Path(__file__).parent.parent / "data" / "clusters.json"
    if not clusters_path.exists():
        pytest.skip("clusters.json not found")
    clusters = load_clusters(clusters_path)
    clusters.merge_roundtable_into_start()
    # Snapshot before filtering (dead-end bosses are valid final boss targets)
    boss_candidates = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )
    clusters.filter_passant_incompatible()
    return clusters, boss_candidates


@pytest.fixture
def real_clusters(real_clusters_and_bosses):
    """Load the actual clusters.json with standard preprocessing."""
    return real_clusters_and_bosses[0]


@pytest.fixture
def real_boss_candidates(real_clusters_and_bosses):
    """Boss candidates snapshotted before passant filtering."""
    return real_clusters_and_bosses[1]


@pytest.fixture
def relaxed_config():
    """Create a config with relaxed requirements for testing.

    Uses minimal requirements to ensure generation succeeds with most seeds.
    """
    return Config(
        seed=3,
        budget=BudgetConfig(tolerance=30),  # Wide tolerance for spread
        requirements=RequirementsConfig(
            legacy_dungeons=0,
            bosses=0,
            mini_dungeons=0,
        ),
        structure=StructureConfig(
            max_parallel_paths=2,
            min_layers=3,
            max_layers=5,
        ),
    )


class TestFullPipeline:
    """End-to-end tests for the generation pipeline."""

    def test_generate_validate_export(
        self, real_clusters, real_boss_candidates, relaxed_config, tmp_path
    ):
        """Full pipeline: generate -> validate -> export."""
        config = relaxed_config

        result = generate_with_retry(
            config, real_clusters, max_attempts=50, boss_candidates=real_boss_candidates
        )
        assert result.seed == 3
        assert (
            result.validation.is_valid
        ), f"Validation failed: {result.validation.errors}"

        dag = result.dag
        json_path = tmp_path / "graph.json"
        export_json(dag, real_clusters, json_path)
        assert json_path.exists()
        with open(json_path) as f:
            data = json.load(f)
        assert data["seed"] == 3

        spoiler_path = tmp_path / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path)
        assert spoiler_path.exists()

        # Verify spoiler log contains expected sections
        spoiler_content = spoiler_path.read_text()
        assert "SPEEDFOG SPOILER" in spoiler_content
        assert f"seed: {result.seed}" in spoiler_content
        assert "NODE DETAILS" in spoiler_content
        # ASCII graph should have box-drawing characters
        assert "│" in spoiler_content

    def test_auto_reroll_finds_valid_seed(
        self, real_clusters, real_boss_candidates, relaxed_config
    ):
        """seed=0 finds a working seed automatically."""
        config = relaxed_config
        config.seed = 0  # Auto-reroll mode
        result = generate_with_retry(
            config,
            real_clusters,
            max_attempts=100,
            boss_candidates=real_boss_candidates,
        )
        assert result.seed != 0
        assert result.validation.is_valid

    def test_multiple_seeds_produce_different_dags(
        self, real_clusters, real_boss_candidates, relaxed_config
    ):
        """Different seeds produce different DAGs."""
        config1 = relaxed_config
        config1.seed = 3
        config2 = Config(
            seed=5,
            budget=relaxed_config.budget,
            requirements=relaxed_config.requirements,
            structure=relaxed_config.structure,
        )
        result1 = generate_with_retry(
            config1, real_clusters, boss_candidates=real_boss_candidates
        )
        result2 = generate_with_retry(
            config2, real_clusters, boss_candidates=real_boss_candidates
        )
        nodes1 = {n.cluster.id for n in result1.dag.nodes.values()}
        nodes2 = {n.cluster.id for n in result2.dag.nodes.values()}
        assert nodes1 != nodes2

    def test_same_seed_produces_identical_dag(
        self, real_clusters, real_boss_candidates, relaxed_config
    ):
        """Same seed produces identical DAG (determinism)."""
        config = relaxed_config
        config.seed = 12346
        result1 = generate_with_retry(
            config, real_clusters, boss_candidates=real_boss_candidates
        )
        result2 = generate_with_retry(
            config, real_clusters, boss_candidates=real_boss_candidates
        )

        assert result1.seed == result2.seed == 12346
        assert result1.dag.seed == result2.dag.seed

        # Same nodes
        assert set(result1.dag.nodes.keys()) == set(result2.dag.nodes.keys())

        # Same clusters in each node
        for node_id in result1.dag.nodes:
            assert (
                result1.dag.nodes[node_id].cluster.id
                == result2.dag.nodes[node_id].cluster.id
            )

        # Same edges
        edges1 = {(e.source_id, e.target_id) for e in result1.dag.edges}
        edges2 = {(e.source_id, e.target_id) for e in result2.dag.edges}
        assert edges1 == edges2

    def test_exported_json_structure(
        self, real_clusters, real_boss_candidates, relaxed_config, tmp_path
    ):
        """Verify exported JSON v4 has correct structure."""
        config = relaxed_config
        result = generate_with_retry(
            config, real_clusters, boss_candidates=real_boss_candidates
        )
        dag = result.dag

        json_path = tmp_path / "graph.json"
        export_json(dag, real_clusters, json_path)

        with open(json_path) as f:
            data = json.load(f)

        # Top-level keys (v4 format)
        assert data["version"] == "4.2"
        assert "seed" in data
        assert "options" in data
        assert "connections" in data
        assert "area_tiers" in data
        assert "nodes" in data
        assert "edges" in data
        assert "event_map" in data
        assert "final_node_flag" in data
        assert "finish_event" in data

        # Options structure
        assert isinstance(data["options"], dict)

        # Nodes structure
        assert isinstance(data["nodes"], dict)
        assert len(data["nodes"]) > 0
        for cluster_id, node_data in data["nodes"].items():
            assert isinstance(cluster_id, str)
            assert "type" in node_data
            assert "display_name" in node_data
            assert "zones" in node_data
            assert "layer" in node_data
            assert "tier" in node_data
            assert "weight" in node_data
            assert isinstance(node_data["zones"], list)
            assert isinstance(node_data["layer"], int)
            assert isinstance(node_data["tier"], int)
            assert isinstance(node_data["weight"], int)

        # Edges structure
        assert isinstance(data["edges"], list)
        assert len(data["edges"]) > 0
        for edge in data["edges"]:
            assert "from" in edge
            assert "to" in edge
            assert edge["from"] in data["nodes"]
            assert edge["to"] in data["nodes"]

        # Connections structure (v4: includes flag_id)
        assert isinstance(data["connections"], list)
        for conn in data["connections"]:
            assert "exit_area" in conn
            assert "exit_gate" in conn
            assert "entrance_area" in conn
            assert "entrance_gate" in conn
            assert "flag_id" in conn
            assert isinstance(conn["flag_id"], int)
            assert conn["flag_id"] >= 1050292000

        # Area tiers structure
        assert isinstance(data["area_tiers"], dict)
        for zone, tier in data["area_tiers"].items():
            assert isinstance(zone, str)
            assert isinstance(tier, int)

        # Event map structure (v4)
        assert isinstance(data["event_map"], dict)
        assert len(data["event_map"]) > 0
        for flag_str, cluster_id in data["event_map"].items():
            assert isinstance(flag_str, str)
            int(flag_str)  # should be a stringified int
            assert cluster_id in data["nodes"]

        # Final node flag (zone-tracking flag for the end node)
        assert isinstance(data["final_node_flag"], int)
        assert data["final_node_flag"] >= 1050292000
        assert str(data["final_node_flag"]) in data["event_map"]

        # Finish event (separate flag for boss death, not in event_map)
        assert isinstance(data["finish_event"], int)
        assert data["finish_event"] >= 1050292000
        assert str(data["finish_event"]) not in data["event_map"]
        assert data["final_node_flag"] != data["finish_event"]

    def test_validation_result_structure(
        self, real_clusters, real_boss_candidates, relaxed_config
    ):
        """Verify validation returns properly structured result."""
        config = relaxed_config
        gen_result = generate_with_retry(
            config, real_clusters, boss_candidates=real_boss_candidates
        )

        validation = gen_result.validation

        assert isinstance(validation, ValidationResult)
        assert isinstance(validation.is_valid, bool)
        assert isinstance(validation.errors, list)
        assert isinstance(validation.warnings, list)


def test_generation_log_with_real_clusters(
    real_clusters, real_boss_candidates, tmp_path
):
    """Full generation produces a valid log with all sections."""
    from speedfog.generation_log import export_generation_log

    config = Config.from_dict(
        {
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
        }
    )
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
    export_generation_log(log, log_path, dag=dag)
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


def test_boss_rush_integration(real_clusters, real_boss_candidates):
    """End-to-end: a boss-rush config produces a DAG whose intermediate
    nodes are all boss_arena or major_boss, and whose final node is
    major_boss."""
    import warnings

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        config = Config.from_dict(
            {
                "run": {"seed": 0},
                "requirements": {
                    "allowed_types": ["boss_arena", "major_boss"],
                    "legacy_dungeons": 0,
                    "bosses": 4,
                    "mini_dungeons": 0,
                    "major_bosses": 1,
                },
                "structure": {
                    "min_layers": 4,
                    "max_layers": 8,
                    "max_parallel_paths": 2,
                },
            }
        )

    result = generate_with_retry(
        config,
        real_clusters,
        max_attempts=200,
        boss_candidates=real_boss_candidates,
    )
    assert result.validation.is_valid, f"Validation failed: {result.validation.errors}"

    dag = result.dag
    intermediate_types = {
        node.cluster.type
        for node_id, node in dag.nodes.items()
        if node_id not in (dag.start_id, dag.end_id)
    }
    assert intermediate_types <= {
        "boss_arena",
        "major_boss",
    }, f"Forbidden types in intermediate nodes: {intermediate_types}"

    end_node = dag.nodes[dag.end_id]
    assert end_node.cluster.type in ("major_boss", "final_boss")


def test_dungeon_crawl_integration(real_clusters, real_boss_candidates):
    """allowed_types = [mini_dungeon, boss_arena, major_boss] excludes legacy
    dungeons from all intermediate nodes."""
    import warnings

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        config = Config.from_dict(
            {
                "run": {"seed": 0},
                "requirements": {
                    "allowed_types": ["mini_dungeon", "boss_arena", "major_boss"],
                    "legacy_dungeons": 0,
                    "bosses": 2,
                    "mini_dungeons": 3,
                    "major_bosses": 1,
                },
                "structure": {
                    "min_layers": 5,
                    "max_layers": 9,
                    "max_parallel_paths": 2,
                },
            }
        )

    result = generate_with_retry(
        config,
        real_clusters,
        max_attempts=200,
        boss_candidates=real_boss_candidates,
    )
    assert result.validation.is_valid, f"Validation failed: {result.validation.errors}"

    dag = result.dag
    intermediate_types = {
        node.cluster.type
        for node_id, node in dag.nodes.items()
        if node_id not in (dag.start_id, dag.end_id)
    }
    assert "legacy_dungeon" not in intermediate_types


def test_legacy_marathon_integration(real_clusters, real_boss_candidates):
    """allowed_types = [legacy_dungeon] produces a DAG with only legacy
    dungeons in intermediate nodes (final boss still major_boss)."""
    import warnings

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        config = Config.from_dict(
            {
                "run": {"seed": 0},
                "requirements": {
                    "allowed_types": ["legacy_dungeon"],
                    "legacy_dungeons": 2,
                    "bosses": 0,
                    "mini_dungeons": 0,
                    "major_bosses": 0,
                },
                "structure": {
                    "min_layers": 3,
                    "max_layers": 5,
                    "max_parallel_paths": 2,
                },
            }
        )

    result = generate_with_retry(
        config,
        real_clusters,
        max_attempts=200,
        boss_candidates=real_boss_candidates,
    )
    assert result.validation.is_valid, f"Validation failed: {result.validation.errors}"

    dag = result.dag
    intermediate_types = {
        node.cluster.type
        for node_id, node in dag.nodes.items()
        if node_id not in (dag.start_id, dag.end_id)
    }
    assert intermediate_types <= {"legacy_dungeon"}

    end_node = dag.nodes[dag.end_id]
    assert end_node.cluster.type in ("major_boss", "final_boss")
