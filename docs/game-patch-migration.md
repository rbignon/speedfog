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
- Baseline before the patch: `eldenring.exe` dated 2025-09-15
  (FileVersion 2.6.1.0), `regulation.bin` md5
  `56ec3b35bc9412aac9838f399f2f8afd` (2036272 bytes, identical to the
  snapshot's), FogRando v0.2.3, Item Randomizer v0.12alpha1, ModEngine 2.1.0,
  SoulsFormatsNEXT submodule at `d0caa7a`.
- Patched game (received 2026-08-27, one day early): `eldenring.exe` dated
  2026-08-27 (FileVersion 2.7.0.0), `regulation.bin` md5
  `f27fb24bb28c9c6f7f0e784aba2baa9e` (2045728 bytes). On the generation host
  the patched install is `/data/thewall/Game` and the frozen 1.16.2 copy is
  `/data/thewall/Game.1.16.2`, both unpacked to loose files; `config.toml`
  points at the frozen copy. Triage results are in "1.17 triage results"
  below.

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
- [x] Freeze a full copy of `Game/` on every machine that generates seeds
      (speedfog-racing's `tools/generate_pool.py` host included). Point
      `config.toml` `game_dir` at the frozen copy. Done on the generation
      host (`/data/thewall/Game.1.16.2`, `config.toml` repointed
      2026-08-27); the racing pool host still has to be checked.
- [ ] Announce to players (the README section exists, the announcement
      does not): block automatic updates, or copy `Game/` and configure the
      launcher; the save file is shared between copies until the
      compatibility test below says otherwise.

### Phase 1: patch-day triage

Run in order; each step decides the next.

- [x] Copy the patched game to a second directory, keep the frozen copy
      untouched. Note the executable date and version. (2.7.0.0, 2026-08-27.)
- [x] Extract the `eldendata/Vanilla` file list plus `msg/*` from the
      patched game and hash-diff against the snapshot:
      `python tools/diff_vanilla_snapshot.py <patched> --reference <frozen>`
      on unpacked game directories. Expected for 1.17: `regulation.bin`,
      msg, the MSB and EMEVD of Stormveil (m10_00), Liurnia, Altus, Caelid
      and Leyndell (m11_00) tiles, `common.emevd`, and
      `m00_00_00_00.talkesdbnd` (grace menu). Anything else is a surprise.
      (Result: 204 changed, 0 missing, see below.)
- [x] Read one changed file of each type with the old SoulsFormats.
      `tools/game_inspect` links `writer/lib/SoulsFormats.dll`:
      `check-params` (every param against the bundled Defs, SoulsIds
      semantics), `diff-msb`, `diff-emevd`, `dump-fmg`, `dump-esd`. Failure
      means S3. (Result: everything reads, all 194 Defs apply to 1.17.)
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

Precondition, not a technical one: a seed ships one `regulation.bin`, and it
must match the executable of every player who runs it. Once the snapshot
moves to 1.17, players on a frozen 1.16 copy get a 1.17 regulation on a 1.16
executable (untested, at best missing rows referenced by the new params).
Moving the snapshot is therefore a community switch (everyone updates, the
frozen-copy instruction is withdrawn), not a rolling one. Until that call,
seeds stay on 1.16 and the frozen copy stays the supported setup.

- [ ] Refresh `eldendata/Defs` only if `check-params` reports a mismatch
      (for 1.17 it reports none: no param layout changed, the 1.16 Defs apply
      to every param, so Paramdex is not on the critical path).
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

## Tooling

- `tools/diff_vanilla_snapshot.py <game> [--reference <frozen>]` (written
  2026-08-27): hashes every `eldendata/Vanilla` file against its counterpart
  in an unpacked game directory and lists changed and missing files. With
  `--reference` the diff becomes three-way: a file is a patch change when
  the game copy differs from the frozen copy, and a stale snapshot file when
  both game copies agree but the snapshot differs (the frozen copy is the
  ground truth, the snapshot is not). This also covers the injectors that
  read from the installed game instead of the snapshot (`RunCompleteInjector`
  reads `menu_dlc02` of every language, `SummerTheme` reads `item` and
  `item_dlc02`). Bundles the snapshot does not carry (`item_dlc01`,
  `menu_dlc01`, `ngword`, `araae`) are listed separately, compared to the
  frozen copy only. Needs loose files (UXM, Nuxe or similar): the FogRando
  snapshot itself is the baseline, no archive extraction here.
- `tools/game_inspect` (Wine): `check-params <regulation.bin> --defs <Defs>
  [--reference <regulation.bin>] [--all]` replays SoulsIds' def application
  over every param (exit code 2 on any failure) and lists row-count changes
  (`--all` lists every param, not only problems and differences);
  `diff-msb` / `diff-emevd` compare one map across two versions; `bnd-list`
  names the files an unpacker left in `_unknown/`.
- Still to write when needed: a snapshot refresh step (copy chosen files
  from a game directory into `eldendata/Vanilla`, idempotent, printing what
  it replaced) and a Paramdex refresh. Both act on gitignored,
  bootstrap-managed directories: hook them into `tools/bootstrap.py` or
  document that they must be re-run after every bootstrap.

## 1.17 triage results

Measured 2026-08-27 with the tooling above, patched install against the
frozen 1.16.2 copy and the FogRando snapshot.

- **Snapshot diff**: 204 of 1711 snapshot files changed, none missing.
  `regulation.bin`; `common.emevd` and `common_func.emevd`;
  `m00_00_00_00.talkesdbnd` (grace menu); Stormveil `m10_00` and Leyndell
  `m11_00` MSB+EMEVD; 133 overworld tiles (MSB, plus the EMEVD of
  `m60_34_50`, `m60_38_41`, `m60_44_52`, `m60_47_42`, `m60_50_40`,
  `m60_51_36`, `m60_52_39`); all four msg bundles (`item`, `menu`,
  `item_dlc02`, `menu_dlc02`) in all 14 languages. Every one of the 204 is a
  real patch change (game differs from the frozen 1.16.2 copy). The base
  `item`/`menu` bundles were in addition already stale in the snapshot
  (FogMod only reads and writes the `_dlc02` ones), which is harmless for
  FogMod but matters for `SummerTheme`: it reads `item.msgbnd` from the
  installed game, so a host generating against a patched install feeds it
  1.17 text. Outside the snapshot, `item_dlc01`/`menu_dlc01` changed in
  every language, and `araae` (absent from the snapshot) changed too.
- **Params**: no layout change. `check-params` applies all 194 bundled Defs
  to the 1.17 regulation exactly as on 1.16.2 (0 problems on both). 26 params
  gained rows: `EquipParamWeapon` +82, `CharaInitParam` +33, `SpEffectParam`
  +29, `ItemLotParam_map` +28, `ShopLineupParam` +19, `EquipParamProtector`
  +18, `EquipParamGoods` +3, `NpcParam` +6, `ActionButtonParam` +1,
  `EquipMtrlSetParam` +4, `BaseChrSelectMenuParam` +2 (the two classes),
  and others. `ShopLineupParam` gained row 101896 inside the Twin Maiden Husks
  range; `ShopInjector` only clears 101800-101817 (8 + 9 + 1 rows), so no
  collision and no need to move `BASE_SHOP_ID`.
- **Formats**: the old SoulsFormats reads the 1.17 regulation, MSB, EMEVD,
  msgbnd and talkesdbnd without error. S3 is off the table for 1.17.
- **New files**: the 1.17 archives contain 73 files the unpacker's
  dictionary did not know; `bnd-list` identifies all of them as part
  bundles (weapon `WP_A_*`, armor `BD/HD/LG/AM_M_*` models, textures,
  animations). No new map, event, param or text file.
- **common.emevd**: new event 780 (2 instructions) initialised from event 0
  (434 -> 435 instructions). **common_func.emevd**: new event 900005590
  (12 instructions). Neither touches the FogMod templates.
- **Stormveil `m10_00`**: one new asset `AEG099_630_9006` (entity 10001256,
  the Spectral Steed Attire pickup) with its Treasure and ObjAct MSB events
  (named "Patch1.17" in the MSB); EMEVD event 0 gained one instruction. No
  entity moved.
- **Leyndell `m11_00`**: one new enemy part `c0000_9060` (entity 11000180)
  and three "Patch1.17" regions (11002180-11002182), the NPC invasion; EMEVD
  gained event 11002930 and five instructions in event 0. No entity moved.
- **Caelid invasion** (`m60_52_39_00`, Dragonbarrow): EMEVD 832 -> 24112
  bytes with ten new events (0, 200, 1052392910, 1252392200-1252392695);
  MSB gains enemy `c0000_9020` (entity 1052390180) and five NPC regions
  (1052392180-1052392182). The level-2 tile `m60_13_09_02` gains enemy
  `c0000_9010` (entity 1052390200) and ten `AEG099_090` assets
  (1052391500-1052391572).
- **Redmane Castle `m60_51_36_00`**: one new enemy `c0000_9013` (entity
  1051360740) with EMEVD event 1051360740 (32 instructions), most likely the
  Knight Leontiel summon (`menu_dlc02` 9560, 80900-80902).
- **Grace menu ESD** (`t000001000`, machine 2147483616): 46 -> 49 states,
  one new branch on menu result 75 (Torrent appearance, texts
  20010070-20010079 and 20011070-20011078 in `menu_dlc02`). Every other
  branch keeps its target, only the state numbers were renumbered.
- **Text** (`engus`): `menu_dlc02` +30 entries (Torrent appearance menu,
  Knight Leontiel summon texts, class names Idus Knight / Heavy Knight,
  "Tarnished Pack" system message); `item_dlc02` +262 entries (new
  equipment names and descriptions) and 39 reworded DLC weapon captions
  (backhand blades, great katanas, light greatswords). No entry removed.
- **Treasure pickups** (one asset plus a Treasure MSB event named
  "Patch1.17", one or two init instructions in EMEVD event 0): `m60_34_50`
  (`AEG099_630_9001`, 1034501601), `m60_38_41` (`AEG099_600_9002`,
  1038411682), `m60_44_52` (`AEG099_395_1000` + `AEG099_990_9010`,
  1044521620/1044521610), `m60_47_42` (`AEG099_620_9000`, 1047421680),
  `m60_50_40` (`AEG099_600_9000`, 1050401680), plus the Stormveil one above.
- **The other 125 changed MSBs**: identical decompressed size, and
  `diff-msb` finds no part, region or event added, removed, renamed,
  re-modelled or moved. Byte-level field changes only (not identified, no
  fog gate or warp region affected). Refreshing them buys nothing; keeping
  the 1.16 versions costs nothing.

Consequence for Phase 2: the S2 exposure is six pickups, two invasions and
one summon NPC, all additive. No `fog.txt` entity moved, so refreshing any
map is a content decision, not a compatibility one. The one input that
changes silently on a host generating against a patched install is the
text `SummerTheme` and `RunCompleteInjector` read from the game directory,
which is why `config.toml` `game_dir` must stay on the frozen copy.

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
- 2026-08-27: patch received and triaged (see "1.17 triage results"). S1
  is the soft path: no param layout change, the bundled Defs apply, formats
  read. `ShopInjector` range clear. Snapshot stays on 1.16 until the
  community switches versions; the frozen copy is the supported setup
  meanwhile. Remaining Phase 1 steps need a Windows machine (pre-patch seed
  on the 1.17 executable, save round-trip) and the speedfog-racing repo
  (AOB re-validation).
