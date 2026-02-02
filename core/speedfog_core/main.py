"""SpeedFog CLI entry point."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from speedfog_core.balance import report_balance
from speedfog_core.clusters import load_clusters
from speedfog_core.config import Config, load_config
from speedfog_core.generator import GenerationError, generate_with_retry
from speedfog_core.output import (
    export_json,
    export_json_v2,
    export_spoiler_log,
    load_fog_data,
)


def main() -> int:
    """Main entry point for the speedfog command."""
    parser = argparse.ArgumentParser(
        description="SpeedFog - Generate randomized Elden Ring run DAGs",
    )
    parser.add_argument(
        "config",
        type=Path,
        nargs="?",
        default=None,
        help="Path to config.toml (optional, uses defaults if not provided)",
    )
    parser.add_argument(
        "-o",
        "--output",
        type=Path,
        default=Path("./seeds/"),
        help="Output directory (default: ./seeds/). Files are written to <output>/<seed>/",
    )
    parser.add_argument(
        "--spoiler",
        action="store_true",
        help="Generate spoiler log file",
    )
    parser.add_argument(
        "--clusters",
        type=Path,
        help="Path to clusters.json (overrides config)",
    )
    parser.add_argument(
        "--seed",
        type=int,
        help="Random seed (overrides config, 0 = auto-reroll)",
    )
    parser.add_argument(
        "--max-attempts",
        type=int,
        default=100,
        help="Max generation attempts for auto-reroll (default: 100)",
    )
    parser.add_argument(
        "-v",
        "--verbose",
        action="store_true",
        help="Verbose output",
    )

    args = parser.parse_args()

    # Load or create config
    if args.config:
        try:
            config = load_config(args.config)
            if args.verbose:
                print(f"Loaded config from {args.config}")
        except FileNotFoundError:
            print(f"Error: Config file not found: {args.config}", file=sys.stderr)
            return 1
    else:
        config = Config()
        if args.verbose:
            print("Using default configuration")

    # Override seed if provided
    if args.seed is not None:
        config.seed = args.seed

    # Determine clusters file path
    if args.clusters:
        clusters_path = args.clusters
    else:
        # Resolve relative to config file or current directory
        if args.config:
            base_dir = args.config.parent
        else:
            base_dir = Path.cwd()

        clusters_path = base_dir / config.paths.clusters_file

        # Also check in data/ relative to project root
        if not clusters_path.exists():
            project_root = Path(__file__).parent.parent.parent
            alt_path = project_root / "data" / "clusters.json"
            if alt_path.exists():
                clusters_path = alt_path

    # Load clusters
    try:
        clusters = load_clusters(clusters_path)
        if args.verbose:
            print(f"Loaded {len(clusters.clusters)} clusters from {clusters_path}")
            for ctype, clist in clusters.by_type.items():
                print(f"  {ctype}: {len(clist)}")
    except FileNotFoundError:
        print(f"Error: Clusters file not found: {clusters_path}", file=sys.stderr)
        return 1

    # Load fog_data.json for accurate map lookups
    fog_data_path = clusters_path.parent / "fog_data.json"
    fog_data = load_fog_data(fog_data_path) if fog_data_path.exists() else None
    if args.verbose and fog_data:
        print(f"Loaded {len(fog_data)} fogs from {fog_data_path}")

    # Generate DAG
    if args.verbose:
        mode = "fixed seed" if config.seed != 0 else "auto-reroll"
        print(f"Generating DAG ({mode})...")

    try:
        result = generate_with_retry(config, clusters, max_attempts=args.max_attempts)
    except GenerationError as e:
        print(f"Error: Generation failed: {e}", file=sys.stderr)
        return 1

    dag = result.dag
    actual_seed = result.seed

    if args.verbose and result.validation.warnings:
        print("Validation warnings:")
        for warning in result.validation.warnings:
            print(f"  - {warning}")

    # Print summary
    if args.verbose or config.seed == 0:
        print(f"Generated DAG with seed {actual_seed}")
        paths = dag.enumerate_paths()
        print(f"  Layers: {max((n.layer for n in dag.nodes.values()), default=0) + 1}")
        print(f"  Nodes: {len(dag.nodes)}")
        print(f"  Paths: {len(paths)}")
        if paths:
            weights = [dag.path_weight(p) for p in paths]
            print(f"  Path weights: {weights}")

    # Print balance report in verbose mode
    if args.verbose:
        print()
        print(report_balance(dag, config.budget))

    # Create output directory: <output>/<seed>/
    seed_dir = args.output / str(actual_seed)
    seed_dir.mkdir(parents=True, exist_ok=True)

    # Export JSON v2 format (for FogModWrapper)
    json_path = seed_dir / "graph.json"
    export_json_v2(dag, clusters, json_path, fog_data=fog_data)
    print(f"Written: {json_path}")

    # Also export v1 format for compatibility
    json_v1_path = seed_dir / "graph_v1.json"
    export_json(dag, json_v1_path)
    if args.verbose:
        print(f"Written: {json_v1_path} (v1 format)")

    # Export spoiler if requested using output module
    if args.spoiler:
        spoiler_path = seed_dir / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path)
        print(f"Written: {spoiler_path}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
