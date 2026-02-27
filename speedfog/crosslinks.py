"""Post-hoc cross-link generation for DAG branches.

Adds optional edges between parallel branches at adjacent layers,
giving players the choice to switch branches during a run.
"""

from __future__ import annotations

import random

from speedfog.dag import Dag, FogRef


def _get_used_exit_fogs(dag: Dag, node_id: str) -> set[FogRef]:
    """Get exit FogRefs already used by outgoing edges."""
    return {edge.exit_fog for edge in dag.get_outgoing_edges(node_id)}


def _get_used_entry_fogs(dag: Dag, node_id: str) -> set[FogRef]:
    """Get entry FogRefs already used by incoming edges."""
    return {edge.entry_fog for edge in dag.get_incoming_edges(node_id)}


def _surplus_exits(dag: Dag, node_id: str) -> list[FogRef]:
    """Get unused exit FogRefs for a node."""
    node = dag.nodes[node_id]
    used = _get_used_exit_fogs(dag, node_id)
    return [f for f in node.exit_fogs if f not in used]


def _surplus_entries(dag: Dag, node_id: str) -> list[FogRef]:
    """Get unused entry FogRefs for a node.

    Checks the cluster's full entry_fogs list against what's already
    consumed by incoming edges.
    """
    node = dag.nodes[node_id]
    used = _get_used_entry_fogs(dag, node_id)
    all_entries = [FogRef(f["fog_id"], f["zone"]) for f in node.cluster.entry_fogs]
    return [f for f in all_entries if f not in used]


def _is_reachable(dag: Dag, source_id: str, target_id: str) -> bool:
    """Check if target is reachable from source via existing edges (BFS)."""
    visited: set[str] = set()
    queue = [source_id]
    while queue:
        current = queue.pop(0)
        if current == target_id:
            return True
        if current in visited:
            continue
        visited.add(current)
        for edge in dag.get_outgoing_edges(current):
            if edge.target_id not in visited:
                queue.append(edge.target_id)
    return False


def find_eligible_pairs(dag: Dag) -> list[tuple[str, str]]:
    """Find all (source, target) pairs eligible for cross-links.

    Eligible pairs satisfy:
    - source.layer == target.layer - 1 (forward, adjacent layers)
    - source has surplus exit fogs
    - target has surplus entry fogs
    - no existing path from source to target (different branches)
    - no existing edge between them

    Returns:
        List of (source_node_id, target_node_id) pairs.
    """
    # Group nodes by layer
    by_layer: dict[int, list[str]] = {}
    for nid, node in dag.nodes.items():
        by_layer.setdefault(node.layer, []).append(nid)

    # Build existing edge set for quick lookup
    existing_edges = {(e.source_id, e.target_id) for e in dag.edges}

    pairs: list[tuple[str, str]] = []
    layers = sorted(by_layer.keys())

    for i in range(len(layers) - 1):
        layer_n = layers[i]
        layer_n1 = layers[i + 1]

        sources_with_surplus = [
            nid for nid in by_layer[layer_n] if _surplus_exits(dag, nid)
        ]
        targets_with_surplus = [
            nid for nid in by_layer[layer_n1] if _surplus_entries(dag, nid)
        ]

        for src in sources_with_surplus:
            for tgt in targets_with_surplus:
                if (src, tgt) in existing_edges:
                    continue
                if _is_reachable(dag, src, tgt):
                    continue
                pairs.append((src, tgt))

    return pairs


def add_crosslinks(
    dag: Dag,
    ratio: float,
    rng: random.Random,
) -> int:
    """Add cross-link edges to a DAG.

    Args:
        dag: The DAG to modify (in place).
        ratio: Fraction of eligible pairs to turn into cross-links (0.0-1.0).
        rng: Random number generator.

    Returns:
        Number of cross-links added.
    """
    if ratio <= 0.0:
        return 0

    pairs = find_eligible_pairs(dag)
    if not pairs:
        return 0

    nb = max(1, round(len(pairs) * ratio))
    rng.shuffle(pairs)

    added = 0
    for src_id, tgt_id in pairs:
        if added >= nb:
            break

        # Re-check surplus (may have been consumed by earlier cross-link)
        src_surplus = _surplus_exits(dag, src_id)
        tgt_surplus = _surplus_entries(dag, tgt_id)
        if not src_surplus or not tgt_surplus:
            continue

        exit_fog = rng.choice(src_surplus)
        entry_fog = rng.choice(tgt_surplus)

        dag.add_edge(src_id, tgt_id, exit_fog, entry_fog)
        # Add entry fog to target node's entry_fogs for validator consistency
        dag.nodes[tgt_id].entry_fogs.append(entry_fog)
        added += 1

    return added
