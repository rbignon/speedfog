"""Tests for output module (JSON and spoiler log export)."""

from pathlib import Path

from speedfog.clusters import ClusterData
from speedfog.dag import Dag, DagNode
from speedfog.output import export_spoiler_log


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
