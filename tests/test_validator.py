"""Tests for DAG validator."""

from speedfog.clusters import ClusterData
from speedfog.config import Config
from speedfog.dag import Dag, DagNode
from speedfog.validator import ValidationResult, validate_dag


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


def make_simple_dag(seed: int = 42) -> Dag:
    """Create a simple valid DAG with start -> end structure."""
    dag = Dag(seed=seed)
    dag.add_node(
        DagNode(
            id="start",
            cluster=make_cluster("start_cluster", cluster_type="start", weight=0),
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=["fog_1"],
        )
    )
    dag.add_node(
        DagNode(
            id="end",
            cluster=make_cluster("end_cluster", cluster_type="final_boss", weight=5),
            layer=1,
            tier=1,
            entry_fogs=["fog_1"],
            exit_fogs=[],
        )
    )
    dag.add_edge("start", "end", "fog_1", "fog_1")
    dag.start_id = "start"
    dag.end_id = "end"
    return dag


def make_dag_with_content(
    legacy_count: int = 1,
    mini_dungeon_count: int = 5,
    boss_count: int = 5,
    path_weight: int = 30,
    layer_count: int = 6,
) -> Dag:
    """Create a DAG with specified content requirements.

    Creates a linear DAG with layers to meet the specified requirements.
    """
    dag = Dag(seed=42)
    current_layer = 0

    # Add start node
    dag.add_node(
        DagNode(
            id="start",
            cluster=make_cluster("start_cluster", cluster_type="start", weight=0),
            layer=current_layer,
            tier=1,
            entry_fogs=[],
            exit_fogs=["fog_start_0"],
        )
    )
    prev_id = "start"
    edge_idx = 0

    # Calculate weight per node (excluding start and end which have fixed weights)
    num_content_nodes = legacy_count + mini_dungeon_count + boss_count
    if num_content_nodes > 0:
        weight_per_node = max(1, path_weight // num_content_nodes)
    else:
        weight_per_node = 5

    # Add legacy dungeons
    for i in range(legacy_count):
        current_layer += 1
        node_id = f"legacy_{i}"
        dag.add_node(
            DagNode(
                id=node_id,
                cluster=make_cluster(
                    f"legacy_cluster_{i}",
                    cluster_type="legacy_dungeon",
                    weight=weight_per_node,
                ),
                layer=current_layer,
                tier=1,
                entry_fogs=[f"fog_{edge_idx}"],
                exit_fogs=[f"fog_{edge_idx + 1}"],
            )
        )
        dag.add_edge(prev_id, node_id, f"fog_{edge_idx}", f"fog_{edge_idx}")
        prev_id = node_id
        edge_idx += 1

    # Add mini_dungeons
    for i in range(mini_dungeon_count):
        current_layer += 1
        node_id = f"mini_{i}"
        dag.add_node(
            DagNode(
                id=node_id,
                cluster=make_cluster(
                    f"mini_cluster_{i}",
                    cluster_type="mini_dungeon",
                    weight=weight_per_node,
                ),
                layer=current_layer,
                tier=1,
                entry_fogs=[f"fog_{edge_idx}"],
                exit_fogs=[f"fog_{edge_idx + 1}"],
            )
        )
        dag.add_edge(prev_id, node_id, f"fog_{edge_idx}", f"fog_{edge_idx}")
        prev_id = node_id
        edge_idx += 1

    # Add bosses
    for i in range(boss_count):
        current_layer += 1
        node_id = f"boss_{i}"
        dag.add_node(
            DagNode(
                id=node_id,
                cluster=make_cluster(
                    f"boss_cluster_{i}",
                    cluster_type="boss_arena",
                    weight=weight_per_node,
                ),
                layer=current_layer,
                tier=1,
                entry_fogs=[f"fog_{edge_idx}"],
                exit_fogs=[f"fog_{edge_idx + 1}"],
            )
        )
        dag.add_edge(prev_id, node_id, f"fog_{edge_idx}", f"fog_{edge_idx}")
        prev_id = node_id
        edge_idx += 1

    # Pad layers if needed
    while current_layer < layer_count - 1:
        current_layer += 1
        node_id = f"filler_{current_layer}"
        dag.add_node(
            DagNode(
                id=node_id,
                cluster=make_cluster(
                    f"filler_cluster_{current_layer}",
                    cluster_type="mini_dungeon",
                    weight=1,
                ),
                layer=current_layer,
                tier=1,
                entry_fogs=[f"fog_{edge_idx}"],
                exit_fogs=[f"fog_{edge_idx + 1}"],
            )
        )
        dag.add_edge(prev_id, node_id, f"fog_{edge_idx}", f"fog_{edge_idx}")
        prev_id = node_id
        edge_idx += 1

    # Add end node
    current_layer += 1
    dag.add_node(
        DagNode(
            id="end",
            cluster=make_cluster("end_cluster", cluster_type="final_boss", weight=5),
            layer=current_layer,
            tier=1,
            entry_fogs=[f"fog_{edge_idx}"],
            exit_fogs=[],
        )
    )
    dag.add_edge(prev_id, "end", f"fog_{edge_idx}", f"fog_{edge_idx}")
    dag.start_id = "start"
    dag.end_id = "end"

    return dag


def make_config(
    legacy_dungeons: int = 1,
    bosses: int = 5,
    mini_dungeons: int = 5,
    tolerance: int = 5,
    min_layers: int = 6,
    max_layers: int = 10,
) -> Config:
    """Create a Config with specified requirements."""
    return Config.from_dict(
        {
            "budget": {
                "tolerance": tolerance,
            },
            "requirements": {
                "legacy_dungeons": legacy_dungeons,
                "bosses": bosses,
                "mini_dungeons": mini_dungeons,
            },
            "structure": {
                "min_layers": min_layers,
                "max_layers": max_layers,
            },
        }
    )


# =============================================================================
# ValidationResult tests
# =============================================================================


class TestValidationResult:
    """Tests for ValidationResult dataclass."""

    def test_valid_result(self):
        """ValidationResult with is_valid=True and no errors."""
        result = ValidationResult(is_valid=True, errors=[], warnings=[])

        assert result.is_valid is True
        assert result.errors == []
        assert result.warnings == []

    def test_invalid_result(self):
        """ValidationResult with is_valid=False and errors."""
        result = ValidationResult(
            is_valid=False,
            errors=["Error 1", "Error 2"],
            warnings=["Warning 1"],
        )

        assert result.is_valid is False
        assert len(result.errors) == 2
        assert "Error 1" in result.errors
        assert "Error 2" in result.errors
        assert len(result.warnings) == 1
        assert "Warning 1" in result.warnings


# =============================================================================
# Structural validation tests
# =============================================================================


class TestStructuralValidation:
    """Tests for structural validation (uses dag.validate_structure)."""

    def test_valid_structure_passes(self):
        """Valid DAG structure passes validation."""
        dag = make_dag_with_content()
        config = make_config()

        result = validate_dag(dag, config)

        # Should not have structural errors
        structural_errors = [e for e in result.errors if "structure" in e.lower()]
        assert structural_errors == []

    def test_invalid_structure_fails(self):
        """Invalid DAG structure produces errors."""
        dag = Dag(seed=42)
        # Missing start_id and end_id
        config = make_config()

        result = validate_dag(dag, config)

        assert result.is_valid is False
        assert len(result.errors) > 0


# =============================================================================
# Requirement validation tests
# =============================================================================


class TestRequirementValidation:
    """Tests for zone requirement validation."""

    def test_insufficient_legacy_dungeons(self):
        """DAG with fewer legacy_dungeons than required produces error."""
        dag = make_dag_with_content(legacy_count=0)
        config = make_config(legacy_dungeons=2)  # Require 2, have 0

        result = validate_dag(dag, config)

        assert result.is_valid is False
        assert any("legacy" in e.lower() for e in result.errors)

    def test_sufficient_legacy_dungeons(self):
        """DAG meeting legacy_dungeons requirement passes."""
        dag = make_dag_with_content(legacy_count=2)
        config = make_config(legacy_dungeons=2)

        result = validate_dag(dag, config)

        # Should not have legacy dungeon errors
        legacy_errors = [e for e in result.errors if "legacy" in e.lower()]
        assert legacy_errors == []

    def test_insufficient_bosses(self):
        """DAG with fewer bosses than required produces error."""
        dag = make_dag_with_content(boss_count=2)
        config = make_config(bosses=5)  # Require 5, have 2

        result = validate_dag(dag, config)

        assert result.is_valid is False
        assert any("boss" in e.lower() for e in result.errors)

    def test_insufficient_mini_dungeons(self):
        """DAG with fewer mini_dungeons than required produces error."""
        dag = make_dag_with_content(mini_dungeon_count=2)
        config = make_config(mini_dungeons=5)  # Require 5, have 2

        result = validate_dag(dag, config)

        assert result.is_valid is False
        assert any("mini" in e.lower() or "dungeon" in e.lower() for e in result.errors)


# =============================================================================
# Path validation tests
# =============================================================================


class TestPathValidation:
    """Tests for path validation."""

    def test_no_paths_error(self):
        """DAG with no paths from start to end produces error."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("s", cluster_type="start"),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("e", cluster_type="final_boss"),
                layer=1,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        # No edge connecting them
        dag.start_id = "start"
        dag.end_id = "end"
        config = make_config()

        result = validate_dag(dag, config)

        assert result.is_valid is False
        assert any("path" in e.lower() or "no path" in e.lower() for e in result.errors)

    def test_single_path_warning(self):
        """DAG with only single path produces warning (not error)."""
        dag = make_simple_dag()  # Linear, single path
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)

        result = validate_dag(dag, config)

        # Single path is a warning, not error
        assert any(
            "single" in w.lower() or "path" in w.lower() for w in result.warnings
        )


# =============================================================================
# Weight validation tests
# =============================================================================


class TestWeightValidation:
    """Tests for path weight spread validation."""

    def test_single_path_no_spread_warning(self):
        """Single-path DAG has zero spread, no weight warnings."""
        dag = make_dag_with_content(
            legacy_count=0,
            mini_dungeon_count=1,
            boss_count=0,
            path_weight=10,
            layer_count=2,
        )
        config = make_config(
            tolerance=5,
            legacy_dungeons=0,
            bosses=0,
            mini_dungeons=0,
            min_layers=1,
        )

        result = validate_dag(dag, config)

        weight_warnings = [w for w in result.warnings if "spread" in w.lower()]
        assert weight_warnings == []

    def test_spread_exceeds_tolerance_warning(self):
        """Multi-path DAG with spread > tolerance produces warning."""
        from speedfog.dag import FogRef

        # Build a forked DAG: start -> a(5) -> end, start -> b(20) -> end
        # Path weights: 0+5+5=10 and 0+20+5=25, spread=15 > tolerance=5
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("cs", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("ca", weight=5),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("f1", "z")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("cb", weight=20),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("f2", "z")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("ce", cluster_type="final_boss", weight=5),
                layer=2,
                tier=1,
                entry_fogs=[FogRef("f3", "z"), FogRef("f4", "z")],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "a", FogRef("f1", "z"), FogRef("f1", "z"))
        dag.add_edge("start", "b", FogRef("f2", "z"), FogRef("f2", "z"))
        dag.add_edge("a", "end", FogRef("f3", "z"), FogRef("f3", "z"))
        dag.add_edge("b", "end", FogRef("f4", "z"), FogRef("f4", "z"))
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(
            tolerance=5,
            legacy_dungeons=0,
            bosses=0,
            mini_dungeons=0,
            min_layers=1,
        )

        result = validate_dag(dag, config)

        assert any("spread" in w.lower() for w in result.warnings)

    def test_spread_within_tolerance_no_warning(self):
        """Multi-path DAG with spread <= tolerance produces no weight warning."""
        from speedfog.dag import FogRef

        # Two paths with equal weight → spread = 0
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("cs", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="a",
                cluster=make_cluster("ca", weight=10),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("f1", "z")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="b",
                cluster=make_cluster("cb", weight=10),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("f2", "z")],
                exit_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster("ce", cluster_type="final_boss", weight=5),
                layer=2,
                tier=1,
                entry_fogs=[FogRef("f3", "z"), FogRef("f4", "z")],
                exit_fogs=[],
            )
        )
        dag.add_edge("start", "a", FogRef("f1", "z"), FogRef("f1", "z"))
        dag.add_edge("start", "b", FogRef("f2", "z"), FogRef("f2", "z"))
        dag.add_edge("a", "end", FogRef("f3", "z"), FogRef("f3", "z"))
        dag.add_edge("b", "end", FogRef("f4", "z"), FogRef("f4", "z"))
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(
            tolerance=5,
            legacy_dungeons=0,
            bosses=0,
            mini_dungeons=0,
            min_layers=1,
        )

        result = validate_dag(dag, config)

        weight_warnings = [w for w in result.warnings if "spread" in w.lower()]
        assert weight_warnings == []


# =============================================================================
# Layer validation tests
# =============================================================================


class TestLayerValidation:
    """Tests for layer count validation."""

    def test_few_layers_warning(self):
        """DAG with fewer layers than min_layers produces warning."""
        dag = make_dag_with_content(
            legacy_count=0,
            mini_dungeon_count=1,
            boss_count=0,
            path_weight=30,
            layer_count=2,  # Very few layers
        )
        config = make_config(
            min_layers=6,  # Require at least 6 layers
            legacy_dungeons=0,
            bosses=0,
            mini_dungeons=0,
        )

        result = validate_dag(dag, config)

        # Few layers is a warning
        assert any("layer" in w.lower() for w in result.warnings)
