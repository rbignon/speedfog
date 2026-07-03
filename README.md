# SpeedFog

Elden Ring mod that generates short randomized runs (~1 hour) with balanced parallel paths. Supports base game and Shadow of the Erdtree DLC.

Unlike FogRando which randomizes the entire world, SpeedFog creates a focused path from Chapel of Anticipation to a random major boss with no dead ends.

## Features

**Run structure**
- **Short runs**: ~1 hour target duration (configurable)
- **Balanced paths, no dead ends**: parallel routes have similar length/difficulty, and every path leads to the final boss
- **Run modes**: restrict cluster types for a boss rush, legacy-dungeon marathon, dungeon crawl, and more (`allowed_types`)
- **Configurable final boss**: Radagon, Promised Consort Radahn, or any major boss (weighted candidates)
- **Zone control**: force specific zones/bosses to appear, or exclude them entirely
- **Difficulty curve**: configurable start/end tiers with linear or power progression
- **Seed-based**: share seeds for identical runs

**Gameplay**
- **Item randomization**: optional Item Randomizer integration (auto-upgrade, placement presets, reduced upgrade costs, all crafting recipes)
- **Boss randomization**: optionally swap boss entities across arenas (minor-only or all), with arena-size constraints and "boss-only" run support
- **Care package**: optional randomized starting build (weapons, armor, spells, talismans)
- **Starting loadout**: key items, Great Runes, whetblades, talisman pouches, and consumables given at start (no softlocks)
- **Rebirth**: respec stats at any Site of Grace (consumes a Larval Tear; a stack is given at start)

**Quality of life & cosmetics**
- **QoL tweaks**: Chapel Site of Grace, faster grace animations, menu input delay removed, etc.
- **Cosmetic themes**: customizable victory banner and opt-in summer theme
- **Racing support**: zone-tracking flags, death markers, and phantom-skin rewards for competitive play

**Packaging**
- **Self-contained output**: includes ModEngine 2 and a launcher

## Requirements

- Elden Ring (Steam version)
- Python 3.10+
- .NET 10.0 SDK
- Wine (Linux only)

## Installation

### 1. Download Dependencies

From Nexusmods (requires account):
- **Required**: [Elden Ring Fog Gate Randomizer](https://www.nexusmods.com/eldenring/mods/3295)
- **Optional**: [Elden Ring Item and Enemy Randomizer](https://www.nexusmods.com/eldenring/mods/428) - for item/enemy randomization

### 2. Clone and Setup

```bash
git clone https://github.com/rbignon/speedfog.git
cd speedfog

# Install Python dependencies
uv pip install -e .

# Install sfextract (extracts DLLs from FogRando)
dotnet tool install -g sfextract

# Extract dependencies, generate derived data, build C# writers, and install ModEngine 2
python tools/bootstrap.py \
  --game-dir /path/to/ELDEN_RING/Game \
  --fogrando /path/to/FogRando.zip \
  --itemrando /path/to/ItemRandomizer.zip

# Or FogRando only (no item randomization)
python tools/bootstrap.py \
  --game-dir /path/to/ELDEN_RING/Game \
  --fogrando /path/to/FogRando.zip
```

### 3. Configure

```bash
cp config.example.toml config.toml
# Edit config.toml to set your game directory and preferences
```

## Usage

### Generate and Build a Run

```bash
uv run speedfog config.toml --logs
```

Output is self-contained in `seeds/<seed>/`:
- `graph.json` - DAG definition
- `logs/spoiler.txt` - Spoiler log
- `logs/generation.log` - Structured generation log
- `modengine2/` - ModEngine 2 binaries + `config_speedfog.toml`
- `lib/` - runtime DLLs loaded by ModEngine 2 (always `MenuInputDelayFix.dll`; item randomizer DLLs when enabled)
- `mods/` - Generated mod files
- `launch_speedfog.bat` - Windows launcher

### CLI Options

```bash
uv run speedfog [config_file] [options]
  --output/-o DIR         # Output directory (overrides config)
  --logs                  # Generate spoiler log and generation log
  --seed INT              # Random seed (overrides config, 0=auto-reroll)
  --max-attempts INT      # Max retries for auto-reroll (default: 100)
  --verbose/-v            # Verbose output
  --no-build              # Skip mod building (graph.json only)
  --game-dir PATH         # Game directory (overrides config)
```

### Generate Only (no mod build)

```bash
uv run speedfog config.toml --no-build --logs
```

This creates only `graph.json` and `logs/`. To build manually, see `writer/README.md`.

### Play

The output folder is self-contained with ModEngine 2 copied from `data/packaging/`:

```bash
./seeds/<seed>/launch_speedfog.bat
```

## Configuration

Edit `config.toml` (see `config.example.toml` for all options).

## How It Works

SpeedFog builds a DAG (Directed Acyclic Graph) of zones with an **exit-driven**
algorithm.

Layer by layer, it picks a set of clusters of the same type and routes the
previous layer's fog-gate exits into them, reusing spare exits to widen the
graph toward `max_parallel_paths` and to weave cross-links between the parallel
branches, until a convergence phase narrows the width back down to a single
node before the final boss.

```
        Chapel of Anticipation
                 │
            ┌────┴────┐        ← Chapel and Roundtable fogs
            ▼         ▼
         Legacy     Legacy     ← saturation: use most exits to open
         Dungeon    Dungeon      parallel branches (up to max_parallel_paths)
            │  \   /  │          and cross-link them
            │   \ /   │
            ▼  /   \  ▼
          Boss       Boss      ← reuse entrance as exits on boss arenas
            │  \   /  │          to prevent dead ends
            │   \ /   │
            ▼  /   \  ▼
          Cave     Catacombs
            │         │
            └────┬────┘        ← convergence: funnel the branches back
                 ▼               down to a single node
            Final Boss         ← final boss (final tier)
```

- **Exit-driven routing**: each layer's clusters are fed by the previous layer's fog-gate exits; splits, merges, and cross-links all emerge from this routing rather than being scheduled
- **Fog-gate reuse**: arena's entrance fogs double as exits toward the next layer, so the same gates that let you in can carry you onward (bidirectional gates)
- **Balanced branches**: cluster weights (≈ traversal time) are weight-matched within each layer, so parallel routes stay comparable and races stay fair
- **Enemy scaling**: each layer carries a tier from `start_tier` to `final_tier` (linear or power curve), applied through FogRando's per-zone scaling
- **Key items at start**: progression items are granted up front to prevent softlocks

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## Credits

- [FogRando](https://www.nexusmods.com/eldenring/mods/3295) by thefifthmatt - Core fog gate system
- [Item Randomizer](https://www.nexusmods.com/eldenring/mods/428) by thefifthmatt - Item/enemy randomization
- [SoulsFormats](https://github.com/soulsmods/SoulsFormatsNEXT) - File format library
- [ModEngine 2](https://github.com/soulsmods/ModEngine2) - Mod loading

## License

SpeedFog is licensed under the GNU General Public License v3.0 or later (GPL-3.0-or-later). See [LICENSE](LICENSE) for the full text.

Copyright (C) 2026 Romain Bignon

SpeedFog adapts logic from FogRando (credited above) and links against third-party binaries (FogMod, RandomizerCommon, SoulsFormats, ModEngine 2) that users download at setup time. Those binaries are not redistributed in this repository and remain under their respective licenses.
