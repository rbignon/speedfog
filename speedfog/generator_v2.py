"""Exit-driven DAG generator (transient module, replaces generator.py at cutover).

Reuses shared helpers from speedfog.generator. Spec:
docs/specs/2026-04-25-exit-driven-dag-generation.md
"""

from __future__ import annotations

import random

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.dag import Dag, DagNode, FogRef
from speedfog.generation_log import FallbackEntry
from speedfog.generator import (
    _filter_exits_by_proximity,
    _mark_cluster_used,
    compute_net_exits,
    pick_cluster_uniform,
    pick_cluster_weight_matched,
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


def _free_exits(dag: Dag, node_id: str) -> list[dict]:
    """Cluster exits not yet consumed by an outgoing edge or by an entry pair."""
    node = dag.nodes[node_id]
    used_exit = {
        (e.exit_fog.fog_id, e.exit_fog.zone) for e in dag.get_outgoing_edges(node_id)
    }
    consumed_entries = [
        {"fog_id": ef.fog_id, "zone": ef.zone} for ef in node.entry_fogs
    ]
    net = compute_net_exits(node.cluster, consumed_entries)
    for entry in consumed_entries:
        net = _filter_exits_by_proximity(node.cluster, entry, net)
    return [f for f in net if (f["fog_id"], f["zone"]) not in used_exit]


def _entry_blocked_by_used_exits(
    entry: dict, cluster: ClusterData, used_exit_keys: set[tuple[str, str]]
) -> bool:
    """True if entry shares a proximity group with any used exit."""
    from speedfog.clusters import fog_matches_spec

    for group in cluster.proximity_groups:
        entry_in = any(
            fog_matches_spec(entry["fog_id"], entry["zone"], spec) for spec in group
        )
        if not entry_in:
            continue
        if any(
            fog_matches_spec(fid, z, spec)
            for fid, z in used_exit_keys
            for spec in group
        ):
            return True
    return False


def _free_entries(dag: Dag, node_id: str) -> list[dict]:
    """Cluster entries available for a new incoming edge.

    With ``allow_shared_entrance`` universal across the data, an entry can be
    reused by multiple incoming edges (DuplicateEntrance). The only exclusions
    are: bidirectional pair already consumed as an exit on this node, and
    proximity exclusion against already-used exits.
    """
    node = dag.nodes[node_id]
    used_exit_keys = {
        (e.exit_fog.fog_id, e.exit_fog.zone) for e in dag.get_outgoing_edges(node_id)
    }
    candidates: list[dict] = []
    for entry in node.cluster.entry_fogs:
        if (entry["fog_id"], entry["zone"]) in used_exit_keys:
            continue
        if _entry_blocked_by_used_exits(entry, node.cluster, used_exit_keys):
            continue
        candidates.append(entry)
    return candidates


def _exits_ordered_by_diversity(
    cluster: ClusterData,
    free_exits: list[dict],
) -> list[dict]:
    """Order free exits to maximise proximity-group diversity at the front.

    Groups exits by ``proximity_groups`` membership, sorts groups by size
    (largest first), then round-robins one from each group per pass. The
    largest-first ordering biases picks toward larger groups across
    successive calls: after a small group's only exit is consumed, the
    larger group still goes first on the next call, so picks stay
    distributed across groups instead of clustering in the small one.

    Exits with no group membership are appended as a final pseudo-group.
    """
    if not cluster.proximity_groups:
        return free_exits

    from speedfog.clusters import fog_matches_spec

    groups: list[list[dict]] = []
    seen: set[tuple[str, str]] = set()
    for group in cluster.proximity_groups:
        in_group = [
            f
            for f in free_exits
            if any(fog_matches_spec(f["fog_id"], f["zone"], s) for s in group)
            and (f["fog_id"], f["zone"]) not in seen
        ]
        if in_group:
            groups.append(in_group)
            seen.update((f["fog_id"], f["zone"]) for f in in_group)
    ungrouped = [f for f in free_exits if (f["fog_id"], f["zone"]) not in seen]
    if ungrouped:
        groups.append(ungrouped)

    # Sort groups largest-first so that across successive calls (as exits
    # get consumed), the bigger group stays at the front and surplus picks
    # are biased toward it, keeping smaller groups represented.
    groups.sort(key=len, reverse=True)

    # Round-robin: one per group, then a second pass, ...
    result: list[dict] = []
    while any(groups):
        for g in groups:
            if g:
                result.append(g.pop(0))
        groups = [g for g in groups if g]
    return result


def connect_nodes(
    dag: Dag, source: DagNode, target: DagNode, rng: random.Random
) -> bool:
    """Add an edge source -> target using one free exit/entry pair.

    Returns False if either side has no free fog gate.
    Forbids multi-edges between the same (source, target).
    """
    if any(e.source_id == source.id and e.target_id == target.id for e in dag.edges):
        return False
    src_exits = _free_exits(dag, source.id)
    tgt_entries = _free_entries(dag, target.id)
    if not src_exits or not tgt_entries:
        return False
    ordered = _exits_ordered_by_diversity(source.cluster, src_exits)
    exit_fog = ordered[0]
    entry_fog = rng.choice(tgt_entries)
    dag.add_edge(
        source.id,
        target.id,
        FogRef(exit_fog["fog_id"], exit_fog["zone"]),
        FogRef(entry_fog["fog_id"], entry_fog["zone"]),
    )
    return True


class GenerationError(Exception):
    """Error during DAG generation (v2)."""


def _pick_source_with_compatible_exit(
    dag: Dag,
    sources: list[DagNode],
    target: DagNode,
    rng: random.Random,
) -> DagNode | None:
    """Pick a source that has at least one free exit and isn't already linked
    to the target."""
    candidates = [
        s
        for s in sources
        if _free_exits(dag, s.id)
        and not any(e.source_id == s.id and e.target_id == target.id for e in dag.edges)
    ]
    if not candidates:
        return None
    return rng.choice(candidates)


def route_exits(
    dag: Dag, sources: list[DagNode], targets: list[DagNode], rng: random.Random
) -> None:
    """Distribute source exits across target slots.

    Phase 1: every target receives at least one incoming edge (no orphans).
    Phase 2: route remaining surplus exits, one edge per (source, target).
    """
    # Phase 1
    shuffled_targets = list(targets)
    rng.shuffle(shuffled_targets)
    for target in shuffled_targets:
        source = _pick_source_with_compatible_exit(dag, sources, target, rng)
        if source is None:
            raise GenerationError(f"No source can reach orphan target {target.id}")
        if not connect_nodes(dag, source, target, rng):
            raise GenerationError(
                f"Failed to connect source {source.id} to target {target.id}"
            )

    # Phase 2: saturate remaining (source, target) pairs.
    for source in sources:
        already_targeted = {e.target_id for e in dag.get_outgoing_edges(source.id)}
        available_targets = [t for t in targets if t.id not in already_targeted]
        rng.shuffle(available_targets)
        for target in available_targets:
            connect_nodes(dag, source, target, rng)


def pick_layer_clusters(
    *,
    width: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    allowed_types: tuple[str, ...] = (
        "mini_dungeon",
        "boss_arena",
        "legacy_dungeon",
        "major_boss",
    ),
    anchor_weight: int | None = None,
) -> tuple[list[ClusterData], list[FallbackEntry]]:
    """Pick `width` clusters for a layer, falling back to other allowed types.

    Returns (picks, fallbacks). Picks are weight-matched when possible.
    Each pick of the wrong type yields a FallbackEntry (reason='pool_exhausted').
    Raises GenerationError if no compatible cluster remains in any allowed type.
    """
    primary_pool = clusters.get_by_type(layer_type)
    fallback_types = [t for t in allowed_types if t != layer_type]

    picks: list[ClusterData] = []
    fallbacks: list[FallbackEntry] = []
    local_used = set(used_zones)
    for slot in range(width):
        c = pick_cluster_weight_matched(
            primary_pool,
            local_used,
            rng,
            anchor_weight=anchor_weight
            if anchor_weight is not None
            else (picks[0].weight if picks else 10),
        )
        if c is None:
            for ft in fallback_types:
                c = pick_cluster_uniform(
                    clusters.get_by_type(ft),
                    local_used,
                    rng,
                )
                if c is not None:
                    fallbacks.append(
                        FallbackEntry(
                            branch_index=slot,
                            preferred_type=layer_type,
                            actual_type=ft,
                            reason="pool_exhausted",
                            pool_remaining={},
                        )
                    )
                    break
        if c is None:
            raise GenerationError(
                f"No cluster available for layer type '{layer_type}' or any "
                f"fallback type at slot {slot}/{width}"
            )
        picks.append(c)
        _mark_cluster_used(c, local_used, clusters)
    return picks, fallbacks
