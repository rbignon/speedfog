# FogModWrapper

C# component of SpeedFog - a thin wrapper around FogMod.dll that generates Elden Ring mod files from a graph.json.

## Requirements

- .NET 8.0 SDK
- Elden Ring game files (regulation.bin, MSBs, EMEVDs)
- Data files from `../data/` (fog_data.json, clusters.json, etc.)

## Building

```bash
cd writer/FogModWrapper
dotnet build

# For self-contained Windows executable:
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
```

## Usage

```bash
FogModWrapper.exe <seed_dir> --game-dir <game_dir> [options]
```

### Arguments

- `seed_dir` - Path to seed directory (contains graph.json, spoiler.txt)

### Options

- `--game-dir <path>` - Path to Elden Ring's Game folder (contains regulation.bin)
- `--data-dir <path>` - Custom path to data directory (default: ../data)
- `-o, --output <path>` - Where to write mod files (default: ./output)
- `--no-package` - Skip ModEngine packaging (output mod files only)
- `--update-modengine` - Force re-download of ModEngine 2

## Output

Creates a self-contained mod with ModEngine 2 and launcher scripts:

```
output/
├── ModEngine/              # ModEngine 2 (auto-downloaded)
├── mods/speedfog/          # Mod files
│   ├── param/gameparam/regulation.bin
│   ├── event/*.emevd.dcx
│   └── map/mapstudio/*.msb.dcx
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

FogModWrapper uses FogMod.dll directly from FogRando, injecting our custom connections into its graph.

See [Architecture](../docs/architecture.md) for details.

### Running Tests

```bash
cd writer/test
./run_integration.sh
```
