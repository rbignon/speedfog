# SpeedFog

Elden Ring mod that generates short randomized runs (~1 hour) with balanced parallel paths. Supports base game and Shadow of the Erdtree DLC.

Unlike FogRando which randomizes the entire world, SpeedFog creates a focused path from Chapel of Anticipation to a random major boss with no dead ends.

## Features

- **Short runs**: ~1 hour target duration (configurable)
- **Balanced paths**: All routes have similar difficulty/length
- **No dead ends**: Every path leads to the final boss
- **Configurable final boss**: Radagon, Promised Consort Radahn, Malenia, or any major boss
- **Difficulty curve**: Configurable start/end tiers with linear or power curve progression
- **Cross-links**: Optional connections between parallel branches for more routing options
- **Seed-based**: Share seeds for identical runs
- **Self-contained output**: Includes ModEngine 2 and launcher
- **Item randomization**: Optional integration with Item Randomizer (auto-upgrade, presets, boss randomization, reduced upgrade costs)
- **All crafting recipes**: Optionally unlock all recipes at start (no cookbook hunting)
- **Care package**: Optional randomized starting build (weapons, armor, spells, talismans)
- **Rebirth**: Respec stats at any Site of Grace
- **Racing support**: Zone tracking flags for competitive play

## Requirements

- Elden Ring (Steam version, with or without DLC)
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

# Extract dependencies, generate derived data, and build C# writers
python tools/bootstrap.py \
  --fogrando /path/to/FogRando.zip \
  --itemrando /path/to/ItemRandomizer.zip

# Or FogRando only (no item randomization)
python tools/bootstrap.py --fogrando /path/to/FogRando.zip
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
- `ModEngine/` - ModEngine 2 (auto-downloaded)
- `mods/` - Generated mod files
- `config_speedfog.toml` - ModEngine config
- `launch_speedfog.bat` - Windows launcher
- `linux/launch_speedfog.sh` - Linux/Proton launcher

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

The output folder is self-contained with ModEngine 2:

```bash
# Windows
./seeds/<seed>/launch_speedfog.bat

# Linux (Proton)
./seeds/<seed>/linux/launch_speedfog.sh
```

## Configuration

Edit `config.toml` (see `config.example.toml` for all options).

## How It Works

SpeedFog generates a DAG (Directed Acyclic Graph) of zones:

```
Chapel of Anticipation
         │
    ┌────┴────┐
    ▼         ▼
 Catacomb   Cave         ← Tier 5
    │         │
    ▼         │
  Boss    ┌───┘
    │     ▼
    └──►Legacy Dungeon   ← Tier 10
              │
         ┌────┴────┐
         ▼         ▼
       Cave      Tunnel  ← Tier 15
         │         │
         └────┬────┘
              ▼
         Final Boss       ← Tier 28
```

- **Enemy scaling**: Based on zone tier (configurable floor, ceiling, and curve)
- **Key items**: All given at start to prevent softlocks
- **Fog gates**: Connect zones via FogRando's fog gate system
- **Cross-links**: Optional sideways connections between parallel branches
- **Rebirth**: Respec stats at any Site of Grace (no Larval Tear needed)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## Credits

- [FogRando](https://www.nexusmods.com/eldenring/mods/3295) by thefifthmatt - Core fog gate system
- [Item Randomizer](https://www.nexusmods.com/eldenring/mods/428) by thefifthmatt - Item/enemy randomization
- [SoulsFormats](https://github.com/soulsmods/SoulsFormatsNEXT) - File format library
- [ModEngine 2](https://github.com/soulsmods/ModEngine2) - Mod loading

## License

MIT License
