"""Tests for cross-link post-processing."""

import random

from speedfog.clusters import ClusterData
from speedfog.crosslinks import (
    _available_entries,
    _surplus_exits,
    add_crosslinks,
    find_eligible_pairs,
)
from speedfog.dag import Dag, DagNode, FogRef


def make_cluster(
    cluster_id: str,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | None = None,
    exit_fogs: list[dict] | None = None,
    allow_entry_as_exit: bool = False,
) -> ClusterData:
    return ClusterData(
        id=cluster_id,
        zones=[f"{cluster_id}_zone"],
        type=cluster_type,
        weight=weight,
        entry_fogs=entry_fogs
        or [{"fog_id": f"{cluster_id}_entry", "zone": cluster_id}],
        exit_fogs=exit_fogs or [{"fog_id": f"{cluster_id}_exit", "zone": cluster_id}],
        allow_entry_as_exit=allow_entry_as_exit,
    )


def make_diamond_dag() -> Dag:
    """Create a diamond DAG: start -> (A, B) -> end.

    Layers: 0=start, 1=(A,B), 2=end
    A and B each have 1 surplus exit and 1 surplus entry.
    """
    dag = Dag(seed=42)

    # Start with 2 exits
    start_c = make_cluster(
        "start_c",
        "start",
        weight=0,
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_exit1", "zone": "start_c"},
            {"fog_id": "s_exit2", "zone": "start_c"},
        ],
    )
    dag.add_node(
        DagNode(
            id="start",
            cluster=start_c,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef("s_exit1", "start_c"), FogRef("s_exit2", "start_c")],
        )
    )

    # A: 1 entry used + 1 surplus entry, 1 exit used + 1 surplus exit
    a_c = make_cluster(
        "a_c",
        entry_fogs=[
            {"fog_id": "a_entry1", "zone": "a_c"},
            {"fog_id": "a_entry2", "zone": "a_c"},
        ],
        exit_fogs=[
            {"fog_id": "a_exit1", "zone": "a_c"},
            {"fog_id": "a_exit2", "zone": "a_c"},
        ],
    )
    dag.add_node(
        DagNode(
            id="a",
            cluster=a_c,
            layer=1,
            tier=2,
            entry_fogs=[FogRef("a_entry1", "a_c")],
            # Node exit_fogs truncated to 1 (matches real generator behavior).
            # Surplus a_exit2 lives only in cluster.exit_fogs.
            exit_fogs=[FogRef("a_exit1", "a_c")],
        )
    )

    # B: same structure
    b_c = make_cluster(
        "b_c",
        entry_fogs=[
            {"fog_id": "b_entry1", "zone": "b_c"},
            {"fog_id": "b_entry2", "zone": "b_c"},
        ],
        exit_fogs=[
            {"fog_id": "b_exit1", "zone": "b_c"},
            {"fog_id": "b_exit2", "zone": "b_c"},
        ],
    )
    dag.add_node(
        DagNode(
            id="b",
            cluster=b_c,
            layer=1,
            tier=2,
            entry_fogs=[FogRef("b_entry1", "b_c")],
            # Same truncation as node A above.
            exit_fogs=[FogRef("b_exit1", "b_c")],
        )
    )

    # End: 2 entries (merge), no exits
    end_c = make_cluster(
        "end_c",
        "final_boss",
        entry_fogs=[
            {"fog_id": "end_entry1", "zone": "end_c"},
            {"fog_id": "end_entry2", "zone": "end_c"},
        ],
        exit_fogs=[],
    )
    dag.add_node(
        DagNode(
            id="end",
            cluster=end_c,
            layer=2,
            tier=3,
            entry_fogs=[FogRef("end_entry1", "end_c"), FogRef("end_entry2", "end_c")],
            exit_fogs=[],
        )
    )

    # Edges: start->A, start->B, A->end, B->end
    dag.add_edge("start", "a", FogRef("s_exit1", "start_c"), FogRef("a_entry1", "a_c"))
    dag.add_edge("start", "b", FogRef("s_exit2", "start_c"), FogRef("b_entry1", "b_c"))
    dag.add_edge("a", "end", FogRef("a_exit1", "a_c"), FogRef("end_entry1", "end_c"))
    dag.add_edge("b", "end", FogRef("b_exit1", "b_c"), FogRef("end_entry2", "end_c"))
    dag.start_id = "start"
    dag.end_id = "end"
    return dag


def _make_three_layer_dag() -> Dag:
    """start -> (A, B) -> (C1, C2) -> end, with surplus fogs on A, B, C1, C2."""
    dag = Dag(seed=42)

    start_c = make_cluster(
        "start_c",
        "start",
        weight=0,
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_exit1", "zone": "start_c"},
            {"fog_id": "s_exit2", "zone": "start_c"},
        ],
    )
    dag.add_node(
        DagNode(
            id="start",
            cluster=start_c,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef("s_exit1", "start_c"), FogRef("s_exit2", "start_c")],
        )
    )

    a_c = make_cluster(
        "a_c",
        entry_fogs=[
            {"fog_id": "a_entry1", "zone": "a_c"},
        ],
        exit_fogs=[
            {"fog_id": "a_exit1", "zone": "a_c"},
            {"fog_id": "a_exit2", "zone": "a_c"},
        ],
    )
    dag.add_node(
        DagNode(
            id="a",
            cluster=a_c,
            layer=1,
            tier=2,
            entry_fogs=[FogRef("a_entry1", "a_c")],
            # Node exit_fogs truncated to 1 (matches real generator behavior:
            # _pick_entry_and_exits_for_node trims to min_exits=1 for passant).
            # Surplus a_exit2 lives only in cluster.exit_fogs.
            exit_fogs=[FogRef("a_exit1", "a_c")],
        )
    )

    b_c = make_cluster(
        "b_c",
        entry_fogs=[
            {"fog_id": "b_entry1", "zone": "b_c"},
        ],
        exit_fogs=[
            {"fog_id": "b_exit1", "zone": "b_c"},
            {"fog_id": "b_exit2", "zone": "b_c"},
        ],
    )
    dag.add_node(
        DagNode(
            id="b",
            cluster=b_c,
            layer=1,
            tier=2,
            entry_fogs=[FogRef("b_entry1", "b_c")],
            # Same truncation as node A above.
            exit_fogs=[FogRef("b_exit1", "b_c")],
        )
    )

    c1_c = make_cluster(
        "c1_c",
        entry_fogs=[
            {"fog_id": "c1_entry1", "zone": "c1_c"},
            {"fog_id": "c1_entry2", "zone": "c1_c"},
        ],
        exit_fogs=[{"fog_id": "c1_exit1", "zone": "c1_c"}],
    )
    dag.add_node(
        DagNode(
            id="c1",
            cluster=c1_c,
            layer=2,
            tier=3,
            entry_fogs=[FogRef("c1_entry1", "c1_c")],
            exit_fogs=[FogRef("c1_exit1", "c1_c")],
        )
    )

    c2_c = make_cluster(
        "c2_c",
        entry_fogs=[
            {"fog_id": "c2_entry1", "zone": "c2_c"},
            {"fog_id": "c2_entry2", "zone": "c2_c"},
        ],
        exit_fogs=[{"fog_id": "c2_exit1", "zone": "c2_c"}],
    )
    dag.add_node(
        DagNode(
            id="c2",
            cluster=c2_c,
            layer=2,
            tier=3,
            entry_fogs=[FogRef("c2_entry1", "c2_c")],
            exit_fogs=[FogRef("c2_exit1", "c2_c")],
        )
    )

    end_c = make_cluster(
        "end_c",
        "final_boss",
        entry_fogs=[
            {"fog_id": "end_entry1", "zone": "end_c"},
            {"fog_id": "end_entry2", "zone": "end_c"},
        ],
        exit_fogs=[],
    )
    dag.add_node(
        DagNode(
            id="end",
            cluster=end_c,
            layer=3,
            tier=4,
            entry_fogs=[FogRef("end_entry1", "end_c"), FogRef("end_entry2", "end_c")],
            exit_fogs=[],
        )
    )

    dag.add_edge("start", "a", FogRef("s_exit1", "start_c"), FogRef("a_entry1", "a_c"))
    dag.add_edge("start", "b", FogRef("s_exit2", "start_c"), FogRef("b_entry1", "b_c"))
    dag.add_edge("a", "c1", FogRef("a_exit1", "a_c"), FogRef("c1_entry1", "c1_c"))
    dag.add_edge("b", "c2", FogRef("b_exit1", "b_c"), FogRef("c2_entry1", "c2_c"))
    dag.add_edge("c1", "end", FogRef("c1_exit1", "c1_c"), FogRef("end_entry1", "end_c"))
    dag.add_edge("c2", "end", FogRef("c2_exit1", "c2_c"), FogRef("end_entry2", "end_c"))
    dag.start_id = "start"
    dag.end_id = "end"
    return dag


class TestFindEligiblePairs:
    def test_diamond_has_zero_eligible_pairs(self):
        """A diamond with surplus fogs but no cross-branch adjacent-layer targets."""
        dag = make_diamond_dag()
        pairs = find_eligible_pairs(dag)
        # A (layer 1) can target B (layer 1)? No - must be N->N+1.
        # start (layer 0) has no surplus exits (both used).
        # A (layer 1) -> end (layer 2) — end has no surplus entries.
        # So in a simple diamond with no surplus on start/end: 0 pairs.
        assert len(pairs) == 0

    def test_three_layer_diamond_with_surplus(self):
        """A->C2 and B->C1 at layer 1->2, where C1 and C2 have surplus entries."""
        dag = _make_three_layer_dag()

        pairs = find_eligible_pairs(dag)
        # A (layer1, surplus exit a_exit2) -> C2 (layer2, surplus entry c2_entry2)
        # B (layer1, surplus exit b_exit2) -> C1 (layer2, surplus entry c1_entry2)
        assert len(pairs) == 2
        sources = {p[0] for p in pairs}
        targets = {p[1] for p in pairs}
        assert sources == {"a", "b"}
        assert targets == {"c1", "c2"}

    def test_entry_excluded_when_fog_id_used_as_exit_on_same_node(self):
        """Entry fog excluded when same fog_id is used as exit on the same node.

        Reproduces the real bug: node N uses fog_id X as exit (outgoing edge),
        then a cross-link tries to use fog_id X as entry into N — but FogMod's
        Pair chain already consumed the entry side.

        However, c_entry (already used by the normal A->C edge) IS available
        for cross-links: entries are arrival points, and FogMod handles
        multiple connections to the same entrance via DuplicateEntrance().
        """
        dag = Dag(seed=1)

        # Topology: start -> (A, B) -> (C, D) -> end
        # Node C uses "shared_fog" as exit to end.
        # Node C also has "shared_fog" in its cluster's entry_fogs (bidirectional).
        # Cross-link B->C should NOT pick "shared_fog" as entry into C (Pair consumed).
        # But B->C CAN use "c_entry" even though it's already used by A->C.

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            entry_fogs=[{"fog_id": "a_entry", "zone": "a"}],
            exit_fogs=[
                {"fog_id": "a_exit1", "zone": "a"},
                {"fog_id": "a_exit2", "zone": "a"},
            ],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[
                {"fog_id": "b_exit1", "zone": "b"},
                {"fog_id": "b_exit2", "zone": "b"},
            ],
        )
        # C: "shared_fog" is BOTH an exit fog (used by outgoing edge to end)
        # AND an entry fog (bidirectional boundary). The entry side is consumed
        # by Graph.Connect()'s Pair chain when the exit side is connected.
        c_c = make_cluster(
            "c",
            entry_fogs=[
                {"fog_id": "c_entry", "zone": "c"},
                {"fog_id": "shared_fog", "zone": "c"},
            ],
            exit_fogs=[
                {"fog_id": "shared_fog", "zone": "c"},
            ],
        )
        d_c = make_cluster(
            "d",
            entry_fogs=[
                {"fog_id": "d_entry", "zone": "d"},
                {"fog_id": "d_entry2", "zone": "d"},
            ],
            exit_fogs=[{"fog_id": "d_exit", "zone": "d"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode("a", a_c, 1, 2, [FogRef("a_entry", "a")], [FogRef("a_exit1", "a")])
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit1", "b")])
        )
        dag.add_node(
            DagNode(
                "c", c_c, 2, 3, [FogRef("c_entry", "c")], [FogRef("shared_fog", "c")]
            )
        )
        dag.add_node(
            DagNode("d", d_c, 2, 3, [FogRef("d_entry", "d")], [FogRef("d_exit", "d")])
        )
        dag.add_node(
            DagNode(
                "e", e_c, 3, 4, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("a_entry", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        dag.add_edge("a", "c", FogRef("a_exit1", "a"), FogRef("c_entry", "c"))
        dag.add_edge("b", "d", FogRef("b_exit1", "b"), FogRef("d_entry", "d"))
        # C uses "shared_fog" as EXIT
        dag.add_edge("c", "e", FogRef("shared_fog", "c"), FogRef("e_entry1", "e"))
        dag.add_edge("d", "e", FogRef("d_exit", "d"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        pairs = find_eligible_pairs(dag)
        # Eligible cross-links at layer 1->2: A->D, B->C
        # A->D: A has surplus a_exit2, D has d_entry2 available — eligible
        # B->C: B has surplus b_exit2, C has "shared_fog" entry in cluster
        #   "shared_fog" is used as exit on C → entry side excluded (Pair chain)
        #   BUT c_entry is available (already used by A->C, but entries are
        #   reusable via DuplicateEntrance) → B->C IS eligible
        assert len(pairs) == 2
        assert ("a", "d") in pairs
        assert ("b", "c") in pairs

    def test_exit_excluded_when_fog_id_used_as_entry_on_same_node(self):
        """Exit fog excluded when same fog_id is used as entry on the same node.

        Symmetric case: a fog_id consumed as entry (incoming edge) means the
        exit side (Pair) is also consumed by Graph.Connect().
        """
        dag = Dag(seed=1)

        # Node C uses "shared_fog" as ENTRY (incoming from A).
        # C also has "shared_fog" in exit_fogs. The exit side is Pair-consumed.
        # Cross-link C->D should NOT pick "shared_fog" as exit from C.

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            entry_fogs=[{"fog_id": "a_entry", "zone": "a"}],
            exit_fogs=[{"fog_id": "shared_fog", "zone": "a"}],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[{"fog_id": "b_exit", "zone": "b"}],
        )
        c_c = make_cluster(
            "c",
            entry_fogs=[{"fog_id": "shared_fog", "zone": "c"}],
            exit_fogs=[
                {"fog_id": "shared_fog", "zone": "c"},
                {"fog_id": "c_exit2", "zone": "c"},
            ],
        )
        d_c = make_cluster(
            "d",
            entry_fogs=[
                {"fog_id": "d_entry", "zone": "d"},
                {"fog_id": "d_entry2", "zone": "d"},
            ],
            exit_fogs=[{"fog_id": "d_exit", "zone": "d"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode(
                "a", a_c, 1, 2, [FogRef("a_entry", "a")], [FogRef("shared_fog", "a")]
            )
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit", "b")])
        )
        # C receives "shared_fog" as ENTRY from A
        dag.add_node(
            DagNode(
                "c",
                c_c,
                2,
                3,
                [FogRef("shared_fog", "c")],
                [FogRef("c_exit2", "c")],
            )
        )
        dag.add_node(
            DagNode("d", d_c, 2, 3, [FogRef("d_entry", "d")], [FogRef("d_exit", "d")])
        )
        dag.add_node(
            DagNode(
                "e", e_c, 3, 4, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("a_entry", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        # A uses "shared_fog" as exit, C receives it as ENTRY
        dag.add_edge("a", "c", FogRef("shared_fog", "a"), FogRef("shared_fog", "c"))
        dag.add_edge("b", "d", FogRef("b_exit", "b"), FogRef("d_entry", "d"))
        dag.add_edge("c", "e", FogRef("c_exit2", "c"), FogRef("e_entry1", "e"))
        dag.add_edge("d", "e", FogRef("d_exit", "d"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        pairs = find_eligible_pairs(dag)
        # C->D at layer 2->3: C has "shared_fog" in cluster exit_fogs (surplus)
        #   BUT "shared_fog" is used as entry on C → exit side excluded
        #   C has c_exit2 but it's already used → no surplus exit for C
        # No eligible pairs exist in this topology
        assert len(pairs) == 0

    def test_same_fog_id_different_zone_not_excluded(self):
        """Same fog_id on different zones creates independent Pairs — not excluded.

        FogMod's Pair is per-zone. A boundary fog between zone A and zone B
        creates independent Pairs on each side. Using fog_id X on zone A as
        exit does NOT consume fog_id X on zone B as entry.
        """
        dag = Dag(seed=1)

        # Topology: start -> (A, B) -> (C, D) -> end
        # C uses "boundary_fog" zone="c_inner" as exit (outgoing to end).
        # C also has "boundary_fog" zone="c_outer" as entry (different zone = different Pair).
        # Cross-link B->C SHOULD be eligible because (boundary_fog, c_outer) is independent.

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            entry_fogs=[{"fog_id": "a_entry", "zone": "a"}],
            exit_fogs=[
                {"fog_id": "a_exit1", "zone": "a"},
                {"fog_id": "a_exit2", "zone": "a"},
            ],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[
                {"fog_id": "b_exit1", "zone": "b"},
                {"fog_id": "b_exit2", "zone": "b"},
            ],
        )
        # C: "boundary_fog" on different zones — independent Pairs
        c_c = make_cluster(
            "c",
            entry_fogs=[
                {"fog_id": "c_entry", "zone": "c_inner"},
                {"fog_id": "boundary_fog", "zone": "c_outer"},
            ],
            exit_fogs=[
                {"fog_id": "boundary_fog", "zone": "c_inner"},
            ],
        )
        d_c = make_cluster(
            "d",
            entry_fogs=[
                {"fog_id": "d_entry", "zone": "d"},
                {"fog_id": "d_entry2", "zone": "d"},
            ],
            exit_fogs=[{"fog_id": "d_exit", "zone": "d"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode("a", a_c, 1, 2, [FogRef("a_entry", "a")], [FogRef("a_exit1", "a")])
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit1", "b")])
        )
        dag.add_node(
            DagNode(
                "c",
                c_c,
                2,
                3,
                [FogRef("c_entry", "c_inner")],
                [FogRef("boundary_fog", "c_inner")],
            )
        )
        dag.add_node(
            DagNode("d", d_c, 2, 3, [FogRef("d_entry", "d")], [FogRef("d_exit", "d")])
        )
        dag.add_node(
            DagNode(
                "e", e_c, 3, 4, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("a_entry", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        dag.add_edge("a", "c", FogRef("a_exit1", "a"), FogRef("c_entry", "c_inner"))
        dag.add_edge("b", "d", FogRef("b_exit1", "b"), FogRef("d_entry", "d"))
        # C uses "boundary_fog" zone="c_inner" as EXIT
        dag.add_edge(
            "c", "e", FogRef("boundary_fog", "c_inner"), FogRef("e_entry1", "e")
        )
        dag.add_edge("d", "e", FogRef("d_exit", "d"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        pairs = find_eligible_pairs(dag)
        # B->C: B has surplus b_exit2, C has entry ("boundary_fog", "c_outer")
        #   Exit uses ("boundary_fog", "c_inner") — different zone, independent Pair
        #   So ("boundary_fog", "c_outer") is NOT excluded → B->C eligible
        # A->D: A has surplus a_exit2, D has surplus d_entry2 → eligible
        assert len(pairs) == 2
        assert ("a", "d") in pairs
        assert ("b", "c") in pairs

    def test_entry_reusable_via_duplicate_entrance(self):
        """Entry fog already used by incoming edge is still available for cross-links.

        FogMod handles multiple connections to the same entrance via
        DuplicateEntrance(). Unlike exits (one gate = one destination),
        entries are arrival points that can receive multiple warps.
        """
        dag = Dag(seed=1)

        # Topology: start -> (A, B) -> (C, D) -> end
        # C has only 1 entry fog (c_entry), already used by A->C.
        # With entry reuse, B->C is still eligible via c_entry.

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            entry_fogs=[{"fog_id": "a_entry", "zone": "a"}],
            exit_fogs=[
                {"fog_id": "a_exit1", "zone": "a"},
                {"fog_id": "a_exit2", "zone": "a"},
            ],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[
                {"fog_id": "b_exit1", "zone": "b"},
                {"fog_id": "b_exit2", "zone": "b"},
            ],
        )
        # C: only 1 entry, already consumed by A->C edge
        c_c = make_cluster(
            "c",
            entry_fogs=[{"fog_id": "c_entry", "zone": "c"}],
            exit_fogs=[{"fog_id": "c_exit", "zone": "c"}],
        )
        d_c = make_cluster(
            "d",
            entry_fogs=[{"fog_id": "d_entry", "zone": "d"}],
            exit_fogs=[{"fog_id": "d_exit", "zone": "d"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode("a", a_c, 1, 2, [FogRef("a_entry", "a")], [FogRef("a_exit1", "a")])
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit1", "b")])
        )
        dag.add_node(
            DagNode("c", c_c, 2, 3, [FogRef("c_entry", "c")], [FogRef("c_exit", "c")])
        )
        dag.add_node(
            DagNode("d", d_c, 2, 3, [FogRef("d_entry", "d")], [FogRef("d_exit", "d")])
        )
        dag.add_node(
            DagNode(
                "e", e_c, 3, 4, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("a_entry", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        dag.add_edge("a", "c", FogRef("a_exit1", "a"), FogRef("c_entry", "c"))
        dag.add_edge("b", "d", FogRef("b_exit1", "b"), FogRef("d_entry", "d"))
        dag.add_edge("c", "e", FogRef("c_exit", "c"), FogRef("e_entry1", "e"))
        dag.add_edge("d", "e", FogRef("d_exit", "d"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        pairs = find_eligible_pairs(dag)
        # A->D: a_exit2 surplus, d_entry reusable → eligible
        # B->C: b_exit2 surplus, c_entry reusable (DuplicateEntrance) → eligible
        # Previously B->C would be blocked because c_entry was "consumed"
        assert len(pairs) == 2
        assert ("a", "d") in pairs
        assert ("b", "c") in pairs

    def test_no_pairs_when_no_surplus(self):
        """Nodes with exactly 1 entry + 1 exit have no surplus."""
        dag = Dag(seed=1)
        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "se1", "zone": "s"},
                {"fog_id": "se2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            entry_fogs=[{"fog_id": "ae", "zone": "a"}],
            exit_fogs=[{"fog_id": "ax", "zone": "a"}],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "be", "zone": "b"}],
            exit_fogs=[{"fog_id": "bx", "zone": "b"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "ee1", "zone": "e"},
                {"fog_id": "ee2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode("s", s_c, 0, 1, [], [FogRef("se1", "s"), FogRef("se2", "s")])
        )
        dag.add_node(DagNode("a", a_c, 1, 2, [FogRef("ae", "a")], [FogRef("ax", "a")]))
        dag.add_node(DagNode("b", b_c, 1, 2, [FogRef("be", "b")], [FogRef("bx", "b")]))
        dag.add_node(
            DagNode("e", e_c, 2, 3, [FogRef("ee1", "e"), FogRef("ee2", "e")], [])
        )
        dag.add_edge("s", "a", FogRef("se1", "s"), FogRef("ae", "a"))
        dag.add_edge("s", "b", FogRef("se2", "s"), FogRef("be", "b"))
        dag.add_edge("a", "e", FogRef("ax", "a"), FogRef("ee1", "e"))
        dag.add_edge("b", "e", FogRef("bx", "b"), FogRef("ee2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        pairs = find_eligible_pairs(dag)
        assert len(pairs) == 0


class TestAddCrosslinks:
    def test_adds_all_eligible(self):
        """Adds all eligible pairs (surplus is structurally rare)."""
        dag = _make_three_layer_dag()
        original_edge_count = len(dag.edges)
        added = add_crosslinks(dag, rng=random.Random(42))
        assert added == 2  # A->C2 and B->C1
        assert len(dag.edges) == original_edge_count + 2

    def test_crosslink_adds_entry_fog_to_target(self):
        """Cross-link edge adds entry_fog to target node."""
        dag = _make_three_layer_dag()
        c2_entries_before = len(dag.nodes["c2"].entry_fogs)
        add_crosslinks(dag, rng=random.Random(42))
        # C2 should have one more entry_fog (from the cross-link)
        c2_entries_after = len(dag.nodes["c2"].entry_fogs)
        assert c2_entries_after == c2_entries_before + 1

    def test_crosslink_adds_exit_fog_to_source(self):
        """Cross-link edge adds exit_fog to source node."""
        dag = _make_three_layer_dag()
        a_exits_before = len(dag.nodes["a"].exit_fogs)
        b_exits_before = len(dag.nodes["b"].exit_fogs)
        add_crosslinks(dag, rng=random.Random(42))
        # Both A and B gain one exit_fog each from cross-links
        assert len(dag.nodes["a"].exit_fogs) == a_exits_before + 1
        assert len(dag.nodes["b"].exit_fogs) == b_exits_before + 1

    def test_crosslink_consumes_surplus_fogs(self):
        """After cross-link, used fogs are no longer surplus."""
        dag = _make_three_layer_dag()
        add_crosslinks(dag, rng=random.Random(42))
        # Re-check: no more eligible pairs (all surplus consumed)
        pairs = find_eligible_pairs(dag)
        assert len(pairs) == 0

    def test_no_same_branch_crosslinks(self):
        """Cross-links only between nodes on different branches."""
        # Linear DAG: start -> A -> B -> end (single branch)
        dag = Dag(seed=1)
        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[{"fog_id": "se", "zone": "s"}],
        )
        a_c = make_cluster(
            "a",
            entry_fogs=[
                {"fog_id": "ae", "zone": "a"},
                {"fog_id": "ae2", "zone": "a"},
            ],
            exit_fogs=[
                {"fog_id": "ax", "zone": "a"},
                {"fog_id": "ax2", "zone": "a"},
            ],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[
                {"fog_id": "be", "zone": "b"},
                {"fog_id": "be2", "zone": "b"},
            ],
            exit_fogs=[{"fog_id": "bx", "zone": "b"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[{"fog_id": "ee", "zone": "e"}],
            exit_fogs=[],
        )

        dag.add_node(DagNode("s", s_c, 0, 1, [], [FogRef("se", "s")]))
        dag.add_node(
            DagNode(
                "a",
                a_c,
                1,
                2,
                [FogRef("ae", "a")],
                [FogRef("ax", "a"), FogRef("ax2", "a")],
            )
        )
        dag.add_node(DagNode("b", b_c, 2, 3, [FogRef("be", "b")], [FogRef("bx", "b")]))
        dag.add_node(DagNode("e", e_c, 3, 4, [FogRef("ee", "e")], []))
        dag.add_edge("s", "a", FogRef("se", "s"), FogRef("ae", "a"))
        dag.add_edge("a", "b", FogRef("ax", "a"), FogRef("be", "b"))
        dag.add_edge("b", "e", FogRef("bx", "b"), FogRef("ee", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        added = add_crosslinks(dag, rng=random.Random(42))
        assert added == 0  # No cross-branch pairs exist

    def test_deterministic_with_same_seed(self):
        """Same seed produces identical cross-links across multiple runs."""
        results = []
        for _ in range(5):
            dag = _make_three_layer_dag()
            add_crosslinks(dag, rng=random.Random(99))
            edges = [(e.source_id, e.target_id) for e in dag.edges]
            results.append(edges)

        for r in results[1:]:
            assert r == results[0]

    def test_crosslinks_added_tracked_on_dag(self):
        """Dag.crosslinks_added is set by add_crosslinks return value."""
        dag = _make_three_layer_dag()
        dag.crosslinks_added = add_crosslinks(dag, rng=random.Random(42))
        assert dag.crosslinks_added == 2


def make_cluster_with_proximity(
    cluster_id: str,
    entry_fogs: list[dict],
    exit_fogs: list[dict],
    proximity_groups: list[list[str]],
) -> ClusterData:
    return ClusterData(
        id=cluster_id,
        zones=[f"{cluster_id}_zone"],
        type="mini_dungeon",
        weight=5,
        entry_fogs=entry_fogs,
        exit_fogs=exit_fogs,
        proximity_groups=proximity_groups,
    )


class TestProximityFiltering:
    def test_surplus_exit_blocked_by_proximity_to_entry(self):
        """Surplus exit sharing a proximity group with consumed entry is excluded."""
        dag = Dag(seed=1)

        # Cluster with proximity_groups: entry fog_A and exit fog_B are proximate.
        # fog_C is an independent exit not in any group.
        c = make_cluster_with_proximity(
            "prox",
            entry_fogs=[
                {"fog_id": "fog_A", "zone": "prox"},
                {"fog_id": "fog_D", "zone": "prox"},
            ],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "prox"},
                {"fog_id": "fog_C", "zone": "prox"},
            ],
            proximity_groups=[["fog_A", "fog_B"]],
        )

        # Node uses fog_A as entry (incoming edge) and fog_B as potential surplus exit
        dag.add_node(DagNode("n", c, 1, 2, [FogRef("fog_A", "prox")], []))

        # Wire a dummy incoming edge so _surplus_exits sees fog_A as consumed entry
        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit", "zone": "s"},
            ],
        )
        dag.add_node(DagNode("s", s_c, 0, 1, [], [FogRef("s_exit", "s")]))
        dag.add_edge("s", "n", FogRef("s_exit", "s"), FogRef("fog_A", "prox"))
        dag.start_id = "s"
        surplus = _surplus_exits(dag, "n")
        # fog_B blocked by proximity to fog_A, fog_C is fine
        assert FogRef("fog_B", "prox") not in surplus
        assert FogRef("fog_C", "prox") in surplus

    def test_surplus_entry_blocked_by_proximity_to_exit(self):
        """Surplus entry sharing a proximity group with consumed exit is excluded."""
        dag = Dag(seed=1)

        # Cluster with proximity_groups: exit fog_X and entry fog_Y are proximate.
        # fog_Z is an independent entry not in any group.
        c = make_cluster_with_proximity(
            "prox2",
            entry_fogs=[
                {"fog_id": "fog_Y", "zone": "prox2"},
                {"fog_id": "fog_Z", "zone": "prox2"},
            ],
            exit_fogs=[
                {"fog_id": "fog_X", "zone": "prox2"},
            ],
            proximity_groups=[["fog_X", "fog_Y"]],
        )

        # Node uses fog_X as exit (outgoing edge) and fog_Y as potential surplus entry
        dag.add_node(DagNode("n", c, 1, 2, [], [FogRef("fog_X", "prox2")]))

        # Wire a dummy outgoing edge so _surplus_entries sees fog_X as consumed exit
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry", "zone": "e"},
            ],
            exit_fogs=[],
        )
        dag.add_node(DagNode("e", e_c, 2, 3, [FogRef("e_entry", "e")], []))
        dag.add_edge("n", "e", FogRef("fog_X", "prox2"), FogRef("e_entry", "e"))
        dag.start_id = "n"
        dag.end_id = "e"
        surplus = _available_entries(dag, "n")
        # fog_Y blocked by proximity to fog_X, fog_Z is fine
        assert FogRef("fog_Y", "prox2") not in surplus
        assert FogRef("fog_Z", "prox2") in surplus

    def test_no_false_blocking_across_groups(self):
        """Fogs in different proximity groups are not blocked by each other."""
        dag = Dag(seed=1)

        # Two independent groups: [fog_A, fog_B] and [fog_C, fog_D]
        # Entry uses fog_A, so fog_B is blocked but fog_D is NOT.
        c = make_cluster_with_proximity(
            "multi",
            entry_fogs=[
                {"fog_id": "fog_A", "zone": "multi"},
            ],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "multi"},
                {"fog_id": "fog_D", "zone": "multi"},
            ],
            proximity_groups=[["fog_A", "fog_B"], ["fog_C", "fog_D"]],
        )

        dag.add_node(DagNode("n", c, 1, 2, [FogRef("fog_A", "multi")], []))

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit", "zone": "s"},
            ],
        )
        dag.add_node(DagNode("s", s_c, 0, 1, [], [FogRef("s_exit", "s")]))
        dag.add_edge("s", "n", FogRef("s_exit", "s"), FogRef("fog_A", "multi"))
        dag.start_id = "s"
        surplus = _surplus_exits(dag, "n")
        assert FogRef("fog_B", "multi") not in surplus  # blocked (same group as A)
        assert FogRef("fog_D", "multi") in surplus  # not blocked (different group)

    def test_add_crosslinks_blocked_by_proximity(self):
        """add_crosslinks returns 0 when only surplus fog is proximity-blocked.

        Integration test: a cross-link pair (A->C2) exists structurally,
        but C2's only surplus entry shares a proximity group with C2's
        consumed exit, so the cross-link cannot be created.
        """
        dag = Dag(seed=1)

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        # A has a surplus exit (a_exit2) — potential cross-link source
        a_c = make_cluster(
            "a",
            entry_fogs=[{"fog_id": "a_entry", "zone": "a"}],
            exit_fogs=[
                {"fog_id": "a_exit1", "zone": "a"},
                {"fog_id": "a_exit2", "zone": "a"},
            ],
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[{"fog_id": "b_exit", "zone": "b"}],
        )
        # C2 has a surplus entry (c2_spare) but it's in a proximity group
        # with c2_exit (consumed as outgoing exit). So cross-link A->C2
        # should be blocked.
        c2_c = make_cluster_with_proximity(
            "c2",
            entry_fogs=[
                {"fog_id": "c2_entry", "zone": "c2"},
                {"fog_id": "c2_spare", "zone": "c2"},
            ],
            exit_fogs=[{"fog_id": "c2_exit", "zone": "c2"}],
            proximity_groups=[["c2_spare", "c2_exit"]],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode("a", a_c, 1, 2, [FogRef("a_entry", "a")], [FogRef("a_exit1", "a")])
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit", "b")])
        )
        dag.add_node(
            DagNode(
                "c2",
                c2_c,
                2,
                3,
                [FogRef("c2_entry", "c2")],
                [FogRef("c2_exit", "c2")],
            )
        )
        dag.add_node(
            DagNode(
                "e", e_c, 3, 4, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("a_entry", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        dag.add_edge("a", "c2", FogRef("a_exit1", "a"), FogRef("c2_entry", "c2"))
        dag.add_edge("b", "e", FogRef("b_exit", "b"), FogRef("e_entry1", "e"))
        dag.add_edge("c2", "e", FogRef("c2_exit", "c2"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        # Without proximity: A has surplus a_exit2, C2 has surplus c2_spare
        # But c2_spare is proximity-blocked by c2_exit → no valid entry on C2
        added = add_crosslinks(dag, rng=random.Random(42))
        assert added == 0

    def test_allow_entry_as_exit_skips_pair_exclusion(self):
        """With allow_entry_as_exit, entry fog does NOT consume its exit Pair.

        Boss arenas with bidirectional fogs (same fog_id in both entry_fogs
        and exit_fogs) and allow_entry_as_exit=True should have surplus
        exits available for cross-links, since the entry direction is
        reused as a forward exit rather than consuming the Pair.
        """
        dag = Dag(seed=1)

        # Topology: start -> (A, B) -> (C, D) -> end
        # A is a boss_arena with 2 bidirectional fogs and allow_entry_as_exit.
        # A enters via bidir1 and exits via bidir2 to C.
        # Without the fix: bidir1 in entry_fogrefs → excluded as exit → 0 surplus.
        # With the fix: allow_entry_as_exit skips pair exclusion → bidir1 surplus.
        # Crosslink A->D eligible (A layer 1, D layer 2, different branches).
        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            "boss_arena",
            entry_fogs=[
                {"fog_id": "bidir1", "zone": "a"},
                {"fog_id": "bidir2", "zone": "a"},
            ],
            exit_fogs=[
                {"fog_id": "bidir1", "zone": "a"},
                {"fog_id": "bidir2", "zone": "a"},
            ],
            allow_entry_as_exit=True,
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[{"fog_id": "b_exit", "zone": "b"}],
        )
        c_c = make_cluster(
            "c",
            entry_fogs=[{"fog_id": "c_entry", "zone": "c"}],
            exit_fogs=[{"fog_id": "c_exit", "zone": "c"}],
        )
        d_c = make_cluster(
            "d",
            entry_fogs=[
                {"fog_id": "d_entry1", "zone": "d"},
                {"fog_id": "d_entry2", "zone": "d"},
            ],
            exit_fogs=[{"fog_id": "d_exit", "zone": "d"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode("a", a_c, 1, 2, [FogRef("bidir1", "a")], [FogRef("bidir2", "a")])
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit", "b")])
        )
        dag.add_node(
            DagNode("c", c_c, 2, 3, [FogRef("c_entry", "c")], [FogRef("c_exit", "c")])
        )
        dag.add_node(
            DagNode("d", d_c, 2, 3, [FogRef("d_entry1", "d")], [FogRef("d_exit", "d")])
        )
        dag.add_node(
            DagNode(
                "e", e_c, 3, 4, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("bidir1", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        dag.add_edge("a", "c", FogRef("bidir2", "a"), FogRef("c_entry", "c"))
        dag.add_edge("b", "d", FogRef("b_exit", "b"), FogRef("d_entry1", "d"))
        dag.add_edge("c", "e", FogRef("c_exit", "c"), FogRef("e_entry1", "e"))
        dag.add_edge("d", "e", FogRef("d_exit", "d"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        # A has allow_entry_as_exit: bidir1 used as entry should NOT block
        # bidir1 as surplus exit
        surplus = _surplus_exits(dag, "a")
        assert FogRef("bidir1", "a") in surplus

        # A->D crosslink eligible (layer 1 → layer 2, different branches)
        pairs = find_eligible_pairs(dag)
        assert ("a", "d") in pairs

    def test_pair_exclusion_still_applies_without_entry_as_exit(self):
        """Without allow_entry_as_exit, entry fog DOES consume its exit Pair.

        Same topology as above but without allow_entry_as_exit — the
        bidirectional Pair exclusion should block the surplus exit.
        """
        dag = Dag(seed=1)

        s_c = make_cluster(
            "s",
            "start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "s_exit1", "zone": "s"},
                {"fog_id": "s_exit2", "zone": "s"},
            ],
        )
        a_c = make_cluster(
            "a",
            "boss_arena",
            entry_fogs=[
                {"fog_id": "bidir1", "zone": "a"},
                {"fog_id": "bidir2", "zone": "a"},
            ],
            exit_fogs=[
                {"fog_id": "bidir1", "zone": "a"},
                {"fog_id": "bidir2", "zone": "a"},
            ],
            allow_entry_as_exit=False,
        )
        b_c = make_cluster(
            "b",
            entry_fogs=[{"fog_id": "b_entry", "zone": "b"}],
            exit_fogs=[{"fog_id": "b_exit", "zone": "b"}],
        )
        e_c = make_cluster(
            "e",
            "final_boss",
            entry_fogs=[
                {"fog_id": "e_entry1", "zone": "e"},
                {"fog_id": "e_entry2", "zone": "e"},
            ],
            exit_fogs=[],
        )

        dag.add_node(
            DagNode(
                "s", s_c, 0, 1, [], [FogRef("s_exit1", "s"), FogRef("s_exit2", "s")]
            )
        )
        dag.add_node(
            DagNode("a", a_c, 1, 2, [FogRef("bidir1", "a")], [FogRef("bidir2", "a")])
        )
        dag.add_node(
            DagNode("b", b_c, 1, 2, [FogRef("b_entry", "b")], [FogRef("b_exit", "b")])
        )
        dag.add_node(
            DagNode(
                "e", e_c, 2, 3, [FogRef("e_entry1", "e"), FogRef("e_entry2", "e")], []
            )
        )

        dag.add_edge("s", "a", FogRef("s_exit1", "s"), FogRef("bidir1", "a"))
        dag.add_edge("s", "b", FogRef("s_exit2", "s"), FogRef("b_entry", "b"))
        dag.add_edge("a", "e", FogRef("bidir2", "a"), FogRef("e_entry1", "e"))
        dag.add_edge("b", "e", FogRef("b_exit", "b"), FogRef("e_entry2", "e"))
        dag.start_id = "s"
        dag.end_id = "e"

        # Without allow_entry_as_exit: bidir1 used as entry BLOCKS bidir1 as exit
        surplus = _surplus_exits(dag, "a")
        assert FogRef("bidir1", "a") not in surplus
