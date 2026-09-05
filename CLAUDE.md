# SpeedFog

Elden Ring mod that generates short randomized runs (~1 hour) with a controlled DAG structure.

## Project Context

SpeedFog creates focused paths from Chapel of Anticipation to a random major boss with:
- Balanced parallel branches (no disadvantaged paths)
- No dead ends (all paths lead to the end)
- Configurable parameters (bosses, dungeons, duration)

Unlike FogRando which randomizes the entire world, SpeedFog generates a smaller, curated experience.

## Architecture

**Hybrid Python + C#:**

```
Python (speedfog/)      C# (writer/)                              Output
─────────────────       ─────────────────                         ─────────────────
config.toml        →                                              output/
clusters.json      →    graph.json → FogModWrapper ──────────┐    ├── mod/
DAG generation     →                      ↑                  ├──► ├── modengine2/
                        item_config.json → ItemRandomizerWrapper  ├── launch_speedfog.bat
                                          (optional)              └── logs/
                                     StaticModBuilder (static mod, at setup)
```

- **Python**: Configuration, cluster/zone data, DAG generation algorithm (package at root)
- **C#**:
  - FogModWrapper - thin wrapper calling FogMod.dll with our graph connections (uses old SoulsFormats.dll)
  - ItemRandomizerWrapper - thin wrapper calling RandomizerCommon.dll for item randomization (optional, uses old SoulsFormats.dll)
  - StaticModBuilder - static mod generator using SoulsFormatsNEXT (runs at setup, not per-seed, output shipped as mods/speedfog/)
- **WitchyBND** (Windows build, run via Wine on Linux): repacks files under `data/mods-src/speedfog/` into `data/mods/speedfog/` at bootstrap. Downloaded lazily into `tools/witchybnd/`. On Linux, the Wine prefix must have the .NET Desktop Runtime installed (`winetricks dotnetdesktop8` or equivalent) — without it `WitchyBND.exe` fails with a cryptic error.
- **Output**: Self-contained folder with ModEngine 2 copied from bootstrap-managed packaging assets

### Item Randomization Workflow

When item randomization is enabled, the workflow is:
1. **ItemRandomizerWrapper** runs first → outputs to `temp/item-randomizer/`
2. **FogModWrapper** runs with `--merge-dir temp/item-randomizer/` → merges item changes
3. Final output contains both fog gate randomization and item randomization

## Directory Structure

```
speedfog/
├── pyproject.toml           # Python project config (at root)
├── speedfog/                # Python package - DAG generation
│   ├── __init__.py
│   ├── main.py              # CLI entry point
│   ├── config.py            # Configuration loading
│   ├── dag.py               # DAG data structures
│   ├── generator.py         # DAG generation algorithm
│   ├── generation_log.py    # Structured generation log (events, summary)
│   ├── clusters.py          # Load and manage clusters from clusters.json
│   ├── planner.py           # Layer planning (cluster type distribution)
│   ├── validator.py         # DAG validation against configuration
│   ├── constants.py         # Shared constants (graph.json version, flag bases, cluster types)
│   ├── graph_export.py      # DAG export to graph.json (Python → C# contract)
│   ├── spoiler.py           # Spoiler log with ASCII graph
│   ├── enemy_data.py        # enemy.txt parsing, boss placement patching
│   ├── care_package.py      # Randomized starting build system
│   ├── tarnished.py         # Tarnished Pack showcase: class loadout + Torrent skin draws
│   ├── boss_arena_constraints.py  # Boss/arena tag model, compat check, matcher
│   ├── fog_mod.py           # FogMod wrapper (runs FogModWrapper.exe)
│   ├── item_randomizer.py   # Item Randomizer integration
│   └── proc.py              # Subprocess streaming with elapsed-time prefixes
├── tests/                   # Python tests
├── data/                    # Shared data files
│   ├── fog.txt              # FogRando zone definitions (gitignored)
│   ├── foglocations2.txt    # FogRando enemy areas (gitignored)
│   ├── fogevents.txt        # FogRando event templates (gitignored)
│   ├── er-common.emedf.json # EMEVD instruction definitions (gitignored)
│   ├── clusters.json        # Generated zone clusters (gitignored)
│   ├── fog_data.json        # Generated fog gate metadata (gitignored)
│   ├── zone_metadata.toml   # Zone weight config (tracked)
│   ├── map_splits.toml      # Synthetic zones/fogs splitting oversized maps (tracked)
│   ├── game_tweaks.toml     # FogMod ConfigVars + startup gate flags + target lists (torrent arenas, spiritspring/stake/entity removals, disabled events, pinned vanilla maps) (tracked)
│   ├── care_package_items.toml  # Curated item pools for care package (tracked)
│   ├── care_package_items_halloween.toml  # Halloween care package pool (tracked)
│   ├── phantom_skins.toml   # Phantom skins catalog (cosmetic auras, tracked)
│   ├── title_screen_overlay.png  # SpeedFog badge composited on the title screen by StaticModBuilder (tracked)
│   ├── boss_arena_tags.json # Boss/arena tags for compatibility matching (tracked)
│   ├── item_preset.yaml     # Item preset configuration (tracked)
│   ├── plugins/             # Plugin data files
│   │   ├── summer.toml      # Summer theme text catalogue (tracked)
│   │   ├── halloween.toml   # Halloween theme text catalogue (tracked)
│   │   ├── halloween_decorations.toml  # Halloween gate decoration catalogue (bones, candelabras, candles; tracked)
│   │   ├── halloween_icon_pumpkin_seed.png  # Golden Seed ("Pumpkin Seed") authored icon art, 160x160 (tracked)
│   │   └── halloween_icon_profane_tear.png  # Sacred Tear ("Profane Tear") authored icon art, 160x160 (tracked)
│   ├── i18n/                # Localization
│   │   ├── fmg_names.json   # FMG name mappings (gitignored, generated by FmgNameExtractor)
│   │   └── fr.toml          # French translation (tracked)
│   ├── packaging/           # Seed package template (ModEngine 2, runtime DLLs, launcher scripts)
│   │   ├── launch_speedfog.bat  # Windows launcher (optional frozen game copy via -p)
│   │   ├── recovery.bat     # Windows save recovery
│   │   └── backups/         # Save backup daemon + launch helpers (PowerShell)
│   ├── mods-src/speedfog/   # Static mod sources, tracked (script/755890_battle-luabnd-dcx/: Untouchable boss battle AI, plain-text Lua)
│   │   └── script/<name>-luabnd-dcx/  # WitchyBND-unpacked layout, repacked at bootstrap
│   ├── mods/speedfog/       # StaticModBuilder output + WitchyBND repacks + user overrides (gitignored)
│   └── mods/speedfog-halloween/  # Halloween icon overlay (05_dummy.tpf.dcx superset), built at bootstrap, shipped only when [plugin.halloween] is enabled (gitignored)
├── writer/                  # C# - Mod file generation
│   ├── lib/                 # DLLs (FogMod, RandomizerCommon, SoulsFormats, etc.)
│   ├── FogModWrapper.Core/  # Shared library (models, graph loading)
│   │   ├── GraphLoader.cs   # Load graph.json v4
│   │   ├── Models/GraphData.cs  # GraphData, Connection, CarePackageItem
│   │   ├── ResourceCalculations.cs  # Pure calculation functions for starting resources
│   │   ├── ShopIdAllocator.cs  # Shop ID allocation utilities
│   │   ├── MapSplitsLoader.cs  # Load data/map_splits.toml (synthetic zones/fogs)
│   │   └── PhaseTimer.cs    # Per-phase timing (standard timer, no ad-hoc Stopwatches)
│   ├── FogModWrapper/       # Fog gate writer - thin wrapper calling FogMod.dll
│   │   ├── Program.cs       # CLI entry point
│   │   ├── ConnectionInjector.cs  # Inject connections into FogMod Graph
│   │   ├── HelperAreaResolver.cs  # Scaling areas for enemy-randomizer helper parts (boss adds) + vanilla misfiled arena parts
│   │   ├── StartingItemInjector.cs  # Inject starting item events into EMEVD
│   │   ├── StartingResourcesInjector.cs  # Inject seeds, tears, keys
│   │   ├── StartingRuneInjector.cs  # Set starting runes via CharaInitParam
│   │   ├── StartingClassRows.cs  # CharaInitParam rows of the selectable classes (from BaseChrSelectMenuParam)
│   │   ├── ClassLoadoutInjector.cs  # Apply Tarnished Pack showcase hand items/armor per starting class
│   │   ├── ClassLoadoutTextPatcher.cs  # Class-selection equipment text updated to the forced loadout (GR_LineHelp, per language)
│   │   ├── RoundtableUnlockInjector.cs  # Unlock Roundtable Hold at start
│   │   ├── ShopInjector.cs           # Add smithing stones + Sentry's Torch to shop
│   │   ├── ZoneTrackingInjector.cs  # Zone tracking flags for racing
│   │   ├── ErdtreeWarpPatcher.cs  # Patch Erdtree fogwarp to target m11_05 directly
│   │   ├── RunCompleteInjector.cs  # Inject "RUN COMPLETE" message on final boss defeat
│   │   ├── ChapelGraceInjector.cs  # Site of Grace at Chapel of Anticipation
│   │   ├── DeathMarkerInjector.cs  # Bloodstain visuals at fog gates
│   │   ├── BossTriggerInjector.cs  # Lock boss arena exits by setting TrapFlag before warp
│   │   ├── RebirthInjector.cs  # Rebirth (stat reallocation) at Sites of Grace
│   │   ├── ShadowRealmBlessingRemover.cs  # Remove DLC "Shadow Realm Blessing" grace menu entry
│   │   ├── ScaduBlessingNeutralizer.cs  # Neutralize Scadutree/Revered Spirit Ash blessing SpEffects
│   │   ├── AlternateFlagPatcher.cs  # Neutralize Event 915, clear flags 300/330 at startup
│   │   ├── PlayRegionPatcher.cs  # Restore vanilla 6000/6001 play region save-limit flags
│   │   ├── SealingTreeWarpPatcher.cs  # Patch Sealing Tree fogwarps (flag 330)
│   │   ├── VanillaWarpRemover.cs  # Remove vanilla assets (warps, blocking gates) and enemy parts listed in game_tweaks.toml
│   │   ├── StartupFlagInjector.cs  # Set event flags at startup (open gates, etc.)
│   │   ├── EventDisabler.cs  # Neutralize vanilla EMEVD events listed in game_tweaks.toml (1.17 NPC invasions)
│   │   ├── VanillaMapPinner.cs  # Ship pinned maps in the seed from FogMod's copy, the Item Randomizer's merge-dir copy, or the snapshot, so later injectors can patch them (game_tweaks.toml pin_vanilla_maps)
│   │   ├── StakeRemover.cs  # Remove vanilla stakes outside DAG
│   │   ├── SpiritspringRemover.cs  # Remove spiritspring jump regions bypassing map-splits chokepoints
│   │   ├── HeavyDoorMessagePatcher.cs  # Suppress "heavy door" popup in common_func
│   │   ├── IntroCutscenePatcher.cs  # Replace the new-game intro cutscene in event 10010020 by SetCurrentTime + ChangeWeather
│   │   ├── TorrentArenaPatcher.cs  # Re-enable Torrent inside selected boss arenas
│   │   ├── WeatherInjector.cs  # Force weather / pin clock hour ([plugin.weather])
│   │   ├── WeaponUpgradeInjector.cs  # Weapon upgrade initialization for starting weapons
│   │   └── eldendata/       # FogRando game data (gitignored)
│   ├── FogModWrapper.Tests/  # xUnit tests
│   ├── ItemRandomizerWrapper/  # Item randomizer - thin wrapper calling RandomizerCommon.dll
│   │   ├── Program.cs       # CLI entry point
│   │   └── diste/           # Item Randomizer game data (gitignored)
│   ├── ItemRandomizerWrapper.Core/  # Shared library for item randomizer
│   │   ├── ArgParser.cs     # Argument parsing
│   │   └── Models.cs        # Data models
│   ├── ItemRandomizerWrapper.Tests/  # xUnit tests for item randomizer
│   ├── FmgNameExtractor/    # Extract FMG names from game data (generates i18n/fmg_names.json)
│   ├── StaticModBuilder/     # Static mod generator, runs at setup (uses SoulsFormatsNEXT submodule)
│   │   ├── Program.cs       # CLI entry point
│   │   ├── GraceAnimationPatcher.cs  # Speed up grace sit/discover animations
│   │   ├── UntouchableTaePatcher.cs  # Retarget four bullet events of c5280 animation 3004 to judge 150 (boss beam carrier)
│   │   ├── TitleScreenPatcher.cs  # Redirect title sprite to a standalone badge texture in 02_title.tpf.dcx
│   │   ├── DdsAtlas.cs      # DX10 DDS header parsing + BC7 block extract + standalone DDS build
│   │   └── LayoutFile.cs    # Menu .layout XML helpers (find/remove SubTexture entries)
│   └── StaticModBuilder.Tests/  # xUnit tests for StaticModBuilder
├── tools/                   # Standalone scripts
│   ├── bootstrap.py             # Project bootstrap (extract deps, build, generate data)
│   ├── generate_clusters.py # Generate clusters.json from fog.txt
│   ├── port_boss_arena_tags.py  # Port BAR data → data/boss_arena_tags.json
│   ├── generate_title_screen.py  # Generate data/title_screen_overlay.png (title badge)
│   ├── generate_halloween_icons.py  # Generate the placeholder Halloween item icon PNGs (data/plugins/)
│   ├── extract_fog_data.py  # Extract fog gate metadata
│   ├── diff_vanilla_snapshot.py  # Hash-diff an unpacked game dir against eldendata/Vanilla (game patch triage)
│   ├── refresh_vanilla_snapshot.py  # Refresh the FogMod snapshot (eldendata/Vanilla) from the game dir (run automatically by bootstrap.py; manual runs for game-patch triage)
│   ├── dump_emevd_warps/    # EMEVD analysis tool (dump warps, search flags, trace inits)
│   └── game_inspect/        # Game data inspection tool (SFX, MSB entities, EMEVD, asset comparison)
├── reference/               # FogRando decompiled code (READ-ONLY)
│   ├── fogrando-src/        # C# source files
│   └── fogrando-data/       # Reference data (foglocations.txt)
├── docs/                    # Documentation
│   ├── architecture.md      # System architecture
│   ├── dag-generation.md    # DAG generation algorithm (exit-driven)
│   ├── clusters.md          # Cluster generation from fog.txt
│   ├── fogmod-emevd-model.md # FogMod EMEVD compilation model
│   ├── connection-injection.md # FogMod Graph connection injection
│   ├── enemy-scaling.md     # FogMod scaling model, tier 21 clamp bypass, blessing neutralization
│   ├── zone-tracking.md     # Zone tracking for racing (region-based lookup)
│   ├── chapel-grace.md      # Chapel of Anticipation Site of Grace
│   ├── starting-items.md    # Starting items and auxiliary flags
│   ├── esd-editing.md       # ESD talk script editing conventions
│   ├── care-package.md      # Randomized starting build system
│   ├── tarnished-showcase.md  # Tarnished Pack showcase mode (class loadout, Torrent skins)
│   ├── untouchable-boss.md  # Aging Untouchable minor boss (vulnerability mechanism, two-phase injector, moveset)
│   ├── item-randomizer.md   # ItemRandomizerWrapper integration
│   ├── event-flags.md       # Event flag allocation and EMEVD reference
│   ├── alternate-warp-patching.md # AlternateFlag warp patching (300/330)
│   ├── item-giving-limitations.md # EMEVD item type constraints
│   ├── vanilla-warp-removal.md # FogMod warp removal workaround
│   ├── stake-removal.md     # Vanilla stake removal (RetryPoint softlock prevention)
│   ├── startup-flag-injection.md # Forcing event flags ON at map load (open gates)
│   ├── death-markers.md     # Bloodstain visuals at fog gates (DrawGroups, DeepCopy bug)
│   ├── boss-trigger-lock.md # Boss arena exit locking (TrapFlag before warp)
│   ├── torrent-arena-patcher.md # Re-enable Torrent in selected boss arenas (DisableTorrent flag)
│   ├── quitout-respawn.md   # Quit-out stable position (PlayRegionParam restore)
│   ├── save-backup.md      # Save backup system (daemon, recovery, config)
│   ├── game-patch-migration.md  # Keeping seeds playable across an Elden Ring update without a FogRando release
│   ├── title-screen.md     # Title screen artwork replacement (BC7 splice at setup)
│   └── plugins/             # Plugin documentation
│       ├── README.md        # Plugin config convention
│       ├── summer-theme.md  # Summer theme specifics
│       ├── halloween-theme.md  # Halloween theme specifics
│       ├── halloween-ambient.md  # Halloween ambient dungeon spawns + gate decorations
│       ├── halloween-icons.md  # Halloween item icon redirect (Golden Seed, Sacred Tear)
│       └── weather.md       # Weather plugin: force weather + pin clock hour
├── SoulsFormats/            # SoulsFormatsNEXT git submodule (used by StaticModBuilder)
└── output/                  # Generated mod (gitignored, self-contained)
```

## Key Files

| File | Purpose |
|------|---------|
| `docs/architecture.md` | System architecture and data formats |
| `docs/dag-generation.md` | DAG generation algorithm (exit-driven routing) |
| `docs/fogmod-emevd-model.md` | FogMod EMEVD compilation model (critical mental model) |
| `docs/connection-injection.md` | Connection injection (shared entrance, ignore_pair) |
| `docs/enemy-scaling.md` | FogMod scaling model, tier 21 clamp bypass, blessing neutralization |
| `docs/zone-tracking.md` | Zone tracking for racing (region-based lookup) |
| `docs/starting-items.md` | Starting items + auxiliary flags (whetblades, Great Runes) |
| `docs/chapel-grace.md` | Chapel of Anticipation Site of Grace (4 subsystems) |
| `docs/esd-editing.md` | ESD editing conventions and ConsistentID allocation |
| `docs/event-flags.md` | Event flag allocation and EMEVD reference |
| `docs/alternate-warp-patching.md` | AlternateFlag warp patching (flags 300/330) |
| `docs/item-randomizer.md` | ItemRandomizerWrapper (preset building, boss placement capture) |
| `docs/boss-arena-constraints.md` | Arena-boss compatibility constraints and matching |
| `docs/care-package.md` | Randomized starting build system |
| `docs/tarnished-showcase.md` | Tarnished Pack showcase mode: `[tarnished]` config, class loadout + Torrent skin draws, graph.json v4.7 fields, flag-to-skin mapping confirmed in-game 2026-09-01 |
| `docs/untouchable-boss.md` | Aging Untouchable minor boss: vulnerability mechanism (nerflantern-style wall lift + partial damage cut), two-phase injector, moveset (own battle script 755890, lantern swing, frenzy beam via judge 150), in-game validation sequence |
| `docs/vanilla-warp-removal.md` | FogMod vanilla warp removal workaround |
| `docs/stake-removal.md` | Vanilla stake removal (RetryPoint softlock prevention) |
| `docs/startup-flag-injection.md` | StartupFlagInjector mechanism + methodology to find new gate flags |
| `docs/death-markers.md` | Bloodstain visuals at fog gates (DrawGroups, DeepCopy bug, entity IDs) |
| `docs/boss-trigger-lock.md` | Boss arena exit locking (TrapFlag vs BossTrigger, warp patching) |
| `docs/torrent-arena-patcher.md` | Re-enable Torrent in selected boss arenas (DisableTorrent collision flag) |
| `docs/quitout-respawn.md` | Quit-out stable position fix (PlayRegionParam restore) |
| `docs/save-backup.md` | Save backup system (daemon, recovery, config) |
| `docs/game-patch-migration.md` | Elden Ring update playbook: pipeline dependencies, scenarios, ordered tasks, save handling (1.17 instance) |
| `docs/item-giving-limitations.md` | EMEVD item type constraints and workarounds |
| `docs/clusters.md` | Cluster generation from fog.txt |
| `docs/opensplit-overrides.md` | Per-warp opensplit overrides (zone_metadata.toml -> Python cluster gen + C# tag injection) |
| `docs/map-splits.md` | Splitting oversized maps with synthetic zones/fogs (map_splits.toml, Enir-Ilim instance) |
| `docs/phantom-skins.md` | Phantom skins catalog (cosmetic player auras for racing rewards) |
| `docs/title-screen.md` | Title screen artwork replacement (BC7 splice at setup) |
| `docs/plugins/README.md` | Plugin config convention (Python passthrough + C# IsPluginEnabled) |
| `docs/plugins/summer-theme.md` | Summer theme: boss epithets + UI banners, catalogue format, discovery |
| `docs/plugins/halloween-theme.md` | Halloween theme: kitsch text reskin, item renames |
| `docs/plugins/halloween-ambient.md` | Halloween ambient dungeon spawns (greeters, ambush packs) + gate decoration catalogue |
| `docs/plugins/halloween-icons.md` | Halloween item icon redirect (Golden Seed, Sacred Tear): 05_dummy overlay + iconId redirect, EXPERIMENT status, revert path, fallbacks |
| `docs/plugins/weather.md` | Weather plugin: force weather + pin clock hour, accepted names, event mechanism |
| `reference/fogrando-src/GameDataWriterE.cs` | Main FogRando writer (5639 lines) |
| `reference/fogrando-src/EldenScaling.cs` | Enemy scaling logic |
| `data/care_package_items.toml` | Curated item pools for care package |
| `data/item_preset.yaml` | Item preset configuration |
| `config.example.toml` | Example configuration |

## Development Guidelines

### Reference Code
- Files in `reference/` are **read-only** - extracted from FogRando for study
- When adapting FogRando code, document the source line numbers
- Key classes from SoulsIds: `GameEditor`, `ParamDictionary`
- **For in-game bugs**: Always consult FogRando sources first - we aim to match its behavior exactly

### Event Templates
- Event templates are loaded directly from FogRando's `data/fogevents.txt`
- SpeedFog aims to match FogRando behavior exactly - no custom templates
- Only add C# logic when FogRando templates cannot express the behavior

### Python (speedfog/)
- Python 3.11+
- TOML for configuration
- JSON for graph output (Python → C# interface)
- Package at root, use `uv run speedfog` from project root

### C# (writer/)
- .NET 10.0
- FogModWrapper and ItemRandomizerWrapper reference pre-built DLLs from `writer/lib/` (old SoulsFormats)
- StaticModBuilder references SoulsFormatsNEXT as a ProjectReference (git submodule at `SoulsFormats/`)
- Follow FogRando patterns for EMEVD/param manipulation

**FogModWrapper** (uses FogMod.dll directly):
| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, loads options, calls FogMod's GameDataWriterE |
| `GraphLoader` | Parses graph.json v4 format from Python |
| `PhantomCatalogLoader` | Loads and validates `data/phantom_skins.toml` |
| `OpenSplitOverrideLoader` | Reads `[warps."<id>"] opensplit = true` from `data/zone_metadata.toml` |
| `GameTweaksLoader` | Loads FogMod ConfigVars, startup gate flags and the six target lists (torrent arenas, spiritspring removals, remove-entities, stake removals, disable-events, pin-vanilla-maps) from `data/game_tweaks.toml` |
| `ResourceCalculations` | Pure calculation functions for starting resources |
| `ShopIdAllocator` | Shop ID allocation utilities |
| `PhaseTimer` | Per-phase timing of Program.cs pipeline steps (use this, not ad-hoc Stopwatches) |
| `ConnectionInjector` | Injects connections into FogMod's Graph, extracts warp data |
| `HelperAreaResolver` | Resolves scaling areas for enemy-randomizer helper parts (boss adds) and re-points misfiled vanilla arena parts (Mini Midra) before FogMod's writer |
| `StartingItemInjector` | Injects starting item events into common.emevd |
| `StartingResourcesInjector` | Injects consumables (seeds, tears, keys) via EMEVD |
| `StartingRuneInjector` | Sets starting runes on all classes via CharaInitParam.soul |
| `StartingClassRows` | Resolves the CharaInitParam rows of the selectable classes from BaseChrSelectMenuParam (covers the 1.17 Tarnished Pack classes) |
| `ClassLoadoutInjector` | Writes the Tarnished Pack showcase hand item + armor set onto CharaInitParam per starting class group (see `docs/tarnished-showcase.md`); runs before `WeaponUpgradeInjector` |
| `ClassLoadoutTextPatcher` | Replaces CharacterWriter's weapon names in the class-selection text (GR_LineHelp 297130+, every language) with the forced loadout's names |
| `RoundtableUnlockInjector` | Unlocks Roundtable Hold at game start |
| `ShopInjector` | Adds smithing stones + Sentry's Torch to Twin Maiden Husks shop |
| `ZoneTrackingInjector` | Injects SetEventFlag before fog gate warps for racing |
| `ErdtreeWarpPatcher` | Patches fogwarps targeting leyndell_erdtree to m11_05, NOPs SkipIfEventFlag(300) in patched events |
| `RunCompleteInjector` | Injects "RUN COMPLETE" golden banner on final boss defeat |
| `ChapelGraceInjector` | Site of Grace + player spawn relocation at Chapel of Anticipation |
| `RebirthInjector` | Rebirth (stat reallocation) option at Sites of Grace via ESD |
| `ShadowRealmBlessingRemover` | Removes the DLC "Shadow Realm Blessing" entry from the grace menu ESD |
| `ScaduBlessingNeutralizer` | Neutralizes Scadutree/Revered Spirit Ash blessing SpEffects (see `docs/enemy-scaling.md`) |
| `SealingTreeWarpPatcher` | Patches Sealing Tree fogwarps to eliminate flag 330 dependency |
| `AlternateFlagPatcher` | Neutralizes Event 915, clears AlternateFlags 300/330 on game start |
| `PlayRegionPatcher` | Restores vanilla 6000/6001 play region save-limit flags that FogMod needlessly remapped |
| `VanillaWarpRemover` | Removes vanilla warp MSB assets that conflict with fog gates, and enemy parts listed in `[[remove_entities]]` (1.17 invader) |
| `StartupFlagInjector` | Sets event flags at startup in any EMEVD (open gates, etc.) |
| `EventDisabler` | Replaces the body of vanilla EMEVD events listed in `[[disable_events]]` of `data/game_tweaks.toml` by an End (Tarnished Pack NPC invasions), skipping events absent from the shipped map data |
| `VanillaMapPinner` | Ships pinned maps in the seed from FogMod's copy, the Item Randomizer's merge-dir copy, or the snapshot, so later injectors can patch them and the player's install does not fill the gap (Radahn arena tile, 1.17 invasion) |
| `StakeRemover` | Removes vanilla stakes that respawn outside the DAG, targets come from `data/game_tweaks.toml` |
| `SpiritspringRemover` | Removes spiritspring jump regions (MountJump/MountJumpFall) that bypass map-splits chokepoints, targets come from `data/game_tweaks.toml` |
| `HeavyDoorMessagePatcher` | Suppresses "heavy door" popup (text 4200) in common_func |
| `IntroCutscenePatcher` | Replaces the new-game intro cutscene (PlayCutsceneToPlayerWithWeatherAndTime 10000040) in event 10010020 of m10_01_00_00 by SetCurrentTime(23:45) + ChangeWeather(Default) (see `docs/chapel-grace.md`) |
| `DeathMarkerInjector` | Bloodstain markers at fog gates (MSB assets + EMEVD SFX) |
| `GateGeometry` | Shared gate FullName parsing, ASide/BSide resolution, deterministic arc offset generation (extracted from `DeathMarkerInjector`) |
| `BossTriggerInjector` | Locks boss arena exit fog gates by setting TrapFlag before entrance warp |
| `TorrentArenaPatcher` | Re-enables Torrent in selected boss arenas by flipping `Collision.DisableTorrent`, targets come from `data/game_tweaks.toml` |
| `WeatherInjector` | Forces a fixed weather and optionally pins the clock hour via a looping common.emevd event (opt-in via `[plugin.weather]`) |
| `WeaponUpgradeInjector` | Weapon upgrade initialization for starting weapons with ashes of war |
| `PhantomCatalogInjector` | Bakes phantom skin catalog (cosmetic auras) into PhantomParam/SpEffectVfxParam/SpEffectParam |
| `OpenSplitInjector` | Tags entrances with `opensplit` before `Graph.Construct` (see `docs/opensplit-overrides.md`) |
| `MapSplitsInjector` | Injects map_splits.toml synthetic zones/fogs into AnnotationData before `Graph.Construct`, splits EnemyAreas (see `docs/map-splits.md`) |
| `TextTheme` | Cosmetic text reskins per theme, summer + halloween, opt-in via `[plugin.summer]` / `[plugin.halloween]` |
| `TextThemeCatalogLoader` | Loads and validates `data/plugins/<theme>.toml` |
| `HalloweenPluginSettings` | Parses `[plugin.halloween]` parameters (`ambushes`) from graph.json's plugin table |
| `HalloweenGateAnchors` | Shared exit-gate anchor collection for the two ambient injectors (source cluster resolved through GraphNode.Zones; decor set includes the start cluster, spawn set does not) |
| `AmbientSpawnInjector` | Places passive greeters + optional decorative ambush packs (token HP, near-zero attack via NpcParam clone) at the anchored exit gates (opt-in via `[plugin.halloween]`, see `docs/plugins/halloween-ambient.md`); runs after GateDecorInjector |
| `GateDecorInjector` | Places catalogue-driven ambient decorations at the same exit gates from `data/plugins/halloween_decorations.toml`, on a per-gate ground estimate (vanilla assets + enemies) |
| `HalloweenDecorLoader` | Loads and validates `data/plugins/halloween_decorations.toml` (Core) |
| `UntouchableBossInjector` | Aging Untouchable minor boss: NpcParam clone + partial damage-cut SpEffect, partial wall (the vanilla immunity wall swapped for the cut in the wall event the randomizer copies per placed boss, so the first parry clears it), boss think row + behavior variation + beam bullet + madness-free lantern pulses when the static assets exist, repoints enemy-randomizer placements (NPCParamID, ThinkParamID), see `docs/untouchable-boss.md` |
| `HalloweenIconInjector` | Repoints Golden Seed/Sacred Tear `EquipParamGoods.iconId` to the Halloween 05_dummy overlay textures (opt-in via `[plugin.halloween]`, see `docs/plugins/halloween-icons.md`) |

**ItemRandomizerWrapper** (uses RandomizerCommon.dll directly):
| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, loads item_config.json, calls Randomizer.Randomize() |

**ItemRandomizerWrapper.Core** (shared library):
| Class | Purpose |
|-------|---------|
| `ArgParser` | CLI argument parsing for item randomizer |
| `Models` | Data models (item config, boss placement) |

**FmgNameExtractor** (standalone tool):
| Class | Purpose |
|-------|---------|
| `Program.cs` | Extracts FMG names from game data, outputs `data/i18n/fmg_names.json` |

**Key FogMod classes** (from FogMod.dll):
| Class | Purpose |
|-------|---------|
| `GameDataWriterE` | Main writer - handles all EMEVD/params/MSB |
| `Graph` | Nodes/edges representing fog connections |
| `RandomizerOptions` | Configuration options (crawl, scale, etc.) |
| `AnnotationData` | Parsed fog.txt data |

**StaticModBuilder** (uses SoulsFormatsNEXT, runs as separate process):
| Class | Purpose |
|-------|---------|
| `Program.cs` | CLI entry, runs all post-processing patches |
| `GraceAnimationPatcher` | Speeds up grace sit/discover animations via TAE event 608 |
| `UntouchableTaePatcher` | Retargets four bullet events of c5280 animation 3004 to judge 150 (the boss's beam carrier; inert for ambient untouchables), layout-guarded |
| `TitleScreenPatcher` | Redirects the title sprite to a standalone composited badge texture in 02_title.tpf.dcx (~1.5 MB shipped, see `docs/title-screen.md`) |
| `HalloweenIconPatcher` | Ships the Golden Seed/Sacred Tear Halloween icons (jack-o'-lantern, burning chalice) as a 05_dummy.tpf.dcx superset (`--halloween-dir`, opt-in overlay output, see `docs/plugins/halloween-icons.md`) |
| `DdsAtlas` | DX10 DDS header parsing + block-aligned BC7 extract + standalone DDS build |
| `LayoutFile` | Menu .layout XML helpers (find/remove SubTexture entries) |

**Key RandomizerCommon classes** (from RandomizerCommon.dll):
| Class | Purpose |
|-------|---------|
| `Randomizer` | Main entry point - orchestrates randomization |
| `RandomizerOptions` | Configuration (item, enemy, seed, difficulty) |
| `Permutation` | Item placement logic |
| `PermutationWriter` | Writes randomized items to params/EMEVD |
| `GameData` | Loads game files from diste/ |

### Zone Data
- Zone definitions extracted from FogRando's `fog.txt` into `clusters.json`
- Zone→map mapping stored in `clusters.json` under `zone_maps`
- Zone types: `legacy_dungeon`, `mini_dungeon`, `boss_arena`, `major_boss`, `start`, `final_boss`
- Weight defaults in `data/zone_metadata.toml`, overrides per zone
- Weight = approximate duration in minutes
- Zone conflicts declared in `zone_metadata.toml` via `conflicts_with` (e.g., Margit/Morgott mutual exclusion)

### FogMod Options
Key options set by FogModWrapper for SpeedFog:

| Option | Value | Purpose |
|--------|-------|---------|
| `crawl` | true | Dungeon crawler mode - enables tier progression |
| `unconnected` | true | Allow edges without vanilla connections |
| `req_backportal` | true | Enable return warps from boss rooms |
| `scale` | true | Apply enemy scaling per tier |

ConfigVars in `Program.cs` set all key items to TRUE (given at start).

## FogRando Reference Points

When implementing features, refer to these sections in `GameDataWriterE.cs`:

| Feature | Lines | Notes |
|---------|-------|-------|
| Load game data | L37-70 | MSBs, EMEVDs, params |
| Fog models | L190-194 | AEG099_230/231/232 |
| Create fog gate | L262+ | `addFakeGate` helper |
| EMEVD events | L1781-1852 | Event creation |
| Scaling | L1964-1966 | EldenScaling integration |
| Write output | L4977-5030 | Params, EMEVDs |

## Setup

```bash
# 1. Install Python dependencies (from project root)
uv pip install -e ".[dev]"

# 2. Initialize SoulsFormatsNEXT submodule (used by StaticModBuilder)
git submodule update --init

# 3. Install sfextract (for extracting DLLs from .NET single-file executables)
dotnet tool install -g sfextract

# 4. Download mods from Nexusmods (requires account):
#    - FogRando: https://www.nexusmods.com/eldenring/mods/3295
#    - Item Randomizer (optional): https://www.nexusmods.com/eldenring/mods/428

# 5. Extract dependencies and generate the static mod (both mods recommended)
python tools/bootstrap.py \
  --game-dir /path/to/ELDEN_RING/Game \
  --fogrando /path/to/FogRando.zip \
  --itemrando /path/to/ItemRandomizer.zip

# Item Randomizer v0.12+ ships only a two-file diste/Vanilla/ stub (manifest
# + regulation.bin); ItemRandomizerWrapper self-extracts the rest from
# --game-dir's BHD/BDT archives on its first run (needs the archives
# present), then reuses the cache on later runs. See docs/item-randomizer.md.
# The bootstrap ends by refreshing FogMod's eldendata snapshot from
# --game-dir (the FogRando zip data is pre-1.17); pass --no-refresh to keep
# the zip contents on a game-patch day (see docs/game-patch-migration.md).

# Or extract only FogRando (legacy mode)
python tools/bootstrap.py \
  --game-dir /path/to/ELDEN_RING/Game \
  --fogrando /path/to/FogRando.zip

# --game-dir is required (provides oo2core_6_win64.dll for dotnet publish
# and enables StaticModBuilder static mod generation).

# 6. Build C# writers (done automatically by setup, or manually)
cd writer/FogModWrapper && dotnet build
cd writer/StaticModBuilder && dotnet build
cd writer/ItemRandomizerWrapper && dotnet build
```

## Commands

```bash
# Generate a run (from project root)
uv run speedfog config.toml --logs
# Creates seeds/<seed>/graph.json and logs/spoiler.txt and logs/generation.log

# Python tests
pytest -v

# C# FogModWrapper - build and publish
cd writer/FogModWrapper
dotnet build
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64

# FogModWrapper - run (Linux with Wine)
wine publish/win-x64/FogModWrapper.exe \
  <seed_dir> \
  --game-dir <game_dir> \
  --data-dir ../../data \
  -o output

# Example paths:
#   seed_dir: seeds/212559448 (contains graph.json and logs/)
#   game_dir: /data/thewall/Game (ELDEN RING/Game folder)

# ItemRandomizerWrapper - build and publish
cd writer/ItemRandomizerWrapper
dotnet build
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64

# ItemRandomizerWrapper - run (generates randomized items)
wine publish/win-x64/ItemRandomizerWrapper.exe \
  item_config.json \
  --game-dir <game_dir> \
  -o temp/item-randomizer

# item_config.json format:
# {"seed": 12345, "difficulty": 50, "options": {"item": true, "enemy": false}}

# Play! (output is self-contained with ModEngine 2 + launcher)
./output/launch_speedfog.bat
```

## Testing

```bash
# Python - all tests
pytest -v

# Python - with coverage
pytest --cov=speedfog

# Tools tests (generate_clusters.py)
cd tools && pytest test_generate_clusters.py -v

# C# - all unit tests
dotnet test writer/SpeedFog.slnx
```

There is no automated end-to-end test of the generated mod: build a seed and
verify it in-game (see Debugging below).

## Debugging

### Philosophy

1. **Match FogRando behavior**: SpeedFog aims to closely replicate FogRando's in-game behavior. When investigating issues, always consult `reference/fogrando-src/` first.

2. **Prefer FogRando parity**: Fix issues by consulting FogRando sources first. Event templates come directly from `data/fogevents.txt` (copied from FogRando).

3. **Reference-driven debugging**: For any in-game problem (fog gates not working, warps failing, scaling issues), first find the equivalent FogRando implementation in `reference/`.

### Generation Timing

Every line streamed from the C# wrappers is prefixed with elapsed time (`[+  12.3s]`, see `speedfog/proc.py`), turning phase-boundary log lines (including FogMod's and RandomizerCommon's `notify` output) into a phase-level profile. FogModWrapper additionally prints a per-phase "Timing breakdown" summary at the end (`PhaseTimer` in FogModWrapper.Core), and the Python pipeline prints its own step breakdown with `-v`. To time a new pipeline step in C#, add a `timer.Phase(...)` call in `Program.cs` instead of an ad-hoc Stopwatch.

### dump_emevd_warps Tool

`tools/dump_emevd_warps/` is a .NET console app for inspecting compiled EMEVD files. Essential for diagnosing warp, flag, and event issues in mod output.

```bash
# Build
cd tools/dump_emevd_warps && dotnet build

# Dump warp instructions from mod output
dotnet run -- dump output/mods/fogmod/event/
dotnet run -- dump output/mods/fogmod/event/ --map-filter m61_44_45

# Dump all instructions in a specific event
dotnet run -- dump output/mods/fogmod/event/ --event 1040290310
dotnet run -- dump output/mods/fogmod/event/ --event all

# Search for all references to a flag (setters, checkers, brute-force scan)
dotnet run -- search output/mods/fogmod/event/ --flag 330

# Trace event initialization (find InitializeEvent calls + parameter data)
dotnet run -- init output/mods/fogmod/event/ --event 1040290310

# Dump RetryPoints (Stakes of Marika) with activation flag/radius/region
dotnet run -- retrypoints output/mods/fogmod/map/mapstudio/ --map-filter m61_49_43
```

### game_inspect Tool

`tools/game_inspect/` is a .NET console app for inspecting game data (SFX bundles, MSB assets, EMEVD events). Requires Wine on Linux (uses Oodle decompression).

```bash
# Build and publish (needed for Wine)
cd tools/game_inspect && dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64

# List SFX IDs in a range within commoneffects
wine publish/win-x64/game_inspect.exe /data/thewall/Game/sfx/ --bundle commoneffects --range 800000-810000

# Search for a specific SFX ID across all bundles
wine publish/win-x64/game_inspect.exe /data/thewall/Game/sfx/ --search 42

# Find MSB entity by ID across all maps in a directory
wine publish/win-x64/game_inspect.exe dump-entity <msb-dir> <entity-id>

# List all assets matching a model name in an MSB
wine publish/win-x64/game_inspect.exe find-model <msb-file> AEG099_090

# List all parts and regions near a position, sorted by distance (default radius 15m)
wine publish/win-x64/game_inspect.exe near <msb-file> 76.7 303.2 127.2 --radius 20

# Compare two assets by entity ID (shows all property differences)
wine publish/win-x64/game_inspect.exe compare <msb-file> <eid1> <eid2>

# Check EMEVD event 0 for instructions referencing entity IDs in 755895xxx range
wine publish/win-x64/game_inspect.exe check-emevd <emevd-file> 0

# List all enemy parts in an MSB with model, NpcParam, entity ID, position, groups
wine publish/win-x64/game_inspect.exe list-enemies <msb-file>

# List parts with a non-unit MSB scale (Enemy + Asset; one MSB or a whole mapstudio dir)
wine publish/win-x64/game_inspect.exe scan-scale <msb-or-mapstudio-dir> [--map-filter m21] [--enemies-only]

# Dump a row from regulation.bin (decrypts ER regulation, applies paramdef XML).
# Use --def-name when the paramdef basename differs from the param (e.g. SpEffect
# for SpEffectParam). Use --field to restrict output to fields containing a
# substring (repeatable).
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> NpcParam --row 44200120 \
  --defs ../../writer/FogModWrapper/eldendata/Defs --field hp --field spEffectID
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> SpEffectParam --row 7800254 \
  --defs ../../writer/FogModWrapper/eldendata/Defs --def-name SpEffect --field maxHpRate
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> NpcParam --prefix 4420 \
  --defs ../../writer/FogModWrapper/eldendata/Defs
# --all lists every row; with --prefix/--all, --field appends the matching
# cells inline (one row per line, pipeable: this feeds speedfog-racing's
# tools/generate_weapons.py together with dump-fmg).
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> EquipParamWeapon --all \
  --field wepType --defs ../../writer/FogModWrapper/eldendata/Defs

# Dump every FMG entry of a msgbnd (fmg name, ID, text), optionally filtered by
# a case-insensitive substring. Output is UTF-8.
wine publish/win-x64/game_inspect.exe dump-fmg <game>/msg/engus/item_dlc02.msgbnd.dcx [substring]

# Game patch triage (see docs/game-patch-migration.md). check-params applies the
# bundled Defs to every param exactly like SoulsIds does for FogMod and lists the
# ones that fail (exit code 2), plus row-count differences against a reference
# regulation.bin (--all lists every param). diff-msb / diff-emevd compare the
# same map across two game versions (added/removed/moved parts, regions, events;
# added/removed/changed EMEVD events). diff-param lists rows added/removed and
# cells changed per param between two regulation.bin. bnd-list identifies DCX
# files an unpacker could not name.
wine publish/win-x64/game_inspect.exe check-params <new-regulation.bin> \
  --defs ../../writer/FogModWrapper/eldendata/Defs --reference <old-regulation.bin> [--all]
wine publish/win-x64/game_inspect.exe diff-param <old-regulation.bin> <new-regulation.bin> \
  --defs ../../writer/FogModWrapper/eldendata/Defs [--param RideParam] [--rows-only]
wine publish/win-x64/game_inspect.exe diff-msb <old.msb.dcx> <new.msb.dcx>
wine publish/win-x64/game_inspect.exe diff-emevd <old.emevd.dcx> <new.emevd.dcx>
wine publish/win-x64/game_inspect.exe bnd-list <game>/_unknown/*

# Vanilla EMEVD under Wine. dump_emevd_warps runs natively on FogMod output
# (DFLT-compressed) but needs Oodle for the game's own KRAK-compressed EMEVDs,
# which has no Linux build. dump-event prints every instruction of one event;
# find-int lists every 4-byte aligned argument slot holding a value (flag, entity ID).
wine publish/win-x64/game_inspect.exe dump-event <game>/event/m11_00_00_00.emevd.dcx 11002930
wine publish/win-x64/game_inspect.exe find-int <game>/event/m11_00_00_00.emevd.dcx 6953
```

### Investigation Tips

**Warp destinations wrong (wrong map variant):**
1. Use `dump_emevd_warps dump --map-filter` to inspect WarpPlayer instructions in the affected map's EMEVD
2. Check if the warp uses an `AlternateFlag` (see `docs/alternate-warp-patching.md`) — look for two WarpPlayer instructions in the same event targeting different map variants
3. Use `dump_emevd_warps search --flag <flag_id>` to find all setters/checkers for the controlling flag
4. Check `fog.txt` Entrances section for `AlternateOf` declarations on the affected zone
5. If a flag is being set by something outside EMEVD (save state, params), patch the warp destinations directly rather than chasing the flag source

**Identifying FogMod-generated events vs vanilla:**
- FogMod warp regions are >= 755890000 (`FOGMOD_ENTITY_BASE`)
- FogMod event IDs are typically in the 1040290xxx range
- Manual fogwarp events may use parameterized entity IDs (shows as `entity_id=0` in raw instructions) — use `init` mode to resolve actual values from InitializeEvent args

**EMEVD events not triggering:**
- Check event templates in `data/fogevents.txt` against FogRando source in `reference/`
- Verify common events are initialized by FogMod's GameDataWriterE
- Confirm entity IDs exist in the target MSB

**Fog gates not visible:**
- Compare `fog_data.json` entries with FogRando's gate creation in `GameDataWriterE.cs:L262+`
- Check model names (AEG099_230/231/232)

**Enemy scaling incorrect:**
- Compare with `EldenScaling.cs` tier definitions
- Verify SpEffect IDs match game params

## Important Notes

- One-way paths (coffins, drops) excluded in v1
- Key items given at start to prevent softlocks
- Enemy scaling uses tiers 1-34 (vanilla's full range)
- **Do NOT commit `data/clusters.json`** — it is generated by `tools/generate_clusters.py` from `data/fog.txt` and is gitignored. It varies depending on the FogRando version used to extract dependencies. Similarly, `data/fog_data.json` and other gitignored data files should never be committed.

## Data Sources

| Data | Source | Format |
|------|--------|--------|
| Key item IDs | `data/fog.txt` L3258-3358 | `ID: 3:XXXX` |
| Zone definitions | `data/fog.txt` Areas section | YAML |
| Warp positions | `data/fog.txt` Entrances section | YAML |
| Enemy scaling tiers | `data/foglocations2.txt` EnemyAreas section | YAML |
| EMEVD instructions | `data/er-common.emedf.json` | JSON |
| Scaling logic | `reference/fogrando-src/EldenScaling.cs` | C# |

## Data Formats

### graph.json v4.7 (Python → C# + visualization + racing)

```json
{
  "version": "4.7",
  "seed": 212559448,
  "options": {"scale": true, "shuffle": true},
  "plugins": {"summer": {"enabled": true}},
  "nodes": {"cluster_id": {"type": "legacy_dungeon", "display_name": "Stormveil Castle", "zones": [...], "layer": 1, "tier": 5, "weight": 15, "exits": [{"fog_id": "AEG099_002_9000", "text": "Godrick front", "from": "stormveil", "to": "other_cluster_id"}], "entrances": [{"text": "before main gate", "from": "source_cluster_id", "to": "stormveil_start", "to_text": "Stormveil Castle Start"}]}},
  "edges": [{"from": "cluster_id_1", "to": "cluster_id_2"}],
  "connections": [
    {"exit_area": "zone1", "exit_gate": "m10_...", "entrance_area": "zone2", "entrance_gate": "m31_...", "flag_id": 1050294000}
  ],
  "area_tiers": {"zone1": 1, "zone2": 5},
  "event_map": {"1050294000": "cluster_id"},
  "finish_event": 1050294002,
  "items_spawned_flag": 1050290000,
  "enemy_assignments": {"30001800": "2049420200"},
  "class_loadout": {"weapons": [{"id": 3560000, "name": "Leontiel's Greatsword"}, ...], "shields": [{"id": 31540000, "name": "Silver Grooved Shield"}, ...], "armor_sets": [[5350000, 5350100, 5350200, 5350300], ...]},
  "torrent_skins": {"unlock": true, "default_flag": 6702}
}
```

- `nodes`/`edges`: DAG topology for visualization tools
- `connections`/`area_tiers`: FogModWrapper consumption (unchanged from v2)
- `event_map`: flag_id (str) → cluster_id mapping for racing zone tracking
- `finish_event`: flag_id set on final boss defeat
- `items_spawned_flag`: saved flag (1050290000) used as one-shot guard for item delivery
- `plugins`: verbatim copy of `[plugin]` config table; C# reads via `GraphData.IsPluginEnabled(name)` (added v4.4)
- `enemy_assignments`: optional `{arena_entity_id: source_entity_id}` map (both decimal strings), the same enemy-randomizer placement mapping already computed in `speedfog/item_randomizer.py` and shipped to ItemRandomizerWrapper as `item_config.json`, now also patched into graph.json (`patch_graph_enemy_assignments`) so FogModWrapper can locate placed bosses; absent or empty when no assignments were made (added v4.5, `GraphData.EnemyAssignments`, consumed by `UntouchableBossInjector`, see `docs/untouchable-boss.md`)
- `class_loadout`: optional, `[tarnished] starting_loadout`'s shuffled weapon/shield/armor-set permutations for the starting classes (weapon always in the right hand, shields once each on occupied left hands, armor per occupied slot; mechanism-named, not pack-named: `[tarnished]` is only its first producer); absent when the option is off (added v4.6, reshaped v4.7, `GraphData.ClassLoadout`, consumed by `ClassLoadoutInjector`, see `docs/tarnished-showcase.md`)
- `torrent_skins`: optional, `[tarnished] unlock_torrent_skins`'s unlock flag plus a resolved `default_flag` (6701-6703) for the pre-selected Torrent skin; absent when the option is off (added v4.6, `GraphData.TorrentSkins`, consumed by `StartingItemInjector`, see `docs/tarnished-showcase.md`)
- `flag_id` per connection: event flag set when fog gate is traversed
- Event flags allocated sequentially from base 1050294000 (range 1050294000-1050294999); persistent flags (e.g. `items_spawned_flag`) come from a separate base 1050290000
- Connections use FogMod's edge FullName format: `{map}_{gate_name}` (e.g., `m10_01_00_00_AEG099_001_9000`)

### fogevents.txt

FogRando EMEVD event templates with parameter notation `X{offset}_{size}`:
- `X0_4` = 4-byte int at offset 0
- `X12_1` = 1-byte value at offset 12

Key templates: `scale`, `showsfx`, `fogwarp`, `common_fingerstart`, `common_roundtable`

### fog_data.json

Fog gate metadata: `{fog_id: {type, zones[], map, entity_id, model, position[], rotation[]}}`

Duplicate fog IDs handled by prefixing with map ID (e.g., `m10_00_00_00_AEG099_002_9000`)
