"""Layer planning module for SpeedFog.

This module handles planning what type of cluster goes in each layer of the DAG.
"""

from __future__ import annotations

import random

from speedfog.config import RequirementsConfig

# Types need at least this many remaining clusters to receive padding.
# Parallel branches can consume ~2 clusters per layer, so we need
# enough headroom to avoid pool exhaustion during generation.
_MIN_REMAINING_FOR_PADDING = 20


def compute_tier(layer_idx: int, total_layers: int, final_tier: int = 28) -> int:
    """Map layer index to difficulty tier.

    Uses linear interpolation to spread tiers across layers.
    First layer gets tier 1, last layer gets final_tier.

    Args:
        layer_idx: Zero-based index of the current layer.
        total_layers: Total number of layers in the DAG.
        final_tier: Maximum tier for the final layer (default 28, range 1-28).

    Returns:
        Difficulty tier between 1 and final_tier (inclusive).
    """
    if total_layers <= 1:
        # Single layer gets the starting tier
        return 1

    # Clamp final_tier to valid range
    final_tier = max(1, min(28, final_tier))

    # Linear interpolation from tier 1 to final_tier
    # layer_idx=0 -> tier 1, layer_idx=total_layers-1 -> final_tier
    progress = layer_idx / (total_layers - 1)
    tier = 1 + progress * (final_tier - 1)

    return int(round(tier))


def _distribute_padding(
    padding_needed: int,
    required_counts: dict[str, int],
    pool_sizes: dict[str, int],
    rng: random.Random,
) -> list[str]:
    """Distribute padding across types proportionally to remaining pool capacity.

    Each type's padding allocation is capped at half its remaining capacity
    when possible, leaving headroom for parallel branches that consume
    multiple clusters per layer.

    Args:
        padding_needed: Number of extra layers to fill.
        required_counts: How many of each type are already committed.
        pool_sizes: Total available clusters per type.
        rng: Random number generator.

    Returns:
        List of type strings for padding layers.
    """
    # Remaining capacity per type = pool_size - already_required
    remaining: dict[str, int] = {}
    for t, pool in pool_sizes.items():
        used = required_counts.get(t, 0)
        left = max(0, pool - used)
        if left >= _MIN_REMAINING_FOR_PADDING:
            remaining[t] = left

    if not remaining:
        return ["mini_dungeon"] * padding_needed

    # Cap per type: at most half of remaining capacity (headroom for branches)
    caps = {t: max(1, cap // 2) for t, cap in remaining.items()}

    # Phase 1: proportional allocation, respecting per-type caps
    total_capacity = sum(remaining.values())
    allocs: dict[str, int] = {}
    for t, cap in remaining.items():
        share = min(round(padding_needed * cap / total_capacity), caps[t])
        allocs[t] = share

    still_needed = padding_needed - sum(allocs.values())

    # Phase 2: distribute remainder randomly, still respecting caps
    if still_needed > 0:
        available = {t: caps[t] - allocs[t] for t in remaining if caps[t] > allocs[t]}
        while still_needed > 0 and available:
            types_list = list(available.keys())
            weights = [remaining[t] for t in types_list]
            pick = rng.choices(types_list, weights=weights, k=1)[0]
            allocs[pick] = allocs.get(pick, 0) + 1
            still_needed -= 1
            if allocs[pick] >= caps[pick]:
                del available[pick]

    # Phase 3: if caps were too restrictive (total caps < padding_needed),
    # relax the 50% cap and assign remainder to the largest-pool type.
    if still_needed > 0:
        largest_type = max(remaining, key=lambda t: remaining[t])
        allocs[largest_type] = allocs.get(largest_type, 0) + still_needed

    result: list[str] = []
    for t, count in allocs.items():
        result.extend([t] * count)

    return result


def plan_layer_types(
    requirements: RequirementsConfig,
    total_layers: int,
    rng: random.Random,
    major_boss_ratio: float = 0.0,
    pool_sizes: dict[str, int] | None = None,
) -> list[str]:
    """Plan sequence of cluster types for each layer.

    Ensures minimum requirements are met, pads with additional layers if needed,
    trims if requirements exceed total_layers, and shuffles the result.
    Then replaces some layers with major_boss based on major_boss_ratio.

    When pool_sizes is provided, padding is distributed proportionally across
    types based on remaining pool capacity. Otherwise, padding uses mini_dungeon.

    Args:
        requirements: Configuration specifying minimum counts for each type.
        total_layers: Total number of layers to plan.
        rng: Random number generator for shuffling.
        major_boss_ratio: Ratio of layers that can be major_boss (0.0-1.0).
        pool_sizes: Available clusters per type (e.g. {"mini_dungeon": 64,
            "boss_arena": 80, "legacy_dungeon": 28}). If None, padding
            defaults to mini_dungeon only.

    Returns:
        List of cluster type strings, one per layer.
    """
    # Build list of required types
    layer_types: list[str] = []
    layer_types.extend(["legacy_dungeon"] * requirements.legacy_dungeons)
    layer_types.extend(["boss_arena"] * requirements.bosses)
    layer_types.extend(["mini_dungeon"] * requirements.mini_dungeons)

    # Trim if we have too many requirements
    if len(layer_types) > total_layers:
        rng.shuffle(layer_types)
        layer_types = layer_types[:total_layers]
    else:
        padding_needed = total_layers - len(layer_types)
        if padding_needed > 0:
            if pool_sizes is not None:
                required_counts = {
                    "legacy_dungeon": requirements.legacy_dungeons,
                    "boss_arena": requirements.bosses,
                    "mini_dungeon": requirements.mini_dungeons,
                }
                layer_types.extend(
                    _distribute_padding(
                        padding_needed, required_counts, pool_sizes, rng
                    )
                )
            else:
                layer_types.extend(["mini_dungeon"] * padding_needed)

    # Shuffle to distribute types randomly across layers
    rng.shuffle(layer_types)

    # Replace some layers with major_boss based on ratio
    if major_boss_ratio > 0.0 and total_layers > 1:
        num_major_boss_slots = max(1, int(total_layers * major_boss_ratio))
        eligible_indices = list(range(total_layers))
        num_to_replace = min(num_major_boss_slots, len(eligible_indices))
        major_boss_indices = rng.sample(eligible_indices, num_to_replace)

        for idx in major_boss_indices:
            layer_types[idx] = "major_boss"

    return layer_types
