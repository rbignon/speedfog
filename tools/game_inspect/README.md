# game_inspect

A .NET CLI tool for inspecting Elden Ring game data: SFX bundles (`*.ffxbnd.dcx`), MSB entities (`*.msb.dcx`), and compiled EMEVD files (`*.emevd.dcx`). Used during SpeedFog development to answer specific questions about vanilla game data that we need to understand or replicate.

## Purpose

Where `dump_emevd_warps` is focused on understanding the EMEVD logic we generate (warps, flags, event init), `game_inspect` is aimed at **vanilla game data discovery**: find an SFX, compare two assets to copy properties between them, locate an entity across all maps, or search model occurrences within an MSB. It is an exploration tool, not a debug tool for our mod output.

## When to use it

- **Finding an SFX**: when injecting bloodstain visuals, smoke effects, or Grace VFX, we need the numeric SFX ID and the bundle that contains it. `list-sfx` and `list-sfx --search` answer that in one call.
- **Locating a vanilla entity**: given an entity ID from decompiled FogRando data or from a wiki, find the exact map and coordinates. `dump-entity` walks every MSB in a directory and prints the match.
- **Copying properties between assets**: when making an asset behave like another (e.g., a fog gate visual borrowed from a vanilla blood puddle), `compare` diffs every property of two assets and shows which fields differ.
- **Scanning a map for a model**: when adding new assets of a known model (e.g., all `AEG099_090` grace bonfires in a map), `find-model` lists them with position and entity ID.
- **Checking FogMod entity allocation**: `check-emevd` scans the startup event (ID 0) for instructions referencing the `755895xxx` entity range used by FogMod, confirming FogMod-initialized entities exist.

For debugging our generated EMEVD (warps, flags we set, event init we emit), use `tools/dump_emevd_warps/` instead.

## Installation / Build

```bash
cd tools/game_inspect
dotnet build
```

On Linux, running the built binary through `dotnet run` works for most subcommands, but SFX bundle and MSB reads trigger Oodle decompression, which only has a Windows-native DLL shipped by the game. **Use Wine for any path that decompresses DCX**:

```bash
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64
wine publish/win-x64/game_inspect.exe <args...>
```

On Windows, run the binary directly without Wine.

Targets .NET 8.0. References `writer/lib/SoulsFormats.dll` and bundles `oo2core_6_win64.dll` + `libzstd.dll`.

## Usage

Subcommand is the first argument. When no subcommand matches, the tool falls through to the SFX listing mode (default).

### `list-sfx` (default): list or search SFX IDs in `.ffxbnd.dcx` bundles

```bash
# List all SFX in every bundle in a directory
wine publish/win-x64/game_inspect.exe /path/to/Game/sfx/

# Restrict to a specific bundle
wine publish/win-x64/game_inspect.exe /path/to/Game/sfx/ --bundle commoneffects

# Filter by ID range
wine publish/win-x64/game_inspect.exe /path/to/Game/sfx/ --bundle commoneffects --range 800000-810000

# Find a specific SFX ID (tells you which bundle contains it)
wine publish/win-x64/game_inspect.exe /path/to/Game/sfx/ --search 42
```

Each bundle is a BND4 archive containing files named `fXXXXXXXX.ffx`. The tool parses the numeric suffix and sorts the results. The `--search` mode is the shortcut we use most when adding a new SFX in EMEVD.

### `dump-entity`: find an MSB part by entity ID across all maps

```bash
wine publish/win-x64/game_inspect.exe dump-entity <msb-dir> <entity-id>
```

Iterates every `*.msb.dcx` under the directory and walks all Part entries via `msb.Parts.GetEntries()`. For each match it prints name, type, position, rotation, scale, and model. Extra type-specific fields are printed only for `Asset` (AssetSfxParamRelativeID), `Enemy` (NPCParamID, ThinkParamID, TalkID, CharaInitID), and `DummyEnemy` (NPCParamID, ThinkParamID). Other Part subtypes (players, collisions, etc.) still match but only the common fields are shown.

### `find-model`: list all assets matching a model in one MSB

```bash
wine publish/win-x64/game_inspect.exe find-model <msb-file> AEG099_090
```

Prints every Asset whose `ModelName` equals the argument, with entity ID and position. Typically used to enumerate vanilla anchor points for a given visual (graces, fog gates, bonfires, etc.).

### `compare`: diff all properties of two assets in the same MSB

```bash
wine publish/win-x64/game_inspect.exe compare <msb-file> <eid1> <eid2>
```

Loads the MSB, finds both assets by entity ID, and walks every public property of `MSBE.Part.Asset` via reflection. For each property that differs between the two, prints a line `DIFF <name>: <value1> vs <value2>`. This is how we figured out which fields to override when cloning a vanilla asset's visual behavior, which DrawGroups mask to copy, which SfxParam to use, and which nested struct fields matter.

### `check-emevd`: scan event 0 for FogMod entity references

```bash
wine publish/win-x64/game_inspect.exe check-emevd <emevd-file> [entity_id]
```

Opens an EMEVD file, finds event ID 0 (the startup event, the only event inspected by this mode), and walks every instruction in it. For each instruction with at least 4 bytes of argument data, it reads the first 4 bytes as a `uint32`. If `entity_id` is provided, only instructions whose first 4 bytes equal that ID are reported. If omitted, any first-4-byte value in the `755895000-755895999` range (FogMod's startup-event entity allocation) is reported. Used as a quick sanity check when adding new entities: "did FogMod actually wire them up in the startup event?" Note the scan is deliberately dumb about opcode semantics, so it can produce false positives if an opcode's first argument is not an entity ID.

## How it works

Dispatch lives in `Program.cs`:

```
game_inspect dump-entity ...   → DumpEntity        (Program.cs)
game_inspect find-model ...    → FindModel.Run     (FindModel.cs)
game_inspect compare ...       → CompareAssets.Run (CompareAssets.cs)
game_inspect check-emevd ...   → CheckEmevd.Run    (CheckEmevd.cs)
game_inspect <sfx-path> ...    → ListSfx           (Program.cs, fallback)
```

Each subcommand is a small free-standing routine that loads one file (or iterates a directory of files) via `SoulsFormats` and prints to stdout. There is no persistent state and no shared infrastructure beyond the single DLL reference.

Key design points:

1. **Reflection over hand-coded layouts**. `compare` uses `type.GetProperties(...)` on `MSBE.Part.Asset` so any property exposed by SoulsFormats is compared, including fields we did not know existed. `FormatValue` handles common types (arrays, `Vector3`, null).
2. **Oodle dependency**. `list-sfx` and any MSB-loading subcommand unpack `.dcx` archives, which require Oodle (`oo2core_6_win64.dll`). The DLL is copied to the output by the csproj, but it is a Windows native library, which is why Wine is the portable path on Linux.
3. **FogMod entity range hard-coded in `check-emevd`**. The `755895000-755895999` range corresponds to FogMod's allocation for SpeedFog-generated entities in startup events. If the allocation scheme changes, update `CheckEmevd.cs`.
4. **Loose argument parsing**. The command-line parser is minimal on purpose: each subcommand takes a fixed positional structure and a couple of flags. No shared parser, no dependency injection. Keep additions this way.

## Related tools and docs

- `tools/dump_emevd_warps/`: the sibling tool for inspecting our generated EMEVD (warps, flags, event init). Use it for mod output, use this one for vanilla data discovery.
- `docs/death-markers.md`: uses `compare` to clone a vanilla bloodstain asset's DrawGroups.
- `docs/fogmod-emevd-model.md`: explains the `755895xxx` entity range scanned by `check-emevd`.
