# Reference Files

This directory contains reference materials extracted from FogRando and related tools. These files are for **reference only** - do not modify them directly.

## Directory Structure

```
reference/
├── fogrando-src/      # Decompiled C# source from FogRando
├── fogrando-data/     # Zone/event data files from FogRando
└── lib/               # DLL dependencies
```

## fogrando-src/

Key C# files from FogRando (decompiled) that are relevant to SpeedFog:

| File | Purpose | Relevance |
|------|---------|-----------|
| `EldenScaling.cs` | Enemy stat scaling by tier | **Critical** - adapt for ScalingWriter |
| `Graph.cs` | World graph data structures | Reference for DAG design |
| `GraphConnector.cs` | Zone connection algorithm | Reference only (we use different approach) |
| `AnnotationData.cs` | YAML parsing for zone data | Reference for zone data structures |
| `EventConfig.cs` | EMEVD event templates | **Critical** - adapt for FogGateWriter |
| `GameDataWriterE.cs` | Mod file generation | **Critical** - main writer logic |
| `Randomizer.cs` | Main randomization orchestration | Reference for overall flow |
| `Util.cs` | Utility functions | Some may be useful |

## fogrando-data/

Data files that define Elden Ring zones and events:

| File | Content | Use |
|------|---------|-----|
| `fog.txt` | Zone definitions (YAML) | Input for `convert_fogrando.py` |
| `fogevents.txt` | Event templates (YAML) | Reference for EMEVD generation |
| `foglocations.txt` | Item location mappings | May need for key items |
| `foglocations2.txt` | Enemy/area info | Reference |
| `er-common.emedf.json` | EMEVD instruction definitions | **Critical** for event writing |

## lib/

DLL dependencies for the C# writer (extracted from FogRando):

| DLL | Purpose |
|-----|---------|
| `SoulsFormats.dll` | Read/write FromSoft file formats |
| `SoulsIds.dll` | Helper library by thefifthmatt (GameEditor, ParamDictionary) |
| `YamlDotNet.dll` | YAML parsing |
| `Newtonsoft.Json.dll` | JSON parsing |
| `ZstdNet.dll` | Compression |
| `BouncyCastle.Cryptography.dll` | Encryption |

### SoulsIds Key Classes

| Class | Purpose |
|-------|---------|
| `GameEditor` | Load/save game data, param utilities (AddRow, CopyRow) |
| `ParamDictionary` | Wrapper around game params with indexer access |
| `GameSpec` | Game-specific configuration (paths, IDs) |

**Note**: For updates, download fresh DLLs from:
- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT/releases)
- [SoulsIds](https://github.com/thefifthmatt/SoulsIds)

## Usage Notes

### Studying EldenScaling.cs

The scaling system uses SpEffect parameters to modify enemy stats. Key points:
- 34 tiers in vanilla (we use 1-28)
- Multipliers for: HP, stamina, damage, defense, souls
- Creates SpEffect entries for tier transitions (fromTier -> toTier)

### Studying EventConfig.cs

Event templates define how to create EMEVD instructions for:
- Fog wall spawning
- Warp triggers
- Boss arena setup
- Cutscene handling

### Studying fog.txt

The YAML structure defines:
- `Areas:` - Zone definitions with names, maps, tags
- `Entrances:` - Fog gate definitions with positions
- `Warps:` - Teleporter definitions

Parse this with `convert_fogrando.py` to generate `zones.toml`.
