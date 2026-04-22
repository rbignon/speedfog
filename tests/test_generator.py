"""Tests for DAG generation logic."""

import os
import random
from pathlib import Path

import pytest

from speedfog.clusters import ClusterData, ClusterPool, fog_matches_spec
from speedfog.config import Config, RequirementsConfig, StructureConfig
from speedfog.dag import Branch, Dag, DagNode, FogRef
from speedfog.generator import (
    GenerationError,
    LayerOperation,
    _filter_exits_by_proximity,
    _find_valid_merge_indices,
    _has_valid_merge_pair,
    _inject_prerequisite,
    _mark_cluster_used,
    _pick_entry_and_exits_for_node,
    _stable_main_shuffle,
    can_be_merge_node,
    can_be_passant_node,
    can_be_split_node,
    compute_net_exits,
    count_net_exits,
    determine_operation,
    execute_merge_layer,
    execute_passant_layer,
    execute_rebalance_layer,
    generate_dag,
    generate_with_retry,
    pick_cluster_uniform,
    pick_cluster_weight_matched,
    pick_cluster_with_type_fallback,
    pick_entry_with_max_exits,
    select_entries_for_merge,
    select_weighted_final_boss,
    update_branch_counters,
    validate_config,
)

_SENTINEL = object()


def _boss_candidates(pool: ClusterPool) -> list[ClusterData]:
    """Return boss candidates from a pool (major_boss + final_boss)."""
    return pool.get_by_type("major_boss") + pool.get_by_type("final_boss")


def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | object = _SENTINEL,
    exit_fogs: list[dict] | object = _SENTINEL,
    allow_shared_entrance: bool = False,
    allow_entry_as_exit: bool = False,
    requires: str = "",
    proximity_groups: list[list[str]] | None = None,
) -> ClusterData:
    """Helper to create test ClusterData objects.

    Uses sentinel value to distinguish between explicit [] and no argument.
    """
    if entry_fogs is _SENTINEL:
        entry_fogs = [{"fog_id": f"{cluster_id}_entry", "zone": cluster_id}]
    if exit_fogs is _SENTINEL:
        exit_fogs = [{"fog_id": f"{cluster_id}_exit", "zone": cluster_id}]
    return ClusterData(
        id=cluster_id,
        zones=zones or [f"{cluster_id}_zone"],
        type=cluster_type,
        weight=weight,
        entry_fogs=entry_fogs,  # type: ignore[arg-type]
        exit_fogs=exit_fogs,  # type: ignore[arg-type]
        allow_shared_entrance=allow_shared_entrance,
        allow_entry_as_exit=allow_entry_as_exit,
        requires=requires,
        proximity_groups=proximity_groups or [],
    )


def make_cluster_pool() -> ClusterPool:
    """Create a minimal cluster pool for testing.

    Includes:
    - 1 start cluster (with multiple exits)
    - 1 final_boss cluster
    - Several legacy_dungeon, mini_dungeon, boss_arena clusters
    """
    pool = ClusterPool()

    # Start cluster with 2 exits
    pool.add(
        make_cluster(
            "chapel_start",
            zones=["chapel"],
            cluster_type="start",
            weight=1,
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "start_exit_1", "zone": "chapel"},
                {"fog_id": "start_exit_2", "zone": "chapel"},
            ],
        )
    )

    # Final bosses
    pool.add(
        make_cluster(
            "erdtree_boss",
            zones=["leyndell_erdtree"],
            cluster_type="final_boss",
            weight=5,
            entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
            exit_fogs=[],
            requires="farumazula_maliketh",
        )
    )
    pool.add(
        make_cluster(
            "pcr_boss",
            zones=["enirilim_radahn"],
            cluster_type="final_boss",
            weight=5,
            entry_fogs=[{"fog_id": "pcr_entry", "zone": "enirilim_radahn"}],
            exit_fogs=[],
        )
    )

    # Maliketh (prerequisite for erdtree, passant-capable major_boss)
    pool.add(
        make_cluster(
            "maliketh",
            zones=["farumazula_maliketh"],
            cluster_type="major_boss",
            weight=4,
            entry_fogs=[{"fog_id": "maliketh_entry", "zone": "farumazula_maliketh"}],
            exit_fogs=[
                {"fog_id": "maliketh_exit", "zone": "farumazula_maliketh"},
                {"fog_id": "maliketh_entry", "zone": "farumazula_maliketh"},
            ],
        )
    )

    # Legacy dungeons
    for i in range(3):
        pool.add(
            make_cluster(
                f"legacy_{i}",
                zones=[f"legacy_{i}_zone"],
                cluster_type="legacy_dungeon",
                weight=10,
                entry_fogs=[
                    {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"}
                ],
                exit_fogs=[
                    {"fog_id": f"legacy_{i}_exit", "zone": f"legacy_{i}_zone"},
                    {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"},
                ],
            )
        )

    # Mini dungeons
    for i in range(10):
        pool.add(
            make_cluster(
                f"mini_{i}",
                zones=[f"mini_{i}_zone"],
                cluster_type="mini_dungeon",
                weight=5,
                entry_fogs=[{"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"}],
                exit_fogs=[
                    {"fog_id": f"mini_{i}_exit", "zone": f"mini_{i}_zone"},
                    {"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"},
                ],
            )
        )

    # Boss arenas
    for i in range(6):
        pool.add(
            make_cluster(
                f"boss_{i}",
                zones=[f"boss_{i}_zone"],
                cluster_type="boss_arena",
                weight=3,
                entry_fogs=[{"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"}],
                exit_fogs=[
                    {"fog_id": f"boss_{i}_exit", "zone": f"boss_{i}_zone"},
                    {"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"},
                ],
            )
        )

    return pool


# =============================================================================
# generate_dag tests
# =============================================================================


class TestGenerateDag:
    """Tests for generate_dag function."""

    def test_generates_dag_with_fixed_seed(self):
        """Generates a DAG reproducibly with a fixed seed."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 12345
        config.structure.min_layers = 3
        config.structure.max_layers = 5
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0

        dag1, _log1 = generate_dag(
            config, pool, seed=12345, boss_candidates=_boss_candidates(pool)
        )
        dag2, _log2 = generate_dag(
            config, pool, seed=12345, boss_candidates=_boss_candidates(pool)
        )

        assert dag1.seed == dag2.seed == 12345
        assert len(dag1.nodes) == len(dag2.nodes)
        assert set(dag1.nodes.keys()) == set(dag2.nodes.keys())

    def test_has_start_and_end_nodes(self):
        """Generated DAG has start and end nodes."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        assert dag.start_id == "start"
        assert dag.end_id == "end"
        assert "start" in dag.nodes
        assert "end" in dag.nodes

    def test_all_paths_reach_end(self):
        """All paths in the DAG lead from start to end."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.min_layers = 3
        config.structure.max_layers = 5
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        # Structure validation ensures reachability (all nodes reachable
        # from start, all nodes can reach end)
        errors = dag.validate_structure()
        assert not errors, f"DAG structure errors: {errors}"

    def test_respects_max_parallel_paths(self):
        """DAG does not exceed max_parallel_paths at any layer."""
        # Create pool with merge-compatible clusters (2+ entries)
        pool = ClusterPool()

        # Start cluster with 2 exits
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "start_exit_1", "zone": "chapel"},
                    {"fog_id": "start_exit_2", "zone": "chapel"},
                ],
            )
        )

        # Final boss
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
                exit_fogs=[],
            )
        )

        # Add merge-compatible clusters (2 entries, 1 net exit after merge)
        for i in range(5):
            pool.add(
                make_cluster(
                    f"merge_{i}",
                    zones=[f"merge_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"merge_{i}_entry_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_entry_b", "zone": f"merge_{i}_zone"},
                    ],
                    exit_fogs=[
                        # 2 entries (bidir) + 1 pure exit = 1 net exit after consuming 2
                        {"fog_id": f"merge_{i}_entry_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_entry_b", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_exit", "zone": f"merge_{i}_zone"},
                    ],
                )
            )

        # Add passant-compatible clusters (1 entry bidir + 1 pure exit = 1 net exit)
        for i in range(20):
            pool.add(
                make_cluster(
                    f"passant_{i}",
                    zones=[f"passant_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"passant_{i}_entry", "zone": f"passant_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"passant_{i}_entry", "zone": f"passant_{i}_zone"},
                        {"fog_id": f"passant_{i}_exit", "zone": f"passant_{i}_zone"},
                    ],
                )
            )

        config = Config()
        config.structure.max_branches = 2
        config.structure.max_parallel_paths = 3
        config.structure.min_layers = 6
        config.structure.max_layers = 8
        config.structure.split_probability = 0.2
        config.structure.merge_probability = 0.2
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 0

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        # Count nodes per layer
        nodes_by_layer: dict[int, int] = {}
        for node in dag.nodes.values():
            layer = node.layer
            nodes_by_layer[layer] = nodes_by_layer.get(layer, 0) + 1

        # Each layer should have at most max_parallel_paths nodes
        for layer, count in nodes_by_layer.items():
            assert (
                count <= config.structure.max_parallel_paths
            ), f"Layer {layer} has {count} nodes > max_parallel_paths {config.structure.max_parallel_paths}"

    def test_no_zone_overlap(self):
        """Each zone appears in exactly one node."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.min_layers = 4
        config.structure.max_layers = 6
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        all_zones: set[str] = set()
        for node in dag.nodes.values():
            for zone in node.cluster.zones:
                assert zone not in all_zones, f"Zone {zone} appears multiple times"
                all_zones.add(zone)

    def test_raises_if_no_start_cluster(self):
        """Raises GenerationError if no start cluster exists."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
            )
        )
        config = Config()

        with pytest.raises(GenerationError, match="[Ss]tart"):
            generate_dag(config, pool, seed=42, boss_candidates=_boss_candidates(pool))

    def test_raises_if_no_final_boss(self):
        """Raises GenerationError if no final_boss cluster exists."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "exit_1", "zone": "chapel"},
                    {"fog_id": "exit_2", "zone": "chapel"},
                ],
            )
        )
        # Add enough intermediate clusters to satisfy requirements
        # We need legacy_dungeons, mini_dungeons, and boss_arenas
        for i in range(5):
            pool.add(
                make_cluster(
                    f"legacy_{i}",
                    zones=[f"legacy_{i}_zone"],
                    cluster_type="legacy_dungeon",
                    entry_fogs=[
                        {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"}
                    ],
                    exit_fogs=[
                        {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"},
                        {"fog_id": f"legacy_{i}_exit", "zone": f"legacy_{i}_zone"},
                    ],
                )
            )
        for i in range(10):
            pool.add(
                make_cluster(
                    f"mini_{i}",
                    zones=[f"mini_{i}_zone"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[
                        {"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"}
                    ],
                    exit_fogs=[
                        {"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"},
                        {"fog_id": f"mini_{i}_exit", "zone": f"mini_{i}_zone"},
                    ],
                )
            )
        for i in range(10):
            pool.add(
                make_cluster(
                    f"boss_{i}",
                    zones=[f"boss_{i}_zone"],
                    cluster_type="boss_arena",
                    entry_fogs=[
                        {"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"}
                    ],
                    exit_fogs=[
                        {"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"},
                        {"fog_id": f"boss_{i}_exit", "zone": f"boss_{i}_zone"},
                    ],
                )
            )
        # Note: No final_boss cluster added!
        config = Config()
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        config.requirements.legacy_dungeons = 1
        config.requirements.mini_dungeons = 1
        config.requirements.bosses = 1

        with pytest.raises(GenerationError, match="[Ff]inal"):
            generate_dag(config, pool, seed=42, boss_candidates=_boss_candidates(pool))

    def test_layer_tiers_increase(self):
        """Difficulty tier increases with layer index."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.min_layers = 4
        config.structure.max_layers = 4
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        # Group tiers by layer
        tiers_by_layer: dict[int, list[int]] = {}
        for node in dag.nodes.values():
            layer = node.layer
            if layer not in tiers_by_layer:
                tiers_by_layer[layer] = []
            tiers_by_layer[layer].append(node.tier)

        # Check tiers increase (on average) with layers
        layers = sorted(tiers_by_layer.keys())
        for i in range(len(layers) - 1):
            avg_tier_current = sum(tiers_by_layer[layers[i]]) / len(
                tiers_by_layer[layers[i]]
            )
            avg_tier_next = sum(tiers_by_layer[layers[i + 1]]) / len(
                tiers_by_layer[layers[i + 1]]
            )
            assert avg_tier_next >= avg_tier_current


# =============================================================================
# generate_with_retry tests
# =============================================================================


class TestGenerateWithRetry:
    """Tests for generate_with_retry function."""

    def test_fixed_seed_single_attempt(self):
        """With non-zero seed, uses that seed directly (single attempt)."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 99999
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        # Relax requirements so test pool can satisfy them
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        result = generate_with_retry(
            config, pool, boss_candidates=_boss_candidates(pool)
        )

        assert result.seed == 99999
        assert result.dag.seed == 99999
        assert result.attempts == 1
        assert result.validation.is_valid

    def test_auto_reroll_finds_valid_seed(self):
        """With seed=0, tries random seeds until success."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 0  # Auto-reroll mode
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        # Relax requirements so test pool can satisfy them
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        result = generate_with_retry(
            config, pool, max_attempts=100, boss_candidates=_boss_candidates(pool)
        )

        assert result.seed != 0  # Should have found a random seed
        assert result.dag.seed == result.seed
        assert len(result.dag.nodes) > 0
        assert result.validation.is_valid

    def test_raises_after_max_attempts(self):
        """Raises GenerationError after max_attempts failures."""
        # Create a pool that will always fail (no start cluster)
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
            )
        )
        pool.add(
            make_cluster(
                "pcr_boss",
                zones=["enirilim_radahn"],
                cluster_type="final_boss",
            )
        )
        config = Config()
        config.seed = 0

        with pytest.raises(GenerationError, match="after.*attempts"):
            generate_with_retry(
                config, pool, max_attempts=5, boss_candidates=_boss_candidates(pool)
            )

    def test_fixed_seed_propagates_error(self):
        """With fixed seed that fails, propagates the error."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
            )
        )
        config = Config()
        config.seed = 42  # Fixed seed

        with pytest.raises(GenerationError):
            generate_with_retry(config, pool, boss_candidates=_boss_candidates(pool))

    def test_post_validate_triggers_retry_in_auto_mode(self):
        """post_validate rejecting the first N seeds forces the loop to keep
        rerolling, and the accepted (dag, seed) is what the hook last saw."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 0
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        seen: list[tuple[object, int]] = []

        def post_validate(dag, seed):
            seen.append((dag, seed))
            if len(seen) < 3:
                raise GenerationError("simulated matcher failure")

        result = generate_with_retry(
            config,
            pool,
            max_attempts=20,
            boss_candidates=_boss_candidates(pool),
            post_validate=post_validate,
        )

        assert len(seen) == 3
        assert result.attempts == 3
        # The returned DAG/seed must match the last (dag, seed) the hook
        # accepted, not an earlier rejected one.
        last_dag, last_seed = seen[-1]
        assert result.seed == last_seed
        assert result.dag is last_dag

    def test_post_validate_fixed_seed_propagates(self):
        """post_validate failing under a fixed seed surfaces the error instead
        of silently passing."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 99999
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        def post_validate(dag, seed):
            raise GenerationError("matcher infeasible")

        with pytest.raises(GenerationError, match="matcher infeasible"):
            generate_with_retry(
                config,
                pool,
                boss_candidates=_boss_candidates(pool),
                post_validate=post_validate,
            )


class TestValidateConfig:
    """Tests for validate_config function."""

    def test_valid_config_returns_empty_list(self):
        """Valid configuration returns no errors."""
        pool = make_cluster_pool()
        config = Config()
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_invalid_first_layer_type(self):
        """Invalid first_layer_type returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.first_layer_type = "invalid_type"
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "first_layer_type" in errors[0]
        assert "invalid_type" in errors[0]

    def test_valid_first_layer_type(self):
        """Valid first_layer_type returns no error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.first_layer_type = "legacy_dungeon"
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_major_bosses_negative_validation(self):
        """Negative major_bosses returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.requirements.major_bosses = -1
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "major_bosses" in errors[0]

    def test_major_bosses_zero_valid(self):
        """major_bosses=0 is valid (no major bosses)."""
        pool = make_cluster_pool()
        config = Config()
        config.requirements.major_bosses = 0
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_major_bosses_positive_valid(self):
        """Positive major_bosses is valid."""
        pool = make_cluster_pool()
        config = Config()
        config.requirements.major_bosses = 8
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_unknown_final_boss_candidate(self):
        """Unknown zone in final_boss_candidates returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.final_boss_candidates = {"nonexistent_zone": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "nonexistent_zone" in errors[0]

    def test_valid_final_boss_candidate(self):
        """Valid zone in final_boss_candidates returns no error."""
        pool = make_cluster_pool()
        config = Config()
        # leyndell_erdtree exists in the fixture
        config.structure.final_boss_candidates = {"leyndell_erdtree": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_final_boss_candidates_all_keyword(self):
        """'all' keyword in final_boss_candidates is valid."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.final_boss_candidates = {"all": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_invalid_weight_returns_error(self):
        """Weight < 1 in final_boss_candidates returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.final_boss_candidates = {"leyndell_erdtree": 0}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "invalid weight" in errors[0]

    def test_multiple_errors_returned(self):
        """Multiple config errors are all returned."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.first_layer_type = "bad_type"
        config.requirements.major_bosses = -1
        config.structure.final_boss_candidates = {"bad_zone": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 3

    def test_requirements_exceed_min_layers_warning(self):
        """Warning when total requirements exceed min_layers."""
        pool = make_cluster_pool()
        config = Config()
        # 2 + 10 + 10 + 8 = 30, but min_layers = 6 (default)
        config.requirements.legacy_dungeons = 2
        config.requirements.bosses = 10
        config.requirements.mini_dungeons = 10
        config.requirements.major_bosses = 8
        config.structure.min_layers = 6
        errors, warnings = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []
        req_warnings = [w for w in warnings if "requirements" in w.lower()]
        assert len(req_warnings) == 1
        assert "30" in req_warnings[0]  # total requirements

    def test_requirements_within_min_layers_no_warning(self):
        """No warning when requirements fit within min_layers."""
        pool = make_cluster_pool()
        config = Config()
        config.requirements.legacy_dungeons = 1
        config.requirements.bosses = 2
        config.requirements.mini_dungeons = 2
        config.requirements.major_bosses = 0
        config.structure.min_layers = 10
        errors, warnings = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []
        assert warnings == []

    def test_warns_when_requirement_exceeds_pool_capacity(self):
        """Warning when requirement * max_parallel_paths > pool_size."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "exit1", "zone": "chapel"},
                    {"fog_id": "exit2", "zone": "chapel"},
                ],
            )
        )
        pool.add(
            make_cluster(
                "boss_end",
                zones=["end_zone"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "e", "zone": "end_zone"}],
                exit_fogs=[],
            )
        )
        # Only 5 major_boss clusters
        for i in range(5):
            pool.add(
                make_cluster(
                    f"major_{i}",
                    zones=[f"major_{i}_z"],
                    cluster_type="major_boss",
                    weight=4,
                )
            )
        # Some other clusters for completeness
        for i in range(10):
            pool.add(
                make_cluster(
                    f"mini_{i}",
                    zones=[f"mini_{i}_z"],
                    cluster_type="mini_dungeon",
                    weight=3,
                )
            )

        config = Config()
        # Zero out other requirements to isolate major_boss warning
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 3  # 3 * 3 (default max_parallel) = 9 > 5
        config.structure.max_parallel_paths = 3
        errors, warnings = validate_config(config, pool, _boss_candidates(pool))
        assert any("major_boss" in w and "pool" in w.lower() for w in warnings)

    def test_no_warning_when_pool_sufficient(self):
        """No warning when pool can satisfy requirement * max_parallel_paths."""
        pool = make_cluster_pool()
        # Add extra major_boss so pool has 2 (maliketh + extra)
        pool.add(
            make_cluster(
                "extra_major",
                zones=["extra_major_z"],
                cluster_type="major_boss",
                weight=4,
            )
        )
        config = Config()
        config.requirements.major_bosses = 1
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.structure.max_parallel_paths = 2
        errors, warnings = validate_config(config, pool, _boss_candidates(pool))
        pool_warnings = [w for w in warnings if "pool" in w.lower()]
        assert pool_warnings == []

    def test_dead_end_major_boss_valid_as_final_boss(self):
        """A major_boss with 0 exits (pruned by passant filter) is valid as final_boss candidate."""
        pool = make_cluster_pool()
        # Add a dead-end boss (0 exits, like Placidusax)
        pool.add(
            make_cluster(
                "dead_end_boss",
                zones=["placidusax_zone"],
                cluster_type="major_boss",
                weight=4,
                entry_fogs=[{"fog_id": "f1", "zone": "placidusax_zone"}],
                exit_fogs=[],
            )
        )
        # Snapshot BEFORE filtering (like main.py does)
        boss_candidates = _boss_candidates(pool)
        pool.filter_passant_incompatible()

        config = Config()
        config.structure.final_boss_candidates = {"placidusax_zone": 1}
        errors, _ = validate_config(config, pool, boss_candidates)
        assert errors == []


# =============================================================================
# Weighted Final Boss Selection Tests
# =============================================================================


class TestSelectWeightedFinalBoss:
    """Tests for select_weighted_final_boss."""

    def _make_boss_cluster(self, zone: str) -> ClusterData:
        return ClusterData(
            id=zone,
            zones=[zone],
            type="major_boss",
            weight=1,
            entry_fogs=[{"fog_id": "f1", "zone": zone}],
            exit_fogs=[],
        )

    def test_weighted_distribution(self):
        """Higher weight produces proportionally more selections."""
        boss_a = self._make_boss_cluster("boss_a")
        boss_b = self._make_boss_cluster("boss_b")
        clusters = [boss_a, boss_b]
        candidates = {"boss_a": 5, "boss_b": 1}

        counts: dict[str, int] = {"boss_a": 0, "boss_b": 0}
        for seed in range(1000):
            rng = random.Random(seed)
            result = select_weighted_final_boss(candidates, clusters, set(), rng)
            counts[result.zones[0]] += 1

        # With 5:1 ratio over 1000 trials, boss_a should appear ~833 times.
        # Use a generous margin to avoid flaky tests.
        assert counts["boss_a"] > counts["boss_b"] * 2

    def test_skips_unavailable_zone(self):
        """Unavailable zone is skipped, next candidate selected."""
        boss_a = self._make_boss_cluster("boss_a")
        boss_b = self._make_boss_cluster("boss_b")
        clusters = [boss_a, boss_b]
        candidates = {"boss_a": 10, "boss_b": 1}
        used_zones = {"boss_a"}  # boss_a blocked

        rng = random.Random(42)
        result = select_weighted_final_boss(candidates, clusters, used_zones, rng)
        assert result.zones[0] == "boss_b"

    def test_all_unavailable_raises(self):
        """GenerationError raised when all candidates are blocked."""
        boss_a = self._make_boss_cluster("boss_a")
        clusters = [boss_a]
        candidates = {"boss_a": 1}
        used_zones = {"boss_a"}

        rng = random.Random(42)
        with pytest.raises(GenerationError):
            select_weighted_final_boss(candidates, clusters, used_zones, rng)


# =============================================================================
# Fog Gate Side Tests (fog_id, zone) pairs
# =============================================================================


class TestComputeNetExits:
    """Tests for compute_net_exits with (fog_id, zone) logic."""

    def test_consuming_entry_removes_same_side_exit(self):
        """Consuming entry from zone A removes exit from zone A."""
        cluster = make_cluster(
            "test",
            zones=["zone_a", "zone_b"],
            entry_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
                {"fog_id": "fog2", "zone": "zone_b"},
            ],
        )

        consumed = [{"fog_id": "fog1", "zone": "zone_a"}]
        remaining = compute_net_exits(cluster, consumed)

        assert len(remaining) == 1
        assert remaining[0]["fog_id"] == "fog2"

    def test_consuming_entry_preserves_opposite_side_exit(self):
        """Consuming entry from zone A does NOT remove exit from zone B with same fog_id."""
        cluster = make_cluster(
            "test",
            zones=["zone_a", "zone_b"],
            entry_fogs=[
                {"fog_id": "shared_fog", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "shared_fog", "zone": "zone_a"},
                {"fog_id": "shared_fog", "zone": "zone_b"},
            ],
        )

        # Consume entry from zone_a side
        consumed = [{"fog_id": "shared_fog", "zone": "zone_a"}]
        remaining = compute_net_exits(cluster, consumed)

        # Exit from zone_b should still be available
        assert len(remaining) == 1
        assert remaining[0]["fog_id"] == "shared_fog"
        assert remaining[0]["zone"] == "zone_b"

    def test_empty_consumed_returns_all_exits(self):
        """No consumed entries returns all exits."""
        cluster = make_cluster(
            "test",
            exit_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
                {"fog_id": "fog2", "zone": "zone_b"},
            ],
        )

        remaining = compute_net_exits(cluster, [])
        assert len(remaining) == 2


class TestCanBeNodeWithExcessExits:
    """Tests for can_be_*_node with excess exits (>= relaxation)."""

    def test_passant_with_excess_exits(self):
        """Cluster with 3 net exits qualifies as passant."""
        cluster = make_cluster(
            "big",
            entry_fogs=[{"fog_id": "entry1", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_b"},
                {"fog_id": "exit2", "zone": "zone_b"},
                {"fog_id": "exit3", "zone": "zone_b"},
            ],
        )
        assert can_be_passant_node(cluster) is True

    def test_split_with_excess_exits(self):
        """Cluster with 4 net exits qualifies as split(2)."""
        cluster = make_cluster(
            "big",
            entry_fogs=[{"fog_id": "entry1", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_b"},
                {"fog_id": "exit2", "zone": "zone_b"},
                {"fog_id": "exit3", "zone": "zone_b"},
                {"fog_id": "exit4", "zone": "zone_b"},
            ],
        )
        assert can_be_split_node(cluster, 2) is True

    def test_merge_with_excess_exits(self):
        """Cluster with 3 entries and 2 net exits qualifies as merge(3)."""
        cluster = make_cluster(
            "big",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "zone_a"},
                {"fog_id": "entry_b", "zone": "zone_b"},
                {"fog_id": "entry_c", "zone": "zone_c"},
            ],
            exit_fogs=[
                {"fog_id": "entry_a", "zone": "zone_a"},
                {"fog_id": "entry_b", "zone": "zone_b"},
                {"fog_id": "entry_c", "zone": "zone_c"},
                {"fog_id": "exit1", "zone": "zone_a"},
                {"fog_id": "exit2", "zone": "zone_b"},
            ],
        )
        assert can_be_merge_node(cluster, 3) is True


class TestCanBeSplitNodeEntryAsExit:
    """Tests for can_be_split_node with allow_entry_as_exit."""

    def test_boss_arena_split2_with_entry_as_exit(self):
        """boss_arena with 1 entry (bidir) + 2 exits, allow_entry_as_exit=True → split(2)."""
        # Without entry-as-exit: 1 entry bidir pair consumed → only 1 net exit → passant only
        # With entry-as-exit: entry doesn't consume pair → 2 exits available → split(2)
        cluster = make_cluster(
            "boss",
            cluster_type="boss_arena",
            entry_fogs=[{"fog_id": "boss_entry", "zone": "boss"}],
            exit_fogs=[
                {"fog_id": "boss_entry", "zone": "boss"},  # bidir pair
                {"fog_id": "boss_exit", "zone": "boss"},  # pure exit
            ],
            allow_entry_as_exit=True,
        )

        assert can_be_split_node(cluster, 2) is True

    def test_boss_arena_split2_without_entry_as_exit(self):
        """Same boss_arena with allow_entry_as_exit=False → NOT split(2)."""
        cluster = make_cluster(
            "boss",
            cluster_type="boss_arena",
            entry_fogs=[{"fog_id": "boss_entry", "zone": "boss"}],
            exit_fogs=[
                {"fog_id": "boss_entry", "zone": "boss"},
                {"fog_id": "boss_exit", "zone": "boss"},
            ],
            allow_entry_as_exit=False,
        )

        assert can_be_split_node(cluster, 2) is False

    def test_entry_as_exit_no_entries(self):
        """allow_entry_as_exit with no entries → not split-capable."""
        cluster = make_cluster(
            "boss",
            cluster_type="boss_arena",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "exit_a", "zone": "boss"},
                {"fog_id": "exit_b", "zone": "boss"},
            ],
            allow_entry_as_exit=True,
        )

        assert can_be_split_node(cluster, 2) is False


class TestCanBePassantNodeEntryAsExit:
    """Tests for can_be_passant_node with allow_entry_as_exit."""

    def test_boss_arena_passant_with_entry_as_exit(self):
        """boss_arena with 1 entry + 1 exit (bidir pair), allow_entry_as_exit=True → passant."""
        # Without entry-as-exit: bidir pair consumed → 0 net exits → dead end
        # With entry-as-exit: 1 exit available → passant
        cluster = make_cluster(
            "boss",
            cluster_type="boss_arena",
            entry_fogs=[{"fog_id": "boss_entry", "zone": "boss"}],
            exit_fogs=[{"fog_id": "boss_entry", "zone": "boss"}],
            allow_entry_as_exit=True,
        )

        assert can_be_passant_node(cluster) is True

    def test_boss_arena_passant_without_entry_as_exit(self):
        """Same cluster with allow_entry_as_exit=False → NOT passant (dead end)."""
        cluster = make_cluster(
            "boss",
            cluster_type="boss_arena",
            entry_fogs=[{"fog_id": "boss_entry", "zone": "boss"}],
            exit_fogs=[{"fog_id": "boss_entry", "zone": "boss"}],
            allow_entry_as_exit=False,
        )

        assert can_be_passant_node(cluster) is False


class TestCanBeMergeNodeSharedEntrance:
    """Tests for can_be_merge_node with allow_shared_entrance."""

    def test_shared_entrance_two_entries_one_exit(self):
        """With shared entrance, 2 entries + 1 exit qualifies for merge(2+)."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[{"fog_id": "exit_a", "zone": "merge_test"}],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is True
        assert can_be_merge_node(cluster, 3) is True  # fan-in doesn't matter

    def test_shared_entrance_needs_at_least_two_entries(self):
        """Shared entrance still requires 2+ entries (spec constraint)."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[{"fog_id": "entry_a", "zone": "merge_test"}],
            exit_fogs=[{"fog_id": "exit_a", "zone": "merge_test"}],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is False

    def test_shared_entrance_needs_at_least_one_exit(self):
        """With shared entrance, still needs at least 1 exit."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is False

    def test_shared_entrance_bidirectional_entry(self):
        """Shared entrance with bidirectional entry still qualifies."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "bidir", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[
                {"fog_id": "bidir", "zone": "merge_test"},
                {"fog_id": "exit_a", "zone": "merge_test"},
            ],
            allow_shared_entrance=True,
        )
        assert can_be_merge_node(cluster, 2) is True

    def test_without_shared_entrance_original_behavior(self):
        """Without shared entrance, original strict merge rules apply."""
        # 1 entry, 1 exit (bidirectional) — net exits = 0 → not merge(2)
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[{"fog_id": "bidir", "zone": "merge_test"}],
            exit_fogs=[{"fog_id": "bidir", "zone": "merge_test"}],
            allow_shared_entrance=False,
        )
        assert can_be_merge_node(cluster, 2) is False


class TestCountNetExits:
    """Tests for count_net_exits with (fog_id, zone) logic."""

    def test_bidirectional_same_zone_costs_one(self):
        """Entry that has same (fog_id, zone) in exits costs 1."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "bidir_fog", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "bidir_fog", "zone": "zone_a"},
                {"fog_id": "other_fog", "zone": "zone_a"},
            ],
        )

        # Consuming 1 entry removes 1 exit (the bidirectional one)
        net_exits = count_net_exits(cluster, 1)
        assert net_exits == 1

    def test_different_zone_same_fog_id_costs_zero(self):
        """Entry from zone A with fog in exits only in zone B costs 0."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "shared_fog", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "shared_fog", "zone": "zone_b"},
                {"fog_id": "other_fog", "zone": "zone_b"},
            ],
        )

        # Entry is from zone_a, but exits are in zone_b - no cost
        net_exits = count_net_exits(cluster, 1)
        assert net_exits == 2  # Both exits preserved


class TestSelectEntriesForMerge:
    """Tests for select_entries_for_merge returning dicts."""

    def test_returns_dicts_with_fog_id_and_zone(self):
        """Selected entries are dicts with fog_id and zone."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
                {"fog_id": "fog2", "zone": "zone_b"},
            ],
            exit_fogs=[],
        )

        rng = random.Random(42)
        entries = select_entries_for_merge(cluster, 2, rng)

        assert len(entries) == 2
        assert all(isinstance(e, dict) for e in entries)
        assert all("fog_id" in e and "zone" in e for e in entries)

    def test_prefers_non_bidirectional_entries(self):
        """Non-bidirectional entries (different zone from exits) are preferred."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "bidir", "zone": "zone_a"},  # same zone as exit
                {"fog_id": "non_bidir", "zone": "zone_b"},  # different zone
            ],
            exit_fogs=[
                {"fog_id": "bidir", "zone": "zone_a"},
            ],
        )

        rng = random.Random(42)
        entries = select_entries_for_merge(cluster, 1, rng)

        # Should pick non_bidir first since it doesn't consume an exit
        assert len(entries) == 1
        assert entries[0]["fog_id"] == "non_bidir"


class TestPickEntryWithMaxExits:
    """Tests for pick_entry_with_max_exits returning dicts."""

    def test_returns_dict_with_fog_id_and_zone(self):
        """Selected entry is a dict with fog_id and zone."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_a"},
            ],
        )

        rng = random.Random(42)
        entry = pick_entry_with_max_exits(cluster, 1, rng)

        assert entry is not None
        assert isinstance(entry, dict)
        assert entry["fog_id"] == "fog1"
        assert entry["zone"] == "zone_a"

    def test_picks_entry_preserving_opposite_side_exits(self):
        """Entry that preserves exits from other zones is valid."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "shared", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "shared", "zone": "zone_b"},  # Different zone
            ],
        )

        rng = random.Random(42)
        entry = pick_entry_with_max_exits(cluster, 1, rng)

        # Entry from zone_a should be valid since exit is in zone_b
        assert entry is not None
        assert entry["fog_id"] == "shared"

    def test_returns_none_when_no_valid_entry(self):
        """Returns None when no entry leaves enough exits."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
            ],
            exit_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},  # Will be consumed
            ],
        )

        rng = random.Random(42)
        entry = pick_entry_with_max_exits(cluster, 1, rng)

        # Entry consumes the only exit, so no valid entry for min_exits=1
        assert entry is None


class TestPickEntryAndExitsForNode:
    """Tests for _pick_entry_and_exits_for_node exit trimming."""

    def test_trims_excess_exits_to_min_exits(self):
        """Cluster with 4 exits returns exactly min_exits=1 exit."""
        cluster = make_cluster(
            "big",
            zones=["zone_a", "zone_b"],
            entry_fogs=[{"fog_id": "entry1", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_a"},
                {"fog_id": "exit2", "zone": "zone_b"},
                {"fog_id": "exit3", "zone": "zone_b"},
                {"fog_id": "exit4", "zone": "zone_b"},
            ],
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
        assert entry.fog_id == "entry1"
        assert len(exits) == 1

    def test_trims_exits_for_split(self):
        """Cluster with 5 exits returns exactly min_exits=2 for split."""
        cluster = make_cluster(
            "big",
            zones=["zone_a", "zone_b"],
            entry_fogs=[{"fog_id": "entry1", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_a"},
                {"fog_id": "exit2", "zone": "zone_b"},
                {"fog_id": "exit3", "zone": "zone_b"},
                {"fog_id": "exit4", "zone": "zone_b"},
                {"fog_id": "exit5", "zone": "zone_b"},
            ],
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 2, rng)
        assert len(exits) == 2

    def test_entry_as_exit_trims_exits(self):
        """Entry-as-exit cluster also trims excess exits."""
        cluster = make_cluster(
            "eae",
            zones=["zone_a"],
            entry_fogs=[{"fog_id": "shared", "zone": "zone_a", "main": True}],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_a"},
                {"fog_id": "exit2", "zone": "zone_a"},
                {"fog_id": "exit3", "zone": "zone_a"},
            ],
            allow_entry_as_exit=True,
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
        assert len(exits) == 1

    def test_entry_as_exit_prefers_non_entry_exit(self):
        """Entry-as-exit cluster prefers exits that differ from the consumed entry."""
        cluster = make_cluster(
            "boss",
            zones=["boss_zone"],
            entry_fogs=[
                {"fog_id": "gate_front", "zone": "boss_zone", "main": True},
            ],
            exit_fogs=[
                {"fog_id": "gate_front", "zone": "boss_zone"},  # same as entry
                {"fog_id": "gate_back", "zone": "boss_zone"},  # different
            ],
            allow_entry_as_exit=True,
        )
        # With min_exits=1, should always pick gate_back (preferred), never gate_front
        for seed in range(20):
            rng = random.Random(seed)
            entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
            assert entry.fog_id == "gate_front"
            assert len(exits) == 1
            assert exits[0].fog_id == "gate_back"

    def test_entry_as_exit_uses_entry_fog_for_split(self):
        """Entry-as-exit cluster uses entry fog as fallback when split needs it."""
        cluster = make_cluster(
            "boss",
            zones=["boss_zone"],
            entry_fogs=[
                {"fog_id": "gate_front", "zone": "boss_zone", "main": True},
            ],
            exit_fogs=[
                {"fog_id": "gate_front", "zone": "boss_zone"},
                {"fog_id": "gate_back", "zone": "boss_zone"},
            ],
            allow_entry_as_exit=True,
        )
        # With min_exits=2, must use both; preferred (gate_back) comes first
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 2, rng)
        assert entry.fog_id == "gate_front"
        assert len(exits) == 2
        assert exits[0].fog_id == "gate_back"
        assert exits[1].fog_id == "gate_front"

    def test_entry_as_exit_all_exits_match_entry(self):
        """When only exit matches entry, it is still used (no alternative)."""
        cluster = make_cluster(
            "boss",
            zones=["boss_zone"],
            entry_fogs=[{"fog_id": "only_gate", "zone": "boss_zone", "main": True}],
            exit_fogs=[{"fog_id": "only_gate", "zone": "boss_zone"}],
            allow_entry_as_exit=True,
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
        assert exits[0].fog_id == "only_gate"

    def test_exact_exits_not_trimmed(self):
        """Cluster with exactly min_exits returns all of them."""
        cluster = make_cluster(
            "exact",
            zones=["zone_a", "zone_b"],
            entry_fogs=[{"fog_id": "entry1", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "zone_b"},
            ],
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
        assert len(exits) == 1


class TestStableMainShuffle:
    """Tests for _stable_main_shuffle helper."""

    def test_main_entries_come_first(self):
        """Main-tagged entries appear before non-main entries."""
        entries = [
            {"fog_id": "a", "zone": "z1"},
            {"fog_id": "b", "zone": "z2", "main": True},
            {"fog_id": "c", "zone": "z3"},
        ]
        result = _stable_main_shuffle(entries, random.Random(42))
        assert result[0]["fog_id"] == "b"
        assert {e["fog_id"] for e in result[1:]} == {"a", "c"}

    def test_no_main_entries(self):
        """All entries returned when none are main-tagged."""
        entries = [
            {"fog_id": "a", "zone": "z1"},
            {"fog_id": "b", "zone": "z2"},
        ]
        result = _stable_main_shuffle(entries, random.Random(42))
        assert len(result) == 2

    def test_all_main_entries(self):
        """All main entries shuffled and returned."""
        entries = [
            {"fog_id": "a", "zone": "z1", "main": True},
            {"fog_id": "b", "zone": "z2", "main": True},
        ]
        result = _stable_main_shuffle(entries, random.Random(42))
        assert len(result) == 2
        assert all(e.get("main") for e in result)


class TestMainPreference:
    """Tests for main-tagged entry preference in selection functions."""

    def test_pick_entry_prefers_main(self):
        """pick_entry_with_max_exits prefers main-tagged entry."""
        cluster = make_cluster(
            "boss",
            entry_fogs=[
                {"fog_id": "back_gate", "zone": "boss_room"},
                {"fog_id": "main_gate", "zone": "boss_room", "main": True},
            ],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "boss_room"},
            ],
        )

        # Run many times to verify consistency
        for seed in range(20):
            entry = pick_entry_with_max_exits(cluster, 1, random.Random(seed))
            assert entry is not None
            assert entry["fog_id"] == "main_gate"

    def test_pick_entry_falls_back_when_no_main(self):
        """pick_entry_with_max_exits falls back to any valid entry."""
        cluster = make_cluster(
            "boss",
            entry_fogs=[
                {"fog_id": "gate_a", "zone": "boss_room"},
                {"fog_id": "gate_b", "zone": "boss_room"},
            ],
            exit_fogs=[
                {"fog_id": "exit1", "zone": "boss_room"},
            ],
        )

        entry = pick_entry_with_max_exits(cluster, 1, random.Random(42))
        assert entry is not None
        assert entry["fog_id"] in ("gate_a", "gate_b")

    def test_pick_entry_falls_back_when_main_violates_exit_constraint(self):
        """Falls back to non-main when main entry would consume the only exit."""
        cluster = make_cluster(
            "boss",
            entry_fogs=[
                # main entry is bidirectional and would consume the only exit
                {"fog_id": "main_gate", "zone": "boss_room", "main": True},
                # non-main entry doesn't consume any exit
                {"fog_id": "back_gate", "zone": "other_zone"},
            ],
            exit_fogs=[
                {"fog_id": "main_gate", "zone": "boss_room"},
            ],
        )

        entry = pick_entry_with_max_exits(cluster, 1, random.Random(42))
        assert entry is not None
        # main_gate consumes the only exit -> 0 remaining, below min_exits=1
        # back_gate preserves the exit -> 1 remaining, meets min_exits=1
        assert entry["fog_id"] == "back_gate"

    def test_select_entries_for_merge_prefers_main(self):
        """select_entries_for_merge prefers main within each group."""
        cluster = make_cluster(
            "boss",
            entry_fogs=[
                {"fog_id": "non_main", "zone": "z1"},
                {"fog_id": "main_entry", "zone": "z2", "main": True},
            ],
            exit_fogs=[],  # all non-bidir
        )

        # Selecting 1 should get the main one
        for seed in range(20):
            entries = select_entries_for_merge(cluster, 1, random.Random(seed))
            assert len(entries) == 1
            assert entries[0]["fog_id"] == "main_entry"


class TestClusterDataAvailableExits:
    """Tests for ClusterData.available_exits with (fog_id, zone) logic."""

    def test_removes_only_same_side_exit(self):
        """Using entry from zone A only removes exit from zone A."""
        cluster = make_cluster(
            "test",
            zones=["zone_a", "zone_b"],
            exit_fogs=[
                {"fog_id": "shared", "zone": "zone_a"},
                {"fog_id": "shared", "zone": "zone_b"},
            ],
        )

        used_entry = {"fog_id": "shared", "zone": "zone_a"}
        available = cluster.available_exits(used_entry)

        assert len(available) == 1
        assert available[0]["zone"] == "zone_b"

    def test_none_entry_returns_all_exits(self):
        """None entry returns all exits."""
        cluster = make_cluster(
            "test",
            exit_fogs=[
                {"fog_id": "fog1", "zone": "zone_a"},
                {"fog_id": "fog2", "zone": "zone_b"},
            ],
        )

        available = cluster.available_exits(None)
        assert len(available) == 2


# =============================================================================
# Merge guard tests
# =============================================================================


class TestMergeGuards:
    """Tests for _has_valid_merge_pair and _find_valid_merge_indices."""

    def test_merge_rejects_same_source_branches(self):
        """_find_valid_merge_indices returns None when all branches share the same node."""
        branches = [
            Branch("b0", "node_1_a", FogRef("exit_0", "z")),
            Branch("b1", "node_1_a", FogRef("exit_1", "z")),
        ]
        rng = random.Random(42)

        assert _has_valid_merge_pair(branches) is False
        assert _find_valid_merge_indices(branches, rng) is None

    def test_merge_selects_different_source_branches(self):
        """_find_valid_merge_indices selects branches with different current nodes."""
        branches = [
            Branch("b0", "node_1_a", FogRef("exit_0", "z")),
            Branch("b1", "node_1_a", FogRef("exit_1", "z")),
            Branch("b2", "node_1_b", FogRef("exit_2", "z")),
        ]
        rng = random.Random(42)

        assert _has_valid_merge_pair(branches) is True
        indices = _find_valid_merge_indices(branches, rng)

        assert len(indices) == 2
        # The selected pair must have different current nodes
        assert (
            branches[indices[0]].current_node_id != branches[indices[1]].current_node_id
        )

    def test_has_valid_merge_pair_all_different(self):
        """All branches from different nodes: valid merge possible."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z")),
            Branch("b1", "node_b", FogRef("exit_1", "z")),
        ]
        assert _has_valid_merge_pair(branches) is True

    def test_find_valid_merge_indices_randomness(self):
        """Different seeds select different valid pairs."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z")),
            Branch("b1", "node_b", FogRef("exit_1", "z")),
            Branch("b2", "node_c", FogRef("exit_2", "z")),
        ]

        selected_pairs = set()
        for seed in range(100):
            rng = random.Random(seed)
            indices = _find_valid_merge_indices(branches, rng)
            selected_pairs.add(tuple(sorted(indices)))

        # With 3 branches all from different nodes, there are 3 valid pairs
        assert len(selected_pairs) > 1

    def test_find_valid_merge_indices_ternary(self):
        """_find_valid_merge_indices with count=3 selects 3-way merges."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z")),
            Branch("b1", "node_b", FogRef("exit_1", "z")),
            Branch("b2", "node_c", FogRef("exit_2", "z")),
        ]
        rng = random.Random(42)
        indices = _find_valid_merge_indices(branches, rng, count=3)

        assert indices is not None
        assert len(indices) == 3
        # All 3 nodes are different, so the only combo is [0, 1, 2]
        assert sorted(indices) == [0, 1, 2]

    def test_find_valid_merge_indices_ternary_needs_two_parents(self):
        """3-way merge requires at least 2 distinct parent nodes."""
        # 3 branches but only 1 distinct parent → no valid 3-way merge
        branches = [
            Branch("b0", "same_node", FogRef("exit_0", "z")),
            Branch("b1", "same_node", FogRef("exit_1", "z")),
            Branch("b2", "same_node", FogRef("exit_2", "z")),
        ]
        rng = random.Random(42)
        indices = _find_valid_merge_indices(branches, rng, count=3)
        assert indices is None

    def test_find_valid_merge_indices_count_exceeds_branches(self):
        """count > len(branches) returns None."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z")),
            Branch("b1", "node_b", FogRef("exit_1", "z")),
        ]
        rng = random.Random(42)
        assert _find_valid_merge_indices(branches, rng, count=3) is None

    def test_has_valid_merge_pair_young_branches_excluded(self):
        """Branches younger than min_age are excluded from merge eligibility."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z"), birth_layer=5),
            Branch("b1", "node_b", FogRef("exit_1", "z"), birth_layer=5),
        ]
        # At layer 6, age=1 < min_age=3 → no valid merge
        assert _has_valid_merge_pair(branches, min_age=3, current_layer=6) is False
        # At layer 8, age=3 >= min_age=3 → valid merge
        assert _has_valid_merge_pair(branches, min_age=3, current_layer=8) is True

    def test_has_valid_merge_pair_mixed_ages(self):
        """Only age-eligible branches are considered for merging."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z"), birth_layer=1),  # old
            Branch("b1", "node_b", FogRef("exit_1", "z"), birth_layer=5),  # young
            Branch("b2", "node_c", FogRef("exit_2", "z"), birth_layer=1),  # old
        ]
        # At layer 4: b0 age=3, b1 age=-1(not eligible), b2 age=3 → valid (b0+b2)
        assert _has_valid_merge_pair(branches, min_age=3, current_layer=4) is True

    def test_find_valid_merge_indices_respects_min_age(self):
        """_find_valid_merge_indices only selects age-eligible branches."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z"), birth_layer=0),  # old
            Branch("b1", "node_b", FogRef("exit_1", "z"), birth_layer=5),  # young
            Branch("b2", "node_c", FogRef("exit_2", "z"), birth_layer=0),  # old
        ]
        rng = random.Random(42)
        # At layer 6: b0 age=6, b1 age=1(too young), b2 age=6
        indices = _find_valid_merge_indices(branches, rng, min_age=3, current_layer=6)
        assert indices is not None
        # b1 (index 1) should never be selected
        assert 1 not in indices
        assert sorted(indices) == [0, 2]

    def test_find_valid_merge_indices_all_too_young(self):
        """Returns None when all branches are too young."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z"), birth_layer=5),
            Branch("b1", "node_b", FogRef("exit_1", "z"), birth_layer=5),
        ]
        rng = random.Random(42)
        assert (
            _find_valid_merge_indices(branches, rng, min_age=3, current_layer=6) is None
        )

    def test_min_age_zero_matches_old_behavior(self):
        """min_age=0 makes all branches eligible regardless of birth_layer."""
        branches = [
            Branch("b0", "node_a", FogRef("exit_0", "z"), birth_layer=100),
            Branch("b1", "node_b", FogRef("exit_1", "z"), birth_layer=100),
        ]
        # current_layer=100, age=0, min_age=0 → eligible
        assert _has_valid_merge_pair(branches, min_age=0, current_layer=100) is True
        rng = random.Random(42)
        indices = _find_valid_merge_indices(branches, rng, min_age=0, current_layer=100)
        assert indices is not None

    def test_nary_split_and_merge_in_generated_dag(self):
        """With max_branches=3 and suitable clusters, DAGs produce 3-way splits/merges."""
        pool = ClusterPool()

        # Start cluster with 3 exits (enables 3-way initial split)
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "start_exit_1", "zone": "chapel"},
                    {"fog_id": "start_exit_2", "zone": "chapel"},
                    {"fog_id": "start_exit_3", "zone": "chapel"},
                ],
            )
        )

        # Final boss
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
                exit_fogs=[],
            )
        )

        # 3-entry merge clusters (3 entries, 1 net exit after merge)
        for i in range(5):
            pool.add(
                make_cluster(
                    f"merge3_{i}",
                    zones=[f"merge3_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"merge3_{i}_entry_a", "zone": f"merge3_{i}_zone"},
                        {"fog_id": f"merge3_{i}_entry_b", "zone": f"merge3_{i}_zone"},
                        {"fog_id": f"merge3_{i}_entry_c", "zone": f"merge3_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"merge3_{i}_exit", "zone": f"merge3_{i}_zone"},
                        {"fog_id": f"merge3_{i}_entry_a", "zone": f"merge3_{i}_zone"},
                        {"fog_id": f"merge3_{i}_entry_b", "zone": f"merge3_{i}_zone"},
                        {"fog_id": f"merge3_{i}_entry_c", "zone": f"merge3_{i}_zone"},
                    ],
                )
            )

        # 3-exit split clusters (1 entry, 3 net exits)
        for i in range(5):
            pool.add(
                make_cluster(
                    f"split3_{i}",
                    zones=[f"split3_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"split3_{i}_entry", "zone": f"split3_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"split3_{i}_exit_a", "zone": f"split3_{i}_zone"},
                        {"fog_id": f"split3_{i}_exit_b", "zone": f"split3_{i}_zone"},
                        {"fog_id": f"split3_{i}_exit_c", "zone": f"split3_{i}_zone"},
                    ],
                )
            )

        # Passant clusters covering all types used by pick_layer_type
        for i in range(10):
            pool.add(
                make_cluster(
                    f"mini_p_{i}",
                    zones=[f"mini_p_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"mini_p_{i}_entry", "zone": f"mini_p_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"mini_p_{i}_exit", "zone": f"mini_p_{i}_zone"},
                        {"fog_id": f"mini_p_{i}_entry", "zone": f"mini_p_{i}_zone"},
                    ],
                )
            )
        for i in range(5):
            pool.add(
                make_cluster(
                    f"boss_p_{i}",
                    zones=[f"boss_p_{i}_zone"],
                    cluster_type="boss_arena",
                    weight=3,
                    entry_fogs=[
                        {"fog_id": f"boss_p_{i}_entry", "zone": f"boss_p_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"boss_p_{i}_exit", "zone": f"boss_p_{i}_zone"},
                        {"fog_id": f"boss_p_{i}_entry", "zone": f"boss_p_{i}_zone"},
                    ],
                )
            )
        for i in range(3):
            pool.add(
                make_cluster(
                    f"legacy_p_{i}",
                    zones=[f"legacy_p_{i}_zone"],
                    cluster_type="legacy_dungeon",
                    weight=10,
                    entry_fogs=[
                        {"fog_id": f"legacy_p_{i}_entry", "zone": f"legacy_p_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"legacy_p_{i}_exit", "zone": f"legacy_p_{i}_zone"},
                        {"fog_id": f"legacy_p_{i}_entry", "zone": f"legacy_p_{i}_zone"},
                    ],
                )
            )

        config = Config()
        config.structure.min_layers = 8
        config.structure.max_layers = 14
        config.structure.max_branches = 3
        config.structure.max_parallel_paths = 4
        config.structure.split_probability = 0.6
        config.structure.merge_probability = 0.6
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        found_3way_split = False
        found_3way_merge = False

        for seed in range(1, 501):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
            except GenerationError:
                continue

            for node_id in dag.nodes:
                in_edges = [e for e in dag.edges if e.target_id == node_id]
                out_edges = [e for e in dag.edges if e.source_id == node_id]
                if len(out_edges) >= 3:
                    found_3way_split = True
                if len(in_edges) >= 3:
                    found_3way_merge = True

            if found_3way_split and found_3way_merge:
                break

        assert found_3way_split, "No 3-way split found in 500 seeds"
        assert found_3way_merge, "No 3-way merge found in 500 seeds"

    def test_no_duplicate_edges_in_generated_dag(self):
        """Generated DAGs with splits/merges have no duplicate (source, target) edges."""
        # Build a pool with split-, merge-, and passant-compatible clusters
        pool = ClusterPool()

        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "start_exit_1", "zone": "chapel"},
                    {"fog_id": "start_exit_2", "zone": "chapel"},
                ],
            )
        )

        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
                exit_fogs=[],
            )
        )

        # Split-compatible (1 entry bidir + 2 pure exits = 2 net exits)
        for i in range(8):
            pool.add(
                make_cluster(
                    f"split_{i}",
                    zones=[f"split_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"split_{i}_entry", "zone": f"split_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"split_{i}_entry", "zone": f"split_{i}_zone"},
                        {"fog_id": f"split_{i}_exit_a", "zone": f"split_{i}_zone"},
                        {"fog_id": f"split_{i}_exit_b", "zone": f"split_{i}_zone"},
                    ],
                )
            )

        # Merge-compatible (2 entries bidir + 1 pure exit = 1 net exit)
        for i in range(8):
            pool.add(
                make_cluster(
                    f"merge_{i}",
                    zones=[f"merge_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"merge_{i}_entry_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_entry_b", "zone": f"merge_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"merge_{i}_entry_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_entry_b", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_exit", "zone": f"merge_{i}_zone"},
                    ],
                )
            )

        # Passant-compatible (1 entry bidir + 1 pure exit = 1 net exit)
        for i in range(15):
            pool.add(
                make_cluster(
                    f"passant_{i}",
                    zones=[f"passant_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"passant_{i}_entry", "zone": f"passant_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"passant_{i}_entry", "zone": f"passant_{i}_zone"},
                        {"fog_id": f"passant_{i}_exit", "zone": f"passant_{i}_zone"},
                    ],
                )
            )

        config = Config()
        config.structure.min_layers = 4
        config.structure.max_layers = 6
        config.structure.max_branches = 2
        config.structure.split_probability = 0.4
        config.structure.merge_probability = 0.4
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 0

        successes = 0
        for seed in range(1, 201):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
            except GenerationError:
                continue
            successes += 1

            # Check for duplicate (source_id, target_id) pairs
            edge_pairs = [(e.source_id, e.target_id) for e in dag.edges]
            assert len(edge_pairs) == len(
                set(edge_pairs)
            ), f"Seed {seed}: duplicate edge pair found in {edge_pairs}"

        # Ensure we actually tested some successful generations
        assert successes >= 10, f"Only {successes} seeds succeeded out of 200"


# =============================================================================
# Shared entrance merge layer tests
# =============================================================================


class TestExecuteMergeLayerSharedEntrance:
    """Tests for execute_merge_layer with shared entrance clusters."""

    def _make_merge_pool(self):
        """Build a minimal pool where the ONLY merge-capable mini_dungeon
        is a shared-entrance cluster. This ensures deterministic testing."""
        pool = ClusterPool()

        # Source clusters (passant-capable, 1 entry + 2 exits with bidir)
        for i in range(2):
            pool.add(
                make_cluster(
                    f"src_{i}",
                    zones=[f"src_{i}_zone"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[{"fog_id": f"src_{i}_entry", "zone": f"src_{i}_zone"}],
                    exit_fogs=[
                        {"fog_id": f"src_{i}_exit", "zone": f"src_{i}_zone"},
                        {"fog_id": f"src_{i}_entry", "zone": f"src_{i}_zone"},
                    ],
                )
            )

        # The only merge-capable cluster (shared entrance: 2 entries + 1 exit)
        pool.add(
            make_cluster(
                "shared_merge",
                zones=["shared_merge_zone"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": "shared_entry_a", "zone": "shared_merge_zone"},
                    {"fog_id": "shared_entry_b", "zone": "shared_merge_zone"},
                ],
                exit_fogs=[
                    {"fog_id": "shared_exit", "zone": "shared_merge_zone"},
                ],
                allow_shared_entrance=True,
            )
        )

        return pool

    def test_shared_entrance_merge_creates_single_entry_node(self):
        """Shared entrance merge creates a node with 1 entry_fog, not N."""
        pool = self._make_merge_pool()
        dag = Dag(seed=42)

        # Create two source nodes from different parents
        src_a_cluster = pool.get_by_id("src_0")
        src_b_cluster = pool.get_by_id("src_1")
        src_a = DagNode(
            id="src_a",
            cluster=src_a_cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef("src_0_exit", "src_0_zone")],
        )
        src_b = DagNode(
            id="src_b",
            cluster=src_b_cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef("src_1_exit", "src_1_zone")],
        )
        dag.add_node(src_a)
        dag.add_node(src_b)

        branches = [
            Branch("a", "src_a", FogRef("src_0_exit", "src_0_zone")),
            Branch("b", "src_b", FogRef("src_1_exit", "src_1_zone")),
        ]

        rng = random.Random(42)
        config = Config()
        used_zones = {"src_0_zone", "src_1_zone"}

        result = execute_merge_layer(
            dag,
            branches,
            1,
            "mini_dungeon",
            pool,
            used_zones,
            rng,
            config,
        )

        # Find the merge node
        merge_nodes = [n for n in dag.nodes.values() if n.cluster.id == "shared_merge"]
        assert len(merge_nodes) == 1
        merge_node = merge_nodes[0]

        # Shared entrance: node has 1 entry_fog, not 2
        assert len(merge_node.entry_fogs) == 1

        # Both edges point to the same entry_fog
        merge_edges = [e for e in dag.edges if e.target_id == merge_node.id]
        assert len(merge_edges) == 2
        assert merge_edges[0].entry_fog == merge_edges[1].entry_fog

        # Result should have 1 branch (merged)
        assert len(result) == 1

    def test_shared_entrance_prefers_non_bidir_entry(self):
        """Shared entrance selects non-bidirectional entry to preserve exits.

        Regression test: if a random entry is chosen and it's bidirectional,
        it may consume all exits, leading to an empty exit_fogs list.
        """
        pool = ClusterPool()

        # Source clusters
        for i in range(2):
            pool.add(
                make_cluster(
                    f"src_{i}",
                    zones=[f"src_{i}_zone"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[{"fog_id": f"src_{i}_entry", "zone": f"src_{i}_zone"}],
                    exit_fogs=[
                        {"fog_id": f"src_{i}_exit", "zone": f"src_{i}_zone"},
                        {"fog_id": f"src_{i}_entry", "zone": f"src_{i}_zone"},
                    ],
                )
            )

        # Asymmetric shared entrance: entry_a is bidirectional (also an exit),
        # entry_b is not. The only exit is entry_a's pair. If entry_a is chosen,
        # exits become empty. select_entries_for_merge should prefer entry_b.
        pool.add(
            make_cluster(
                "asymmetric_merge",
                zones=["asym_zone"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": "bidir_fog", "zone": "asym_zone"},
                    {"fog_id": "nonbidir_fog", "zone": "asym_zone"},
                ],
                exit_fogs=[
                    {"fog_id": "bidir_fog", "zone": "asym_zone"},
                ],
                allow_shared_entrance=True,
            )
        )

        dag = Dag(seed=42)
        src_a_cluster = pool.get_by_id("src_0")
        src_b_cluster = pool.get_by_id("src_1")
        src_a = DagNode(
            id="src_a",
            cluster=src_a_cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef("src_0_exit", "src_0_zone")],
        )
        src_b = DagNode(
            id="src_b",
            cluster=src_b_cluster,
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef("src_1_exit", "src_1_zone")],
        )
        dag.add_node(src_a)
        dag.add_node(src_b)

        branches = [
            Branch("a", "src_a", FogRef("src_0_exit", "src_0_zone")),
            Branch("b", "src_b", FogRef("src_1_exit", "src_1_zone")),
        ]

        rng = random.Random(42)
        config = Config()
        used_zones = {"src_0_zone", "src_1_zone"}

        result = execute_merge_layer(
            dag, branches, 1, "mini_dungeon", pool, used_zones, rng, config
        )

        merge_nodes = [
            n for n in dag.nodes.values() if n.cluster.id == "asymmetric_merge"
        ]
        assert len(merge_nodes) == 1
        merge_node = merge_nodes[0]

        # Should have selected the non-bidirectional entry to preserve the exit
        assert merge_node.entry_fogs[0] == FogRef("nonbidir_fog", "asym_zone")
        # Exit preserved (bidir_fog not consumed)
        assert len(merge_node.exit_fogs) == 1
        assert len(result) == 1


# =============================================================================
# Roundtable merge into start
# =============================================================================


class TestMergeRoundtableIntoStart:
    """Tests for ClusterPool.merge_roundtable_into_start."""

    def _make_pool_with_roundtable(self) -> ClusterPool:
        """Create a pool with chapel_start and roundtable clusters."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel_start"],
                cluster_type="start",
                weight=1,
                entry_fogs=[
                    {"fog_id": "AEG099_001_9000", "zone": "chapel_start"},
                ],
                exit_fogs=[
                    {"fog_id": "AEG099_001_9000", "zone": "chapel_start"},
                ],
            )
        )
        pool.add(
            make_cluster(
                "roundtable_dacf",
                zones=["roundtable"],
                cluster_type="other",
                weight=4,
                entry_fogs=[
                    {"fog_id": "AEG099_231_9000", "zone": "roundtable"},
                ],
                exit_fogs=[
                    {"fog_id": "AEG099_231_9000", "zone": "roundtable"},
                ],
            )
        )
        return pool

    def test_merges_zones(self):
        """Start cluster gains roundtable zone after merge."""
        pool = self._make_pool_with_roundtable()
        pool.merge_roundtable_into_start()

        start = pool.get_by_type("start")[0]
        assert "roundtable" in start.zones
        assert "chapel_start" in start.zones

    def test_merges_exit_fogs(self):
        """Start cluster gains roundtable exit fog after merge."""
        pool = self._make_pool_with_roundtable()
        pool.merge_roundtable_into_start()

        start = pool.get_by_type("start")[0]
        exit_fog_ids = [f["fog_id"] for f in start.exit_fogs]
        assert "AEG099_001_9000" in exit_fog_ids
        assert "AEG099_231_9000" in exit_fog_ids

    def test_merges_entry_fogs(self):
        """Start cluster gains roundtable entry fog after merge."""
        pool = self._make_pool_with_roundtable()
        pool.merge_roundtable_into_start()

        start = pool.get_by_type("start")[0]
        entry_fog_ids = [f["fog_id"] for f in start.entry_fogs]
        assert "AEG099_231_9000" in entry_fog_ids

    def test_removes_roundtable_from_pool(self):
        """Roundtable cluster is removed from pool after merge."""
        pool = self._make_pool_with_roundtable()
        assert pool.get_by_id("roundtable_dacf") is not None

        pool.merge_roundtable_into_start()

        assert pool.get_by_id("roundtable_dacf") is None
        assert all("roundtable" not in c.zones for c in pool.get_by_type("other"))

    def test_noop_without_roundtable(self):
        """Merge is a no-op when there is no roundtable cluster."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel_start"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "exit1", "zone": "chapel_start"},
                ],
            )
        )

        pool.merge_roundtable_into_start()

        start = pool.get_by_type("start")[0]
        assert start.zones == ["chapel_start"]
        assert len(start.exit_fogs) == 1

    def test_noop_without_start(self):
        """Merge is a no-op when there is no start cluster."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "roundtable_dacf",
                zones=["roundtable"],
                cluster_type="other",
                weight=4,
            )
        )

        pool.merge_roundtable_into_start()

        # Roundtable should still be in the pool
        assert pool.get_by_id("roundtable_dacf") is not None


# =============================================================================
# Shared entrance simulation tests
# =============================================================================


def make_cluster_pool_with_shared_entrance() -> ClusterPool:
    """Create a cluster pool with shared-entrance merge-capable clusters.

    Includes clusters with 2 bidir entries + no pure exit, which:
    - Cannot be merge(2) in the old model (0 net exits after consuming 2)
    - CAN be merge(N) with shared entrance (2+ entries, 1+ exits)

    This pool demonstrates the key benefit of the fog reuse model.
    """
    pool = ClusterPool()

    # Start with 2 exits
    pool.add(
        make_cluster(
            "chapel_start",
            zones=["chapel"],
            cluster_type="start",
            weight=1,
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "start_exit_1", "zone": "chapel"},
                {"fog_id": "start_exit_2", "zone": "chapel"},
            ],
        )
    )

    # Final boss
    pool.add(
        make_cluster(
            "erdtree_boss",
            zones=["leyndell_erdtree"],
            cluster_type="final_boss",
            weight=5,
            entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
            exit_fogs=[],
        )
    )

    # Shared-entrance merge clusters: 2 bidir entries, no pure exit.
    # Old model: count_net_exits(2) = 0 → NOT merge(2).
    # Shared entrance: 2+ entries, 1+ exits → merge-capable.
    for i in range(5):
        pool.add(
            make_cluster(
                f"shared_{i}",
                zones=[f"shared_{i}_zone"],
                cluster_type="mini_dungeon",
                weight=5,
                entry_fogs=[
                    {"fog_id": f"shared_{i}_entry_a", "zone": f"shared_{i}_zone"},
                    {"fog_id": f"shared_{i}_entry_b", "zone": f"shared_{i}_zone"},
                ],
                exit_fogs=[
                    {"fog_id": f"shared_{i}_entry_a", "zone": f"shared_{i}_zone"},
                    {"fog_id": f"shared_{i}_entry_b", "zone": f"shared_{i}_zone"},
                ],
                allow_shared_entrance=True,
            )
        )

    # Passant clusters (1 entry bidir + 1 pure exit = 1 net exit)
    for i in range(25):
        pool.add(
            make_cluster(
                f"passant_{i}",
                zones=[f"passant_{i}_zone"],
                cluster_type="mini_dungeon",
                weight=5,
                entry_fogs=[
                    {"fog_id": f"passant_{i}_entry", "zone": f"passant_{i}_zone"},
                ],
                exit_fogs=[
                    {"fog_id": f"passant_{i}_entry", "zone": f"passant_{i}_zone"},
                    {"fog_id": f"passant_{i}_exit", "zone": f"passant_{i}_zone"},
                ],
            )
        )

    # Boss arenas (passant-capable)
    for i in range(6):
        pool.add(
            make_cluster(
                f"boss_{i}",
                zones=[f"boss_{i}_zone"],
                cluster_type="boss_arena",
                weight=3,
                entry_fogs=[
                    {"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"},
                ],
                exit_fogs=[
                    {"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"},
                    {"fog_id": f"boss_{i}_exit", "zone": f"boss_{i}_zone"},
                ],
            )
        )

    # Legacy dungeons (passant-capable)
    for i in range(3):
        pool.add(
            make_cluster(
                f"legacy_{i}",
                zones=[f"legacy_{i}_zone"],
                cluster_type="legacy_dungeon",
                weight=10,
                entry_fogs=[
                    {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"},
                ],
                exit_fogs=[
                    {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"},
                    {"fog_id": f"legacy_{i}_exit", "zone": f"legacy_{i}_zone"},
                ],
            )
        )

    return pool


class TestSharedEntranceSimulation:
    """Verify shared entrance merges work in full DAG generation."""

    def test_generation_succeeds_with_shared_entrance_clusters(self):
        """DAG generation succeeds when merge pool includes shared-entrance clusters."""
        pool = make_cluster_pool_with_shared_entrance()
        config = Config()
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.5
        config.structure.min_layers = 6
        config.structure.max_layers = 10
        # Relax requirements for the test pool
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 0

        # Run seeds — at least one should succeed with shared entrance merges
        success = False
        for seed in range(30):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
                assert len(dag.nodes) >= 3  # at least start + 1 node + end
                success = True
                break
            except GenerationError:
                continue
        assert success, "No seed produced a valid DAG with shared entrance merges"


class TestEntryAsExitSimulation:
    """Verify entry-as-exit boss arenas work in full DAG generation."""

    def _make_pool_with_entry_as_exit(self) -> ClusterPool:
        """Create a pool with entry-as-exit boss arenas."""
        pool = ClusterPool()

        # Start with 2 exits
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "start_exit_1", "zone": "chapel"},
                    {"fog_id": "start_exit_2", "zone": "chapel"},
                ],
            )
        )

        # Final boss
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
                exit_fogs=[],
            )
        )

        # Boss arenas with entry-as-exit (split-capable via the mechanism)
        for i in range(6):
            pool.add(
                make_cluster(
                    f"eae_boss_{i}",
                    zones=[f"eae_boss_{i}_zone"],
                    cluster_type="boss_arena",
                    weight=3,
                    entry_fogs=[
                        {"fog_id": f"eae_boss_{i}_entry", "zone": f"eae_boss_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"eae_boss_{i}_entry", "zone": f"eae_boss_{i}_zone"},
                        {"fog_id": f"eae_boss_{i}_exit", "zone": f"eae_boss_{i}_zone"},
                    ],
                    allow_entry_as_exit=True,
                )
            )

        # Merge-compatible boss arenas (2 entries + shared entrance)
        # Need enough to survive passant consumption (they're now also passant-eligible)
        for i in range(8):
            pool.add(
                make_cluster(
                    f"merge_boss_{i}",
                    zones=[f"merge_boss_{i}_zone"],
                    cluster_type="boss_arena",
                    weight=3,
                    entry_fogs=[
                        {
                            "fog_id": f"merge_boss_{i}_entry_a",
                            "zone": f"merge_boss_{i}_zone",
                        },
                        {
                            "fog_id": f"merge_boss_{i}_entry_b",
                            "zone": f"merge_boss_{i}_zone",
                        },
                    ],
                    exit_fogs=[
                        {
                            "fog_id": f"merge_boss_{i}_entry_a",
                            "zone": f"merge_boss_{i}_zone",
                        },
                        {
                            "fog_id": f"merge_boss_{i}_entry_b",
                            "zone": f"merge_boss_{i}_zone",
                        },
                        {
                            "fog_id": f"merge_boss_{i}_exit",
                            "zone": f"merge_boss_{i}_zone",
                        },
                    ],
                    allow_shared_entrance=True,
                )
            )

        # Mini dungeons (passant-capable)
        for i in range(10):
            pool.add(
                make_cluster(
                    f"mini_{i}",
                    zones=[f"mini_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"},
                        {"fog_id": f"mini_{i}_exit", "zone": f"mini_{i}_zone"},
                    ],
                )
            )

        # Merge-compatible clusters (shared entrance)
        # Need enough to survive passant consumption (they're now also passant-eligible)
        for i in range(10):
            pool.add(
                make_cluster(
                    f"merge_{i}",
                    zones=[f"merge_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"merge_{i}_entry_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_entry_b", "zone": f"merge_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"merge_{i}_entry_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_entry_b", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_exit", "zone": f"merge_{i}_zone"},
                    ],
                    allow_shared_entrance=True,
                )
            )

        return pool

    def test_generation_succeeds_with_entry_as_exit(self):
        """DAG generation succeeds with entry-as-exit boss arenas."""
        pool = self._make_pool_with_entry_as_exit()
        config = Config()
        config.structure.split_probability = 0.3
        config.structure.merge_probability = 0.3
        config.structure.min_layers = 6
        config.structure.max_layers = 10
        config.structure.max_branches = 2
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 2  # plan boss_arena layers
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 0

        success = False
        for seed in range(30):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
                assert len(dag.nodes) >= 3
                success = True
                break
            except GenerationError:
                continue
        assert success, "No seed produced a valid DAG with entry-as-exit arenas"

    def test_boss_arena_used_as_split_node(self):
        """At least some seeds produce a DAG where a boss_arena acts as split node."""
        pool = self._make_pool_with_entry_as_exit()
        config = Config()
        config.structure.split_probability = 1.0  # force splits
        config.structure.merge_probability = 0.3
        config.structure.min_layers = 4
        config.structure.max_layers = 6
        config.structure.max_branches = 2
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 3  # plan boss_arena layers so splits can use them
        config.requirements.mini_dungeons = 0

        boss_arena_split_count = 0
        for seed in range(50):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
                for node in dag.nodes.values():
                    if node.cluster.type == "boss_arena" and len(node.exit_fogs) >= 2:
                        # Count outgoing edges to verify it's actually a split
                        out_edges = [e for e in dag.edges if e.source_id == node.id]
                        if len(out_edges) >= 2:
                            boss_arena_split_count += 1
            except GenerationError:
                pass  # Some seeds may fail, that's OK

        assert (
            boss_arena_split_count > 0
        ), "No seeds produced a boss_arena split node (expected at least 1 in 50 seeds)"


# =============================================================================
# Cluster-first selection tests
# =============================================================================


class TestFilterPassantIncompatible:
    """Tests for ClusterPool.filter_passant_incompatible."""

    def test_removes_clusters_with_zero_net_exits(self):
        """Clusters with 1 bidir entry + 1 exit (0 net) are removed."""
        pool = ClusterPool()
        # Good: 1 entry + 2 exits (1 bidir + 1 pure) = 1 net exit
        pool.add(
            make_cluster(
                "good",
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": "f1", "zone": "z1"}],
                exit_fogs=[
                    {"fog_id": "f1", "zone": "z1"},
                    {"fog_id": "f2", "zone": "z1"},
                ],
            )
        )
        # Bad: 1 entry + 1 exit, same fog = 0 net exits
        pool.add(
            make_cluster(
                "bad",
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": "f1", "zone": "z1"}],
                exit_fogs=[{"fog_id": "f1", "zone": "z1"}],
            )
        )
        removed = pool.filter_passant_incompatible()
        assert len(pool.get_by_type("mini_dungeon")) == 1
        assert pool.get_by_type("mini_dungeon")[0].id == "good"
        assert len(removed) == 1
        assert removed[0].id == "bad"

    def test_keeps_entry_as_exit_clusters(self):
        """Clusters with allow_entry_as_exit are always passant-compatible."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "eax",
                cluster_type="boss_arena",
                entry_fogs=[{"fog_id": "f1", "zone": "z1"}],
                exit_fogs=[{"fog_id": "f1", "zone": "z1"}],
                allow_entry_as_exit=True,
            )
        )
        removed = pool.filter_passant_incompatible()
        assert len(pool.get_by_type("boss_arena")) == 1
        assert len(removed) == 0

    def test_skips_start_and_final_boss(self):
        """Start and final_boss clusters are never filtered."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "start",
                cluster_type="start",
                entry_fogs=[],
                exit_fogs=[{"fog_id": "f1", "zone": "z1"}],
            )
        )
        pool.add(
            make_cluster(
                "fb",
                cluster_type="final_boss",
                entry_fogs=[{"fog_id": "f1", "zone": "z1"}],
                exit_fogs=[],
            )
        )
        removed = pool.filter_passant_incompatible()
        assert len(pool.clusters) == 2
        assert len(removed) == 0

    def test_dead_end_major_boss_pruned_from_pool(self):
        """A major_boss with 0 exits is removed from the active pool."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "dead_end",
                cluster_type="major_boss",
                zones=["dead_zone"],
                entry_fogs=[{"fog_id": "f1", "zone": "dead_zone"}],
                exit_fogs=[],
            )
        )
        removed = pool.filter_passant_incompatible()
        assert len(removed) == 1
        assert removed[0].id == "dead_end"
        assert pool.get_by_type("major_boss") == []


class TestPickClusterUniform:
    """Tests for pick_cluster_uniform."""

    def test_picks_from_available(self):
        """Picks a cluster with no zone overlap."""
        c1 = make_cluster("c1", zones=["z1"])
        c2 = make_cluster("c2", zones=["z2"])
        result = pick_cluster_uniform([c1, c2], {"z1"}, random.Random(42))
        assert result is c2

    def test_returns_none_when_all_used(self):
        """Returns None when all zones overlap."""
        c1 = make_cluster("c1", zones=["z1"])
        result = pick_cluster_uniform([c1], {"z1"}, random.Random(42))
        assert result is None

    def test_uniform_distribution(self):
        """Selection is approximately uniform."""
        clusters = [make_cluster(f"c{i}", zones=[f"z{i}"]) for i in range(3)]
        counts = {c.id: 0 for c in clusters}
        for seed in range(3000):
            picked = pick_cluster_uniform(clusters, set(), random.Random(seed))
            counts[picked.id] += 1
        # Each should be roughly 1000 +/- 200
        for count in counts.values():
            assert 800 < count < 1200


class TestDetermineOperation:
    """Tests for determine_operation."""

    def test_passant_when_cluster_cant_split_or_merge(self):
        """Returns PASSANT when cluster has no split/merge capability."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "e1", "zone": "z1"},
                {"fog_id": "x1", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 1.0
        config.structure.merge_probability = 1.0
        branches = [Branch("b0", "start", FogRef("x", "z"))]
        op, fan = determine_operation(cluster, branches, config, random.Random(42))
        assert op == LayerOperation.PASSANT

    def test_split_when_cluster_can_split(self):
        """Returns SPLIT when cluster has 2+ exits and probability hits."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
                {"fog_id": "x3", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 1.0
        config.structure.max_branches = 3
        config.structure.max_parallel_paths = 3
        branches = [Branch("b0", "start", FogRef("x", "z"))]
        op, fan = determine_operation(cluster, branches, config, random.Random(42))
        assert op == LayerOperation.SPLIT
        assert fan >= 2

    def test_no_split_at_max_paths(self):
        """Never returns SPLIT when already at max_parallel_paths."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 1.0
        config.structure.merge_probability = 0.0
        config.structure.max_parallel_paths = 2
        branches = [
            Branch("b0", "n0", FogRef("x", "z")),
            Branch("b1", "n1", FogRef("y", "z")),
        ]
        op, fan = determine_operation(cluster, branches, config, random.Random(42))
        assert op == LayerOperation.PASSANT

    def test_merge_when_cluster_can_merge(self):
        """Returns MERGE when cluster has 2+ entries and valid merge pair."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[
                {"fog_id": "e1", "zone": "z1"},
                {"fog_id": "e2", "zone": "z1"},
            ],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
            ],
            allow_shared_entrance=True,
        )
        config = Config()
        config.structure.merge_probability = 1.0
        config.structure.split_probability = 0.0
        config.structure.max_branches = 2
        config.structure.max_parallel_paths = 3
        branches = [
            Branch("b0", "n0", FogRef("x", "z")),
            Branch("b1", "n1", FogRef("y", "z")),
        ]
        op, fan = determine_operation(cluster, branches, config, random.Random(42))
        assert op == LayerOperation.MERGE

    def test_both_split_and_merge_priority_cascade(self):
        """When split_prob + merge_prob > 1.0, acts as priority cascade."""
        # Cluster that can both split (3 exits) and merge (2 entries, shared)
        cluster = make_cluster(
            "c1",
            entry_fogs=[
                {"fog_id": "e1", "zone": "z1"},
                {"fog_id": "e2", "zone": "z1"},
            ],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
                {"fog_id": "x3", "zone": "z1"},
            ],
            allow_shared_entrance=True,
        )
        config = Config()
        config.structure.split_probability = 0.9
        config.structure.merge_probability = 0.5
        config.structure.max_branches = 3
        config.structure.max_parallel_paths = 4
        branches = [
            Branch("b0", "n0", FogRef("x", "z")),
            Branch("b1", "n1", FogRef("y", "z")),
        ]

        # Run many times and check distribution
        counts: dict[LayerOperation, int] = {
            LayerOperation.SPLIT: 0,
            LayerOperation.MERGE: 0,
            LayerOperation.PASSANT: 0,
        }
        for seed in range(1000):
            op, _fan = determine_operation(
                cluster, branches, config, random.Random(seed)
            )
            counts[op] += 1

        # With 0.9 + 0.5 = 1.4, passant should never be reached
        assert counts[LayerOperation.PASSANT] == 0
        # Split should get ~90%, merge ~10%
        assert counts[LayerOperation.SPLIT] > 800
        assert counts[LayerOperation.MERGE] > 50

    def test_forced_split_via_spacing_threshold(self):
        """max_branch_spacing triggers forced SPLIT when not saturated."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
                {"fog_id": "x3", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 0.0  # Would normally never split
        config.structure.max_parallel_paths = 3
        config.structure.max_branch_spacing = 4
        # 1 branch with stale counter exceeding threshold, not saturated
        branches = [
            Branch("b0", "start", FogRef("x", "z"), layers_since_last_split=5),
        ]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
        )
        assert op == LayerOperation.SPLIT
        assert fan >= 2

    def test_forced_split_fallback_when_cant_split(self):
        """Forced split falls back to PASSANT when cluster can't split."""
        # Cluster with 1 exit -- can't split
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
        )
        config = Config()
        config.structure.split_probability = 0.0
        config.structure.max_branch_spacing = 4
        branches = [
            Branch("b0", "start", FogRef("x", "z"), layers_since_last_split=5),
        ]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
        )
        assert op == LayerOperation.PASSANT

    def test_prefer_merge_overrides_probability(self):
        """prefer_merge=True bypasses probability roll for MERGE."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[
                {"fog_id": "e1", "zone": "z1"},
                {"fog_id": "e2", "zone": "z1"},
            ],
            exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
            allow_shared_entrance=True,
        )
        config = Config()
        config.structure.merge_probability = 0.0  # Would normally never merge
        config.structure.max_parallel_paths = 3
        branches = [
            Branch("b0", "n0", FogRef("x", "z")),
            Branch("b1", "n1", FogRef("y", "z")),
        ]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
            prefer_merge=True,
        )
        assert op == LayerOperation.MERGE

    def test_force_none_uses_normal_logic(self):
        """force=None (default) uses normal probability logic for multi-branch."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
                {"fog_id": "x3", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 0.0  # Never split
        config.structure.merge_probability = 0.0
        config.structure.max_parallel_paths = 3
        # Need 2+ branches to test probability roll (single branch forces split)
        branches = [
            Branch("b0", "n0", FogRef("x", "z")),
            Branch("b1", "n1", FogRef("y", "z")),
        ]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
        )
        assert op == LayerOperation.PASSANT

    def test_single_branch_forces_split(self):
        """Single branch outside convergence forces SPLIT when cluster can split."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
                {"fog_id": "x3", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 0.0  # Would normally never split
        config.structure.max_parallel_paths = 3
        branches = [Branch("b0", "start", FogRef("x", "z"))]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
        )
        assert op == LayerOperation.SPLIT
        assert fan >= 2

    def test_single_branch_no_force_during_convergence(self):
        """Single branch during convergence (prefer_merge) does not force split."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z1"},
                {"fog_id": "x2", "zone": "z1"},
                {"fog_id": "x3", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 0.0
        config.structure.max_parallel_paths = 3
        branches = [Branch("b0", "start", FogRef("x", "z"))]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
            prefer_merge=True,
        )
        assert op == LayerOperation.PASSANT

    def test_single_branch_passant_when_cant_split(self):
        """Single branch falls back to PASSANT when cluster can't split."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "e1", "zone": "z1"},
                {"fog_id": "x1", "zone": "z1"},
            ],
        )
        config = Config()
        config.structure.split_probability = 0.0
        config.structure.max_parallel_paths = 3
        branches = [Branch("b0", "start", FogRef("x", "z"))]
        op, fan = determine_operation(
            cluster,
            branches,
            config,
            random.Random(42),
        )
        assert op == LayerOperation.PASSANT


# =============================================================================
# Prerequisite injection tests
# =============================================================================


class TestInjectPrerequisite:
    """Tests for _inject_prerequisite function."""

    def test_no_prerequisite_is_noop(self):
        """No prerequisite → returns branches and layer unchanged."""
        pool = make_cluster_pool()
        dag = Dag(seed=1)
        dag.add_node(
            DagNode(
                id="merge_node",
                cluster=pool.get_by_type("mini_dungeon")[0],
                layer=5,
                tier=10,
                entry_fogs=[FogRef("e", "z")],
                exit_fogs=[FogRef("x", "z")],
            )
        )
        branches = [Branch("b0", "merge_node", FogRef("x", "z"))]

        # pcr_boss has no requires
        end_cluster = pool.get_by_id("pcr_boss")
        assert end_cluster is not None

        result_branches, result_layer = _inject_prerequisite(
            dag, branches, 6, end_cluster, pool, set(), random.Random(42)
        )
        assert result_branches is branches
        assert result_layer == 6

    def test_prerequisite_injects_node(self):
        """Prerequisite cluster is injected as passant node."""
        pool = make_cluster_pool()
        dag = Dag(seed=1)
        dag.add_node(
            DagNode(
                id="merge_node",
                cluster=pool.get_by_type("mini_dungeon")[0],
                layer=5,
                tier=10,
                entry_fogs=[FogRef("e", "z")],
                exit_fogs=[FogRef("x", "z")],
            )
        )
        branches = [Branch("b0", "merge_node", FogRef("x", "z"))]

        end_cluster = pool.get_by_id("erdtree_boss")
        assert end_cluster is not None
        assert end_cluster.requires == "farumazula_maliketh"

        result_branches, result_layer = _inject_prerequisite(
            dag, branches, 6, end_cluster, pool, set(), random.Random(42)
        )

        # New node should be added
        assert "node_6_a" in dag.nodes
        prereq_node = dag.nodes["node_6_a"]
        assert "farumazula_maliketh" in prereq_node.cluster.zones

        # Branch updated
        assert len(result_branches) == 1
        assert result_branches[0].current_node_id == "node_6_a"
        assert result_layer == 7

        # Edge from merge_node to prereq
        assert any(
            e.source_id == "merge_node" and e.target_id == "node_6_a" for e in dag.edges
        )

    def test_prerequisite_unavailable_raises(self):
        """Raises GenerationError if prerequisite zones already used."""
        pool = make_cluster_pool()
        dag = Dag(seed=1)
        dag.add_node(
            DagNode(
                id="merge_node",
                cluster=pool.get_by_type("mini_dungeon")[0],
                layer=5,
                tier=10,
                entry_fogs=[FogRef("e", "z")],
                exit_fogs=[FogRef("x", "z")],
            )
        )
        branches = [Branch("b0", "merge_node", FogRef("x", "z"))]

        end_cluster = pool.get_by_id("erdtree_boss")
        assert end_cluster is not None

        # Mark maliketh zones as used
        used_zones = {"farumazula_maliketh"}

        with pytest.raises(GenerationError, match="Prerequisite cluster not available"):
            _inject_prerequisite(
                dag,
                branches,
                6,
                end_cluster,
                pool,
                used_zones,
                random.Random(42),
            )


class TestPrerequisiteInGenerateDag:
    """Tests for prerequisite behavior in full generate_dag."""

    def test_erdtree_boss_has_maliketh_on_path(self):
        """When leyndell_erdtree is final boss, Maliketh appears on mandatory path."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 42
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        config.structure.final_boss_candidates = {"leyndell_erdtree": 1}

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        # Maliketh should exist in the DAG and be an immediate predecessor of end
        maliketh_nodes = [
            nid for nid, n in dag.nodes.items() if n.cluster.id == "maliketh"
        ]
        assert len(maliketh_nodes) >= 1, "Maliketh not found in DAG"

        # Maliketh should have an edge to end
        end_incoming = dag.get_incoming_edges("end")
        end_sources = {e.source_id for e in end_incoming}
        assert any(
            mid in end_sources for mid in maliketh_nodes
        ), "Maliketh is not an immediate predecessor of end"

    def test_maliketh_not_randomly_placed_when_reserved(self):
        """Maliketh zones are reserved and not used for intermediate layers."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.min_layers = 3
        config.structure.max_layers = 5
        config.structure.max_branches = 1
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        config.structure.final_boss_candidates = {"leyndell_erdtree": 1}

        # Run many seeds to check Maliketh is never on an optional branch
        for seed in range(50):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
            except GenerationError:
                continue

            # Verify end is erdtree
            end_node = dag.nodes["end"]
            if "leyndell_erdtree" not in end_node.cluster.zones:
                continue

            # Maliketh should appear exactly once: as the prereq node
            maliketh_nodes = [
                nid
                for nid, n in dag.nodes.items()
                if "farumazula_maliketh" in n.cluster.zones
            ]
            assert len(maliketh_nodes) == 1
            # And it should be the immediate predecessor of end
            end_incoming = dag.get_incoming_edges("end")
            end_sources = {e.source_id for e in end_incoming}
            assert maliketh_nodes[0] in end_sources

    def test_no_prerequisite_for_pcr_boss(self):
        """PCR boss (no requires) doesn't inject any prerequisite."""
        pool = make_cluster_pool()
        config = Config()
        config.seed = 42
        config.structure.min_layers = 3
        config.structure.max_layers = 3
        config.structure.max_branches = 1
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0
        config.structure.final_boss_candidates = {"enirilim_radahn": 1}

        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )

        # Maliketh should NOT be on the path (it's available as major_boss
        # but not required)
        end_node = dag.nodes["end"]
        assert "enirilim_radahn" in end_node.cluster.zones

        # Basic structural check — all nodes reachable, all can reach end
        errors = dag.validate_structure()
        assert not errors, f"DAG structure errors: {errors}"


class TestPickClusterUniformReservedZones:
    """Tests for reserved_zones parameter in pick_cluster_uniform."""

    def test_reserved_zones_excluded(self):
        """Clusters with reserved zones are not picked."""
        c1 = make_cluster("c1", zones=["z1"])
        c2 = make_cluster("c2", zones=["z2"])
        c3 = make_cluster("c3", zones=["z3"])
        candidates = [c1, c2, c3]

        # Reserve z2 — c2 should never be picked
        for _ in range(100):
            result = pick_cluster_uniform(
                candidates,
                set(),
                random.Random(_),
                reserved_zones=frozenset(["z2"]),
            )
            assert result is not None
            assert result.id != "c2"

    def test_reserved_and_used_both_excluded(self):
        """Both used_zones and reserved_zones are excluded."""
        c1 = make_cluster("c1", zones=["z1"])
        c2 = make_cluster("c2", zones=["z2"])
        c3 = make_cluster("c3", zones=["z3"])
        candidates = [c1, c2, c3]

        result = pick_cluster_uniform(
            candidates,
            {"z1"},
            random.Random(42),
            reserved_zones=frozenset(["z2"]),
        )
        assert result is not None
        assert result.id == "c3"

    def test_all_reserved_returns_none(self):
        """Returns None when all clusters have reserved zones."""
        c1 = make_cluster("c1", zones=["z1"])
        candidates = [c1]

        result = pick_cluster_uniform(
            candidates,
            set(),
            random.Random(42),
            reserved_zones=frozenset(["z1"]),
        )
        assert result is None


# =============================================================================
# Proximity Groups tests
# =============================================================================


class TestFogMatchesSpec:
    """Tests for fog_matches_spec helper."""

    def test_unqualified_matches_any_zone(self):
        assert fog_matches_spec("fog_A", "zone_x", "fog_A") is True
        assert fog_matches_spec("fog_A", "zone_y", "fog_A") is True

    def test_unqualified_no_match(self):
        assert fog_matches_spec("fog_A", "zone_x", "fog_B") is False

    def test_qualified_matches_exact(self):
        assert fog_matches_spec("fog_A", "zone_x", "zone_x:fog_A") is True

    def test_qualified_wrong_zone(self):
        assert fog_matches_spec("fog_A", "zone_x", "zone_y:fog_A") is False

    def test_qualified_wrong_fog(self):
        assert fog_matches_spec("fog_A", "zone_x", "zone_x:fog_B") is False


class TestFilterExitsByProximity:
    """Tests for _filter_exits_by_proximity."""

    def test_no_groups_returns_all(self):
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z"}],
            exit_fogs=[{"fog_id": "x1", "zone": "z"}, {"fog_id": "x2", "zone": "z"}],
        )
        entry = {"fog_id": "e1", "zone": "z"}
        result = _filter_exits_by_proximity(cluster, entry, cluster.exit_fogs)
        assert len(result) == 2

    def test_entry_in_group_blocks_exit(self):
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "fog_A", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "z"},
                {"fog_id": "fog_C", "zone": "z"},
            ],
            proximity_groups=[["fog_A", "fog_B"]],
        )
        entry = {"fog_id": "fog_A", "zone": "z"}
        result = _filter_exits_by_proximity(cluster, entry, cluster.exit_fogs)
        assert len(result) == 1
        assert result[0]["fog_id"] == "fog_C"

    def test_entry_not_in_any_group(self):
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "fog_D", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "fog_A", "zone": "z"},
                {"fog_id": "fog_B", "zone": "z"},
            ],
            proximity_groups=[["fog_A", "fog_B"]],
        )
        entry = {"fog_id": "fog_D", "zone": "z"}
        result = _filter_exits_by_proximity(cluster, entry, cluster.exit_fogs)
        assert len(result) == 2

    def test_zone_qualified_spec(self):
        """Zone-qualified spec only blocks matching zone."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "fog_A", "zone": "z1"}],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "z1"},
                {"fog_id": "fog_B", "zone": "z2"},
            ],
            proximity_groups=[["z1:fog_A", "z1:fog_B"]],
        )
        entry = {"fog_id": "fog_A", "zone": "z1"}
        result = _filter_exits_by_proximity(cluster, entry, cluster.exit_fogs)
        # fog_B in z1 blocked, fog_B in z2 not blocked
        assert len(result) == 1
        assert result[0]["zone"] == "z2"

    def test_three_fogs_in_group(self):
        """All three fogs in a group block each other."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "fog_A", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "fog_B", "zone": "z"},
                {"fog_id": "fog_C", "zone": "z"},
                {"fog_id": "fog_D", "zone": "z"},
            ],
            proximity_groups=[["fog_A", "fog_B", "fog_C"]],
        )
        entry = {"fog_id": "fog_A", "zone": "z"}
        result = _filter_exits_by_proximity(cluster, entry, cluster.exit_fogs)
        assert len(result) == 1
        assert result[0]["fog_id"] == "fog_D"


class TestProximityGroups:
    """Tests for proximity_groups integration in capacity checks and fog picking."""

    def test_count_net_exits_reduced_by_proximity(self):
        """Proximity groups reduce counted exits."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
            ],
            proximity_groups=[["e1", "x1"]],
        )
        # Without proximity: 2 exits (e1 not bidirectional with x1/x2)
        # With proximity: e1 blocks x1, so worst case = 1
        assert count_net_exits(cluster, 1) == 1

    def test_can_be_passant_with_proximity(self):
        """Passant still works if at least 1 exit survives proximity."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
            ],
            proximity_groups=[["e1", "x1"]],
        )
        assert can_be_passant_node(cluster) is True

    def test_cannot_be_passant_all_exits_blocked(self):
        """Passant fails if all exits are in proximity group with entry."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z"}],
            exit_fogs=[{"fog_id": "x1", "zone": "z"}],
            proximity_groups=[["e1", "x1"]],
        )
        assert can_be_passant_node(cluster) is False

    def test_count_net_exits_multi_entry_proximity(self):
        """Multi-entry proximity: both entries block different exits."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[
                {"fog_id": "e1", "zone": "z"},
                {"fog_id": "e2", "zone": "z"},
            ],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
                {"fog_id": "x3", "zone": "z"},
            ],
            proximity_groups=[["e1", "x1"], ["e2", "x2"]],
        )
        # With 2 entries: e1 blocks x1, e2 blocks x2, only x3 survives
        assert count_net_exits(cluster, 2) == 1

    def test_merge_blocked_by_proximity(self):
        """Merge fails if proximity blocks all exits for some entry combo."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[
                {"fog_id": "e1", "zone": "z"},
                {"fog_id": "e2", "zone": "z"},
            ],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
            ],
            proximity_groups=[["e1", "x1"], ["e2", "x1"]],
        )
        # Both entries block the only exit → can't merge
        assert can_be_merge_node(cluster, 2) is False

    def test_pick_entry_with_max_exits_respects_proximity(self):
        """pick_entry_with_max_exits skips entries where proximity blocks exits."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[
                {"fog_id": "e1", "zone": "z"},
                {"fog_id": "e2", "zone": "z"},
            ],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
            ],
            proximity_groups=[["e1", "x1", "x2"]],
        )
        rng = random.Random(42)
        # e1 blocks both exits; e2 blocks neither
        result = pick_entry_with_max_exits(cluster, 2, rng)
        assert result is not None
        assert result["fog_id"] == "e2"

    def test_pick_entry_and_exits_filters_proximity(self):
        """_pick_entry_and_exits_for_node excludes proximity-blocked exits."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
            ],
            proximity_groups=[["e1", "x1"]],
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
        assert entry.fog_id == "e1"
        assert len(exits) == 1
        assert exits[0].fog_id == "x2"

    def test_entry_as_exit_proximity_partial_block(self):
        """Entry-as-exit: proximity blocks some but not all exits."""
        cluster = make_cluster(
            "c1",
            entry_fogs=[{"fog_id": "e1", "zone": "z"}],
            exit_fogs=[
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
                {"fog_id": "e1", "zone": "z"},
            ],
            allow_entry_as_exit=True,
            proximity_groups=[["e1", "x1"]],
        )
        rng = random.Random(42)
        entry, exits = _pick_entry_and_exits_for_node(cluster, 1, rng)
        assert entry.fog_id == "e1"
        # x1 and e1 blocked by proximity with e1; only x2 survives
        assert len(exits) == 1
        assert exits[0].fog_id == "x2"


class TestAllowedEntriesExits:
    """Tests for allowed_entries/allowed_exits load-time filtering."""

    def test_allowed_entries_filters_at_load(self):
        """Only specified entries survive from_dict."""
        data = {
            "id": "c1",
            "zones": ["z"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [
                {"fog_id": "e1", "zone": "z"},
                {"fog_id": "e2", "zone": "z"},
                {"fog_id": "e3", "zone": "z"},
            ],
            "exit_fogs": [{"fog_id": "x1", "zone": "z"}],
            "allowed_entries": ["e2"],
        }
        cluster = ClusterData.from_dict(data)
        assert len(cluster.entry_fogs) == 1
        assert cluster.entry_fogs[0]["fog_id"] == "e2"

    def test_allowed_exits_filters_at_load(self):
        """Only specified exits survive from_dict."""
        data = {
            "id": "c1",
            "zones": ["z"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [{"fog_id": "e1", "zone": "z"}],
            "exit_fogs": [
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
                {"fog_id": "x3", "zone": "z"},
            ],
            "allowed_exits": ["x1", "x3"],
        }
        cluster = ClusterData.from_dict(data)
        assert len(cluster.exit_fogs) == 2
        fog_ids = {f["fog_id"] for f in cluster.exit_fogs}
        assert fog_ids == {"x1", "x3"}

    def test_zone_qualified_allowed_entries(self):
        """Zone-qualified specifiers match only the correct zone."""
        data = {
            "id": "c1",
            "zones": ["z1", "z2"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [
                {"fog_id": "e1", "zone": "z1"},
                {"fog_id": "e1", "zone": "z2"},
            ],
            "exit_fogs": [{"fog_id": "x1", "zone": "z1"}],
            "allowed_entries": ["z2:e1"],
        }
        cluster = ClusterData.from_dict(data)
        assert len(cluster.entry_fogs) == 1
        assert cluster.entry_fogs[0]["zone"] == "z2"

    def test_no_allowed_entries_keeps_all(self):
        """Without allowed_entries, all entries are kept."""
        data = {
            "id": "c1",
            "zones": ["z"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [
                {"fog_id": "e1", "zone": "z"},
                {"fog_id": "e2", "zone": "z"},
            ],
            "exit_fogs": [{"fog_id": "x1", "zone": "z"}],
        }
        cluster = ClusterData.from_dict(data)
        assert len(cluster.entry_fogs) == 2

    def test_allowed_entries_affects_capacity(self):
        """Filtering entries at load time reduces merge capacity."""
        data = {
            "id": "c1",
            "zones": ["z"],
            "type": "mini_dungeon",
            "weight": 5,
            "entry_fogs": [
                {"fog_id": "e1", "zone": "z"},
                {"fog_id": "e2", "zone": "z"},
                {"fog_id": "e3", "zone": "z"},
            ],
            "exit_fogs": [
                {"fog_id": "x1", "zone": "z"},
                {"fog_id": "x2", "zone": "z"},
            ],
            "allowed_entries": ["e1"],
        }
        cluster = ClusterData.from_dict(data)
        # Only 1 entry → can't merge (needs 2)
        assert can_be_merge_node(cluster, 2) is False
        assert can_be_passant_node(cluster) is True


# =============================================================================
# Cross-link pipeline tests
# =============================================================================


class TestCrosslinkPipeline:
    """Tests for cross-link integration in generate_dag."""

    def _make_pool_with_surplus_entries(self) -> ClusterPool:
        """Create a pool where clusters have surplus entry fogs.

        This enables cross-link generation since surplus entries on
        targets and surplus exits on sources are both needed.
        """
        pool = ClusterPool()

        # Start cluster with 2 exits
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": "start_exit_1", "zone": "chapel"},
                    {"fog_id": "start_exit_2", "zone": "chapel"},
                ],
            )
        )

        # Final boss
        pool.add(
            make_cluster(
                "pcr_boss",
                zones=["enirilim_radahn"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[
                    {"fog_id": "pcr_entry", "zone": "enirilim_radahn"},
                ],
                exit_fogs=[],
            )
        )

        # Mini dungeons with 2 entries and 2 exits (surplus after 1 consumed)
        for i in range(10):
            pool.add(
                make_cluster(
                    f"mini_{i}",
                    zones=[f"mini_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=5,
                    entry_fogs=[
                        {"fog_id": f"mini_{i}_entry", "zone": f"mini_{i}_zone"},
                        {"fog_id": f"mini_{i}_entry2", "zone": f"mini_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"mini_{i}_exit", "zone": f"mini_{i}_zone"},
                        {"fog_id": f"mini_{i}_exit2", "zone": f"mini_{i}_zone"},
                    ],
                )
            )

        # Boss arenas with 2 entries and 2 exits
        for i in range(6):
            pool.add(
                make_cluster(
                    f"boss_{i}",
                    zones=[f"boss_{i}_zone"],
                    cluster_type="boss_arena",
                    weight=3,
                    entry_fogs=[
                        {"fog_id": f"boss_{i}_entry", "zone": f"boss_{i}_zone"},
                        {"fog_id": f"boss_{i}_entry2", "zone": f"boss_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"boss_{i}_exit", "zone": f"boss_{i}_zone"},
                        {"fog_id": f"boss_{i}_exit2", "zone": f"boss_{i}_zone"},
                    ],
                )
            )

        # Legacy dungeons
        for i in range(3):
            pool.add(
                make_cluster(
                    f"legacy_{i}",
                    zones=[f"legacy_{i}_zone"],
                    cluster_type="legacy_dungeon",
                    weight=10,
                    entry_fogs=[
                        {"fog_id": f"legacy_{i}_entry", "zone": f"legacy_{i}_zone"},
                        {"fog_id": f"legacy_{i}_entry2", "zone": f"legacy_{i}_zone"},
                    ],
                    exit_fogs=[
                        {"fog_id": f"legacy_{i}_exit", "zone": f"legacy_{i}_zone"},
                        {"fog_id": f"legacy_{i}_exit2", "zone": f"legacy_{i}_zone"},
                    ],
                )
            )

        return pool

    def test_crosslinks_applied_when_configured(self):
        """generate_dag adds cross-links when crosslinks=True."""
        pool = self._make_pool_with_surplus_entries()
        config = Config.from_dict(
            {
                "requirements": {"major_bosses": 0},
                "structure": {
                    "crosslinks": True,
                    "max_parallel_paths": 2,
                    "split_probability": 0.9,
                    "min_layers": 4,
                    "max_layers": 4,
                },
            }
        )
        dag, _log = generate_dag(
            config, pool, seed=42, boss_candidates=_boss_candidates(pool)
        )
        # With crosslinks enabled and surplus fogs, the DAG should
        # pass validation and have additional paths from cross-links
        errors = dag.validate_structure()
        assert errors == [], f"DAG validation failed: {errors}"

    def test_crosslinks_add_extra_edges(self):
        """Enabling cross-links adds more edges than disabled with same seed.

        Tries multiple seeds to find one that produces a DAG with eligible
        cross-link pairs (parallel branches at adjacent layers with surplus fogs).
        """
        pool = self._make_pool_with_surplus_entries()
        base_structure = {
            "max_parallel_paths": 3,
            "split_probability": 0.9,
            "merge_probability": 0.2,
            "min_layers": 8,
            "max_layers": 12,
            "min_branch_age": 2,
        }

        # Try seeds until we find one that produces cross-links
        found = False
        for seed in range(100):
            config_off = Config.from_dict(
                {"structure": {**base_structure, "crosslinks": False}}
            )
            try:
                dag_off, _log_off = generate_dag(
                    config_off,
                    pool,
                    seed=seed,
                    boss_candidates=_boss_candidates(pool),
                )
            except Exception:
                continue

            config_on = Config.from_dict(
                {"structure": {**base_structure, "crosslinks": True}}
            )
            dag_on, _log_on = generate_dag(
                config_on, pool, seed=seed, boss_candidates=_boss_candidates(pool)
            )

            if len(dag_on.edges) > len(dag_off.edges):
                # Verify node count is the same (cross-links don't add nodes)
                assert len(dag_on.nodes) == len(dag_off.nodes)
                # Verify DAG is still valid
                errors = dag_on.validate_structure()
                assert errors == [], f"DAG validation failed: {errors}"
                found = True
                break

        assert found, "No seed produced cross-links — check pool/config setup"


# =============================================================================
# Asymmetric max_exits / max_entrances tests
# =============================================================================


class TestAsymmetricExitsEntrances:
    """Tests for asymmetric max_exits / max_entrances configuration."""

    @staticmethod
    def _make_wide_pool() -> ClusterPool:
        """Create a pool with clusters that have 4+ exits for wide splits."""
        pool = ClusterPool()

        # Start cluster with 4 exits
        pool.add(
            make_cluster(
                "chapel_start",
                zones=["chapel"],
                cluster_type="start",
                weight=1,
                entry_fogs=[],
                exit_fogs=[
                    {"fog_id": f"start_exit_{i}", "zone": "chapel"} for i in range(4)
                ],
            )
        )

        # Final boss
        pool.add(
            make_cluster(
                "erdtree_boss",
                zones=["leyndell_erdtree"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "final_entry", "zone": "leyndell_erdtree"}],
                exit_fogs=[],
            )
        )

        # Merge-capable clusters (2+ entries, 2+ exits)
        for i in range(30):
            pool.add(
                make_cluster(
                    f"mini_{i}",
                    zones=[f"mini_{i}_zone"],
                    cluster_type="mini_dungeon",
                    weight=3,
                    entry_fogs=[
                        {"fog_id": f"mini_{i}_entry_{j}", "zone": f"mini_{i}_zone"}
                        for j in range(3)
                    ],
                    exit_fogs=[
                        {"fog_id": f"mini_{i}_exit_{j}", "zone": f"mini_{i}_zone"}
                        for j in range(3)
                    ],
                )
            )

        # Legacy dungeons
        for i in range(5):
            pool.add(
                make_cluster(
                    f"legacy_{i}",
                    zones=[f"legacy_{i}_zone"],
                    cluster_type="legacy_dungeon",
                    weight=8,
                    entry_fogs=[
                        {"fog_id": f"legacy_{i}_entry_{j}", "zone": f"legacy_{i}_zone"}
                        for j in range(3)
                    ],
                    exit_fogs=[
                        {"fog_id": f"legacy_{i}_exit_{j}", "zone": f"legacy_{i}_zone"}
                        for j in range(3)
                    ],
                )
            )

        # Boss arenas
        for i in range(10):
            pool.add(
                make_cluster(
                    f"boss_{i}",
                    zones=[f"boss_{i}_zone"],
                    cluster_type="boss_arena",
                    weight=3,
                    entry_fogs=[
                        {"fog_id": f"boss_{i}_entry_{j}", "zone": f"boss_{i}_zone"}
                        for j in range(2)
                    ],
                    exit_fogs=[
                        {"fog_id": f"boss_{i}_exit_{j}", "zone": f"boss_{i}_zone"}
                        for j in range(2)
                    ],
                )
            )

        return pool

    def test_asymmetric_config_produces_valid_dag(self):
        """Asymmetric max_exits=4, max_entrances=2 produces a valid DAG."""
        pool = self._make_wide_pool()
        pool.filter_passant_incompatible()
        config = Config.from_dict(
            {
                "structure": {
                    "max_parallel_paths": 4,
                    "max_branches": 2,
                    "max_exits": 4,
                    "max_entrances": 2,
                    "min_layers": 4,
                    "max_layers": 8,
                    "split_probability": 0.9,
                    "merge_probability": 0.5,
                },
                "requirements": {
                    "legacy_dungeons": 0,
                    "bosses": 0,
                    "mini_dungeons": 0,
                    "major_bosses": 0,
                },
            }
        )

        successes = 0
        for seed in range(1, 500):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
                errors = dag.validate_structure()
                assert errors == [], f"Seed {seed}: {errors}"
                successes += 1
                if successes >= 5:
                    break
            except GenerationError:
                continue

        assert successes >= 5, f"Only {successes}/5 DAGs generated successfully"

    def test_max_entrances_limits_merge_fan_in(self):
        """max_entrances=2 with 4 branches forces multi-step merge convergence."""
        pool = self._make_wide_pool()
        pool.filter_passant_incompatible()
        config = Config.from_dict(
            {
                "structure": {
                    "max_parallel_paths": 4,
                    "max_branches": 2,
                    "max_exits": 4,
                    "max_entrances": 2,
                    "min_layers": 5,
                    "max_layers": 10,
                    "split_probability": 0.9,
                    "merge_probability": 0.5,
                },
                "requirements": {
                    "legacy_dungeons": 0,
                    "bosses": 0,
                    "mini_dungeons": 0,
                    "major_bosses": 0,
                },
            }
        )

        # With max_entrances=2, merging 4 branches requires at least 2 merge steps.
        # Check that no merge node has more than 2 incoming edges (fan-in ≤ 2).
        found_multi_branch = False
        for seed in range(1, 500):
            try:
                dag, _log = generate_dag(
                    config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
                )
            except GenerationError:
                continue

            # Count incoming edges per node
            in_degree: dict[str, int] = dict.fromkeys(dag.nodes, 0)
            for edge in dag.edges:
                in_degree[edge.target_id] += 1

            # No node should have fan-in > max_entrances
            for nid, deg in in_degree.items():
                assert (
                    deg <= 2
                ), f"Seed {seed}: node {nid} has fan-in {deg} > max_entrances=2"

            # Check if any node has multiple outgoing edges (split)
            has_split = any(
                len({e.target_id for e in dag.get_outgoing_edges(nid)}) > 1
                for nid in dag.nodes
            )
            if has_split:
                found_multi_branch = True
                break

        assert found_multi_branch, "No seed produced a multi-branch DAG"


def test_execute_passant_layer_carries_counter():
    """execute_passant_layer preserves layers_since_last_split."""
    dag = Dag(seed=1)
    start = DagNode(
        id="start",
        cluster=make_cluster(
            "s",
            zones=["sz"],
            cluster_type="start",
            entry_fogs=[],
            exit_fogs=[{"fog_id": "sx", "zone": "sz"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("sx", "sz")],
    )
    dag.add_node(start)
    dag.start_id = "start"

    branches = [
        Branch("b0", "start", FogRef("sx", "sz"), layers_since_last_split=5),
    ]
    passant_cluster = make_cluster(
        "p1",
        zones=["p1z"],
        cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "p1e", "zone": "p1z"}],
        exit_fogs=[{"fog_id": "p1x", "zone": "p1z"}],
    )
    pool = ClusterPool()
    pool.add(passant_cluster)
    used_zones: set[str] = {"sz"}

    result = execute_passant_layer(
        dag,
        branches,
        1,
        "mini_dungeon",
        pool,
        used_zones,
        random.Random(42),
    )
    assert result[0].layers_since_last_split == 5


def test_execute_merge_layer_carries_counter():
    """execute_merge_layer: merged branch gets max(sources), passant gets carry."""
    dag = Dag(seed=1)
    n0 = DagNode(
        id="n0",
        cluster=make_cluster(
            "c0",
            zones=["z0"],
            entry_fogs=[{"fog_id": "e0", "zone": "z0"}],
            exit_fogs=[{"fog_id": "x0", "zone": "z0"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("x0", "z0")],
    )
    n1 = DagNode(
        id="n1",
        cluster=make_cluster(
            "c1",
            zones=["z1"],
            entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
            exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("x1", "z1")],
    )
    n2 = DagNode(
        id="n2",
        cluster=make_cluster(
            "c2",
            zones=["z2"],
            entry_fogs=[{"fog_id": "e2", "zone": "z2"}],
            exit_fogs=[{"fog_id": "x2", "zone": "z2"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("x2", "z2")],
    )
    dag.add_node(n0)
    dag.add_node(n1)
    dag.add_node(n2)

    branches = [
        Branch("b0", "n0", FogRef("x0", "z0"), layers_since_last_split=3),
        Branch("b1", "n1", FogRef("x1", "z1"), layers_since_last_split=7),
        Branch("b2", "n2", FogRef("x2", "z2"), layers_since_last_split=2),
    ]
    merge_cluster = make_cluster(
        "mg",
        zones=["mgz"],
        cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "mge1", "zone": "mgz"},
            {"fog_id": "mge2", "zone": "mgz"},
        ],
        exit_fogs=[{"fog_id": "mgx", "zone": "mgz"}],
        allow_shared_entrance=True,
    )
    passant_cluster = make_cluster(
        "pc",
        zones=["pcz"],
        cluster_type="mini_dungeon",
        entry_fogs=[{"fog_id": "pce", "zone": "pcz"}],
        exit_fogs=[{"fog_id": "pcx", "zone": "pcz"}],
    )
    pool = ClusterPool()
    pool.add(merge_cluster)
    pool.add(passant_cluster)
    used_zones: set[str] = {"z0", "z1", "z2"}
    config = Config()
    config.structure.max_entrances = 2  # Force merge of only 2, leaving 1 passant
    config.structure.max_branch_spacing = 0  # Disabled, just testing carry

    result = execute_merge_layer(
        dag,
        branches,
        1,
        "mini_dungeon",
        pool,
        used_zones,
        random.Random(42),
        config,
    )
    # Result: [merged_branch, passant_branch]
    assert len(result) == 2
    merged = [b for b in result if b.id.startswith("merged_")]
    passant = [b for b in result if not b.id.startswith("merged_")]
    assert len(merged) == 1
    assert len(passant) == 1
    # Merged branch gets max of its source counters
    assert merged[0].layers_since_last_split == max(
        b.layers_since_last_split for b in branches if b.id != passant[0].id
    )
    # Non-merged branch carries its own counter
    source = next(b for b in branches if b.id == passant[0].id)
    assert passant[0].layers_since_last_split == source.layers_since_last_split


# =============================================================================
# update_branch_counters tests
# =============================================================================


def test_counter_update_split():
    """Split children get 0, other branches get +1."""
    split_children = [
        Branch("b0_a", "split", FogRef("a", "z"), layers_since_last_split=999),
        Branch("b0_b", "split", FogRef("b", "z"), layers_since_last_split=999),
    ]
    passant_branches = [
        Branch("b1", "n2", FogRef("y2", "z"), layers_since_last_split=2),
    ]
    update_branch_counters(
        LayerOperation.SPLIT,
        split_children=split_children,
        passant_branches=passant_branches,
    )
    assert [b.layers_since_last_split for b in split_children] == [0, 0]
    assert passant_branches[0].layers_since_last_split == 3


def test_counter_update_passant():
    """All branches get +1."""
    branches = [
        Branch("b0", "n0", FogRef("x", "z"), layers_since_last_split=2),
        Branch("b1", "n1", FogRef("y", "z"), layers_since_last_split=5),
    ]
    update_branch_counters(
        LayerOperation.PASSANT,
        passant_branches=branches,
    )
    assert [b.layers_since_last_split for b in branches] == [3, 6]


def test_counter_update_merge():
    """Merged branch gets max(sources), passant branches get +1."""
    merged = Branch("merged", "m", FogRef("x", "z"), layers_since_last_split=999)
    passant = [
        Branch("b2", "n2", FogRef("y", "z"), layers_since_last_split=1),
    ]
    merge_sources = [
        Branch("b0", "n0", FogRef("a", "z"), layers_since_last_split=3),
        Branch("b1", "n1", FogRef("b", "z"), layers_since_last_split=7),
    ]
    update_branch_counters(
        LayerOperation.MERGE,
        passant_branches=passant,
        merged_branches=(merged, merge_sources),
    )
    assert merged.layers_since_last_split == 7
    assert passant[0].layers_since_last_split == 2


# =============================================================================
# Max branch spacing end-to-end tests
# =============================================================================


def test_forced_split_triggers_at_threshold():
    """A branch exceeding max_branch_spacing gets a forced split."""
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[{"fog_id": "s_x1", "zone": "start_z"}],
    )
    # All clusters are splittable (2+ exits) so the forced split can always fire
    clusters = []
    for i in range(20):
        clusters.append(
            make_cluster(
                f"sp{i}",
                zones=[f"sp{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                    {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
                ],
            )
        )
    # Also add merge-capable clusters for the forced merge at near-end
    for i in range(5):
        clusters.append(
            make_cluster(
                f"mg{i}",
                zones=[f"mg{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                    {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
                allow_shared_entrance=True,
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters:
        pool.add(c)
    pool.add(boss)

    config = Config()
    config.structure.final_boss_candidates = {"boss_z": 1}
    config.structure.max_branch_spacing = 2
    config.structure.split_probability = 0.0  # Would never split naturally
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 3
    config.structure.min_layers = 10
    config.structure.max_layers = 14
    config.requirements.mini_dungeons = 8
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    # Try multiple seeds since cluster selection is random
    found_split = False
    for seed in range(50):
        try:
            dag, _log = generate_dag(
                config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
            )
            has_split = any(
                len({e.target_id for e in dag.get_outgoing_edges(nid)}) > 1
                for nid in dag.nodes
            )
            if has_split:
                found_split = True
                break
        except GenerationError:
            continue

    assert found_split, "Expected at least one seed to produce a forced split"


def test_forced_split_targets_most_stale_branch():
    """Forced split selects the branch with highest layers_since_last_split.

    Verifies the stale-targeting logic by constructing a scenario with
    two branches of different ages, then checking which gets split.
    """
    dag = Dag(seed=1)
    # Two nodes at layer 0, simulating two branches with different staleness
    n_fresh = DagNode(
        id="n_fresh",
        cluster=make_cluster(
            "cf",
            zones=["zf"],
            entry_fogs=[{"fog_id": "ef", "zone": "zf"}],
            exit_fogs=[{"fog_id": "xf", "zone": "zf"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xf", "zf")],
    )
    n_stale = DagNode(
        id="n_stale",
        cluster=make_cluster(
            "cs",
            zones=["zs"],
            entry_fogs=[{"fog_id": "es", "zone": "zs"}],
            exit_fogs=[{"fog_id": "xs", "zone": "zs"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xs", "zs")],
    )
    dag.add_node(n_fresh)
    dag.add_node(n_stale)

    # Branch A: recently split (counter=0), Branch B: stale (counter=5)
    # Same parent node — anti-micro-merge blocks REBALANCE, forcing SPLIT
    branches = [
        Branch("b_fresh", "n_fresh", FogRef("xf", "zf"), layers_since_last_split=0),
        Branch("b_stale", "n_fresh", FogRef("xs", "zs"), layers_since_last_split=5),
    ]

    # Splittable cluster (2+ exits)
    split_cluster = make_cluster(
        "split_c",
        zones=["sc_z"],
        entry_fogs=[{"fog_id": "sc_e", "zone": "sc_z"}],
        exit_fogs=[
            {"fog_id": "sc_x1", "zone": "sc_z"},
            {"fog_id": "sc_x2", "zone": "sc_z"},
        ],
    )

    config = Config()
    config.structure.split_probability = 0.0  # Would never split normally
    config.structure.max_parallel_paths = 4
    config.structure.max_branch_spacing = 4  # Threshold at 4, stale has 5

    # determine_operation returns forced SPLIT (not REBALANCE — not saturated)
    op, fan = determine_operation(
        split_cluster,
        branches,
        config,
        random.Random(42),
    )
    assert op == LayerOperation.SPLIT

    # Simulate the stale-targeting logic from the main loop
    max_stale_val = max(b.layers_since_last_split for b in branches)
    stale_indices = [
        i for i, b in enumerate(branches) if b.layers_since_last_split == max_stale_val
    ]
    # Should always pick branch index 1 (the stale one)
    assert stale_indices == [1]
    # Even with different RNG seeds, the only candidate is b_stale
    for seed in range(10):
        split_idx = random.Random(seed).choice(stale_indices)
        assert branches[split_idx].id == "b_stale"


def test_rebalance_when_saturated():
    """When max_parallel_paths is reached, REBALANCE merges + splits on same layer."""
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
            {"fog_id": "s_x3", "zone": "start_z"},
        ],
    )
    clusters_list = []
    for i in range(30):
        clusters_list.append(
            make_cluster(
                f"sp{i}",
                zones=[f"sp{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                    {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
                ],
            )
        )
    for i in range(10):
        clusters_list.append(
            make_cluster(
                f"mg{i}",
                zones=[f"mg{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                    {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
                allow_shared_entrance=True,
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters_list:
        pool.add(c)
    pool.add(boss)

    config = Config()
    config.structure.final_boss_candidates = {"boss_z": 1}
    config.structure.max_branch_spacing = 3
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 3  # Start saturates with 3 exits
    config.structure.min_layers = 10
    config.structure.max_layers = 14
    config.requirements.mini_dungeons = 8
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    # Should succeed — REBALANCE handles spacing at max branch count
    success = False
    for seed in range(50):
        try:
            dag, _log = generate_dag(
                config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
            )
            assert dag.end_id
            success = True
            break
        except GenerationError:
            continue
    assert success, "No seed produced a valid DAG with REBALANCE when saturated"


def test_forced_merge_bypasses_min_branch_age():
    """Forced merge for spacing ignores min_branch_age."""
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    clusters_list = []
    for i in range(20):
        clusters_list.append(
            make_cluster(
                f"sp{i}",
                zones=[f"sp{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                    {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
                ],
            )
        )
    for i in range(5):
        clusters_list.append(
            make_cluster(
                f"mg{i}",
                zones=[f"mg{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                    {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
                allow_shared_entrance=True,
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters_list:
        pool.add(c)
    pool.add(boss)

    config = Config()
    config.structure.final_boss_candidates = {"boss_z": 1}
    config.structure.max_branch_spacing = 5
    config.structure.min_branch_age = 4  # High but < max_branch_spacing
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 2
    config.structure.min_layers = 10
    config.structure.max_layers = 14
    config.requirements.mini_dungeons = 8
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    # Should succeed — forced merge bypasses min_branch_age
    success = False
    for seed in range(50):
        try:
            dag, _log = generate_dag(
                config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
            )
            assert dag.end_id
            success = True
            break
        except GenerationError:
            continue
    assert success, "Forced merge should bypass min_branch_age"


def test_max_branch_spacing_disabled_regression():
    """max_branch_spacing=0 produces same linear behavior as before the feature."""
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[{"fog_id": "s_x1", "zone": "start_z"}],
    )
    passants = []
    for i in range(10):
        passants.append(
            make_cluster(
                f"p{i}",
                zones=[f"p{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"p{i}_e", "zone": f"p{i}_z"}],
                exit_fogs=[{"fog_id": f"p{i}_x", "zone": f"p{i}_z"}],
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in passants:
        pool.add(c)
    pool.add(boss)

    config = Config()
    config.structure.final_boss_candidates = {"boss_z": 1}
    config.structure.max_branch_spacing = 0  # Disabled
    config.structure.split_probability = 1.0
    config.structure.min_layers = 6
    config.structure.max_layers = 6
    config.requirements.mini_dungeons = 4
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    dag, _log = generate_dag(
        config, pool, seed=42, boss_candidates=_boss_candidates(pool)
    )
    # Linear — no splits possible with 1-exit clusters
    assert not any(
        len({e.target_id for e in dag.get_outgoing_edges(nid)}) > 1 for nid in dag.nodes
    )


def test_first_layer_type_counter_propagation():
    """first_layer_type passant correctly initializes layers_since_last_split.

    After first_layer_type passant, counters should be 1 (one passant layer
    elapsed). The forced split should still trigger at the right threshold.
    """
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[{"fog_id": "s_x1", "zone": "start_z"}],
    )
    # Legacy dungeon for first_layer_type
    ld = make_cluster(
        "ld0",
        zones=["ld0_z"],
        cluster_type="legacy_dungeon",
        entry_fogs=[{"fog_id": "ld0_e", "zone": "ld0_z"}],
        exit_fogs=[
            {"fog_id": "ld0_x1", "zone": "ld0_z"},
            {"fog_id": "ld0_x2", "zone": "ld0_z"},
        ],
    )
    # Splittable mini_dungeons
    clusters = []
    for i in range(20):
        clusters.append(
            make_cluster(
                f"sp{i}",
                zones=[f"sp{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                    {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
                ],
            )
        )
    # Merge-capable clusters
    for i in range(5):
        clusters.append(
            make_cluster(
                f"mg{i}",
                zones=[f"mg{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                    {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
                allow_shared_entrance=True,
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    pool.add(ld)
    for c in clusters:
        pool.add(c)
    pool.add(boss)

    config = Config()
    config.structure.final_boss_candidates = {"boss_z": 1}
    config.structure.max_branch_spacing = 3
    config.structure.first_layer_type = "legacy_dungeon"
    config.structure.split_probability = 0.0  # Only forced splits
    config.structure.merge_probability = 0.0
    config.structure.max_parallel_paths = 3
    config.structure.min_layers = 10
    config.structure.max_layers = 14
    config.requirements.mini_dungeons = 8
    config.requirements.bosses = 0
    config.requirements.legacy_dungeons = 0
    config.requirements.major_bosses = 0

    # Try multiple seeds — at least one should produce a forced split.
    # With split_probability=0.0, splits only happen via max_branch_spacing
    # enforcement. If first_layer_type correctly increments the counter,
    # the forced split triggers 1 layer sooner than if counter stayed at 0.
    found_split = False
    for seed in range(50):
        try:
            dag, _log = generate_dag(
                config, pool, seed=seed, boss_candidates=_boss_candidates(pool)
            )
            has_split = any(
                len({e.target_id for e in dag.get_outgoing_edges(nid)}) > 1
                for nid in dag.nodes
            )
            if has_split:
                found_split = True
                break
        except GenerationError:
            continue

    assert found_split, "Expected at least one seed to produce a forced split"


@pytest.mark.skipif(
    not os.path.exists("data/clusters.json"),
    reason="Requires data/clusters.json",
)
def test_max_branch_spacing_statistical():
    """No branch exceeds max_branch_spacing + 2 across many seeds."""
    from speedfog.clusters import ClusterPool as RealClusterPool

    pool = RealClusterPool.from_json("data/clusters.json")
    boss_candidates = _boss_candidates(pool)
    max_spacing = 4
    violations = []

    for seed in range(50):
        config = Config()
        config.seed = seed
        config.structure.max_branch_spacing = max_spacing
        try:
            dag, _log = generate_dag(config, pool, boss_candidates=boss_candidates)
        except GenerationError:
            continue

        max_observed = _measure_max_branch_spacing(dag)
        if max_observed > max_spacing + 2:
            violations.append((seed, max_observed))

    assert not violations, f"Branches exceeded max_branch_spacing + 2: {violations}"


def _measure_max_branch_spacing(dag):
    """Measure the maximum consecutive non-split layers in the DAG.

    Walks layer-by-layer, counting consecutive layers where no node
    has multiple outgoing targets. Terminal nodes (0 outgoing edges)
    are excluded from the check.
    """
    max_layer = max((n.layer for n in dag.nodes.values()), default=-1)
    max_spacing = 0
    since_last_split = 0

    for layer in range(max_layer + 1):
        layer_nodes = [nid for nid, n in dag.nodes.items() if n.layer == layer]
        has_split = False
        all_terminal = True
        for nid in layer_nodes:
            targets = {e.target_id for e in dag.get_outgoing_edges(nid)}
            if len(targets) > 0:
                all_terminal = False
            if len(targets) >= 2:
                has_split = True
        if all_terminal:
            continue
        if has_split:
            since_last_split = 0
        else:
            since_last_split += 1
        max_spacing = max(max_spacing, since_last_split)

    return max_spacing


# ── execute_rebalance_layer tests ──────────────────────────────────────


def test_execute_rebalance_layer_basic():
    """execute_rebalance_layer splits stale branch and merges another pair."""
    dag = Dag(seed=1)

    # 3 branches: A (stale), B and C (fresh, different parent nodes)
    n_a = DagNode(
        id="n_a",
        cluster=make_cluster(
            "ca",
            zones=["za"],
            entry_fogs=[{"fog_id": "ea", "zone": "za"}],
            exit_fogs=[{"fog_id": "xa", "zone": "za"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xa", "za")],
    )
    n_b = DagNode(
        id="n_b",
        cluster=make_cluster(
            "cb",
            zones=["zb"],
            entry_fogs=[{"fog_id": "eb", "zone": "zb"}],
            exit_fogs=[{"fog_id": "xb", "zone": "zb"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xb", "zb")],
    )
    n_c = DagNode(
        id="n_c",
        cluster=make_cluster(
            "cc",
            zones=["zc"],
            entry_fogs=[{"fog_id": "ec", "zone": "zc"}],
            exit_fogs=[{"fog_id": "xc", "zone": "zc"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xc", "zc")],
    )
    dag.add_node(n_a)
    dag.add_node(n_b)
    dag.add_node(n_c)

    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),  # stale
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]

    # Pool: split-capable + merge-capable clusters
    pool = ClusterPool()
    for i in range(5):
        pool.add(
            make_cluster(
                f"split{i}",
                zones=[f"s{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                    {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
                ],
            )
        )
    for i in range(5):
        pool.add(
            make_cluster(
                f"merge{i}",
                zones=[f"m{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"m{i}_e1", "zone": f"m{i}_z"},
                    {"fog_id": f"m{i}_e2", "zone": f"m{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"m{i}_x", "zone": f"m{i}_z"}],
                allow_shared_entrance=True,
            )
        )

    config = Config()
    config.structure.max_parallel_paths = 3

    result = execute_rebalance_layer(
        dag,
        branches,
        layer_idx=1,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=random.Random(42),
        config=config,
    )

    result_branches, layers_used = result
    # Same number of branches (rebalance is N -> N)
    assert len(result_branches) == 3
    assert layers_used == 1
    # At least one branch has counter = 0 (from the split)
    assert any(b.layers_since_last_split == 0 for b in result_branches)
    # No branch named "a" remains (it was split into children)
    assert not any(b.id == "a" for b in result_branches)


def test_execute_rebalance_layer_no_merge_pair():
    """Returns None when no valid merge pair exists."""
    dag = Dag(seed=1)
    # All branches share same parent node → anti-micro-merge blocks merge
    n = DagNode(
        id="n_shared",
        cluster=make_cluster(
            "cs",
            zones=["zs"],
            entry_fogs=[{"fog_id": "es", "zone": "zs"}],
            exit_fogs=[{"fog_id": "xs", "zone": "zs"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xs", "zs")],
    )
    dag.add_node(n)
    branches = [
        Branch("a", "n_shared", FogRef("xs", "zs"), layers_since_last_split=5),
        Branch("b", "n_shared", FogRef("xs", "zs"), layers_since_last_split=1),
        Branch("c", "n_shared", FogRef("xs", "zs"), layers_since_last_split=1),
    ]
    pool = ClusterPool()
    for i in range(5):
        pool.add(
            make_cluster(
                f"sp{i}",
                zones=[f"s{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                    {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
                ],
            )
        )
    config = Config()
    config.structure.max_parallel_paths = 3

    result = execute_rebalance_layer(
        dag,
        branches,
        layer_idx=1,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=random.Random(42),
        config=config,
    )
    # No valid merge pair (anti-micro-merge: all share same parent) → returns None
    assert result is None


def test_execute_rebalance_layer_counter_propagation():
    """Merged branch counter ends at max(A, B) + 1 after update."""
    dag = Dag(seed=1)
    n_a = DagNode(
        id="n_a",
        cluster=make_cluster(
            "ca",
            zones=["za"],
            entry_fogs=[{"fog_id": "ea", "zone": "za"}],
            exit_fogs=[{"fog_id": "xa", "zone": "za"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xa", "za")],
    )
    n_b = DagNode(
        id="n_b",
        cluster=make_cluster(
            "cb",
            zones=["zb"],
            entry_fogs=[{"fog_id": "eb", "zone": "zb"}],
            exit_fogs=[{"fog_id": "xb", "zone": "zb"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xb", "zb")],
    )
    n_c = DagNode(
        id="n_c",
        cluster=make_cluster(
            "cc",
            zones=["zc"],
            entry_fogs=[{"fog_id": "ec", "zone": "zc"}],
            exit_fogs=[{"fog_id": "xc", "zone": "zc"}],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("xc", "zc")],
    )
    dag.add_node(n_a)
    dag.add_node(n_b)
    dag.add_node(n_c)

    branches = [
        Branch(
            "a", "n_a", FogRef("xa", "za"), layers_since_last_split=8
        ),  # stale (split)
        Branch(
            "b", "n_b", FogRef("xb", "zb"), layers_since_last_split=3
        ),  # merge candidate
        Branch(
            "c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1
        ),  # merge candidate
    ]

    pool = ClusterPool()
    for i in range(5):
        pool.add(
            make_cluster(
                f"split{i}",
                zones=[f"s{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                    {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
                ],
            )
        )
    for i in range(5):
        pool.add(
            make_cluster(
                f"merge{i}",
                zones=[f"m{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"m{i}_e1", "zone": f"m{i}_z"},
                    {"fog_id": f"m{i}_e2", "zone": f"m{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"m{i}_x", "zone": f"m{i}_z"}],
                allow_shared_entrance=True,
            )
        )

    config = Config()
    config.structure.max_parallel_paths = 3

    result = execute_rebalance_layer(
        dag,
        branches,
        layer_idx=1,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=random.Random(42),
        config=config,
    )

    result_branches, layers_used = result
    assert layers_used == 1

    # Split children have counter = 0
    split_children = [b for b in result_branches if b.layers_since_last_split == 0]
    assert len(split_children) == 2

    # Merged branch has counter = max(3, 1) + 1 = 4
    merged = [b for b in result_branches if "merged" in b.id]
    assert len(merged) == 1
    assert merged[0].layers_since_last_split == 4  # max(3, 1) + 1


# ── determine_operation REBALANCE / prefer_merge tests ─────────────────


def test_determine_operation_returns_rebalance():
    """determine_operation returns REBALANCE when saturated + stale."""
    cluster = make_cluster(
        "c1",
        zones=["z1"],
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
        ],
    )
    # 3 branches at max_parallel_paths=3, one stale
    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]
    config = Config()
    config.structure.max_parallel_paths = 3
    config.structure.max_branch_spacing = 4
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0

    op, fan = determine_operation(cluster, branches, config, random.Random(42))
    assert op == LayerOperation.REBALANCE


def test_determine_operation_rebalance_at_2_branches():
    """REBALANCE works with 2 branches (merge-first strategy)."""
    cluster = make_cluster(
        "c1",
        zones=["z1"],
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
        ],
    )
    # 2 branches with different parents, one stale
    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
    ]
    config = Config()
    config.structure.max_parallel_paths = 3
    config.structure.max_branch_spacing = 4
    config.structure.split_probability = 0.0
    config.structure.merge_probability = 0.0

    op, fan = determine_operation(cluster, branches, config, random.Random(42))
    assert op == LayerOperation.REBALANCE


def test_determine_operation_prefer_merge():
    """prefer_merge=True bypasses probability roll in favor of MERGE."""
    cluster = make_cluster(
        "c1",
        zones=["z1"],
        entry_fogs=[
            {"fog_id": "e1", "zone": "z1"},
            {"fog_id": "e2", "zone": "z1"},
        ],
        exit_fogs=[{"fog_id": "x1", "zone": "z1"}],
        allow_shared_entrance=True,
    )
    # 2 branches, different parent nodes
    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=0),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=0),
    ]
    config = Config()
    config.structure.max_parallel_paths = 4
    config.structure.split_probability = 1.0  # Would always split
    config.structure.merge_probability = 0.0  # Would never merge

    # With prefer_merge: should merge despite split_probability=1.0
    op, _ = determine_operation(
        cluster,
        branches,
        config,
        random.Random(42),
        prefer_merge=True,
    )
    assert op == LayerOperation.MERGE


# ── Convergence integration tests ─────────────────────────────────────


def test_rebalance_during_convergence():
    """Convergence phase doesn't create linear stretches > threshold + 2."""
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    clusters_list = []
    for i in range(30):
        clusters_list.append(
            make_cluster(
                f"sp{i}",
                zones=[f"sp{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                    {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
                ],
            )
        )
    for i in range(10):
        clusters_list.append(
            make_cluster(
                f"mg{i}",
                zones=[f"mg{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                    {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
                allow_shared_entrance=True,
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters_list:
        pool.add(c)
    pool.add(boss)

    max_spacing = 4
    violations = []
    for seed in range(30):
        config = Config()
        config.structure.final_boss_candidates = {"boss_z": 1}
        config.structure.max_branch_spacing = max_spacing
        config.structure.split_probability = 0.9
        config.structure.merge_probability = 0.5
        config.structure.max_parallel_paths = 4
        config.structure.min_layers = 12
        config.structure.max_layers = 18
        config.requirements.mini_dungeons = 10
        config.requirements.bosses = 0
        config.requirements.legacy_dungeons = 0
        config.requirements.major_bosses = 0

        try:
            dag, _log = generate_dag(
                config,
                pool,
                seed=seed,
                boss_candidates=_boss_candidates(pool),
            )
            max_observed = _measure_max_branch_spacing(dag)
            # During convergence, merges reduce branch count below
            # max_parallel_paths, so REBALANCE no longer triggers.
            # Remaining merge-only convergence can add up to merge_reserve
            # layers. Allow max_spacing + merge_reserve (= 4 + 6 = 10).
            limit = max_spacing + config.structure.max_parallel_paths + 2
            if max_observed > limit:
                violations.append((seed, max_observed))
        except GenerationError:
            continue

    assert not violations, f"Convergence stretches exceeded limit: {violations}"


def test_convergence_terminates():
    """Convergence loop always terminates (no infinite REBALANCE loop)."""
    start = make_cluster(
        "start",
        zones=["start_z"],
        cluster_type="start",
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "s_x1", "zone": "start_z"},
            {"fog_id": "s_x2", "zone": "start_z"},
        ],
    )
    clusters_list = []
    for i in range(40):
        clusters_list.append(
            make_cluster(
                f"sp{i}",
                zones=[f"sp{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"sp{i}_e", "zone": f"sp{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"sp{i}_x1", "zone": f"sp{i}_z"},
                    {"fog_id": f"sp{i}_x2", "zone": f"sp{i}_z"},
                ],
            )
        )
    for i in range(10):
        clusters_list.append(
            make_cluster(
                f"mg{i}",
                zones=[f"mg{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mg{i}_e1", "zone": f"mg{i}_z"},
                    {"fog_id": f"mg{i}_e2", "zone": f"mg{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mg{i}_x", "zone": f"mg{i}_z"}],
                allow_shared_entrance=True,
            )
        )
    boss = make_cluster(
        "boss1",
        zones=["boss_z"],
        cluster_type="final_boss",
        entry_fogs=[{"fog_id": "b_e", "zone": "boss_z"}],
        exit_fogs=[],
    )
    pool = ClusterPool()
    pool.add(start)
    for c in clusters_list:
        pool.add(c)
    pool.add(boss)

    for seed in range(20):
        config = Config()
        config.structure.final_boss_candidates = {"boss_z": 1}
        config.structure.max_branch_spacing = 3
        config.structure.split_probability = 1.0
        config.structure.merge_probability = 0.0
        config.structure.max_parallel_paths = 4
        config.structure.min_layers = 10
        config.structure.max_layers = 14
        config.requirements.mini_dungeons = 8
        config.requirements.bosses = 0
        config.requirements.legacy_dungeons = 0
        config.requirements.major_bosses = 0

        try:
            dag, _log = generate_dag(
                config,
                pool,
                seed=seed,
                boss_candidates=_boss_candidates(pool),
            )
            # If we get here, convergence terminated
            assert dag.end_id
        except GenerationError as e:
            # Cluster exhaustion is acceptable, but convergence timeout is not
            assert "Convergence failed" not in str(
                e
            ), f"Convergence timeout at seed {seed}: {e}"


# ── Type homogeneity: REBALANCE respects layer_type ──────────────────


def test_execute_rebalance_returns_none_when_no_split_capable_of_type():
    """Returns None when split-capable clusters exist but not of layer_type."""
    dag = Dag(seed=1)

    # 3 branches with different parent nodes
    for name in ("a", "b", "c"):
        n = DagNode(
            id=f"n_{name}",
            cluster=make_cluster(
                f"c{name}",
                zones=[f"z{name}"],
                entry_fogs=[{"fog_id": f"e{name}", "zone": f"z{name}"}],
                exit_fogs=[{"fog_id": f"x{name}", "zone": f"z{name}"}],
            ),
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef(f"x{name}", f"z{name}")],
        )
        dag.add_node(n)

    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]

    # Pool has split-capable clusters but only of type mini_dungeon
    pool = ClusterPool()
    for i in range(5):
        pool.add(
            make_cluster(
                f"split{i}",
                zones=[f"s{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[{"fog_id": f"s{i}_e", "zone": f"s{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"s{i}_x1", "zone": f"s{i}_z"},
                    {"fog_id": f"s{i}_x2", "zone": f"s{i}_z"},
                ],
            )
        )
    for i in range(5):
        pool.add(
            make_cluster(
                f"merge{i}",
                zones=[f"m{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"m{i}_e1", "zone": f"m{i}_z"},
                    {"fog_id": f"m{i}_e2", "zone": f"m{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"m{i}_x", "zone": f"m{i}_z"}],
                allow_shared_entrance=True,
            )
        )

    config = Config()
    config.structure.max_parallel_paths = 3

    # Request layer_type="major_boss" — no major_boss in pool
    result = execute_rebalance_layer(
        dag,
        branches,
        layer_idx=1,
        layer_type="major_boss",
        clusters=pool,
        used_zones=set(),
        rng=random.Random(42),
        config=config,
    )

    assert result is None


def test_execute_rebalance_returns_none_when_no_merge_capable_of_type():
    """Returns None when merge-capable clusters exist but not of layer_type."""
    dag = Dag(seed=1)

    for name in ("a", "b", "c"):
        n = DagNode(
            id=f"n_{name}",
            cluster=make_cluster(
                f"c{name}",
                zones=[f"z{name}"],
                entry_fogs=[{"fog_id": f"e{name}", "zone": f"z{name}"}],
                exit_fogs=[{"fog_id": f"x{name}", "zone": f"z{name}"}],
            ),
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef(f"x{name}", f"z{name}")],
        )
        dag.add_node(n)

    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]

    pool = ClusterPool()
    # Split-capable of type major_boss
    for i in range(3):
        pool.add(
            make_cluster(
                f"split_mb{i}",
                zones=[f"smb{i}_z"],
                cluster_type="major_boss",
                entry_fogs=[{"fog_id": f"smb{i}_e", "zone": f"smb{i}_z"}],
                exit_fogs=[
                    {"fog_id": f"smb{i}_x1", "zone": f"smb{i}_z"},
                    {"fog_id": f"smb{i}_x2", "zone": f"smb{i}_z"},
                ],
            )
        )
    # Merge-capable but only mini_dungeon (wrong type)
    for i in range(3):
        pool.add(
            make_cluster(
                f"merge_md{i}",
                zones=[f"mmd{i}_z"],
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"mmd{i}_e1", "zone": f"mmd{i}_z"},
                    {"fog_id": f"mmd{i}_e2", "zone": f"mmd{i}_z"},
                ],
                exit_fogs=[{"fog_id": f"mmd{i}_x", "zone": f"mmd{i}_z"}],
                allow_shared_entrance=True,
            )
        )

    config = Config()
    config.structure.max_parallel_paths = 3

    result = execute_rebalance_layer(
        dag,
        branches,
        layer_idx=1,
        layer_type="major_boss",
        clusters=pool,
        used_zones=set(),
        rng=random.Random(42),
        config=config,
    )

    assert result is None


def test_determine_operation_skip_rebalance():
    """skip_rebalance=True skips REBALANCE even when conditions are met."""
    cluster = make_cluster(
        "c1",
        zones=["z1"],
        entry_fogs=[{"fog_id": "e1", "zone": "z1"}],
        exit_fogs=[
            {"fog_id": "x1", "zone": "z1"},
            {"fog_id": "x2", "zone": "z1"},
        ],
    )
    config = Config()
    config.structure.max_parallel_paths = 4
    config.structure.max_branch_spacing = 3

    branches = [
        Branch("a", "n_a", FogRef("xa", "za"), layers_since_last_split=5),
        Branch("b", "n_b", FogRef("xb", "zb"), layers_since_last_split=1),
        Branch("c", "n_c", FogRef("xc", "zc"), layers_since_last_split=1),
    ]

    # Without skip_rebalance: should return REBALANCE
    op, _ = determine_operation(
        cluster,
        branches,
        config,
        random.Random(42),
    )
    assert op == LayerOperation.REBALANCE

    # With skip_rebalance: should NOT return REBALANCE
    op, _ = determine_operation(
        cluster,
        branches,
        config,
        random.Random(42),
        skip_rebalance=True,
    )
    assert op != LayerOperation.REBALANCE


class TestZoneConflicts:
    """Tests for zone conflict exclusion during DAG generation."""

    def test_conflicting_zone_excluded_after_selection(self):
        """When a cluster is selected, clusters with conflicting zones are excluded."""
        margit = make_cluster(
            "margit",
            zones=["stormveil_margit"],
            cluster_type="major_boss",
            weight=2,
        )
        morgott = make_cluster(
            "morgott",
            zones=["leyndell_sanctuary"],
            cluster_type="major_boss",
            weight=2,
        )
        other = make_cluster(
            "other",
            zones=["other_zone"],
            cluster_type="major_boss",
            weight=2,
        )

        pool = ClusterPool()
        pool.zone_conflicts = {
            "stormveil_margit": ["leyndell_sanctuary"],
            "leyndell_sanctuary": ["stormveil_margit"],
        }
        for c in [margit, morgott, other]:
            pool.add(c)

        used_zones: set[str] = set()

        # Simulate selecting margit
        _mark_cluster_used(margit, used_zones, pool)

        # Now morgott should be excluded (its zone is in used_zones)
        result = pick_cluster_uniform(
            pool.get_by_type("major_boss"), used_zones, random.Random(42)
        )
        assert result is not None
        assert result.id == "other"

    def test_mark_cluster_used_adds_conflicts(self):
        """_mark_cluster_used adds both cluster zones and conflicting zones."""
        margit = make_cluster(
            "margit",
            zones=["stormveil_margit"],
            cluster_type="major_boss",
            weight=2,
        )
        pool = ClusterPool()
        pool.zone_conflicts = {
            "stormveil_margit": ["leyndell_sanctuary"],
        }
        pool.add(margit)

        used_zones: set[str] = set()
        _mark_cluster_used(margit, used_zones, pool)

        assert "stormveil_margit" in used_zones
        assert "leyndell_sanctuary" in used_zones

    def test_mark_cluster_used_no_conflicts(self):
        """_mark_cluster_used works when no conflicts exist."""
        cluster = make_cluster("test", zones=["zone_a"])
        pool = ClusterPool()
        pool.add(cluster)

        used_zones: set[str] = set()
        _mark_cluster_used(cluster, used_zones, pool)

        assert used_zones == {"zone_a"}


class TestPickClusterWithTypeFallbackDistribution:
    """Tests for weighted fallback distribution."""

    def test_fallback_distributes_across_types(self):
        """Fallback should pick from multiple types, not always the largest pool."""
        pool = ClusterPool()
        # No mini_dungeon clusters at all (force fallback)
        for i in range(20):
            pool.add(
                make_cluster(
                    f"boss_{i}",
                    cluster_type="boss_arena",
                    zones=[f"boss_{i}_z"],
                )
            )
        for i in range(15):
            pool.add(
                make_cluster(
                    f"legacy_{i}",
                    cluster_type="legacy_dungeon",
                    zones=[f"legacy_{i}_z"],
                )
            )

        from collections import Counter

        type_counts: Counter[str] = Counter()
        for seed in range(100):
            rng = random.Random(seed)
            result = pick_cluster_with_type_fallback(
                pool,
                "mini_dungeon",
                set(),
                rng,
            )
            assert result is not None
            type_counts[result.type] += 1

        # Both types should appear (not just boss_arena every time)
        assert type_counts["boss_arena"] > 0
        assert type_counts["legacy_dungeon"] > 0


class TestSplitNoClusterReuse:
    """Ensure SPLIT marks primary_cluster used before passant picks.

    Regression test for a TOCTOU bug where primary_cluster was selected at
    the top of the main loop but only marked used inside `if i == split_idx`.
    When split_idx > 0, passant branches processed before the split could
    pick the same cluster, creating two DAG nodes sharing one cluster.
    The output then merged their connections, causing entry-as-exit violations
    and duplicate exit fog assignments.
    """

    @pytest.mark.skipif(
        not Path("data/clusters.json").exists(),
        reason="requires data/clusters.json",
    )
    def test_no_duplicate_cluster_across_nodes(self):
        """No two DAG nodes should reference the same cluster.

        Uses real cluster data with a high-split config to reproduce the
        TOCTOU collision between primary_cluster and passant picks.
        """
        from collections import Counter
        from copy import deepcopy

        from speedfog.clusters import load_clusters

        clusters_orig = load_clusters(Path("data/clusters.json"))

        structure = StructureConfig(
            split_probability=0.9,
            max_parallel_paths=4,
            merge_probability=0.5,
            min_branch_age=3,
            crosslinks=True,
            max_layers=30,
            min_layers=25,
            first_layer_type="legacy_dungeon",
        )
        structure.max_exits = 4
        structure.max_entrances = 2
        config = Config(
            seed=0,
            structure=structure,
            requirements=RequirementsConfig(
                legacy_dungeons=2,
                bosses=7,
                mini_dungeons=8,
                major_bosses=8,
            ),
        )

        violations = 0
        tested = 0
        for seed in range(2000):
            try:
                clusters = deepcopy(clusters_orig)
                clusters.merge_roundtable_into_start()
                boss_cand = clusters.get_by_type("major_boss") + clusters.get_by_type(
                    "final_boss"
                )
                clusters.filter_passant_incompatible()
                dag, _log = generate_dag(
                    config, clusters, seed=seed, boss_candidates=boss_cand
                )
                tested += 1
                uses = Counter(n.cluster.id for n in dag.nodes.values())
                dupes = {k: v for k, v in uses.items() if v > 1}
                if dupes:
                    violations += 1
            except GenerationError:
                pass

        assert tested > 100, f"Too few successful generations: {tested}"
        assert (
            violations == 0
        ), f"{violations}/{tested} seeds had duplicate cluster usage"


class TestPickClusterWeightMatched:
    """Tests for pick_cluster_weight_matched."""

    def _make_pool(self, weights: list[int]) -> list[ClusterData]:
        """Create clusters with distinct zones and specified weights."""
        return [
            make_cluster(f"c{i}", zones=[f"z{i}"], weight=w)
            for i, w in enumerate(weights)
        ]

    def test_exact_match_preferred(self):
        """When an exact weight match exists, it is chosen."""
        candidates = self._make_pool([1, 2, 3, 4, 5])
        # Run many times: anchor=3 should always pick weight-3 first
        results = set()
        for seed in range(50):
            r = pick_cluster_weight_matched(
                candidates,
                set(),
                random.Random(seed),
                anchor_weight=3,
            )
            assert r is not None
            results.add(r.weight)
        assert results == {3}  # Only exact match since only 1 candidate at w=3

    def test_tolerance_widening_prefers_closer(self):
        """Closer weight matches are preferred over further ones."""
        # Weights [2, 5]: anchor=3
        # tol=1: weight 2 matches (|2-3|=1), weight 5 doesn't (|5-3|=2)
        # So weight 2 is always preferred (picked at tol=1 before tol=2)
        candidates = self._make_pool([2, 5])
        results = set()
        for seed in range(50):
            r = pick_cluster_weight_matched(
                candidates,
                set(),
                random.Random(seed),
                anchor_weight=3,
                max_tolerance=3,
            )
            assert r is not None
            results.add(r.weight)
        assert results == {2}  # Weight 2 always wins (closer to anchor)

    def test_tolerance_widening_same_distance(self):
        """Candidates at the same distance from anchor are both reachable."""
        # Weights [1, 5]: anchor=3, both at distance 2
        # tol=0: no match, tol=1: no match
        # tol=2: both match -> either can be picked
        candidates = self._make_pool([1, 5])
        results = set()
        for seed in range(50):
            r = pick_cluster_weight_matched(
                candidates,
                set(),
                random.Random(seed),
                anchor_weight=3,
                max_tolerance=2,
            )
            assert r is not None
            results.add(r.weight)
        assert results == {1, 5}  # Both reachable at tol=2

    def test_fallback_to_any(self):
        """When nothing within max_tolerance, falls back to any available."""
        candidates = self._make_pool([1, 1, 1])
        result = pick_cluster_weight_matched(
            candidates,
            set(),
            random.Random(42),
            anchor_weight=10,
            max_tolerance=2,
        )
        assert result is not None
        assert result.weight == 1

    def test_disabled_when_zero(self):
        """max_tolerance=0 returns uniform random (no weight preference)."""
        candidates = self._make_pool([1, 5, 10])
        weights_seen: set[int] = set()
        for seed in range(100):
            r = pick_cluster_weight_matched(
                candidates,
                set(),
                random.Random(seed),
                anchor_weight=5,
                max_tolerance=0,
            )
            assert r is not None
            weights_seen.add(r.weight)
        # With 100 seeds and 3 candidates, all weights should appear
        assert weights_seen == {1, 5, 10}

    def test_filter_fn_composed(self):
        """filter_fn is applied alongside weight matching."""
        c_passant = make_cluster(
            "ok",
            zones=["z_ok"],
            weight=3,
            entry_fogs=[{"fog_id": "e", "zone": "z_ok"}],
            exit_fogs=[{"fog_id": "x", "zone": "z_ok"}],
        )
        c_no_passant = make_cluster(
            "bad",
            zones=["z_bad"],
            weight=3,
            entry_fogs=[],
            exit_fogs=[],
        )
        candidates = [c_passant, c_no_passant]
        result = pick_cluster_weight_matched(
            candidates,
            set(),
            random.Random(42),
            anchor_weight=3,
            filter_fn=can_be_passant_node,
        )
        assert result is not None
        assert result.id == "ok"

    def test_zone_exclusion(self):
        """Candidates with overlapping zones are excluded."""
        candidates = self._make_pool([3, 3, 3])
        used = {"z0", "z1"}  # Exclude first two
        result = pick_cluster_weight_matched(
            candidates,
            used,
            random.Random(42),
            anchor_weight=3,
        )
        assert result is not None
        assert result.id == "c2"

    def test_returns_none_when_empty(self):
        """Returns None when no candidates available."""
        result = pick_cluster_weight_matched(
            [],
            set(),
            random.Random(42),
            anchor_weight=3,
        )
        assert result is None

    def test_reserved_zones_excluded(self):
        """Reserved zones are excluded like used zones."""
        candidates = self._make_pool([3])
        result = pick_cluster_weight_matched(
            candidates,
            set(),
            random.Random(42),
            anchor_weight=3,
            reserved_zones=frozenset({"z0"}),
        )
        assert result is None


def test_parallel_branches_weight_matched():
    """On a PASSANT layer with 2+ branches, clusters have similar weights.

    Uses a pool where weight-1 and weight-8 clusters coexist.
    With weight matching, if branch A gets weight-1, branch B should
    not get weight-8 (too far at tolerance 3).
    """
    # Build a pool with weight variety (large enough for convergence).
    # Include clusters with 2+ entries so merges can happen.
    clusters_list = []
    for i in range(30):
        clusters_list.append(
            make_cluster(
                f"light_{i}",
                zones=[f"l{i}"],
                weight=1,
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"le1_{i}", "zone": f"l{i}"},
                    {"fog_id": f"le2_{i}", "zone": f"l{i}"},
                ],
                exit_fogs=[
                    {"fog_id": f"lx1_{i}", "zone": f"l{i}"},
                    {"fog_id": f"lx2_{i}", "zone": f"l{i}"},
                ],
            )
        )
    for i in range(10):
        clusters_list.append(
            make_cluster(
                f"heavy_{i}",
                zones=[f"h{i}"],
                weight=8,
                cluster_type="mini_dungeon",
                entry_fogs=[
                    {"fog_id": f"he1_{i}", "zone": f"h{i}"},
                    {"fog_id": f"he2_{i}", "zone": f"h{i}"},
                ],
                exit_fogs=[
                    {"fog_id": f"hx1_{i}", "zone": f"h{i}"},
                    {"fog_id": f"hx2_{i}", "zone": f"h{i}"},
                ],
            )
        )
    # Need a start cluster and final boss
    start = make_cluster(
        "start_c",
        zones=["start_z"],
        cluster_type="start",
        weight=1,
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "sx1", "zone": "start_z"},
            {"fog_id": "sx2", "zone": "start_z"},
        ],
    )
    final = make_cluster(
        "final_c",
        zones=["final_z"],
        cluster_type="final_boss",
        weight=3,
        entry_fogs=[
            {"fog_id": "fe1", "zone": "final_z"},
            {"fog_id": "fe2", "zone": "final_z"},
        ],
        exit_fogs=[],
    )
    clusters_list.extend([start, final])

    pool = ClusterPool()
    for c in clusters_list:
        pool.add(c)

    config = Config.from_dict(
        {
            "structure": {
                "max_parallel_paths": 3,
                "min_layers": 4,
                "max_layers": 6,
                "split_probability": 1.0,
                "max_weight_tolerance": 3,
                "max_branch_spacing": 0,
                "final_boss_candidates": {"final_z": 1},
            },
        }
    )

    # Generate multiple DAGs, check weight spread on parallel layers
    max_spreads = []
    for seed in range(50):
        try:
            dag, _log = generate_dag(
                config,
                pool,
                seed=seed,
                boss_candidates=pool.get_by_type("final_boss"),
            )
        except GenerationError:
            continue
        # Find layers with multiple nodes
        layers: dict[int, list[int]] = {}
        for node in dag.nodes.values():
            weights = layers.setdefault(node.layer, [])
            weights.append(node.cluster.weight)
        for _layer_idx, weights in layers.items():
            if len(weights) >= 2:
                max_spreads.append(max(weights) - min(weights))

    # At least some DAGs must have generated successfully
    assert max_spreads, "No DAGs generated successfully"

    # With weight matching (tolerance 3), spreads should improve over baseline.
    # The pool has weight-1 and weight-8 clusters, so when weight-8 is primary
    # (~25% of picks), no secondary matches within tolerance -> fallback.
    # Expected ratio: ~75% within tolerance (vs ~60% without weight matching).
    within_tolerance = sum(1 for s in max_spreads if s <= 3)
    ratio = within_tolerance / len(max_spreads)
    assert ratio >= 0.65, (
        f"Only {ratio:.0%} of parallel layers within tolerance 3. "
        f"Spreads: {sorted(set(max_spreads))}"
    )


def _make_dag_with_start():
    """Helper: create a Dag with a start node and 2 exit fogs."""
    dag = Dag(seed=1)
    start = DagNode(
        id="start",
        cluster=make_cluster(
            "s",
            zones=["sz"],
            cluster_type="start",
            entry_fogs=[],
            exit_fogs=[
                {"fog_id": "sx1", "zone": "sz"},
                {"fog_id": "sx2", "zone": "sz"},
            ],
        ),
        layer=0,
        tier=1,
        entry_fogs=[],
        exit_fogs=[FogRef("sx1", "sz"), FogRef("sx2", "sz")],
    )
    dag.add_node(start)
    dag.start_id = "start"
    return dag


def test_execute_passant_layer_weight_matched():
    """execute_passant_layer uses weight matching: first branch anchors the rest."""
    config = Config.from_dict({"structure": {"max_weight_tolerance": 2}})

    # Run multiple seeds: the second branch should be within tolerance
    # of the first branch when the first pick is not the outlier.
    for seed in range(20):
        test_dag = _make_dag_with_start()
        test_branches = [
            Branch("b0", "start", FogRef("sx1", "sz"), layers_since_last_split=0),
            Branch("b1", "start", FogRef("sx2", "sz"), layers_since_last_split=0),
        ]
        # Rebuild pool each iteration (clusters get consumed)
        test_pool = ClusterPool()
        for i in range(5):
            test_pool.add(
                make_cluster(
                    f"w1_{i}",
                    zones=[f"w1z{i}"],
                    weight=1,
                    cluster_type="mini_dungeon",
                )
            )
        for i in range(5):
            test_pool.add(
                make_cluster(
                    f"w2_{i}",
                    zones=[f"w2z{i}"],
                    weight=2,
                    cluster_type="mini_dungeon",
                )
            )
        test_pool.add(
            make_cluster(
                "outlier",
                zones=["oz"],
                weight=8,
                cluster_type="mini_dungeon",
            )
        )

        result = execute_passant_layer(
            test_dag,
            test_branches,
            1,
            "mini_dungeon",
            test_pool,
            {"sz"},
            random.Random(seed),
            config=config,
        )
        node_a = test_dag.nodes[result[0].current_node_id]
        node_b = test_dag.nodes[result[1].current_node_id]
        spread = abs(node_a.cluster.weight - node_b.cluster.weight)
        # First pick is unconstrained. If weight <= 2, second should
        # match within tol=2 -> spread <= 2.
        # If first is w=8 (outlier, 1/11 chance): fallback picks any.
        if node_a.cluster.weight <= 2:
            assert spread <= 2, (
                f"seed={seed}: spread={spread} "
                f"(weights: {node_a.cluster.weight}, {node_b.cluster.weight})"
            )


def test_execute_merge_layer_weight_matched():
    """execute_merge_layer: non-merged branches weight-match the merge cluster."""
    dag = Dag(seed=1)
    # 3 nodes on layer 0
    for i in range(3):
        n = DagNode(
            id=f"n{i}",
            cluster=make_cluster(
                f"c{i}",
                zones=[f"z{i}"],
                entry_fogs=[{"fog_id": f"e{i}", "zone": f"z{i}"}],
                exit_fogs=[{"fog_id": f"x{i}", "zone": f"z{i}"}],
            ),
            layer=0,
            tier=1,
            entry_fogs=[],
            exit_fogs=[FogRef(f"x{i}", f"z{i}")],
        )
        dag.add_node(n)

    branches = [
        Branch(
            "b0", "n0", FogRef("x0", "z0"), birth_layer=0, layers_since_last_split=3
        ),
        Branch(
            "b1", "n1", FogRef("x1", "z1"), birth_layer=0, layers_since_last_split=3
        ),
        Branch(
            "b2", "n2", FogRef("x2", "z2"), birth_layer=0, layers_since_last_split=3
        ),
    ]

    # Merge cluster (weight 2) + passant candidates
    merge_c = make_cluster(
        "merge",
        zones=["mz"],
        weight=2,
        cluster_type="mini_dungeon",
        entry_fogs=[
            {"fog_id": "me1", "zone": "mz"},
            {"fog_id": "me2", "zone": "mz"},
        ],
        exit_fogs=[{"fog_id": "mx", "zone": "mz"}],
    )
    passant_close = make_cluster(
        "p_close",
        zones=["pz1"],
        weight=2,
        cluster_type="mini_dungeon",
    )
    passant_far = make_cluster(
        "p_far",
        zones=["pz2"],
        weight=10,
        cluster_type="mini_dungeon",
    )
    pool = ClusterPool()
    pool.add(merge_c)
    pool.add(passant_close)
    pool.add(passant_far)

    config = Config.from_dict({"structure": {"max_weight_tolerance": 2}})

    result = execute_merge_layer(
        dag,
        branches,
        1,
        "mini_dungeon",
        pool,
        {"z0", "z1", "z2"},
        random.Random(42),
        config,
    )
    # The non-merged branch should get passant_close (weight 2),
    # not passant_far (weight 10), since merge cluster anchor is weight 2
    passant_branches = [b for b in result if "merged" not in b.id]
    if passant_branches:
        passant_node = dag.nodes[passant_branches[0].current_node_id]
        assert passant_node.cluster.weight == 2
