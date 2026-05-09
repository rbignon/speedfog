"""Tests for DAG generation logic."""

import random

import pytest

from speedfog.clusters import ClusterData, ClusterPool, fog_matches_spec
from speedfog.config import Config, RequirementsConfig, StructureConfig
from speedfog.generator import (
    GenerationError,
    _filter_exits_by_proximity,
    _mark_cluster_used,
    can_be_merge_node,
    can_be_passant_node,
    can_be_split_node,
    compute_net_exits,
    count_net_exits,
    generate_dag,
    generate_with_retry,
    pick_cluster_uniform,
    pick_cluster_weight_matched,
    select_weighted_final_boss,
    validate_config,
)

_SENTINEL = object()


def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | object = _SENTINEL,
    exit_fogs: list[dict] | object = _SENTINEL,
    allow_entry_as_exit: bool = False,
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
        allow_entry_as_exit=allow_entry_as_exit,
        proximity_groups=proximity_groups or [],
    )


def make_cluster_pool() -> ClusterPool:
    """Create a minimal cluster pool for testing.

    The pool uses a major_boss cluster (``test_final_boss``) as the canonical
    end node so the exit-driven generator can select it via
    ``clusters.get_by_type("major_boss")``. Tests configure
    ``final_boss_candidates={"test_final_boss_zone": 1}`` to point at it.

    Includes:
    - 1 start cluster (with 2 exits)
    - 1 major_boss cluster as the final boss target
    - Several legacy_dungeon, mini_dungeon, boss_arena, and major_boss clusters
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

    # Canonical final boss (major_boss, terminal - no exits so it won't be used
    # as an intermediate node).  Provide 2 entries so route_exits can fan in
    # multiple sources if the DAG converges here with width > 1.
    pool.add(
        make_cluster(
            "test_final_boss",
            zones=["test_final_boss_zone"],
            cluster_type="major_boss",
            weight=5,
            entry_fogs=[
                {"fog_id": "final_entry_a", "zone": "test_final_boss_zone"},
                {"fog_id": "final_entry_b", "zone": "test_final_boss_zone"},
                {"fog_id": "final_entry_c", "zone": "test_final_boss_zone"},
            ],
            exit_fogs=[],
        )
    )

    # Additional major_boss clusters (passant-capable, used as intermediate)
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


def _make_test_config(seed: int = 42, *, layers_count: int = 6) -> Config:
    """Return a Config pointing at the test_final_boss_zone with small layers_count.

    All per-type requirements are zeroed so the small test pool can satisfy them.
    """
    cfg = Config(seed=seed)
    cfg.structure.final_boss_candidates = {"test_final_boss_zone": 1}
    cfg.structure.layers_count = layers_count
    cfg.requirements.legacy_dungeons = 0
    cfg.requirements.bosses = 0
    cfg.requirements.mini_dungeons = 0
    cfg.requirements.major_bosses = 0
    return cfg


def _boss_candidates(pool: ClusterPool) -> list:
    """Return boss candidate clusters (major_boss + final_boss) from pool.

    Mirrors the logic in main.py: boss_candidates is captured before
    filter_passant_incompatible removes dead-end major_boss clusters.
    """
    return pool.get_by_type("major_boss") + pool.get_by_type("final_boss")


# =============================================================================
# generate_dag tests
# =============================================================================


class TestGenerateDag:
    """Tests for generate_dag (exit-driven implementation)."""

    def test_generates_dag_with_fixed_seed(self):
        """Generates a DAG reproducibly with a fixed seed."""
        pool = make_cluster_pool()
        config = _make_test_config(seed=12345)

        dag1, _log1 = generate_dag(config, pool)
        dag2, _log2 = generate_dag(config, pool)

        assert dag1.seed == dag2.seed == 12345
        assert len(dag1.nodes) == len(dag2.nodes)
        assert set(dag1.nodes.keys()) == set(dag2.nodes.keys())

    def test_has_start_and_end_nodes(self):
        """Generated DAG has start and end nodes."""
        pool = make_cluster_pool()
        config = _make_test_config()

        dag, _log = generate_dag(config, pool)

        assert dag.start_id is not None
        assert dag.end_id is not None
        assert dag.start_id in dag.nodes
        assert dag.end_id in dag.nodes

    def test_end_is_final_boss(self):
        """The end node uses the chosen final boss cluster."""
        pool = make_cluster_pool()
        config = _make_test_config()

        dag, _log = generate_dag(config, pool)

        end_node = dag.nodes[dag.end_id]
        assert "test_final_boss_zone" in end_node.cluster.zones

    def test_all_paths_reach_end(self):
        """All paths in the DAG lead from start to end."""
        pool = make_cluster_pool()
        config = _make_test_config()

        dag, _log = generate_dag(config, pool)

        errors = dag.validate_structure()
        assert not errors, f"DAG structure errors: {errors}"

    def test_respects_max_parallel_paths(self):
        """DAG does not exceed max_parallel_paths at any layer."""
        pool = make_cluster_pool()
        config = _make_test_config(layers_count=8)
        config.structure.max_parallel_paths = 2

        dag, _log = generate_dag(config, pool)

        nodes_by_layer: dict[int, int] = {}
        for node in dag.nodes.values():
            layer = node.layer
            nodes_by_layer[layer] = nodes_by_layer.get(layer, 0) + 1

        for layer, count in nodes_by_layer.items():
            assert (
                count <= config.structure.max_parallel_paths
            ), f"Layer {layer} has {count} nodes > max_parallel_paths"

    def test_no_zone_overlap(self):
        """Each zone appears in exactly one node."""
        pool = make_cluster_pool()
        config = _make_test_config()

        dag, _log = generate_dag(config, pool)

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
                "some_boss",
                zones=["some_zone"],
                cluster_type="major_boss",
                entry_fogs=[{"fog_id": "e", "zone": "some_zone"}],
                exit_fogs=[],
            )
        )
        config = Config(seed=42)
        config.structure.final_boss_candidates = {"some_zone": 1}
        config.structure.layers_count = 4

        with pytest.raises(GenerationError, match="[Ss]tart"):
            generate_dag(config, pool)

    def test_raises_if_no_final_boss_candidate(self):
        """Raises GenerationError when no major_boss cluster matches candidates."""
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
        for i in range(5):
            pool.add(
                make_cluster(
                    f"mini_{i}",
                    zones=[f"mini_{i}_zone"],
                    cluster_type="mini_dungeon",
                    entry_fogs=[{"fog_id": f"mini_{i}_e", "zone": f"mini_{i}_zone"}],
                    exit_fogs=[
                        {"fog_id": f"mini_{i}_e", "zone": f"mini_{i}_zone"},
                        {"fog_id": f"mini_{i}_x", "zone": f"mini_{i}_zone"},
                    ],
                )
            )
        # No cluster has zone "nonexistent_zone", so selection fails.
        config = Config(seed=42)
        config.structure.final_boss_candidates = {"nonexistent_zone": 1}
        config.structure.layers_count = 4
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        with pytest.raises(GenerationError, match="[Ff]inal"):
            generate_dag(config, pool)

    def test_layer_tiers_increase(self):
        """Difficulty tier (weakly) increases with layer index."""
        pool = make_cluster_pool()
        config = _make_test_config(layers_count=8)

        dag, _log = generate_dag(config, pool)

        tiers_by_layer: dict[int, list[int]] = {}
        for node in dag.nodes.values():
            layer = node.layer
            if layer not in tiers_by_layer:
                tiers_by_layer[layer] = []
            tiers_by_layer[layer].append(node.tier)

        layers = sorted(tiers_by_layer.keys())
        for i in range(len(layers) - 1):
            avg_current = sum(tiers_by_layer[layers[i]]) / len(
                tiers_by_layer[layers[i]]
            )
            avg_next = sum(tiers_by_layer[layers[i + 1]]) / len(
                tiers_by_layer[layers[i + 1]]
            )
            assert avg_next >= avg_current


# =============================================================================
# generate_with_retry tests
# =============================================================================


class TestGenerateWithRetry:
    """Tests for generate_with_retry (exit-driven implementation)."""

    def test_fixed_seed_single_attempt(self):
        """With non-zero seed, uses that seed directly (single attempt)."""
        pool = make_cluster_pool()
        config = _make_test_config(seed=99999)

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
        config = _make_test_config(seed=0)

        result = generate_with_retry(
            config, pool, max_attempts=100, boss_candidates=_boss_candidates(pool)
        )

        assert result.seed != 0
        assert result.dag.seed == result.seed
        assert len(result.dag.nodes) > 0
        assert result.validation.is_valid

    def test_raises_after_max_attempts(self):
        """Raises GenerationError after max_attempts failures (no start cluster)."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "some_boss",
                zones=["some_zone"],
                cluster_type="major_boss",
                entry_fogs=[{"fog_id": "e", "zone": "some_zone"}],
                exit_fogs=[],
            )
        )
        config = Config(seed=0)
        config.structure.final_boss_candidates = {"some_zone": 1}
        config.structure.layers_count = 4
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 0

        with pytest.raises(GenerationError, match="after.*attempts"):
            generate_with_retry(
                config, pool, max_attempts=5, boss_candidates=_boss_candidates(pool)
            )

    def test_fixed_seed_propagates_error(self):
        """With fixed seed that fails, propagates the error."""
        pool = ClusterPool()
        pool.add(
            make_cluster(
                "some_boss",
                zones=["some_zone"],
                cluster_type="major_boss",
                entry_fogs=[{"fog_id": "e", "zone": "some_zone"}],
                exit_fogs=[],
            )
        )
        config = Config(seed=42)
        config.structure.final_boss_candidates = {"some_zone": 1}
        config.structure.layers_count = 4
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0
        config.requirements.major_bosses = 0

        with pytest.raises(GenerationError):
            generate_with_retry(config, pool, boss_candidates=_boss_candidates(pool))

    def test_post_validate_triggers_retry_in_auto_mode(self):
        """post_validate rejecting the first N seeds forces the loop to keep
        rerolling, and the accepted (dag, seed) is what the hook last saw."""
        pool = make_cluster_pool()
        config = _make_test_config(seed=0)

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
        config = _make_test_config(seed=99999)

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

    def _cfg(self) -> Config:
        """Return a minimally valid Config for the test pool.

        Points at test_final_boss_zone (exists in make_cluster_pool()) and
        zeros all requirements so the small pool doesn't trigger oversubscription
        errors that would obscure what each test is actually checking.
        """
        cfg = Config()
        cfg.structure.final_boss_candidates = {"test_final_boss_zone": 1}
        cfg.requirements.major_bosses = 0
        return cfg

    def test_valid_config_returns_empty_list(self):
        """Valid configuration returns no errors."""
        pool = make_cluster_pool()
        config = self._cfg()
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_invalid_first_layer_type(self):
        """Invalid first_layer_type returns error."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.structure.first_layer_type = "invalid_type"
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "first_layer_type" in errors[0]
        assert "invalid_type" in errors[0]

    def test_valid_first_layer_type(self):
        """Valid first_layer_type returns no error."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.structure.first_layer_type = "legacy_dungeon"
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_major_bosses_negative_validation(self):
        """Negative major_bosses returns error."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.requirements.major_bosses = -1
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "major_bosses" in errors[0]

    def test_major_bosses_zero_valid(self):
        """major_bosses=0 is valid (no major bosses)."""
        pool = make_cluster_pool()
        config = self._cfg()
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_major_bosses_positive_valid(self):
        """Positive major_bosses is valid."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.requirements.major_bosses = 8
        config.structure.layers_count = 50
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_unknown_final_boss_candidate(self):
        """Unknown zone in final_boss_candidates returns error."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.structure.final_boss_candidates = {"nonexistent_zone": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "nonexistent_zone" in errors[0]

    def test_valid_final_boss_candidate(self):
        """Valid zone in final_boss_candidates returns no error."""
        pool = make_cluster_pool()
        config = self._cfg()
        # test_final_boss_zone exists in the fixture (already set by _cfg)
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_final_boss_candidates_all_keyword(self):
        """'all' keyword in final_boss_candidates is valid."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.structure.final_boss_candidates = {"all": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert errors == []

    def test_invalid_weight_returns_error(self):
        """Weight < 1 in final_boss_candidates returns error."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.structure.final_boss_candidates = {"test_final_boss_zone": 0}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 1
        assert "invalid weight" in errors[0]

    def test_multiple_errors_returned(self):
        """Multiple config errors are all returned."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.structure.first_layer_type = "bad_type"
        config.requirements.major_bosses = -1
        config.structure.final_boss_candidates = {"bad_zone": 1}
        errors, _ = validate_config(config, pool, _boss_candidates(pool))
        assert len(errors) == 3

    def test_requirements_within_layers_no_warning(self):
        """No warning when requirements fit within layers_count."""
        pool = make_cluster_pool()
        config = self._cfg()
        config.requirements.legacy_dungeons = 1
        config.requirements.bosses = 2
        config.requirements.mini_dungeons = 2
        config.requirements.major_bosses = 0
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
        # Only 5 major_boss clusters
        for i in range(5):
            pool.add(
                make_cluster(
                    f"major_{i}",
                    zones=[f"major_{i}_z"],
                    cluster_type="major_boss",
                    entry_fogs=[{"fog_id": f"e{i}", "zone": f"major_{i}_z"}],
                    exit_fogs=[],
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

        config = self._cfg()
        # Override final_boss_candidates to a zone in this pool
        config.structure.final_boss_candidates = {"major_0_z": 1}
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
        config = self._cfg()
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

        config = self._cfg()
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


class TestCanBeMergeNode:
    """Tests for can_be_merge_node."""

    def test_two_entries_one_exit_qualifies(self):
        """2 entries + 1 exit qualifies for merge(2+); fan-in doesn't matter."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[{"fog_id": "exit_a", "zone": "merge_test"}],
        )
        assert can_be_merge_node(cluster, 2) is True
        assert can_be_merge_node(cluster, 3) is True  # fan-in doesn't matter

    def test_needs_at_least_two_entries(self):
        """Requires 2+ entries (spec constraint)."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[{"fog_id": "entry_a", "zone": "merge_test"}],
            exit_fogs=[{"fog_id": "exit_a", "zone": "merge_test"}],
        )
        assert can_be_merge_node(cluster, 2) is False

    def test_needs_at_least_one_exit(self):
        """Still needs at least 1 exit."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "merge_test"},
                {"fog_id": "entry_b", "zone": "merge_test"},
            ],
            exit_fogs=[],
        )
        assert can_be_merge_node(cluster, 2) is False

    def test_bidirectional_entry_still_qualifies(self):
        """Bidirectional entry fog still qualifies when 2+ entries present."""
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
        )
        assert can_be_merge_node(cluster, 2) is True

    def test_single_entry_single_exit_bidirectional_fails(self):
        """1 entry, 1 exit (bidirectional) - only 1 entry, not merge-eligible."""
        cluster = make_cluster(
            "merge_test",
            entry_fogs=[{"fog_id": "bidir", "zone": "merge_test"}],
            exit_fogs=[{"fog_id": "bidir", "zone": "merge_test"}],
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

    # NOTE: test_pick_entry_with_max_exits_respects_proximity and
    # test_pick_entry_and_exits_filters_proximity deleted -- those functions
    # (pick_entry_with_max_exits, _pick_entry_and_exits_for_node) no longer
    # exist in generator.py after the exit-driven cutover.


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


def test_validate_config_rejects_oversubscribed_layers_count():
    from speedfog.clusters import ClusterPool
    from speedfog.config import (
        BudgetConfig,
        Config,
    )
    from speedfog.generator import validate_config

    cfg = Config(
        seed=1,
        requirements=RequirementsConfig(
            legacy_dungeons=4, bosses=4, mini_dungeons=4, major_bosses=0
        ),
        structure=StructureConfig(layers_count=10),  # budget = 8
        budget=BudgetConfig(),
    )
    pool = ClusterPool()
    errors, _warnings = validate_config(cfg, pool, boss_candidates=[])
    assert any("layers_count" in e and "requirements" in e for e in errors)


# ---------------------------------------------------------------------------
# Tests migrated from test_generator_v2.py
# ---------------------------------------------------------------------------


def _mk_cluster_v2(cid: str, ctype: str, weight: int = 10) -> ClusterData:
    return ClusterData(
        id=cid,
        zones=[cid],
        type=ctype,
        weight=weight,
        entry_fogs=[{"fog_id": "E", "zone": cid}],
        exit_fogs=[{"fog_id": "X", "zone": cid}],
    )


def test_pick_layer_clusters_returns_requested_type_when_available():
    from speedfog.generator import pick_layer_clusters

    pool = ClusterPool()
    for i in range(5):
        pool.add(_mk_cluster_v2(f"md_{i}", "mini_dungeon", weight=10))
    rng = random.Random(0)
    picked, fallbacks = pick_layer_clusters(
        width=3,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=rng,
    )
    assert len(picked) == 3
    assert all(c.type == "mini_dungeon" for c in picked)
    assert fallbacks == []


def test_pick_layer_clusters_falls_back_when_type_pool_exhausted():
    from speedfog.generator import pick_layer_clusters

    pool = ClusterPool()
    pool.add(_mk_cluster_v2("md_0", "mini_dungeon"))
    pool.add(_mk_cluster_v2("ba_0", "boss_arena"))
    pool.add(_mk_cluster_v2("ba_1", "boss_arena"))
    rng = random.Random(0)
    picked, fallbacks = pick_layer_clusters(
        width=3,
        layer_type="mini_dungeon",
        clusters=pool,
        used_zones=set(),
        rng=rng,
        allowed_types=("mini_dungeon", "boss_arena"),
    )
    assert len(picked) == 3
    types = sorted(c.type for c in picked)
    assert types == ["boss_arena", "boss_arena", "mini_dungeon"]
    assert len(fallbacks) == 2  # two boss_arenas were fallbacks
    assert all(f.preferred_type == "mini_dungeon" for f in fallbacks)


def test_pick_layer_clusters_fails_when_no_compatible_remaining():
    from speedfog.generator import GenerationError, pick_layer_clusters

    pool = ClusterPool()
    pool.add(_mk_cluster_v2("md_0", "mini_dungeon"))
    rng = random.Random(0)

    with pytest.raises(GenerationError):
        pick_layer_clusters(
            width=3,
            layer_type="mini_dungeon",
            clusters=pool,
            used_zones=set(),
            rng=rng,
            allowed_types=("mini_dungeon",),
        )


def test_generator_v2_corpus_validity_50_seeds():
    from pathlib import Path

    from speedfog.clusters import load_clusters
    from speedfog.config import (
        BudgetConfig,
        Config,
        RequirementsConfig,
        StructureConfig,
    )
    from speedfog.generator import generate_dag
    from speedfog.validator import validate_dag

    pool = load_clusters(Path(__file__).parent.parent / "data" / "clusters.json")
    pool.merge_roundtable_into_start()
    pool.filter_passant_incompatible()

    failures: list[tuple[int, str]] = []
    for seed in range(1000, 1050):
        cfg = Config(
            seed=seed,
            requirements=RequirementsConfig(
                legacy_dungeons=1,
                bosses=3,
                mini_dungeons=3,
                major_bosses=1,
            ),
            structure=StructureConfig(
                layers_count=20,
                max_parallel_paths=4,
                final_boss_candidates={"leyndell_throne": 1},
            ),
            budget=BudgetConfig(),
        )
        try:
            dag, _ = generate_dag(cfg, pool)
        except Exception as e:
            failures.append((seed, f"GENERATION: {e}"))
            continue
        struct_errors = dag.validate_structure()
        if struct_errors:
            failures.append((seed, f"STRUCT: {struct_errors[:3]}"))
            continue
        result = validate_dag(dag, cfg)
        if not result.is_valid:
            failures.append((seed, f"VALIDATOR: {result.errors[:3]}"))

    assert not failures, f"{len(failures)}/50 seeds failed: {failures[:5]}"


def test_generate_dag_v2_produces_exact_layers_count():
    from pathlib import Path

    from speedfog.clusters import load_clusters
    from speedfog.config import (
        BudgetConfig,
        Config,
        RequirementsConfig,
        StructureConfig,
    )
    from speedfog.generator import generate_dag

    pool = load_clusters(Path(__file__).parent.parent / "data" / "clusters.json")
    pool.merge_roundtable_into_start()
    pool.filter_passant_incompatible()

    cfg = Config(
        seed=12345,
        requirements=RequirementsConfig(
            legacy_dungeons=1,
            bosses=3,
            mini_dungeons=3,
            major_bosses=1,
            allowed_types=[
                "mini_dungeon",
                "boss_arena",
                "legacy_dungeon",
                "major_boss",
            ],
        ),
        structure=StructureConfig(
            layers_count=15,
            max_parallel_paths=4,
            final_boss_candidates={"leyndell_throne": 1},
        ),
        budget=BudgetConfig(),
    )
    dag, log = generate_dag(cfg, pool)

    layers = {n.layer for n in dag.nodes.values()}
    assert layers == set(range(15)), f"Expected 15 layers, got {sorted(layers)}"
    # Validator
    errors = dag.validate_structure()
    assert errors == [], errors


# ---------------------------------------------------------------------------
# Tests migrated from test_route_exits.py
# ---------------------------------------------------------------------------

from speedfog.dag import Dag, DagNode, FogRef  # noqa: E402


def _mk_cluster_re(
    cid: str, entries: list[tuple[str, str]], exits: list[tuple[str, str]]
) -> ClusterData:
    return ClusterData(
        id=cid,
        zones=[cid],
        type="mini_dungeon",
        weight=10,
        entry_fogs=[{"fog_id": fid, "zone": z} for fid, z in entries],
        exit_fogs=[{"fog_id": fid, "zone": z} for fid, z in exits],
    )


def _mk_node_re(
    cluster: ClusterData, layer: int, entry: FogRef | None = None
) -> DagNode:
    return DagNode(
        id=f"node_{cluster.id}",
        cluster=cluster,
        layer=layer,
        tier=1,
        entry_fogs=[entry] if entry else [],
        exit_fogs=[],
    )


def test_count_node_net_exits_no_entry_returns_all_exits():
    from speedfog.generator import count_node_net_exits

    c = _mk_cluster_re("a", entries=[], exits=[("F1", "z1"), ("F2", "z1")])
    node = _mk_node_re(c, layer=0)
    dag = Dag(seed=0)
    dag.add_node(node)
    assert count_node_net_exits(dag, node.id) == 2


def test_count_node_net_exits_subtracts_consumed_entry():
    from speedfog.generator import count_node_net_exits

    # Same fog appears as entry and exit (bidirectional) -- entry consumes it.
    c = _mk_cluster_re("a", entries=[("F1", "z1")], exits=[("F1", "z1"), ("F2", "z1")])
    node = _mk_node_re(c, layer=1, entry=FogRef("F1", "z1"))
    dag = Dag(seed=0)
    dag.add_node(node)
    assert count_node_net_exits(dag, node.id) == 1


def test_compute_target_width_saturation_under_cap():
    from speedfog.generator import compute_target_width

    # remaining > current_width -> saturation, capped at max_parallel_paths
    assert (
        compute_target_width(
            remaining=20, current_width=2, sum_exits=4, max_parallel_paths=5
        )
        == 4
    )


def test_compute_target_width_saturation_at_cap():
    from speedfog.generator import compute_target_width

    assert (
        compute_target_width(
            remaining=20, current_width=3, sum_exits=12, max_parallel_paths=5
        )
        == 5
    )


def test_compute_target_width_convergence_decrements_one():
    from speedfog.generator import compute_target_width

    # remaining == current_width -> countdown
    assert (
        compute_target_width(
            remaining=4, current_width=4, sum_exits=99, max_parallel_paths=5
        )
        == 3
    )


def test_compute_target_width_convergence_terminates_at_one():
    from speedfog.generator import compute_target_width

    assert (
        compute_target_width(
            remaining=2, current_width=2, sum_exits=99, max_parallel_paths=5
        )
        == 1
    )


def test_connect_nodes_creates_edge_with_unique_fogs():
    from speedfog.generator import connect_nodes

    src_c = _mk_cluster_re("s", entries=[], exits=[("F1", "z1"), ("F2", "z1")])
    tgt_c = _mk_cluster_re("t", entries=[("E1", "z2")], exits=[])
    src = _mk_node_re(src_c, layer=0)
    tgt = _mk_node_re(tgt_c, layer=1)
    dag = Dag(seed=0)
    dag.add_node(src)
    dag.add_node(tgt)
    rng = random.Random(42)

    ok = connect_nodes(dag, src, tgt, rng)
    assert ok is True
    assert len(dag.edges) == 1
    edge = dag.edges[0]
    assert edge.source_id == src.id
    assert edge.target_id == tgt.id
    assert (edge.exit_fog.fog_id, edge.exit_fog.zone) in {("F1", "z1"), ("F2", "z1")}
    assert (edge.entry_fog.fog_id, edge.entry_fog.zone) == ("E1", "z2")


def test_connect_nodes_returns_false_when_source_has_no_free_exit():
    from speedfog.generator import connect_nodes

    src_c = _mk_cluster_re("s", entries=[], exits=[("F1", "z1")])
    tgt_c = _mk_cluster_re("t", entries=[("E1", "z2")], exits=[])
    src = _mk_node_re(src_c, layer=0)
    tgt = _mk_node_re(tgt_c, layer=1)
    dag = Dag(seed=0)
    dag.add_node(src)
    dag.add_node(tgt)
    # Pre-consume F1 by adding an outgoing edge.
    dag.add_edge(src.id, tgt.id, FogRef("F1", "z1"), FogRef("E1", "z2"))
    rng = random.Random(42)

    ok = connect_nodes(dag, src, tgt, rng)
    assert ok is False
    assert len(dag.edges) == 1  # no new edge added


def test_route_exits_phase1_each_target_gets_one_edge():
    from speedfog.generator import route_exits

    s1_c = _mk_cluster_re("s1", entries=[], exits=[("F1", "z1")])
    s2_c = _mk_cluster_re("s2", entries=[], exits=[("F2", "z2")])
    t1_c = _mk_cluster_re("t1", entries=[("E1", "z3")], exits=[])
    t2_c = _mk_cluster_re("t2", entries=[("E2", "z4")], exits=[])
    s1 = _mk_node_re(s1_c, layer=0)
    s2 = _mk_node_re(s2_c, layer=0)
    t1 = _mk_node_re(t1_c, layer=1)
    t2 = _mk_node_re(t2_c, layer=1)
    dag = Dag(seed=0)
    for n in (s1, s2, t1, t2):
        dag.add_node(n)
    rng = random.Random(0)

    route_exits(dag, sources=[s1, s2], targets=[t1, t2], rng=rng)

    incoming_t1 = dag.get_incoming_edges(t1.id)
    incoming_t2 = dag.get_incoming_edges(t2.id)
    assert len(incoming_t1) >= 1
    assert len(incoming_t2) >= 1


def test_route_exits_raises_when_target_unreachable():
    from speedfog.generator import GenerationError, route_exits

    # Source has 0 free exits, target has no incoming edge possible.
    s_c = _mk_cluster_re(
        "s", entries=[("F1", "z1")], exits=[("F1", "z1")]
    )  # bidirectional
    t_c = _mk_cluster_re("t", entries=[("E1", "z2")], exits=[])
    s = _mk_node_re(s_c, layer=0, entry=FogRef("F1", "z1"))  # consumes the only exit
    t = _mk_node_re(t_c, layer=1)
    dag = Dag(seed=0)
    dag.add_node(s)
    dag.add_node(t)

    with pytest.raises(GenerationError, match="orphan"):
        route_exits(dag, sources=[s], targets=[t], rng=random.Random(0))


def test_route_exits_phase2_saturates_to_all_targets():
    from speedfog.generator import route_exits

    # Source with 3 exits, 3 targets -- every (source, target) pair gets an edge.
    s_c = _mk_cluster_re(
        "s", entries=[], exits=[("F1", "z1"), ("F2", "z1"), ("F3", "z1")]
    )
    t1_c = _mk_cluster_re("t1", entries=[("E1", "z2")], exits=[])
    t2_c = _mk_cluster_re("t2", entries=[("E2", "z3")], exits=[])
    t3_c = _mk_cluster_re("t3", entries=[("E3", "z4")], exits=[])
    s = _mk_node_re(s_c, layer=0)
    t1, t2, t3 = (_mk_node_re(c, layer=1) for c in (t1_c, t2_c, t3_c))
    dag = Dag(seed=0)
    for n in (s, t1, t2, t3):
        dag.add_node(n)
    rng = random.Random(0)

    route_exits(dag, sources=[s], targets=[t1, t2, t3], rng=rng)

    pairs = {(e.source_id, e.target_id) for e in dag.edges}
    assert pairs == {(s.id, t1.id), (s.id, t2.id), (s.id, t3.id)}


def test_route_exits_phase2_drops_surplus_when_more_exits_than_targets():
    from speedfog.generator import route_exits

    # Source has 5 exits, 3 targets -- drop 2 exits (keep 3 unique pairs).
    s_c = _mk_cluster_re(
        "s",
        entries=[],
        exits=[("F1", "z1"), ("F2", "z1"), ("F3", "z1"), ("F4", "z1"), ("F5", "z1")],
    )
    t1_c = _mk_cluster_re("t1", entries=[("E1", "z2")], exits=[])
    t2_c = _mk_cluster_re("t2", entries=[("E2", "z3")], exits=[])
    t3_c = _mk_cluster_re("t3", entries=[("E3", "z4")], exits=[])
    s = _mk_node_re(s_c, layer=0)
    t1, t2, t3 = (_mk_node_re(c, layer=1) for c in (t1_c, t2_c, t3_c))
    dag = Dag(seed=0)
    for n in (s, t1, t2, t3):
        dag.add_node(n)
    rng = random.Random(0)

    route_exits(dag, sources=[s], targets=[t1, t2, t3], rng=rng)

    out_edges = dag.get_outgoing_edges(s.id)
    assert len(out_edges) == 3
    # No multi-edges
    pairs = {(e.source_id, e.target_id) for e in out_edges}
    assert len(pairs) == 3


def test_route_exits_phase2_proximity_diversity_kept():
    from speedfog.generator import route_exits

    # Source has 4 exits in 2 proximity groups (F1-F2, F3-F4), 2 targets:
    # The kept pair should be one from each group.
    s_c = ClusterData(
        id="s",
        zones=["s_zone"],
        type="mini_dungeon",
        weight=10,
        entry_fogs=[],
        exit_fogs=[
            {"fog_id": "F1", "zone": "z1"},
            {"fog_id": "F2", "zone": "z1"},
            {"fog_id": "F3", "zone": "z1"},
            {"fog_id": "F4", "zone": "z1"},
        ],
        proximity_groups=[["F1", "F2"], ["F3", "F4"]],
    )
    t1_c = _mk_cluster_re("t1", entries=[("E1", "z2")], exits=[])
    t2_c = _mk_cluster_re("t2", entries=[("E2", "z3")], exits=[])
    s = _mk_node_re(s_c, layer=0)
    t1, t2 = _mk_node_re(t1_c, layer=1), _mk_node_re(t2_c, layer=1)
    dag = Dag(seed=0)
    for n in (s, t1, t2):
        dag.add_node(n)

    route_exits(dag, sources=[s], targets=[t1, t2], rng=random.Random(0))
    used_fogs = {e.exit_fog.fog_id for e in dag.get_outgoing_edges(s.id)}
    # The two kept exits are from different groups
    in_group_a = used_fogs & {"F1", "F2"}
    in_group_b = used_fogs & {"F3", "F4"}
    assert len(in_group_a) == 1 and len(in_group_b) == 1
