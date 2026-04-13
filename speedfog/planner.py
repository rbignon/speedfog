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


def compute_tier(
    layer_idx: int,
    total_layers: int,
    final_tier: int = 28,
    *,
    start_tier: int = 1,
    curve: str = "linear",
    exponent: float = 0.6,
) -> int:
    """Map layer index to difficulty tier.

    Args:
        layer_idx: Zero-based index of the current layer.
        total_layers: Total number of layers in the DAG.
        final_tier: Maximum tier for the final layer (default 28, range 1-28).
        start_tier: Minimum tier for the first layer (default 1, range 1-28).
            Must be <= final_tier.
        curve: Progression curve type ("linear" or "power").
        exponent: Power curve exponent (only used when curve="power").
            < 1.0 (e.g. 0.6): front-loaded, harder early, gentler late game.
            = 1.0: equivalent to linear.
            > 1.0 (e.g. 1.5): back-loaded, easy early, punitive late game.

    Returns:
        Difficulty tier between start_tier and final_tier (inclusive).
    """
    if total_layers <= 1:
        return start_tier

    # Clamp to valid range
    start_tier = max(1, min(28, start_tier))
    final_tier = max(start_tier, min(28, final_tier))

    progress = layer_idx / (total_layers - 1)

    if curve == "power":
        progress = progress**exponent
    elif curve != "linear":
        raise ValueError(f"Unknown tier curve: '{curve}'")

    tier = start_tier + progress * (final_tier - start_tier)

    return max(start_tier, min(final_tier, int(round(tier))))


def pick_weighted_type(
    pool_sizes: dict[str, int],
    used_counts: dict[str, int],
    rng: random.Random,
    *,
    fallback: str = "mini_dungeon",
) -> str:
    """Pick a type weighted by remaining pool capacity.

    Used for convergence layers and other contexts where a type must be
    chosen proportionally to remaining availability.

    Args:
        pool_sizes: Total available clusters per type.
        used_counts: How many of each type have been consumed so far.
        rng: Random number generator.
        fallback: Type returned when every pool is exhausted. Caller
            should pass a type known to be allowed in the current run.

    Returns:
        A type string chosen proportionally to remaining capacity, or
        `fallback` if every pool is empty.
    """
    remaining = {
        t: max(0, pool - used_counts.get(t, 0)) for t, pool in pool_sizes.items()
    }
    candidates = {t: r for t, r in remaining.items() if r > 0}

    if not candidates:
        return fallback

    types_list = list(candidates.keys())
    weights = [candidates[t] for t in types_list]
    return rng.choices(types_list, weights=weights, k=1)[0]


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
    pool_sizes: dict[str, int] | None = None,
) -> list[str]:
    """Plan sequence of cluster types for each layer.

    Ensures minimum requirements are met, pads with additional layers if needed,
    trims if requirements exceed total_layers, and shuffles the result.

    Major bosses are included as explicit requirements alongside other types.
    Padding excludes major_boss (not in pool_sizes).

    When pool_sizes is provided, padding is distributed proportionally across
    types based on remaining pool capacity. Otherwise, padding uses mini_dungeon.

    Args:
        requirements: Configuration specifying minimum counts for each type.
        total_layers: Total number of layers to plan.
        rng: Random number generator for shuffling.
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
    layer_types.extend(["major_boss"] * requirements.major_bosses)

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
                    "major_boss": requirements.major_bosses,
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

    return layer_types
