"""DAG validation for SpeedFog.

This module validates DAGs against configuration requirements,
distinguishing between errors (blocking) and warnings (informational).
"""

from __future__ import annotations

from dataclasses import dataclass, field

from speedfog.clusters import ClusterPool
from speedfog.config import Config
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

    # Check minimum requirements
    _check_requirements(dag, config, errors)

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

    For shared entrance nodes (allow_shared_entrance=True), multiple incoming
    edges share a single entry fog, so we only require entry_fogs >= 1.

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
        incoming = dag.get_incoming_edges(node_id)
        if node.cluster.allow_shared_entrance:
            # Shared entrance: multiple branches connect to the same entry fog
            if len(node.entry_fogs) < 1:
                errors.append(f"Node {node_id}: shared entrance but no entry_fogs")
        elif len(incoming) != len(node.entry_fogs):
            errors.append(
                f"Node {node_id}: {len(incoming)} incoming edges "
                f"but {len(node.entry_fogs)} entry_fogs"
            )
    return errors


def _check_requirements(dag: Dag, config: Config, errors: list[str]) -> None:
    """Check that DAG meets minimum zone requirements.

    Args:
        dag: The DAG to check.
        config: Configuration with requirements.
        errors: List to append errors to.
    """
    req = config.requirements

    # Check legacy dungeons
    legacy_count = dag.count_by_type("legacy_dungeon")
    if legacy_count < req.legacy_dungeons:
        errors.append(
            f"Insufficient legacy_dungeons: {legacy_count} < {req.legacy_dungeons}"
        )

    # Check bosses (boss_arena type)
    boss_count = dag.count_by_type("boss_arena")
    if boss_count < req.bosses:
        errors.append(f"Insufficient bosses: {boss_count} < {req.bosses}")

    # Check mini_dungeons
    mini_count = dag.count_by_type("mini_dungeon")
    if mini_count < req.mini_dungeons:
        errors.append(f"Insufficient mini_dungeons: {mini_count} < {req.mini_dungeons}")

    # Check required zones
    if req.zones:
        all_zones: set[str] = set()
        for node in dag.nodes.values():
            all_zones.update(node.cluster.zones)
        for zone in req.zones:
            if zone not in all_zones:
                errors.append(f"Required zone missing: '{zone}'")


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

    if layer_count < config.structure.min_layers:
        warnings.append(
            f"Few layers: {layer_count} < {config.structure.min_layers} minimum"
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
