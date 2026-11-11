# Winter Rendering Research

**Date:** 2026-11-09
**Status:** Research complete, validated in game; the winter plugin is built from it (see below)

How Elden Ring decides the color of ground, walls, grass and foliage, and which levers turn the whole game white (base game and DLC) without shipping hundreds of megabytes of textures. Everything below was established by throwaway spikes dropped into a seed's `mods/` folders and checked in game, most of it twice (once in a cumulative install, once in isolation after the cumulative installs led us astray). The spike artifacts (scripts, census files, built binders, texture packs) lived in a session scratchpad and are not part of the repository; this document is what survives. Where the text says "assumed" or "candidate", the design still has to decide.

## Design decisions (winter plugin, 2026-11-10)

The plugin built from this research ([plugins/winter.md](plugins/winter.md); design in `docs/superpowers/specs/2026-11-10-winter-plugin-design.md`) departs from the "shape assumed by the spikes" at the end of this document as follows:

- Textures ship at half of the vanilla resolution (long side at least 128 px), not at the spike's mix; quarter resolution was not needed.
- The MSB vegetation model swaps (lever 5) are not built: FogMod writes only 285 MSBs per seed, and a static set would collide with the mod order. There is no per-seed part: the plugin is a static mod only.
- Snowfall is the weather plugin (`weather = "snow"`, `freeze_time = false`), not part of the plugin.
- Trees and the remaining foliage families are kept out (measured costs in the design's Appendix E).
- The full FLVER scan excludes 38 skinned materials the spike had converted (they stay vanilla).

## Measured inventory

One source of truth for the numbers used below (game version 1.17 files, measured on 2026-11-09):

| Item | Count |
|---|---|
| Vanilla MSBs in `map/mapstudio` | 1347 |
| MSBs in FogMod's snapshot (`writer/FogModWrapper/eldendata/Vanilla`) | 1071 |
| MSBs written by FogMod into one seed (`mods/fogmod/map/mapstudio`) | 285 (129 `m60`, 62 `m61`, 94 dungeons) |
| Matbins in `allmaterial.matbinbnd.dcx` / `allmaterial_dlc02.matbinbnd.dcx` | 15 089 / 3283 |
| Matbins changed by the last spike (conversions plus terrain retargets) | 5859 / 2023 |
| Converted binders, measured output size | 2 593 728 B / 525 936 B (vanilla 2 749 136 B / 606 032 B) |
| `GrassTypeParam` rows | 501 (347 with id below 400, 8 between 400 and 999, 146 DLC rows at 1000 and above) |
| Maps whose MapPieces reference grass rows / distinct rows referenced | 636 / 324 |
| Asset placements in all vanilla MSBs / distinct asset models | 624 987 / 26 735 |
| SpeedTree shrubs (FLVER height below 3 m) / trees | 16 models, 27 661 placements / about 100 models, 42 546 placements |
| Materials sampled for the skinned-mesh check / skinned among them | 2496 / 14 |

## Data model: what a pixel's color depends on

- **MSB parts** (`map/mapstudio/mXX_YY_ZZ_WW.msb.dcx`): `MapPiece` parts reference map geometry (`map/mXX/mXX_YY_ZZ_WW/*.mapbnd.dcx`, one FLVER each), `Asset` parts reference asset geometry (`asset/aeg/aegNNN/aegNNN_MMM.geombnd.dcx`). A MapPiece also carries up to six `GrassParamId` slots (engine grass, see below). FogMod writes only the MSBs it touches into a seed (285, see the inventory); any other map has to be shipped first, which is what `VanillaMapPinner` does for `pin_vanilla_maps` in `data/game_tweaks.toml`, and 276 vanilla MSBs are not even in the snapshot.
- **FLVER materials** name a material and a path. Most paths are `N:\GR\data\Material\mtd\<Folder>\matxml\<name>.matxml`, resolved by name to a MATBIN; a few are legacy `.mtd` paths resolved against `mtd/allmaterialbnd.mtdbnd.dcx` (2519 legacy MTDs, no grass or terrain in them). SpeedTree FLVERs (`AEG800`..`AEG816`: trees, shrubs; DLC `AEG490` plants) embed their texture names directly and use `speedtree.matbinbnd.dcx`, whose matbins carry no paths: only texture replacement reaches them.
- **Material binders**: `material/allmaterial.matbinbnd.dcx` (folders `Map_Preset`, `Map_LOD`, `Map_Public`, `Map_m10_00`, ..., `Map_m60_00`, `Map_m60_fort`), `material/allmaterial_dlc02.matbinbnd.dcx` (the DLC maps; DLC-only shaders live under `SPX_DLC02\` instead of `SPX\`, for example `M[Road_Ground]` and `M[Cliff_001]`), `material/allmaterial_dlc01.matbinbnd.dcx` (characters and parts only). Binders almost never share a matbin path (none between dlc01 and base, two between dlc02 and base: `Map_m95_77/AEG997_800` and `_801`), so there is no override to worry about. The `.devpatch.dcx` files are 125 bytes and empty.
- **MATBIN** = shader path (`.spx`), source path, key, parameters, samplers. Parameters specific to a shader are prefixed with the shader name (`M_AMSN_V__Snow__snp_0_float_0`); `group_N_CommonUV-UVParam` and `g_GX_*` parameters are shared. Samplers are named `Texture2D_<index>_<role>` and their index/role set is constant per shader ("layout"). SoulsFormatsNEXT reads and writes MATBIN natively.
- **Textures**: `asset/aet/aetNNN/aetNNN_MMM.tpf.dcx` plus a low-resolution twin `aetNNN_MMM_l.tpf.dcx`, resolved by texture name (`AETNNN_MMM_a` albedo, `_n` normal, `_Billboards_a` for SpeedTree). DDS with DX10 header: albedo BC1 (dxgi 72) or BC7 (99), normals in game-specific formats. Some referenced textures have no TPF at all (dead references such as `AET000_083`, `AET066_008`); nothing renders from them in vanilla either.
- **Shader families** (suffixes compose): `M[AMSN_V]` base PBR, `M[AMSN]` metallic variant, `[Ov_N]` overlay normal, `[Ov_AN]` overlay albedo and normal, `[Ov_A_Ch2]`, `[Mb2]`/`[Mb3]`/`[Mb4]`/`[Mb5]` blend layers (`GSBlendMap` samplers), `[3Mask]`, `[Far_AN]`, `[_ColorAdjust][3Mask_ch18]`, `[1Mask]`, `_Edge` (alpha cut-out), `[Translucency]`, `_Grass2`/`_Grass`/`_Vesitation`/`[Flower_m10]` (foliage; `Vesitation` is the game's own spelling), `[Snow]` and `[Snow_VtxMask]` (snow on top, the latter gated by a vertex mask).

Vanilla snow twins available in the base binder: `M[AMSN_V][Snow]` (47), `M[AMSN_V][Ov_N][Snow]` (82), `M[AMSN_V][Mb2][Ov_N][Snow]` (21), `M[AMSN][Snow]` (14), `M[AMSN_V][Ov_N][Snow]_Edge` (8), plus `[Snow_VtxMask]` variants (222/87/77/26/14/10/5). Sampler roles of the plain twin: `Texture2D_0_AlbedoMap_0` snow layer albedo (`AET000_080_a`, the snow texture the shaders blend on top), `Texture2D_2_AlbedoMap_0` base albedo, `Texture2D_1_NormalMap_0` base normal, `Texture2D_3_NormalMap_0` snow normal (`AET001_078_n`); the blend twin keeps two `GSBlendMap` layers plus the snow layer. The snow shaders cover upward-facing surfaces: floors, roofs, pebbles, cart tops, stair treads turn white, walls stay bare. Modal parameters across vanilla twins: `float3_0 = (0, 2, 0)`, `float_0 = 0`; `float_1 = 2`, `float_4 = 0.6`, `float_9 = 0` on the blend twin. Heavier values exist in vanilla (`float3_0.y` up to 6, `float_0` down to -4) and made no visible difference when tried on Farum Azula (the roofs there were simply not converted yet).

## Vegetation: three render paths, one lever

| Kind | Where it comes from | What reaches it |
|---|---|---|
| Grass baked into map geometry | Grass2 meshes inside MapPiece FLVERs (the Chapel of Anticipation holds 67 000 such vertices, in MapPieces that are not displayed in the chapel state a run starts in) | matbin |
| Engine grass | `GrassTypeParam` rows selected per MapPiece by the MSB `Grass` slots; each row names models (`AEG001_254`, `AEG210_555`, ...) whose FLVERs use `Map_Preset/Field_XX_AEGgrass` or per-map matbins | matbin (the preset's texture), or a texture published under the vanilla name |
| Placed assets | MSB `Asset` parts: grass carpets (`AEG210_554`, 14 m, the grass actually visible at the Chapel), leaf clumps (`AEG210_550..553`), flowers, SpeedTree shrubs and trees | matbin for preset-based assets, texture replacement for SpeedTree, model swap in the MSB |

`GrassTypeParam` edits in `regulation.bin` are inert: `baseDensity = 0` on all rows, or every grass row pointed at a snow bush, changed nothing after a full restart in an isolated test. The param bytes were verified faithful (header identical, only row values differ); the game simply does not use it for what is rendered. Do not spend time there again.

## Levers that work (validated in game)

### 1. Matbin conversion to the vanilla snow twins (no new textures)

Rewrite a matbin so it uses the snow twin shader: shader path swapped, samplers rebuilt from the twin's layout (base albedo and normal copied from the source, snow albedo and normal set to the vanilla snow textures, overlay or third layers dropped, masks copied when the twin has them), parameters kept except the source-prefixed ones, the twin's modal snow parameters inserted, `group_N` parameters the twin does not know dropped. A rule is keyed by the exact source shader name and applies only when the source's sampler set is exactly the one listed for that rule (sets are constant per shader, so this is a safety check, not a heuristic):

| Source family | Twin | Note |
|---|---|---|
| `M[AMSN_V]`, `[Ov_N]`, `[Ov_AN]`, `[Ov_A_Ch2]`, metallic `M[AMSN]`, `M[AMSN][Ov_N]`, `M[AMSN][Ov_AN]`, and the one-UV-set blends `[Mb2][1mask][Ov_N]`, `[Mb3][3Mask_T][Ov_N2_T]` (first layer kept, see the third exclusion) | `M[AMSN_V][Snow]` | the plain twin is the validated one; `M[AMSN_V][Ov_N][Snow]` (overlay kept) was tried once in an early cumulative round, judged ineffective and never re-tested in isolation; the metallic twin `M[AMSN][Snow]` was never validated, so the metallic map is dropped |
| `[Mb2]`, `[Mb2][Ov_N]`, `[Mb2][Ov_AN]`, `[Ov_N][Mb2_ColorAdjust][1Mask_ch18]`, `[Mb3]`, `[Mb3][Ov_N]`, `[Mb3][Ov_N2_T]`, `[Mb3][Snow_VtxMask]`, `[Ov_N][Mb3_ColorAdjust][3Mask_ch18]` (both `M[AMSN_V]` and `M[AMSN]`), `[Ov_N][Mb3_ColorAdjust][3Mask_ch18][Snow_VtxMask_ch19]`, `[Mb4]`, `[Mb4_ColorAdjust][3Mask_ch18]` (with and without `[Ov_N]`) | `M[AMSN_V][Mb2][Ov_N][Snow]` | layers beyond the second dropped, color adjustment lost (fine under snow) |
| `_Edge`, `[Ov_N]_Edge`, `M[AMSN]_Edge`, `M[AMSN][Ov_N]_Edge` | `M[AMSN_V][Ov_N][Snow]_Edge` | overlay normal taken from the template (dummy normal) when the source has none |
| every `[Snow_VtxMask]` variant | the plain twin of the same shape | forces full snow where vanilla gates it by vertex mask (Mountaintops, DLC); the twin's snow slot takes the catalogue textures, not the source's: the base game binds the same snow there (`AET000_080`, mean luminance 230 of 255) but the DLC binds soot and dirt (`AET073_140` at 14, `AET051_105` at 24, `AET430_031` at 46), which the first catalogue forced to full coverage on about a hundred DLC matbins (the Midra arena walls and platform ledges, `m61_00_446` and `m61_00_444`, 2026-11-11) |

Three hard exclusions:

- **Never convert a matbin used by a skinned mesh** (FLVER vertex layout with `BoneIndices`/`BoneWeights`): the snow shaders have no skinned permutation and the mesh is not drawn at all (the Chapel's wooden bridge `AEG217_237`, materials `m10_00_519/520`). Only 14 of the 2496 materials sampled are skinned; the production build must scan every FLVER (mapbnd and geombnd) that references a converted matbin and keep those matbins vanilla.
- Foliage shaders (`_Grass*`, `_Vesitation`, `[Flower_m10]`, `[Translucency]`) have no snow twin; retarget or frost their textures instead (levers 2 and 4).
- **Never send a matbin to a twin whose vertex shaders read a vertex input its meshes do not provide.** The shader bundle `shader/shaderbdle.shaderbdlebnd.dcx` holds one `.shaderbdle` BND4 per shader whose `.vpo` entries are DXBC vertex shaders; their `ISGN` chunk lists the inputs. Every map shader of the rules reads POSITION0, NORMAL0, TANGENT0, COLOR1 and TEXCOORD0; the blend shaders (`[Mb2]`, `[Mb3]`, `[Mb4]`, their ColorAdjust variants and the twin `M[AMSN_V][Mb2][Ov_N][Snow]`) also read TEXCOORD1, except `[Mb2][1mask][Ov_N]` and `[Mb3][3Mask_T][Ov_N2_T]`, which blend by texture masks in UV0; `M[AMSN_V][Mb2]` alone also reads COLOR0. A FLVER layout member binds the semantic of its index (`VertexColor` 1 is COLOR1, `UV` 1 is TEXCOORD1), and a mesh whose layout lacks an input of the bound vertex shader is not drawn: the Shaded Castle rocks `AEG004_286/289/290/291` (matbin `AEG004_276`, `[Mb2][1mask][Ov_N]`, one UV set) vanished once their matbin pointed at the two-UV twin (2026-11-11; 180 one-UV meshes over 155 binders draw 19 of that rule's 22 base-binder matbins; the `[3Mask_T][Ov_N2_T]` rule has 2 such meshes, both in Leyndell). The two one-UV sources now go to the plain twin, which reads one UV set, and the build enforces the rule: the layout scan records the UV sets every mesh provides per matbin, each catalogue template declares the UV sets its twin reads (`uv_sets`), and a matbin below its twin's need stays vanilla and is listed in the report (see `docs/plugins/winter.md`). UV sets are counted by layout member index, not by component count: the signatures read TEXCOORD0 as xy and TEXCOORD1 as xyzw, the game's layouts provide TEXCOORD1 as a four-component member of index 1 (`UByte4Norm` or `Byte4`), and a four-component member of index 0 (`Short4`, which SoulsFormats decodes as two UV values) is TEXCOORD0 alone, read whole only by `[Ov_A_Ch2]`.

The spike selectors restricted the first rounds to six folders (`Map_Preset`, `Map_LOD`, `Map_m60_00`, `Map_m60_fort`, `Map_Public`, `Map_m10_00`), which is why Leyndell, Ainsel, Haligtree, the Academy, Farum Azula and the Volcano Manor stayed bare until the rules were run over every `Map_*` folder of both binders. In `Map_Preset`, only matbins named `AEG*`/`AET*` (asset presets) were converted; the `Field_*` terrain presets are handled by lever 3.

### 2. Grass atlas retarget (no new textures)

Grass materials sample a cut-out atlas; vanilla ships snow atlases with the same layout as the green ones, so pointing the albedo sampler at the snow atlas whitens the grass with correct cut-outs:

| Snow atlas | Compatible layouts |
|---|---|
| `AET005_076_a` (short snow grass) | `AET005_070`, `073`, `075`, `082`, `093`, `095`, `AET210_561`, `AET402_070` |
| `AET005_068_a` (tall snow grass) | `AET005_044`, `045`, `046`, `065`, `066`, `AET210_560`, `AET402_066`, `AET406_077` |

Layouts must be checked by eye; `AET005_085` (beach), `AET230_853/568`, `AET400_002`, `AET002_594`, `AET003_512` look similar in a thumbnail and are not compatible. The retarget also fixes the engine grass that uses these presets (Limgrave is white with the binder alone).

### 3. Terrain blend albedo retarget (no new textures)

The multi-layer terrain shaders have no snow twin, but their albedo samplers can all be pointed at the vanilla snow ground `AET000_081_a` (the full-ground snow texture of the Mountaintops presets, as opposed to `AET000_080_a`, the thin snow layer the snow shaders blend on top); far variants already use `AET099_986_a`. This paints every face, cliffs included, which read as snowy cliffs. Selector used by the last spike, applied after lever 1 so converted matbins (shader now contains `Snow`) are never touched:

- shader name matching `Road_Ground|Cliff_001|Far_AN2|cheap_C|\[Mb[345](?:_Ch2|_N)?\](?:\[[^\]]+\])*\[(3Mask|Far_AN|Far|Ov_N2|Ov_AN|Cover)\]|\[Mb[345](?:_Ch2|_N)?\]\[Far_AN\]`, plus every `Map_Preset/Field_*` matbin whatever its shader;
- minus shaders matching `Grass|Vesitation|Flower|Edge|Translucen|Snow|Crystal|Emissive|Glow|Skin|magma|Water|Lake|Decal|Sway` (case-insensitive);
- minus matbin names matching `magma|lava|lake|underwater|swamp|river|wet|water|grow|snow|Glow|Emissive|VolcanoGrass|AEGgrass|\.bak|\.mat$` (case-insensitive; the two grass terms keep ground textures off the grass atlas presets).

Precedence between levers 1 and 3 is therefore by order: a shader with an exact rule (for example `M[AMSN_V][Mb3][3Mask_T][Ov_N2_T]`) is converted; shaders without a rule that match the regex above are retargeted; the coverage figures in the inventory are the sum of both.

### 4. Frosted textures (bytes)

For foliage with no snow twin: decode the albedo, apply `v = 0.62 + 0.38 * luminance` with luminance in 0..1, `rgb = (v - 0.02, v, v + 0.03)` clamped to 0..1, keep alpha, rebuild the mip chain, re-encode BC1, republish the TPF under the vanilla name together with its `_l` twin and `_Billboards` textures. Half resolution looks fine for white foliage and divides the cost by four. Spike packs (sizes as built):

| Pack | Textures | Size |
|---|---|---|
| Chapel and Stormveil leaves, full resolution | `AET210_414`, `562`, `566`, `569`, `AET220_099` | 6.6 MB |
| DLC grass atlases, half resolution (the albedos referenced by the `_Grass`/`_Grass2` matbins of `Map_m61_00`, `Map_m61_01`, `Map_m61_09`, `Map_m20_00`) | `AET055_058`, `156`, `157`; `AET066_000`, `003`, `012`, `013`, `017`, `022`, `030`, `031`, `033`..`036`, `038`, `047`, `048`, `050`, `055`, `056`, `058`, `059`, `062`, `066`, `074`, `090`..`093`, `095`..`098`, `115`, `118`; `AET410_053`, `150`, `154`, `161`, `163`, `218`, `221`, `230`; `AET995_035`..`038` | 16 MB |
| SpeedTree shrubs, half resolution | `AET801_000`, `010`, `060`, `080`, `090`, `220`, `270`, `480`, `510`, `560`, `590`, `980`; `AET805_000`, `500`; `AET810_020`; `AET811_020`; `AET813_040`, each with its `_Billboards` | 7.7 MB |
| DLC plants `AEG490` (Rauh, Church of the Bud), half resolution | the 40 `AET490_*` atlases | 19 MB |

About 49 MB in total as built (the first pack at full resolution would drop to about 1.7 MB at half); everything at quarter resolution would be about 12 MB. Texture replacement is the only lever for SpeedTree models.

### 5. Asset model swaps in the MSB (no new textures) - not built

Validated in game by the spike, but dropped by the winter plugin (see "Design decisions" above).

Replace the `ModelName` of vegetation `Asset` parts with vanilla snow models and add the model entries to the MSB model list; written with SoulsFormatsNEXT `MSBE` (byte-identical round trip on FogMod's MSBs; WitchyBND cannot repack those, it writes no manifest for them). Validated visually at the Chapel (385 swaps) and Stormveil (1227 swaps). Rules of the last spike's table (432 source models):

- Measurements come from the model's FLVER bounding box (`BoundingBoxMin/Max` in the header, part scale not applied; every swapped part had scale 1): footprint = the larger of the X and Z extents, height = Y extent. Eligible when footprint is at most 7 m and height at most 3.5 m.
- Every material path of the FLVER is resolved to its matbin and shader; all shaders must be foliage (`Grass*`, `Vesitation`, `Flower`). One grass material on a rock is not enough: a looser rule swapped the 14 m ground chunks `AEG210_554` and `AEG210_388` and opened holes in the Chapel floor.
- Targets: grass (and grass with flowers) to `AEG001_377`; bushes (`Vesitation`) to `AEG001_371` when the footprint is below 2.5 m, `AEG001_372` below 4 m, `AEG001_373` otherwise; flowers only to `AEG098_242`.
- SpeedTree shrubs have no matbin shaders and use their own table (16 models with FLVER height below 3 m): footprint below 3.5 m to `AEG001_371`, below 5 m to `AEG001_372`, otherwise `AEG001_373`; the tiny `AEG800_011` to `AEG001_374`.
- Snow bushes and tufts are placed at the source part's position and rotation; floating or sinking was not noticed in the two validated maps but was not measured.

Because FogMod writes only 285 MSBs per seed, per-seed swaps reach only those maps; vegetation in any other map needs the MSB pinned first (see `VanillaMapPinner`) or a static treatment. With the frosted SpeedTree atlases in place the swap mostly matters for the leaf-clump assets, so the design may decide to skip it.

### 6. Snowfall

The weather plugin already forces `snow` or `heavy_snow` (see [plugins/weather.md](plugins/weather.md)).

## Facts that cost rounds

- A matbin edit is read at map load; `regulation.bin` is read at game start only. Always restart the executable.
- ModEngine 2 gives priority to the first folder in the `mods` list (`speedfog`, then `speedfog-halloween`, `fogmod`, `itemrando`).
- Cumulative drops cannot attribute an effect to a cause. The isolation protocol that settled everything: T0 pristine seed, T1 one lever, T2 another lever, full restart each time, same screenshot spot. The game reads the tester's local Windows copy of the seed; a network mirror of the game folder is not what renders.
- A canary must sit on the material you can prove is the carrier. The Chapel's visible grass is asset `AEG210_554` through `Field_01_AEGgrass`, not the 67 000 baked vertices of `m10_00_879`; the "brown patch" in its courtyard was `m10_00_878` (930 vertices, metallic `Mb3_ColorAdjust`), not the leaf-litter grass rows. Smithbox names at the spot plus a per-material vertex census of the MapPieces resolve these fast.
- MATBIN strings are UTF-16 and 5888 of 15 089 files have them at odd byte offsets: a byte-regex scanner must not decode the whole file from offset 0 (moot with the SoulsFormatsNEXT reader).
- Foreign `group_N` UV parameters left behind by a conversion are accepted by the engine (57 such matbins rendered fine) but are dropped anyway.
- `allmaterial_dlc02` shader paths use `SPX_DLC02\`; selectors that match `SPX\` only see nothing of the DLC-specific shaders.

## Remaining gaps after the last spike

- Foliage not yet frosted: `[Translucency]` leaves outside `AET210_569` (85 matbins), `_Vesitation` foliage outside the frosted atlases (111 matbins, including the 68 `_Vesitation_Emissive_Glow`), per-map `_Grass2`/`_Grass` materials with their own atlases (111: Ainsel, Farum, Volcano, colosseums). The texture lever applies atlas by atlas, at a cost in megabytes.
- Decorative shaders left alone: `M[Flag]`, `[Wax]`, `[Crystal]`, `[magma]`, water, `M[A]`, skins and decals, DLC-only `M[ShadowThorn]`, `M[GhostStone]`.
- Trees (about 100 SpeedTree models, 42 546 placements) were excluded from the spike scope by Roger ("tout sauf les arbres"); their atlases exist if the design revisits it.

## Shape assumed by the spikes (for the design to confirm)

- Static part, built once at setup and shipped as an opt-in mod folder like `mods/speedfog-halloween`: the converted `allmaterial.matbinbnd.dcx` and `allmaterial_dlc02.matbinbnd.dcx` (2.5 MB and 0.5 MB as built), the frosted TPFs, and a FLVER scan of every mapbnd and geombnd to build the skinned-material exclusion list.
- Per-seed part (FogModWrapper, after FogMod writes the MSBs): vegetation model swaps from a curated table, within the MSBs the seed ships.
- Libraries: SoulsFormatsNEXT (submodule) reads and writes MATBIN, TPF, FLVER2 and MSBE natively, including KRAK-compressed DCX through `oo2core_6_win64.dll` when the tool runs under Wine, as StaticModBuilder already does for `TitleScreenPatcher`; StaticModBuilder already depends on BCnEncoder (BC7 today, BC1 available; keep encoding single-threaded under Wine, see [title-screen.md](title-screen.md)). WitchyBND is not needed for any of this.
- Open for the design: texture resolution (half or quarter), weather value, whether per-seed swaps are worth their MSB coverage limit, which remaining foliage families to frost, whether the `M[AMSN_V][Ov_N][Snow]` twin should be re-tested in isolation.
