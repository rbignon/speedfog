#!/usr/bin/env python3
"""Analyze zone/cluster appearance probability across many generated seeds.

Usage:
    python tools/analyze_zone_distribution.py [--seeds N] [--config path.toml]

Generates N seeds using the given config (or standard racing pool defaults)
and reports how often each cluster appears.
"""

from __future__ import annotations

import argparse
import sys
from collections import Counter, defaultdict
from pathlib import Path

# Add project root to path
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

from speedfog.clusters import ClusterPool, load_clusters  # noqa: E402
from speedfog.config import Config, resolve_final_boss_candidates  # noqa: E402
from speedfog.generator import GenerationError, generate_dag  # noqa: E402


def load_racing_standard_config() -> Config:
    """Load the standard racing pool config matching standard.toml."""
    return Config.from_dict(
        {
            "run": {"seed": 0},
            "budget": {"tolerance": 5},
            "requirements": {
                "legacy_dungeons": 1,
                "bosses": 10,
                "mini_dungeons": 5,
            },
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


def run_analysis(
    config: Config,
    clusters: ClusterPool,
    boss_candidates: list,
    num_seeds: int,
    verbose: bool = False,
) -> None:
    """Generate many seeds and analyze zone distribution."""
    import random

    cluster_appearances: Counter[str] = Counter()
    cluster_as_final: Counter[str] = Counter()
    cluster_type_map: dict[str, str] = {}
    type_appearances: Counter[str] = Counter()
    failed = 0
    successful = 0

    # Track per-type pool sizes for reference
    type_pool_sizes: dict[str, int] = {}
    for ctype, clist in clusters.by_type.items():
        type_pool_sizes[ctype] = len(clist)

    base_rng = random.Random(42)  # Deterministic for reproducibility

    for i in range(num_seeds):
        seed = base_rng.randint(1, 999_999_999)
        try:
            dag = generate_dag(config, clusters, seed, boss_candidates=boss_candidates)
            successful += 1

            for _node_id, node in dag.nodes.items():
                cid = node.cluster.id
                cluster_appearances[cid] += 1
                cluster_type_map[cid] = node.cluster.type
                type_appearances[node.cluster.type] += 1

            # Track final boss
            if dag.end_id and dag.end_id in dag.nodes:
                end_cluster = dag.nodes[dag.end_id].cluster
                cluster_as_final[end_cluster.id] += 1

        except GenerationError:
            failed += 1

        if verbose and (i + 1) % 500 == 0:
            print(f"  Progress: {i + 1}/{num_seeds} ({failed} failed)")

    print(f"\n{'='*80}")
    print("ZONE DISTRIBUTION ANALYSIS")
    print(f"{'='*80}")
    print(f"Seeds generated: {successful}/{num_seeds} ({failed} failed)")
    print(
        f"Config: layers={config.structure.min_layers}-{config.structure.max_layers}, "
        f"bosses={config.requirements.bosses}, "
        f"legacy={config.requirements.legacy_dungeons}, "
        f"mini={config.requirements.mini_dungeons}"
    )
    print(
        f"Split/merge prob: {config.structure.split_probability}/{config.structure.merge_probability}"
    )
    print(f"Major boss ratio: {config.structure.major_boss_ratio}")
    print()

    # --- Per-type summary ---
    print(f"{'TYPE SUMMARY':=^80}")
    print(
        f"{'Type':<20} {'Pool':>5} {'Appearances':>12} {'Avg/seed':>10} {'Avg %used':>10}"
    )
    print("-" * 60)
    for ctype in sorted(type_pool_sizes.keys()):
        pool_size = type_pool_sizes[ctype]
        total_appearances = type_appearances.get(ctype, 0)
        avg_per_seed = total_appearances / max(successful, 1)
        pct_used = (avg_per_seed / pool_size * 100) if pool_size > 0 else 0
        print(
            f"{ctype:<20} {pool_size:>5} {total_appearances:>12} {avg_per_seed:>10.1f} {pct_used:>9.1f}%"
        )
    print()

    # --- Per-cluster distribution ---
    # Group by type
    by_type: dict[str, list[tuple[str, int]]] = defaultdict(list)
    for cid, count in cluster_appearances.items():
        ctype = cluster_type_map.get(cid, "unknown")
        by_type[ctype].append((cid, count))

    for ctype in [
        "legacy_dungeon",
        "mini_dungeon",
        "boss_arena",
        "major_boss",
        "final_boss",
        "start",
        "other",
    ]:
        items = by_type.get(ctype, [])
        if not items:
            continue

        pool_size = type_pool_sizes.get(ctype, 0)
        never_seen = pool_size - len(items)

        items.sort(key=lambda x: x[1], reverse=True)
        total_appearances = sum(c for _, c in items)

        print(f"\n{'='*80}")
        print(
            f"TYPE: {ctype} (pool={pool_size}, appeared={len(items)}, never_seen={never_seen})"
        )
        print(f"{'='*80}")

        if not items:
            continue

        max_count = items[0][1]
        min_count = items[-1][1] if items else 0
        avg_count = total_appearances / max(len(items), 1)

        # Ideal: each cluster appears equally often
        # If type uses N clusters per seed, ideal = successful * N / pool_size
        # But N varies per seed (depends on branches), so we compute from totals
        avg_per_seed = total_appearances / max(successful, 1)
        ideal_per_cluster = total_appearances / max(pool_size, 1)

        print(f"Appearances: avg={avg_count:.1f}, max={max_count}, min={min_count}")
        print(f"Avg clusters used per seed: {avg_per_seed:.1f}/{pool_size}")
        if ideal_per_cluster > 0:
            print(f"Ideal (uniform): {ideal_per_cluster:.1f} per cluster")
            spread = max_count / max(min_count, 1)
            print(f"Max/min ratio: {spread:.1f}x")
        print()

        print(f"  {'Cluster':<40} {'Count':>7} {'%seeds':>8} {'vs ideal':>10}")
        print(f"  {'-'*65}")
        for cid, count in items:
            pct = count / max(successful, 1) * 100
            # Get display name
            cluster_obj = clusters.get_by_id(cid)
            name = ""
            if cluster_obj:
                name = clusters.get_display_name(cluster_obj)
            label = f"{name} ({cid})" if name and name != cid else cid

            vs_ideal = (
                (count / ideal_per_cluster - 1) * 100 if ideal_per_cluster > 0 else 0
            )
            sign = "+" if vs_ideal >= 0 else ""
            print(f"  {label:<40} {count:>7} {pct:>7.1f}% {sign}{vs_ideal:>8.1f}%")

        # Show clusters that NEVER appeared
        if never_seen > 0:
            seen_ids = {cid for cid, _ in items}
            all_of_type = clusters.get_by_type(ctype)
            missing = [c for c in all_of_type if c.id not in seen_ids]
            if missing:
                print(f"\n  NEVER APPEARED ({len(missing)}):")
                for c in missing:
                    name = clusters.get_display_name(c)
                    entries = len(c.entry_fogs)
                    exits = len(c.exit_fogs)
                    label = f"{name} ({c.id})" if name and name != c.id else c.id
                    print(f"    {label:<40} entries={entries} exits={exits}")

    # --- Final boss distribution ---
    if cluster_as_final:
        print(f"\n{'='*80}")
        print("FINAL BOSS DISTRIBUTION")
        print(f"{'='*80}")
        for cid, count in cluster_as_final.most_common():
            pct = count / max(successful, 1) * 100
            cluster_obj = clusters.get_by_id(cid)
            name = clusters.get_display_name(cluster_obj) if cluster_obj else cid
            label = f"{name} ({cid})" if name and name != cid else cid
            print(f"  {label:<45} {count:>7} ({pct:.1f}%)")

    # --- Inequality metrics ---
    print(f"\n{'='*80}")
    print("INEQUALITY METRICS (per type)")
    print(f"{'='*80}")
    print(
        f"{'Type':<20} {'Gini':>6} {'CV':>6} {'Max/Min':>8} {'Top5%':>10} {'Bot5%':>10}"
    )
    print("-" * 65)

    for ctype in ["legacy_dungeon", "mini_dungeon", "boss_arena", "major_boss"]:
        items = by_type.get(ctype, [])
        pool_size = type_pool_sizes.get(ctype, 0)
        if not items or pool_size == 0:
            continue

        # Include zeros for clusters that never appeared
        counts = [c for _, c in items] + [0] * (pool_size - len(items))
        counts.sort()

        n = len(counts)
        mean = sum(counts) / n if n > 0 else 0

        # Gini coefficient
        if mean > 0 and n > 1:
            numerator = sum(
                abs(counts[i] - counts[j]) for i in range(n) for j in range(n)
            )
            gini = numerator / (2 * n * n * mean)
        else:
            gini = 0

        # Coefficient of Variation
        if mean > 0:
            variance = sum((c - mean) ** 2 for c in counts) / n
            cv = variance**0.5 / mean
        else:
            cv = 0

        # Max/min ratio (counting zeros)
        max_c = max(counts)
        min_c = min(counts)
        ratio = max_c / max(min_c, 1)

        # Top/bottom 5% share
        top_n = max(1, n // 20)
        top5_share = sum(counts[-top_n:]) / max(sum(counts), 1) * 100
        bot5_share = sum(counts[:top_n]) / max(sum(counts), 1) * 100

        print(
            f"{ctype:<20} {gini:>6.3f} {cv:>6.3f} {ratio:>7.1f}x {top5_share:>9.1f}% {bot5_share:>9.1f}%"
        )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Analyze zone distribution across seeds"
    )
    parser.add_argument(
        "--seeds",
        type=int,
        default=5000,
        help="Number of seeds to generate (default: 5000)",
    )
    parser.add_argument(
        "--config",
        type=Path,
        default=None,
        help="Path to config.toml (default: standard racing pool)",
    )
    parser.add_argument("-v", "--verbose", action="store_true", help="Show progress")
    args = parser.parse_args()

    # Load config
    if args.config:
        config = Config.from_toml(args.config)
    else:
        config = load_racing_standard_config()

    # Load clusters
    clusters_path = project_root / "data" / "clusters.json"
    try:
        clusters = load_clusters(clusters_path)
    except FileNotFoundError:
        print(f"Error: {clusters_path} not found", file=sys.stderr)
        return 1

    # Preprocess clusters (same as main.py)
    if config.structure.max_exits > 1 and config.structure.max_parallel_paths > 1:
        clusters.merge_roundtable_into_start()
    boss_candidates = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )
    removed = clusters.filter_passant_incompatible()
    if removed:
        print(f"Filtered {len(removed)} passant-incompatible clusters")

    # Resolve final_boss_candidates
    all_boss_clusters = boss_candidates
    all_boss_zones = {zone for cluster in all_boss_clusters for zone in cluster.zones}
    config.structure.final_boss_candidates = resolve_final_boss_candidates(
        config.structure.effective_final_boss_candidates, all_boss_zones
    )

    print(f"Analyzing {args.seeds} seeds...")
    print(f"Cluster pool: {len(clusters.clusters)} clusters")
    for ctype, clist in sorted(clusters.by_type.items()):
        print(f"  {ctype}: {len(clist)}")

    run_analysis(config, clusters, boss_candidates, args.seeds, verbose=args.verbose)
    return 0


if __name__ == "__main__":
    sys.exit(main())
