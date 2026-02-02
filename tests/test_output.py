"""Tests for output module (JSON and spoiler log export)."""

import json
from pathlib import Path

from speedfog.clusters import ClusterData
from speedfog.dag import Dag, DagNode
from speedfog.output import dag_to_dict, export_json, export_spoiler_log


def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | None = None,
    exit_fogs: list[dict] | None = None,
) -> ClusterData:
    """Helper to create test ClusterData objects."""
    return ClusterData(
        id=cluster_id,
        zones=zones or [f"{cluster_id}_zone"],
        type=cluster_type,
        weight=weight,
        entry_fogs=entry_fogs
        or [{"fog_id": f"{cluster_id}_entry", "zone": cluster_id}],
        exit_fogs=exit_fogs or [{"fog_id": f"{cluster_id}_exit", "zone": cluster_id}],
    )


def make_test_dag() -> Dag:
    """Create a simple test DAG for tests.

    Structure:
          start (layer 0, tier 1, weight 5)
         /     \\
        a       b   (layer 1, tier 5, weights 10, 15)
         \\     /
           end     (layer 2, tier 10, weight 5)

    Paths:
    - start -> a -> end (weight 20)
    - start -> b -> end (weight 25)
    """
    dag = Dag(seed=42)

    # Add start node
    dag.add_node(
        DagNode(
            id="start",
            cluster=make_cluster(
                "c_start", zones=["z_start"], cluster_type="start", weight=5
            ),
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=["fog_1", "fog_2"],
        )
    )

    # Add branch a
    dag.add_node(
        DagNode(
            id="a",
            cluster=make_cluster(
                "c_a", zones=["z_a"], cluster_type="legacy_dungeon", weight=10
            ),
            layer=1,
            tier=5,
            entry_fogs=["fog_1"],
            exit_fogs=["fog_3"],
        )
    )

    # Add branch b
    dag.add_node(
        DagNode(
            id="b",
            cluster=make_cluster(
                "c_b", zones=["z_b1", "z_b2"], cluster_type="mini_dungeon", weight=15
            ),
            layer=1,
            tier=5,
            entry_fogs=["fog_2"],
            exit_fogs=["fog_4"],
        )
    )

    # Add end node (merge node with 2 incoming edges)
    dag.add_node(
        DagNode(
            id="end",
            cluster=make_cluster(
                "c_end", zones=["z_end"], cluster_type="final_boss", weight=5
            ),
            layer=2,
            tier=10,
            entry_fogs=["fog_3", "fog_4"],  # Both branches merge here
            exit_fogs=[],
        )
    )

    # Add edges
    dag.add_edge("start", "a", "fog_1", "fog_1")
    dag.add_edge("start", "b", "fog_2", "fog_2")
    dag.add_edge("a", "end", "fog_3", "fog_3")
    dag.add_edge("b", "end", "fog_4", "fog_4")

    dag.start_id = "start"
    dag.end_id = "end"

    return dag


# =============================================================================
# dag_to_dict tests
# =============================================================================


class TestDagToDict:
    """Tests for dag_to_dict function."""

    def test_contains_seed(self):
        """dag_to_dict result contains the seed."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        assert "seed" in result
        assert result["seed"] == 42

    def test_contains_nodes(self):
        """dag_to_dict result contains nodes dict."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        assert "nodes" in result
        assert isinstance(result["nodes"], dict)
        assert len(result["nodes"]) == 4
        assert "start" in result["nodes"]
        assert "a" in result["nodes"]
        assert "b" in result["nodes"]
        assert "end" in result["nodes"]

    def test_node_has_cluster_info(self):
        """Each node contains cluster information."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        node_a = result["nodes"]["a"]
        assert "cluster_id" in node_a
        assert node_a["cluster_id"] == "c_a"
        assert "zones" in node_a
        assert node_a["zones"] == ["z_a"]
        assert "type" in node_a
        assert node_a["type"] == "legacy_dungeon"
        assert "weight" in node_a
        assert node_a["weight"] == 10
        assert "layer" in node_a
        assert node_a["layer"] == 1
        assert "tier" in node_a
        assert node_a["tier"] == 5
        assert "entry_fogs" in node_a
        assert node_a["entry_fogs"] == ["fog_1"]
        assert "exit_fogs" in node_a
        assert node_a["exit_fogs"] == ["fog_3"]

    def test_contains_edges(self):
        """dag_to_dict result contains edges list."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        assert "edges" in result
        assert isinstance(result["edges"], list)
        assert len(result["edges"]) == 4

    def test_edge_structure(self):
        """Each edge has source, target, and fog_id."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        edges = result["edges"]
        # Find the edge from start to a
        edge_start_a = next(
            (e for e in edges if e["source"] == "start" and e["target"] == "a"), None
        )
        assert edge_start_a is not None
        assert edge_start_a["source"] == "start"
        assert edge_start_a["target"] == "a"
        assert edge_start_a["fog_id"] == "fog_1"

    def test_contains_path_stats(self):
        """dag_to_dict result contains path statistics."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        assert "total_paths" in result
        assert result["total_paths"] == 2
        assert "path_weights" in result
        assert isinstance(result["path_weights"], list)
        assert len(result["path_weights"]) == 2
        # Weights: start->a->end = 5+10+5 = 20, start->b->end = 5+15+5 = 25
        assert sorted(result["path_weights"]) == [20, 25]

    def test_contains_totals(self):
        """dag_to_dict result contains totals."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        assert "total_nodes" in result
        assert result["total_nodes"] == 4
        assert "total_zones" in result
        # Zones: z_start, z_a, z_b1, z_b2, z_end = 5
        assert result["total_zones"] == 5
        assert "total_layers" in result
        # Layers: 0, 1, 2 = 3 layers
        assert result["total_layers"] == 3

    def test_contains_start_end_ids(self):
        """dag_to_dict result contains start_id and end_id."""
        dag = make_test_dag()
        result = dag_to_dict(dag)
        assert "start_id" in result
        assert result["start_id"] == "start"
        assert "end_id" in result
        assert result["end_id"] == "end"


# =============================================================================
# export_json tests
# =============================================================================


class TestExportJson:
    """Tests for export_json function."""

    def test_creates_valid_json_file(self, tmp_path: Path):
        """export_json creates a valid JSON file."""
        dag = make_test_dag()
        output_file = tmp_path / "graph.json"

        export_json(dag, output_file)

        assert output_file.exists()
        # Verify it's valid JSON
        with open(output_file, encoding="utf-8") as f:
            data = json.load(f)
        assert "seed" in data
        assert data["seed"] == 42

    def test_json_is_formatted(self, tmp_path: Path):
        """export_json creates formatted (indented) JSON."""
        dag = make_test_dag()
        output_file = tmp_path / "graph.json"

        export_json(dag, output_file)

        content = output_file.read_text(encoding="utf-8")
        # Check that it's indented (contains newlines and spaces)
        assert "\n" in content
        # Check for indentation (at least 2 spaces at start of some line)
        lines = content.split("\n")
        indented_lines = [line for line in lines if line.startswith("  ")]
        assert len(indented_lines) > 0


# =============================================================================
# export_spoiler_log tests
# =============================================================================


class TestExportSpoilerLog:
    """Tests for export_spoiler_log function."""

    def test_creates_file(self, tmp_path: Path):
        """export_spoiler_log creates a file."""
        dag = make_test_dag()
        output_file = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_file)

        assert output_file.exists()

    def test_contains_seed(self, tmp_path: Path):
        """export_spoiler_log output contains the seed."""
        dag = make_test_dag()
        output_file = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_file)

        content = output_file.read_text(encoding="utf-8")
        assert "42" in content
        assert "seed" in content.lower()

    def test_contains_ascii_graph(self, tmp_path: Path):
        """export_spoiler_log output contains ASCII graph visualization."""
        dag = make_test_dag()
        output_file = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_file)

        content = output_file.read_text(encoding="utf-8")
        # Should have ASCII graph elements (box-drawing characters)
        assert "│" in content  # Vertical lines for connections
        # Should mention specific clusters
        assert "c_start" in content
        assert "c_a" in content or "c_b" in content
        # Should have weight annotations
        assert "(w:" in content

    def test_contains_paths_section(self, tmp_path: Path):
        """export_spoiler_log output contains paths section."""
        dag = make_test_dag()
        output_file = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_file)

        content = output_file.read_text(encoding="utf-8")
        # Should have paths information
        assert "path" in content.lower()
