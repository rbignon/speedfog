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
    generate_with_retry,
    load_clusters,
)
from speedfog.output import export_json


@pytest.fixture
def real_clusters():
    """Load the actual clusters.json."""
    clusters_path = Path(__file__).parent.parent / "data" / "clusters.json"
    if not clusters_path.exists():
        pytest.skip("clusters.json not found")
    return load_clusters(clusters_path)


@pytest.fixture
def relaxed_config():
    """Create a config with relaxed requirements for testing.

    Uses minimal requirements to ensure generation succeeds with most seeds.
    """
    return Config(
        seed=1,
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

    def test_generate_validate_export(self, real_clusters, relaxed_config, tmp_path):
        """Full pipeline: generate -> validate -> export."""
        config = relaxed_config

        result = generate_with_retry(config, real_clusters, max_attempts=50)
        assert result.seed == 1
        assert (
            result.validation.is_valid
        ), f"Validation failed: {result.validation.errors}"

        dag = result.dag
        json_path = tmp_path / "graph.json"
        export_json(dag, real_clusters, json_path)
        assert json_path.exists()
        with open(json_path) as f:
            data = json.load(f)
        assert data["seed"] == 1

        spoiler_path = tmp_path / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path)
        assert spoiler_path.exists()

        # Verify spoiler log contains expected sections
        spoiler_content = spoiler_path.read_text()
        assert "SPEEDFOG SPOILER" in spoiler_content
        assert f"seed: {result.seed}" in spoiler_content
        assert "PATH SUMMARY" in spoiler_content
        # ASCII graph should have box-drawing characters
        assert "│" in spoiler_content

    def test_auto_reroll_finds_valid_seed(self, real_clusters, relaxed_config):
        """seed=0 finds a working seed automatically."""
        config = relaxed_config
        config.seed = 0  # Auto-reroll mode
        result = generate_with_retry(config, real_clusters, max_attempts=100)
        assert result.seed != 0
        assert result.validation.is_valid

    def test_multiple_seeds_produce_different_dags(self, real_clusters, relaxed_config):
        """Different seeds produce different DAGs."""
        config1 = relaxed_config
        config1.seed = 1
        config2 = Config(
            seed=2,
            budget=relaxed_config.budget,
            requirements=relaxed_config.requirements,
            structure=relaxed_config.structure,
        )
        result1 = generate_with_retry(config1, real_clusters)
        result2 = generate_with_retry(config2, real_clusters)
        nodes1 = {n.cluster.id for n in result1.dag.nodes.values()}
        nodes2 = {n.cluster.id for n in result2.dag.nodes.values()}
        assert nodes1 != nodes2

    def test_same_seed_produces_identical_dag(self, real_clusters, relaxed_config):
        """Same seed produces identical DAG (determinism)."""
        config = relaxed_config
        config.seed = 12345
        result1 = generate_with_retry(config, real_clusters)
        result2 = generate_with_retry(config, real_clusters)

        assert result1.seed == result2.seed == 12345
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

    def test_exported_json_structure(self, real_clusters, relaxed_config, tmp_path):
        """Verify exported JSON v4 has correct structure."""
        config = relaxed_config
        result = generate_with_retry(config, real_clusters)
        dag = result.dag

        json_path = tmp_path / "graph.json"
        export_json(dag, real_clusters, json_path)

        with open(json_path) as f:
            data = json.load(f)

        # Top-level keys (v4 format)
        assert data["version"] == "4.0"
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
            assert conn["flag_id"] >= 1040292800

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
        assert data["final_node_flag"] >= 1040292800
        assert str(data["final_node_flag"]) in data["event_map"]

        # Finish event (separate flag for boss death, not in event_map)
        assert isinstance(data["finish_event"], int)
        assert data["finish_event"] >= 1040292800
        assert str(data["finish_event"]) not in data["event_map"]
        assert data["final_node_flag"] != data["finish_event"]

    def test_validation_result_structure(self, real_clusters, relaxed_config):
        """Verify validation returns properly structured result."""
        config = relaxed_config
        gen_result = generate_with_retry(config, real_clusters)

        validation = gen_result.validation

        assert isinstance(validation, ValidationResult)
        assert isinstance(validation.is_valid, bool)
        assert isinstance(validation.errors, list)
        assert isinstance(validation.warnings, list)
