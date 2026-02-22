#!/usr/bin/env python3
"""Analyze correlation between fog gate counts and selection frequency,
and categorize generation failures."""

from __future__ import annotations

import random
import sys
from collections import Counter, defaultdict
from pathlib import Path

project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from speedfog.clusters import load_clusters  # noqa: E402
from speedfog.config import Config, resolve_final_boss_candidates  # noqa: E402
from speedfog.generator import GenerationError, generate_dag  # noqa: E402


def load_racing_standard_config() -> Config:
    return Config.from_dict(
        {
            "run": {"seed": 0},
            "budget": {"tolerance": 5},
            "requirements": {"legacy_dungeons": 1, "bosses": 10, "mini_dungeons": 5},
            "structure": {
                "max_parallel_paths": 3,
                "min_layers": 25,
                "max_layers": 30,
                "final_tier": 20,
                "split_probability": 0.9,
                "merge_probability": 0.5,
                "max_branches": 3,
                "first_layer_type": "legacy_dungeon",
                "major_boss_ratio": 0.3,
                "final_boss_candidates": ["all"],
            },
        }
    )


def compute_fog_stats(cluster) -> dict:
    """Compute fog gate statistics for a cluster."""
    entry_keys = {(e["fog_id"], e["zone"]) for e in cluster.entry_fogs}
    exit_keys = {(e["fog_id"], e["zone"]) for e in cluster.exit_fogs}
    bidir = entry_keys & exit_keys

    return {
        "entries": len(cluster.entry_fogs),
        "exits": len(cluster.exit_fogs),
        "bidir": len(bidir),
        "net_exits_after_1": max(
            0,
            len(cluster.exit_fogs)
            - (1 if len(bidir) > 0 and len(entry_keys - bidir) == 0 else 0),
        ),
        "shared_entrance": cluster.allow_shared_entrance,
        "entry_as_exit": cluster.allow_entry_as_exit,
    }


def main():
    config = load_racing_standard_config()
    clusters_path = project_root / "data" / "clusters.json"
    clusters = load_clusters(clusters_path)

    if config.structure.max_branches > 1 and config.structure.max_parallel_paths > 1:
        clusters.merge_roundtable_into_start()
    clusters.filter_passant_incompatible()

    all_boss_clusters = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )
    all_boss_zones = {zone for cluster in all_boss_clusters for zone in cluster.zones}
    config.structure.final_boss_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )

    # --- Part 1: Generate seeds and collect stats ---
    num_seeds = 3000
    cluster_appearances: Counter[str] = Counter()
    failure_reasons: Counter[str] = Counter()
    failure_details: list[str] = []
    successful = 0
    base_rng = random.Random(42)

    # Track nodes used per seed for exhaustion analysis
    nodes_per_seed: list[int] = []
    type_usage_per_seed: list[dict[str, int]] = []

    for _i in range(num_seeds):
        seed = base_rng.randint(1, 999_999_999)
        try:
            dag = generate_dag(config, clusters, seed)
            successful += 1
            nodes_per_seed.append(len(dag.nodes))

            type_counts: dict[str, int] = defaultdict(int)
            for node in dag.nodes.values():
                cluster_appearances[node.cluster.id] += 1
                type_counts[node.cluster.type] += 1
            type_usage_per_seed.append(dict(type_counts))

        except GenerationError as e:
            msg = str(e)
            # Categorize failure
            if "No passant-compatible" in msg:
                failure_reasons["pool_exhaustion_passant"] += 1
            elif "No split-compatible" in msg:
                failure_reasons["pool_exhaustion_split"] += 1
            elif "No merge-compatible" in msg:
                failure_reasons["pool_exhaustion_merge"] += 1
            elif "No available final boss" in msg:
                failure_reasons["no_final_boss"] += 1
            elif "Validation failed" in msg:
                failure_reasons["validation"] += 1
            else:
                failure_reasons["other"] += 1
            if len(failure_details) < 30:
                failure_details.append(f"  seed={seed}: {msg[:120]}")

    print(f"{'='*80}")
    print("PART 1: FAILURE ANALYSIS")
    print(f"{'='*80}")
    print(
        f"Total: {num_seeds}, Success: {successful}, Failed: {num_seeds - successful} ({(num_seeds-successful)/num_seeds*100:.1f}%)"
    )
    print()
    print("Failure categories:")
    for reason, count in failure_reasons.most_common():
        print(f"  {reason:<35} {count:>5} ({count/(num_seeds-successful)*100:.1f}%)")
    print()
    print("Sample failures:")
    for detail in failure_details[:15]:
        print(detail)

    # --- Part 2: Pool exhaustion analysis ---
    print(f"\n{'='*80}")
    print("PART 2: POOL EXHAUSTION ANALYSIS")
    print(f"{'='*80}")

    if type_usage_per_seed:
        print("\nAverage clusters consumed per seed (successful only):")
        type_totals: dict[str, list[int]] = defaultdict(list)
        for usage in type_usage_per_seed:
            for ctype in ["mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"]:
                type_totals[ctype].append(usage.get(ctype, 0))

        for ctype in ["mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"]:
            pool_size = len(clusters.get_by_type(ctype))
            vals = type_totals[ctype]
            avg = sum(vals) / len(vals)
            mx = max(vals)
            mn = min(vals)
            pct_consumed = avg / pool_size * 100
            print(
                f"  {ctype:<20} pool={pool_size:>3}  avg_used={avg:.1f}  max={mx}  min={mn}  avg_consumed={pct_consumed:.0f}%"
            )

        avg_nodes = sum(nodes_per_seed) / len(nodes_per_seed)
        print(f"\n  Average total nodes per seed: {avg_nodes:.1f}")
        print(f"  Max nodes: {max(nodes_per_seed)}")

    # --- Part 3: Fog gate correlation ---
    print(f"\n{'='*80}")
    print("PART 3: FOG GATE CORRELATION")
    print(f"{'='*80}")

    for ctype in ["mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"]:
        type_clusters = clusters.get_by_type(ctype)
        if not type_clusters:
            continue

        print(f"\n--- {ctype} (pool={len(type_clusters)}) ---")
        print(
            f"  {'Cluster':<50} {'E':>3} {'X':>3} {'Bi':>3} {'Flags':>8} {'Count':>6} {'%seeds':>7}"
        )
        print(f"  {'-'*85}")

        rows = []
        for c in type_clusters:
            stats = compute_fog_stats(c)
            count = cluster_appearances.get(c.id, 0)
            name = clusters.get_display_name(c)
            label = f"{name}"[:50]
            flags = ""
            if stats["shared_entrance"]:
                flags += "SE "
            if stats["entry_as_exit"]:
                flags += "EaX"
            rows.append((label, stats, count, flags, c.id))

        # Sort by count descending
        rows.sort(key=lambda r: r[2], reverse=True)

        for label, stats, count, flags, _cid in rows:
            pct = count / max(successful, 1) * 100
            print(
                f"  {label:<50} {stats['entries']:>3} {stats['exits']:>3} {stats['bidir']:>3} {flags:>8} {count:>6} {pct:>6.1f}%"
            )

        # Compute Pearson correlation between connectivity and count
        import math

        entries = [compute_fog_stats(c)["entries"] for c in type_clusters]
        exits = [compute_fog_stats(c)["exits"] for c in type_clusters]
        connectivity = [e + x for e, x in zip(entries, exits, strict=False)]
        counts = [cluster_appearances.get(c.id, 0) for c in type_clusters]

        n = len(connectivity)
        if n > 2:
            mean_c = sum(connectivity) / n
            mean_f = sum(counts) / n
            cov = (
                sum(
                    (c - mean_c) * (f - mean_f)
                    for c, f in zip(connectivity, counts, strict=False)
                )
                / n
            )
            std_c = math.sqrt(sum((c - mean_c) ** 2 for c in connectivity) / n)
            std_f = math.sqrt(sum((f - mean_f) ** 2 for f in counts) / n)
            if std_c > 0 and std_f > 0:
                pearson = cov / (std_c * std_f)
            else:
                pearson = 0
            print(f"\n  Pearson correlation (connectivity vs frequency): {pearson:.3f}")

    # --- Part 4: Clusters that can serve each role ---
    print(f"\n{'='*80}")
    print("PART 4: ROLE COMPATIBILITY ANALYSIS")
    print(f"{'='*80}")

    from speedfog.generator import (
        can_be_merge_node,
        can_be_passant_node,
        can_be_split_node,
    )

    for ctype in ["mini_dungeon", "boss_arena", "legacy_dungeon", "major_boss"]:
        type_clusters = clusters.get_by_type(ctype)
        if not type_clusters:
            continue

        passant = sum(1 for c in type_clusters if can_be_passant_node(c))
        split2 = sum(1 for c in type_clusters if can_be_split_node(c, 2))
        split3 = sum(1 for c in type_clusters if can_be_split_node(c, 3))
        merge2 = sum(1 for c in type_clusters if can_be_merge_node(c, 2))
        merge3 = sum(1 for c in type_clusters if can_be_merge_node(c, 3))

        print(f"\n  {ctype} (pool={len(type_clusters)}):")
        print(
            f"    Passant-compatible:  {passant:>3}/{len(type_clusters)} ({passant/len(type_clusters)*100:.0f}%)"
        )
        print(
            f"    Split-2-compatible:  {split2:>3}/{len(type_clusters)} ({split2/len(type_clusters)*100:.0f}%)"
        )
        print(
            f"    Split-3-compatible:  {split3:>3}/{len(type_clusters)} ({split3/len(type_clusters)*100:.0f}%)"
        )
        print(
            f"    Merge-2-compatible:  {merge2:>3}/{len(type_clusters)} ({merge2/len(type_clusters)*100:.0f}%)"
        )
        print(
            f"    Merge-3-compatible:  {merge3:>3}/{len(type_clusters)} ({merge3/len(type_clusters)*100:.0f}%)"
        )


if __name__ == "__main__":
    main()
