# SpeedFog

Elden Ring mod that generates short randomized runs (~1 hour) with balanced parallel paths.

Unlike FogRando which randomizes the entire world, SpeedFog creates a focused path from Chapel of Anticipation to Radagon with no dead ends.

## Features

- **Short runs**: ~1 hour target duration (configurable)
- **Balanced paths**: All routes have similar difficulty/length
- **No dead ends**: Every path leads to Radagon
- **Seed-based**: Share seeds for identical runs
- **Self-contained output**: Includes ModEngine 2 and launcher

## Requirements

- Elden Ring (Steam version, base game)
- Python 3.10+
- .NET 8.0 SDK
- Wine (Linux only)

## Installation

### 1. Download FogRando

Download [Elden Ring Fog Gate Randomizer](https://www.nexusmods.com/eldenring/mods/3295) from Nexusmods (requires account).

### 2. Clone and Setup

```bash
git clone https://github.com/user/speedfog.git
cd speedfog

# Install Python dependencies
uv pip install -e .

# Install sfextract (extracts DLLs from FogRando)
dotnet tool install -g sfextract

# Extract FogRando dependencies and build FogModWrapper
python tools/setup_dependencies.py /path/to/FogRando.zip
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
- `spoiler.txt` - Solution path
- `mods/speedfog/` - Generated mod files
- `launch_speedfog.bat` - Windows launcher
- `launch_speedfog.sh` - Linux/Proton launcher

### Generate Only (no mod build)

```bash
uv run speedfog config.toml --no-build --spoiler
```

Then build manually:
```bash
wine writer/FogModWrapper/publish/win-x64/FogModWrapper.exe \
  seeds/<seed> --game-dir /path/to/game --data-dir data -o seeds/<seed>
```

### Play

The output folder is self-contained with ModEngine 2:

```bash
# Windows
./seeds/<seed>/launch_speedfog.bat

# Linux (Proton)
./seeds/<seed>/launch_speedfog.sh
```

## Configuration

Edit `config.toml`:

```toml
[run]
seed = 0                        # 0 = random seed

[budget]
total_weight = 30               # Target path length (~minutes)
tolerance = 5                   # Allowed variance

[requirements]
legacy_dungeons = 1             # Minimum legacy dungeons
bosses = 5                      # Minimum bosses before Radagon
mini_dungeons = 5               # Minimum caves/catacombs

[paths]
game_dir = "/path/to/ELDEN RING/Game"
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
           Radagon       ← Tier 28
```

- **Enemy scaling**: Based on zone tier (1-28)
- **Key items**: All given at start to prevent softlocks
- **Fog gates**: Connect zones via FogRando's fog gate system

## FAQ

**Q: Can I use this with other mods?**
A: SpeedFog modifies zone connections and enemy scaling. Other mods may conflict.

**Q: Why base game only?**
A: v1 focuses on base game. DLC support planned for v2.

**Q: I'm stuck, is this a softlock?**
A: Check the spoiler log (`seeds/<seed>/spoiler.txt`) for the correct path.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## Credits

- [FogRando](https://www.nexusmods.com/eldenring/mods/3295) by thefifthmatt - Core fog gate system
- [SoulsFormats](https://github.com/soulsmods/SoulsFormatsNEXT) - File format library
- [ModEngine 2](https://github.com/soulsmods/ModEngine2) - Mod loading

## License

MIT License - See [LICENSE](LICENSE) file.
