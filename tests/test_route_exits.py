"""Unit tests for routing primitives in generator_v2."""

from __future__ import annotations

from speedfog.clusters import ClusterData
from speedfog.dag import Dag, DagNode, FogRef


def _mk_cluster(
    cid: str, entries: list[tuple[str, str]], exits: list[tuple[str, str]]
) -> ClusterData:
    return ClusterData(
        id=cid,
        zones=[cid],
        type="mini_dungeon",
        weight=10,
        entry_fogs=[{"fog_id": fid, "zone": z} for fid, z in entries],
        exit_fogs=[{"fog_id": fid, "zone": z} for fid, z in exits],
    )


def _mk_node(cluster: ClusterData, layer: int, entry: FogRef | None = None) -> DagNode:
    return DagNode(
        id=f"node_{cluster.id}",
        cluster=cluster,
        layer=layer,
        tier=1,
        entry_fogs=[entry] if entry else [],
        exit_fogs=[],
    )


def test_count_node_net_exits_no_entry_returns_all_exits():
    from speedfog.generator_v2 import count_node_net_exits

    c = _mk_cluster("a", entries=[], exits=[("F1", "z1"), ("F2", "z1")])
    node = _mk_node(c, layer=0)
    dag = Dag(seed=0)
    dag.add_node(node)
    assert count_node_net_exits(dag, node.id) == 2


def test_count_node_net_exits_subtracts_consumed_entry():
    from speedfog.generator_v2 import count_node_net_exits

    # Same fog appears as entry and exit (bidirectional) -- entry consumes it.
    c = _mk_cluster("a", entries=[("F1", "z1")], exits=[("F1", "z1"), ("F2", "z1")])
    node = _mk_node(c, layer=1, entry=FogRef("F1", "z1"))
    dag = Dag(seed=0)
    dag.add_node(node)
    assert count_node_net_exits(dag, node.id) == 1


def test_compute_target_width_saturation_under_cap():
    from speedfog.generator_v2 import compute_target_width

    # remaining > current_width -> saturation, capped at max_parallel_paths
    assert (
        compute_target_width(
            remaining=20, current_width=2, sum_exits=4, max_parallel_paths=5
        )
        == 4
    )


def test_compute_target_width_saturation_at_cap():
    from speedfog.generator_v2 import compute_target_width

    assert (
        compute_target_width(
            remaining=20, current_width=3, sum_exits=12, max_parallel_paths=5
        )
        == 5
    )


def test_compute_target_width_convergence_decrements_one():
    from speedfog.generator_v2 import compute_target_width

    # remaining == current_width -> countdown
    assert (
        compute_target_width(
            remaining=4, current_width=4, sum_exits=99, max_parallel_paths=5
        )
        == 3
    )


def test_compute_target_width_convergence_terminates_at_one():
    from speedfog.generator_v2 import compute_target_width

    assert (
        compute_target_width(
            remaining=2, current_width=2, sum_exits=99, max_parallel_paths=5
        )
        == 1
    )
