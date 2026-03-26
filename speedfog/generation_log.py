"""Generation log data model and serialization for SpeedFog.

Captures diagnostic events during DAG generation: planner decisions,
per-layer operations, type fallbacks with pool state, crosslink
decisions, and summary statistics.
"""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class NodeEntry:
    """A node created at a layer."""

    cluster_id: str
    cluster_type: str
    weight: int
    role: str  # start, primary, passant, split_child, merge_target,
    # rebalance_split, rebalance_merge, rebalance_passant, final_boss


@dataclass
class FallbackEntry:
    """A type fallback event at a layer."""

    branch_index: int
    preferred_type: str
    actual_type: str
    reason: str  # pool_exhausted, zone_conflict
    pool_remaining: dict[str, int]


@dataclass
class LayerEvent:
    """What happened at a single layer during generation."""

    layer: int
    phase: str  # start, first_layer, planned, convergence, prerequisite, final_boss
    planned_type: str | None
    operation: str  # START, PASSANT, SPLIT, MERGE, REBALANCE
    branches_before: int
    branches_after: int
    nodes: list[NodeEntry] = field(default_factory=list)
    fallbacks: list[FallbackEntry] = field(default_factory=list)
    pool_snapshot: dict[str, int] | None = None  # pool state at convergence start


@dataclass
class PlanEvent:
    """Planner decisions captured before layer execution."""

    seed: int
    requirements: dict[str, int]
    target_total: int
    merge_reserve: int
    num_intermediate: int
    first_layer_type: str | None
    planned_types: list[str]
    pool_sizes: dict[str, int]
    final_boss: str
    reserved_zones: set[str]


@dataclass
class CrosslinkDetail:
    """A single crosslink attempt (added or skipped)."""

    source_id: str
    target_id: str
    reason: str | None = None  # None=added, "no_surplus_exits", "no_available_entries"


@dataclass
class CrosslinkEvent:
    """Summary of crosslink pass."""

    eligible_pairs: int
    added: int
    skipped: int
    added_details: list[CrosslinkDetail] = field(default_factory=list)
    skipped_details: list[CrosslinkDetail] = field(default_factory=list)


@dataclass
class SummaryEvent:
    """End-of-generation summary statistics."""

    total_layers: int
    total_nodes: int
    planned_layers: int
    convergence_layers: int
    crosslinks: int
    fallback_count: int
    fallback_summary: list[tuple[int, str]]  # (layer, preferred_type)
    pool_at_end: dict[str, int]


@dataclass
class GenerationLog:
    """Accumulates structured events during DAG generation."""

    plan_event: PlanEvent | None = None
    layer_events: list[LayerEvent] = field(default_factory=list)
    crosslink_event: CrosslinkEvent | None = None
    summary: SummaryEvent | None = None
