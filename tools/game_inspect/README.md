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

### `list-collisions`: list collision parts in one MSB

```bash
wine publish/win-x64/game_inspect.exe list-collisions <msb-file> [--torrent-only]
```

Prints every `Part.Collision` with its `DisableTorrent` flag, `PlayRegionID`
(the raw ID from the MSB, which keys into `PlayRegionParam` rows, e.g. to see
which collisions are gated by `pcPositionSaveLimitEventFlagId`), position, and
entity ID. `--torrent-only` restricts output to collisions where Torrent is
disabled.

### `check-emevd`: scan event 0 for FogMod entity references

```bash
wine publish/win-x64/game_inspect.exe check-emevd <emevd-file> [entity_id]
```

Opens an EMEVD file, finds event ID 0 (the startup event, the only event inspected by this mode), and walks every instruction in it. For each instruction with at least 4 bytes of argument data, it reads the first 4 bytes as a `uint32`. If `entity_id` is provided, only instructions whose first 4 bytes equal that ID are reported. If omitted, any first-4-byte value in the `755895000-755895999` range (FogMod's startup-event entity allocation) is reported. Used as a quick sanity check when adding new entities: "did FogMod actually wire them up in the startup event?" Note the scan is deliberately dumb about opcode semantics, so it can produce false positives if an opcode's first argument is not an entity ID.

### EMEVD under Wine: `dump-event`, `find-int`

```bash
wine publish/win-x64/game_inspect.exe dump-event <emevd> <event-id>
wine publish/win-x64/game_inspect.exe find-int <emevd> <int32>
```

`dump-event` prints every instruction of one event as `bank[id]` with the
argument bytes decoded as int32s and as hex, then the parameter table in
the `X{offset}_{size} -> [instruction] byte N` notation of `fogevents.txt`. `find-int` lists every 4-byte
aligned argument slot, in every instruction of every event, that holds the
value (accepted as int32 or uint32, compared as a bit pattern): flag checks,
entity references, initializer arguments. `dump_emevd_warps` does the same
with opcode names and runs natively on FogMod output (DFLT-compressed DCX),
but the game's own EMEVDs are KRAK-compressed and need Oodle, which only
exists as a Windows DLL; these two work through Wine like the rest of this
tool, so they are the way to read vanilla files on Linux.

### Params and text: `dump-param`, `dump-fmg`

```bash
# One row, every field (or the fields whose name contains a --field substring)
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> NpcParam --row 44200120 \
  --defs ../../writer/FogModWrapper/eldendata/Defs --field hp
# Rows by ID prefix, or every row with --all; --field appends the matching cells
# inline on each line (pipeable, this feeds speedfog-racing's generate_weapons.py)
wine publish/win-x64/game_inspect.exe dump-param <regulation.bin> EquipParamWeapon --all \
  --field wepType --defs ../../writer/FogModWrapper/eldendata/Defs
# Every FMG entry of a msgbnd (fmg name, ID, text), optionally filtered by substring
wine publish/win-x64/game_inspect.exe dump-fmg <game>/msg/engus/item_dlc02.msgbnd.dcx "Blessing"
```

`dump-param` decrypts `regulation.bin` and applies the paramdef XML named
after the param (`--def-name` when the basename differs, e.g. `SpEffect` for
`SpEffectParam`). A `--field` that matches no field of the paramdef is an
error, so a typo cannot produce a well-formed but empty dump. `--row` cannot
be combined with `--prefix` or `--all`. Output is UTF-8 when redirected.

### Game patch triage: `check-params`, `diff-param`, `diff-msb`, `diff-emevd`, `bnd-list`

Written for the Elden Ring 1.17 update (see `docs/game-patch-migration.md`).

```bash
# Apply the bundled Defs to every param, SoulsIds-style; exit code 2 on any failure.
wine publish/win-x64/game_inspect.exe check-params <regulation.bin> \
  --defs ../../writer/FogModWrapper/eldendata/Defs [--reference <old-regulation.bin>] [--all]

# Rows added/removed and cells changed per param between two regulation.bin
wine publish/win-x64/game_inspect.exe diff-param <old-regulation.bin> <new-regulation.bin> \
  --defs ../../writer/FogModWrapper/eldendata/Defs [--param RideParam]... [--rows-only]

# Compare one map across two game versions
wine publish/win-x64/game_inspect.exe diff-msb <old.msb.dcx> <new.msb.dcx>
wine publish/win-x64/game_inspect.exe diff-emevd <old.emevd.dcx> <new.emevd.dcx>

# Identify DCX files an archive unpacker could not name
wine publish/win-x64/game_inspect.exe bnd-list <game>/_unknown/*
```

`check-params` replays the predicate SoulsIds uses when it loads params for
FogMod (`ParamType` equality and `DetectedSize == GetRowSize(ulong.MaxValue)`,
`ParamDictionary.ApplyParamdefCarefully`) over every `.param` in the
regulation, and prints the ones no def applies to. With `--reference` it also
lists params whose row count or paramdef data version differs between the two
files. `--all` prints every param. Do not replace it with SoulsFormats' own
`PARAM.ApplyParamdefCarefully`, which uses a different row size and reports
false mismatches.

`diff-param` applies the Defs the same way and, per param, lists rows added
or removed by ID and, for common rows, every cell whose value changed
(`field: old -> new`). `--param` restricts it, `--rows-only` skips the cell
comparison. A param that no def applies to is compared by row ID and row
name only. This is how the Elden Ring 1.17 Torrent rows (`RideParam`
80020-80050) were found.

`diff-msb` first compares the decompressed bytes (a re-compressed but identical
file is reported as such), then lists parts, regions and events added or
removed by name, and common parts/regions whose model, entity ID, position or
rotation changed. `diff-emevd` does the same for events by ID, comparing the
instruction stream (bank, ID, argument bytes) of common events. Both print a
"no differences" line when the bytes differ but nothing they look at does.

`bnd-list` decompresses each file and prints the inner magic and size, plus
the entry names of BND3, BND4 and TPF containers.

## How it works

Dispatch lives in `Program.cs`:

```
game_inspect dump-entity ...   → DumpEntity        (Program.cs)
game_inspect find-model ...    → FindModel.Run     (FindModel.cs)
game_inspect compare ...       → CompareAssets.Run (CompareAssets.cs)
game_inspect check-emevd ...   → CheckEmevd.Run    (CheckEmevd.cs)
game_inspect dump-param ...    → DumpParam.Run     (DumpParam.cs)
game_inspect dump-fmg ...      → DumpFmg.Run       (DumpFmg.cs)
game_inspect check-params ...  → CheckParams.Run   (CheckParams.cs)
game_inspect diff-param ...    → DiffParam.Run     (DiffParam.cs)
game_inspect dump-event ...    → DumpEvent.RunDump (DumpEvent.cs)
game_inspect find-int ...      → DumpEvent.RunFind (DumpEvent.cs)
game_inspect diff-msb ...      → DiffMsb.Run       (DiffMsb.cs)
game_inspect diff-emevd ...    → DiffEmevd.Run     (DiffEmevd.cs)
game_inspect bnd-list ...      → BndList.Run       (BndList.cs)
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
