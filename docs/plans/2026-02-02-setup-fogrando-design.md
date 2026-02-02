# Setup FogRando Dependencies

Design document for `tools/setup_fogrando.py` - a script to extract FogRando dependencies from the Nexusmods download.

## Context

FogModWrapper requires:
- DLLs in `writer/lib/` (FogMod.dll, SoulsFormats.dll, etc.)
- Game data in `writer/FogModWrapper/eldendata/`
- FogRando data files in `data/` (fog.txt, fogevents.txt, etc.)

These files cannot be distributed in the repo (Nexusmods license) and must be extracted from the FogRando download. The user must manually download the ZIP from Nexusmods (requires account).

## CLI Interface

```
python tools/setup_fogrando.py <path-to-zip> [--force]
```

**Arguments:**
- `path-to-zip` (required): Path to FogRando ZIP downloaded from Nexusmods
- `--force` (optional): Overwrite existing files

**Behavior:**
- Without `--force`: Skip if dependencies already exist
- With `--force`: Delete and re-extract everything

**Exit codes:**
- 0: Success (or already installed without --force)
- 1: Error (ZIP not found, sfextract missing, extraction failed)

## Dependencies

- `sfextract` (dotnet tool) - Extracts DLLs from .NET single-file executables
- Install: `dotnet tool install -g sfextract`

## Execution Flow

```
1. Check prerequisites
   ├── ZIP file exists?
   ├── sfextract installed?
   └── Files already present? (without --force: message and exit 0)

2. Extract to temp directory
   ├── unzip → temp/fog/FogMod.exe + temp/fog/eldendata/
   └── sfextract temp/fog/FogMod.exe → temp/extracted/

3. Copy to destinations
   ├── DLLs: temp/extracted/*.dll → writer/lib/
   ├── eldendata: temp/fog/eldendata/ → writer/FogModWrapper/eldendata/
   └── FogRando data: temp/fog/eldendata/Base/{fog.txt, fogevents.txt,
       foglocations2.txt, er-common.emedf.json} → data/

4. Regenerate derived data
   ├── python tools/generate_clusters.py → data/clusters.json
   └── python tools/extract_fog_data.py → data/fog_data.json

5. Cleanup
   └── Delete temp directory
```

## Files Copied

**DLLs → `writer/lib/`:**
- FogMod.dll, SoulsFormats.dll, SoulsIds.dll
- BouncyCastle.Cryptography.dll, Newtonsoft.Json.dll, YamlDotNet.dll
- ZstdNet.dll, DrSwizzler.dll
- libzstd.dll (from zip root)

**Data → `writer/FogModWrapper/eldendata/`:**
- Base/, Defs/, Graphviz/, ModEngine/, Names/, Vanilla/

**Data → `data/`:**
- fog.txt, fogevents.txt, foglocations2.txt, er-common.emedf.json

## Output Messages

**Success:**
```
$ python tools/setup_fogrando.py ~/Downloads/FogRando-v0.2.3.zip

[1/5] Checking prerequisites...
      ✓ sfextract found
      ✓ ZIP file valid

[2/5] Extracting FogMod.exe...
      ✓ Extracted 14 DLLs

[3/5] Copying files...
      → writer/lib/ (8 DLLs)
      → writer/FogModWrapper/eldendata/
      → data/ (4 files)

[4/5] Regenerating derived data...
      → clusters.json (255 clusters)
      → fog_data.json (487 fog gates)

[5/5] Cleanup...
      ✓ Done

Setup complete! You can now build FogModWrapper:
  cd writer/FogModWrapper && dotnet build
```

**Already installed:**
```
FogRando dependencies already installed.
Use --force to reinstall.
```

**Missing sfextract:**
```
[1/5] Checking prerequisites...
      ✗ sfextract not found

Install it with: dotnet tool install -g sfextract
```

## Git Changes

**`.gitignore` additions:**
```gitignore
# FogRando data files (extracted by setup_fogrando.py)
data/fog.txt
data/fogevents.txt
data/foglocations2.txt
data/er-common.emedf.json
data/clusters.json
data/fog_data.json
```

**Documentation updates:**
- CLAUDE.md: Update commands section
- data/README.md: Clarify which files are extracted vs config

## Future Work

- Make cluster IDs deterministic (based on zone names) so regeneration produces stable output
