"""Tests for DAG validator."""

from speedfog.clusters import ClusterData
from speedfog.config import Config
from speedfog.dag import Dag, DagNode, FogRef
from speedfog.validator import ValidationResult, validate_dag


def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | None = None,
    exit_fogs: list[dict] | None = None,
    allow_shared_entrance: bool = False,
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
        allow_shared_entrance=allow_shared_entrance,
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
    min_layers: int = 6,
    max_layers: int = 10,
) -> Config:
    """Create a Config with specified requirements."""
    return Config.from_dict(
        {
            "budget": {},
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


# =============================================================================
# Shared entrance entry fog consistency tests
# =============================================================================


class TestSharedEntranceValidation:
    """Tests for entry fog consistency with shared entrance nodes."""

    def test_shared_entrance_allows_fewer_entry_fogs(self):
        """Shared entrance node with 2 incoming edges but 1 entry_fog is valid."""
        dag = Dag(seed=42)

        start_cluster = make_cluster("start", cluster_type="start", weight=0)
        dag.add_node(
            DagNode(
                id="start",
                cluster=start_cluster,
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=["exit_a", "exit_b"],
            )
        )

        branch_a = make_cluster("branch_a", cluster_type="mini_dungeon")
        dag.add_node(
            DagNode(
                id="branch_a",
                cluster=branch_a,
                layer=1,
                tier=1,
                entry_fogs=["fog_a"],
                exit_fogs=["fog_a_out"],
            )
        )
        dag.add_edge("start", "branch_a", "exit_a", "fog_a")

        branch_b = make_cluster("branch_b", cluster_type="mini_dungeon")
        dag.add_node(
            DagNode(
                id="branch_b",
                cluster=branch_b,
                layer=1,
                tier=1,
                entry_fogs=["fog_b"],
                exit_fogs=["fog_b_out"],
            )
        )
        dag.add_edge("start", "branch_b", "exit_b", "fog_b")

        # Shared entrance merge: 2 incoming edges, 1 entry_fog
        merge_cluster = make_cluster(
            "merge",
            cluster_type="mini_dungeon",
            allow_shared_entrance=True,
        )
        dag.add_node(
            DagNode(
                id="merge",
                cluster=merge_cluster,
                layer=2,
                tier=1,
                entry_fogs=["shared_entry"],
                exit_fogs=["merge_exit"],
            )
        )
        dag.add_edge("branch_a", "merge", "fog_a_out", "shared_entry")
        dag.add_edge("branch_b", "merge", "fog_b_out", "shared_entry")

        # End node
        end_cluster = make_cluster("end", cluster_type="major_boss", weight=0)
        dag.add_node(
            DagNode(
                id="end",
                cluster=end_cluster,
                layer=3,
                tier=1,
                entry_fogs=["end_entry"],
                exit_fogs=[],
            )
        )
        dag.add_edge("merge", "end", "merge_exit", "end_entry")
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        result = validate_dag(dag, config)

        # No entry fog consistency errors
        entry_fog_errors = [
            e for e in result.errors if "entry_fogs" in e or "shared entrance" in e
        ]
        assert entry_fog_errors == []

    def test_non_shared_entrance_rejects_mismatched_entry_fogs(self):
        """Non-shared entrance node with 2 incoming edges but 1 entry_fog is invalid."""
        dag = Dag(seed=42)

        start_cluster = make_cluster("start", cluster_type="start", weight=0)
        dag.add_node(
            DagNode(
                id="start",
                cluster=start_cluster,
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=["exit_a", "exit_b"],
            )
        )

        branch_a = make_cluster("branch_a", cluster_type="mini_dungeon")
        dag.add_node(
            DagNode(
                id="branch_a",
                cluster=branch_a,
                layer=1,
                tier=1,
                entry_fogs=["fog_a"],
                exit_fogs=["fog_a_out"],
            )
        )
        dag.add_edge("start", "branch_a", "exit_a", "fog_a")

        branch_b = make_cluster("branch_b", cluster_type="mini_dungeon")
        dag.add_node(
            DagNode(
                id="branch_b",
                cluster=branch_b,
                layer=1,
                tier=1,
                entry_fogs=["fog_b"],
                exit_fogs=["fog_b_out"],
            )
        )
        dag.add_edge("start", "branch_b", "exit_b", "fog_b")

        # NOT shared entrance: 2 incoming edges, 1 entry_fog → error
        merge_cluster = make_cluster(
            "merge",
            cluster_type="mini_dungeon",
            allow_shared_entrance=False,
        )
        dag.add_node(
            DagNode(
                id="merge",
                cluster=merge_cluster,
                layer=2,
                tier=1,
                entry_fogs=["only_entry"],
                exit_fogs=["merge_exit"],
            )
        )
        dag.add_edge("branch_a", "merge", "fog_a_out", "only_entry")
        dag.add_edge("branch_b", "merge", "fog_b_out", "only_entry")

        end_cluster = make_cluster("end", cluster_type="major_boss", weight=0)
        dag.add_node(
            DagNode(
                id="end",
                cluster=end_cluster,
                layer=3,
                tier=1,
                entry_fogs=["end_entry"],
                exit_fogs=[],
            )
        )
        dag.add_edge("merge", "end", "merge_exit", "end_entry")
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        result = validate_dag(dag, config)

        # Should have entry fog consistency error
        entry_fog_errors = [e for e in result.errors if "entry_fogs" in e]
        assert len(entry_fog_errors) == 1
        assert "merge" in entry_fog_errors[0]


class TestRequiredZones:
    """Tests for required zones validation."""

    def test_required_zone_present_passes(self):
        """DAG containing the required zone passes validation."""
        dag = make_dag_with_content(legacy_count=1, boss_count=1, mini_dungeon_count=1)
        # The helper creates clusters with zones like "legacy_cluster_0_zone"
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        config.requirements.zones = ["legacy_cluster_0_zone"]

        result = validate_dag(dag, config)

        zone_errors = [e for e in result.errors if "Required zone" in e]
        assert zone_errors == []

    def test_required_zone_missing_fails(self):
        """DAG missing a required zone produces error."""
        dag = make_dag_with_content(legacy_count=1, boss_count=1, mini_dungeon_count=1)
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        config.requirements.zones = ["haligtree_malenia"]

        result = validate_dag(dag, config)

        assert result.is_valid is False
        zone_errors = [e for e in result.errors if "Required zone" in e]
        assert len(zone_errors) == 1
        assert "haligtree_malenia" in zone_errors[0]

    def test_multiple_required_zones_all_missing(self):
        """DAG missing multiple required zones reports each one."""
        dag = make_dag_with_content(legacy_count=1, boss_count=1, mini_dungeon_count=1)
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        config.requirements.zones = ["caelid_radahn", "haligtree_malenia"]

        result = validate_dag(dag, config)

        assert result.is_valid is False
        zone_errors = [e for e in result.errors if "Required zone" in e]
        assert len(zone_errors) == 2

    def test_empty_required_zones_passes(self):
        """Empty zones list imposes no constraint."""
        dag = make_simple_dag()
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        config.requirements.zones = []

        result = validate_dag(dag, config)

        zone_errors = [e for e in result.errors if "Required zone" in e]
        assert zone_errors == []


class TestEntryZoneMembership:
    """Tests for entry zone membership validation."""

    def test_entry_zone_in_target_cluster_passes(self):
        """Edge whose entry_fog zone is in target cluster zones = ok."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("start_c", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster(
                    "castle",
                    zones=["castle_front", "castle_back"],
                    cluster_type="final_boss",
                ),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("castle_entry", "castle_front")],
            )
        )
        dag.add_edge(
            "start",
            "end",
            FogRef("start_exit", "start_c_zone"),
            FogRef("castle_entry", "castle_front"),
        )
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        result = validate_dag(dag, config)

        zone_errors = [e for e in result.errors if "Entry zone mismatch" in e]
        assert zone_errors == []

    def test_entry_zone_not_in_target_cluster_fails(self):
        """Edge whose entry_fog zone is NOT in target cluster = error."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("start_c", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
            )
        )
        # Target cluster has zones ["castle_front"] but edge entry_fog uses "sewer"
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster(
                    "castle",
                    zones=["castle_front"],
                    cluster_type="final_boss",
                ),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("sewer_entry", "sewer")],
            )
        )
        dag.add_edge(
            "start",
            "end",
            FogRef("start_exit", "start_c_zone"),
            FogRef("sewer_entry", "sewer"),  # zone "sewer" not in target cluster
        )
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        result = validate_dag(dag, config)

        assert result.is_valid is False
        zone_errors = [e for e in result.errors if "Entry zone mismatch" in e]
        assert len(zone_errors) == 1
        assert "sewer" in zone_errors[0]
        assert "castle" in zone_errors[0]

    def test_empty_entry_zone_skipped(self):
        """Edge with empty entry_fog zone is not checked."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("start_c", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
            )
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=make_cluster(
                    "boss",
                    zones=["boss_zone"],
                    cluster_type="final_boss",
                ),
                layer=1,
                tier=1,
                entry_fogs=[FogRef("", "")],
            )
        )
        dag.add_edge(
            "start",
            "end",
            FogRef("start_exit", "start_c_zone"),
            FogRef("", ""),  # empty zone — final boss edge case
        )
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        result = validate_dag(dag, config)

        zone_errors = [e for e in result.errors if "Entry zone mismatch" in e]
        assert zone_errors == []


class TestLayerTypeHomogeneity:
    """Tests for layer type homogeneity validation."""

    def test_homogeneous_parallel_branches_pass(self):
        """Layer with multiple nodes of the same type passes."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("start_c", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=["exit_a", "exit_b"],
            )
        )
        # Layer 1: two nodes with the SAME type
        dag.add_node(
            DagNode(
                id="node_a",
                cluster=make_cluster("mini_a", cluster_type="mini_dungeon"),
                layer=1,
                tier=1,
                entry_fogs=["fog_a"],
                exit_fogs=["fog_a_out"],
            )
        )
        dag.add_node(
            DagNode(
                id="node_b",
                cluster=make_cluster("mini_b", cluster_type="mini_dungeon"),
                layer=1,
                tier=1,
                entry_fogs=["fog_b"],
                exit_fogs=["fog_b_out"],
            )
        )
        dag.add_edge("start", "node_a", "exit_a", "fog_a")
        dag.add_edge("start", "node_b", "exit_b", "fog_b")

        end_cluster = make_cluster(
            "end_c", cluster_type="final_boss", allow_shared_entrance=True
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=end_cluster,
                layer=2,
                tier=1,
                entry_fogs=["fog_end"],
                exit_fogs=[],
            )
        )
        dag.add_edge("node_a", "end", "fog_a_out", "fog_end")
        dag.add_edge("node_b", "end", "fog_b_out", "fog_end")
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        result = validate_dag(dag, config)

        homogeneity_errors = [e for e in result.errors if "mixed types" in e.lower()]
        assert homogeneity_errors == []

    def test_mixed_types_in_layer_fails(self):
        """Layer with nodes of different types produces error."""
        dag = Dag(seed=42)
        dag.add_node(
            DagNode(
                id="start",
                cluster=make_cluster("start_c", cluster_type="start", weight=0),
                layer=0,
                tier=1,
                entry_fogs=[],
                exit_fogs=["exit_a", "exit_b"],
            )
        )
        # Layer 1: two nodes with different types
        dag.add_node(
            DagNode(
                id="node_a",
                cluster=make_cluster("boss_c", cluster_type="boss_arena"),
                layer=1,
                tier=1,
                entry_fogs=["fog_a"],
                exit_fogs=["fog_a_out"],
            )
        )
        dag.add_node(
            DagNode(
                id="node_b",
                cluster=make_cluster("dungeon_c", cluster_type="legacy_dungeon"),
                layer=1,
                tier=1,
                entry_fogs=["fog_b"],
                exit_fogs=["fog_b_out"],
            )
        )
        dag.add_edge("start", "node_a", "exit_a", "fog_a")
        dag.add_edge("start", "node_b", "exit_b", "fog_b")

        # Merge to end
        end_cluster = make_cluster(
            "end_c", cluster_type="final_boss", allow_shared_entrance=True
        )
        dag.add_node(
            DagNode(
                id="end",
                cluster=end_cluster,
                layer=2,
                tier=1,
                entry_fogs=["fog_end"],
                exit_fogs=[],
            )
        )
        dag.add_edge("node_a", "end", "fog_a_out", "fog_end")
        dag.add_edge("node_b", "end", "fog_b_out", "fog_end")
        dag.start_id = "start"
        dag.end_id = "end"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        result = validate_dag(dag, config)

        assert result.is_valid is False
        homogeneity_errors = [e for e in result.errors if "mixed types" in e.lower()]
        assert len(homogeneity_errors) == 1
        assert "Layer 1" in homogeneity_errors[0]
        assert "boss_arena" in homogeneity_errors[0]
        assert "legacy_dungeon" in homogeneity_errors[0]

    def test_single_node_layers_skipped(self):
        """Layers with a single node are not checked (trivially homogeneous)."""
        # make_simple_dag has 2 single-node layers (start, end)
        dag = make_simple_dag()
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)

        result = validate_dag(dag, config)

        homogeneity_errors = [e for e in result.errors if "mixed types" in e.lower()]
        assert homogeneity_errors == []


class TestEventFlagBudget:
    """Tests for event flag budget validation."""

    def test_small_dag_within_budget(self):
        """Simple DAG with few edges passes budget check."""
        dag = make_simple_dag()
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)

        result = validate_dag(dag, config)

        budget_errors = [e for e in result.errors if "flag budget" in e]
        assert budget_errors == []

    def test_large_dag_exceeds_budget_with_death_markers(self):
        """DAG with many nodes exceeds budget when death markers are on."""
        # 300 content nodes = 301 edges + 1 finish + 3*300 death flags = 1202 > 1000
        dag = make_dag_with_content(
            legacy_count=0, boss_count=0, mini_dungeon_count=300, layer_count=300
        )
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        config.death_markers = True

        result = validate_dag(dag, config)

        budget_errors = [e for e in result.errors if "flag budget" in e]
        assert len(budget_errors) == 1
        assert "exceeded" in budget_errors[0]

    def test_large_dag_within_budget_without_death_markers(self):
        """Same large DAG passes when death markers are off (fewer flags)."""
        # 300 content nodes = 301 edges + 1 finish = 302 < 1000
        dag = make_dag_with_content(
            legacy_count=0, boss_count=0, mini_dungeon_count=300, layer_count=300
        )
        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)
        config.death_markers = False

        result = validate_dag(dag, config)

        budget_errors = [e for e in result.errors if "flag budget" in e]
        assert budget_errors == []

    def test_death_markers_toggle_changes_count(self):
        """Death markers on vs off changes whether budget is exceeded."""
        # 260 nodes: 261 edges + 1 = 262 without markers, 262 + 780 = 1042 with
        dag = make_dag_with_content(
            legacy_count=0, boss_count=0, mini_dungeon_count=260, layer_count=260
        )
        config_on = make_config(
            legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1
        )
        config_on.death_markers = True

        config_off = make_config(
            legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1
        )
        config_off.death_markers = False

        result_on = validate_dag(dag, config_on)
        result_off = validate_dag(dag, config_off)

        on_budget_errors = [e for e in result_on.errors if "flag budget" in e]
        off_budget_errors = [e for e in result_off.errors if "flag budget" in e]
        assert len(on_budget_errors) == 1
        assert off_budget_errors == []

    def test_malformed_dag_skips_budget_check(self):
        """DAG with start_id not in nodes skips budget check gracefully."""
        dag = Dag(seed=42)
        dag.start_id = "nonexistent"
        dag.end_id = "also_nonexistent"

        config = make_config(legacy_dungeons=0, bosses=0, mini_dungeons=0, min_layers=1)

        result = validate_dag(dag, config)

        # Should have structural errors but no budget crash
        budget_errors = [e for e in result.errors if "flag budget" in e]
        assert budget_errors == []
        assert result.is_valid is False


class TestValidatorAllowedTypes:
    """Validator honors allowed_types."""

    def _boss_rush_config(self) -> Config:
        import warnings as _w

        with _w.catch_warnings():
            _w.simplefilter("ignore")
            return Config.from_dict(
                {
                    "requirements": {
                        "allowed_types": ["boss_arena", "major_boss"],
                        "legacy_dungeons": 0,
                        "bosses": 2,
                        "mini_dungeons": 0,
                        "major_bosses": 1,
                    },
                    "structure": {"min_layers": 1, "max_layers": 5},
                }
            )

    def test_excluded_types_skipped_in_requirements_check(self):
        """Excluded types produce no 'insufficient' error even if count is 0."""
        dag = make_dag_with_content(
            legacy_count=0,
            mini_dungeon_count=0,
            boss_count=2,
            path_weight=10,
            layer_count=3,
        )
        config = self._boss_rush_config()
        result = validate_dag(dag, config)
        assert not any(
            "legacy_dungeons" in e or "mini_dungeons" in e for e in result.errors
        )

    def test_zone_in_excluded_type_flagged(self):
        """A required zone whose cluster type is excluded raises an error."""
        from speedfog.clusters import ClusterPool

        pool = ClusterPool()
        pool.add(
            make_cluster(
                "mini_cluster",
                zones=["unreachable_mini_zone"],
                cluster_type="mini_dungeon",
            )
        )

        import warnings as _w

        with _w.catch_warnings():
            _w.simplefilter("ignore")
            config = Config.from_dict(
                {
                    "requirements": {
                        "allowed_types": ["boss_arena", "major_boss"],
                        "zones": ["unreachable_mini_zone"],
                        "legacy_dungeons": 0,
                        "bosses": 0,
                        "mini_dungeons": 0,
                        "major_bosses": 0,
                    },
                    "structure": {"min_layers": 1, "max_layers": 5},
                }
            )

        dag = make_simple_dag()
        result = validate_dag(dag, config, pool)
        assert any("unreachable_mini_zone" in e for e in result.errors)
        assert any("not in allowed_types" in e for e in result.errors)

    def test_zone_in_allowed_type_not_flagged(self):
        """A required zone whose cluster type is allowed does not trigger the check."""
        from speedfog.clusters import ClusterPool

        pool = ClusterPool()
        pool.add(
            make_cluster(
                "boss_cluster",
                zones=["reachable_boss_zone"],
                cluster_type="boss_arena",
            )
        )

        import warnings as _w

        with _w.catch_warnings():
            _w.simplefilter("ignore")
            config = Config.from_dict(
                {
                    "requirements": {
                        "allowed_types": ["boss_arena", "major_boss"],
                        "zones": ["reachable_boss_zone"],
                        "legacy_dungeons": 0,
                        "bosses": 0,
                        "mini_dungeons": 0,
                        "major_bosses": 0,
                    },
                    "structure": {"min_layers": 1, "max_layers": 5},
                }
            )

        dag = make_simple_dag()
        result = validate_dag(dag, config, pool)
        # Zone-type check must not flag this (but the zone missing error is fine).
        assert not any("not in allowed_types" in e for e in result.errors)
