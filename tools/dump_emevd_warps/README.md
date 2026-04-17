# dump_emevd_warps

A .NET CLI tool for inspecting compiled Elden Ring EMEVD files (`*.emevd.dcx`). Used during SpeedFog development to debug fog gate warps, event flags, event initialization, and MSB assets in the mod output.

## Purpose

EMEVD files are the compiled event scripts that drive gameplay logic (warps, flag toggles, spawns, cutscenes, etc.). They are binary and impossible to read with standard tools. This utility decodes the opcodes that matter for SpeedFog and answers questions like:

- Where does a given fog gate warp the player?
- Which events set or check flag `1050292000`?
- Which caller initializes event `1040290310`, and with what parameters?
- Which vanilla warp instructions targeted `leyndell_erdtree` before we patched them?
- Which MSB assets exist at a given entity ID, and what are their properties?

It uses the old `SoulsFormats.dll` bundled in `writer/lib/` (same one used by FogModWrapper) so the view matches what FogMod produces.

## When to use it

Reach for this tool whenever something goes wrong in the generated mod and the cause is in compiled EMEVD. Typical symptoms:

- **Warp destination is wrong** (e.g., player ends up in the wrong map variant). Dump the warp instructions, check for `AlternateFlag` patterns (two `WarpPlayer` calls in the same event with different map variants, gated by `SkipIfEventFlag`). See `docs/alternate-warp-patching.md`.
- **A flag has unexpected state**. Run `search --flag <id>` to list every setter and checker.
- **An event does not trigger**. Run `init --event <id>` to find who initializes it and inspect the parameters passed.
- **A FogMod-generated event behaves unexpectedly**. Entity IDs >= `755890000` and event IDs in the `1040290xxx` range are FogMod-generated; the rest is vanilla.
- **An MSB asset is missing or has wrong properties** (e.g., fog gate not visible, boss trigger firing wrong). Use `asset` to dump all properties, `objacts` to inspect ObjActs.

For any non-EMEVD investigation (SFX bundles, enemy params, asset model comparison across maps), use `tools/game_inspect/` instead.

## Installation / Build

```bash
cd tools/dump_emevd_warps
dotnet build
```

Targets .NET 8.0. References `writer/lib/SoulsFormats.dll` and bundles `oo2core_6_win64.dll` + `libzstd.dll` for DCX decompression. No Wine needed on Linux (runs natively on .NET).

## Usage

All invocations go through `dotnet run --` from the tool directory, or the built binary under `bin/Debug/net8.0/`.

### `dump`: inspect events and warps

```bash
# Dump every event containing a WarpPlayer or CutsceneWarp instruction, across all EMEVDs
dotnet run -- dump output/mods/fogmod/event/

# Filter by map
dotnet run -- dump output/mods/fogmod/event/ --map-filter m10_01

# Dump every instruction of a specific event (regardless of warp presence)
dotnet run -- dump output/mods/fogmod/event/ --event 1040290310

# Dump everything everywhere (noisy)
dotnet run -- dump output/mods/fogmod/event/ --event all
```

Each instruction is printed as:

```
[042] 2003:014 WarpPlayer(m11_05_00_00, region=755891234)  (0B-05-00-00-12-34-56-78)  P[src=4→tgt=4,len=4]
```

- `[042]` = instruction index within the event
- `2003:014` = `Bank:ID` (opcode) of the raw instruction
- `WarpPlayer(...)` = human-readable decoding (only for opcodes we care about; unknown ones stay empty)
- `(0B-05-...)` = hex dump of the raw argument bytes
- `P[src=4→tgt=4,len=4]` = marks an argument slot that is overridden by the caller via `InitializeEvent` (parameterized event)

### `search`: find all references to a flag

```bash
dotnet run -- search output/mods/fogmod/event/ --flag 330
```

Scans every instruction in every EMEVD for references to the flag. It handles:

- Direct setters: `SetEventFlag`, `SetNetworkConnectedEventFlag`, `BatchSetEventFlags`, `BatchSetNetworkEventFlags`.
- Checkers: `IfEventFlag`, `IfBatchEventFlags`, `WaitForEventFlag`, `SkipIfEventFlag`, `EndIfEventFlag`, `GotoIfEventFlag`.
- Brute-force scan: for opcodes we do not decode, the tool scans the raw argument bytes at 4-byte aligned offsets and reports any `int32` that equals the flag ID, prefixed with `BRUTE[...]`. To avoid duplicates, instructions already handled by the specialized decoders are excluded from the brute-force pass.

This is how we found e.g. that FogMod sets flag `300` via `Event 915` in vanilla, which we then patched out.

### `init`: trace event initialization

```bash
dotnet run -- init output/mods/fogmod/event/ --event 1040290310
```

Finds every `InitializeEvent` (2000:0) and `InitializeCommonEvent` (2000:6) call that targets the given event ID. Prints the calling event, instruction index, slot number, and the parameter values passed. Essential when an event has parameterized slots and you need to know what concrete values the caller supplied (these show up as `entity_id=0` in a raw dump because they come from the caller, not the event body).

### `asset`: dump MSB asset properties by entity ID or name

```bash
dotnet run -- asset <msb-dir> --entity 755899900 755895000 [--map-filter m10_01]
dotnet run -- asset <msb-dir> --name H012345
dotnet run -- asset <msb-dir> --first --typeinfo
```

Dumps every property (via reflection) of the matching assets: `ModelName`, `Position`, `Rotation`, `DrawGroups`, `DisplayGroups`, `CollisionMask`, plus all nested struct scalars. Useful when you need to copy properties between assets (e.g., bloodstain visuals borrowed from a working vanilla asset, see `docs/death-markers.md`).

Flag gloss:
- `--entity ID1 ID2 ...`: match one or more entity IDs (multi-value).
- `--name NAME1 NAME2 ...`: match by MSB part name.
- `--first`: dump the first asset in every MSB (useful for quick sampling).
- `--typeinfo`: print the full `MSBE.Part.Asset` type hierarchy (public properties, fields, nested struct members) of the first asset in the first MSB. Used to explore what SoulsFormats exposes before deciding which fields matter.

### `objacts`: list assets and ObjActs in an MSB

```bash
dotnet run -- objacts <msb-dir> [--map-filter m10_01]
```

Lists every Asset entry (name, entity ID, model, position) and every ObjAct event (part name, flag ID, entity ID, ObjAct ID, position). ObjActs are the action trigger records that tie a `SetEventFlag` to a player interaction (e.g., rest at a Grace).

## How it works

Single-file dispatch in `Program.cs`. EMEVD and MSB files are loaded via `EMEVD.Read` / `MSBE.Read` from `SoulsFormats`, with DCX decompression handled by the native libs copied next to the binary.

1. **Mode dispatch**:
   - `DoDump` iterates events, optionally filtering. For warp-only mode, it first scans for `WarpPlayer` (2003:14) or `CutsceneWarp` (2002:11/12), then prints all instructions of matching events so you see the surrounding flag logic.
   - `DoSearch` walks every instruction. For the handful of flag-touching opcodes we know, it reads the flag ID at the expected offset. For the rest, it runs a 4-byte aligned scan and reports matches. `IsDecodedFlagInstruction` prevents the brute-force pass from re-reporting the same hits.
   - `DoInit` looks for opcodes `2000:0` (InitializeEvent) and `2000:6` (InitializeCommonEvent) and dumps the parameter tail.
   - `asset` / `objacts` use `MSBE` directly and dump Part.Asset / Events.ObjActs fields.
2. **Decoder**: `Decode(Instruction)` is a large switch over `(Bank, ID)` pairs. Each case reads bytes from `ArgData` at fixed offsets and formats them. The decoder covers roughly 50 opcodes relevant to SpeedFog: condition system (`0:*`), wait (`1001:*`), flow control (`1000:*`, `1003:*`), event init (`2000:*`), warps (`2002:*`, `2003:14`), flag setters (`2003:22,66,69`), item/asset/display/sound actions, and more. Unknown opcodes fall through and are printed with only their hex bytes.
3. **Parameter annotations**: when an instruction's argument bytes are overridden by the caller (via the event's `Parameters` table), the dumper annotates the line with `P[src=...→tgt=...,len=...]` so you know which bytes are dynamic.

### Byte-level knowledge

The tool hard-codes the argument layout for each opcode (e.g., `SetEventFlag (2003:66)`: `FlagType(b@0) FlagID(u32@4) State(b@8)`). These layouts come from the community-maintained EMEDF definitions and are sanity-checked against our `data/er-common.emedf.json`. When adding a new opcode, follow the existing pattern: add a case in `Decode` reading from the right offsets, and if the opcode references event flags or event IDs, also handle it in `DoSearch` / `DoInit` plus register it in `IsDecodedFlagInstruction` to suppress brute-force duplicates.

## Related docs

- `docs/fogmod-emevd-model.md`: how FogMod lays out its events and entity IDs
- `docs/alternate-warp-patching.md`: AlternateFlag warps and how this tool helps diagnose them
- `docs/event-flags.md`: flag range allocation
- `docs/death-markers.md`: asset property copying with `asset` mode
