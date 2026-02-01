# SpeedFog

**SpeedFog** is an Elden Ring mod that generates short randomized runs (~1 hour) with a controlled zone structure. Unlike FogRando which randomizes the entire world, SpeedFog creates a focused path from Chapel of Anticipation to Radagon with balanced parallel branches.

## Features

- **Short runs**: ~1 hour target duration (configurable)
- **Balanced paths**: All routes through the game have similar difficulty/length
- **No dead ends**: Every path leads to Radagon
- **Configurable**: Adjust number of bosses, dungeons, difficulty curve
- **Seed-based**: Share seeds for identical runs

## Requirements

- Elden Ring (Steam version)
- [ModEngine 2](https://github.com/soulsmods/ModEngine2)
- [Elden Ring Enemy and Item Randomizer](https://www.nexusmods.com/eldenring/mods/428) (run this first)
- Python 3.10+ (for DAG generation)
- .NET 8.0 SDK (for building the writer)
- Wine (Linux only, for running the writer)

## Quick Start

### 1. Run Enemy/Item Randomizer First

SpeedFog only randomizes zone connections. Run the Enemy/Item Randomizer first to randomize enemies and item drops.

### 2. Generate a SpeedFog Run

```bash
# Edit config to your preferences
cp config.example.toml config.toml
nano config.toml

# Generate the DAG and spoiler
speedfog config.toml --spoiler -o /tmp/speedfog
# Creates /tmp/speedfog/<seed>/graph.json and spoiler.txt

# Generate mod files (Windows)
SpeedFogWriter.exe /tmp/speedfog/<seed> "C:/Games/ELDEN RING/Game" ./output

# Generate mod files (Linux via Wine - see "Running on Linux" section)
wine SpeedFogWriter.exe /tmp/speedfog/<seed> "/path/to/ELDEN RING/Game" ./output
```

### 3. Install the Mod

```bash
# Copy to ModEngine mods folder
cp -r output/mods/speedfog /path/to/ModEngine/mods/
```

### 4. Launch the Game

Start Elden Ring through ModEngine 2 and begin a new game.

## Configuration

Edit `config.toml` to customize your run:

```toml
[run]
seed = 12345                    # Set to 0 for random seed

[budget]
total_weight = 30               # Target "length" of each path
tolerance = 5                   # Allowed variance (+/- 5)

[requirements]
legacy_dungeons = 1             # Minimum legacy dungeons per run
bosses = 5                      # Minimum bosses before Radagon
mini_dungeons = 5               # Minimum caves/catacombs/tunnels

[structure]
max_parallel_paths = 3          # Maximum simultaneous branches
split_probability = 0.4         # Chance of path splitting
merge_probability = 0.3         # Chance of paths merging

[paths]
game_dir = "C:/Program Files/Steam/steamapps/common/ELDEN RING/Game"
output_dir = "./output"
clusters_file = "./data/clusters.json"
```

## How It Works

### Zone Structure

SpeedFog generates a DAG (Directed Acyclic Graph) of zones:

```
Chapel of Anticipation
         │
    ┌────┴────┐
    ▼         ▼
 Catacomb   Cave        ← Layer 1 (Tier 5)
    │         │
    ▼         │
  Boss    ┌───┘
    │     ▼
    └──►Legacy Dungeon  ← Layer 2 (Tier 10)
              │
         ┌────┴────┐
         ▼         ▼
       Cave      Tunnel ← Layer 3 (Tier 15)
         │         │
         └────┬────┘
              ▼
           Radagon      ← Final (Tier 28)
```

### Enemy Scaling

Enemies are scaled based on their zone's tier (1-28). Early zones have weaker enemies, later zones are harder. The scaling uses SpEffect modifiers for HP, damage, defense, and souls.

### Key Items

All key items (Rusty Key, Stonesword Keys, etc.) are given at the start to prevent softlocks.

## File Structure

```
speedfog/
├── data/                   # Shared data files
│   ├── fog.txt             # FogRando zone definitions (source)
│   ├── foglocations2.txt   # FogRando enemy areas (source)
│   ├── er-common.emedf.json  # EMEVD instruction definitions
│   ├── clusters.json       # Generated zone clusters
│   ├── fog_data.json       # Generated fog gate metadata
│   ├── zone_metadata.toml  # Zone weight defaults/overrides
│   └── speedfog-events.yaml  # EMEVD event templates
├── core/                   # Python - DAG generation
│   └── speedfog_core/      # Python package
├── writer/                 # C# - Mod file generation
│   └── SpeedFogWriter/
├── reference/              # FogRando source for reference (read-only)
│   └── fogrando-src/       # Decompiled C# code
├── docs/plans/             # Implementation specifications
└── output/                 # Generated mod files
```

## Development

See [docs/plans/](docs/plans/) for detailed implementation specifications:

- [Design Overview](docs/plans/2026-01-29-speedfog-design.md)
- [Phase 1: Foundations](docs/plans/phase-1-foundations.md)
- [Phase 2: DAG Generation](docs/plans/phase-2-dag-generation.md)
- [Phase 3: C# Writer](docs/plans/phase-3-csharp-writer.md)

### Building from Source

```bash
# Python core
cd core
pip install -e .

# C# writer (Windows)
cd writer
dotnet build

# C# writer (Linux - cross-compile for Windows)
cd writer
dotnet build -r win-x64
```

### Running on Linux

The C# writer depends on Windows-only libraries (Oodle compression via `oo2core_6_win64.dll`). On Linux, use Wine:

```bash
# 1. Generate the DAG and spoiler (native Linux)
cd core
speedfog config.toml --spoiler -o /tmp/speedfog
# Creates /tmp/speedfog/<seed>/graph.json and spoiler.txt

# 2. Copy Oodle DLL from game folder (required for reading game files)
cp "/path/to/ELDEN RING/Game/oo2core_6_win64.dll" ../writer/SpeedFogWriter/bin/Release/net8.0/win-x64/

# 3. Run the writer with Wine
cd ../writer/SpeedFogWriter/bin/Release/net8.0/win-x64/
wine SpeedFogWriter.exe /tmp/speedfog/<seed> "/path/to/ELDEN RING/Game" ./output
```

**Note**: The game itself runs via Proton on Linux, so this workflow integrates naturally.

## FAQ

**Q: Can I use this with other mods?**
A: SpeedFog is designed to work with Enemy/Item Randomizer. Other mods may conflict.

**Q: Why is there no DLC content?**
A: v1 focuses on base game zones. DLC support planned for v2.

**Q: I'm stuck, is this a softlock?**
A: SpeedFog gives all key items at start, so softlocks should be rare. If stuck, check the spoiler log for the correct path.

**Q: Can I customize which zones appear?**
A: Edit `data/zone_metadata.toml` to adjust zone weights, or modify `data/clusters.json` to exclude specific clusters.

## Credits

- [FogRando](https://www.nexusmods.com/eldenring/mods/3295) by thefifthmatt - Inspiration and reference code
- [SoulsFormats](https://github.com/soulsmods/SoulsFormatsNEXT) - File format library
- [ModEngine 2](https://github.com/soulsmods/ModEngine2) - Mod loading

## License

MIT License - See LICENSE file.
