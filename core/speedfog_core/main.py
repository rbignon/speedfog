"""SpeedFog CLI entry point."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from speedfog_core.clusters import load_clusters
from speedfog_core.config import load_config, Config
from speedfog_core.generator import generate_with_retry, GenerationError


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
        default=Path("graph.json"),
        help="Output JSON file (default: graph.json)",
    )
    parser.add_argument(
        "--spoiler",
        type=Path,
        help="Output spoiler log file",
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

        # Also check in core/data relative to script location
        if not clusters_path.exists():
            script_dir = Path(__file__).parent.parent
            alt_path = script_dir / "data" / "clusters.json"
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

    # Generate DAG
    if args.verbose:
        mode = "fixed seed" if config.seed != 0 else "auto-reroll"
        print(f"Generating DAG ({mode})...")

    try:
        dag, actual_seed = generate_with_retry(
            config, clusters, max_attempts=args.max_attempts
        )
    except GenerationError as e:
        print(f"Error: Generation failed: {e}", file=sys.stderr)
        return 1

    if args.verbose or config.seed == 0:
        print(f"Generated DAG with seed {actual_seed}")
        paths = dag.get_paths()
        print(f"  Layers: {max((n.layer for n in dag.nodes.values()), default=0) + 1}")
        print(f"  Nodes: {len(dag.nodes)}")
        print(f"  Paths: {len(paths)}")
        if paths:
            weights = [dag.path_weight(p) for p in paths]
            print(f"  Path weights: {weights}")

    # Export JSON
    dag.export_json(args.output)
    print(f"Written: {args.output}")

    # Export spoiler if requested
    if args.spoiler:
        dag.export_spoiler(args.spoiler)
        print(f"Written: {args.spoiler}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
