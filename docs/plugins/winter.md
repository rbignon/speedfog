# Winter Plugin

Turns the ground, walls, roofs, grass, bushes, ivy and leaves of the whole
game (base game and Shadow of the Erdtree) white, for a seasonal window.
Opt-in via config:

```toml
[plugin.winter]
enabled = true
```

The plugin has no parameter and no FogModWrapper side: everything it ships
is a static mod built once at bootstrap (`data/mods/speedfog-winter/`) and
copied into the seed by `speedfog/packaging.py`. Design: [the winter plugin
spec](../superpowers/specs/2026-11-10-winter-plugin-design.md). Research
base (levers, pitfalls, coverage numbers): [winter-rendering.md](../winter-rendering.md).

## What it does

- Rewrites the game's two material binders so that about 7000 materials use
  the vanilla snow shaders: upward-facing surfaces (floors, roofs, stair
  treads, pebbles) turn white, walls stay bare. Terrain materials without a
  snow shader have their albedo layers pointed at the vanilla snow ground.
  No new texture is needed for this part.
- Ships 270 frosted texture packs (135 stems, each with its `_l` twin):
  leaf clumps, ivy, vines, leaf litter, grass atlases of the DLC and the
  SpeedTree shrubs turn white while keeping their cut-out shape, and the
  catacomb and Shadow Keep elevators and levers (`AEG027_016/002`,
  `AEG427_002/023`) get a frosted look, since their `_Skin_PaintDecal`
  shaders have no snow twin to convert to. The base-game wooden elevator
  cabins (`AEG027_001` and its siblings, Limgrave Tunnels for example) get
  the same treatment for another reason: their `M[AMSN]` matbins have a
  twin but stay vanilla in the conversion because the cabin meshes are
  skinned, so their shared texture `AET003_501` is frosted (it also sits
  under the snow layer of the converted Academy pieces, as intended). The
  Shadow Keep textures get the heavy frost (`heavy_tpfs`): with the normal
  one, their dark planks read as pale wood under the shaft's warm, dim light
  (checked in game 2026-11-12, after a flat magenta albedo proved the pack
  is loaded for that DLC asset); the catacomb ones read fine with the normal
  frost and keep it.
- Replaces Torrent with the Frozen Reindeer mount: three authored binders
  (`chr/c8002.chrbnd.dcx`, `chr/c8002_h.texbnd.dcx`, `chr/c8000.anibnd.dcx`)
  shipped verbatim from the overlay folder `data/mods-src/speedfog-winter/`
  (see "How it is built"). Since game 1.17 the default Torrent body is
  `c8002` (RideParam 80020; `c8000` only holds the skeleton and the
  animations), so the skin shows for everyone, without an event flag or the
  Tarnished Pack. A pack owner who picks a regalia at a grace gets that
  regalia instead. The skin is a 1.17.2 port, made for SpeedFog, of the
  Frozen Reindeer Mount (Nexus mod 2691, the Dark Souls II reindeer): the
  reindeer meshes are added to the vanilla `c8002` FLVER, and the `c8000`
  animation set is re-exported with frost particle events (InvokeFFX) on 51
  animations and a replaced double-jump burst.
- Does not make it snow. Snowfall comes from the weather plugin (below).

Not covered: trees (SpeedTree trunks and canopies outside the shrub atlases),
decorative shaders (flags, wax, crystals, magma, water, skins, decals) and
the remaining foliage families listed under "Deferred". Skinned meshes keep
their vanilla look (see "How it is built").

## Config

`[plugin.winter] enabled = true` is the whole plugin. Python passes the table
through to `graph.json` like any plugin (see [README.md](README.md)); the
only consumer is `packaging.py`, which copies the winter folder into the seed
and registers it in the ModEngine 2 config. Mod order, first wins:
`speedfog`, `speedfog-halloween`, `speedfog-winter`, `fogmod`, `itemrando`.
No other mod ships `material/` or `asset/aet/`, so the position does not
change what renders; it is fixed for determinism.

**Weather companion.** For snowfall, enable the weather plugin next to it:

```toml
[plugin.weather]
enabled = true
weather = "snow"
freeze_time = false
```

`freeze_time = false` keeps the run's 23:45 start and a running clock (see
[weather.md](weather.md)). The winter plugin never reads or sets the weather;
the two are independent, so a seed may have the white ground under a
cloudless sky (the Sunbathing pools do exactly that) or snow without the
white ground.

If `[plugin.winter]` is enabled and `data/mods/speedfog-winter/` is missing
or incomplete (either material binder absent, or no file under
`asset/aet/`), `package_seed` raises `PackagingError` naming what is missing
and "run tools/bootstrap.py (without --no-winter)", before copying anything.
This differs from the Halloween overlay, which is silently omitted: a winter
seed without its files, or with part of them, is a broken seed.

## Racing activation

On the racing side (`../speedfog-racing/tools/pools/_base.toml`) the season
is four lines, added on December 1 and removed on February 1 by hand:

```toml
[plugin.winter]
enabled = true

[plugin.weather]
enabled = true
weather = "snow"
freeze_time = false
```

Pools inherit by deep merge (`generate_pool.py`), table by table and key by
key, so a pool with its own `[plugin.weather]` keeps it: Sunbathing keeps
`cloudless` at noon on white ground, Halloween keeps its midnight fog, and
every `training_*` pool inherits from its race pool.

## How it is built

`tools/bootstrap.py` runs `StaticModBuilder --winter-dir
data/mods/speedfog-winter/` (under Wine on Linux) and prints one line before
it, since the step runs silently for about 5 minutes. `WinterModBuilder`
first deletes the folder, its report and any `speedfog-winter.building/`
left by an interrupted run, then runs the steps in order into
`speedfog-winter.building/`, writes the report and only then moves the
staging folder into place: `data/mods/speedfog-winter/` is either a complete
build or absent, never a partial one. StaticModBuilder refuses a
`--winter-dir` that is, or contains, its game, mod, Halloween or data
directory. The classes are in `writer/StaticModBuilder/Winter/`.

1. **Catalogues.** `WinterCatalogLoader` reads
   `data/plugins/winter_materials.toml` (snow texture paths, the three
   template matbins and their modal snow parameters, 35 conversion rules,
   the retarget table, the terrain selectors and keep list) into a
   `WinterCatalog`, and `data/plugins/winter_textures.toml` (the 135 TPF
   stems in two lists, `tpfs` and `heavy_tpfs`, one frost strength each).
   Retarget targets are verified to exist before anything is built.
2. **Layout scan.** `MaterialLayoutScanner` reads every mapbnd and geombnd
   of the game (`FlverMaterialTable` reads the FLVER material table and the
   vertex layouts, `MatbinNames` resolves material paths to matbin names)
   and collects into `MaterialLayouts`, per matbin, what the vertex layouts
   of its meshes provide: whether a skinned mesh (bone indices and weights)
   draws it, and the UV sets every mesh drawing it provides (TEXCOORD0
   upwards, the smallest count over those meshes). A layout member binds the
   vertex shader input of its semantic and index, and a mesh whose layout
   lacks an input the bound shader reads is not drawn; the snow shaders have
   no skinned permutation, so a skinned mesh using a converted matbin is not
   drawn either (this is how the Chapel's wooden bridge disappeared in the
   spike, and how the Shaded Castle rocks disappeared under the two-UV twin,
   see `docs/winter-rendering.md`). The scan must be complete: a
   binder that cannot be read fails the build. The fast table reader is
   checked against `FLVER2.Read` on about 200 binders spread over the whole
   list (geombnds and mapbnds alike); a disagreement starts the scan over
   with `FLVER2.Read` for every binder, since the binders already read relied
   on the table reader. The slowest step.
3. **Material binders.** `WinterMaterialBuilder` rewrites
   `material/allmaterial.matbinbnd.dcx` and
   `material/allmaterial_dlc02.matbinbnd.dcx`, only in `Map_*` folders,
   in four passes: (1) load the three template matbins; (2) texture
   retargets, binder-wide (Limgrave ground textures to the snow ground,
   grey LOD macro to the flat white `AET099_986`, grass atlases to their
   snow twins; `MatbinRetargeter`); (3) conversion to the snow twins by
   exact shader name, only when the sampler layout matches the rule, the
   matbin is not skinned and its meshes provide the UV sets the twin reads
   (`uv_sets` of the template; `MatbinConverter`); (4) terrain albedo retarget on
   the matbins pass 3 did not convert. Untouched matbins stay byte-identical.
4. **Textures.** `WinterTextureBuilder` rewrites each listed TPF and its `_l`
   twin under `asset/aet/<family>/`, same names and compression.
   `DdsImage` parses and builds the DDS headers, `FrostEncoder` applies the
   frost. Resolution policy, uniform for every texture of every TPF:
   half of the vanilla resolution when the vanilla long side is at least
   128 px, otherwise unchanged. Albedos (`_a`) are decoded, frosted
   (`lum = 0.3 R + 0.59 G + 0.11 B`; normal frost, the `tpfs` list:
   `v = 0.62 + 0.38 lum`, colour `(v - 0.02, v, v + 0.03)`; heavy frost, the
   `heavy_tpfs` list: `v = 0.78 + 0.22 lum`, colour `(v - 0.03, v, v + 0.05)`;
   alpha kept; `FrostStrength`) and re-encoded as BC1 with 1-bit alpha by
   BCnEncoder (single-threaded: parallel BC encodes crash randomly under
   Wine). The normal frost keeps the texture's grain (a black pixel lands on a
   mid grey, 158); the heavy one is meant for textures whose vanilla is dark
   and whose room light is warm, where the normal frost reads as pale wood
   rather than snow (its look is still owed an in-game check, see below). A
   stem listed twice, in one list or across both, fails the catalogue load.
   Normals, masks and emissive maps are never re-encoded: the top mip is
   dropped and the header rewritten.
5. **Overlay.** `WinterOverlay` copies every file under the data directory's
   `mods-src/speedfog-winter/` into the build at the same relative path,
   verbatim, dot-prefixed files and folders skipped: today the three Torrent
   binders under `chr/`. The folder is optional and not tracked yet: a
   generating host gets it by hand (copy `data/mods-src/speedfog-winter/`
   next to the catalogues before the bootstrap). Without it the bootstrap
   prints a warning before the step and the build prints `Note: no winter
   overlay at ...` and ships no skin, which packaging cannot tell apart from
   a build with it. An overlay path the build already wrote (a file, or a
   folder such as `material/` or `asset/`) fails the build.
6. **Report.** `WinterReport` writes `data/mods/speedfog-winter.report.txt`
   (not shipped; packaging copies only the folder): the scan summary
   (binders, FLVERs, skinned matbins, matbins with fewer UV sets than the
   most demanding twin reads, reader used), the skinned and UV-set exclusions
   by matbin name and shader, per binder the counts of converted,
   texture-retargeted, terrain-retargeted and left-vanilla matbins with the
   conversions per rule, per TPF stem the counts of frosted (with the frost
   strength), reduced and untouched textures plus a total, the overlay files
   with their sizes, and any warning. It does not list the converted or
   retargeted matbins by name, nor texture sizes.

Skinned exclusions: the scan finds 283 skinned materials in the whole game;
38 of them would otherwise have been converted and stay vanilla (no snow).
Among them are the Chapel of Anticipation's plank materials (`m10_00_200`,
used by `AEG210_336/376`) that the research spike had converted: those
objects were invisible during the spike's validation and now come back, in
their vanilla look.

UV-set exclusions: the two-layer twin `M[AMSN_V][Mb2][Ov_N][Snow]` reads
TEXCOORD1 (`uv_sets = 2` in the catalogue), so a matbin that some mesh draws
with one UV set is never sent to it. The catalogue routes the two source
shaders whose own vertex shaders read one UV set (`[Mb2][1mask][Ov_N]`,
`[Mb3][3Mask_T][Ov_N2_T]`, the Shaded Castle rocks among them) to the plain
twin, so this exclusion only catches matbins whose meshes provide fewer UV
sets than their vanilla shader reads: 11 in the real game (7 in the base
binder, 4 in the DLC one), each drawn somewhere by a mesh with a single
`Short2` UV member: the Mountaintops rocks `AEG004_086/087` (snow-gated by
vertex mask in vanilla, drawn with one UV set by the far-LOD overworld
tiles `m60_xx_yy_01/02`), `AEG100_036_ID001`, and map pieces of Leyndell,
Ainsel, Stormveil and the DLC (`m11_00_209/315`, `m12_00_204`,
`m18_00_216`, `m61_00_289/356`, `m61_05_017/115`). Background in
`docs/winter-rendering.md`, third exclusion.

Failures: on a hard error (a binder or template matbin missing or not
matching its declared layout, a listed TPF or retarget target missing, an
unsupported DDS format, a failed post-condition, an unreadable FLVER binder,
an overlay file colliding with a built path) `WinterModBuilder` prints
`WINTER FAILED: <reason>` and leaves no winter folder, staging folder or
report. The rest of StaticModBuilder and of the
bootstrap still run (the snapshot refresh must never be skipped because of a
texture); the bootstrap echoes the `WINTER FAILED` line (and no other stderr
line of the Wine run), reports "Winter mod not built" after the
StaticModBuilder step and repeats it as its last line. Packaging a winter
seed then raises with the message pointing at the bootstrap. Any other
exception is a bug, not an input problem: the same cleanup runs, then the
exception propagates, StaticModBuilder exits non-zero and the bootstrap
fails with its stderr.

`python tools/bootstrap.py ... --no-winter` skips the step on a machine that
will never generate winter seeds; it is the longest step of the bootstrap.

## Size and time

Measured on the real game (1.17, a local game copy, Wine):

| Item | Figure |
|---|---|
| Material binders | 2.58 MB (base) and 0.53 MB (DLC); vanilla 2.75 MB and 0.61 MB |
| Matbins | 7053 conversions, 886 retargets, 38 skinned exclusions, 11 UV-set exclusions, 0 layout skips |
| Textures | 270 TPFs, 20.25 MB |
| Overlay (Torrent skin) | 3 binders, 6.63 MB |
| Folder shipped per seed | 30 MB (29.99 MB); 23 MB (23.36 MB) without the overlay |
| Scan | 226 s over 24 987 binders, 283 skinned materials, 4841 matbins with fewer than 2 UV sets |
| Winter step | 240 to 339 s inside the bootstrap step over three runs (scan about 226 s) |
| Whole bootstrap | 472 s |

Per-seed generation time is unchanged. The seed zip grows by about the
folder size.

## Validation status

Offline verification against the research spike's validated artifacts is
done. Binder differences, all classified: the 34 matbins newly excluded by
the full scan (vanilla in production, converted in the spike), 95 flat-white
macro directory spellings, 2 beach grass atlases left vanilla, 21 binder-wide
retargets outside the spike's six folders, 120 parameter-order differences
and 57 extra `group_N` parameters the spike kept, and 222 serialization-only
differences (same content re-serialized). One bucket was not expected: 10
far-terrain matbins (`m10_00_694`, `m10_00_695`, `Field_01_LOD_13`,
`Field_01_LOD_14`, `Field_01_test`, `m60_fort_076`, `m61_00_902`,
`m61_09_087`, `m61_09_088`, `m61_09_137`) whose macro layer is the flat white
`AET099_986_a` where the spike had the tiled snow ground `AET000_081_a`
(pass order: the binder-wide retarget runs before the terrain pass).
Textures: normals, masks and emissive maps byte-identical to the reference
(with the `_l` reduction); every albedo is BC1; the frosted albedos decoded
against the frost formula applied to the decoded vanilla level of the same
size have a minimum PSNR of 32.5 dB (median 40.1 dB); alpha coverage at level
0 equals vanilla. Known caveat: the generated lower mips are thinner in alpha
than vanilla (mean 2.75 coverage points, worst 11.7; the spike's pack showed
1.72 on average), so distant foliage may look slightly thinner than vanilla.
A coverage-preserving mip generation is the follow-up if it shows.

**In-game pass owed to Roger** (one pass, full restart, since matbins are
read at map load):

- Chapel of Anticipation: floors, grass, vines, leaf litter, the wooden
  bridge and the plank objects (now present, without snow).
- Limgrave: ground, engine grass, bushes.
- Stormveil Castle, Farum Azula roofs, one DLC field, Romina's arena.
- Shadow Keep elevator cabin and levers under the heavy frost (`heavy_tpfs`):
  snow rather than pale wood, grain still readable; the catacomb elevator and
  lever, on the normal frost, unchanged.
- A base-game wooden elevator cabin (`AEG027_001`, Limgrave Tunnels
  `m32_01`): frosted like the lever beside it (`AET003_501`, normal frost).
- The ten far-terrain pieces listed above, whose far layer is flat white
  rather than tiled snow.
- Torrent: whistle, idle, gallop, double jump; the reindeer model with its
  frost particles. In the Tarnished pool a regalia picked at a grace must
  still show the vanilla skin.
- Snowfall, on a seed generated with `[plugin.weather] weather = "snow"`,
  `freeze_time = false`. Whether `snow` renders precipitation in Limgrave has
  never been observed; it is independent of this plugin's code.

## Revert path

Disable the plugin (seed-level: no folder copied, no ModEngine entry), or
delete `data/mods/speedfog-winter/` and its report and bootstrap again with
`--no-winter`. Nothing else in the pipeline depends on it.

## Deferred

Measured in design Appendix E; none built.

- Remaining foliage families (`_Grass`/`_Grass2` with their own atlases,
  `[Translucency]`, `_Vesitation`, `_Vesitation_Emissive_Glow`, `[Flower_*]`):
  about 123 MB at half resolution for all of them (308 MB of vanilla TPFs).
- Trees (about 100 SpeedTree models, 42 546 placements): about 30 MB at half
  resolution, kept out because fog-randomizer runs barely cross the
  overworld. The shrubs and the trees sharing their atlases are already
  frosted; the most placed trees (`AEG801_250/253/255`) are not.
- MSB vegetation model swaps (research lever 5): dropped. FogMod ships only
  285 MSBs per seed, and a static set of swapped MSBs would collide with the
  mod order.

## Pointers

- Design: `docs/superpowers/specs/2026-11-10-winter-plugin-design.md`
  (decisions, appendices A to E: conversion rules, templates, textures,
  retarget table, measurements).
- Research: [winter-rendering.md](../winter-rendering.md).
- Weather: [weather.md](weather.md). Patch-day handling:
  [../game-patch-migration.md](../game-patch-migration.md).
- Data: `data/plugins/winter_materials.toml`, `data/plugins/winter_textures.toml`.
- Overlay: `data/mods-src/speedfog-winter/` (not tracked yet; `chr/` holds
  the three Torrent skin binders, 6.63 MB). Patch-day note on its frozen
  `c8000.anibnd.dcx`: [../game-patch-migration.md](../game-patch-migration.md).
