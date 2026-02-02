"""Balance analysis for SpeedFog DAG paths.

This module analyzes whether all paths through the DAG have similar weights
(balanced). A balanced DAG ensures that all routes from start to end take
approximately the same time to complete.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from speedfog.config import BudgetConfig
    from speedfog.dag import Dag


@dataclass
class PathStats:
    """Statistics about paths through a DAG.

    Attributes:
        paths: List of all paths (each path is a list of node IDs)
        weights: List of weights for each path (same order as paths)
        min_weight: Minimum path weight (0 if no paths)
        max_weight: Maximum path weight (0 if no paths)
        avg_weight: Average path weight (0.0 if no paths)
    """

    paths: list[list[str]]
    weights: list[int]
    min_weight: int
    max_weight: int
    avg_weight: float

    @classmethod
    def from_dag(cls, dag: Dag) -> PathStats:
        """Compute path statistics from a DAG.

        Args:
            dag: The DAG to analyze

        Returns:
            PathStats with computed statistics
        """
        paths = dag.enumerate_paths()

        if not paths:
            return cls(
                paths=[],
                weights=[],
                min_weight=0,
                max_weight=0,
                avg_weight=0.0,
            )

        weights = [dag.path_weight(path) for path in paths]

        return cls(
            paths=paths,
            weights=weights,
            min_weight=min(weights),
            max_weight=max(weights),
            avg_weight=sum(weights) / len(weights),
        )


def analyze_balance(dag: Dag, budget: BudgetConfig) -> dict[str, Any]:
    """Analyze whether paths through the DAG are balanced.

    A DAG is balanced if all paths have weights within the budget tolerance.

    Args:
        dag: The DAG to analyze
        budget: Budget configuration with min_weight and max_weight

    Returns:
        Dictionary containing:
        - is_balanced: True if all paths are within budget
        - stats: PathStats object with detailed statistics
        - underweight_paths: List of paths below budget.min_weight
        - overweight_paths: List of paths above budget.max_weight
        - weight_spread: Difference between max and min path weights
    """
    stats = PathStats.from_dag(dag)

    underweight_paths: list[dict[str, Any]] = []
    overweight_paths: list[dict[str, Any]] = []

    for path, weight in zip(stats.paths, stats.weights, strict=True):
        if weight < budget.min_weight:
            underweight_paths.append({"path": path, "weight": weight})
        elif weight > budget.max_weight:
            overweight_paths.append({"path": path, "weight": weight})

    is_balanced = len(underweight_paths) == 0 and len(overweight_paths) == 0
    weight_spread = stats.max_weight - stats.min_weight

    return {
        "is_balanced": is_balanced,
        "stats": stats,
        "underweight_paths": underweight_paths,
        "overweight_paths": overweight_paths,
        "weight_spread": weight_spread,
    }


def report_balance(dag: Dag, budget: BudgetConfig) -> str:
    """Generate a human-readable balance report.

    Args:
        dag: The DAG to analyze
        budget: Budget configuration

    Returns:
        Multi-line string report
    """
    analysis = analyze_balance(dag, budget)
    stats: PathStats = analysis["stats"]
    lines: list[str] = []

    lines.append("=" * 50)
    lines.append("Balance Analysis Report")
    lines.append("=" * 50)
    lines.append("")

    # Overall status
    if analysis["is_balanced"]:
        lines.append("Status: BALANCED")
    else:
        lines.append("Status: IMBALANCED")
    lines.append("")

    # Statistics
    lines.append("Path Statistics:")
    lines.append(f"  Total paths: {len(stats.paths)}")
    if stats.paths:
        lines.append(f"  Min weight: {stats.min_weight}")
        lines.append(f"  Max weight: {stats.max_weight}")
        lines.append(f"  Avg weight: {stats.avg_weight:.1f}")
        lines.append(f"  Weight spread: {analysis['weight_spread']}")
    lines.append("")

    # Budget info
    lines.append("Budget Configuration:")
    lines.append(f"  Target weight: {budget.total_weight}")
    lines.append(f"  Tolerance: +/- {budget.tolerance}")
    lines.append(f"  Acceptable range: {budget.min_weight} - {budget.max_weight}")
    lines.append("")

    # Problem paths
    if analysis["underweight_paths"]:
        lines.append(f"Underweight Paths ({len(analysis['underweight_paths'])}):")
        for item in analysis["underweight_paths"]:
            path_str = " -> ".join(item["path"])
            lines.append(f"  Weight {item['weight']}: {path_str}")
        lines.append("")

    if analysis["overweight_paths"]:
        lines.append(f"Overweight Paths ({len(analysis['overweight_paths'])}):")
        for item in analysis["overweight_paths"]:
            path_str = " -> ".join(item["path"])
            lines.append(f"  Weight {item['weight']}: {path_str}")
        lines.append("")

    return "\n".join(lines)
