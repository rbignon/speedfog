"""Shared constants: graph.json contract, event flag allocation, cluster types.

Single source of truth for values that were previously duplicated across
modules (config, generator, planner, validator, graph_export).
"""

from __future__ import annotations

# Version of the graph.json format emitted by graph_export.dag_to_dict and
# consumed by writer/FogModWrapper.Core/GraphLoader.cs. Bump on any
# structural change and document it in docs/architecture.md.
# 4.6: optional class_loadout + torrent_skins (Tarnished showcase)
# 4.7: class_loadout reshaped to weapons/shields/armor_sets (weapon always,
#      shields once each on occupied left hands, armor per occupied slot)
# 4.8: optional boss_names (healthbar names of promoted mobs, see
#      docs/boss-healthbar-names.md)
GRAPH_JSON_VERSION = "4.8"

# SpeedFog's dedicated flag base: 1050290000 (m60_50_29_00, unclaimed).
# Saved flags (4xxx): zone tracking, finish event, death markers.
# Saved flags persist across area reloads, unlike temporary (2xxx) flags.
EVENT_FLAG_BASE = 1050294000
EVENT_FLAG_BUDGET = 1000

# Persistent flags (0xxx, saved): mod state that must survive area reloads.
PERSISTENT_FLAG_BASE = 1050290000
# Offset 0: items_spawned_flag (racing mod runtime item spawn prevention)
ITEMS_SPAWNED_FLAG = PERSISTENT_FLAG_BASE + 0
# Offset 1: banner_shown_flag (C#-side only, see RunCompleteInjector.cs)

# Cluster types the generator can place in intermediate layers (and that
# requirements/allowed_types accept). "start" and "final_boss" are structural
# endpoints, never selectable.
INTERMEDIATE_CLUSTER_TYPES = (
    "boss_arena",
    "mini_dungeon",
    "legacy_dungeon",
    "major_boss",
)

# Enemy scaling tier ceiling (SpeedFog uses tiers 1-34, vanilla's full range).
MAX_TIER = 34

# Default hard cap on weight spread (max - min) within a single layer.
DEFAULT_MAX_LAYER_SPREAD = 2.0

# Width of the weight matcher's anchor bands (generator.py): the first band
# is +/- this value around the anchor and widens by the same step up to
# max_weight_tolerance, which config.py requires to be a multiple of it.
WEIGHT_TOLERANCE_STEP = 0.5
