"""Tests for layer planning module."""

import random

from speedfog.config import RequirementsConfig
from speedfog.planner import _distribute_padding, compute_tier, plan_layer_types


class TestDistributePadding:
    """Tests for _distribute_padding function."""

    def test_zero_padding(self):
        """Zero padding returns empty list."""
        result = _distribute_padding(0, {}, {"mini_dungeon": 64}, random.Random(42))
        assert result == []

    def test_all_below_threshold_falls_back(self):
        """When all types are below threshold, fallback to mini_dungeon."""
        result = _distribute_padding(
            5, {}, {"mini_dungeon": 5, "boss_arena": 10}, random.Random(42)
        )
        assert result == ["mini_dungeon"] * 5

    def test_single_eligible_type(self):
        """Only one type above threshold gets all padding."""
        result = _distribute_padding(
            10, {}, {"mini_dungeon": 60, "boss_arena": 5}, random.Random(42)
        )
        assert all(t == "mini_dungeon" for t in result)
        assert len(result) == 10

    def test_proportional_distribution(self):
        """Larger pools get more padding."""
        result = _distribute_padding(
            30,
            {},
            {"mini_dungeon": 60, "boss_arena": 80, "legacy_dungeon": 40},
            random.Random(42),
        )
        from collections import Counter

        counts = Counter(result)
        assert len(result) == 30
        # boss_arena (80) should get the most
        assert counts["boss_arena"] >= counts["legacy_dungeon"]

    def test_required_counts_reduce_capacity(self):
        """Required counts reduce remaining capacity for padding."""
        result = _distribute_padding(
            10,
            {"mini_dungeon": 50},  # 64 - 50 = 14, below threshold of 20
            {"mini_dungeon": 64, "boss_arena": 80},
            random.Random(42),
        )
        from collections import Counter

        counts = Counter(result)
        # mini_dungeon has only 14 remaining (< 20 threshold), excluded
        assert counts.get("mini_dungeon", 0) == 0
        assert counts["boss_arena"] == 10

    def test_caps_exceeded_triggers_fallback(self):
        """When caps are too restrictive, remainder goes to largest pool."""
        # Each type has 20 remaining, cap = 10 each, total cap = 30
        # padding_needed = 40 exceeds total caps
        result = _distribute_padding(
            40,
            {},
            {"mini_dungeon": 20, "boss_arena": 20, "legacy_dungeon": 40},
            random.Random(42),
        )
        assert len(result) == 40
        from collections import Counter

        counts = Counter(result)
        # legacy_dungeon has the largest pool, gets the overflow
        assert counts["legacy_dungeon"] > counts.get("mini_dungeon", 0)


class TestComputeTier:
    """Tests for compute_tier function."""

    def test_first_layer_tier_1(self):
        """First layer (index 0) should have tier 1."""
        assert compute_tier(0, 10) == 1

    def test_last_layer_tier_28_default(self):
        """Last layer should have tier 28 with default final_tier."""
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
        """All computed tiers must be within [1, final_tier]."""
        for total_layers in range(1, 20):
            for layer_idx in range(total_layers):
                tier = compute_tier(layer_idx, total_layers)
                assert (
                    1 <= tier <= 28
                ), f"Tier {tier} out of bounds for layer {layer_idx}/{total_layers}"

    def test_two_layers_first_and_last(self):
        """Two layers should have tier 1 and final_tier."""
        assert compute_tier(0, 2) == 1
        assert compute_tier(1, 2) == 28  # default final_tier

    def test_tiers_monotonically_increase(self):
        """Tiers should increase or stay the same as layer index increases."""
        for total_layers in [3, 5, 10, 15]:
            tiers = [compute_tier(i, total_layers) for i in range(total_layers)]
            for i in range(1, len(tiers)):
                assert (
                    tiers[i] >= tiers[i - 1]
                ), f"Tiers not monotonic: {tiers} for {total_layers} layers"

    # Tests for configurable final_tier

    def test_custom_final_tier_last_layer(self):
        """Last layer should match custom final_tier."""
        assert compute_tier(9, 10, final_tier=20) == 20
        assert compute_tier(9, 10, final_tier=15) == 15
        assert compute_tier(9, 10, final_tier=10) == 10

    def test_custom_final_tier_first_layer_always_1(self):
        """First layer should always be tier 1 regardless of final_tier."""
        assert compute_tier(0, 10, final_tier=20) == 1
        assert compute_tier(0, 10, final_tier=10) == 1
        assert compute_tier(0, 10, final_tier=5) == 1

    def test_custom_final_tier_linear_interpolation(self):
        """Tiers should interpolate linearly from 1 to final_tier."""
        # With final_tier=10 and 10 layers:
        # layer 0 -> 1, layer 9 -> 10
        # layer 5 (middle) should be around 5 or 6
        tier_mid = compute_tier(5, 10, final_tier=10)
        assert 5 <= tier_mid <= 6

    def test_custom_final_tier_two_layers(self):
        """Two layers with custom final_tier should be 1 and final_tier."""
        assert compute_tier(0, 2, final_tier=15) == 1
        assert compute_tier(1, 2, final_tier=15) == 15

    def test_custom_final_tier_bounds_within_range(self):
        """All tiers should be within [1, final_tier]."""
        for final_tier in [5, 10, 15, 20, 28]:
            for total_layers in range(2, 15):
                for layer_idx in range(total_layers):
                    tier = compute_tier(layer_idx, total_layers, final_tier)
                    assert 1 <= tier <= final_tier, (
                        f"Tier {tier} out of bounds [1, {final_tier}] "
                        f"for layer {layer_idx}/{total_layers}"
                    )

    def test_custom_final_tier_monotonic(self):
        """Tiers should be monotonically increasing with custom final_tier."""
        for final_tier in [5, 10, 15, 20]:
            for total_layers in [3, 5, 10]:
                tiers = [
                    compute_tier(i, total_layers, final_tier)
                    for i in range(total_layers)
                ]
                for i in range(1, len(tiers)):
                    assert tiers[i] >= tiers[i - 1], (
                        f"Tiers not monotonic: {tiers} "
                        f"for {total_layers} layers, final_tier={final_tier}"
                    )

    def test_final_tier_clamped_to_valid_range(self):
        """Final tier should be clamped to [1, 28]."""
        # final_tier > 28 should be clamped to 28
        assert compute_tier(9, 10, final_tier=50) == 28
        # final_tier < 1 should be clamped to 1
        assert compute_tier(9, 10, final_tier=0) == 1
        assert compute_tier(9, 10, final_tier=-5) == 1

    # Tests for power curve

    def test_power_curve_first_layer_always_1(self):
        """First layer should always be tier 1 with power curve."""
        assert compute_tier(0, 10, curve="power", exponent=0.6) == 1
        assert compute_tier(0, 10, curve="power", exponent=1.5) == 1

    def test_power_curve_last_layer_matches_final_tier(self):
        """Last layer should always match final_tier with power curve."""
        assert compute_tier(9, 10, final_tier=20, curve="power", exponent=0.6) == 20
        assert compute_tier(9, 10, final_tier=17, curve="power", exponent=1.5) == 17

    def test_power_curve_exponent_1_matches_linear(self):
        """Power curve with exponent=1.0 should be identical to linear."""
        for total in [5, 10, 20, 30]:
            for i in range(total):
                linear_tier = compute_tier(i, total, final_tier=20)
                power_tier = compute_tier(
                    i, total, final_tier=20, curve="power", exponent=1.0
                )
                assert (
                    linear_tier == power_tier
                ), f"layer {i}/{total}: linear={linear_tier} != power(1.0)={power_tier}"

    def test_power_curve_front_loaded(self):
        """Exponent < 1 should front-load tiers (higher early, lower late vs linear)."""
        total, ft = 30, 20
        # Compare midpoint: power(0.6) should be higher than linear at early layers
        linear_early = compute_tier(5, total, ft)
        power_early = compute_tier(5, total, ft, curve="power", exponent=0.6)
        assert (
            power_early > linear_early
        ), f"Power(0.6) at layer 5 should exceed linear: {power_early} vs {linear_early}"

    def test_power_curve_back_loaded(self):
        """Exponent > 1 should back-load tiers (lower early, higher late vs linear)."""
        total, ft = 30, 20
        # Early layers should be lower than linear
        linear_early = compute_tier(5, total, ft)
        power_early = compute_tier(5, total, ft, curve="power", exponent=2.0)
        assert (
            power_early < linear_early
        ), f"Power(2.0) at layer 5 should be below linear: {power_early} vs {linear_early}"

    def test_power_curve_monotonically_increasing(self):
        """Power curve tiers should be monotonically increasing."""
        for exp in [0.4, 0.6, 1.0, 1.5, 2.0]:
            for total in [5, 10, 20, 30]:
                tiers = [
                    compute_tier(i, total, 20, curve="power", exponent=exp)
                    for i in range(total)
                ]
                for i in range(1, len(tiers)):
                    assert (
                        tiers[i] >= tiers[i - 1]
                    ), f"Not monotonic with exp={exp}, total={total}: {tiers}"

    def test_power_curve_bounds(self):
        """All power curve tiers should be within [1, final_tier]."""
        for exp in [0.3, 0.6, 1.0, 1.5, 3.0]:
            for ft in [10, 17, 20, 28]:
                for total in range(2, 35):
                    for i in range(total):
                        tier = compute_tier(i, total, ft, curve="power", exponent=exp)
                        assert 1 <= tier <= ft, (
                            f"Tier {tier} out of [1, {ft}] at "
                            f"layer {i}/{total}, exp={exp}"
                        )

    def test_layer_beyond_estimated_total_clamped(self):
        """Tiers must stay within [1, final_tier] even when layer_idx >= total_layers.

        This happens when forced merges or prerequisite injections add extra
        layers beyond the initial estimate. Before the fix, power curves with
        progress > 1.0 produced tiers > 28, crashing FogMod's EldenScaling.
        """
        for exp in [0.6, 1.0, 1.8, 3.0]:
            for ft in [12, 20, 28]:
                for overshoot in range(1, 15):
                    total = 30
                    layer = total + overshoot
                    tier = compute_tier(layer, total, ft, curve="power", exponent=exp)
                    assert 1 <= tier <= ft, (
                        f"Tier {tier} out of [1, {ft}] at "
                        f"layer {layer}/{total}, exp={exp}"
                    )

    def test_power_curve_single_layer(self):
        """Single layer should return tier 1 regardless of curve settings."""
        assert compute_tier(0, 1, curve="power", exponent=0.6) == 1

    def test_unknown_curve_raises_error(self):
        """Unknown curve name should raise ValueError."""
        import pytest

        with pytest.raises(ValueError, match="Unknown tier curve"):
            compute_tier(5, 10, curve="sigmoid")

    # Tests for start_tier

    def test_start_tier_first_layer(self):
        """First layer should match start_tier."""
        assert compute_tier(0, 10, start_tier=5) == 5
        assert compute_tier(0, 10, start_tier=10) == 10

    def test_start_tier_last_layer_matches_final_tier(self):
        """Last layer should still match final_tier."""
        assert compute_tier(9, 10, final_tier=28, start_tier=5) == 28
        assert compute_tier(9, 10, final_tier=20, start_tier=10) == 20

    def test_start_tier_interpolation(self):
        """Middle layers should interpolate between start_tier and final_tier."""
        # With start_tier=5, final_tier=28, 10 layers:
        # layer 0 -> 5, layer 9 -> 28, midpoint ~16-17
        tier_mid = compute_tier(5, 10, final_tier=28, start_tier=5)
        assert 5 < tier_mid < 28

    def test_start_tier_single_layer(self):
        """Single layer should return start_tier."""
        assert compute_tier(0, 1, start_tier=5) == 5

    def test_start_tier_two_layers(self):
        """Two layers should be start_tier and final_tier."""
        assert compute_tier(0, 2, final_tier=20, start_tier=5) == 5
        assert compute_tier(1, 2, final_tier=20, start_tier=5) == 20

    def test_start_tier_bounds(self):
        """All tiers should be within [start_tier, final_tier]."""
        for st in [1, 3, 5, 10]:
            for ft in [st, st + 5, 28]:
                if ft > 28:
                    continue
                for total in range(2, 15):
                    for i in range(total):
                        tier = compute_tier(i, total, ft, start_tier=st)
                        assert st <= tier <= ft, (
                            f"Tier {tier} out of [{st}, {ft}] " f"for layer {i}/{total}"
                        )

    def test_start_tier_monotonic(self):
        """Tiers should be monotonically increasing with start_tier."""
        for st in [3, 5, 10]:
            for ft in [st + 5, 28]:
                if ft > 28:
                    continue
                for total in [5, 10, 20]:
                    tiers = [
                        compute_tier(i, total, ft, start_tier=st) for i in range(total)
                    ]
                    for i in range(1, len(tiers)):
                        assert tiers[i] >= tiers[i - 1], (
                            f"Not monotonic: {tiers} "
                            f"for start_tier={st}, final_tier={ft}, total={total}"
                        )

    def test_start_tier_with_power_curve(self):
        """start_tier should work with power curve."""
        tier_first = compute_tier(0, 30, 28, start_tier=5, curve="power", exponent=0.7)
        tier_last = compute_tier(29, 30, 28, start_tier=5, curve="power", exponent=0.7)
        assert tier_first == 5
        assert tier_last == 28

    def test_start_tier_clamped(self):
        """start_tier below 1 should be clamped to 1."""
        assert compute_tier(0, 10, start_tier=0) == 1
        assert compute_tier(0, 10, start_tier=-5) == 1

    def test_start_tier_equal_final_tier(self):
        """When start_tier == final_tier, all layers should have that tier."""
        for i in range(10):
            assert compute_tier(i, 10, final_tier=15, start_tier=15) == 15

    def test_start_tier_default_backward_compatible(self):
        """Default start_tier=1 should produce identical results to old behavior."""
        for total in [5, 10, 20]:
            for i in range(total):
                assert compute_tier(i, total, 20) == compute_tier(
                    i, total, 20, start_tier=1
                )


class TestPlanLayerTypes:
    """Tests for plan_layer_types function."""

    def test_includes_required_legacy_dungeons(self):
        """Output should include required number of legacy_dungeons."""
        reqs = RequirementsConfig(
            legacy_dungeons=2, bosses=0, mini_dungeons=0, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        assert result.count("legacy_dungeon") == 2

    def test_includes_required_bosses(self):
        """Output should include required number of boss_arenas."""
        reqs = RequirementsConfig(
            legacy_dungeons=0, bosses=3, mini_dungeons=0, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        assert result.count("boss_arena") == 3

    def test_includes_required_mini_dungeons(self):
        """Output should include at least the required number of mini_dungeons."""
        reqs = RequirementsConfig(
            legacy_dungeons=0, bosses=0, mini_dungeons=4, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        # Padding uses mini_dungeons, so we may have more than required
        assert result.count("mini_dungeon") >= 4

    def test_output_length_matches_total(self):
        """Output list length should match total_layers."""
        reqs = RequirementsConfig(
            legacy_dungeons=1, bosses=2, mini_dungeons=1, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=8, rng=rng)
        assert len(result) == 8

    def test_pads_with_mini_dungeons_no_pool(self):
        """Should pad with mini_dungeons when no pool_sizes provided."""
        reqs = RequirementsConfig(
            legacy_dungeons=1, bosses=1, mini_dungeons=1, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng)
        assert len(result) == 10
        # 1 legacy + 1 boss + 1 mini = 3 required, 7 more mini_dungeons for padding
        assert result.count("mini_dungeon") == 8  # 1 required + 7 padding

    def test_pads_proportionally_with_pool_sizes(self):
        """Should distribute padding across types based on pool capacity."""
        reqs = RequirementsConfig(
            legacy_dungeons=1, bosses=1, mini_dungeons=1, major_bosses=0
        )
        rng = random.Random(42)
        pool_sizes = {"mini_dungeon": 60, "boss_arena": 80, "legacy_dungeon": 30}
        result = plan_layer_types(reqs, total_layers=20, rng=rng, pool_sizes=pool_sizes)
        assert len(result) == 20
        # All three types should appear (requirements + padding)
        assert result.count("legacy_dungeon") >= 1
        assert result.count("boss_arena") >= 1
        assert result.count("mini_dungeon") >= 1
        # Padding should be distributed, not all mini_dungeon
        assert result.count("mini_dungeon") < 17  # not 1 + 16 padding

    def test_pads_proportionally_respects_capacity(self):
        """Types with larger pools should get more padding."""
        reqs = RequirementsConfig(
            legacy_dungeons=0, bosses=0, mini_dungeons=0, major_bosses=0
        )
        rng = random.Random(42)
        # Realistic pool sizes matching actual clusters.json proportions
        pool_sizes = {"mini_dungeon": 64, "boss_arena": 80, "legacy_dungeon": 28}
        result = plan_layer_types(reqs, total_layers=20, rng=rng, pool_sizes=pool_sizes)
        assert len(result) == 20
        # boss_arena has the largest pool, should get the most padding
        assert result.count("boss_arena") > result.count("legacy_dungeon")

    def test_trims_if_too_many_requirements(self):
        """Should trim requirements if they exceed total_layers."""
        reqs = RequirementsConfig(
            legacy_dungeons=3, bosses=4, mini_dungeons=5, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=5, rng=rng)
        assert len(result) == 5

    def test_shuffled_order(self):
        """Output should be shuffled (not in fixed order)."""
        reqs = RequirementsConfig(
            legacy_dungeons=2, bosses=2, mini_dungeons=2, major_bosses=0
        )
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
        reqs = RequirementsConfig(
            legacy_dungeons=2, bosses=2, mini_dungeons=2, major_bosses=0
        )

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
        reqs = RequirementsConfig(
            legacy_dungeons=2, bosses=2, mini_dungeons=2, major_bosses=0
        )

        rng1 = random.Random(42)
        result1 = plan_layer_types(reqs, total_layers=6, rng=rng1)

        rng2 = random.Random(42)
        result2 = plan_layer_types(reqs, total_layers=6, rng=rng2)

        assert result1 == result2

    def test_all_requirements_present_after_shuffle(self):
        """All required types should be present after shuffling."""
        reqs = RequirementsConfig(
            legacy_dungeons=1, bosses=3, mini_dungeons=2, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=6, rng=rng)

        assert result.count("legacy_dungeon") >= 1
        assert result.count("boss_arena") >= 3
        assert result.count("mini_dungeon") >= 2

    def test_major_bosses_zero_no_major_boss(self):
        """With major_bosses=0, no major_boss should appear."""
        reqs = RequirementsConfig(
            legacy_dungeons=1, bosses=2, mini_dungeons=2, major_bosses=0
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng)
        assert "major_boss" not in result

    def test_major_bosses_included_in_plan(self):
        """major_bosses are included in the plan like other types."""
        reqs = RequirementsConfig(
            legacy_dungeons=1, bosses=2, mini_dungeons=2, major_bosses=3
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng)
        assert result.count("major_boss") >= 3

    def test_major_bosses_do_not_overwrite_requirements(self):
        """major_bosses should not reduce the count of other required types."""
        reqs = RequirementsConfig(
            legacy_dungeons=2, bosses=5, mini_dungeons=10, major_bosses=8
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=25, rng=rng)
        assert result.count("legacy_dungeon") >= 2
        assert result.count("boss_arena") >= 5
        assert result.count("mini_dungeon") >= 10
        assert result.count("major_boss") >= 8

    def test_major_bosses_exact_count_no_padding(self):
        """When requirements exactly fill total_layers, counts are exact."""
        reqs = RequirementsConfig(
            legacy_dungeons=2, bosses=3, mini_dungeons=3, major_bosses=2
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng)
        assert len(result) == 10
        assert result.count("legacy_dungeon") == 2
        assert result.count("boss_arena") == 3
        assert result.count("mini_dungeon") == 3
        assert result.count("major_boss") == 2

    def test_major_bosses_excluded_from_padding(self):
        """Padding should not add extra major_boss layers."""
        reqs = RequirementsConfig(
            legacy_dungeons=0, bosses=0, mini_dungeons=0, major_bosses=2
        )
        rng = random.Random(42)
        result = plan_layer_types(reqs, total_layers=10, rng=rng)
        assert result.count("major_boss") == 2  # Only the required ones
