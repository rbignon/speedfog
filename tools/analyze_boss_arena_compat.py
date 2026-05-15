#!/usr/bin/env python3
"""Boss/arena compatibility statistics from ``data/boss_arena_tags.json``.

For each boss in the candidate pool, computes the fraction of arenas where
the constraints in ``speedfog.boss_arena_constraints.is_compatible`` accept
it. The partition into major/minor pools follows
``speedfog.item_randomizer`` exactly:

* ``cluster.type == "major_boss"`` → major arena, vanilla ID joins major
  pool.
* ``cluster.type == "boss_arena"`` → minor arena, vanilla ID joins minor
  pool.
* Phase-1 siblings of multi-phase leaders (from ``enemy.txt``) get their
  own slot on both sides.
* Source-only entries with ``pool == "minor"`` (or ``"major"``) are added
  to the matching boss pool.
* Entries with ``boss.exclude_from_pool = True`` are dropped from the
  boss pool but kept on the arena side (BAR semantics).

The boss/arena ``type`` field in the tags file is *not* consulted by
``is_compatible``, so it never participates in the matching.

Reports per-boss compatibility (sorted) and a global distribution summary,
both with and without the size constraint.

Usage:
    python tools/analyze_boss_arena_compat.py [--tags data/boss_arena_tags.json]
                                              [--clusters data/clusters.json]
                                              [--enemy data/enemy.txt]
                                              [--no-dlc] [--csv out.csv]
                                              [--pool major|minor|both]
"""

from __future__ import annotations

import argparse
import csv
import random
import statistics
import sys
from collections import defaultdict
from collections.abc import Iterable, Mapping
from pathlib import Path

project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from speedfog.boss_arena_constraints import (  # noqa: E402
    EntityTags,
    MatchingError,
    is_compatible,
    load_tags,
    match_arenas_to_bosses,
)
from speedfog.clusters import ClusterPool, load_clusters  # noqa: E402
from speedfog.item_randomizer import _compose_pool  # noqa: E402
from speedfog.output import parse_boss_phases, resolve_entity_id  # noqa: E402

CLUSTER_TYPE_BY_POOL = {"major": "major_boss", "minor": "boss_arena"}


def vanilla_ids_of_type(clusters: ClusterPool, cluster_type: str) -> list[int]:
    out: list[int] = []
    for c in clusters.get_by_type(cluster_type):
        eid = resolve_entity_id(c.defeat_flag)
        if eid:
            out.append(eid)
    return out


def collect_arenas(
    tags: Mapping[int, EntityTags],
    vanilla_ids: Iterable[int],
    phase_mapping: Mapping[int, int],
) -> dict[int, EntityTags]:
    """Replicate the arena-side expansion from ``_build_enemy_assignments``.

    For every vanilla leader of a cluster, include its arena tag; if the
    leader has a phase-1 sibling (per ``phase_mapping``), include that
    too. Entries without an ``arena`` block raise just like the live code
    would.
    """
    out: dict[int, EntityTags] = {}
    for leader in vanilla_ids:
        slots = [leader]
        phase1 = phase_mapping.get(leader)
        if phase1 is not None:
            slots.append(phase1)
        for eid in slots:
            entry = tags.get(eid)
            if entry is None or entry.arena is None:
                continue
            out[eid] = entry
    return out


def build_pools(
    tags: Mapping[int, EntityTags],
    clusters: ClusterPool,
    phase_mapping: Mapping[int, int],
    *,
    include_dlc: bool,
) -> tuple[dict[str, dict[int, EntityTags]], dict[str, dict[int, EntityTags]]]:
    """Build major/minor boss and arena pools matching item_randomizer.

    DLC filtering, when requested, drops DLC entries from both the
    vanilla-ID list and the candidate pool before composition.
    """

    def keep(eid: int) -> bool:
        entry = tags.get(eid)
        if entry is None:
            return False
        return include_dlc or not entry.dlc

    filtered_tags: dict[int, EntityTags] = {
        eid: entry for eid, entry in tags.items() if include_dlc or not entry.dlc
    }

    bosses: dict[str, dict[int, EntityTags]] = {}
    arenas: dict[str, dict[int, EntityTags]] = {}
    for pool_kind, cluster_type in CLUSTER_TYPE_BY_POOL.items():
        vanilla_ids = [
            eid for eid in vanilla_ids_of_type(clusters, cluster_type) if keep(eid)
        ]
        composed = _compose_pool(
            filtered_tags, pool_kind, vanilla_ids, phase_mapping=phase_mapping
        )
        bosses[pool_kind] = {
            eid: filtered_tags[eid] for eid in composed if eid in filtered_tags
        }
        arenas[pool_kind] = collect_arenas(filtered_tags, vanilla_ids, phase_mapping)
    return bosses, arenas


def compat_counts(
    bosses: Mapping[int, EntityTags],
    arenas: Mapping[int, EntityTags],
    *,
    check_size: bool,
) -> dict[int, int]:
    """For each boss, count how many arenas accept it."""
    counts: dict[int, int] = {}
    for bid, bentry in bosses.items():
        n = sum(
            1
            for aentry in arenas.values()
            if aentry.arena is not None
            and is_compatible(aentry.arena, bentry.boss, check_size=check_size)
        )
        counts[bid] = n
    return counts


def arena_compat_counts(
    bosses: Mapping[int, EntityTags],
    arenas: Mapping[int, EntityTags],
    *,
    check_size: bool,
) -> dict[int, int]:
    """For each arena, count how many bosses are compatible."""
    counts: dict[int, int] = {}
    for aid, aentry in arenas.items():
        assert aentry.arena is not None
        n = sum(
            1
            for bentry in bosses.values()
            if is_compatible(aentry.arena, bentry.boss, check_size=check_size)
        )
        counts[aid] = n
    return counts


def monte_carlo(
    bosses: Mapping[int, EntityTags],
    arenas: Mapping[int, EntityTags],
    *,
    check_size: bool,
    iterations: int,
    seed: int,
    arena_sample: int | None = None,
) -> tuple[dict[int, int], int, int]:
    """Run ``match_arenas_to_bosses`` ``iterations`` times.

    Counts how often each boss is assigned to at least one arena and
    reports the number of iterations that raised ``MatchingError`` so the
    caller can flag pools with no feasible matching (which would point at
    a real data problem here, since the inputs come from a stable pool).

    Args:
        arena_sample: If set, randomly pick this many arenas per iteration
            (clamped to ``len(arenas)``). Models a seed where the DAG only
            instantiates a subset of arenas, amplifying the size bias the
            full-pool view dilutes. If ``None``, every arena is included
            in every iteration.

    Returns ``(appearance_counts, successful_iterations, failed_iterations)``.
    """
    base = random.Random(seed)
    boss_tags = {bid: e.boss for bid, e in bosses.items()}
    arena_tags = {aid: e.arena for aid, e in arenas.items() if e.arena is not None}
    counts: dict[int, int] = {bid: 0 for bid in boss_tags}
    success = 0
    failures = 0
    arena_ids = list(arena_tags.keys())
    sample_n = min(arena_sample, len(arena_ids)) if arena_sample is not None else None
    for _ in range(iterations):
        rng = random.Random(base.getrandbits(64))
        if sample_n is not None:
            picked = rng.sample(arena_ids, sample_n)
            iter_arenas = {aid: arena_tags[aid] for aid in picked}
        else:
            iter_arenas = arena_tags
        try:
            assignment = match_arenas_to_bosses(
                arenas=iter_arenas,
                bosses=boss_tags,
                rng=rng,
                check_size=check_size,
            )
        except MatchingError:
            failures += 1
            continue
        success += 1
        for bid in assignment.values():
            counts[bid] += 1
    return counts, success, failures


def fmt_pct(n: int, total: int) -> str:
    return f"{(100.0 * n / total):5.1f}%" if total else "    --"


def print_summary(label: str, counts: dict[int, int], total_arenas: int) -> None:
    if not counts:
        print(f"  {label}: (empty)")
        return
    vals = list(counts.values())
    pcts = [100.0 * v / total_arenas for v in vals] if total_arenas else [0.0]
    print(
        f"  {label}: n={len(vals)} arenas={total_arenas} "
        f"avg={statistics.mean(pcts):.1f}% "
        f"median={statistics.median(pcts):.1f}% "
        f"min={min(pcts):.1f}% max={max(pcts):.1f}% "
        f"stdev={statistics.pstdev(pcts):.1f}%"
    )


def print_histogram(counts: dict[int, int], total_arenas: int, bins: int = 10) -> None:
    if not counts or not total_arenas:
        return
    edges = [i * total_arenas / bins for i in range(bins + 1)]
    buckets = [0] * bins
    for v in counts.values():
        idx = min(bins - 1, int(v * bins / total_arenas)) if total_arenas else 0
        buckets[idx] += 1
    width = max(buckets) or 1
    for i in range(bins):
        lo = 100.0 * edges[i] / total_arenas
        hi = 100.0 * edges[i + 1] / total_arenas
        bar = "#" * int(40 * buckets[i] / width)
        print(f"    {lo:5.1f}% - {hi:5.1f}%  | {buckets[i]:3d} {bar}")


def print_table(
    title: str,
    items: Mapping[int, EntityTags],
    counts: dict[int, int],
    total: int,
    *,
    limit: int | None,
) -> None:
    print(f"\n{title} (n={len(counts)}, denominator={total})")
    print("    compat%  count  id            name")
    ranked = sorted(counts.items(), key=lambda kv: (kv[1], items[kv[0]].name))
    if limit is not None:
        ranked = ranked[:limit]
    for eid, n in ranked:
        entry = items[eid]
        dlc_tag = " [DLC]" if entry.dlc else ""
        print(f"    {fmt_pct(n, total)}  {n:5d}  {eid:<12}  {entry.name}{dlc_tag}")


def write_csv(
    path: Path,
    tags: Mapping[int, EntityTags],
    pool_kind: str,
    side: str,
    counts_size: dict[int, int],
    counts_nosize: dict[int, int],
    denom: int,
) -> None:
    with path.open("w", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(
            [
                "side",
                "pool",
                "entity_id",
                "name",
                "type",
                "size",
                "dlc",
                "compat_with_size",
                "compat_without_size",
                "denominator",
                "pct_with_size",
                "pct_without_size",
            ]
        )
        for eid, n_size in counts_size.items():
            entry = tags[eid]
            etype = entry.boss.type if side == "boss" else entry.arena.type  # type: ignore[union-attr]
            esize = entry.boss.size if side == "boss" else entry.arena.size  # type: ignore[union-attr]
            n_nosize = counts_nosize[eid]
            w.writerow(
                [
                    side,
                    pool_kind,
                    eid,
                    entry.name,
                    etype,
                    esize,
                    int(entry.dlc),
                    n_size,
                    n_nosize,
                    denom,
                    f"{100.0 * n_size / denom:.2f}" if denom else "",
                    f"{100.0 * n_nosize / denom:.2f}" if denom else "",
                ]
            )


def print_monte_carlo(
    label: str,
    bosses: Mapping[int, EntityTags],
    counts: dict[int, int],
    successful: int,
    failures: int,
    iterations: int,
    *,
    top: int,
    arena_sample: int | None,
) -> None:
    header = (
        f"\nMonte Carlo selection frequency "
        f"({successful}/{iterations} successful matchings"
    )
    if failures:
        header += f", {failures} infeasible"
    if arena_sample is not None:
        header += f", arena_sample={arena_sample}"
    print(header + ")")
    if failures and arena_sample is None:
        print(
            "  WARNING: infeasible matchings on the full pool indicate the "
            "compatibility graph has no perfect matching. Inspect the most "
            "restricted bosses above."
        )
    if successful == 0:
        print("  (no successful matchings, can't compute frequencies)")
        return
    by_size: dict[int, list[float]] = defaultdict(list)
    for bid, n in counts.items():
        by_size[bosses[bid].boss.size].append(100.0 * n / successful)
    print("  Appearance % by boss size:")
    for size in sorted(by_size):
        vals = by_size[size]
        print(
            f"    size={size}  n={len(vals):3d}  "
            f"avg={statistics.mean(vals):5.1f}%  "
            f"min={min(vals):5.1f}%  max={max(vals):5.1f}%"
        )
    print()
    print("    appear%  count  size  id            name")
    ranked = sorted(counts.items(), key=lambda kv: (kv[1], bosses[kv[0]].name))

    def emit(bid: int, n: int) -> None:
        entry = bosses[bid]
        dlc_tag = " [DLC]" if entry.dlc else ""
        print(
            f"    {fmt_pct(n, successful)}  {n:5d}  "
            f"{entry.boss.size:4d}  {bid:<12}  {entry.name}{dlc_tag}"
        )

    if len(ranked) <= 2 * top:
        for bid, n in ranked:
            emit(bid, n)
    else:
        for bid, n in ranked[:top]:
            emit(bid, n)
        print("    ...")
        for bid, n in ranked[-top:]:
            emit(bid, n)


def run_pool(
    label: str,
    bosses: dict[int, EntityTags],
    arenas: dict[int, EntityTags],
    *,
    top: int,
    csv_out: Path | None,
    mc_iterations: int,
    mc_seed: int,
    mc_arena_sample: int | None,
) -> None:
    n_arenas = len(arenas)
    n_bosses = len(bosses)
    print(
        f"\n{'=' * 72}\n{label} pool: {n_bosses} bosses, {n_arenas} arenas\n{'=' * 72}"
    )
    if not n_arenas or not n_bosses:
        print("  (empty pool, skipping)")
        return

    boss_counts_size = compat_counts(bosses, arenas, check_size=True)
    boss_counts_nosize = compat_counts(bosses, arenas, check_size=False)
    arena_counts_size = arena_compat_counts(bosses, arenas, check_size=True)
    arena_counts_nosize = arena_compat_counts(bosses, arenas, check_size=False)

    print("\nBoss-side summary (% of arenas a boss can occupy):")
    print_summary("with check_size   ", boss_counts_size, n_arenas)
    print_summary("without check_size", boss_counts_nosize, n_arenas)
    print("\nDistribution (with check_size):")
    print_histogram(boss_counts_size, n_arenas)

    print("\nArena-side summary (% of bosses an arena can host):")
    print_summary("with check_size   ", arena_counts_size, n_bosses)
    print_summary("without check_size", arena_counts_nosize, n_bosses)

    print_table(
        "Most restricted bosses (ascending, check_size=True)",
        bosses,
        boss_counts_size,
        n_arenas,
        limit=top,
    )
    print_table(
        "Most restricted arenas (ascending, check_size=True)",
        arenas,
        arena_counts_size,
        n_bosses,
        limit=top,
    )

    if csv_out is not None:
        boss_csv = csv_out.with_name(f"{csv_out.stem}_{label}_boss{csv_out.suffix}")
        arena_csv = csv_out.with_name(f"{csv_out.stem}_{label}_arena{csv_out.suffix}")
        write_csv(
            boss_csv,
            bosses,
            label,
            "boss",
            boss_counts_size,
            boss_counts_nosize,
            n_arenas,
        )
        write_csv(
            arena_csv,
            arenas,
            label,
            "arena",
            arena_counts_size,
            arena_counts_nosize,
            n_bosses,
        )
        print(f"\nCSV: {boss_csv}\nCSV: {arena_csv}")

    if mc_iterations > 0:
        mc_counts, success, failures = monte_carlo(
            bosses,
            arenas,
            check_size=True,
            iterations=mc_iterations,
            seed=mc_seed,
            arena_sample=mc_arena_sample,
        )
        print_monte_carlo(
            label,
            bosses,
            mc_counts,
            success,
            failures,
            mc_iterations,
            top=top,
            arena_sample=mc_arena_sample,
        )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    data_dir = project_root / "data"
    parser.add_argument(
        "--tags",
        type=Path,
        default=data_dir / "boss_arena_tags.json",
        help="Path to boss_arena_tags.json",
    )
    parser.add_argument(
        "--clusters",
        type=Path,
        default=data_dir / "clusters.json",
        help="Path to clusters.json (source of truth for major/minor split)",
    )
    parser.add_argument(
        "--enemy",
        type=Path,
        default=data_dir / "enemy.txt",
        help="Path to enemy.txt (NextPhase mapping for multi-phase bosses)",
    )
    parser.add_argument(
        "--pool",
        choices=("major", "minor", "both"),
        default="both",
        help="Which pool to analyze (default: both)",
    )
    parser.add_argument(
        "--no-dlc",
        action="store_true",
        help="Exclude DLC entries from both sides",
    )
    parser.add_argument(
        "--top",
        type=int,
        default=15,
        help="How many entries to show in the restricted tables (default: 15)",
    )
    parser.add_argument(
        "--csv",
        type=Path,
        default=None,
        help="Base path for CSV export (suffix _<pool>_boss / _<pool>_arena added)",
    )
    parser.add_argument(
        "--monte-carlo",
        type=int,
        default=0,
        metavar="N",
        help="Run N random matchings and report boss appearance frequency",
    )
    parser.add_argument(
        "--mc-seed",
        type=int,
        default=42,
        help="Base seed for the Monte Carlo simulation (default: 42)",
    )
    parser.add_argument(
        "--mc-arena-sample",
        type=int,
        default=None,
        metavar="N",
        help=(
            "If set, randomly draw N arenas per Monte Carlo iteration "
            "(models a real seed picking a DAG-sized subset)"
        ),
    )
    args = parser.parse_args(argv)

    tags = load_tags(args.tags)
    clusters = load_clusters(args.clusters)
    phase_mapping = parse_boss_phases(args.enemy)
    bosses, arenas = build_pools(
        tags, clusters, phase_mapping, include_dlc=not args.no_dlc
    )

    print(
        f"Tags: {len(tags)} entries  Clusters: {len(clusters.clusters)}  "
        f"Phase pairs: {len(phase_mapping)}  "
        f"(DLC {'excluded' if args.no_dlc else 'included'})"
    )
    print(f"  Boss pool: major={len(bosses['major'])} minor={len(bosses['minor'])}")
    print(f"  Arena pool: major={len(arenas['major'])} minor={len(arenas['minor'])}")

    pools = ("major", "minor") if args.pool == "both" else (args.pool,)
    for kind in pools:
        run_pool(
            kind,
            bosses[kind],
            arenas[kind],
            top=args.top,
            csv_out=args.csv,
            mc_iterations=args.monte_carlo,
            mc_seed=args.mc_seed,
            mc_arena_sample=args.mc_arena_sample,
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
