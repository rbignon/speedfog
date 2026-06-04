"""DAG validation for SpeedFog.

This module validates DAGs against configuration requirements,
distinguishing between errors (blocking) and warnings (informational).
"""

from __future__ import annotations

from dataclasses import dataclass, field

from speedfog.clusters import ClusterPool
from speedfog.config import Config, resolve_final_boss_candidates
from speedfog.dag import Dag, FogRef
from speedfog.output import EVENT_FLAG_BUDGET


@dataclass
class ValidationResult:
    """Result of DAG validation.

    Attributes:
        is_valid: True if the DAG passes all required checks (no errors).
        errors: List of blocking issues that make the DAG invalid.
        warnings: List of informational issues that don't block validation.
    """

    is_valid: bool
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


def validate_dag(
    dag: Dag, config: Config, clusters: ClusterPool | None = None
) -> ValidationResult:
    """Validate a DAG against all constraints.

    Checks:
    - Structural validity (uses dag.validate_structure())
    - Entry fog consistency (incoming edges match entry_fogs count)
    - Entry zone membership (entry_fog.zone ∈ target cluster zones)
    - Minimum requirements (bosses, legacy_dungeons, mini_dungeons)
    - Layer count (few layers = warning)

    Args:
        dag: The DAG to validate.
        config: Configuration with requirements and budget.
        clusters: Optional ClusterPool (currently unused, kept for API
            compatibility).

    Returns:
        ValidationResult with errors and warnings.
    """
    errors: list[str] = []
    warnings: list[str] = []

    # Check structural validity
    structural_errors = dag.validate_structure()
    if structural_errors:
        errors.extend(structural_errors)

    # Check entry fog consistency
    entry_fog_errors = _check_entry_fog_consistency(dag)
    if entry_fog_errors:
        errors.extend(entry_fog_errors)

    # Check no duplicate edges (same source→target pair)
    duplicate_errors = _check_no_duplicate_edges(dag)
    if duplicate_errors:
        errors.extend(duplicate_errors)

    # Check entry zone membership (entry_fog.zone ∈ target cluster zones)
    entry_zone_errors = _check_entry_zone_membership(dag)
    if entry_zone_errors:
        errors.extend(entry_zone_errors)

    # Check layer type homogeneity (no mixed types within a layer)
    homogeneity_errors = _check_layer_type_homogeneity(dag)
    if homogeneity_errors:
        errors.extend(homogeneity_errors)

    # Check layer weight spread (parallel branches must be balanced)
    spread_errors = _check_layer_weight_spread(
        dag, max_spread=config.structure.max_layer_spread
    )
    if spread_errors:
        errors.extend(spread_errors)

    # Check minimum requirements
    _check_requirements(dag, config, errors)

    # Check zone-type reachability (needs cluster data)
    if clusters is not None:
        _check_zone_types_allowed(config, clusters, errors)

    # Check layer count
    _check_layers(dag, config, warnings)

    # Check event flag budget
    _check_event_flag_budget(dag, config, errors)

    return ValidationResult(
        is_valid=len(errors) == 0,
        errors=errors,
        warnings=warnings,
    )


def _check_entry_fog_consistency(dag: Dag) -> list[str]:
    """Check that entry_fogs count matches incoming edge count.

    Multiple incoming edges may share a single entry fog, so we only require
    entry_fogs >= 1 for any non-start node.

    Args:
        dag: The DAG to check.

    Returns:
        List of error messages.
    """
    errors: list[str] = []
    for node_id, node in dag.nodes.items():
        if node_id == dag.start_id:
            # Start node has no incoming edges
            continue
        if len(node.entry_fogs) < 1:
            errors.append(f"Node {node_id}: has incoming edges but no entry_fogs")
    return errors


def _check_requirements(dag: Dag, config: Config, errors: list[str]) -> None:
    """Check that DAG meets minimum zone requirements.

    Types outside `requirements.allowed_types` are skipped: their minima
    are ignored so no "insufficient" error is emitted.

    Args:
        dag: The DAG to check.
        config: Configuration with requirements.
        errors: List to append errors to.
    """
    req = config.requirements
    type_checks = [
        ("legacy_dungeon", req.legacy_dungeons, "legacy_dungeons"),
        ("boss_arena", req.bosses, "bosses"),
        ("mini_dungeon", req.mini_dungeons, "mini_dungeons"),
    ]
    for cluster_type, required, label in type_checks:
        if cluster_type not in req.allowed_types:
            continue
        actual = dag.count_by_type(cluster_type)
        if actual < required:
            errors.append(f"Insufficient {label}: {actual} < {required}")

    # Check required zones
    if req.zones:
        all_zones: set[str] = set()
        for node in dag.nodes.values():
            all_zones.update(node.cluster.zones)
        for zone in req.zones:
            if zone not in all_zones:
                errors.append(f"Required zone missing: '{zone}'")


def _check_zone_types_allowed(
    config: Config, clusters: ClusterPool, errors: list[str]
) -> None:
    """Ensure every required zone belongs to a cluster with an allowed type.

    A zone listed in `requirements.zones` whose cluster type is excluded by
    `allowed_types` is unreachable: the DAG can never include it. Flag this
    as an early configuration error so the user sees it before a generation
    attempt fails obscurely.
    """
    allowed = set(config.requirements.allowed_types)
    for zone in config.requirements.zones:
        for cluster in clusters.clusters:
            if zone in cluster.zones:
                if cluster.type not in allowed:
                    errors.append(
                        f"Required zone '{zone}' has type '{cluster.type}' "
                        f"which is not in allowed_types={sorted(allowed)}"
                    )
                break


def _check_no_duplicate_edges(dag: Dag) -> list[str]:
    """Check that no two edges share the same (source_id, target_id) pair.

    Duplicate edges indicate a micro split-merge pattern where two branches
    from the same node merge into the same target, creating a pointless
    Y-shape topology.

    Args:
        dag: The DAG to check.

    Returns:
        List of error messages.
    """
    errors: list[str] = []
    seen: set[tuple[str, str]] = set()
    for edge in dag.edges:
        pair = (edge.source_id, edge.target_id)
        if pair in seen:
            errors.append(
                f"Duplicate edge: {edge.source_id} → {edge.target_id} "
                f"(micro split-merge pattern)"
            )
        seen.add(pair)
    return errors


def _check_layers(dag: Dag, config: Config, warnings: list[str]) -> None:
    """Check layer count against configuration.

    Args:
        dag: The DAG to check.
        config: Configuration with structure requirements.
        warnings: List to append warnings to.
    """
    if not dag.nodes:
        return

    # Find max layer in the DAG
    max_layer = max(node.layer for node in dag.nodes.values())
    # Layer count is max_layer + 1 (layers are 0-indexed)
    layer_count = max_layer + 1

    if layer_count < config.structure.layers_count:
        warnings.append(
            f"Few layers: {layer_count} < {config.structure.layers_count} target"
        )


def _check_event_flag_budget(dag: Dag, config: Config, errors: list[str]) -> None:
    """Check that the DAG won't exceed the event flag allocation budget.

    Flag allocation: 1 per edge (zone tracking) + 1 (finish event) +
    3 per non-start cluster (death markers, when enabled).

    Args:
        dag: The DAG to check.
        config: Configuration (death_markers flag).
        errors: List to append errors to.
    """
    if dag.start_id not in dag.nodes:
        return  # Structural errors will catch this

    # Upper bound: dag_to_dict may skip edges with missing nodes/zones,
    # so the actual allocation can be lower. Conservative is safe here.
    flag_count = len(dag.edges) + 1  # connections + finish_event

    if config.death_markers:
        start_cluster_id = dag.nodes[dag.start_id].cluster.id
        unique_clusters = {
            node.cluster.id
            for node in dag.nodes.values()
            if node.cluster.id != start_cluster_id
        }
        flag_count += 3 * len(unique_clusters)

    if flag_count > EVENT_FLAG_BUDGET:
        errors.append(
            f"Event flag budget exceeded: {flag_count} flags needed "
            f"(max {EVENT_FLAG_BUDGET})"
        )


def _check_layer_weight_spread(dag: Dag, max_spread: float = 2.0) -> list[str]:
    """Check that no layer's weight spread exceeds ``max_spread``.

    Parallel branches in the same layer must have comparable weights so
    that no path is disadvantaged. Generation enforces this as a hard
    window; this validator is a safety net against regressions.

    Single-node layers are skipped (spread is 0 by definition).

    Args:
        dag: The DAG to check.
        max_spread: Maximum ``max(weights) - min(weights)`` permitted.

    Returns:
        List of error messages for layers with excessive spread.
    """
    errors: list[str] = []

    layers: dict[int, list[str]] = {}
    for node_id, node in dag.nodes.items():
        layers.setdefault(node.layer, []).append(node_id)

    for layer_idx in sorted(layers):
        node_ids = layers[layer_idx]
        if len(node_ids) <= 1:
            continue

        weights = [dag.nodes[nid].cluster.weight for nid in node_ids]
        spread = max(weights) - min(weights)
        if spread > max_spread + 1e-9:
            details = ", ".join(
                f"{dag.nodes[nid].cluster.id}(w={dag.nodes[nid].cluster.weight})"
                for nid in node_ids
            )
            errors.append(
                f"Layer {layer_idx}: weight spread {spread} > {max_spread} "
                f"[{details}]"
            )

    return errors


def _check_layer_type_homogeneity(dag: Dag) -> list[str]:
    """Check that all nodes within each layer share the same cluster type.

    Mixed types in a layer create unfair asymmetry between parallel branches
    (e.g., one player faces a major boss while another just traverses a zone).

    Single-node layers (including start and end) are naturally skipped
    since there is nothing to compare.

    Args:
        dag: The DAG to check.

    Returns:
        List of error messages for layers with mixed types.
    """
    errors: list[str] = []

    # Group nodes by layer
    layers: dict[int, list[str]] = {}
    for node_id, node in dag.nodes.items():
        layers.setdefault(node.layer, []).append(node_id)

    for layer_idx in sorted(layers):
        node_ids = layers[layer_idx]
        if len(node_ids) <= 1:
            continue

        types = {dag.nodes[nid].cluster.type for nid in node_ids}
        if len(types) > 1:
            type_details = ", ".join(
                f"{dag.nodes[nid].cluster.type}({dag.nodes[nid].cluster.id})"
                for nid in node_ids
            )
            errors.append(f"Layer {layer_idx}: mixed types [{type_details}]")

    return errors


def _check_entry_zone_membership(dag: Dag) -> list[str]:
    """Check that each edge's entry_fog zone belongs to the target cluster.

    If entry_fog.zone is not in target_node.cluster.zones, the C#
    ZoneTrackingInjector's same-zone merge logic may produce false negatives:
    two connections with the same entrance_area string but targeting different
    clusters would be treated as a same-zone merge instead of a real collision.

    Args:
        dag: The DAG to check.

    Returns:
        List of error messages.
    """
    errors: list[str] = []
    for edge in dag.edges:
        target_node = dag.nodes.get(edge.target_id)
        if target_node is None:
            continue
        if not isinstance(edge.entry_fog, FogRef):
            continue  # Test helpers may pass plain strings; skip gracefully
        entry_zone = edge.entry_fog.zone
        if entry_zone and entry_zone not in target_node.cluster.zones:
            errors.append(
                f"Entry zone mismatch: edge {edge.source_id}→{edge.target_id} "
                f"has entry_fog zone '{entry_zone}' not in target cluster "
                f"'{target_node.cluster.id}' zones {target_node.cluster.zones}"
            )
    return errors


def validate_exclusions(config: Config, clusters: ClusterPool) -> list[str]:
    """Validate requirements.exclude_zones against the cluster pool.

    Must run on the UNFILTERED pool, before ClusterPool.exclude_zones mutates
    it. Returns blocking error messages (empty list if valid). Exclusion is a
    hard pool filter, so any problem here is a configuration error.

    Checks:
      1. Existence: every excluded zone matches some cluster zone (typo guard;
         a missing zone would otherwise be a silent no-op).
      2. No zone is both required (requirements.zones) and excluded.
      3. At least one final boss candidate survives exclusion. Excluded zones
         are pruned from final_boss_candidates by main (not an error), so the
         only blocking case is excluding every candidate. The 'all' keyword is
         handled the same way: it resolves against the filtered pool.
    """
    excluded = config.requirements.exclude_zones
    if not excluded:
        return []

    errors: list[str] = []
    excluded_set = set(excluded)

    # 1. Existence
    pool_zones = {z for c in clusters.clusters for z in c.zones}
    for zone in sorted(excluded_set):
        if zone not in pool_zones:
            errors.append(f"Unknown exclude_zone: '{zone}' (not in any cluster)")

    # 2. Conflict with requirements.zones
    for zone in sorted(excluded_set & set(config.requirements.zones)):
        errors.append(f"Zone '{zone}' is both required and excluded")

    # 3. At least one final boss must survive exclusion. Excluded zones are
    #    dropped from final_boss_candidates rather than erroring (see
    #    _apply_exclusions in main), so the only blocking case is removing every
    #    candidate. Survival is judged at CLUSTER granularity (a
    #    cluster is removed if ANY of its zones is excluded), matching
    #    ClusterPool.exclude_zones.
    boss_clusters = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )
    all_boss_zones = {z for c in boss_clusters for z in c.zones}
    candidate_zones = set(
        resolve_final_boss_candidates(
            config.structure.effective_final_boss_candidates, all_boss_zones
        )
    )
    if candidate_zones:
        survives = any(
            candidate_zones.intersection(c.zones)
            and not excluded_set.intersection(c.zones)
            for c in boss_clusters
        )
        if not survives:
            errors.append("exclude_zones removes every final boss candidate")

    return errors
