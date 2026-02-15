"""Tests for output module (JSON and spoiler log export)."""

from pathlib import Path

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.dag import Dag, DagNode
from speedfog.output import (
    _effective_type,
    _make_fullname,
    dag_to_dict,
    export_spoiler_log,
)


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
        or [
            {
                "fog_id": f"{cluster_id}_entry",
                "zone": cluster_id,
                "text": f"{cluster_id} entry",
            }
        ],
        exit_fogs=exit_fogs
        or [
            {
                "fog_id": f"{cluster_id}_exit",
                "zone": cluster_id,
                "text": f"{cluster_id} exit",
            }
        ],
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
                    {"fog_id": "fog_1", "zone": "z_start", "text": "Gate to A"},
                    {"fog_id": "fog_2", "zone": "z_start", "text": "Gate to B"},
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
                entry_fogs=[{"fog_id": "fog_1", "zone": "z_a", "text": "Gate to A"}],
                exit_fogs=[{"fog_id": "fog_3", "zone": "z_a", "text": "Gate to end"}],
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
                entry_fogs=[{"fog_id": "fog_2", "zone": "z_b1", "text": "Gate to B"}],
                exit_fogs=[
                    {"fog_id": "fog_4", "zone": "z_b1", "text": "Gate to end B"}
                ],
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
                    {"fog_id": "fog_3", "zone": "z_end", "text": "Gate to end"},
                    {"fog_id": "fog_4", "zone": "z_end", "text": "Gate to end B"},
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

    def test_final_node_flag_is_end_node_zone_flag(self):
        """final_node_flag matches the zone-tracking flag for the end node."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)

        end_cluster_id = dag.nodes[dag.end_id].cluster.id
        # Find the flag_id that maps to the end node in event_map
        end_flag = None
        for flag_str, cluster_id in result["event_map"].items():
            if cluster_id == end_cluster_id:
                end_flag = int(flag_str)
                break

        assert end_flag is not None
        assert result["final_node_flag"] == end_flag

    def test_final_node_flag_differs_from_finish_event(self):
        """final_node_flag (zone entry) and finish_event (boss death) are different."""
        result = _make_result()
        assert result["final_node_flag"] != result["finish_event"]

    def test_connections_have_flag_id(self):
        """Each connection has a flag_id field."""
        result = _make_result()
        for conn in result["connections"]:
            assert "flag_id" in conn
            assert isinstance(conn["flag_id"], int)
            assert conn["flag_id"] >= 1040292800

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

    def test_run_complete_message_default(self):
        """Default run_complete_message is 'RUN COMPLETE'."""
        result = _make_result()
        assert result["run_complete_message"] == "RUN COMPLETE"

    def test_run_complete_message_custom(self):
        """Custom run_complete_message is passed through."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters, run_complete_message="GG")
        assert result["run_complete_message"] == "GG"

    def test_chapel_grace_default(self):
        """Default chapel_grace is True."""
        result = _make_result()
        assert result["chapel_grace"] is True

    def test_chapel_grace_false(self):
        """chapel_grace=False is passed through."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters, chapel_grace=False)
        assert result["chapel_grace"] is False

    def test_finish_boss_defeat_flag_present(self):
        """finish_boss_defeat_flag is included in the output."""
        result = _make_result()
        assert "finish_boss_defeat_flag" in result

    def test_finish_boss_defeat_flag_from_cluster(self):
        """finish_boss_defeat_flag reflects the end node's cluster defeat_flag."""
        dag = make_test_dag()
        dag.nodes["end"].cluster.defeat_flag = 19000800
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert result["finish_boss_defeat_flag"] == 19000800

    def test_finish_boss_defeat_flag_zero_when_missing(self):
        """finish_boss_defeat_flag is 0 when cluster has no defeat_flag."""
        result = _make_result()
        assert result["finish_boss_defeat_flag"] == 0


# =============================================================================
# Node exits tests
# =============================================================================


class TestNodeExits:
    """Tests for exits field in graph.json nodes."""

    def test_exits_present_in_nodes(self):
        """Every node has an exits key."""
        result = _make_result()
        for node_id, node_data in result["nodes"].items():
            assert "exits" in node_data, f"Node {node_id} missing exits"

    def test_exits_from_dag_edges(self):
        """Exits match actual DAG edges from each node."""
        result = _make_result()
        # start has 2 exits (to c_a and c_b)
        start_exits = result["nodes"]["c_start"]["exits"]
        assert len(start_exits) == 2
        exit_targets = {e["to"] for e in start_exits}
        assert exit_targets == {"c_a", "c_b"}

    def test_exit_fields(self):
        """Each exit has fog_id, text, to, and from fields."""
        result = _make_result()
        for node_id, node_data in result["nodes"].items():
            for exit_item in node_data["exits"]:
                assert "fog_id" in exit_item, f"Exit in {node_id} missing fog_id"
                assert "text" in exit_item, f"Exit in {node_id} missing text"
                assert "to" in exit_item, f"Exit in {node_id} missing to"
                assert "from" in exit_item, f"Exit in {node_id} missing from"

    def test_end_node_has_no_exits(self):
        """Final boss node has exits: []."""
        result = _make_result()
        assert result["nodes"]["c_end"]["exits"] == []

    def test_exit_text_from_cluster(self):
        """Exit text comes from the cluster's exit_fogs text field."""
        result = _make_result()
        start_exits = result["nodes"]["c_start"]["exits"]
        # fog_1 has text "Gate to A", fog_2 has text "Gate to B"
        exit_by_fog = {e["fog_id"]: e for e in start_exits}
        assert exit_by_fog["fog_1"]["text"] == "Gate to A"
        assert exit_by_fog["fog_2"]["text"] == "Gate to B"

    def test_exit_from_zone(self):
        """Exit 'from' field contains the origin zone within the cluster."""
        result = _make_result()
        # start exits both come from z_start
        start_exits = result["nodes"]["c_start"]["exits"]
        for exit_item in start_exits:
            assert exit_item["from"] == "z_start"
        # branch b exit comes from z_b1 (not z_b2)
        b_exits = result["nodes"]["c_b"]["exits"]
        assert len(b_exits) == 1
        assert b_exits[0]["from"] == "z_b1"

    def test_exit_from_omitted_when_fog_not_in_exit_fogs(self):
        """Exit 'from' field is omitted when fog_id is not in cluster exit_fogs."""
        dag = make_test_dag()
        dag.add_edge("start", "a", "unknown_fog", "fog_1")
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        start_exits = result["nodes"]["c_start"]["exits"]
        unknown_exits = [e for e in start_exits if e["fog_id"] == "unknown_fog"]
        assert len(unknown_exits) == 1
        assert "from" not in unknown_exits[0]

    def test_exit_text_fallback_to_fog_id(self):
        """When text is missing from exit_fogs, falls back to fog_id."""
        dag = make_test_dag()
        # Remove text from start's exit_fogs
        dag.nodes["start"].cluster.exit_fogs = [
            {"fog_id": "fog_1", "zone": "z_start"},
            {"fog_id": "fog_2", "zone": "z_start"},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        start_exits = result["nodes"]["c_start"]["exits"]
        exit_by_fog = {e["fog_id"]: e for e in start_exits}
        assert exit_by_fog["fog_1"]["text"] == "fog_1"
        assert exit_by_fog["fog_2"]["text"] == "fog_2"

    def test_exit_text_prefers_side_text(self):
        """When side_text is present, it takes priority over gate-level text."""
        dag = make_test_dag()
        # Add side_text to start's exit_fogs
        dag.nodes["start"].cluster.exit_fogs = [
            {
                "fog_id": "fog_1",
                "zone": "z_start",
                "text": "Gate Name",
                "side_text": "detailed side description",
            },
            {"fog_id": "fog_2", "zone": "z_start", "text": "Gate to B"},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        start_exits = result["nodes"]["c_start"]["exits"]
        exit_by_fog = {e["fog_id"]: e for e in start_exits}
        # fog_1 should use side_text
        assert exit_by_fog["fog_1"]["text"] == "detailed side description"
        # fog_2 falls back to gate-level text
        assert exit_by_fog["fog_2"]["text"] == "Gate to B"


# =============================================================================
# Duplicate fog_id across zones tests
# =============================================================================


class TestDuplicateFogIdAcrossZones:
    """Tests for clusters with the same fog_id in exit_fogs for multiple zones.

    This reproduces the Redmane Castle scenario where AEG099_001_9001 appears
    as an exit from both caelid_redmane_boss and caelid_redmane_postboss zones
    (two sides of the same physical fog gate).
    """

    def _make_dag_with_duplicate_exit_fogs(self) -> tuple[Dag, ClusterPool]:
        """Create a DAG where a multi-zone cluster has duplicate fog_ids.

        Structure:
            start -> mid -> dest_1
                         -> dest_2

        mid has two zones (zone_a, zone_b) with fog_id "shared_fog" as an
        exit from both zones (two sides of the same gate).
        """
        dag = Dag(seed=99)

        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster(
                    "c_start",
                    zones=["z_start"],
                    cluster_type="start",
                    entry_fogs=[],
                    exit_fogs=[
                        {"fog_id": "fog_entry", "zone": "z_start", "text": "To mid"},
                    ],
                ),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=["fog_entry"],
            )
        )

        # Multi-zone cluster with duplicate fog_id across zones
        dag.add_node(
            DagNode(
                id="mid",
                cluster=make_cluster(
                    "c_mid",
                    zones=["zone_a", "zone_b"],
                    cluster_type="boss_arena",
                    entry_fogs=[
                        {"fog_id": "fog_entry", "zone": "zone_b", "text": "Entry"},
                    ],
                    exit_fogs=[
                        {
                            "fog_id": "unique_fog",
                            "zone": "zone_a",
                            "text": "Unique exit",
                        },
                        # Same fog_id, different zones (two sides of one gate)
                        {
                            "fog_id": "shared_fog",
                            "zone": "zone_a",
                            "text": "Shared A side",
                        },
                        {
                            "fog_id": "shared_fog",
                            "zone": "zone_b",
                            "text": "Shared B side",
                        },
                    ],
                ),
                layer=1,
                tier=3,
                entry_fogs=["fog_entry"],
                exit_fogs=["unique_fog", "shared_fog", "shared_fog"],
            )
        )

        dag.add_node(
            DagNode(
                id="dest_1",
                cluster=make_cluster(
                    "c_dest1",
                    zones=["z_d1"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[
                        {"fog_id": "d1_entry", "zone": "z_d1", "text": "Dest 1 entry"},
                    ],
                    exit_fogs=[],
                ),
                layer=2,
                tier=5,
                entry_fogs=["d1_entry"],
                exit_fogs=[],
            )
        )

        dag.add_node(
            DagNode(
                id="dest_2",
                cluster=make_cluster(
                    "c_dest2",
                    zones=["z_d2"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[
                        {"fog_id": "d2_entry", "zone": "z_d2", "text": "Dest 2 entry"},
                    ],
                    exit_fogs=[],
                ),
                layer=2,
                tier=5,
                entry_fogs=["d2_entry"],
                exit_fogs=[],
            )
        )

        dag.add_edge("start", "mid", "fog_entry", "fog_entry")
        # Two edges using the same fog_id but from different zone sides
        dag.add_edge("mid", "dest_1", "shared_fog", "d1_entry")
        dag.add_edge("mid", "dest_2", "shared_fog", "d2_entry")

        dag.start_id = "start"
        dag.end_id = "dest_2"

        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={
                "zone_a": "m10_00",
                "zone_b": "m10_00",
                "z_start": "m00_00",
                "z_d1": "m20_00",
                "z_d2": "m30_00",
            },
            zone_names={},
        )
        return dag, clusters

    def test_connections_have_different_exit_areas(self):
        """Two edges with same fog_id get different exit_area (different fog sides)."""
        dag, clusters = self._make_dag_with_duplicate_exit_fogs()
        result = dag_to_dict(dag, clusters)

        shared_conns = [
            c for c in result["connections"] if "shared_fog" in c["exit_gate"]
        ]
        assert len(shared_conns) == 2

        exit_areas = {c["exit_area"] for c in shared_conns}
        assert exit_areas == {
            "zone_a",
            "zone_b",
        }, f"Expected both zones, got {exit_areas}"

    def test_node_exits_have_different_from_zones(self):
        """Two exits with same fog_id get different 'from' zones."""
        dag, clusters = self._make_dag_with_duplicate_exit_fogs()
        result = dag_to_dict(dag, clusters)

        mid_exits = result["nodes"]["c_mid"]["exits"]
        shared_exits = [e for e in mid_exits if e["fog_id"] == "shared_fog"]
        assert len(shared_exits) == 2

        from_zones = {e["from"] for e in shared_exits}
        assert from_zones == {
            "zone_a",
            "zone_b",
        }, f"Expected both zones, got {from_zones}"


# =============================================================================
# Starting larval tears tests
# =============================================================================


class TestStartingLarvalTears:
    """Tests for starting_larval_tears in graph.json output."""

    def test_default_value(self):
        """Default starting_larval_tears is 10."""
        result = _make_result()
        assert result["starting_larval_tears"] == 10

    def test_custom_value(self):
        """Custom starting_larval_tears is passed through."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters, starting_larval_tears=5)
        assert result["starting_larval_tears"] == 5

    def test_zero_value(self):
        """starting_larval_tears=0 disables rebirth."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters, starting_larval_tears=0)
        assert result["starting_larval_tears"] == 0


# =============================================================================
# ClusterData.from_dict unique exit filtering tests
# =============================================================================


class TestClusterDataFromDictUniqueFiltering:
    """Tests for unique exit_fogs filtering in ClusterData.from_dict()."""

    def test_unique_exits_filtered_from_exit_fogs(self):
        """Unique exits are removed from exit_fogs."""
        data = {
            "id": "test",
            "zones": ["z1"],
            "type": "mini_dungeon",
            "weight": 5,
            "exit_fogs": [
                {"fog_id": "normal_gate", "zone": "z1"},
                {"fog_id": "coffin_warp", "zone": "z1", "unique": True},
            ],
        }
        cluster = ClusterData.from_dict(data)
        assert len(cluster.exit_fogs) == 1
        assert cluster.exit_fogs[0]["fog_id"] == "normal_gate"

    def test_unique_exits_stored_in_unique_exit_fogs(self):
        """Unique exits are stored in unique_exit_fogs."""
        data = {
            "id": "test",
            "zones": ["z1"],
            "type": "mini_dungeon",
            "weight": 5,
            "exit_fogs": [
                {"fog_id": "normal_gate", "zone": "z1"},
                {
                    "fog_id": "coffin_warp",
                    "zone": "z1",
                    "unique": True,
                    "location": 12345,
                },
            ],
        }
        cluster = ClusterData.from_dict(data)
        assert len(cluster.unique_exit_fogs) == 1
        assert cluster.unique_exit_fogs[0]["fog_id"] == "coffin_warp"
        assert cluster.unique_exit_fogs[0]["location"] == 12345

    def test_no_unique_exits_empty_list(self):
        """No unique exits results in empty unique_exit_fogs."""
        data = {
            "id": "test",
            "zones": ["z1"],
            "type": "mini_dungeon",
            "weight": 5,
            "exit_fogs": [{"fog_id": "gate", "zone": "z1"}],
        }
        cluster = ClusterData.from_dict(data)
        assert cluster.unique_exit_fogs == []
        assert len(cluster.exit_fogs) == 1


# =============================================================================
# remove_entities tests
# =============================================================================


class TestRemoveEntities:
    """Tests for remove_entities in dag_to_dict output."""

    def test_empty_when_no_unique_exits(self):
        """remove_entities is empty when no clusters have unique exits."""
        result = _make_result()
        assert result["remove_entities"] == []

    def test_emits_entities_from_unique_exits(self):
        """remove_entities contains entries from unique_exit_fogs with locations."""
        dag = make_test_dag()
        # Add unique exits to a node's cluster
        dag.nodes["a"].cluster.unique_exit_fogs = [
            {
                "fog_id": "coffin_warp",
                "zone": "z_a",
                "unique": True,
                "location": 12051500,
            },
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_a": "m12_05_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert len(result["remove_entities"]) == 1
        assert result["remove_entities"][0] == {
            "map": "m12_05_00_00",
            "entity_id": 12051500,
        }

    def test_skips_unique_exits_without_location(self):
        """Unique exits without location field are skipped."""
        dag = make_test_dag()
        dag.nodes["a"].cluster.unique_exit_fogs = [
            {"fog_id": "warp_no_loc", "zone": "z_a", "unique": True},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_a": "m12_05_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert result["remove_entities"] == []

    def test_deduplicates_same_entity(self):
        """Same (map, entity_id) from different nodes is deduplicated."""
        dag = make_test_dag()
        dag.nodes["a"].cluster.unique_exit_fogs = [
            {"fog_id": "warp1", "zone": "z_a", "unique": True, "location": 12051500},
        ]
        dag.nodes["b"].cluster.unique_exit_fogs = [
            {"fog_id": "warp2", "zone": "z_a", "unique": True, "location": 12051500},
        ]
        # Both reference z_a which maps to same map
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_a": "m12_05_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert len(result["remove_entities"]) == 1


# =============================================================================
# _make_fullname cross-map warp tests
# =============================================================================


def _make_cross_map_fog_data() -> dict:
    """Create fog_data simulating a cross-map boundary warp (e.g., Raya Lucaria).

    Entity 1035452610 is on the academy_entrance side (m60_35_46_00).
    Entity 1035462610 is the paired entity on the liurnia side (m60_35_45_00).
    They connect the same two zones but are in different map tiles.
    """
    return {
        # South gate: academy_entrance side (internal)
        "1035452610": {
            "type": "warp",
            "zones": ["academy_entrance", "liurnia"],
            "map": "m60_35_46_00",
            "destination_map": "m60_35_45_00",
        },
        "m60_35_46_00_1035452610": {
            "type": "warp",
            "zones": ["academy_entrance", "liurnia"],
            "map": "m60_35_46_00",
            "destination_map": "m60_35_45_00",
        },
        # South gate: liurnia side (external) — the paired entity
        "1035462610": {
            "type": "warp",
            "zones": ["liurnia", "academy_entrance"],
            "map": "m60_35_45_00",
            "destination_map": "m60_35_46_00",
        },
        "m60_35_45_00_1035462610": {
            "type": "warp",
            "zones": ["liurnia", "academy_entrance"],
            "map": "m60_35_45_00",
            "destination_map": "m60_35_46_00",
        },
        # A same-map boss warp (no cross-map issue)
        "30122840": {
            "type": "warp",
            "zones": ["dungeon_boss", "dungeon"],
            "map": "m30_12_00_00",
            "destination_map": "m30_12_00_00",
        },
        "m30_12_00_00_30122840": {
            "type": "warp",
            "zones": ["dungeon_boss", "dungeon"],
            "map": "m30_12_00_00",
            "destination_map": "m30_12_00_00",
        },
    }


def _make_pool() -> ClusterPool:
    """Minimal ClusterPool for _make_fullname tests."""
    return ClusterPool(
        clusters=[],
        zone_maps={
            "academy_entrance": "m14_00_00_00",
            "liurnia": "m60_35_45_00",
            "dungeon_boss": "m30_12_00_00",
            "dungeon": "m30_12_00_00",
        },
        zone_names={},
    )


class TestMakeFullnameCrossMapWarps:
    """Tests for _make_fullname handling of cross-map boundary warps.

    Overworld warps that span different map tiles have two entities — one on
    each side. Entry gates need the external-side entity (FogMod From edge),
    exit gates need the internal-side entity (FogMod To edge).
    """

    def test_entry_internal_resolves_to_external(self):
        """Entry warp on internal side resolves to paired external entity."""
        fog_data = _make_cross_map_fog_data()
        pool = _make_pool()

        result = _make_fullname(
            "1035452610", "academy_entrance", pool, fog_data, is_entry=True
        )
        assert result == "m60_35_45_00_1035462610"

    def test_entry_already_external_unchanged(self):
        """Entry warp already on external side stays unchanged."""
        fog_data = _make_cross_map_fog_data()
        pool = _make_pool()

        result = _make_fullname(
            "1035462610", "academy_entrance", pool, fog_data, is_entry=True
        )
        assert result == "m60_35_45_00_1035462610"

    def test_exit_external_resolves_to_internal(self):
        """Exit warp on external side resolves to paired internal entity."""
        fog_data = _make_cross_map_fog_data()
        pool = _make_pool()

        result = _make_fullname(
            "1035462610", "academy_entrance", pool, fog_data, is_entry=False
        )
        assert result == "m60_35_46_00_1035452610"

    def test_exit_already_internal_unchanged(self):
        """Exit warp already on internal side stays unchanged."""
        fog_data = _make_cross_map_fog_data()
        pool = _make_pool()

        result = _make_fullname(
            "1035452610", "academy_entrance", pool, fog_data, is_entry=False
        )
        assert result == "m60_35_46_00_1035452610"

    def test_same_map_warp_unaffected(self):
        """Same-map boss warps (dest_map == map) are not altered."""
        fog_data = _make_cross_map_fog_data()
        pool = _make_pool()

        entry = _make_fullname(
            "30122840", "dungeon_boss", pool, fog_data, is_entry=True
        )
        exit_ = _make_fullname(
            "30122840", "dungeon_boss", pool, fog_data, is_entry=False
        )
        assert entry == "m30_12_00_00_30122840"
        assert exit_ == "m30_12_00_00_30122840"

    def test_no_paired_entity_falls_through(self, capsys):
        """When no paired entity exists, falls through with warning."""
        fog_data = {
            "9999999": {
                "type": "warp",
                "zones": ["zone_a", "zone_b"],
                "map": "m60_01_00_00",
                "destination_map": "m60_02_00_00",
            },
        }
        pool = ClusterPool(
            clusters=[],
            zone_maps={"zone_a": "m60_01_00_00"},
            zone_names={},
        )

        # Entry with internal entity but no paired entity in dest_map
        result = _make_fullname("9999999", "zone_a", pool, fog_data, is_entry=True)
        # Falls through to default: map_id + fog_id
        assert result == "m60_01_00_00_9999999"

        captured = capsys.readouterr()
        assert "Warning" in captured.out
        assert "9999999" in captured.out

    def test_non_numeric_fog_unaffected(self):
        """AEG fog gates (non-numeric) skip the warp cross-map logic entirely."""
        fog_data = {
            "AEG099_001_9000": {
                "zones": ["academy_entrance"],
                "map": "m14_00_00_00",
            },
            "m14_00_00_00_AEG099_001_9000": {
                "zones": ["academy_entrance"],
                "map": "m14_00_00_00",
            },
        }
        pool = _make_pool()

        result = _make_fullname(
            "AEG099_001_9000", "academy_entrance", pool, fog_data, is_entry=True
        )
        assert result == "m14_00_00_00_AEG099_001_9000"
