"""Tests for DAG generation logic."""

import random

import pytest

from speedfog.clusters import ClusterData, ClusterPool
from speedfog.config import Config
from speedfog.dag import Branch, Dag, DagNode, FogRef
from speedfog.generator import (
    GenerationError,
    _find_valid_merge_indices,
    _has_valid_merge_pair,
    _stable_main_shuffle,
    can_be_merge_node,
    cluster_has_usable_exits,
    compute_net_exits,
    count_net_exits,
    execute_merge_layer,
    generate_dag,
    generate_with_retry,
    pick_cluster,
    pick_entry_fog_with_exits,
    pick_entry_with_max_exits,
    select_entries_for_merge,
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
    allow_shared_entrance: bool = False,
    allow_entry_as_exit: bool = False,
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
# cluster_has_usable_exits tests
# =============================================================================


class TestClusterHasUsableExits:
    """Tests for cluster_has_usable_exits function."""

    def test_cluster_with_exits_after_entry(self):
        """A cluster with exit fogs remaining after using entry is usable."""
        cluster = make_cluster(
            "test",
            entry_fogs=[{"fog_id": "entry_fog", "zone": "test"}],
            exit_fogs=[
                {"fog_id": "entry_fog", "zone": "test"},  # bidirectional
                {"fog_id": "other_exit", "zone": "test"},  # additional exit
            ],
        )

        assert cluster_has_usable_exits(cluster) is True

    def test_cluster_without_exits_after_entry(self):
        """A cluster with only bidirectional fog is a dead end."""
        cluster = make_cluster(
            "test",
            entry_fogs=[{"fog_id": "bidirectional", "zone": "test"}],
            exit_fogs=[{"fog_id": "bidirectional", "zone": "test"}],
        )

        assert cluster_has_usable_exits(cluster) is False

    def test_cluster_no_entry_fogs(self):
        """A cluster without entry fogs is not usable (can't enter)."""
        cluster = make_cluster(
            "test",
            entry_fogs=[],
            exit_fogs=[{"fog_id": "exit_fog", "zone": "test"}],
        )

        assert cluster_has_usable_exits(cluster) is False

    def test_cluster_multiple_entries_one_has_exits(self):
        """A cluster is usable if at least one entry leaves exits available."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "dead_end_entry", "zone": "test"},
                {"fog_id": "good_entry", "zone": "test"},
            ],
            exit_fogs=[
                {
                    "fog_id": "dead_end_entry",
                    "zone": "test",
                },  # only exit for first entry
                {
                    "fog_id": "other_exit",
                    "zone": "test",
                },  # exit remains for second entry
            ],
        )

        assert cluster_has_usable_exits(cluster) is True


# =============================================================================
# pick_entry_fog_with_exits tests
# =============================================================================


class TestPickEntryFogWithExits:
    """Tests for pick_entry_fog_with_exits function."""

    def test_picks_valid_entry(self):
        """Picks an entry fog that leaves at least one exit available."""
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "test"},
                {"fog_id": "entry_b", "zone": "test"},
            ],
            exit_fogs=[
                {"fog_id": "entry_a", "zone": "test"},  # bidirectional
                {
                    "fog_id": "exit_c",
                    "zone": "test",
                },  # available after using entry_a or entry_b
            ],
        )
        rng = random.Random(42)

        result = pick_entry_fog_with_exits(cluster, rng)

        # Both entries should leave exit_c available
        assert result in ["entry_a", "entry_b"]

    def test_returns_none_if_no_valid_entry(self):
        """Returns None when no entry fog leaves any exits available."""
        cluster = make_cluster(
            "test",
            entry_fogs=[{"fog_id": "only_entry", "zone": "test"}],
            exit_fogs=[{"fog_id": "only_entry", "zone": "test"}],  # only bidirectional
        )
        rng = random.Random(42)

        result = pick_entry_fog_with_exits(cluster, rng)

        assert result is None

    def test_picks_any_valid_entry(self):
        """Picks any entry fog that leaves exits available."""
        # Both entries are valid since each leaves at least one exit
        cluster = make_cluster(
            "test",
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "test"},
                {"fog_id": "entry_b", "zone": "test"},
            ],
            exit_fogs=[
                {"fog_id": "entry_a", "zone": "test"},  # Bidirectional
                {"fog_id": "entry_b", "zone": "test"},  # Bidirectional
                {"fog_id": "real_exit", "zone": "test"},  # Always available
            ],
        )

        # Verify both entries can be selected (different seeds)
        selected = set()
        for seed in range(100):
            rng = random.Random(seed)
            result = pick_entry_fog_with_exits(cluster, rng)
            assert result in ["entry_a", "entry_b"]
            selected.add(result)

        # Both should have been selected at least once
        assert "entry_a" in selected
        assert "entry_b" in selected


# =============================================================================
# pick_cluster tests
# =============================================================================


class TestPickCluster:
    """Tests for pick_cluster function."""

    def test_picks_from_candidates(self):
        """Picks a cluster from the candidates list."""
        candidates = [
            make_cluster("a", zones=["zone_a"]),
            make_cluster("b", zones=["zone_b"]),
        ]
        rng = random.Random(42)

        result = pick_cluster(candidates, set(), rng)

        assert result is not None
        assert result.id in ["a", "b"]

    def test_excludes_used_zones(self):
        """Does not pick clusters whose zones overlap with used_zones."""
        candidates = [
            make_cluster("a", zones=["zone_a"]),
            make_cluster("b", zones=["zone_b"]),
        ]
        used_zones = {"zone_a"}
        rng = random.Random(42)

        result = pick_cluster(candidates, used_zones, rng)

        assert result is not None
        assert result.id == "b"

    def test_returns_none_if_all_used(self):
        """Returns None when all candidates have used zones."""
        candidates = [
            make_cluster("a", zones=["zone_a"]),
            make_cluster("b", zones=["zone_b"]),
        ]
        used_zones = {"zone_a", "zone_b"}
        rng = random.Random(42)

        result = pick_cluster(candidates, used_zones, rng)

        assert result is None

    def test_returns_none_if_empty_candidates(self):
        """Returns None for empty candidates list."""
        candidates: list[ClusterData] = []
        rng = random.Random(42)

        result = pick_cluster(candidates, set(), rng)

        assert result is None

    def test_require_exits_filters_dead_ends(self):
        """With require_exits=True, filters out clusters without usable exits."""
        # Dead end cluster - entry is the only exit
        dead_end = make_cluster(
            "dead",
            zones=["dead_zone"],
            entry_fogs=[{"fog_id": "bidir", "zone": "dead_zone"}],
            exit_fogs=[{"fog_id": "bidir", "zone": "dead_zone"}],
        )
        # Good cluster - has additional exit
        good = make_cluster(
            "good",
            zones=["good_zone"],
            entry_fogs=[{"fog_id": "entry", "zone": "good_zone"}],
            exit_fogs=[
                {"fog_id": "entry", "zone": "good_zone"},
                {"fog_id": "exit", "zone": "good_zone"},
            ],
        )
        candidates = [dead_end, good]
        rng = random.Random(42)

        result = pick_cluster(candidates, set(), rng, require_exits=True)

        assert result is not None
        assert result.id == "good"

    def test_require_exits_false_allows_dead_ends(self):
        """With require_exits=False, allows clusters without usable exits."""
        # Dead end cluster - entry is the only exit
        dead_end = make_cluster(
            "dead",
            zones=["dead_zone"],
            entry_fogs=[{"fog_id": "bidir", "zone": "dead_zone"}],
            exit_fogs=[{"fog_id": "bidir", "zone": "dead_zone"}],
        )
        candidates = [dead_end]
        rng = random.Random(42)

        result = pick_cluster(candidates, set(), rng, require_exits=False)

        assert result is not None
        assert result.id == "dead"


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

        dag1 = generate_dag(config, pool, seed=12345)
        dag2 = generate_dag(config, pool, seed=12345)

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

        dag = generate_dag(config, pool, seed=42)

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

        dag = generate_dag(config, pool, seed=42)
        paths = dag.enumerate_paths()

        assert len(paths) > 0
        for path in paths:
            assert path[0] == "start"
            assert path[-1] == "end"

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
        for i in range(10):
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
        config.structure.min_layers = 4
        config.structure.max_layers = 4
        config.structure.split_probability = 0.2
        config.structure.merge_probability = 0.2
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        dag = generate_dag(config, pool, seed=42)

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

        dag = generate_dag(config, pool, seed=42)

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
            generate_dag(config, pool, seed=42)

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
            generate_dag(config, pool, seed=42)

    def test_layer_tiers_increase(self):
        """Difficulty tier increases with layer index."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.min_layers = 4
        config.structure.max_layers = 4
        config.structure.max_branches = 1  # Single branch avoids merge requirement
        config.structure.split_probability = 0.0
        config.structure.merge_probability = 0.0

        dag = generate_dag(config, pool, seed=42)

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

        result = generate_with_retry(config, pool)

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

        result = generate_with_retry(config, pool, max_attempts=100)

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
            generate_with_retry(config, pool, max_attempts=5)

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
            generate_with_retry(config, pool)


class TestValidateConfig:
    """Tests for validate_config function."""

    def test_valid_config_returns_empty_list(self):
        """Valid configuration returns no errors."""
        pool = make_cluster_pool()
        config = Config()
        errors = validate_config(config, pool)
        assert errors == []

    def test_invalid_first_layer_type(self):
        """Invalid first_layer_type returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.first_layer_type = "invalid_type"
        errors = validate_config(config, pool)
        assert len(errors) == 1
        assert "first_layer_type" in errors[0]
        assert "invalid_type" in errors[0]

    def test_valid_first_layer_type(self):
        """Valid first_layer_type returns no error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.first_layer_type = "legacy_dungeon"
        errors = validate_config(config, pool)
        assert errors == []

    def test_major_boss_ratio_out_of_range_negative(self):
        """Negative major_boss_ratio returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.major_boss_ratio = -0.1
        errors = validate_config(config, pool)
        assert len(errors) == 1
        assert "major_boss_ratio" in errors[0]

    def test_major_boss_ratio_out_of_range_above_one(self):
        """major_boss_ratio > 1.0 returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.major_boss_ratio = 1.5
        errors = validate_config(config, pool)
        assert len(errors) == 1
        assert "major_boss_ratio" in errors[0]

    def test_valid_major_boss_ratio(self):
        """Valid major_boss_ratio returns no error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.major_boss_ratio = 0.5
        errors = validate_config(config, pool)
        assert errors == []

    def test_unknown_final_boss_candidate(self):
        """Unknown zone in final_boss_candidates returns error."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.final_boss_candidates = ["nonexistent_zone"]
        errors = validate_config(config, pool)
        assert len(errors) == 1
        assert "nonexistent_zone" in errors[0]

    def test_valid_final_boss_candidate(self):
        """Valid zone in final_boss_candidates returns no error."""
        pool = make_cluster_pool()
        config = Config()
        # leyndell_erdtree exists in the fixture
        config.structure.final_boss_candidates = ["leyndell_erdtree"]
        errors = validate_config(config, pool)
        assert errors == []

    def test_final_boss_candidates_all_keyword(self):
        """'all' keyword in final_boss_candidates is valid."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.final_boss_candidates = ["all"]
        errors = validate_config(config, pool)
        assert errors == []

    def test_multiple_errors_returned(self):
        """Multiple config errors are all returned."""
        pool = make_cluster_pool()
        config = Config()
        config.structure.first_layer_type = "bad_type"
        config.structure.major_boss_ratio = 2.0
        config.structure.final_boss_candidates = ["bad_zone"]
        errors = validate_config(config, pool)
        assert len(errors) == 3


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
        config.structure.min_layers = 4
        config.structure.max_layers = 8
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
                dag = generate_dag(config, pool, seed=seed)
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

        successes = 0
        for seed in range(1, 201):
            try:
                dag = generate_dag(config, pool, seed=seed)
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
            2,
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
