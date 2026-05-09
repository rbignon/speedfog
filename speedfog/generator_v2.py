"""Exit-driven DAG generator (transient module, replaces generator.py at cutover).

Reuses shared helpers from speedfog.generator. Spec:
docs/specs/2026-04-25-exit-driven-dag-generation.md
"""

from __future__ import annotations

import random

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.config import Config, resolve_final_boss_candidates
from speedfog.dag import Dag, DagNode, FogRef
from speedfog.generation_log import (
    FallbackEntry,
    GenerationLog,
    LayerEvent,
    NodeEntry,
    PlanEvent,
    SummaryEvent,
)
from speedfog.generator import (
    _filter_exits_by_proximity,
    _mark_cluster_used,
    compute_net_exits,
    pick_cluster_uniform,
    pick_cluster_weight_matched,
    select_weighted_final_boss,
)
from speedfog.planner import compute_tier, plan_layer_types


def count_node_net_exits(dag: Dag, node_id: str) -> int:
    """Number of exits remaining on a node, after accounting for consumed entries.

    Reuses ``compute_net_exits`` (same-side-pair semantics) and proximity-group
    exclusion. Already-used outgoing edges are also subtracted so this can be
    called mid-routing.

    For ``allow_entry_as_exit`` clusters, entries do not reduce exit capacity
    (the same gate is used from both sides), so only already-claimed outgoing
    edges are subtracted.
    """
    node = dag.nodes[node_id]
    used_exit_keys = {
        (e.exit_fog.fog_id, e.exit_fog.zone) for e in dag.get_outgoing_edges(node_id)
    }
    if node.cluster.allow_entry_as_exit:
        return sum(
            1
            for f in node.cluster.exit_fogs
            if (f["fog_id"], f["zone"]) not in used_exit_keys
        )
    consumed_entries = [
        {"fog_id": ef.fog_id, "zone": ef.zone} for ef in node.entry_fogs
    ]
    net = compute_net_exits(node.cluster, consumed_entries)
    for entry in consumed_entries:
        net = _filter_exits_by_proximity(node.cluster, entry, net)
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
    """Cluster exits not yet consumed by an outgoing edge or by an entry pair.

    For ``allow_entry_as_exit`` clusters the entry fog and exit fog share the
    same physical gate (the player enters from one side and exits from the
    other).  Consuming an entry does NOT reduce the exit capacity in that
    case, so we skip the ``compute_net_exits`` subtraction and only filter
    out exits already claimed by an outgoing edge.
    """
    node = dag.nodes[node_id]
    used_exit = {
        (e.exit_fog.fog_id, e.exit_fog.zone) for e in dag.get_outgoing_edges(node_id)
    }
    if node.cluster.allow_entry_as_exit:
        return [
            f
            for f in node.cluster.exit_fogs
            if (f["fog_id"], f["zone"]) not in used_exit
        ]
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


def _safe_entry_candidates(dag: Dag, target: DagNode) -> list[dict]:
    """Return the free entries of target that, when consumed, leave at least one exit.

    Simulates adding each free entry to the current set of consumed entries and
    returns only those where the resulting net exits (after proximity filtering
    and already-used-exit subtraction) is non-empty.

    For ``allow_entry_as_exit`` clusters entries do not reduce exit capacity
    (different sides of the same gate), so every free entry is safe.

    Note: uses dag.get_incoming_edges() (not target.entry_fogs) because
    entry_fogs is only populated after route_exits returns.
    Multiple sources may share the same entry fog (allow_shared_entrance is
    universal). compute_net_exits uses set semantics, so repeating the same
    entry fog does not compound exit consumption.
    """
    free_entries = _free_entries(dag, target.id)
    if not free_entries:
        return []
    if target.cluster.allow_entry_as_exit:
        # Entries don't consume exits for these clusters; all free entries are safe.
        return free_entries
    current_incoming = dag.get_incoming_edges(target.id)
    current_entries = [
        {"fog_id": e.entry_fog.fog_id, "zone": e.entry_fog.zone}
        for e in current_incoming
    ]
    used_exit_keys = {
        (e.exit_fog.fog_id, e.exit_fog.zone) for e in dag.get_outgoing_edges(target.id)
    }
    safe: list[dict] = []
    for candidate_entry in free_entries:
        simulated_entries = current_entries + [candidate_entry]
        net = compute_net_exits(target.cluster, simulated_entries)
        for entry in simulated_entries:
            net = _filter_exits_by_proximity(target.cluster, entry, net)
        remaining = [f for f in net if (f["fog_id"], f["zone"]) not in used_exit_keys]
        if remaining:
            safe.append(candidate_entry)
    return safe


def _target_has_free_exit_remaining(dag: Dag, target: DagNode) -> bool:
    """Return True if target will still have at least one free exit after
    receiving a new incoming edge.

    Used in Phase 1 and Phase 2 of route_exits to prevent over-consuming a
    node's exit capacity. Delegates to _safe_entry_candidates.
    """
    return bool(_safe_entry_candidates(dag, target))


def connect_nodes(
    dag: Dag, source: DagNode, target: DagNode, rng: random.Random
) -> bool:
    """Add an edge source -> target using one free exit/entry pair.

    Returns False if either side has no free fog gate.
    Forbids multi-edges between the same (source, target).

    Entry selection prefers entries that leave the target with at least one
    remaining exit (non-destructive entries), falling back to any free entry
    only when no safe choice exists.
    """
    if any(e.source_id == source.id and e.target_id == target.id for e in dag.edges):
        return False
    src_exits = _free_exits(dag, source.id)
    tgt_entries = _free_entries(dag, target.id)
    if not src_exits or not tgt_entries:
        return False
    ordered = _exits_ordered_by_diversity(source.cluster, src_exits)
    exit_fog = ordered[0]
    # Prefer entries that leave target with at least 1 exit remaining.
    safe_entries = _safe_entry_candidates(dag, target)
    # Prefer safe entries. Fall back to any free entry when none are safe
    # (e.g., terminal nodes with no exits, or when called from Phase 1 fallback
    # where we accept some dead ends to avoid orphaned targets).
    entry_pool = safe_entries if safe_entries else tgt_entries
    entry_fog = rng.choice(entry_pool)
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
    Phase 1b: every source gets at least one outgoing edge (no dead ends).
    Phase 2: route remaining surplus exits, one edge per (source, target).
    """
    # Phase 1: every target gets at least one incoming edge.
    # Prefer source-target pairings that leave the target with remaining exits
    # (so it can be a non-dead-end source in the next layer). Fall back to any
    # valid connection if no such pairing exists.
    shuffled_targets = list(targets)
    rng.shuffle(shuffled_targets)
    for target in shuffled_targets:
        # First: find a source that leaves the target with exits remaining
        candidates = [
            s
            for s in sources
            if _free_exits(dag, s.id)
            and not any(
                e.source_id == s.id and e.target_id == target.id for e in dag.edges
            )
            and _target_has_free_exit_remaining(dag, target)
        ]
        if candidates:
            source: DagNode | None = rng.choice(candidates)
        else:
            # Fall back to any compatible source (target may become a dead end,
            # but at least it won't be orphaned)
            source = _pick_source_with_compatible_exit(dag, sources, target, rng)
        if source is None:
            raise GenerationError(f"No source can reach orphan target {target.id}")
        if not connect_nodes(dag, source, target, rng):
            raise GenerationError(
                f"Failed to connect source {source.id} to target {target.id}"
            )

    # Phase 1b: every source with available exits must have at least one
    # outgoing edge (no avoidable dead ends).
    # Sources whose single fog gate was consumed as an incoming entry are
    # natural terminals (bidirectional pairing via compute_net_exits leaves
    # them with 0 free exits). Those are skipped; only sources that still
    # have exits but failed to connect to any target raise an error.
    shuffled_sources = list(sources)
    rng.shuffle(shuffled_sources)
    for source in shuffled_sources:
        if dag.get_outgoing_edges(source.id):
            continue  # already has an outgoing edge from Phase 1
        if not _free_exits(dag, source.id):
            continue  # natural terminal: all exits consumed by bidirectional pairing
        # Find a target this source can connect to.
        # Prefer targets that still have exits remaining after the new entry.
        not_yet_targeted = [
            t
            for t in targets
            if not any(
                e.source_id == source.id and e.target_id == t.id for e in dag.edges
            )
        ]
        # Prefer targets that won't become dead ends
        preferred = [
            t for t in not_yet_targeted if _target_has_free_exit_remaining(dag, t)
        ]
        candidates_1b = preferred if preferred else not_yet_targeted
        rng.shuffle(candidates_1b)
        connected = False
        for target in candidates_1b:
            if connect_nodes(dag, source, target, rng):
                connected = True
                break
        if not connected:
            raise GenerationError(
                f"Source {source.id} has no compatible exit to any target"
            )

    # Phase 2: saturate remaining (source, target) pairs, but only when the
    # target still has exits left after absorbing the new entry (so it won't
    # become a dead end on the NEXT routing step).
    for source in sources:
        already_targeted = {e.target_id for e in dag.get_outgoing_edges(source.id)}
        available_targets = [t for t in targets if t.id not in already_targeted]
        rng.shuffle(available_targets)
        for target in available_targets:
            # Guard: would this new entry leave the target with 0 exits?
            if not _target_has_free_exit_remaining(dag, target):
                continue
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


def generate_dag(config: Config, clusters: ClusterPool) -> tuple[Dag, GenerationLog]:
    """Generate a DAG using the exit-driven algorithm."""
    seed = config.seed
    rng = random.Random(seed)
    dag = Dag(seed=seed)
    log = GenerationLog()
    used_zones: set[str] = set()
    total_target = config.structure.layers_count

    # 1. Pick final boss
    boss_cluster_list = clusters.get_by_type("major_boss")
    all_boss_zones = {zone for c in boss_cluster_list for zone in c.zones}
    weighted_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    final_boss = select_weighted_final_boss(
        weighted_candidates,
        boss_cluster_list,
        used_zones,
        rng,
    )
    _mark_cluster_used(final_boss, used_zones, clusters)

    # 2. Layer 0: start cluster
    start_clusters = clusters.get_by_type("start")
    if not start_clusters:
        raise GenerationError("No start cluster available in pool")
    start = start_clusters[0]
    start_node = DagNode(
        id="node_0_a",
        cluster=start,
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef(f["fog_id"], f["zone"]) for f in start.exit_fogs],
    )
    dag.add_node(start_node)
    dag.start_id = start_node.id
    _mark_cluster_used(start, used_zones, clusters)
    current_layer_nodes = [start_node]

    # 3. Plan layer types (exclude start + boss)
    intermediate_count = total_target - 2
    pool_sizes = {
        t: len(clusters.get_by_type(t))
        for t in ("mini_dungeon", "boss_arena", "legacy_dungeon")
        if t in config.requirements.allowed_types
    }
    layer_types = plan_layer_types(
        config.requirements,
        intermediate_count,
        rng,
        pool_sizes=pool_sizes,
    )

    log.plan_event = PlanEvent(
        seed=seed,
        requirements={
            "legacy_dungeon": config.requirements.legacy_dungeons,
            "boss_arena": config.requirements.bosses,
            "mini_dungeon": config.requirements.mini_dungeons,
            "major_boss": config.requirements.major_bosses,
        },
        target_total=total_target,
        merge_reserve=0,  # legacy field, no longer used
        num_intermediate=intermediate_count,
        first_layer_type=config.structure.first_layer_type,
        planned_types=list(layer_types),
        pool_sizes=pool_sizes,
        final_boss=final_boss.id,
        reserved_zones=set(),
    )

    # 4. Main loop: saturation -> convergence
    allowed_types = tuple(config.requirements.allowed_types)
    for layer_idx in range(1, total_target - 1):
        remaining = total_target - layer_idx  # includes boss layer
        current_width = len(current_layer_nodes)
        sum_exits = sum(count_node_net_exits(dag, n.id) for n in current_layer_nodes)
        target_width = compute_target_width(
            remaining=remaining,
            current_width=current_width,
            sum_exits=sum_exits,
            max_parallel_paths=config.structure.max_parallel_paths,
        )
        if target_width <= 0:
            raise GenerationError(
                f"target_width={target_width} at layer {layer_idx} "
                f"(sum_exits={sum_exits}, current_width={current_width})"
            )

        layer_type = (
            config.structure.first_layer_type
            if layer_idx == 1 and config.structure.first_layer_type
            else layer_types[layer_idx - 1]
        )
        anchor_weight = current_layer_nodes[0].cluster.weight
        picked, fallbacks = pick_layer_clusters(
            width=target_width,
            layer_type=layer_type,
            clusters=clusters,
            used_zones=used_zones,
            rng=rng,
            allowed_types=allowed_types,
            anchor_weight=anchor_weight,
        )

        next_nodes: list[DagNode] = []
        for i, c in enumerate(picked):
            node = DagNode(
                id=f"node_{layer_idx}_{chr(97 + i)}",
                cluster=c,
                layer=layer_idx,
                tier=1,
                entry_fogs=[],
                exit_fogs=[FogRef(f["fog_id"], f["zone"]) for f in c.exit_fogs],
            )
            dag.add_node(node)
            next_nodes.append(node)
            _mark_cluster_used(c, used_zones, clusters)

        route_exits(dag, current_layer_nodes, next_nodes, rng)

        # Record entry_fogs on each next node from incoming edges
        for n in next_nodes:
            n.entry_fogs = [e.entry_fog for e in dag.get_incoming_edges(n.id)]

        phase = "saturation" if remaining > current_width else "convergence"
        log.layer_events.append(
            LayerEvent(
                layer=layer_idx,
                phase=phase,
                planned_type=layer_type,
                operation="ROUTE",
                branches_before=current_width,
                branches_after=len(next_nodes),
                nodes=[
                    NodeEntry(n.cluster.id, n.cluster.type, n.cluster.weight, "routed")
                    for n in next_nodes
                ],
                fallbacks=fallbacks,
            )
        )
        current_layer_nodes = next_nodes

    # 5. Final boss layer
    boss_node = DagNode(
        id=f"node_{total_target - 1}_a",
        cluster=final_boss,
        layer=total_target - 1,
        tier=28,
        entry_fogs=[],
        exit_fogs=[],
    )
    dag.add_node(boss_node)
    dag.end_id = boss_node.id
    route_exits(dag, current_layer_nodes, [boss_node], rng)
    boss_node.entry_fogs = [e.entry_fog for e in dag.get_incoming_edges(boss_node.id)]

    # 6. Tier assignment
    for node in dag.nodes.values():
        node.tier = compute_tier(
            node.layer,
            total_target,
            final_tier=config.structure.final_tier,
            start_tier=config.structure.start_tier,
            curve=config.structure.tier_curve,
            exponent=config.structure.tier_curve_exponent,
        )

    # 7. Build summary
    all_fallbacks = [fb for le in log.layer_events for fb in le.fallbacks]
    fallback_summary = [
        (le.layer, fb.preferred_type) for le in log.layer_events for fb in le.fallbacks
    ]
    log.summary = SummaryEvent(
        total_layers=total_target,
        total_nodes=dag.total_nodes(),
        planned_layers=intermediate_count,
        convergence_layers=sum(
            1 for le in log.layer_events if le.phase == "convergence"
        ),
        crosslinks=0,  # always 0 (saturating routing makes them inline)
        fallback_count=len(all_fallbacks),
        fallback_summary=fallback_summary,
        pool_at_end={},
    )
    return dag, log
