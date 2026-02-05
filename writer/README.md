# SpeedFog Writer

C# components of SpeedFog - thin wrappers around FogMod.dll and RandomizerCommon.dll.

## Components

| Component | Purpose |
|-----------|---------|
| **FogModWrapper** | Generates fog gate connections from graph.json using FogMod.dll |
| **ItemRandomizerWrapper** | Randomizes items/enemies using RandomizerCommon.dll (optional) |

## Requirements

- .NET 8.0 SDK
- Elden Ring game files (regulation.bin, MSBs, EMEVDs)
- Dependencies extracted via `tools/setup_dependencies.py`

## Building

```bash
# Build all projects
cd writer
dotnet build SpeedFog.slnx

# Or build individually
cd writer/FogModWrapper && dotnet build
cd writer/ItemRandomizerWrapper && dotnet build

# For self-contained Windows executables:
cd writer/FogModWrapper && dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
cd writer/ItemRandomizerWrapper && dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
```

## FogModWrapper

Generates the fog gate mod from a graph.json.

### Usage

```bash
FogModWrapper.exe <seed_dir> --game-dir <game_dir> [options]
```

### Arguments

- `seed_dir` - Path to seed directory (contains graph.json, spoiler.txt)

### Options

- `--game-dir <path>` - Path to Elden Ring's Game folder (contains regulation.bin)
- `--data-dir <path>` - Custom path to data directory (default: ../data)
- `-o, --output <path>` - Where to write mod files (default: ./output)
- `--merge-dir <path>` - Merge files from another mod (e.g., Item Randomizer output)
- `--no-package` - Skip ModEngine packaging (output mod files only)
- `--update-modengine` - Force re-download of ModEngine 2

## ItemRandomizerWrapper

Randomizes items and enemies. Output can be merged into FogModWrapper via `--merge-dir`.

### Usage

```bash
ItemRandomizerWrapper.exe <config_path> --game-dir <game_dir> [options]
```

### Arguments

- `config_path` - Path to item_config.json

### Options

- `--game-dir <path>` - Path to Elden Ring's Game folder
- `--data-dir <path>` - Custom path to diste directory (default: ./diste)
- `-o, --output <path>` - Where to write randomized files (default: ./output)

### Config Format (item_config.json)

```json
{
  "seed": 12345,
  "difficulty": 50,
  "preset": "speedfog_enemy",
  "options": {
    "item": true,
    "enemy": true
  }
}
```

## Output

FogModWrapper creates a self-contained mod with ModEngine 2:

```
output/
├── ModEngine/              # ModEngine 2 (auto-downloaded)
├── mods/
│   ├── fogmod/             # Fog gate mod files
│   │   ├── param/gameparam/regulation.bin
│   │   ├── event/*.emevd.dcx
│   │   └── map/mapstudio/*.msb.dcx
│   └── itemrando/          # Item Randomizer files (if enabled)
├── config_speedfog.toml    # ModEngine config
├── launch_speedfog.bat     # Windows launcher
├── launch_speedfog.sh      # Linux/Proton launcher
└── spoiler.txt             # Path spoiler log
```

## Playing

After generating the mod:
- **Windows**: Double-click `output/launch_speedfog.bat`
- **Linux/Proton**: Run `output/launch_speedfog.sh`

## Development

Both wrappers use their respective DLLs directly, injecting SpeedFog-specific configuration.

See [Architecture](../docs/architecture.md) for details.

### Running Tests

```bash
cd writer/test
./run_integration.sh
```
