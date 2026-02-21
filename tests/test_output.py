"""Tests for output module (JSON and spoiler log export)."""

from pathlib import Path

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.dag import Dag, DagNode, FogRef
from speedfog.output import (
    _effective_type,
    _make_fullname,
    dag_to_dict,
    export_spoiler_log,
    load_vanilla_tiers,
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
            exit_fogs=[FogRef("fog_1", "z_start"), FogRef("fog_2", "z_start")],
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
            entry_fogs=[FogRef("fog_1", "z_a")],
            exit_fogs=[FogRef("fog_3", "z_a")],
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
            entry_fogs=[FogRef("fog_2", "z_b1")],
            exit_fogs=[FogRef("fog_4", "z_b1")],
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
            entry_fogs=[FogRef("fog_3", "z_end"), FogRef("fog_4", "z_end")],
            exit_fogs=[],
        )
    )

    # Add edges
    dag.add_edge("start", "a", FogRef("fog_1", "z_start"), FogRef("fog_1", "z_a"))
    dag.add_edge("start", "b", FogRef("fog_2", "z_start"), FogRef("fog_2", "z_b1"))
    dag.add_edge("a", "end", FogRef("fog_3", "z_a"), FogRef("fog_3", "z_end"))
    dag.add_edge("b", "end", FogRef("fog_4", "z_b1"), FogRef("fog_4", "z_end"))

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

    def test_version_is_4_1(self):
        """Version string is '4.1'."""
        result = _make_result()
        assert result["version"] == "4.1"

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
        """final_node_flag matches one of the zone-tracking flags for the end node."""
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

    def test_merge_node_connections_have_unique_flag_ids(self):
        """Two connections to the same node get different flag_ids.

        In the diamond DAG, edges a→end and b→end have unique flags so the
        racing mod can detect re-entry from a different branch.
        Both flags map to the same cluster in event_map.
        """
        result = _make_result()
        # Find connections going to the end node's zone (z_end)
        end_connections = [
            c for c in result["connections"] if c["entrance_area"] == "z_end"
        ]
        assert len(end_connections) == 2
        assert end_connections[0]["flag_id"] != end_connections[1]["flag_id"]

        # Both flags map to the same cluster in event_map
        event_map = result["event_map"]
        cluster_0 = event_map[str(end_connections[0]["flag_id"])]
        cluster_1 = event_map[str(end_connections[1]["flag_id"])]
        assert cluster_0 == cluster_1

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

    def test_exit_from_text_from_zone_names(self):
        """Exit 'from_text' is populated from zone_names when available."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={"z_start": "Starting Area", "z_b1": "Branch B Room 1"},
        )
        result = dag_to_dict(dag, clusters)
        # start exits have from=z_start → from_text present
        for exit_item in result["nodes"]["c_start"]["exits"]:
            assert exit_item["from_text"] == "Starting Area"
        # branch b exit has from=z_b1 → from_text present
        b_exits = result["nodes"]["c_b"]["exits"]
        assert b_exits[0]["from_text"] == "Branch B Room 1"

    def test_exit_from_text_absent_when_zone_name_missing(self):
        """Exit has no 'from_text' when zone_names lacks the zone ID."""
        result = _make_result()  # uses zone_names={}
        for node_data in result["nodes"].values():
            for exit_item in node_data["exits"]:
                assert "from_text" not in exit_item

    def test_exit_from_uses_fogref_zone_even_when_not_in_exit_fogs(self):
        """Exit 'from' uses FogRef zone even when fog_id not in cluster exit_fogs."""
        dag = make_test_dag()
        dag.add_edge(
            "start", "a", FogRef("unknown_fog", "z_start"), FogRef("fog_1", "z_a")
        )
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        start_exits = result["nodes"]["c_start"]["exits"]
        unknown_exits = [e for e in start_exits if e["fog_id"] == "unknown_fog"]
        assert len(unknown_exits) == 1
        # FogRef always carries zone, so 'from' is always present
        assert unknown_exits[0]["from"] == "z_start"

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
# Node entrances tests
# =============================================================================


class TestNodeEntrances:
    """Tests for entrances field in graph.json nodes."""

    def test_entrances_present_in_nodes(self):
        """Every node has an entrances key."""
        result = _make_result()
        for node_id, node_data in result["nodes"].items():
            assert "entrances" in node_data, f"Node {node_id} missing entrances"

    def test_entrances_from_dag_edges(self):
        """Entrances match actual DAG edges targeting each node."""
        result = _make_result()
        # end node has 2 entrances (from c_a and c_b)
        end_entrances = result["nodes"]["c_end"]["entrances"]
        assert len(end_entrances) == 2
        entrance_sources = {e["from"] for e in end_entrances}
        assert entrance_sources == {"c_a", "c_b"}

    def test_entrance_fields(self):
        """Each entrance has text, from, and to fields."""
        result = _make_result()
        for node_id, node_data in result["nodes"].items():
            for ent in node_data["entrances"]:
                assert "text" in ent, f"Entrance in {node_id} missing text"
                assert "from" in ent, f"Entrance in {node_id} missing from"
                assert "to" in ent, f"Entrance in {node_id} missing to"

    def test_start_node_has_no_entrances(self):
        """Start node has entrances: []."""
        result = _make_result()
        assert result["nodes"]["c_start"]["entrances"] == []

    def test_entrance_text_from_cluster(self):
        """Entrance text comes from the cluster's entry_fogs text field."""
        result = _make_result()
        # c_a has entry_fogs with text "Gate to A"
        a_entrances = result["nodes"]["c_a"]["entrances"]
        assert len(a_entrances) == 1
        assert a_entrances[0]["text"] == "Gate to A"

    def test_entrance_from_is_source_cluster_id(self):
        """Entrance 'from' field is the source cluster ID."""
        result = _make_result()
        a_entrances = result["nodes"]["c_a"]["entrances"]
        assert a_entrances[0]["from"] == "c_start"
        b_entrances = result["nodes"]["c_b"]["entrances"]
        assert b_entrances[0]["from"] == "c_start"

    def test_entrance_to_is_entry_zone(self):
        """Entrance 'to' field is the zone within this node where we arrive."""
        result = _make_result()
        a_entrances = result["nodes"]["c_a"]["entrances"]
        assert a_entrances[0]["to"] == "z_a"
        b_entrances = result["nodes"]["c_b"]["entrances"]
        assert b_entrances[0]["to"] == "z_b1"

    def test_entrance_to_text_from_zone_names(self):
        """Entrance 'to_text' is populated from zone_names when available."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={"z_a": "Branch A Room", "z_b1": "Branch B Room 1"},
        )
        result = dag_to_dict(dag, clusters)
        a_entrances = result["nodes"]["c_a"]["entrances"]
        assert a_entrances[0]["to_text"] == "Branch A Room"
        b_entrances = result["nodes"]["c_b"]["entrances"]
        assert b_entrances[0]["to_text"] == "Branch B Room 1"

    def test_entrance_to_text_absent_when_zone_name_missing(self):
        """Entrance has no 'to_text' when zone_names lacks the zone ID."""
        result = _make_result()  # uses zone_names={}
        for node_data in result["nodes"].values():
            for ent in node_data["entrances"]:
                assert "to_text" not in ent

    def test_entrance_text_prefers_side_text(self):
        """When side_text is present in entry_fogs, it takes priority over text."""
        dag = make_test_dag()
        dag.nodes["a"].cluster.entry_fogs = [
            {
                "fog_id": "fog_1",
                "zone": "z_a",
                "text": "Gate Name",
                "side_text": "detailed entry description",
            },
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        a_entrances = result["nodes"]["c_a"]["entrances"]
        assert a_entrances[0]["text"] == "detailed entry description"

    def test_entrance_text_fallback_to_fog_id(self):
        """When text is missing from entry_fogs, falls back to fog_id."""
        dag = make_test_dag()
        dag.nodes["a"].cluster.entry_fogs = [
            {"fog_id": "fog_1", "zone": "z_a"},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        a_entrances = result["nodes"]["c_a"]["entrances"]
        assert a_entrances[0]["text"] == "fog_1"

    def test_merge_node_has_multiple_entrances(self):
        """End node (merge point) has entrances from both branches."""
        result = _make_result()
        end_entrances = result["nodes"]["c_end"]["entrances"]
        assert len(end_entrances) == 2
        # Check both branches contribute
        from_clusters = [e["from"] for e in end_entrances]
        assert "c_a" in from_clusters
        assert "c_b" in from_clusters


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
                exit_fogs=[FogRef("fog_entry", "z_start")],
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
                entry_fogs=[FogRef("fog_entry", "zone_b")],
                exit_fogs=[
                    FogRef("unique_fog", "zone_a"),
                    FogRef("shared_fog", "zone_a"),
                    FogRef("shared_fog", "zone_b"),
                ],
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
                entry_fogs=[FogRef("d1_entry", "z_d1")],
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
                entry_fogs=[FogRef("d2_entry", "z_d2")],
                exit_fogs=[],
            )
        )

        dag.add_edge(
            "start",
            "mid",
            FogRef("fog_entry", "z_start"),
            FogRef("fog_entry", "zone_b"),
        )
        # Two edges using the same fog_id but from different zone sides
        dag.add_edge(
            "mid",
            "dest_1",
            FogRef("shared_fog", "zone_a"),
            FogRef("d1_entry", "z_d1"),
        )
        dag.add_edge(
            "mid",
            "dest_2",
            FogRef("shared_fog", "zone_b"),
            FogRef("d2_entry", "z_d2"),
        )

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
# Duplicate exit fog: zone resolution regression test
# =============================================================================


class TestDuplicateExitFogs:
    """Regression test for duplicate fog_ids resolving to wrong zone.

    When a cluster has the same fog_id as both an entry and an exit in
    different zones, the exit should use the zone that wasn't consumed
    by the entry — not the first match.
    """

    def test_single_edge_picks_correct_zone_for_duplicate_fog(self):
        """Single edge with duplicate fog_id resolves to correct zone (not first match)."""
        dag = Dag(seed=42)

        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster(
                    "c_start",
                    zones=["z_start"],
                    cluster_type="start",
                    entry_fogs=[],
                    exit_fogs=[
                        {"fog_id": "fog_entry", "zone": "z_start", "text": "Go"},
                    ],
                ),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[FogRef("fog_entry", "z_start")],
            )
        )

        # Cluster with same fog_id in two zones (like belurat_2c41)
        dag.add_node(
            DagNode(
                id="mid",
                cluster=make_cluster(
                    "c_mid",
                    zones=["zone_a", "zone_b"],
                    cluster_type="legacy_dungeon",
                    entry_fogs=[
                        {"fog_id": "shared_fog", "zone": "zone_a", "text": "Entry A"},
                    ],
                    exit_fogs=[
                        {"fog_id": "shared_fog", "zone": "zone_a", "text": "Side A"},
                        {"fog_id": "shared_fog", "zone": "zone_b", "text": "Side B"},
                    ],
                ),
                layer=1,
                tier=3,
                # After consuming entry (zone_a), only zone_b exit remains
                # FogRef now carries zone info:
                entry_fogs=[FogRef("shared_fog", "zone_a")],
                exit_fogs=[FogRef("shared_fog", "zone_b")],
            )
        )

        dag.add_node(
            DagNode(
                id="dest",
                cluster=make_cluster(
                    "c_dest",
                    zones=["z_dest"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[
                        {"fog_id": "d_entry", "zone": "z_dest", "text": "Dest entry"},
                    ],
                    exit_fogs=[],
                ),
                layer=2,
                tier=5,
                entry_fogs=[FogRef("d_entry", "z_dest")],
                exit_fogs=[],
            )
        )

        dag.add_edge(
            "start",
            "mid",
            FogRef("fog_entry", "z_start"),
            FogRef("shared_fog", "zone_a"),
        )
        dag.add_edge(
            "mid",
            "dest",
            FogRef("shared_fog", "zone_b"),
            FogRef("d_entry", "z_dest"),
        )
        dag.start_id = "start"
        dag.end_id = "dest"

        clusters = ClusterPool(
            clusters=[n.cluster for n in dag.nodes.values()],
            zone_maps={
                "zone_a": "m10_00",
                "zone_b": "m20_00",
                "z_start": "m00_00",
                "z_dest": "m30_00",
            },
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)

        # The exit edge from mid should use zone_b (the remaining exit after
        # consuming zone_a entry)
        mid_conn = [
            c for c in result["connections"] if c["exit_area"] in ("zone_a", "zone_b")
        ]
        assert len(mid_conn) == 1
        assert mid_conn[0]["exit_area"] == "zone_b", (
            f"Expected zone_b (remaining after entry consumed zone_a), "
            f"got {mid_conn[0]['exit_area']}"
        )


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

    def test_unused_exit_with_location_removed(self):
        """Regular exit_fogs with location but NOT used in edges are removed."""
        dag = make_test_dag()
        # Add a location to an exit_fog that is NOT used in any edge
        # The end node has no outgoing edges, so all its exit_fogs are unused
        dag.nodes["end"].cluster.exit_fogs = [
            {"fog_id": "unused_warp", "zone": "z_end", "location": 99999},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_end": "m13_00_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert {"map": "m13_00_00_00", "entity_id": 99999} in result["remove_entities"]

    def test_used_exit_with_location_not_removed(self):
        """Regular exit_fogs with location that ARE used in edges are NOT removed."""
        dag = make_test_dag()
        # Add location to start's fog_1 exit, which IS used (edge start→a)
        dag.nodes["start"].cluster.exit_fogs = [
            {"fog_id": "fog_1", "zone": "z_start", "location": 88888},
            {"fog_id": "fog_2", "zone": "z_start"},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_start": "m10_00_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        # fog_1 is used in edge start→a, so its entity should NOT be removed
        removed = {(e["map"], e["entity_id"]) for e in result["remove_entities"]}
        assert ("m10_00_00_00", 88888) not in removed

    def test_unused_exit_without_location_ignored(self):
        """Regular exit_fogs without location are not added to remove_entities."""
        dag = make_test_dag()
        # end node's exit_fogs have no location
        dag.nodes["end"].cluster.exit_fogs = [
            {"fog_id": "unused_warp", "zone": "z_end"},  # No location
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_end": "m13_00_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        assert result["remove_entities"] == []

    def test_unused_exit_deduplicates_with_unique_exits(self):
        """Unused regular exit and unique exit with same entity are deduplicated."""
        dag = make_test_dag()
        # Same entity from unique and regular exit
        dag.nodes["a"].cluster.unique_exit_fogs = [
            {"fog_id": "warp1", "zone": "z_a", "unique": True, "location": 55555},
        ]
        dag.nodes["end"].cluster.exit_fogs = [
            {"fog_id": "warp2", "zone": "z_a", "location": 55555},
        ]
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={"z_a": "m12_05_00_00"},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)
        # Should be deduplicated to just 1 entry
        matching = [e for e in result["remove_entities"] if e["entity_id"] == 55555]
        assert len(matching) == 1


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


# =============================================================================
# load_vanilla_tiers tests
# =============================================================================


class TestLoadVanillaTiers:
    """Tests for load_vanilla_tiers function."""

    def test_parses_enemy_areas(self, tmp_path: Path):
        """Parses Name and ScalingTier from EnemyAreas section."""
        content = """\
Items: []
EnemyAreas:
- Name: limgrave
  Groups: 123
  ScalingTier: 1
- Name: stormveil
  Cols: m10_00_00_00_h001
  ScalingTier: 3
- Name: caelid
  ScalingTier: 10
"""
        p = tmp_path / "foglocations2.txt"
        p.write_text(content, encoding="utf-8")

        tiers = load_vanilla_tiers(p)
        assert tiers == {"limgrave": 1, "stormveil": 3, "caelid": 10}

    def test_missing_file_returns_empty(self, tmp_path: Path):
        """Returns empty dict when file doesn't exist."""
        p = tmp_path / "nonexistent.txt"
        tiers = load_vanilla_tiers(p)
        assert tiers == {}

    def test_no_enemy_areas_returns_empty(self, tmp_path: Path):
        """Returns empty dict when file has no EnemyAreas section."""
        p = tmp_path / "foglocations2.txt"
        p.write_text("Items: []\n", encoding="utf-8")
        tiers = load_vanilla_tiers(p)
        assert tiers == {}

    def test_real_file(self):
        """Smoke test against the real foglocations2.txt."""
        real_path = Path(__file__).parent.parent / "data" / "foglocations2.txt"
        if not real_path.exists():
            return  # Skip if not available
        tiers = load_vanilla_tiers(real_path)
        # Should have many entries
        assert len(tiers) > 50
        # Spot-check known values
        assert tiers["limgrave"] == 1
        assert tiers["stormveil"] == 3
        assert tiers["stormveil_godrick"] == 4


# =============================================================================
# original_tier in dag_to_dict tests
# =============================================================================


class TestOriginalTier:
    """Tests for original_tier field in dag_to_dict output."""

    def test_none_when_no_vanilla_tiers(self):
        """original_tier is None when vanilla_tiers not provided."""
        result = _make_result()
        for node_data in result["nodes"].values():
            assert node_data["original_tier"] is None

    def test_populated_from_vanilla_tiers(self):
        """original_tier is set from vanilla_tiers mapping."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        vanilla_tiers = {"z_start": 1, "z_a": 5, "z_b1": 8, "z_b2": 12, "z_end": 20}
        result = dag_to_dict(dag, clusters, vanilla_tiers=vanilla_tiers)

        assert result["nodes"]["c_start"]["original_tier"] == 1
        assert result["nodes"]["c_a"]["original_tier"] == 5
        # c_b has zones z_b1 (8) and z_b2 (12) → max is 12
        assert result["nodes"]["c_b"]["original_tier"] == 12
        assert result["nodes"]["c_end"]["original_tier"] == 20

    def test_max_of_multiple_zones(self):
        """original_tier uses max tier across multiple zones in a node."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        # Only provide tiers for the multi-zone node (c_b has z_b1, z_b2)
        vanilla_tiers = {"z_b1": 3, "z_b2": 7}
        result = dag_to_dict(dag, clusters, vanilla_tiers=vanilla_tiers)

        assert result["nodes"]["c_b"]["original_tier"] == 7
        # Nodes with no matching zones get None
        assert result["nodes"]["c_start"]["original_tier"] is None

    def test_partial_zone_coverage(self):
        """original_tier works when only some zones have tiers."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        # Only z_b2 has a tier, z_b1 doesn't
        vanilla_tiers = {"z_b2": 15}
        result = dag_to_dict(dag, clusters, vanilla_tiers=vanilla_tiers)

        assert result["nodes"]["c_b"]["original_tier"] == 15


# =============================================================================
# exit_entity_id tests
# =============================================================================


class TestExitEntityId:
    """Tests for exit_entity_id field in dag_to_dict connections."""

    def test_connections_have_exit_entity_id(self):
        """Connections have exit_entity_id populated from fog_data entity_id."""
        dag = make_test_dag()
        # fog_data keyed by fullname (after _make_fullname resolution).
        # With zone_maps provided, _make_fullname produces "{map}_{fog_id}" fullnames.
        fog_data = {
            "m10_00_00_00_fog_1": {
                "zones": ["z_start"],
                "map": "m10_00_00_00",
                "entity_id": 13001850,
            },
            "m10_00_00_00_fog_2": {
                "zones": ["z_start"],
                "map": "m10_00_00_00",
                "entity_id": 13001851,
            },
            "m11_00_00_00_fog_3": {
                "zones": ["z_a"],
                "map": "m11_00_00_00",
                "entity_id": 13001900,
            },
            "m12_00_00_00_fog_4": {
                "zones": ["z_b1"],
                "map": "m12_00_00_00",
                "entity_id": 13001901,
            },
        }
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={
                "z_start": "m10_00_00_00",
                "z_a": "m11_00_00_00",
                "z_b1": "m12_00_00_00",
                "z_b2": "m12_00_00_00",
                "z_end": "m13_00_00_00",
            },
            zone_names={},
        )
        result = dag_to_dict(dag, clusters, fog_data=fog_data)
        # Verify specific entity_id values by matching exit_gate fullnames
        entity_by_gate = {
            c["exit_gate"]: c["exit_entity_id"] for c in result["connections"]
        }
        assert entity_by_gate["m10_00_00_00_fog_1"] == 13001850
        assert entity_by_gate["m10_00_00_00_fog_2"] == 13001851
        assert entity_by_gate["m11_00_00_00_fog_3"] == 13001900
        assert entity_by_gate["m12_00_00_00_fog_4"] == 13001901

    def test_connections_exit_entity_id_zero_without_fog_data(self):
        """Without fog_data, exit_entity_id defaults to 0."""
        result = _make_result()
        for conn in result["connections"]:
            assert conn["exit_entity_id"] == 0

    def test_connections_exit_entity_id_zero_when_not_in_fog_data(self):
        """When fullname is not in fog_data, exit_entity_id is 0."""
        dag = make_test_dag()
        # fog_data with keys that won't match the fullnames
        fog_data = {
            "m99_00_00_00_AEG099_999_9999": {
                "zones": ["unrelated"],
                "map": "m99_00_00_00",
                "entity_id": 99999999,
            },
        }
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={},
            zone_names={},
        )
        result = dag_to_dict(dag, clusters, fog_data=fog_data)
        for conn in result["connections"]:
            assert conn["exit_entity_id"] == 0


class TestIgnorePairInConnections:
    """Tests for ignore_pair field in dag_to_dict connections."""

    def test_ignore_pair_set_when_target_has_allow_entry_as_exit(self):
        """Connections targeting an allow_entry_as_exit cluster get ignore_pair=True."""
        dag = Dag(seed=42)
        start_cluster = make_cluster(
            "c_start",
            zones=["z_start"],
            cluster_type="start",
            weight=5,
            entry_fogs=[],
            exit_fogs=[{"fog_id": "fog_1", "zone": "z_start", "text": "Out"}],
        )
        boss_cluster = ClusterData(
            id="c_boss",
            zones=["z_boss"],
            type="boss_arena",
            weight=3,
            entry_fogs=[{"fog_id": "fog_1", "zone": "z_boss", "text": "In"}],
            exit_fogs=[
                {"fog_id": "fog_1", "zone": "z_boss", "text": "Back"},
                {"fog_id": "fog_2", "zone": "z_boss", "text": "Out"},
            ],
            allow_entry_as_exit=True,
        )
        dag.add_node(
            DagNode(
                id="start",
                cluster=start_cluster,
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[FogRef("fog_1", "z_start")],
            )
        )
        dag.add_node(
            DagNode(
                id="boss",
                cluster=boss_cluster,
                layer=1,
                tier=5,
                entry_fogs=[FogRef("fog_1", "z_boss")],
                exit_fogs=[FogRef("fog_1", "z_boss"), FogRef("fog_2", "z_boss")],
            )
        )
        dag.add_edge(
            "start", "boss", FogRef("fog_1", "z_start"), FogRef("fog_1", "z_boss")
        )
        dag.start_id = "start"
        dag.end_id = "boss"

        clusters = ClusterPool(
            clusters=[start_cluster, boss_cluster],
            zone_maps={"z_start": "m10_00_00_00", "z_boss": "m11_00_00_00"},
            zone_names={},
        )

        result = dag_to_dict(dag, clusters)

        assert len(result["connections"]) == 1
        assert result["connections"][0]["ignore_pair"] is True

    def test_no_ignore_pair_when_target_is_normal(self):
        """Connections targeting a normal cluster do NOT get ignore_pair."""
        dag = make_test_dag()
        clusters = ClusterPool(
            clusters=[node.cluster for node in dag.nodes.values()],
            zone_maps={
                "z_start": "m10_00_00_00",
                "z_a": "m11_00_00_00",
                "z_b1": "m12_00_00_00",
                "z_b2": "m12_00_00_00",
                "z_end": "m13_00_00_00",
            },
            zone_names={},
        )
        result = dag_to_dict(dag, clusters)

        for conn in result["connections"]:
            assert "ignore_pair" not in conn
