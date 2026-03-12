"""Post-hoc cross-link generation for DAG branches.

Adds optional edges between parallel branches at adjacent layers,
giving players the choice to switch branches during a run.
"""

from __future__ import annotations

import random
from collections import deque

from speedfog.clusters import ClusterData, ClusterPool, fog_matches_spec
from speedfog.dag import Dag, FogRef


def _blocked_by_proximity(
    cluster_data: ClusterData,
    candidate: FogRef,
    consumed: set[FogRef],
) -> bool:
    """Check if candidate FogRef shares a proximity group with any consumed FogRef."""
    if not cluster_data.proximity_groups:
        return False

    for group in cluster_data.proximity_groups:
        candidate_in = any(
            fog_matches_spec(candidate.fog_id, candidate.zone, spec) for spec in group
        )
        if not candidate_in:
            continue
        for ref in consumed:
            if any(fog_matches_spec(ref.fog_id, ref.zone, spec) for spec in group):
                return True
    return False


def _get_used_exit_fogs(dag: Dag, node_id: str) -> set[FogRef]:
    """Get exit FogRefs already used by outgoing edges."""
    return {edge.exit_fog for edge in dag.get_outgoing_edges(node_id)}


def _get_used_entry_fogs(dag: Dag, node_id: str) -> set[FogRef]:
    """Get entry FogRefs already used by incoming edges."""
    return {edge.entry_fog for edge in dag.get_incoming_edges(node_id)}


def _surplus_exits(dag: Dag, node_id: str) -> list[FogRef]:
    """Get unused exit FogRefs for a node.

    Checks the cluster's full exit_fogs list against what's already
    consumed by outgoing edges. The node's exit_fogs may be trimmed
    during generation, so we use the cluster as the source of truth.

    Also excludes exits whose (fog_id, zone) is already consumed as an
    entry on this node. Bidirectional fog gates have both an exit and
    entry side linked via Pair in FogMod's Graph; when Graph.Connect()
    uses one side, it also marks the Pair as consumed. The Pair is
    per-zone: the same fog_id on different zones creates independent
    Pairs, so we match on (fog_id, zone), not bare fog_id.
    """
    node = dag.nodes[node_id]
    used = _get_used_exit_fogs(dag, node_id)
    # FogRefs consumed as entry on this node — their exit Pair is also consumed
    entry_fogrefs = _get_used_entry_fogs(dag, node_id)
    all_exits = [FogRef(f["fog_id"], f["zone"]) for f in node.cluster.exit_fogs]
    return [
        f
        for f in all_exits
        if f not in used
        and f not in entry_fogrefs
        and not _blocked_by_proximity(node.cluster, f, entry_fogrefs)
    ]


def _available_entries(dag: Dag, node_id: str) -> list[FogRef]:
    """Get entry FogRefs available for cross-links on a node.

    Unlike exits (which are physical gates — one gate = one destination),
    entries are arrival points: multiple exits can all warp to the same
    entrance. FogMod handles this via DuplicateEntrance(), so entry fogs
    already used by incoming edges are still available for cross-links.

    The only exclusions are:
    - Bidirectional Pair: if the entry fog is already consumed as an exit
      on this node, its Pair is taken (see _surplus_exits rationale).
    - Proximity groups: same spatial exclusion as exits.
    """
    node = dag.nodes[node_id]
    # FogRefs consumed as exit on this node — their entry Pair is also consumed
    exit_fogrefs = _get_used_exit_fogs(dag, node_id)
    all_entries = [FogRef(f["fog_id"], f["zone"]) for f in node.cluster.entry_fogs]
    return [
        f
        for f in all_entries
        if f not in exit_fogrefs
        and not _blocked_by_proximity(node.cluster, f, exit_fogrefs)
    ]


def _is_reachable(dag: Dag, source_id: str, target_id: str) -> bool:
    """Check if target is reachable from source via existing edges (BFS)."""
    visited: set[str] = set()
    queue: deque[str] = deque([source_id])
    while queue:
        current = queue.popleft()
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
    - source.layer == target.layer - 1 (forward, adjacent layers only —
      skipping layers would let players bypass content, which is
      unacceptable in racing)
    - source has surplus exit fogs
    - target has available entry fogs (entries are reusable via
      DuplicateEntrance — only Pair chain and proximity exclude)
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
        targets_with_entries = [
            nid for nid in by_layer[layer_n1] if _available_entries(dag, nid)
        ]

        for src in sources_with_surplus:
            for tgt in targets_with_entries:
                if (src, tgt) in existing_edges:
                    continue
                if _is_reachable(dag, src, tgt):
                    continue
                pairs.append((src, tgt))

    return pairs


def _build_collision_index(
    dag: Dag,
    clusters: ClusterPool,
) -> dict[str, set[str]]:
    """Build an index of exit_fog_id → set of entrance maps.

    Matches the Python validator's conservative collision check: a collision
    occurs when two edges share the same exit fog_id (gate model) and their
    entrance zones resolve to the same map. This is intentionally strict —
    a false rejection costs a retry (~ms), while a false acceptance costs
    a full C# build failure (~minutes).
    """
    index: dict[str, set[str]] = {}
    for edge in dag.edges:
        entry_map = clusters.get_map(edge.entry_fog.zone)
        if entry_map is not None:
            index.setdefault(edge.exit_fog.fog_id, set()).add(entry_map)
    return index


def _build_compound_index(
    dag: Dag,
    clusters: ClusterPool,
) -> dict[tuple[str, str], int]:
    """Build an index of (source_node_id, entrance_map) → edge count.

    The C# ZoneTrackingInjector matches warp events using (source_map, dest_map)
    compound keys. When two edges from the same source node target zones on the
    same entrance map AND both use AEG099 gates (FogMod-allocated entities that
    can't be entity-matched), the C# can't disambiguate which flag to inject.

    This index tracks these compound keys so cross-links can avoid creating
    unresolvable collisions.
    """
    index: dict[tuple[str, str], int] = {}
    for edge in dag.edges:
        entry_map = clusters.get_map(edge.entry_fog.zone)
        if entry_map is not None:
            key = (edge.source_id, entry_map)
            index[key] = index.get(key, 0) + 1
    return index


def _would_collide(
    exit_fog: FogRef,
    entry_fog: FogRef,
    collision_index: dict[str, set[str]],
    clusters: ClusterPool,
) -> bool:
    """Check if adding an edge with this exit/entry would cause a fog_id collision."""
    entry_map = clusters.get_map(entry_fog.zone)
    if entry_map is None:
        return False
    existing_maps = collision_index.get(exit_fog.fog_id)
    if existing_maps is None:
        return False
    return entry_map in existing_maps


def _would_compound_collide(
    src_id: str,
    entry_fog: FogRef,
    compound_index: dict[tuple[str, str], int],
    clusters: ClusterPool,
) -> bool:
    """Check if adding an edge would create a compound key collision.

    Returns True if the source node already has an edge to a zone on the
    same entrance map. This prevents the C# ZoneTrackingInjector from
    failing to disambiguate two edges with the same (source_map, dest_map).
    """
    entry_map = clusters.get_map(entry_fog.zone)
    if entry_map is None:
        return False
    return compound_index.get((src_id, entry_map), 0) > 0


def add_crosslinks(
    dag: Dag,
    rng: random.Random,
    clusters: ClusterPool | None = None,
) -> int:
    """Add cross-link edges to a DAG.

    Tries every eligible pair. When clusters is provided, skips
    combinations that would create zone tracking collisions (same
    exit fog_id targeting the same destination map from multiple edges).

    Args:
        dag: The DAG to modify (in place).
        rng: Random number generator.
        clusters: If provided, enables collision-aware filtering.

    Returns:
        Number of cross-links added.
    """
    pairs = find_eligible_pairs(dag)
    if not pairs:
        return 0

    rng.shuffle(pairs)

    collision_index = _build_collision_index(dag, clusters) if clusters else None
    compound_index = _build_compound_index(dag, clusters) if clusters else None

    added = 0
    for src_id, tgt_id in pairs:
        # Re-check surplus (may have been consumed by earlier cross-link)
        src_surplus = _surplus_exits(dag, src_id)
        tgt_surplus = _available_entries(dag, tgt_id)
        if not src_surplus or not tgt_surplus:
            continue

        # Try all exit/entry combinations, pick a non-colliding one
        if collision_index is not None:
            assert clusters is not None
            assert compound_index is not None
            rng.shuffle(src_surplus)
            rng.shuffle(tgt_surplus)
            found = False
            for exit_fog in src_surplus:
                for entry_fog in tgt_surplus:
                    if not _would_collide(
                        exit_fog, entry_fog, collision_index, clusters
                    ) and not _would_compound_collide(
                        src_id, entry_fog, compound_index, clusters
                    ):
                        _add_crosslink(
                            dag,
                            src_id,
                            tgt_id,
                            exit_fog,
                            entry_fog,
                            collision_index,
                            compound_index,
                            clusters,
                        )
                        added += 1
                        found = True
                        break
                if found:
                    break
        else:
            exit_fog = rng.choice(src_surplus)
            entry_fog = rng.choice(tgt_surplus)
            dag.add_edge(src_id, tgt_id, exit_fog, entry_fog)
            dag.nodes[src_id].exit_fogs.append(exit_fog)
            dag.nodes[tgt_id].entry_fogs.append(entry_fog)
            added += 1

    return added


def _add_crosslink(
    dag: Dag,
    src_id: str,
    tgt_id: str,
    exit_fog: FogRef,
    entry_fog: FogRef,
    collision_index: dict[str, set[str]],
    compound_index: dict[tuple[str, str], int],
    clusters: ClusterPool,
) -> None:
    """Add a cross-link edge and update the collision indexes."""
    dag.add_edge(src_id, tgt_id, exit_fog, entry_fog)
    dag.nodes[src_id].exit_fogs.append(exit_fog)
    dag.nodes[tgt_id].entry_fogs.append(entry_fog)
    # Update collision indexes with the new edge
    entry_map = clusters.get_map(entry_fog.zone)
    if entry_map is not None:
        collision_index.setdefault(exit_fog.fog_id, set()).add(entry_map)
        key = (src_id, entry_map)
        compound_index[key] = compound_index.get(key, 0) + 1
