"""Shared constants: graph.json contract, event flag allocation, cluster types.

Single source of truth for values that were previously duplicated across
modules (config, generator, planner, validator, graph_export).
"""

from __future__ import annotations

# Version of the graph.json format emitted by graph_export.dag_to_dict and
# consumed by writer/FogModWrapper.Core/GraphLoader.cs. Bump on any
# structural change and document it in docs/architecture.md.
GRAPH_JSON_VERSION = "4.4"

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

# Enemy scaling tier ceiling (SpeedFog uses tiers 1-28, a subset of vanilla).
MAX_TIER = 28

# Default hard cap on weight spread (max - min) within a single layer.
DEFAULT_MAX_LAYER_SPREAD = 2.0
