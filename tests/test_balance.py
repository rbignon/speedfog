"""Tests for balance analysis module."""

from speedfog.balance import PathStats, analyze_balance, report_balance
from speedfog.clusters import ClusterData
from speedfog.config import BudgetConfig
from speedfog.dag import Dag, DagNode


def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | None = None,
    exit_fogs: list[dict] | None = None,
) -> ClusterData:
    """Helper to create test ClusterData objects."""
    return ClusterData(
        id=cluster_id,
        zones=zones or [f"{cluster_id}_zone"],
        type=cluster_type,
        weight=weight,
        entry_fogs=entry_fogs
        or [{"fog_id": f"{cluster_id}_entry", "zone": cluster_id}],
        exit_fogs=exit_fogs or [{"fog_id": f"{cluster_id}_exit", "zone": cluster_id}],
    )


def make_linear_dag(weights: list[int]) -> Dag:
    """Create a linear DAG with nodes having the given weights.

    Creates nodes: n0 -> n1 -> n2 -> ... -> n(len-1)
    """
    dag = Dag(seed=42)
    for i, weight in enumerate(weights):
        node = DagNode(
            id=f"n{i}",
            cluster=make_cluster(f"c{i}", weight=weight),
            layer=i,
            tier=1,
            entry_fogs=[f"fog_{i}"] if i > 0 else [],
            exit_fogs=[],
        )
        dag.add_node(node)
        if i > 0:
            dag.add_edge(f"n{i - 1}", f"n{i}", f"fog_{i}", f"fog_{i}")
    if weights:
        dag.start_id = "n0"
        dag.end_id = f"n{len(weights) - 1}"
    return dag


def make_forked_dag(
    start_weight: int, branch_weights: list[list[int]], end_weight: int
) -> Dag:
    """Create a forked DAG with multiple parallel branches.

    Structure:
        start -> branch[0] nodes -> end
        start -> branch[1] nodes -> end
        ...

    Args:
        start_weight: Weight of start node
        branch_weights: List of weight lists, one per branch
        end_weight: Weight of end node

    Example:
        make_forked_dag(5, [[10], [15]], 5)
        Creates:
            start (5) -> a0 (10) -> end (5)
            start (5) -> b0 (15) -> end (5)
    """
    dag = Dag(seed=42)

    # Start node
    start = DagNode(
        id="start",
        cluster=make_cluster("c_start", weight=start_weight),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[],
    )
    dag.add_node(start)

    # End node - place at layer after all branches
    max_branch_len = max(len(b) for b in branch_weights) if branch_weights else 0
    end_layer = max_branch_len + 1

    end = DagNode(
        id="end",
        cluster=make_cluster("c_end", weight=end_weight),
        layer=end_layer,
        tier=10,
        entry_fogs=["fog_end"],
        exit_fogs=[],
    )
    dag.add_node(end)

    # Create branches
    branch_labels = "abcdefghij"
    for branch_idx, branch in enumerate(branch_weights):
        label = branch_labels[branch_idx]
        prev_id = "start"

        for node_idx, weight in enumerate(branch):
            node_id = f"{label}{node_idx}"
            node = DagNode(
                id=node_id,
                cluster=make_cluster(f"c_{node_id}", weight=weight),
                layer=node_idx + 1,
                tier=node_idx + 1,
                entry_fogs=[f"fog_{node_id}"],
                exit_fogs=[],
            )
            dag.add_node(node)
            dag.add_edge(prev_id, node_id, f"fog_{node_id}", f"fog_{node_id}")
            prev_id = node_id

        # Connect last node in branch to end
        dag.add_edge(prev_id, "end", f"fog_{label}_end", f"fog_{label}_end")

    dag.start_id = "start"
    dag.end_id = "end"
    return dag


# =============================================================================
# PathStats tests
# =============================================================================


class TestPathStatsFromDag:
    """Tests for PathStats.from_dag."""

    def test_from_dag_linear(self):
        """from_dag computes correct statistics for linear DAG."""
        dag = make_linear_dag([10, 15, 20])

        stats = PathStats.from_dag(dag)

        assert len(stats.paths) == 1
        assert stats.paths[0] == ["n0", "n1", "n2"]
        assert stats.weights == [45]
        assert stats.min_weight == 45
        assert stats.max_weight == 45
        assert stats.avg_weight == 45.0

    def test_from_dag_forked(self):
        """from_dag computes correct statistics for forked DAG."""
        # Two branches:
        # start(5) -> a0(10) -> end(5) = 20
        # start(5) -> b0(15) -> end(5) = 25
        dag = make_forked_dag(5, [[10], [15]], 5)

        stats = PathStats.from_dag(dag)

        assert len(stats.paths) == 2
        assert len(stats.weights) == 2
        assert sorted(stats.weights) == [20, 25]
        assert stats.min_weight == 20
        assert stats.max_weight == 25
        assert stats.avg_weight == 22.5

    def test_from_dag_empty(self):
        """from_dag handles empty DAG."""
        dag = Dag(seed=42)

        stats = PathStats.from_dag(dag)

        assert stats.paths == []
        assert stats.weights == []
        assert stats.min_weight == 0
        assert stats.max_weight == 0
        assert stats.avg_weight == 0.0

    def test_from_dag_multiple_branches(self):
        """from_dag handles DAG with multiple branches of different lengths."""
        # Three branches with different weights
        # start(2) -> a0(8) -> end(2) = 12
        # start(2) -> b0(10) -> b1(5) -> end(2) = 19
        # start(2) -> c0(3) -> end(2) = 7
        dag = make_forked_dag(2, [[8], [10, 5], [3]], 2)

        stats = PathStats.from_dag(dag)

        assert len(stats.paths) == 3
        assert len(stats.weights) == 3
        assert stats.min_weight == 7
        assert stats.max_weight == 19


# =============================================================================
# analyze_balance tests
# =============================================================================


class TestAnalyzeBalance:
    """Tests for analyze_balance function."""

    def test_balanced_paths_returns_balanced_true(self):
        """Balanced paths (within budget) returns is_balanced=True."""
        # Two equal branches: both = 25
        dag = make_forked_dag(5, [[10], [10]], 10)
        budget = BudgetConfig(total_weight=25, tolerance=5)  # 20-30

        result = analyze_balance(dag, budget)

        assert result["is_balanced"] is True
        assert result["underweight_paths"] == []
        assert result["overweight_paths"] == []

    def test_underweight_path_detected(self):
        """Underweight path detected (below budget.min_weight)."""
        # Branch a: 5 + 5 + 5 = 15 (underweight)
        # Branch b: 5 + 20 + 5 = 30 (ok)
        dag = make_forked_dag(5, [[5], [20]], 5)
        budget = BudgetConfig(total_weight=30, tolerance=5)  # 25-35

        result = analyze_balance(dag, budget)

        assert result["is_balanced"] is False
        assert len(result["underweight_paths"]) == 1
        # The underweight path is start -> a0 -> end with weight 15
        underweight = result["underweight_paths"][0]
        assert underweight["weight"] == 15

    def test_overweight_path_detected(self):
        """Overweight path detected (above budget.max_weight)."""
        # Branch a: 5 + 10 + 5 = 20 (ok)
        # Branch b: 5 + 50 + 5 = 60 (overweight)
        dag = make_forked_dag(5, [[10], [50]], 5)
        budget = BudgetConfig(total_weight=25, tolerance=10)  # 15-35

        result = analyze_balance(dag, budget)

        assert result["is_balanced"] is False
        assert len(result["overweight_paths"]) == 1
        overweight = result["overweight_paths"][0]
        assert overweight["weight"] == 60

    def test_weight_spread_calculation(self):
        """weight_spread is max - min."""
        # Two branches:
        # start(5) -> a0(10) -> end(5) = 20
        # start(5) -> b0(30) -> end(5) = 40
        dag = make_forked_dag(5, [[10], [30]], 5)
        budget = BudgetConfig(total_weight=30, tolerance=20)

        result = analyze_balance(dag, budget)

        assert result["weight_spread"] == 20  # 40 - 20

    def test_stats_included_in_result(self):
        """Result includes PathStats."""
        dag = make_linear_dag([10, 20, 30])
        budget = BudgetConfig(total_weight=60, tolerance=10)

        result = analyze_balance(dag, budget)

        assert "stats" in result
        assert isinstance(result["stats"], PathStats)
        assert result["stats"].min_weight == 60
        assert result["stats"].max_weight == 60

    def test_empty_dag(self):
        """Empty DAG returns balanced with zero spread."""
        dag = Dag(seed=42)
        budget = BudgetConfig(total_weight=30, tolerance=5)

        result = analyze_balance(dag, budget)

        assert result["is_balanced"] is True
        assert result["weight_spread"] == 0
        assert result["underweight_paths"] == []
        assert result["overweight_paths"] == []


# =============================================================================
# report_balance tests
# =============================================================================


class TestReportBalance:
    """Tests for report_balance function."""

    def test_report_contains_statistics(self):
        """Report contains key statistics."""
        dag = make_forked_dag(5, [[10], [15]], 5)
        budget = BudgetConfig(total_weight=25, tolerance=10)

        report = report_balance(dag, budget)

        assert "paths" in report.lower() or "2" in report
        assert "min" in report.lower()
        assert "max" in report.lower()

    def test_report_shows_balanced(self):
        """Report shows balanced indicator for balanced DAG."""
        dag = make_forked_dag(5, [[10], [10]], 5)
        budget = BudgetConfig(total_weight=20, tolerance=10)  # 10-30, both paths = 20

        report = report_balance(dag, budget)

        assert "balanced" in report.lower()

    def test_report_shows_imbalanced(self):
        """Report shows imbalanced indicator for unbalanced DAG."""
        # Branch a: 5 + 5 + 5 = 15 (underweight for 30 +/- 5)
        # Branch b: 5 + 20 + 5 = 30 (ok)
        dag = make_forked_dag(5, [[5], [20]], 5)
        budget = BudgetConfig(total_weight=30, tolerance=5)  # 25-35

        report = report_balance(dag, budget)

        assert "underweight" in report.lower() or "imbalanced" in report.lower()

    def test_report_shows_weight_spread(self):
        """Report includes weight spread."""
        dag = make_forked_dag(5, [[10], [30]], 5)
        budget = BudgetConfig(total_weight=30, tolerance=20)

        report = report_balance(dag, budget)

        assert "spread" in report.lower() or "20" in report

    def test_report_empty_dag(self):
        """Report handles empty DAG gracefully."""
        dag = Dag(seed=42)
        budget = BudgetConfig(total_weight=30, tolerance=5)

        report = report_balance(dag, budget)

        # Should not crash and should produce some output
        assert isinstance(report, str)
        assert len(report) > 0
