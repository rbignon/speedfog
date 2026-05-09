"""Compare old vs new generator on a corpus of seeds.

Outputs JSON to stdout with per-seed metrics:
  total_nodes, total_edges, exit_utilization, type_counts, weight_stddev.

Usage:
    uv run python tools/compare_generators.py --seeds 1000-1099 \\
        --layers-count 20 --max-parallel 4
"""

from __future__ import annotations

import argparse
import json
import statistics
import sys
from pathlib import Path

from speedfog.clusters import load_clusters
from speedfog.config import (
    BudgetConfig,
    Config,
    RequirementsConfig,
    StructureConfig,
)
from speedfog.generator import generate_dag as generate_old
from speedfog.generator_v2 import generate_dag as generate_new


def _exit_utilization(dag) -> float:
    available = sum(len(n.cluster.exit_fogs) for n in dag.nodes.values())
    if available == 0:
        return 0.0
    return len(dag.edges) / available


def _stats_for_dag(dag) -> dict:
    type_counts: dict[str, int] = {}
    weights: list[float] = []
    for n in dag.nodes.values():
        type_counts[n.cluster.type] = type_counts.get(n.cluster.type, 0) + 1
        weights.append(n.cluster.weight)
    return {
        "total_nodes": len(dag.nodes),
        "total_edges": len(dag.edges),
        "exit_utilization": round(_exit_utilization(dag), 3),
        "type_counts": type_counts,
        "weight_mean": round(statistics.mean(weights), 2),
        "weight_stddev": round(statistics.pstdev(weights), 2),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seeds", required=True, help="Range like 1000-1099")
    parser.add_argument("--layers-count", type=int, default=20)
    parser.add_argument("--max-parallel", type=int, default=4)
    args = parser.parse_args()
    lo, hi = (int(x) for x in args.seeds.split("-"))

    pool_path = Path(__file__).parent.parent / "data" / "clusters.json"
    pool = load_clusters(pool_path)
    pool.merge_roundtable_into_start()

    # Snapshot boss candidates before passant filter (mirrors main.py logic).
    boss_candidates = pool.get_by_type("major_boss") + pool.get_by_type("final_boss")

    pool.filter_passant_incompatible()

    rows: list[dict] = []
    for seed in range(lo, hi + 1):
        cfg = Config(
            seed=seed,
            requirements=RequirementsConfig(
                legacy_dungeons=1,
                bosses=3,
                mini_dungeons=3,
                major_bosses=1,
            ),
            structure=StructureConfig(
                layers_count=args.layers_count,
                max_parallel_paths=args.max_parallel,
                final_boss_candidates={"leyndell_throne": 1},
            ),
            budget=BudgetConfig(),
        )
        row: dict = {"seed": seed}
        try:
            old_dag, _ = generate_old(cfg, pool, boss_candidates=boss_candidates)
            row["old"] = _stats_for_dag(old_dag)
        except Exception as e:
            row["old_error"] = str(e)
        try:
            new_dag, _ = generate_new(cfg, pool)
            row["new"] = _stats_for_dag(new_dag)
        except Exception as e:
            row["new_error"] = str(e)
        rows.append(row)

    json.dump(rows, sys.stdout, indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main())
