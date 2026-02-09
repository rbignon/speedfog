"""Tests for output module (JSON and spoiler log export)."""

from pathlib import Path

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.dag import Dag, DagNode
from speedfog.output import _effective_type, dag_to_dict, export_spoiler_log


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
                "c_start",
                zones=["z_start"],
                cluster_type="start",
                weight=5,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "fog_1", "zone": "z_start"},
                    {"fog_id": "fog_2", "zone": "z_start"},
                ],
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
                "c_a",
                zones=["z_a"],
                cluster_type="legacy_dungeon",
                weight=10,
                entry_fogs=[{"fog_id": "fog_1", "zone": "z_a"}],
                exit_fogs=[{"fog_id": "fog_3", "zone": "z_a"}],
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
                "c_b",
                zones=["z_b1", "z_b2"],
                cluster_type="mini_dungeon",
                weight=15,
                entry_fogs=[{"fog_id": "fog_2", "zone": "z_b1"}],
                exit_fogs=[{"fog_id": "fog_4", "zone": "z_b1"}],
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
                "c_end",
                zones=["z_end"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[
                    {"fog_id": "fog_3", "zone": "z_end"},
                    {"fog_id": "fog_4", "zone": "z_end"},
                ],
                exit_fogs=[],
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
# _effective_type tests
# =============================================================================


class TestEffectiveType:
    """Tests for _effective_type helper."""

    def test_end_node_major_boss_becomes_final_boss(self):
        """End node with major_boss cluster type returns 'final_boss'."""
        dag = make_test_dag()
        # Override end node's cluster type to major_boss
        dag.nodes["end"].cluster.type = "major_boss"

        assert _effective_type(dag.nodes["end"], dag) == "final_boss"

    def test_end_node_already_final_boss(self):
        """End node with final_boss cluster type still returns 'final_boss'."""
        dag = make_test_dag()
        assert dag.nodes["end"].cluster.type == "final_boss"

        assert _effective_type(dag.nodes["end"], dag) == "final_boss"

    def test_non_end_node_keeps_original_type(self):
        """Non-end nodes keep their original cluster type."""
        dag = make_test_dag()

        assert _effective_type(dag.nodes["start"], dag) == "start"
        assert _effective_type(dag.nodes["a"], dag) == "legacy_dungeon"
        assert _effective_type(dag.nodes["b"], dag) == "mini_dungeon"

    def test_non_end_major_boss_stays_major_boss(self):
        """A major_boss node that is NOT the end node keeps 'major_boss'."""
        dag = make_test_dag()
        dag.nodes["a"].cluster.type = "major_boss"

        assert _effective_type(dag.nodes["a"], dag) == "major_boss"


# =============================================================================
# dag_to_dict effective type tests
# =============================================================================


class TestDagToDictEffectiveType:
    """Tests for final_boss type override in dag_to_dict."""

    def test_major_boss_end_node_typed_final_boss_in_json(self):
        """dag_to_dict outputs 'final_boss' for end node even if cluster is major_boss."""
        dag = make_test_dag()
        dag.nodes["end"].cluster.type = "major_boss"

        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )

        result = dag_to_dict(dag, clusters)

        assert result["nodes"]["c_end"]["type"] == "final_boss"
        # ClusterData was NOT mutated
        assert dag.nodes["end"].cluster.type == "major_boss"
        # Other nodes unchanged
        assert result["nodes"]["c_start"]["type"] == "start"
        assert result["nodes"]["c_a"]["type"] == "legacy_dungeon"


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

    def test_major_boss_end_shows_final_boss(self, tmp_path: Path):
        """Spoiler log shows [final_boss] for end node even if cluster is major_boss."""
        dag = make_test_dag()
        dag.nodes["end"].cluster.type = "major_boss"
        output_file = tmp_path / "spoiler.txt"

        export_spoiler_log(dag, output_file)

        content = output_file.read_text(encoding="utf-8")
        assert "[final_boss]" in content
        assert "[major_boss]" not in content


# =============================================================================
# Event map / v4 format tests
# =============================================================================


def _make_result() -> dict:
    """Helper: call dag_to_dict with the standard diamond test DAG."""
    dag = make_test_dag()
    clusters = ClusterPool(
        clusters=[node.cluster for node in dag.nodes.values()],
        zone_maps={},
        zone_names={},
    )
    return dag_to_dict(dag, clusters)


class TestEventMap:
    """Tests for v4 event_map, finish_event, and flag_id fields."""

    def test_version_is_4(self):
        """Version string is '4.0'."""
        result = _make_result()
        assert result["version"] == "4.0"

    def test_event_map_keys_are_string_flag_ids(self):
        """event_map keys are stringified integers."""
        result = _make_result()
        for key in result["event_map"]:
            assert isinstance(key, str)
            int(key)  # should not raise

    def test_event_map_values_are_node_ids(self):
        """event_map values match cluster IDs from the nodes dict."""
        result = _make_result()
        node_ids = set(result["nodes"].keys())
        for cluster_id in result["event_map"].values():
            assert cluster_id in node_ids

    def test_event_map_excludes_start_node(self):
        """Start node does not appear in event_map."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)

        start_cluster_id = dag.nodes[dag.start_id].cluster.id
        assert start_cluster_id not in result["event_map"].values()

    def test_finish_event_is_separate_from_zone_flags(self):
        """finish_event is a distinct flag not used for zone tracking."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)

        # finish_event must NOT appear in event_map (it's a separate flag)
        assert str(result["finish_event"]) not in result["event_map"]

        # It should be a valid flag ID in the expected range
        assert isinstance(result["finish_event"], int)
        assert result["finish_event"] >= 1040292800

    def test_finish_event_follows_zone_flags(self):
        """finish_event is allocated after all zone tracking flags."""
        result = _make_result()
        zone_flags = {int(k) for k in result["event_map"]}
        assert result["finish_event"] > max(zone_flags)

    def test_connections_have_flag_id(self):
        """Each connection has a flag_id field."""
        result = _make_result()
        for conn in result["connections"]:
            assert "flag_id" in conn
            assert isinstance(conn["flag_id"], int)
            assert conn["flag_id"] >= 9000000

    def test_merge_node_connections_share_flag_id(self):
        """Two connections to the same node get the same flag_id.

        In the diamond DAG, edges a→end and b→end should share the end node's flag_id.
        """
        result = _make_result()
        # Find connections going to the end node's zone (z_end)
        end_connections = [
            c for c in result["connections"] if c["entrance_area"] == "z_end"
        ]
        assert len(end_connections) == 2
        assert end_connections[0]["flag_id"] == end_connections[1]["flag_id"]
