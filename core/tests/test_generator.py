"""Tests for DAG generation logic."""

import random

import pytest

from speedfog_core.clusters import ClusterData, ClusterPool
from speedfog_core.config import Config
from speedfog_core.generator import (
    GenerationError,
    cluster_has_usable_exits,
    generate_dag,
    generate_with_retry,
    pick_cluster,
    pick_entry_fog_with_exits,
)

_SENTINEL = object()


def make_cluster(
    cluster_id: str,
    zones: list[str] | None = None,
    cluster_type: str = "mini_dungeon",
    weight: int = 5,
    entry_fogs: list[dict] | object = _SENTINEL,
    exit_fogs: list[dict] | object = _SENTINEL,
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

    # Final boss
    pool.add(
        make_cluster(
            "erdtree_boss",
            zones=["erdtree_throne"],
            cluster_type="final_boss",
            weight=5,
            entry_fogs=[{"fog_id": "final_entry", "zone": "erdtree_throne"}],
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

    def test_respects_max_branches(self):
        """DAG does not exceed max_branches at any layer."""
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
                zones=["erdtree_throne"],
                cluster_type="final_boss",
                weight=5,
                entry_fogs=[{"fog_id": "final_entry", "zone": "erdtree_throne"}],
                exit_fogs=[],
            )
        )

        # Add merge-compatible clusters (2 entries, 2 exits)
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
                        {"fog_id": f"merge_{i}_exit_a", "zone": f"merge_{i}_zone"},
                        {"fog_id": f"merge_{i}_exit_b", "zone": f"merge_{i}_zone"},
                    ],
                )
            )

        config = Config()
        config.structure.max_branches = 2
        config.structure.min_layers = 4
        config.structure.max_layers = 4
        config.structure.split_probability = 0.2
        config.structure.merge_probability = 0.2
        config.requirements.legacy_dungeons = 0
        config.requirements.bosses = 0
        config.requirements.mini_dungeons = 0

        dag = generate_dag(config, pool, seed=42)

        # Count nodes per layer (excluding start and end)
        nodes_by_layer: dict[int, int] = {}
        for node in dag.nodes.values():
            layer = node.layer
            nodes_by_layer[layer] = nodes_by_layer.get(layer, 0) + 1

        # Each layer should have at most max_branches nodes
        for layer, count in nodes_by_layer.items():
            assert (
                count <= config.structure.max_branches
            ), f"Layer {layer} has {count} nodes > max_branches {config.structure.max_branches}"

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
                zones=["erdtree"],
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
                zones=["erdtree"],
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
                zones=["erdtree"],
                cluster_type="final_boss",
            )
        )
        config = Config()
        config.seed = 42  # Fixed seed

        with pytest.raises(GenerationError):
            generate_with_retry(config, pool)
