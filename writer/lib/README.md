# C# Dependencies

DLL dependencies for the SpeedFog C# writer (extracted from FogRando).

## Required DLLs

| DLL | Purpose |
|-----|---------|
| `FogMod.dll` | FogRando mod generator (used by FogModWrapper) |
| `SoulsFormats.dll` | Read/write FromSoft file formats |
| `SoulsIds.dll` | Helper library by thefifthmatt (GameEditor, ParamDictionary) |
| `YamlDotNet.dll` | YAML parsing |
| `Newtonsoft.Json.dll` | JSON parsing |
| `ZstdNet.dll` | Compression |
| `BouncyCastle.Cryptography.dll` | Encryption |
| `DrSwizzler.dll` | DDS texture swizzling |

## SoulsIds Key Classes

| Class | Purpose |
|-------|---------|
| `GameEditor` | Load/save game data, param utilities (AddRow, CopyRow) |
| `ParamDictionary` | Wrapper around game params with indexer access |
| `GameSpec` | Game-specific configuration (paths, IDs) |

## Updating DLLs

For updates, download fresh DLLs from:
- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT/releases)
- [SoulsIds](https://github.com/thefifthmatt/SoulsIds)

## Note

These DLLs are not committed to git (in .gitignore). Copy them manually from a FogRando installation or download from the links above.
