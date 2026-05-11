"""DAG generation algorithm for SpeedFog (exit-driven implementation).

Generates a randomized DAG using saturating routing:
- Start: chapel_start cluster (multiple exits fan out)
- Saturation phase: width grows toward max_parallel_paths
- Convergence phase: width shrinks to 1 before final boss
- End: final_boss cluster

Spec: docs/specs/2026-04-25-exit-driven-dag-generation.md
"""

from __future__ import annotations

import random
from collections.abc import Callable
from dataclasses import dataclass, field, replace
from itertools import combinations

from speedfog.clusters import ClusterData, ClusterPool, fog_matches_spec
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
from speedfog.planner import compute_tier, plan_layer_types
from speedfog.validator import ValidationResult, validate_dag


class GenerationError(Exception):
    """Error during DAG generation."""

    pass


# Valid cluster types for first_layer_type
VALID_FIRST_LAYER_TYPES = {"legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"}


@dataclass
class GenerationResult:
    """Result of DAG generation.

    Attributes:
        dag: The generated DAG.
        seed: The actual seed used for generation.
        validation: Validation result (with any warnings).
        attempts: Number of generation attempts made.
    """

    dag: Dag
    seed: int
    validation: ValidationResult
    attempts: int
    log: GenerationLog = field(default_factory=GenerationLog)


# =============================================================================
# Shared cluster helpers (used by clusters.py, validator, and generator)
# =============================================================================


def _filter_exits_by_proximity(
    cluster: ClusterData, entry: dict, exits: list[dict]
) -> list[dict]:
    """Remove exits that share a proximity group with the entry."""
    if not cluster.proximity_groups:
        return exits

    entry_id = entry["fog_id"]
    entry_zone = entry["zone"]

    # Find all groups the entry belongs to
    blocked_specs: set[str] = set()
    for group in cluster.proximity_groups:
        entry_in_group = any(
            fog_matches_spec(entry_id, entry_zone, spec) for spec in group
        )
        if entry_in_group:
            blocked_specs.update(group)

    if not blocked_specs:
        return exits

    return [
        f
        for f in exits
        if not any(
            fog_matches_spec(f["fog_id"], f["zone"], spec) for spec in blocked_specs
        )
    ]


def compute_net_exits(cluster: ClusterData, consumed_entries: list[dict]) -> list[dict]:
    """Return exits remaining after consuming given entry fogs.

    A fog gate connecting two zones has two sides. Consuming an entry
    from zone A only removes the exit from zone A (same side), not
    the exit from zone B (opposite side of the same gate).

    Args:
        cluster: The cluster to check.
        consumed_entries: List of entry fog dicts {"fog_id", "zone"} being used.

    Returns:
        List of exit fog dicts remaining after consuming entries.
    """
    consumed_set = {(e["fog_id"], e["zone"]) for e in consumed_entries}
    return [
        f for f in cluster.exit_fogs if (f["fog_id"], f["zone"]) not in consumed_set
    ]


def count_net_exits(cluster: ClusterData, num_entries: int) -> int:
    """Minimum net exits when consuming num_entries (greedy: prefer non-bidirectional).

    This calculates the worst-case net exits by greedily selecting entries
    that cost the least (non-bidirectional entries have zero cost).
    Also accounts for proximity_groups: exits sharing a proximity group
    with any consumed entry are excluded.

    A fog is bidirectional only if the same (fog_id, zone) pair appears
    in both entry and exit lists - meaning the same side of the gate.

    Args:
        cluster: The cluster to check.
        num_entries: Number of entry fogs to consume.

    Returns:
        Minimum number of exits remaining after consuming num_entries.
    """
    if num_entries > len(cluster.entry_fogs):
        return 0

    if not cluster.proximity_groups:
        # Fast path: no proximity constraints
        exit_keys = {(f["fog_id"], f["zone"]) for f in cluster.exit_fogs}
        entry_costs: list[tuple[dict, int]] = []
        for entry in cluster.entry_fogs:
            key = (entry["fog_id"], entry["zone"])
            cost = 1 if key in exit_keys else 0
            entry_costs.append((entry, cost))
        entry_costs.sort(key=lambda x: x[1])
        consumed = [entry for entry, _ in entry_costs[:num_entries]]
        return len(compute_net_exits(cluster, consumed))

    # With proximity: worst-case across all entry combinations.
    # For each combination, compute net exits then filter by proximity
    # for each consumed entry. Return the minimum.
    min_exits = len(cluster.exit_fogs)
    for combo in combinations(cluster.entry_fogs, num_entries):
        consumed = list(combo)
        net = compute_net_exits(cluster, consumed)
        for entry in consumed:
            net = _filter_exits_by_proximity(cluster, entry, net)
        min_exits = min(min_exits, len(net))

    return min_exits


def can_be_split_node(cluster: ClusterData, num_out: int) -> bool:
    """Check if cluster can be a split node (1 entry -> num_out exits).

    Requires at least num_out exits after consuming 1 entry.
    Extra exits beyond num_out are left unmapped (no fog gate created).

    Args:
        cluster: The cluster to check.
        num_out: Minimum number of exits required after using 1 entry.

    Returns:
        True if cluster has enough exits for num_out branches.
    """
    if cluster.allow_entry_as_exit:
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= num_out
    return count_net_exits(cluster, 1) >= num_out


def can_be_merge_node(cluster: ClusterData, num_in: int) -> bool:
    """Check if cluster can be a merge node (num_in entries -> 1+ exit).

    Multiple branches connect to the same entrance fog gate (shared entrance).
    Requires 2+ entries + 1+ exit regardless of fan-in. Extra exits beyond 1
    are left unmapped.

    Args:
        cluster: The cluster to check.
        num_in: Number of entry fogs to consume.

    Returns:
        True if cluster can serve as a merge node.
    """
    return len(cluster.entry_fogs) >= 2 and len(cluster.exit_fogs) >= 1


def can_be_passant_node(cluster: ClusterData) -> bool:
    """Check if cluster can be a passant node (1 entry -> 1+ exit).

    Requires at least 1 exit after consuming 1 entry.
    Extra exits are left unmapped (no fog gate created).

    Args:
        cluster: The cluster to check.

    Returns:
        True if cluster has at least 1 exit available after using 1 entry.
    """
    if cluster.allow_entry_as_exit:
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= 1
    return count_net_exits(cluster, 1) >= 1


def select_weighted_final_boss(
    weighted_candidates: dict[str, int],
    boss_clusters: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
) -> ClusterData:
    """Select a final boss cluster using weighted random selection.

    Picks candidates proportional to their weight, retrying with
    remaining candidates if the selected zone has a conflict.

    Args:
        weighted_candidates: Zone name -> weight mapping.
        boss_clusters: Available boss clusters to match against.
        used_zones: Zones already consumed by other nodes.
        rng: Seeded random instance.

    Returns:
        The selected boss cluster.

    Raises:
        GenerationError: If no candidate is available.
    """
    remaining = dict(weighted_candidates)
    while remaining:
        zones = list(remaining.keys())
        weights = [remaining[z] for z in zones]
        [zone_name] = rng.choices(zones, weights=weights, k=1)

        for cluster in boss_clusters:
            if zone_name in cluster.zones:
                if not any(z in used_zones for z in cluster.zones):
                    return cluster
        # Zone unavailable (conflict), remove and retry with remaining candidates
        del remaining[zone_name]

    raise GenerationError(
        f"No available final boss from candidates: {list(weighted_candidates.keys())}"
    )


def validate_config(
    config: Config, clusters: ClusterPool, boss_candidates: list[ClusterData]
) -> tuple[list[str], list[str]]:
    """Validate configuration options against available clusters.

    Args:
        config: Configuration to validate.
        clusters: Available cluster pool.
        boss_candidates: Pre-filtered list of clusters eligible as final boss.

    Returns:
        Tuple of (errors, warnings). Errors are blocking; warnings are informational.
    """
    errors: list[str] = []
    warnings: list[str] = []

    # Validate first_layer_type
    if config.structure.first_layer_type:
        if config.structure.first_layer_type not in VALID_FIRST_LAYER_TYPES:
            errors.append(
                f"Invalid first_layer_type: '{config.structure.first_layer_type}'. "
                f"Valid options: {', '.join(sorted(VALID_FIRST_LAYER_TYPES))}"
            )

    # Validate major_bosses
    if config.requirements.major_bosses < 0:
        errors.append(
            f"major_bosses must be >= 0, got {config.requirements.major_bosses}"
        )

    total_requirements = (
        config.requirements.legacy_dungeons
        + config.requirements.bosses
        + config.requirements.mini_dungeons
        + config.requirements.major_bosses
    )

    budget = config.structure.layers_count - 2  # exclude start and final boss
    if total_requirements > budget:
        errors.append(
            f"sum of requirements ({total_requirements}) exceeds layers_count - 2 "
            f"({budget}). Increase layers_count or reduce per-type minimums."
        )

    # Validate final_boss_candidates
    all_boss_clusters = boss_candidates
    all_boss_zones = {zone for cluster in all_boss_clusters for zone in cluster.zones}

    # Resolve "all" keyword and validate each zone
    resolved_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )
    for zone in resolved_candidates:
        if zone not in all_boss_zones:
            errors.append(f"Unknown final_boss candidate zone: '{zone}'")
    for zone, weight in resolved_candidates.items():
        if weight < 1:
            errors.append(
                f"final_boss candidate '{zone}' has invalid weight {weight} (must be >= 1)"
            )

    # Check pool capacity against requirements with branching
    max_branches = config.structure.max_parallel_paths
    requirement_map = {
        "legacy_dungeon": config.requirements.legacy_dungeons,
        "boss_arena": config.requirements.bosses,
        "mini_dungeon": config.requirements.mini_dungeons,
        "major_boss": config.requirements.major_bosses,
    }
    for cluster_type, required in requirement_map.items():
        if required == 0:
            continue
        pool_size = len(clusters.get_by_type(cluster_type))
        max_consumption = required * max_branches
        if max_consumption > pool_size:
            warnings.append(
                f"{cluster_type}: requirement ({required}) x max_parallel_paths "
                f"({max_branches}) = {max_consumption} exceeds pool size "
                f"({pool_size}); type may be exhausted during generation"
            )

    return errors, warnings


# =============================================================================
# Cluster selection helpers
# =============================================================================


def pick_cluster_weight_matched(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    anchor_weight: int,
    filter_fn: Callable[[ClusterData], bool] = lambda c: True,
    *,
    reserved_zones: frozenset[str] = frozenset(),
    max_tolerance: int = 3,
) -> ClusterData | None:
    """Pick a cluster with weight close to anchor_weight.

    Filters candidates once (zone availability + filter_fn), then applies
    progressive weight tolerance starting from exact match.
    Falls back to any available cluster if no match within max_tolerance.

    Args:
        candidates: List of candidate clusters.
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        anchor_weight: Target weight to match.
        filter_fn: Additional filter (e.g. can_be_passant_node).
        reserved_zones: Zones reserved for prerequisite placement.
        max_tolerance: Max tolerance steps (0 = disabled, uniform random).

    Returns:
        A cluster close to anchor_weight, or None if nothing available.
    """
    available = [
        c
        for c in candidates
        if not any(z in used_zones or z in reserved_zones for z in c.zones)
        and filter_fn(c)
    ]
    if not available:
        return None

    if max_tolerance <= 0:
        return rng.choice(available)

    for tol in range(0, max_tolerance + 1):
        matched = [c for c in available if abs(c.weight - anchor_weight) <= tol]
        if matched:
            return rng.choice(matched)

    return rng.choice(available)


def _mark_cluster_used(
    cluster: ClusterData,
    used_zones: set[str],
    clusters: ClusterPool,
) -> None:
    """Mark a cluster's zones as used, including conflicting zones."""
    used_zones.update(cluster.zones)
    used_zones.update(clusters.get_conflicting_zones(cluster.zones))


def pick_cluster_uniform(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> ClusterData | None:
    """Pick a cluster uniformly at random (no capability filter).

    Only checks zone overlap and reserved zones. Capability is determined
    after selection.

    Args:
        candidates: List of candidate clusters.
        used_zones: Set of zone IDs already used.
        rng: Random number generator.
        reserved_zones: Zones reserved for prerequisite placement (excluded).

    Returns:
        A random available cluster, or None if all zones overlap.
    """
    available = [
        c
        for c in candidates
        if not any(z in used_zones or z in reserved_zones for z in c.zones)
    ]
    if not available:
        return None
    return rng.choice(available)


# =============================================================================
# Exit-driven routing primitives
# =============================================================================


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

    An entry can be reused by multiple incoming edges (DuplicateEntrance). The only exclusions
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
    Multiple sources may share the same entry fog. compute_net_exits uses set semantics, so repeating the same
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
    # Boss arenas (and other clusters with multiple gates) tag the canonical
    # entrance with `main: true`. FogMod's getMainSpawnPoint() requires that
    # gate's edge to be connected, otherwise dying in the arena and using the
    # Marika effigy can respawn the player outside the arena (softlock). Prefer
    # main-tagged entries when any are present in the pool.
    main_entries = [e for e in entry_pool if e.get("main")]
    entry_fog = rng.choice(main_entries if main_entries else entry_pool)
    dag.add_edge(
        source.id,
        target.id,
        FogRef(exit_fog["fog_id"], exit_fog["zone"]),
        FogRef(entry_fog["fog_id"], entry_fog["zone"]),
    )
    return True


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
) -> tuple[list[ClusterData], list[FallbackEntry]]:
    """Pick `width` clusters for a layer, falling back to other allowed types.

    The first slot is picked uniformly from the primary pool; it sets the
    intra-layer weight anchor. Subsequent slots are weight-matched against
    that anchor so all branches at this layer have a comparable weight.

    Returns (picks, fallbacks). Each pick of the wrong type yields a
    FallbackEntry (reason='pool_exhausted'). Raises GenerationError if no
    compatible cluster remains in any allowed type.
    """
    primary_pool = clusters.get_by_type(layer_type)
    fallback_types = [t for t in allowed_types if t != layer_type]

    picks: list[ClusterData] = []
    fallbacks: list[FallbackEntry] = []
    local_used = set(used_zones)
    for slot in range(width):
        if not picks:
            c = pick_cluster_uniform(primary_pool, local_used, rng)
        else:
            c = pick_cluster_weight_matched(
                primary_pool,
                local_used,
                rng,
                anchor_weight=picks[0].weight,
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


# =============================================================================
# Main DAG generator
# =============================================================================


def generate_dag(config: Config, clusters: ClusterPool) -> tuple[Dag, GenerationLog]:
    """Generate a DAG using the exit-driven algorithm.

    Algorithm:
    1. Pick final boss from weighted candidates
    2. Create start node from the start cluster
    3. Plan layer types (intermediate layers)
    4. Main loop: saturation phase expands width, convergence phase shrinks to 1
    5. Final boss layer: route all remaining sources to the boss
    6. Assign tiers post-hoc

    Args:
        config: Configuration with requirements and structure (config.seed used).
        clusters: Pool of available clusters.

    Returns:
        Tuple of (Generated DAG, GenerationLog with diagnostic events)

    Raises:
        GenerationError: If generation fails (not enough clusters, routing failure)
    """
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
        num_intermediate=intermediate_count,
        first_layer_type=config.structure.first_layer_type,
        planned_types=list(layer_types),
        pool_sizes=pool_sizes,
        final_boss=final_boss.id,
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
        picked, fallbacks = pick_layer_clusters(
            width=target_width,
            layer_type=layer_type,
            clusters=clusters,
            used_zones=used_zones,
            rng=rng,
            allowed_types=allowed_types,
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
        fallback_slots = {fb.branch_index for fb in fallbacks}
        anchor = picked[0].weight
        node_entries: list[NodeEntry] = []
        for i, n in enumerate(next_nodes):
            delta: int | None
            if i == 0 or i in fallback_slots:
                delta = None
            else:
                delta = abs(n.cluster.weight - anchor)
            node_entries.append(
                NodeEntry(
                    n.cluster.id,
                    n.cluster.type,
                    n.cluster.weight,
                    "routed",
                    weight_delta=delta,
                )
            )
        log.layer_events.append(
            LayerEvent(
                layer=layer_idx,
                phase=phase,
                planned_type=layer_type,
                operation="ROUTE",
                branches_before=current_width,
                branches_after=len(next_nodes),
                nodes=node_entries,
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
        fallback_count=len(all_fallbacks),
        fallback_summary=fallback_summary,
        pool_at_end={},
    )
    return dag, log


def generate_with_retry(
    config: Config,
    clusters: ClusterPool,
    max_attempts: int = 100,
    *,
    boss_candidates: list[ClusterData],
    post_validate: Callable[[Dag, int], None] | None = None,
) -> GenerationResult:
    """Generate DAG with automatic retry on failure.

    If config.seed is 0, tries random seeds until success (generation + validation).
    If config.seed is non-zero, uses that seed (fails if generation or validation fails).

    Args:
        config: Configuration
        clusters: Cluster pool
        max_attempts: Maximum retry attempts (only for seed=0)
        boss_candidates: Pre-filtered list of clusters eligible as final boss.
            Used only for validate_config; generate_dag selects from the pool directly.
        post_validate: Optional hook run after structural validation. Receives
            ``(dag, seed)``. Raising ``GenerationError`` triggers a reroll in
            auto mode, or propagates under a fixed seed. Used by callers to
            reject DAGs that survive structural checks but fail a downstream
            constraint (e.g. no feasible boss-arena matching). Its outcome is
            not reflected in the returned ``GenerationResult.validation``.

    Returns:
        GenerationResult with DAG, seed, validation, and attempt count.

    Raises:
        GenerationError: If generation fails after max_attempts
    """
    # Validate config before attempting generation
    config_errors, config_warnings = validate_config(config, clusters, boss_candidates)
    if config_errors:
        raise GenerationError(f"Invalid configuration: {'; '.join(config_errors)}")
    for warning in config_warnings:
        print(f"  Config warning: {warning}")

    if config.seed != 0:
        # Fixed seed - single attempt
        dag, log = generate_dag(config, clusters)
        validation = validate_dag(dag, config, clusters)
        if not validation.is_valid:
            errors = "; ".join(validation.errors)
            raise GenerationError(f"Validation failed: {errors}")
        if post_validate is not None:
            post_validate(dag, config.seed)
        return GenerationResult(
            dag=dag,
            seed=config.seed,
            validation=validation,
            attempts=1,
            log=log,
        )

    # Auto-reroll mode: generate with fresh seeds until one succeeds.
    # Each attempt uses a config copy with the attempt seed set.
    base_rng = random.Random()

    for attempt in range(max_attempts):
        seed = base_rng.randint(1, 999999999)
        attempt_config = replace(config, seed=seed)
        try:
            dag, log = generate_dag(attempt_config, clusters)
            validation = validate_dag(dag, attempt_config, clusters)
            if not validation.is_valid:
                errors = "; ".join(validation.errors)
                raise GenerationError(f"Validation failed: {errors}")
            if post_validate is not None:
                post_validate(dag, seed)
            return GenerationResult(
                dag=dag,
                seed=seed,
                validation=validation,
                attempts=attempt + 1,
                log=log,
            )
        except GenerationError as e:
            print(f"Attempt {attempt + 1}: seed {seed} failed - {e}")
            continue

    raise GenerationError(f"Failed to generate DAG after {max_attempts} attempts")
