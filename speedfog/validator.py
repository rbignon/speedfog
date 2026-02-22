"""DAG validation for SpeedFog.

This module validates DAGs against configuration requirements,
distinguishing between errors (blocking) and warnings (informational).
"""

from __future__ import annotations

from dataclasses import dataclass, field

from speedfog.clusters import ClusterPool
from speedfog.config import Config
from speedfog.dag import Dag, DagEdge


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
    - Minimum requirements (bosses, legacy_dungeons, mini_dungeons)
    - Zone tracking collisions (shared exit gate + same entrance map)
    - Path count (no paths = error, single path = warning)
    - Weight spread (path weight disparity = warnings)
    - Layer count (few layers = warning)

    Args:
        dag: The DAG to validate.
        config: Configuration with requirements and budget.
        clusters: Optional ClusterPool for zone→map resolution. When provided,
            enables zone tracking collision detection.

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

    # Check zone tracking collisions (shared exit gate + same entrance map)
    if clusters is not None:
        collision_errors = _check_zone_tracking_collisions(dag, clusters)
        if collision_errors:
            errors.extend(collision_errors)

    # Check minimum requirements
    _check_requirements(dag, config, errors)

    # Check paths
    _check_paths(dag, config, errors, warnings)

    # Check layer count
    _check_layers(dag, config, warnings)

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


def _check_paths(
    dag: Dag, config: Config, errors: list[str], warnings: list[str]
) -> None:
    """Check path count and weights.

    Args:
        dag: The DAG to check.
        config: Configuration with budget.
        errors: List to append errors to.
        warnings: List to append warnings to.
    """
    paths = dag.enumerate_paths()

    # No paths = error
    if len(paths) == 0:
        errors.append("No paths from start to end")
        return

    # Single path = warning
    if len(paths) == 1:
        warnings.append("Only a single path exists (no parallel branches)")

    # Check weight spread across paths
    weights = [dag.path_weight(path) for path in paths]
    spread = max(weights) - min(weights)
    if spread > config.budget.tolerance:
        warnings.append(
            f"Path weight spread {spread} exceeds tolerance {config.budget.tolerance} "
            f"(min: {min(weights)}, max: {max(weights)})"
        )


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


def _check_zone_tracking_collisions(dag: Dag, clusters: ClusterPool) -> list[str]:
    """Check for exit gate collisions that break zone tracking.

    When two edges share the same exit fog_id (same physical gate, e.g., a
    bidirectional gate between two zones of one dungeon) AND their entrance
    zones resolve to the same map, the C# ZoneTrackingInjector cannot
    disambiguate which event flag to inject. Reject the DAG early rather
    than letting the C# build fail.

    Args:
        dag: The DAG to check.
        clusters: ClusterPool for zone→map resolution.

    Returns:
        List of error messages.
    """
    errors: list[str] = []

    # Group edges by exit fog_id
    by_exit_fog: dict[str, list[DagEdge]] = {}
    for edge in dag.edges:
        by_exit_fog.setdefault(edge.exit_fog.fog_id, []).append(edge)

    for fog_id, edges in by_exit_fog.items():
        if len(edges) < 2:
            continue

        # Check if any pair of edges targets the same entrance map
        seen_entrance_maps: dict[str, str] = {}  # map_id -> edge description
        for edge in edges:
            entrance_map = clusters.get_map(edge.entry_fog.zone)
            if entrance_map is None:
                continue
            if entrance_map in seen_entrance_maps:
                errors.append(
                    f"Zone tracking collision: gate {fog_id} exits to "
                    f"{entrance_map} from both {seen_entrance_maps[entrance_map]}"
                    f" and {edge.exit_fog.zone}→{edge.entry_fog.zone}"
                    f" (node {edge.target_id})"
                )
            else:
                seen_entrance_maps[entrance_map] = (
                    f"{edge.exit_fog.zone}→{edge.entry_fog.zone}"
                    f" (node {edge.target_id})"
                )

    return errors
