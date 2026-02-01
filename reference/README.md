# Reference Files

This directory contains reference materials extracted from FogRando for **studying the implementation**. Do not modify these files directly.

**Note**: Production data files have been moved to `data/` and DLLs to `writer/lib/`. This directory only contains source code and reference data for study.

## Directory Structure

```
reference/
├── fogrando-src/      # Decompiled C# source from FogRando
└── fogrando-data/     # Reference-only data files (fogevents.txt, foglocations.txt)
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

Reference-only data files (production files are in `data/`):

| File | Content | Use |
|------|---------|-----|
| `fogevents.txt` | Event templates (YAML) | Reference for EMEVD generation |
| `foglocations.txt` | Item location mappings | May need for key items |

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

### Studying fogevents.txt

Reference for EMEVD event patterns. SpeedFog uses `data/speedfog-events.yaml` for its own event templates.
