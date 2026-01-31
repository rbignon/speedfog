"""Integration tests for full DAG generation pipeline."""

import json
from pathlib import Path

import pytest

from speedfog_core import (
    BudgetConfig,
    Config,
    RequirementsConfig,
    StructureConfig,
    ValidationResult,
    export_json,
    export_spoiler_log,
    generate_with_retry,
    load_clusters,
    validate_dag,
)


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
        seed=42,
        budget=BudgetConfig(total_weight=60, tolerance=30),  # Wide tolerance
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

        dag, seed = generate_with_retry(config, real_clusters, max_attempts=50)
        assert seed == 42

        result = validate_dag(dag, config)
        assert result.is_valid, f"Validation failed: {result.errors}"

        json_path = tmp_path / "graph.json"
        export_json(dag, json_path)
        assert json_path.exists()
        with open(json_path) as f:
            data = json.load(f)
        assert data["seed"] == 42

        spoiler_path = tmp_path / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path)
        assert spoiler_path.exists()

        # Verify spoiler log contains expected sections
        spoiler_content = spoiler_path.read_text()
        assert "SPEEDFOG SPOILER" in spoiler_content
        assert f"seed: {seed}" in spoiler_content
        assert "PATH SUMMARY" in spoiler_content
        # ASCII graph should have box-drawing characters
        assert "│" in spoiler_content

    def test_auto_reroll_finds_valid_seed(self, real_clusters, relaxed_config):
        """seed=0 finds a working seed automatically."""
        config = relaxed_config
        config.seed = 0  # Auto-reroll mode
        dag, seed = generate_with_retry(config, real_clusters, max_attempts=100)
        assert seed != 0
        result = validate_dag(dag, config)
        assert result.is_valid

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
        dag1, _ = generate_with_retry(config1, real_clusters)
        dag2, _ = generate_with_retry(config2, real_clusters)
        nodes1 = {n.cluster.id for n in dag1.nodes.values()}
        nodes2 = {n.cluster.id for n in dag2.nodes.values()}
        assert nodes1 != nodes2

    def test_same_seed_produces_identical_dag(self, real_clusters, relaxed_config):
        """Same seed produces identical DAG (determinism)."""
        config = relaxed_config
        config.seed = 12345
        dag1, seed1 = generate_with_retry(config, real_clusters)
        dag2, seed2 = generate_with_retry(config, real_clusters)

        assert seed1 == seed2 == 12345
        assert dag1.seed == dag2.seed

        # Same nodes
        assert set(dag1.nodes.keys()) == set(dag2.nodes.keys())

        # Same clusters in each node
        for node_id in dag1.nodes:
            assert dag1.nodes[node_id].cluster.id == dag2.nodes[node_id].cluster.id

        # Same edges
        edges1 = {(e.source_id, e.target_id) for e in dag1.edges}
        edges2 = {(e.source_id, e.target_id) for e in dag2.edges}
        assert edges1 == edges2

    def test_exported_json_structure(self, real_clusters, relaxed_config, tmp_path):
        """Verify exported JSON has correct structure for C# writer."""
        config = relaxed_config
        dag, _ = generate_with_retry(config, real_clusters)

        json_path = tmp_path / "graph.json"
        export_json(dag, json_path)

        with open(json_path) as f:
            data = json.load(f)

        # Top-level keys
        assert "seed" in data
        assert "total_layers" in data
        assert "total_nodes" in data
        assert "total_zones" in data
        assert "total_paths" in data
        assert "path_weights" in data
        assert "nodes" in data
        assert "edges" in data
        assert "start_id" in data
        assert "end_id" in data

        # Nodes structure
        assert isinstance(data["nodes"], dict)
        for _node_id, node in data["nodes"].items():
            assert "cluster_id" in node
            assert "zones" in node
            assert "type" in node
            assert "weight" in node
            assert "layer" in node
            assert "tier" in node
            assert "entry_fog" in node
            assert "exit_fogs" in node

        # Edges structure
        assert isinstance(data["edges"], list)
        for edge in data["edges"]:
            assert "source" in edge
            assert "target" in edge
            assert "fog_id" in edge

    def test_validation_result_structure(self, real_clusters, relaxed_config):
        """Verify validation returns properly structured result."""
        config = relaxed_config
        dag, _ = generate_with_retry(config, real_clusters)

        result = validate_dag(dag, config)

        assert isinstance(result, ValidationResult)
        assert isinstance(result.is_valid, bool)
        assert isinstance(result.errors, list)
        assert isinstance(result.warnings, list)
