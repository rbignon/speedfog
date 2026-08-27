# Game Patch Migration

**Status:** Active (Elden Ring 1.17, Tarnished Pack, 2026-08-28)

Working reference for keeping SpeedFog playable across an Elden Ring update
when Fog Gate Randomizer (FogRando) is not updated yet. It records what the
pipeline actually depends on, the ordered tasks to run when a patch lands,
and the decisions taken for the current instance. Tick the checkboxes as the
work progresses and keep the decision log at the bottom.

## Current instance: 1.17

- Patch: App Ver. 1.17, Regulation Ver. 1.17, mandatory for online play.
  Steam release 2026-08-28 00:00 CEST.
  [Patch notes](https://en.bandainamcoent.eu/elden-ring/news/elden-ring-patch-notes-version-117).
- Content: two starting classes (Idus Knight, Heavy Knight), new weapons
  "purchased from merchants or found in the Lands Between", three Spectral
  Steed Attires found in Stormveil Castle, Liurnia of the Lakes and Altus
  Plateau, a Site of Grace menu entry to change Torrent's appearance, NPC
  invasion events in Caelid and Leyndell Royal Capital (the latter disabled
  once Leyndell is the Ashen Capital), weapon/spell/skill balance changes.
  No save wipe ("Items that were already found will not be changed").
- thefifthmatt's stated timeline: Item and Enemy Randomizer updated within
  a day or two; FogRando "may take much longer", possibly not before it
  merges into the unified randomizer (no WinForms, sources published).
- Baseline before the patch: `eldenring.exe` dated 2025-09-15,
  `regulation.bin` md5 `56ec3b35bc9412aac9838f399f2f8afd`, FogRando v0.2.3,
  Item Randomizer v0.12alpha1, ModEngine 2.1.0, SoulsFormatsNEXT submodule
  at `d0caa7a`.

## What the pipeline depends on

Verified 2026-08-26 in `reference/fogrando-src/` and in decompiled
`SoulsIds.dll` / `SoulsFormats.dll` (`ilspycmd`, see the recipe below).

1. **FogMod never reads the installed game while writing.**
   `GameDataWriterE.cs:29-31` sets `GameDir = eldendata\Vanilla` (falling
   back to `..\randomizer\diste\Vanilla` when absent) and loads MSB, EMEVD,
   `regulation.bin`, `msg/engus/*_dlc02.msgbnd.dcx` and talk ESDs from that
   bundled snapshot (about 1700 files: 1655 plus 56 under `msg/`, in
   `writer/FogModWrapper/eldendata/`). The only external lookup is
   `MergedMods.Resolve`, which is our `--merge-dir`: files produced by the
   Item Randomizer take precedence over the snapshot. The Item Randomizer is
   snapshot-based as well (`diste/Vanilla/regulation.bin`, RandomizerCommon
   `GameData.cs:1048`), so a patched `game_dir` alone never leaks 1.17 params
   into a seed. Consequence: a game patch does not break FogMod's own reads.
   Seeds keep shipping pre-patch files, and the engine receives them. What
   can still break generation is listed in points 3 and 5.
2. **A seed overrides these files in the game**, all derived from the
   snapshot: `regulation.bin`, `event/{map}.emevd.dcx` plus `common` and
   `common_func`, `msg/engus/item_dlc02.msgbnd.dcx` and
   `menu_dlc02.msgbnd.dcx`, `script/talk/{map}.talkesdbnd.dcx`,
   `map/mapstudio/{map}.msb.dcx` (`GameDataWriterE.cs:4981-5194`).
3. **Paramdef mismatch is silent, then fatal.** SoulsIds
   `ParamDictionary.ApplyParamdefCarefully` skips a def whose row size does
   not match the file; SoulsFormats `PARAM.Write` then throws "Params cannot
   be written without applying a paramdef". One changed param layout anywhere
   in `regulation.bin` stops generation until `eldendata/Defs` (194 Paramdex
   XML files) is refreshed. FogMod itself edits 13 params: BonfireWarpParam,
   SpEffectParam, ShopLineupParam, ActionButtonParam, WorldMapPointParam,
   NpcParam, ItemLotParam_map, GameAreaParam, EquipParamGoods,
   EquipMtrlSetParam, AssetEnvironmentGeometryParam, PlayRegionParam,
   MapDefaultInfoParam.
4. **Libraries can be rebuilt.** FogRando's `SoulsFormats.dll` is
   SoulsFormatsNEXT commit `83dfdd9` (assembly `SoulsFormats 1.0.0.0`,
   netstandard2.1), an ancestor of the `SoulsFormats/` submodule. SoulsIds is
   public (`github.com/thefifthmatt/SoulsIds`). Both can be rebuilt and
   dropped into `writer/lib/` under the closed `FogMod.dll`, as long as the
   members FogMod calls keep their signatures. Remember the existing split:
   FogRando's SoulsIds and the Item Randomizer's are not interchangeable
   (`writer/lib/itemrando/`).
5. **Our injectors read a few files from the installed game** with the old
   SoulsFormats: `ChapelGraceInjector` and `DeathMarkerInjector` (vanilla
   EMEVD), `RunCompleteInjector` (msg, all languages) and `SummerTheme` (msg,
   `engus` and `frafr`), `GraceTalkEsd` (talkesdbnd fallback),
   `TorrentArenaPatcher` and `SpiritspringRemover` (game MSB via
   `MsbHelper.FindMsbPath` when the mod dir has none for that map), and
   `PlayRegionPatcher`, which opens the game's `regulation.bin` with the
   bundled Defs (`RegulationEditor.Open(gameDir)`) and copies
   `PlayRegionParam` rows from it: with a patched game directory it imports
   1.17 rows silently, or fails if that param's layout changed. After a
   patch they all mix patched files with FogMod's pre-patch output. The
   generation host must therefore keep a game directory on the same version
   as the snapshot (`config.toml` `game_dir`).
6. **Runtime.** `modengine2_launcher.exe` accepts `-p/--game-path`
   (exe path, bypasses Steam autodetect); the seed launcher exposes it as
   `SPEEDFOG_GAME_PATH` or `game_path=` in `%APPDATA%\SpeedFog\config.ini`
   or in `<seed>\config.ini` (see `docs/save-backup.md`). The speedfog-racing DLL locates memory with
   AOB pattern scans (`mod/src/core/aob.rs`), robust to most patches but to
   be re-validated on every executable change.

## Scenarios

| Id | Trigger | Symptom | Lever |
|----|---------|---------|-------|
| S1 | Params and text change | Seeds ship pre-patch `regulation.bin`: new content absent at best, crash at boot if the exe expects missing fields or tables | Refresh Defs, `Vanilla/regulation.bin`, `Vanilla/msg` |
| S2 | Maps and events change too | Touched maps overwritten with pre-patch versions (content missing, no crash expected); refreshing them exposes `fog.txt`/`fogevents.txt` to moved entities | Hash-diff, refresh map by map, validate in-game |
| S3 | Binary format change | Old SoulsFormats cannot read patched files, or the engine refuses old files | Rebuild SoulsFormatsNEXT and SoulsIds, swap DLLs |
| S4 | Executable-level | ModEngine 2 hooks or racing AOBs fail | Wait for ModEngine 2, evaluate me3, re-scan patterns |
| S5 | FogRando merges into the unified randomizer | `ConnectionInjector`, `MapSplitsInjector`, `OpenSplitInjector`, `HelperAreaResolver` need porting; post-processing injectors are independent | Port once the API is visible |
| S6 | Fallback: recompile FogMod from decompiled source | Internal tooling only, thefifthmatt's closed code | Ask him first |

For 1.17 the patch notes put us in S1 plus S2 on at least Stormveil,
Liurnia, Altus, Caelid and Leyndell. S3 has no signal in the notes.

## Tasks

### Phase 0: before the patch

- [x] Launcher override for a frozen game copy (commit `877d53a`).
- [ ] Run `backups/resolve_game_path.ps1` once on a Windows machine and
      test the three edge cases: an empty value (`game_path=""`), a
      commented key (`# game_path=`), a path containing `=`.
- [ ] Freeze a full copy of `Game/` on every machine that generates seeds
      (speedfog-racing's `tools/generate_pool.py` host included). Point
      `config.toml` `game_dir` at the frozen copy.
- [ ] Announce to players (the README section exists, the announcement
      does not): block automatic updates, or copy `Game/` and configure the
      launcher; the save file is shared between copies until the
      compatibility test below says otherwise.

### Phase 1: patch-day triage

Run in order; each step decides the next.

- [ ] Copy the patched game to a second directory, keep the frozen copy
      untouched. Note the executable date and version.
- [ ] Extract the `eldendata/Vanilla` file list plus `msg/*` from the
      patched game (WitchyBND under Wine, or SoulsIds `BhdExtractor`) and
      hash-diff against the snapshot. Expected for 1.17: `regulation.bin`,
      msg, the MSB and EMEVD of Stormveil (m10_00), Liurnia, Altus, Caelid
      and Leyndell (m11_00) tiles, `common.emevd`, and
      `m00_00_00_00.talkesdbnd` (grace menu). Anything else is a surprise.
- [ ] Read one changed file of each type with the old SoulsFormats.
      `tools/game_inspect` links `writer/lib/SoulsFormats.dll` and already
      has `dump-param`, `find-model` and `check-emevd` for that. Failure
      means S3.
- [ ] Launch an existing pre-patch seed on the patched executable through
      ModEngine. Outcomes: plays (buy time), boots without the new content
      (fine for racing), crashes at boot (S1 hard path), fails before the
      title screen (S4).
- [ ] Save compatibility: copy the current save aside, start the patched
      game once with it (vanilla, no mods), then load that save on the
      frozen copy. Loads: the shared-save concern is closed for this patch.
      Does not load: see "Save handling" below.
- [ ] speedfog-racing: re-validate the AOB patterns, IGT fix and overlay on
      the patched executable (independent of the steps above).

### Phase 2: move the snapshot to the new version

- [ ] Wait for Paramdex defs for 1.17, refresh `eldendata/Defs`.
- [ ] Replace `Vanilla/regulation.bin` and `Vanilla/msg` with the 1.17
      files. Keep MSB, EMEVD and ESD on 1.16 unless a reason below applies.
      Trap: `eldendata/` is gitignored and `tools/bootstrap.py:286-294`
      re-copies it wholesale from the FogRando zip, so any re-bootstrap (on
      the racing pool host too) silently reverts Defs and Vanilla to 1.16.
      Until bootstrap learns to re-apply the refresh, re-run the refresh
      tooling after every bootstrap and check the `regulation.bin` md5
      before generating.
- [ ] Regenerate a known seed and diff it against the pre-patch output
      (exclude `regulation.bin` from byte diffs, it is never reproducible).
      Then play it on the patched executable.
- [ ] `ShopInjector` clears ShopLineupParam 101800 to 101800+n before
      adding its rows. The log prints the vanilla IDs in 101800-101999: if
      1.17 placed new purchasable weapons there, move `BASE_SHOP_ID` or
      skip occupied IDs before shipping.
- [ ] Decide per map whether to refresh MSB/EMEVD from 1.17:
  - Torrent attire pickups (Stormveil, Liurnia, Altus) and new weapons in
    the world: harmless either way, absent from seeds if not refreshed.
  - Leyndell invasion: conditioned on the Ashen Capital state (flag 300).
    In a SpeedFog run flag 300 stays OFF until the Erdtree warp
    (`ErdtreeWarpPatcher` sets it just before that warp; `AlternateFlagPatcher`
    only clears 330 at startup, see `docs/alternate-warp-patching.md`), so
    refreshing `m11_00` EMEVD would make the invader appear in runs.
    Default: do not refresh, this is a gameplay change to decide explicitly.
  - Caelid invasion: same default.
  - If a map is refreshed, re-check the fog gates of that map in-game
    (`fog.txt` entity IDs, `fogevents.txt` templates).
- [ ] Grace menu ESD (`m00_00_00_00.talkesdbnd`): 1.17 adds a Torrent
      attire entry. Keeping the 1.16 ESD hides it (acceptable). If refreshed,
      re-validate `RebirthInjector` and `ShadowRealmBlessingRemover`
      (ConsistentID allocation, matched menu entries, `docs/esd-editing.md`).
- [ ] Only then bump the Item Randomizer to the 1.17 release and re-run
      with `--merge-dir`. Never bump it alone: with `--merge-dir`, FogMod
      reads the randomizer's `regulation.bin` with its own Defs, and a
      pre-patch Defs set crashes on the first changed param layout.
- [ ] In-game validation list: Chapel grace and spawn, a fog gate warp in
      each refreshed map, boss trigger lock, run-complete banner, rebirth
      menu, care package delivery, racing overlay zone tracking.

### Phase 3: only if formats changed (S3)

- [ ] Build the `SoulsFormats/` submodule at a commit with 1.17 support,
      drop it in `writer/lib/SoulsFormats.dll`, run the FogModWrapper tests
      and a generation. `MissingMethodException` means an API drift on a
      member FogMod calls: shim it or go to Phase 4.
- [ ] Rebuild SoulsIds from source against that SoulsFormats.

### Phase 4: fallbacks

- FogMod recompile (S6): the whole assembly decompiles cleanly with
  ILSpy 11 at C# 5 target; about 620 display-class identifiers in
  `GameDataWriterE` to rename before it compiles. Conditions: FogRando still
  not updated after a few weeks, and thefifthmatt asked. Seeds are generated
  on our side so a rebuilt DLL never reaches players.
- FogRando merge (S5): port the injectors that reach into FogMod's `Graph`
  and `AnnotationData` (the S5 list; `OpenSplitInjector` and
  `MapSplitsInjector` run before `Graph.Construct`, `ConnectionInjector` and
  `HelperAreaResolver` after) to the new API when it is published.

## Save handling

A frozen copy and the Steam install share
`%APPDATA%\EldenRing\<id>\ER0000.sl2`.

- Stage 1 (done): launcher override plus the instruction that alternating
  players own their save hygiene; the backup daemon and `recovery.bat` are
  the safety net.
- Stage 2 (conditional, about a day): `eldenring_alt_saves.dll` as opt-in.
  It only changes the save file extension (`altsaves.toml` next to it) and
  is loaded through `external_dlls`; it is already proven on the pre-patch
  executable, which is exactly what a frozen copy runs. Work: emit a second
  ModEngine config with the DLL from `packaging.py`, pick it in the launcher
  when a game path is configured, make `launch_helper.ps1` look for the
  alternate extension, seed the alternate file with a one-shot copy of
  `ER0000.sl2`. Check the DLL's redistribution licence first. Trigger: the
  save test fails and FogRando is still not updated after a few weeks.
- Not planned: swapping save files around the launch (fragile on crash);
  me3 (only if ModEngine 2 itself breaks, S4; it was tried and reverted in
  commit `3b6958d`).

## Tooling to write

- `tools/diff_vanilla_snapshot.py`: given a game directory, extract the
  files listed in `eldendata/Vanilla` plus `msg/*`, hash them against the
  snapshot, print the changed list. This is Phase 1 step 2 and the input of
  every Phase 2 decision.
- `tools/refresh_paramdefs.py`: fetch or copy a Paramdex checkout into
  `eldendata/Defs`, reporting defs whose row size changed.
- Both act on gitignored, bootstrap-managed directories: make them idempotent,
  print what they replaced, and either hook them into `tools/bootstrap.py`
  or document that they must be re-run after every bootstrap.

## Clean decompile recipe

The global `ilspycmd` (10.1.1) and ILSpy 11 with the default language target
both crash on `GameDataWriterE.Write`, which is why `reference/fogrando-src`
carries `CS$<>` closure artefacts. The C# 5 target avoids the crash:

```bash
dotnet tool install ilspycmd --tool-path "$SCRATCH/tools" --version 11.0.0.9375
"$SCRATCH/tools/ilspycmd" writer/lib/FogMod.dll  -p -lv CSharp5 --disable-updatecheck -o "$SCRATCH/fogmod"
"$SCRATCH/tools/ilspycmd" writer/lib/SoulsIds.dll -p -o "$SCRATCH/soulsids"
```

## Decision log

- 2026-08-26: analysis written; data refresh (S1) chosen as the primary
  route, FogMod recompile kept as a fallback only.
- 2026-08-27: launcher game path override shipped (`877d53a`). Save
  separation deferred to a conditional stage 2 (alt-saves opt-in), me3
  rejected for this purpose.
- 2026-08-27: patch notes read. Content patch, no format signal; S2 on
  Stormveil, Liurnia, Altus, Caelid, Leyndell; grace menu ESD changes;
  `ShopInjector` range to check after the regulation refresh. Default for
  the two NPC invasions: keep the 1.16 EMEVD.
