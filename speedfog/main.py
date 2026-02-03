"""SpeedFog CLI entry point."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

from speedfog.balance import report_balance
from speedfog.clusters import load_clusters
from speedfog.config import Config, load_config
from speedfog.generator import GenerationError, generate_with_retry
from speedfog.output import (
    export_json_v2,
    export_spoiler_log,
    load_fog_data,
)


def run_fogmodwrapper(
    seed_dir: Path,
    game_dir: Path,
    platform: str | None,
    verbose: bool,
) -> bool:
    """Run FogModWrapper to generate the mod.

    Args:
        seed_dir: Directory containing graph.json (output also goes here)
        game_dir: Path to Elden Ring Game directory
        platform: "windows", "linux", or None for auto-detect
        verbose: Print command and output

    Returns:
        True on success, False on failure.
    """
    project_root = Path(__file__).parent.parent
    wrapper_dir = project_root / "writer" / "FogModWrapper"
    wrapper_exe = wrapper_dir / "publish" / "win-x64" / "FogModWrapper.exe"
    data_dir = project_root / "data"

    if not wrapper_exe.exists():
        print(f"Error: FogModWrapper not found at {wrapper_exe}", file=sys.stderr)
        print("Run: python tools/setup_fogrando.py <fogrando.zip>", file=sys.stderr)
        return False

    # Detect platform (only Windows is native, everything else needs Wine)
    if platform is None or platform == "auto":
        platform = "windows" if sys.platform == "win32" else "linux"

    # Check Wine availability on non-Windows
    if platform == "linux" and shutil.which("wine") is None:
        print(
            "Error: Wine not found. Install wine to build mods on Linux.",
            file=sys.stderr,
        )
        return False

    # Build command with absolute paths (since we change cwd)
    seed_dir = seed_dir.resolve()
    game_dir = game_dir.resolve()
    data_dir = data_dir.resolve()

    if platform == "linux":
        cmd = ["wine", str(wrapper_exe.resolve())]
    else:
        cmd = [str(wrapper_exe.resolve())]

    cmd.extend(
        [
            str(seed_dir),
            "--game-dir",
            str(game_dir),
            "--data-dir",
            str(data_dir),
            "-o",
            str(seed_dir),
        ]
    )

    if verbose:
        print(f"Running: {' '.join(cmd)}")
        print(f"Working directory: {wrapper_dir}")

    # Run from wrapper_dir so FogModWrapper finds eldendata/
    # Stream output in real-time
    process = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        cwd=wrapper_dir,
        bufsize=1,  # Line buffered
    )

    # Print output as it arrives
    assert process.stdout is not None
    for line in process.stdout:
        print(line, end="")

    process.wait()
    return process.returncode == 0


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
    parser.add_argument(
        "--no-build",
        action="store_true",
        help="Skip mod building (only generate graph.json)",
    )
    parser.add_argument(
        "--game-dir",
        type=Path,
        help="Path to Elden Ring Game directory (overrides config)",
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
    starting_item_lots = config.starting_items.get_item_lots()
    export_json_v2(
        dag,
        clusters,
        json_path,
        fog_data=fog_data,
        starting_item_lots=starting_item_lots,
        starting_runes=config.starting_items.starting_runes,
        starting_golden_seeds=config.starting_items.golden_seeds,
        starting_sacred_tears=config.starting_items.sacred_tears,
    )
    print(f"Written: {json_path}")
    if starting_item_lots:
        print(f"Starting items: {len(starting_item_lots)} item lots configured")
    if config.starting_items.starting_runes > 0:
        print(f"Starting runes: {config.starting_items.starting_runes:,}")
    if config.starting_items.golden_seeds > 0:
        print(f"Starting golden seeds: {config.starting_items.golden_seeds}")
    if config.starting_items.sacred_tears > 0:
        print(f"Starting sacred tears: {config.starting_items.sacred_tears}")

    # Export spoiler if requested using output module
    if args.spoiler:
        spoiler_path = seed_dir / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path)
        print(f"Written: {spoiler_path}")

    # Build mod unless --no-build
    if not args.no_build:
        # Determine game_dir (CLI > config)
        game_dir = args.game_dir or (
            Path(config.paths.game_dir) if config.paths.game_dir else None
        )
        if not game_dir:
            print(
                "Error: --game-dir required (or set paths.game_dir in config.toml)",
                file=sys.stderr,
            )
            return 1

        if not game_dir.exists():
            print(f"Error: Game directory not found: {game_dir}", file=sys.stderr)
            return 1

        print("Building mod...")
        if not run_fogmodwrapper(
            seed_dir, game_dir, config.paths.platform, args.verbose
        ):
            print(
                "Error: Mod build failed (graph.json preserved for debugging)",
                file=sys.stderr,
            )
            return 1

        print(f"Mod ready: {seed_dir}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
