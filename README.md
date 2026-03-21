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
- .NET 8.0 SDK
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
python tools/setup_dependencies.py \
  --fogrando /path/to/FogRando.zip \
  --itemrando /path/to/ItemRandomizer.zip

# Or FogRando only (no item randomization)
python tools/setup_dependencies.py --fogrando /path/to/FogRando.zip
```

### 3. Configure

```bash
cp config.example.toml config.toml
# Edit config.toml to set your game directory and preferences
```

## Usage

### Generate and Build a Run

```bash
uv run speedfog config.toml --spoiler
```

Output is self-contained in `seeds/<seed>/`:
- `graph.json` - DAG definition
- `spoiler.txt` - Spoiler log
- `ModEngine/` - ModEngine 2 (auto-downloaded)
- `mods/` - Generated mod files
- `config_speedfog.toml` - ModEngine config
- `launch_speedfog.bat` - Windows launcher
- `linux/launch_speedfog.sh` - Linux/Proton launcher

### CLI Options

```bash
uv run speedfog [config_file] [options]
  --output/-o DIR         # Output directory (overrides config)
  --spoiler               # Generate spoiler log
  --seed INT              # Random seed (overrides config, 0=auto-reroll)
  --max-attempts INT      # Max retries for auto-reroll (default: 100)
  --verbose/-v            # Verbose output
  --no-build              # Skip mod building (graph.json only)
  --game-dir PATH         # Game directory (overrides config)
```

### Generate Only (no mod build)

```bash
uv run speedfog config.toml --no-build --spoiler
```

This creates only `graph.json` and `spoiler.txt`. To build manually, see `writer/README.md`.

### Play

The output folder is self-contained with ModEngine 2:

```bash
# Windows
./seeds/<seed>/launch_speedfog.bat

# Linux (Proton)
./seeds/<seed>/linux/launch_speedfog.sh
```

## Configuration

Edit `config.toml` (see `config.example.toml` for all options):

```toml
[run]
seed = 0                        # 0 = random seed
# run_complete_message = "RUN COMPLETE"  # Golden banner on boss defeat
# chapel_grace = true                    # Site of Grace at starting location

[budget]
tolerance = 5                   # Max weight spread between paths

[requirements]
legacy_dungeons = 1             # Minimum legacy dungeons
bosses = 5                      # Minimum bosses before final boss
major_bosses = 8                # Minimum major boss encounters
mini_dungeons = 5               # Minimum caves/catacombs
# zones = ["caelid_radahn"]     # Force specific zones to appear

[structure]
max_parallel_paths = 3          # Max concurrent branches
final_tier = 28                 # Enemy scaling ceiling (1-28)
# start_tier = 1                # Enemy scaling floor (1-28)
# tier_curve = "linear"         # "linear" or "power"
# tier_curve_exponent = 0.6     # Power curve exponent (< 1.0 = front-loaded)
# crosslinks = true             # Cross-links between parallel branches
# final_boss_candidates = ["leyndell_erdtree", "enirilim_radahn"]

[paths]
game_dir = "/path/to/ELDEN RING/Game"

[starting_items]
# Key items, Great Runes, DLC items, talisman pouches, consumable resources
# See config.example.toml for the full list

[care_package]
enabled = false                 # Randomized starting build
# weapon_upgrade = 8            # Upgrade level for starting weapons

[item_randomizer]
enabled = true                  # Item Randomizer integration
# remove_requirements = true    # Drop stat requirements on weapons/spells
# auto_upgrade_weapons = true   # New weapons match your highest upgrade
# auto_upgrade_dropped = true   # Dropped items auto-match upgrade level
# reduce_upgrade_cost = true    # 1 smithing stone per upgrade level
# allcraft = true               # Unlock all crafting recipes at start
# item_preset = true            # Weapons to bosses, seeds/tears to key locations
# dlc = true                    # Include DLC items and enemies

[enemy]
randomize_bosses = "none"       # "none", "minor", or "all"
# ignore_arena_size = false     # Allow large bosses in small arenas
```

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
