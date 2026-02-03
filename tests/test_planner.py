"""Tests for layer planning module."""

import random

from speedfog.config import RequirementsConfig
from speedfog.planner import compute_tier, plan_layer_types


class TestComputeTier:
    """Tests for compute_tier function."""

    def test_first_layer_tier_1(self):
        """First layer (index 0) should have tier 1."""
        assert compute_tier(0, 10) == 1

    def test_last_layer_tier_28(self):
        """Last layer should have tier 28."""
        assert compute_tier(9, 10) == 28

    def test_middle_layers_intermediate(self):
        """Middle layers should have intermediate tiers."""
        # With 10 layers, layer 5 should be roughly in the middle
        tier = compute_tier(5, 10)
        assert 1 < tier < 28

    def test_single_layer_edge_case(self):
        """Single layer should have tier 1 (starting tier)."""
        assert compute_tier(0, 1) == 1

    def test_all_tiers_within_bounds(self):
        """All computed tiers must be within [1, 28]."""
        for total_layers in range(1, 20):
            for layer_idx in range(total_layers):
                tier = compute_tier(layer_idx, total_layers)
                assert (
                    1 <= tier <= 28
                ), f"Tier {tier} out of bounds for layer {layer_idx}/{total_layers}"

    def test_two_layers_first_and_last(self):
        """Two layers should have tier 1 and tier 28."""
        assert compute_tier(0, 2) == 1
        assert compute_tier(1, 2) == 28

    def test_tiers_monotonically_increase(self):
        """Tiers should increase or stay the same as layer index increases."""
        for total_layers in [3, 5, 10, 15]:
            tiers = [compute_tier(i, total_layers) for i in range(total_layers)]
            for i in range(1, len(tiers)):
                assert (
                    tiers[i] >= tiers[i - 1]
                ), f"Tiers not monotonic: {tiers} for {total_layers} layers"


class TestPlanLayerTypes:
    """Tests for plan_layer_types function."""

    def test_includes_required_legacy_dungeons(self):
        """Output should include required number of legacy_dungeons."""
        reqs = RequirementsConfig(legacy_dungeons=2, bosses=0, mini_dungeons=0)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        assert result.count("legacy_dungeon") == 2

    def test_includes_required_bosses(self):
        """Output should include required number of boss_arenas."""
        reqs = RequirementsConfig(legacy_dungeons=0, bosses=3, mini_dungeons=0)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        assert result.count("boss_arena") == 3

    def test_includes_required_mini_dungeons(self):
        """Output should include at least the required number of mini_dungeons."""
        reqs = RequirementsConfig(legacy_dungeons=0, bosses=0, mini_dungeons=4)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        # Padding uses mini_dungeons, so we may have more than required
        assert result.count("mini_dungeon") >= 4

    def test_output_length_matches_total(self):
        """Output list length should match total_layers."""
        reqs = RequirementsConfig(legacy_dungeons=1, bosses=2, mini_dungeons=1)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=8, rng=rng)
        assert len(result) == 8

    def test_pads_with_mini_dungeons(self):
        """Should pad with mini_dungeons when requirements < total_layers."""
        reqs = RequirementsConfig(legacy_dungeons=1, bosses=1, mini_dungeons=1)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng)
        assert len(result) == 10
        # 1 legacy + 1 boss + 1 mini = 3 required, 7 more mini_dungeons for padding
        assert result.count("mini_dungeon") == 8  # 1 required + 7 padding

    def test_trims_if_too_many_requirements(self):
        """Should trim requirements if they exceed total_layers."""
        reqs = RequirementsConfig(legacy_dungeons=3, bosses=4, mini_dungeons=5)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        assert len(result) == 5

    def test_shuffled_order(self):
        """Output should be shuffled (not in fixed order)."""
        reqs = RequirementsConfig(legacy_dungeons=2, bosses=2, mini_dungeons=2)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=6, rng=rng)

        # The result should be shuffled, not in the original order
        # Original order would be: legacy, legacy, boss_arena, boss_arena, mini, mini
        unshuffled = ["legacy_dungeon"] * 2 + ["boss_arena"] * 2 + ["mini_dungeon"] * 2
        # With a fixed seed, the shuffle should produce a different order
        # (statistically very unlikely to remain the same)
        assert result != unshuffled or len(result) <= 1

    def test_different_seeds_different_order(self):
        """Different seeds should produce different orders."""
        reqs = RequirementsConfig(legacy_dungeons=2, bosses=2, mini_dungeons=2)

        rng1 = random.Random(42)
        result1 = plan_layer_types(reqs, total_layers=6, rng=rng1)

        rng2 = random.Random(123)
        result2 = plan_layer_types(reqs, total_layers=6, rng=rng2)

        # Same content but likely different order
        assert sorted(result1) == sorted(result2)
        # Different seeds should produce different orders (very high probability)
        assert result1 != result2

    def test_same_seed_same_order(self):
        """Same seed should produce reproducible results."""
        reqs = RequirementsConfig(legacy_dungeons=2, bosses=2, mini_dungeons=2)

        rng1 = random.Random(42)
        result1 = plan_layer_types(reqs, total_layers=6, rng=rng1)

        rng2 = random.Random(42)
        result2 = plan_layer_types(reqs, total_layers=6, rng=rng2)

        assert result1 == result2

    def test_all_requirements_present_after_shuffle(self):
        """All required types should be present after shuffling."""
        reqs = RequirementsConfig(legacy_dungeons=1, bosses=3, mini_dungeons=2)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=6, rng=rng)

        assert result.count("legacy_dungeon") >= 1
        assert result.count("boss_arena") >= 3
        assert result.count("mini_dungeon") >= 2

    def test_major_boss_ratio_zero_no_major_boss(self):
        """With major_boss_ratio=0, no major_boss should appear."""
        reqs = RequirementsConfig(legacy_dungeons=1, bosses=2, mini_dungeons=2)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng, major_boss_ratio=0.0)
        assert "major_boss" not in result

    def test_major_boss_ratio_adds_major_bosses(self):
        """With major_boss_ratio > 0, major_boss entries should appear."""
        reqs = RequirementsConfig(legacy_dungeons=1, bosses=2, mini_dungeons=2)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng, major_boss_ratio=0.3)
        # 0.3 * 10 = 3 major bosses expected
        assert result.count("major_boss") >= 1

    def test_major_boss_ratio_not_in_last_layer(self):
        """Major bosses should not appear in the last layer."""
        reqs = RequirementsConfig(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        # Test with high ratio to maximize chance of hitting last layer
        for seed in range(100):
            rng = random.Random(seed)
            result = plan_layer_types(
                reqs, total_layers=5, rng=rng, major_boss_ratio=0.8
            )
            # Last layer should never be major_boss
            assert (
                result[-1] != "major_boss"
            ), f"Last layer is major_boss with seed {seed}"

    def test_major_boss_ratio_respects_ratio(self):
        """Number of major bosses should roughly match the ratio."""
        reqs = RequirementsConfig(legacy_dungeons=0, bosses=0, mini_dungeons=0)
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng, major_boss_ratio=0.3)
        # Expect about 3 major bosses (0.3 * 10)
        num_major = result.count("major_boss")
        assert 1 <= num_major <= 5, f"Expected ~3 major_boss, got {num_major}"
