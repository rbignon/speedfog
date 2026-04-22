"""SpeedFog CLI entry point."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
import time
from pathlib import Path

from speedfog.boss_arena_constraints import load_tags
from speedfog.care_package import sample_care_package
from speedfog.clusters import load_clusters
from speedfog.config import Config, load_config
from speedfog.fog_mod import run_fogmodwrapper
from speedfog.generator import GenerationError, generate_with_retry
from speedfog.item_randomizer import generate_item_config, run_item_randomizer
from speedfog.output import (
    append_boss_placements_to_spoiler,
    export_json,
    export_spoiler_log,
    load_boss_placements,
    load_fog_data,
    load_vanilla_tiers,
    parse_boss_phases,
    patch_graph_boss_placements,
    resolve_entity_id,
)


class StepTimer:
    """Tracks elapsed time per named step."""

    def __init__(self) -> None:
        self.steps: list[tuple[str, float]] = []
        self._start = time.perf_counter()
        self._step_start: float = 0.0
        self._step_name: str | None = None

    def step(self, name: str) -> None:
        """Start a new step, closing the previous one if any."""
        now = time.perf_counter()
        if self._step_name is not None:
            self.steps.append((self._step_name, now - self._step_start))
        self._step_name = name
        self._step_start = now

    def stop(self) -> float:
        """Stop the current step and return total elapsed time."""
        now = time.perf_counter()
        if self._step_name is not None:
            self.steps.append((self._step_name, now - self._step_start))
            self._step_name = None
        return now - self._start

    def format_summary(self) -> str:
        """Format per-step timing summary."""
        total = sum(d for _, d in self.steps)
        lines = []
        for name, duration in self.steps:
            pct = (duration / total * 100) if total > 0 else 0
            lines.append(f"  {name:<25s} {duration:6.2f}s  ({pct:4.1f}%)")
        return "\n".join(lines)


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
        "--logs",
        action="store_true",
        help="Generate diagnostic logs (spoiler.txt, generation.log)",
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
        help="Skip mod building and ItemRandomizer (inputs still written)",
    )
    parser.add_argument(
        "--game-dir",
        type=Path,
        help="Path to Elden Ring Game directory (overrides config)",
    )

    args = parser.parse_args()

    timer = StepTimer()
    timer.step("Generate DAG")

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
    # Only when splitting is enabled (max_exits > 1) and parallel paths
    # are allowed (max_parallel_paths > 1), otherwise the extra exit is unused.
    if config.structure.max_exits > 1 and config.structure.max_parallel_paths > 1:
        clusters.merge_roundtable_into_start()

    # Snapshot boss clusters before passant filter removes dead-end arenas.
    # Dead-end bosses (0 exits) are invalid as passant nodes but valid as
    # final boss endpoints — the run terminates there.
    boss_candidates = clusters.get_by_type("major_boss") + clusters.get_by_type(
        "final_boss"
    )

    # Filter clusters that can never be passant nodes (1 bidir entry + 1 exit)
    removed = clusters.filter_passant_incompatible()
    if args.verbose and removed:
        print(f"Filtered {len(removed)} passant-incompatible clusters")

    # Load fog_data.json for accurate map lookups
    fog_data_path = clusters_path.parent / "fog_data.json"
    fog_data = load_fog_data(fog_data_path) if fog_data_path.exists() else None
    if args.verbose and fog_data:
        print(f"Loaded {len(fog_data)} fogs from {fog_data_path}")

    # Load vanilla scaling tiers for original_tier in graph.json
    foglocations_path = clusters_path.parent / "foglocations2.txt"
    vanilla_tiers = load_vanilla_tiers(foglocations_path)
    if args.verbose and vanilla_tiers:
        print(f"Loaded {len(vanilla_tiers)} vanilla tiers from {foglocations_path}")

    # Generate DAG
    if args.verbose:
        mode = "fixed seed" if config.seed != 0 else "auto-reroll"
        print(f"Generating DAG ({mode})...")

    try:
        result = generate_with_retry(
            config,
            clusters,
            max_attempts=args.max_attempts,
            boss_candidates=boss_candidates,
        )
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
        print(f"  Layers: {max((n.layer for n in dag.nodes.values()), default=0) + 1}")
        print(f"  Nodes: {len(dag.nodes)}")
        if dag.crosslinks_added > 0:
            print(f"  Cross-links: {dag.crosslinks_added}")

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

    # Resolve run_complete_message (seeded pick when a list is configured).
    run_complete_message = config.resolve_run_complete_message(actual_seed)

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
        run_complete_message=run_complete_message,
        chapel_grace=config.chapel_grace,
        sentry_torch_shop=config.sentry_torch_shop,
        starting_larval_tears=config.starting_items.larval_tears,
        starting_stonesword_keys=config.starting_items.stonesword_keys,
        vanilla_tiers=vanilla_tiers,
        death_markers=config.death_markers,
        weapon_upgrade=config.care_package.weapon_upgrade
        if config.care_package.enabled
        else 0,
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

    # Export logs if requested
    spoiler_path: Path | None = None
    if args.logs:
        logs_dir = seed_dir / "logs"
        logs_dir.mkdir(parents=True, exist_ok=True)
        _spoiler: Path = logs_dir / "spoiler.txt"
        spoiler_path = _spoiler
        export_spoiler_log(dag, _spoiler, care_package=care_package_items)
        print(f"Written: {_spoiler}")

        from speedfog.generation_log import export_generation_log

        gen_log_path = logs_dir / "generation.log"
        export_generation_log(result.log, gen_log_path, dag=dag)
        print(f"Written: {gen_log_path}")

    # Determine game_dir early (needed for Item Randomizer and FogModWrapper)
    game_dir = args.game_dir or (
        Path(config.paths.game_dir) if config.paths.game_dir else None
    )

    # Generate Item Randomizer inputs if enabled (even with --no-build)
    item_rando_output: Path | None = None
    if config.item_randomizer.enabled:
        timer.step("Item Randomizer inputs" if args.no_build else "Item Randomizer")

        # Generate item_config.json
        enemy_txt_path = clusters_path.parent / "enemy.txt"
        phase_mapping = parse_boss_phases(enemy_txt_path)

        def _vanilla_ids_of_type(t: str) -> list[int]:
            out: list[int] = []
            for c in clusters.get_by_type(t):
                eid = resolve_entity_id(c.defeat_flag)
                if eid:
                    out.append(eid)
            return out

        if config.enemy.randomize_bosses != "none":
            tags = load_tags(project_root / "data" / "boss_arena_tags.json")
            vanilla_major_ids = _vanilla_ids_of_type("major_boss")
            vanilla_minor_ids = _vanilla_ids_of_type("boss_arena")
        else:
            tags = None
            vanilla_major_ids = []
            vanilla_minor_ids = []

        boss_clusters_for_assignment = [
            n.cluster
            for n in dag.nodes.values()
            if n.cluster.type in ("boss_arena", "major_boss") and n.cluster.defeat_flag
        ]
        item_config = generate_item_config(
            config,
            actual_seed,
            boss_clusters=boss_clusters_for_assignment,
            tags=tags,
            vanilla_major_ids=vanilla_major_ids,
            vanilla_minor_ids=vanilla_minor_ids,
            phase_mapping=phase_mapping,
        )
        item_config_path = seed_dir / "item_config.json"
        with item_config_path.open("w") as f:
            json.dump(item_config, f, indent=2)
        if args.verbose:
            print(f"Written: {item_config_path}")

        # Copy item preset
        item_preset_path = seed_dir / "item_preset.yaml"
        if config.item_randomizer.item_preset:
            if config.item_randomizer.item_preset_path:
                item_preset_src = Path(config.item_randomizer.item_preset_path)
            else:
                item_preset_src = project_root / "data" / "item_preset.yaml"
            if item_preset_src.exists():
                shutil.copy(item_preset_src, item_preset_path)
                if args.verbose:
                    print(f"Copied: {item_preset_path}")
            else:
                print(
                    f"Warning: Item preset not found at {item_preset_src}",
                    file=sys.stderr,
                )

        # Run ItemRandomizerWrapper (skipped with --no-build)
        if not args.no_build:
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

                # Patch graph.json with boss placements if available
                boss_placements_path = item_rando_dir / "boss_placements.json"
                boss_placements = load_boss_placements(boss_placements_path)
                if boss_placements:
                    patch_graph_boss_placements(
                        json_path, dag, boss_placements, phase_mapping
                    )
                    print(f"Boss placements: {len(boss_placements)} bosses randomized")

                    # Append boss placements to spoiler log
                    if args.logs:
                        assert spoiler_path is not None
                        append_boss_placements_to_spoiler(spoiler_path, boss_placements)
            else:
                print(
                    "Error: Item Randomizer failed",
                    file=sys.stderr,
                )
                return 1

            # Clean up transient Item Randomizer config files
            item_config_path.unlink(missing_ok=True)
            item_preset_path.unlink(missing_ok=True)

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

        timer.step("Build mod")
        print("Building mod...")
        if not run_fogmodwrapper(
            seed_dir, game_dir, config.paths.platform, args.verbose, merge_dir
        ):
            print(
                "Error: Mod build failed (graph.json preserved for debugging)",
                file=sys.stderr,
            )
            return 1

        # Copy overlay files (e.g. patched animations from setup) into mod output
        overlay_dir = project_root / "data" / "overlay"
        if overlay_dir.is_dir():
            mod_dir = seed_dir / "mods" / "fogmod"
            count = 0
            for src in overlay_dir.rglob("*"):
                if src.is_file():
                    rel = src.relative_to(overlay_dir)
                    dest = mod_dir / rel
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(src, dest)
                    count += 1
                    if args.verbose:
                        print(f"Overlay: {rel}")
            if count > 0:
                print(f"Overlay: copied {count} file(s) from data/overlay/")

        print(f"Mod ready: {seed_dir}")

    total = timer.stop()
    print(f"Done in {total:.2f}s")
    if args.verbose and len(timer.steps) > 1:
        print("Timing breakdown:")
        print(timer.format_summary())

    return 0


if __name__ == "__main__":
    sys.exit(main())
