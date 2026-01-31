"""Layer planning module for SpeedFog.

This module handles planning what type of cluster goes in each layer of the DAG.
"""

from __future__ import annotations

import random

from speedfog_core.config import RequirementsConfig


def compute_tier(layer_idx: int, total_layers: int) -> int:
    """Map layer index to difficulty tier (1-28).

    Uses linear interpolation to spread tiers across layers.
    First layer gets tier 1, last layer gets tier 28.

    Args:
        layer_idx: Zero-based index of the current layer.
        total_layers: Total number of layers in the DAG.

    Returns:
        Difficulty tier between 1 and 28 (inclusive).
    """
    if total_layers <= 1:
        # Single layer gets the starting tier
        return 1

    # Linear interpolation from tier 1 to tier 28
    # layer_idx=0 -> tier 1, layer_idx=total_layers-1 -> tier 28
    progress = layer_idx / (total_layers - 1)
    tier = 1 + progress * 27  # 27 = 28 - 1

    return int(round(tier))


def plan_layer_types(
    requirements: RequirementsConfig,
    total_layers: int,
    rng: random.Random,
) -> list[str]:
    """Plan sequence of cluster types for each layer.

    Ensures minimum requirements are met, pads with mini_dungeons if needed,
    trims if requirements exceed total_layers, and shuffles the result.

    Args:
        requirements: Configuration specifying minimum counts for each type.
        total_layers: Total number of layers to plan.
        rng: Random number generator for shuffling.

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
        # Pad with mini_dungeons if we have fewer requirements than layers
        padding_needed = total_layers - len(layer_types)
        layer_types.extend(["mini_dungeon"] * padding_needed)

    # Shuffle to distribute types randomly across layers
    rng.shuffle(layer_types)

    return layer_types
