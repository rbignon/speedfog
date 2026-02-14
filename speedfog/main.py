"""SpeedFog CLI entry point."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

from speedfog.balance import report_balance
from speedfog.care_package import sample_care_package
from speedfog.clusters import load_clusters
from speedfog.config import Config, load_config
from speedfog.fog_mod import run_fogmodwrapper
from speedfog.generator import GenerationError, generate_with_retry
from speedfog.item_randomizer import generate_item_config, run_item_randomizer
from speedfog.output import (
    export_json,
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
        default=None,
        help="Output directory (default: config's output_dir or ./seeds). "
        "Files are written to <output>/<seed>/",
    )
    parser.add_argument(
        "--spoiler",
        action="store_true",
        help="Generate spoiler log file",
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

    # Determine output directory: CLI > config > default
    if args.output is not None:
        output_dir = args.output
    else:
        output_dir = Path(config.paths.output_dir)

    # Find clusters.json in data/ relative to project root
    project_root = Path(__file__).parent.parent
    clusters_path = project_root / "data" / "clusters.json"

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

    # Merge roundtable into start cluster for a second exit branch.
    # Only when branching is enabled (max_branches > 1) and parallel paths
    # are allowed (max_parallel_paths > 1), otherwise the extra exit is unused.
    if config.structure.max_branches > 1 and config.structure.max_parallel_paths > 1:
        clusters.merge_roundtable_into_start()

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
    seed_dir = output_dir / str(actual_seed)
    seed_dir.mkdir(parents=True, exist_ok=True)

    # Sample care package if enabled
    care_package_items = None
    if config.care_package.enabled:
        pool_path = project_root / "data" / "care_package_items.toml"
        if pool_path.exists():
            care_package_items = sample_care_package(
                config.care_package, actual_seed, pool_path
            )
            if args.verbose:
                print(f"Care package: {len(care_package_items)} items sampled")
        else:
            print(
                f"Warning: Care package pool not found: {pool_path}",
                file=sys.stderr,
            )

    # Export JSON v4 format (for FogModWrapper and visualization)
    json_path = seed_dir / "graph.json"
    starting_goods = config.starting_items.get_starting_goods()
    export_json(
        dag,
        clusters,
        json_path,
        fog_data=fog_data,
        starting_goods=starting_goods,
        starting_runes=config.starting_items.starting_runes,
        starting_golden_seeds=config.starting_items.golden_seeds,
        starting_sacred_tears=config.starting_items.sacred_tears,
        care_package=care_package_items,
        run_complete_message=config.run_complete_message,
        chapel_grace=config.chapel_grace,
        starting_larval_tears=config.starting_items.larval_tears,
    )
    print(f"Written: {json_path}")
    if starting_goods:
        print(f"Starting items: {len(starting_goods)} goods configured")
    if config.starting_items.starting_runes > 0:
        print(f"Starting runes: {config.starting_items.starting_runes:,}")
    if config.starting_items.golden_seeds > 0:
        print(f"Starting golden seeds: {config.starting_items.golden_seeds}")
    if config.starting_items.sacred_tears > 0:
        print(f"Starting sacred tears: {config.starting_items.sacred_tears}")
    if config.starting_items.larval_tears > 0:
        print(f"Starting larval tears: {config.starting_items.larval_tears}")
    if care_package_items:
        print(f"Care package: {len(care_package_items)} items")
        if args.verbose:
            for item in care_package_items:
                type_names = {0: "Weapon", 1: "Protector", 2: "Accessory", 3: "Goods"}
                print(
                    f"  [{type_names.get(item.type, '?')}] {item.name} (id={item.id})"
                )

    # Export spoiler if requested using output module
    if args.spoiler:
        spoiler_path = seed_dir / "spoiler.txt"
        export_spoiler_log(dag, spoiler_path, care_package=care_package_items)
        print(f"Written: {spoiler_path}")

    # Determine game_dir early (needed for Item Randomizer and FogModWrapper)
    game_dir = args.game_dir or (
        Path(config.paths.game_dir) if config.paths.game_dir else None
    )

    # Run Item Randomizer if enabled
    item_rando_output: Path | None = None
    if config.item_randomizer.enabled and not args.no_build:
        if not game_dir:
            print(
                "Error: --game-dir required for Item Randomizer",
                file=sys.stderr,
            )
            return 1

        if not game_dir.exists():
            print(f"Error: Game directory not found: {game_dir}", file=sys.stderr)
            return 1

        print("Running Item Randomizer...")

        # Generate item_config.json
        item_config = generate_item_config(config, actual_seed)
        item_config_path = seed_dir / "item_config.json"
        with item_config_path.open("w") as f:
            json.dump(item_config, f, indent=2)
        if args.verbose:
            print(f"Written: {item_config_path}")

        # Copy enemy preset
        project_root = Path(__file__).parent.parent
        preset_src = project_root / "data" / "enemy_preset.yaml"
        preset_dst = seed_dir / "enemy_preset.yaml"
        if preset_src.exists():
            shutil.copy(preset_src, preset_dst)
            if args.verbose:
                print(f"Copied: {preset_dst}")
        else:
            print(f"Warning: Enemy preset not found at {preset_src}", file=sys.stderr)

        # Run ItemRandomizerWrapper
        # Output directly to mods/itemrando so ModEngine can load it
        item_rando_dir = seed_dir / "mods" / "itemrando"
        item_rando_dir.mkdir(parents=True, exist_ok=True)

        if run_item_randomizer(
            seed_dir=seed_dir,
            game_dir=game_dir,
            output_dir=item_rando_dir,
            platform=config.paths.platform,
            verbose=args.verbose,
        ):
            item_rando_output = item_rando_dir
        else:
            print(
                "Error: Item Randomizer failed (continuing without it)",
                file=sys.stderr,
            )

    # Build mod unless --no-build
    if not args.no_build:
        # game_dir was already determined above for Item Randomizer
        # but we still need to check it if Item Randomizer was disabled
        if not game_dir:
            print(
                "Error: --game-dir required (or set paths.game_dir in config.toml)",
                file=sys.stderr,
            )
            return 1

        if not game_dir.exists():
            print(f"Error: Game directory not found: {game_dir}", file=sys.stderr)
            return 1

        # Determine merge_dir based on Item Randomizer
        merge_dir = None
        if item_rando_output and item_rando_output.exists():
            merge_dir = item_rando_output

        print("Building mod...")
        if not run_fogmodwrapper(
            seed_dir, game_dir, config.paths.platform, args.verbose, merge_dir
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
