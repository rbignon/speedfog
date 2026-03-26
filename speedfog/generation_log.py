"""Generation log data model and serialization for SpeedFog.

Captures diagnostic events during DAG generation: planner decisions,
per-layer operations, type fallbacks with pool state, crosslink
decisions, and summary statistics.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from speedfog.clusters import ClusterData
    from speedfog.dag import Dag


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


def compute_pool_remaining(
    clusters: list[ClusterData],
    used_zones: set[str],
    reserved_zones: frozenset[str],
) -> dict[str, int]:
    """Count available clusters per type, filtering by used/reserved zones.

    Args:
        clusters: All clusters to count.
        used_zones: Zones already consumed.
        reserved_zones: Zones reserved for final boss / prerequisite.

    Returns:
        Dict of type -> available count.
    """
    counts: dict[str, int] = {}
    for c in clusters:
        if c.type not in counts:
            counts[c.type] = 0
        if not any(z in used_zones or z in reserved_zones for z in c.zones):
            counts[c.type] += 1
    return counts


def export_generation_log(
    log: GenerationLog,
    output_path: Path,
    dag: Dag | None = None,
) -> None:
    """Serialize a GenerationLog to a human-readable text file.

    Args:
        log: The generation log to serialize.
        output_path: Path to write the log file.
        dag: Optional DAG for resolving node layer numbers in crosslinks.
    """
    lines: list[str] = []

    # Header
    lines.append("=" * 64)
    if log.plan_event:
        lines.append(f"GENERATION LOG (seed: {log.plan_event.seed})")
    else:
        lines.append("GENERATION LOG")
    lines.append("=" * 64)
    lines.append("")

    # PLAN section
    if log.plan_event:
        pe = log.plan_event
        lines.append("PLAN")
        lines.append(f"  Final boss: {pe.final_boss}")
        if pe.reserved_zones:
            lines.append(f"  Reserved zones: {', '.join(sorted(pe.reserved_zones))}")
        req_parts = [f"{t}={c}" for t, c in sorted(pe.requirements.items())]
        lines.append(f"  Requirements: {', '.join(req_parts)}")
        lines.append(
            f"  Target layers: {pe.target_total} "
            f"(min={pe.target_total}, merge_reserve={pe.merge_reserve})"
        )
        lines.append(f"  Intermediate layers: {pe.num_intermediate}")
        if pe.first_layer_type:
            lines.append(f"  First layer type: {pe.first_layer_type}")
        seq = ", ".join(pe.planned_types)
        lines.append(f"  Planned sequence: [{seq}]")
        pool_parts = [f"{t}={c}" for t, c in sorted(pe.pool_sizes.items())]
        lines.append(f"  Pool sizes: {', '.join(pool_parts)}")
        lines.append("")

    # LAYERS section
    if log.layer_events:
        lines.append("LAYERS")
        convergence_started = False
        for le in log.layer_events:
            if le.phase == "convergence" and not convergence_started:
                convergence_started = True
                lines.append("")
                lines.append(
                    f"  --- CONVERGENCE ({le.branches_before} branches remaining) ---"
                )
                if le.pool_snapshot:
                    pool_parts = [
                        f"{t}={c}" for t, c in sorted(le.pool_snapshot.items())
                    ]
                    lines.append(f"  Pool: {', '.join(pool_parts)}")

            # Layer header
            if le.phase == "start":
                phase_str = "start"
            elif le.phase == "first_layer":
                phase_str = (
                    f"first_layer={le.planned_type}"
                    if le.planned_type
                    else "first_layer"
                )
            elif le.phase == "planned":
                phase_str = (
                    f"planned={le.planned_type}" if le.planned_type else "planned"
                )
            elif le.phase == "convergence":
                phase_str = (
                    f"convergence={le.planned_type}"
                    if le.planned_type
                    else "convergence"
                )
            elif le.phase == "prerequisite":
                phase_str = "prerequisite"
            elif le.phase == "final_boss":
                phase_str = "final_boss"
            else:
                phase_str = le.phase

            lines.append(
                f"  L{le.layer} [{phase_str}] {le.operation} "
                f"{le.branches_before}->{le.branches_after} branches"
            )

            # Nodes
            for node in le.nodes:
                fallback_mark = ""
                if le.planned_type and node.cluster_type != le.planned_type:
                    for fb in le.fallbacks:
                        if fb.actual_type == node.cluster_type:
                            fallback_mark = " *** FALLBACK ***"
                            break
                lines.append(
                    f"    {node.cluster_id} [{node.cluster_type}, w={node.weight}] "
                    f"({node.role}){fallback_mark}"
                )

            # Fallback details
            if le.fallbacks:
                lines.append("    Fallbacks:")
                for fb in le.fallbacks:
                    pool_parts = [
                        f"{t}={c}" for t, c in sorted(fb.pool_remaining.items())
                    ]
                    lines.append(
                        f"      b{fb.branch_index}: wanted {fb.preferred_type}, "
                        f"got {fb.actual_type} ({fb.reason}: {', '.join(pool_parts)})"
                    )

            lines.append("")
        lines.append("")

    # CROSSLINKS section
    if log.crosslink_event:
        ce = log.crosslink_event
        lines.append("CROSSLINKS")
        lines.append(
            f"  Eligible pairs: {ce.eligible_pairs}, "
            f"Added: {ce.added}, Skipped: {ce.skipped}"
        )
        if ce.added_details:
            lines.append("  Added:")
            for d in ce.added_details:
                src_layer = (
                    f"L{dag.nodes[d.source_id].layer} "
                    if dag and d.source_id in dag.nodes
                    else ""
                )
                tgt_layer = (
                    f"L{dag.nodes[d.target_id].layer} "
                    if dag and d.target_id in dag.nodes
                    else ""
                )
                lines.append(
                    f"    {src_layer}{d.source_id} -> {tgt_layer}{d.target_id}"
                )
        if ce.skipped_details:
            lines.append("  Skipped:")
            for d in ce.skipped_details:
                src_layer = (
                    f"L{dag.nodes[d.source_id].layer} "
                    if dag and d.source_id in dag.nodes
                    else ""
                )
                tgt_layer = (
                    f"L{dag.nodes[d.target_id].layer} "
                    if dag and d.target_id in dag.nodes
                    else ""
                )
                lines.append(
                    f"    {src_layer}{d.source_id} -> {tgt_layer}{d.target_id}: {d.reason}"
                )
        lines.append("")

    # SUMMARY section
    if log.summary:
        s = log.summary
        lines.append("SUMMARY")
        lines.append(f"  Layers: {s.total_layers}")
        lines.append(f"  Nodes: {s.total_nodes}")
        if s.crosslinks:
            lines.append(f"  Crosslinks: {s.crosslinks}")
        lines.append(f"  Fallbacks: {s.fallback_count}")
        if s.fallback_summary:
            parts = [f"L{layer}: {ptype}" for layer, ptype in s.fallback_summary]
            lines.append(f"    {', '.join(parts)}")
        if s.pool_at_end:
            pool_parts = [f"{t}={c}" for t, c in sorted(s.pool_at_end.items())]
            lines.append(f"  Pool at end: {', '.join(pool_parts)}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
