"""Exit-driven DAG generator (transient module, replaces generator.py at cutover).

Reuses shared helpers from speedfog.generator. Spec:
docs/specs/2026-04-25-exit-driven-dag-generation.md
"""

from __future__ import annotations

from speedfog.dag import Dag
from speedfog.generator import (
    _filter_exits_by_proximity,
    compute_net_exits,
)


def count_node_net_exits(dag: Dag, node_id: str) -> int:
    """Number of exits remaining on a node, after accounting for consumed entries.

    Reuses ``compute_net_exits`` (same-side-pair semantics) and proximity-group
    exclusion. Already-used outgoing edges are also subtracted so this can be
    called mid-routing.
    """
    node = dag.nodes[node_id]
    consumed_entries = [
        {"fog_id": ef.fog_id, "zone": ef.zone} for ef in node.entry_fogs
    ]
    net = compute_net_exits(node.cluster, consumed_entries)
    for entry in consumed_entries:
        net = _filter_exits_by_proximity(node.cluster, entry, net)
    used_exit_keys = {
        (e.exit_fog.fog_id, e.exit_fog.zone) for e in dag.get_outgoing_edges(node_id)
    }
    return sum(1 for f in net if (f["fog_id"], f["zone"]) not in used_exit_keys)


def compute_target_width(
    *,
    remaining: int,
    current_width: int,
    sum_exits: int,
    max_parallel_paths: int,
) -> int:
    """Width of the next layer.

    Saturation phase (``remaining > current_width``) caps at
    ``max_parallel_paths``. Convergence phase (``remaining <= current_width``)
    is a strict ``current_width - 1`` countdown.
    """
    if remaining > current_width:
        return min(max_parallel_paths, sum_exits)
    return current_width - 1
