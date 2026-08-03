# Title Screen Badge

**Date:** 2026-08-03
**Status:** Active (redirect model validated in-game)

Overlays a SpeedFog badge (Inter wordmark + waving checkered flag, top right of the logo) onto the vanilla ELDEN RING title screen artwork. Applied at setup by GamePatcher (`TitleScreenPatcher`), not per-seed, and shipped as ~1.5 MB of patched files instead of the 112 MB a naive atlas replacement would cost.

## How the game resolves the title image

The title screen is drawn by Scaleform GFX files that reference images **by name**, resolved by the engine at runtime:

- `menu/05_001_title_logo.gfx` contains a `GFxDefineExternalImage2` entry for `MENU_Title_EldenRing_01.tga` (2532x1532). No pixels and no atlas UVs live in the GFX.
- The engine builds a sprite registry from the `.layout` XML files bundled in `menu/{hi,low}/01_common.sblytbnd.dcx`. Vanilla maps `MENU_Title_EldenRing_01` into the `SB_Title_01` atlas of `01_common.tpf.dcx` (65 MB hi / 52 MB low; the atlas also holds HUD bars and inventory icons). **hi and low pack their atlases differently** (sprite at y=60 in hi, y=0 in low) but hold identical sprite pixels.
- Names not found in any layout are looked up **directly among loaded TPF texture names**: that is how the boot logos (`MENU_FROMSOFTWARE_Logo` in `02_title.tpf.dcx`) and the `MENU_Dummy*` fallbacks resolve; those TPFs contain no layouts at all.
- The menu texture groups (`01_Common`, `02_Title`, `05_Dummy`, ...) are a hardcoded table in the exe; `02_title.tpf.dcx` is the title screen's own resource block (70 KB vanilla). Dangling references are harmless: the same GFX still references `MENU_DS3_LOGO`, which resolves nowhere.

## How the patch works

Instead of shipping a modified 65 MB atlas, the patch **redirects the lookup** (validated in-game 2026-08-03):

1. `01_common.sblytbnd.dcx` (21 KB): the `MENU_Title_EldenRing_01` SubTexture entry is removed from `SB_Title_01.layout`, so the atlas no longer resolves the name.
2. `02_title.tpf.dcx` (70 KB -> ~730 KB): a standalone texture named `MENU_Title_EldenRing_01` is added: the vanilla sprite pixels with the badge alpha-composited on top, BC7-encoded, single-mip DX10 DDS (TPF format byte 102 like the other menu BC7 textures).

`TitleScreenPatcher` does this per variant (hi and low) at setup, reading only vanilla files from the game dir: parse the sprite rect from the layout (this also absorbs the hi/low packing difference and future repacks), extract the sprite's BC7 blocks from the atlas (`DdsAtlas.ExtractBlocks`, read-only), decode, composite `data/title_screen_overlay.png`, re-encode, wrap as standalone DDS (`DdsAtlas.BuildStandaloneDds`), rewrite the sblytbnd and 02_title into `data/overlay/menu/{hi,low}/`. hi and low sprite pixels are identical, so the encode runs once (cached on pixel content).

Encoding is single-threaded: BCnEncoder's parallel encode loop crashes non-deterministically under Wine's .NET runtime (see the comment in `TitleScreenPatcher.CompositeAndEncode`); serial Balanced takes ~90s and re-encoding BC7-decoded content gains nothing visible from BestQuality. Inside the sprite the vanilla art goes through one decode/re-encode generation, visually lossless in practice at Balanced (measured max per-channel delta 29/255, mean ~1).

The committed artwork is `data/title_screen_overlay.png`, a **2532x1532 RGBA overlay, transparent except the badge** (plus an opaque black patch erasing the vanilla TM glyph, whose spot the badge takes). It was originally produced by `tools/generate_title_screen.py` and has since been hand-polished (commit 920905a): **the committed PNG is authoritative**, and re-running the script overwrites it with the script's older design. Use the script as a starting point for redesigns only.

`speedfog/main.py` already copies `data/overlay/` recursively into each seed's `mods/fogmod/`, and ModEngine 2 picks the files up as loose-file overrides. No per-seed work, and no vanilla art is committed to the repo.

## Replacing the artwork

1. Edit `tools/generate_title_screen.py` and re-run it (or replace `data/title_screen_overlay.png` directly; the size must match the sprite rect, 2532x1532, or the patch skips with a warning).
2. Re-run the overlay generation:

```bash
cd writer/GamePatcher
wine publish/win-x64/GamePatcher.exe <game-dir> ../../data/overlay --data-dir ../../data
```

(or re-run `tools/bootstrap.py`, which passes `--data-dir` automatically). The title patch takes ~2 minutes, dominated by the serial BC7 encode.

3. Rebuild or re-copy a seed output to get the new overlay files.

## Verification

```bash
# layout: the sprite entry must be gone from SB_Title_01.layout
wine tools/witchybnd/WitchyBND.exe -p data/overlay/menu/hi/01_common.sblytbnd.dcx
grep -c MENU_Title_EldenRing_01 data/overlay/menu/hi/01_common-sblytbnd-dcx/SB_Title_01.layout  # 0

# texture: 02_title must contain the composited standalone texture
wine tools/witchybnd/WitchyBND.exe -p data/overlay/menu/hi/02_title.tpf.dcx
python3 -c "from PIL import Image; Image.open(
    'data/overlay/menu/hi/02_title-tpf-dcx/MENU_Title_EldenRing_01.dds').convert('RGB').save('/tmp/title.png')"
```

If `data/overlay/menu/{hi,low}/01_common.tpf.dcx` exists, it is a stale full-atlas override from a pre-redirect SpeedFog version; the patcher deletes it on its next run, and it is safe to delete by hand.

In-game: the badge shows on the title screen and above the main menu entries. Failure modes are benign: if the redirect ever breaks (game update reshuffling the menu blocks), the title logo disappears but nothing crashes; the patcher itself warn-skips on any unexpected layout/DDS shape. If the logo is missing specifically after quitting out to the title from a run, suspect `02_title` residency on that path (untested corner, see git history for the investigation notes).
