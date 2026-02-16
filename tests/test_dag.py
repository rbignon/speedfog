"""Tests for DAG data structures."""

from speedfog.clusters import ClusterData
from speedfog.dag import Dag, DagEdge, DagNode, FogRef


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


def _f(fog_id: str, zone: str = "z") -> FogRef:
    """Shorthand for creating FogRef in tests."""
    return FogRef(fog_id, zone)


# =============================================================================
# DagNode tests
# =============================================================================


class TestDagNode:
    """Tests for DagNode hash and equality."""

    def test_hash_by_id(self):
        """DagNode hashes by id only."""
        cluster = make_cluster("c1")
        node1 = DagNode(
            id="node_1",
            cluster=cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[_f("fog_a")],
        )
        node2 = DagNode(
            id="node_1",
            cluster=cluster,
            layer=1,  # Different layer
            tier=5,  # Different tier
            entry_fogs=[_f("different")],
            exit_fogs=[_f("fog_b")],
        )
        assert hash(node1) == hash(node2)

    def test_equality_by_id(self):
        """DagNode equality is by id only."""
        cluster1 = make_cluster("c1")
        cluster2 = make_cluster("c2")
        node1 = DagNode(
            id="same_id",
            cluster=cluster1,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[],
        )
        node2 = DagNode(
            id="same_id",
            cluster=cluster2,  # Different cluster
            layer=5,  # Different layer
            tier=10,  # Different tier
            entry_fogs=[_f("entry")],
            exit_fogs=[_f("exit")],
        )
        assert node1 == node2

    def test_inequality_different_ids(self):
        """DagNode with different ids are not equal."""
        cluster = make_cluster("c1")
        node1 = DagNode(
            id="node_a",
            cluster=cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[],
        )
        node2 = DagNode(
            id="node_b",
            cluster=cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[],
        )
        assert node1 != node2

    def test_usable_in_set(self):
        """DagNode can be used in sets (hashable)."""
        cluster = make_cluster("c1")
        node1 = DagNode(
            id="n1", cluster=cluster, layer=0, tier=1, entry_fogs=[], exit_fogs=[]
        )
        node2 = DagNode(
            id="n2", cluster=cluster, layer=0, tier=1, entry_fogs=[], exit_fogs=[]
        )
        node1_dup = DagNode(
            id="n1",
            cluster=cluster,
            layer=5,
            tier=10,
            entry_fogs=[_f("x")],
            exit_fogs=[_f("y")],
        )

        node_set = {node1, node2, node1_dup}
        assert len(node_set) == 2  # n1 and n2, duplicate removed


# =============================================================================
# DagEdge tests
# =============================================================================


class TestDagEdge:
    """Tests for DagEdge hash and equality."""

    def test_hash_by_tuple(self):
        """DagEdge hashes by (source_id, target_id, fog_id)."""
        edge1 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        edge2 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        assert hash(edge1) == hash(edge2)

    def test_equality_by_tuple(self):
        """DagEdge equality is by (source_id, target_id, fog_id)."""
        edge1 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        edge2 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        assert edge1 == edge2

    def test_inequality_different_source(self):
        """DagEdge with different source_id are not equal."""
        edge1 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        edge2 = DagEdge(
            source_id="x",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        assert edge1 != edge2

    def test_inequality_different_target(self):
        """DagEdge with different target_id are not equal."""
        edge1 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        edge2 = DagEdge(
            source_id="a",
            target_id="x",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        assert edge1 != edge2

    def test_inequality_different_fog(self):
        """DagEdge with different fog_id are not equal."""
        edge1 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        edge2 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_2"),
            entry_fog=_f("fog_2"),
        )
        assert edge1 != edge2

    def test_usable_in_set(self):
        """DagEdge can be used in sets (hashable)."""
        edge1 = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )
        edge2 = DagEdge(
            source_id="a",
            target_id="c",
            exit_fog=_f("fog_2"),
            entry_fog=_f("fog_2"),
        )
        edge1_dup = DagEdge(
            source_id="a",
            target_id="b",
            exit_fog=_f("fog_1"),
            entry_fog=_f("fog_1"),
        )

        edge_set = {edge1, edge2, edge1_dup}
        assert len(edge_set) == 2


# =============================================================================
# Dag basic operations tests
# =============================================================================


class TestDagBasicOperations:
    """Tests for Dag add/get operations."""

    def test_add_node(self):
        """Dag.add_node adds node to nodes dict."""
        dag = Dag(seed=42)
        cluster = make_cluster("c1")
        node = DagNode(
            id="n1", cluster=cluster, layer=0, tier=1, entry_fogs=[], exit_fogs=[]
        )

        dag.add_node(node)

        assert "n1" in dag.nodes
        assert dag.nodes["n1"] is node

    def test_add_edge(self):
        """Dag.add_edge adds edge to edges list."""
        dag = Dag(seed=42)

        dag.add_edge("a", "b", _f("fog_1"), _f("fog_1"))

        assert len(dag.edges) == 1
        assert dag.edges[0].source_id == "a"
        assert dag.edges[0].target_id == "b"
        assert dag.edges[0].fog_id == "fog_1"

    def test_get_node_existing(self):
        """Dag.get_node returns node by id."""
        dag = Dag(seed=42)
        cluster = make_cluster("c1")
        node = DagNode(
            id="n1", cluster=cluster, layer=0, tier=1, entry_fogs=[], exit_fogs=[]
        )
        dag.add_node(node)

        result = dag.get_node("n1")

        assert result is node

    def test_get_node_missing(self):
        """Dag.get_node returns None for missing node."""
        dag = Dag(seed=42)

        result = dag.get_node("nonexistent")

        assert result is None

    def test_get_outgoing_edges(self):
        """Dag.get_outgoing_edges returns edges from a node."""
        dag = Dag(seed=42)
        dag.add_edge("a", "b", _f("fog_1"), _f("fog_1"))
        dag.add_edge("a", "c", _f("fog_2"), _f("fog_2"))
        dag.add_edge("b", "c", _f("fog_3"), _f("fog_3"))

        edges = dag.get_outgoing_edges("a")

        assert len(edges) == 2
        targets = {e.target_id for e in edges}
        assert targets == {"b", "c"}

    def test_get_outgoing_edges_empty(self):
        """Dag.get_outgoing_edges returns empty list for node with no outgoing."""
        dag = Dag(seed=42)
        dag.add_edge("a", "b", _f("fog_1"), _f("fog_1"))

        edges = dag.get_outgoing_edges("b")

        assert edges == []

    def test_get_incoming_edges(self):
        """Dag.get_incoming_edges returns edges to a node."""
        dag = Dag(seed=42)
        dag.add_edge("a", "c", _f("fog_1"), _f("fog_1"))
        dag.add_edge("b", "c", _f("fog_2"), _f("fog_2"))
        dag.add_edge("c", "d", _f("fog_3"), _f("fog_3"))

        edges = dag.get_incoming_edges("c")

        assert len(edges) == 2
        sources = {e.source_id for e in edges}
        assert sources == {"a", "b"}

    def test_get_incoming_edges_empty(self):
        """Dag.get_incoming_edges returns empty list for node with no incoming."""
        dag = Dag(seed=42)
        dag.add_edge("a", "b", _f("fog_1"), _f("fog_1"))

        edges = dag.get_incoming_edges("a")

        assert edges == []


# =============================================================================
# Path enumeration tests
# =============================================================================


class TestDagPathEnumeration:
    """Tests for Dag.enumerate_paths."""

    def test_enumerate_paths_linear(self):
        """enumerate_paths returns single path for linear DAG."""
        dag = Dag(seed=42)
        for i, node_id in enumerate(["start", "mid", "end"]):
            cluster = make_cluster(f"c{i}", weight=10)
            dag.add_node(
                DagNode(
                    id=node_id,
                    cluster=cluster,
                    layer=i,
                    tier=1,
                    entry_fogs=[],
                    exit_fogs=[],
                )
            )
        dag.add_edge("start", "mid", _f("fog_1"), _f("fog_1"))
        dag.add_edge("mid", "end", _f("fog_2"), _f("fog_2"))
        dag.start_id = "start"
        dag.end_id = "end"

        paths = dag.enumerate_paths()

        assert len(paths) == 1
        assert paths[0] == ["start", "mid", "end"]

    def test_enumerate_paths_forked(self):
        """enumerate_paths returns all paths for forked DAG."""
        # Structure:
        #       start
        #      /     \
        #     a       b
        #      \     /
        #        end
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c0"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=1,
                tier=5,
                entry_fogs=[_f("e1")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c2"),
                layer=1,
                tier=5,
                entry_fogs=[_f("e2")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c3"),
                layer=2,
                tier=10,
                entry_fogs=[_f("e3")],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "a", _f("fog_1"), _f("fog_1"))
        dag.add_edge("start", "b", _f("fog_2"), _f("fog_2"))
        dag.add_edge("a", "end", _f("fog_3"), _f("fog_3"))
        dag.add_edge("b", "end", _f("fog_4"), _f("fog_4"))
        dag.start_id = "start"
        dag.end_id = "end"

        paths = dag.enumerate_paths()

        assert len(paths) == 2
        path_tuples = {tuple(p) for p in paths}
        assert ("start", "a", "end") in path_tuples
        assert ("start", "b", "end") in path_tuples

    def test_enumerate_paths_no_start(self):
        """enumerate_paths returns empty list when start_id not set."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.end_id = "a"

        paths = dag.enumerate_paths()

        assert paths == []

    def test_enumerate_paths_no_end(self):
        """enumerate_paths returns empty list when end_id not set."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.start_id = "a"

        paths = dag.enumerate_paths()

        assert paths == []

    def test_path_weight(self):
        """path_weight returns sum of cluster weights."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1", weight=10),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c2", weight=15),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="c",
                cluster=make_cluster("c3", weight=20),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )

        weight = dag.path_weight(["a", "b", "c"])

        assert weight == 45

    def test_path_weight_empty(self):
        """path_weight returns 0 for empty path."""
        dag = Dag(seed=42)

        weight = dag.path_weight([])

        assert weight == 0


# =============================================================================
# Statistics tests
# =============================================================================


class TestDagStatistics:
    """Tests for Dag statistics methods."""

    def test_total_nodes(self):
        """total_nodes returns count of nodes."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="c",
                cluster=make_cluster("c3"),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )

        assert dag.total_nodes() == 3

    def test_total_nodes_empty(self):
        """total_nodes returns 0 for empty DAG."""
        dag = Dag(seed=42)

        assert dag.total_nodes() == 0

    def test_total_zones(self):
        """total_zones returns count of unique zones across all nodes."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1", zones=["z1", "z2"]),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c2", zones=["z2", "z3"]),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="c",
                cluster=make_cluster("c3", zones=["z4"]),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )

        assert dag.total_zones() == 4  # z1, z2, z3, z4

    def test_total_zones_empty(self):
        """total_zones returns 0 for empty DAG."""
        dag = Dag(seed=42)

        assert dag.total_zones() == 0

    def test_count_by_type(self):
        """count_by_type returns count of nodes with matching cluster type."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1", cluster_type="legacy_dungeon"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c2", cluster_type="mini_dungeon"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="c",
                cluster=make_cluster("c3", cluster_type="legacy_dungeon"),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="d",
                cluster=make_cluster("c4", cluster_type="boss_arena"),
                layer=3,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )

        assert dag.count_by_type("legacy_dungeon") == 2
        assert dag.count_by_type("mini_dungeon") == 1
        assert dag.count_by_type("boss_arena") == 1
        assert dag.count_by_type("nonexistent") == 0


# =============================================================================
# Validation tests
# =============================================================================


class TestDagValidation:
    """Tests for Dag.validate_structure."""

    def test_validate_missing_start_id(self):
        """validate_structure reports missing start_id."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.end_id = "a"

        errors = dag.validate_structure()

        assert any("start" in e.lower() for e in errors)

    def test_validate_missing_end_id(self):
        """validate_structure reports missing end_id."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.start_id = "a"

        errors = dag.validate_structure()

        assert any("end" in e.lower() for e in errors)

    def test_validate_start_node_missing(self):
        """validate_structure reports when start_id references missing node."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.start_id = "nonexistent"
        dag.end_id = "a"

        errors = dag.validate_structure()

        assert any("start" in e.lower() and "not found" in e.lower() for e in errors)

    def test_validate_end_node_missing(self):
        """validate_structure reports when end_id references missing node."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.start_id = "a"
        dag.end_id = "nonexistent"

        errors = dag.validate_structure()

        assert any("end" in e.lower() and "not found" in e.lower() for e in errors)

    def test_validate_unreachable_nodes(self):
        """validate_structure reports nodes not reachable from start."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="orphan",
                cluster=make_cluster("c3"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "end", _f("fog_1"), _f("fog_1"))
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert any("unreachable" in e.lower() and "orphan" in e for e in errors)

    def test_validate_dead_ends(self):
        """validate_structure reports nodes that cannot reach end."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="dead",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c3"),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "dead", _f("fog_1"), _f("fog_1"))
        dag.add_edge("start", "end", _f("fog_2"), _f("fog_2"))
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert any("dead end" in e.lower() and "dead" in e for e in errors)

    def test_validate_backward_edges(self):
        """validate_structure reports edges going to same or lower layer."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="mid",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c3"),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "mid", _f("fog_1"), _f("fog_1"))
        dag.add_edge("mid", "end", _f("fog_2"), _f("fog_2"))
        dag.add_edge("mid", "start", _f("fog_3"), _f("fog_3"))  # Backward edge!
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert any("backward" in e.lower() for e in errors)

    def test_validate_same_layer_edge(self):
        """validate_structure reports edges between nodes in same layer."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c3"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c4"),
                layer=2,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "a", _f("fog_1"), _f("fog_1"))
        dag.add_edge("start", "b", _f("fog_2"), _f("fog_2"))
        dag.add_edge("a", "b", _f("fog_3"), _f("fog_3"))  # Same layer edge!
        dag.add_edge("b", "end", _f("fog_4"), _f("fog_4"))
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert any("backward" in e.lower() or "same layer" in e.lower() for e in errors)

    def test_validate_valid_dag(self):
        """validate_structure returns empty list for valid DAG."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("c2"),
                layer=1,
                tier=5,
                entry_fogs=[_f("e1")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("c3"),
                layer=1,
                tier=5,
                entry_fogs=[_f("e2")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c4"),
                layer=2,
                tier=10,
                entry_fogs=[_f("e3")],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "a", _f("fog_1"), _f("fog_1"))
        dag.add_edge("start", "b", _f("fog_2"), _f("fog_2"))
        dag.add_edge("a", "end", _f("fog_3"), _f("fog_3"))
        dag.add_edge("b", "end", _f("fog_4"), _f("fog_4"))
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert errors == []

    def test_validate_edge_references_missing_source(self):
        """validate_structure reports edges with missing source node."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "end", _f("fog_1"), _f("fog_1"))
        dag.add_edge("nonexistent", "end", _f("fog_2"), _f("fog_2"))  # Missing source
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert any("nonexistent" in e for e in errors)

    def test_validate_edge_references_missing_target(self):
        """validate_structure reports edges with missing target node."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("c1"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("c2"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "end", _f("fog_1"), _f("fog_1"))
        dag.add_edge("start", "nonexistent", _f("fog_2"), _f("fog_2"))  # Missing target
        dag.start_id = "start"
        dag.end_id = "end"

        errors = dag.validate_structure()

        assert any("nonexistent" in e for e in errors)
