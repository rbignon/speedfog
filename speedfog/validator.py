"""DAG validation for SpeedFog.

This module validates DAGs against configuration requirements,
distinguishing between errors (blocking) and warnings (informational).
"""

from __future__ import annotations

from dataclasses import dataclass, field

from speedfog.config import Config
from speedfog.dag import Dag


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


def validate_dag(dag: Dag, config: Config) -> ValidationResult:
    """Validate a DAG against all constraints.

    Checks:
    - Structural validity (uses dag.validate_structure())
    - Entry fog consistency (incoming edges match entry_fogs count)
    - Minimum requirements (bosses, legacy_dungeons, mini_dungeons)
    - Path count (no paths = error, single path = warning)
    - Weight spread (path weight disparity = warnings)
    - Layer count (few layers = warning)

    Args:
        dag: The DAG to validate.
        config: Configuration with requirements and budget.

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
        if len(incoming) != len(node.entry_fogs):
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
