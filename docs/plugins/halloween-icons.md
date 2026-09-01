# Halloween Item Icons

**Date:** 2026-09-01
**Status:** EXPERIMENT, in-game check not yet performed (see below)

Gives Golden Seed and Sacred Tear custom jack-o'-lantern / burning-chalice inventory
icons on Halloween seeds, matching the
item renames already done by the
text theme (`Pumpkin Seed`, `Profane Tear`; see
[halloween-theme.md](halloween-theme.md)). The user's authored art
shipped on 2026-09-01. Fourth feature under the `[plugin.halloween]`
namespace, alongside the text reskin, the ambient spawns, and the gate
decorations (see [halloween-ambient.md](halloween-ambient.md)).

## The two icons

Larval Tear ("Gummy Worm") originally had the third icon and text
override; both were removed on 2026-09-01 (user decision): the item is
never dropped during a run, so the reskin was invisible.

| Item | EquipParamGoods row | Vanilla `iconId` | Halloween `iconId` | Texture name |
|------|---------------------|-------------------|---------------------|--------------|
| Golden Seed | 10010 | 383 | 60383 | `MENU_ItemIcon_60383` |
| Sacred Tear | 10020 | 384 | 60384 | `MENU_ItemIcon_60384` |

The new ids are u16-safe (`iconId` is a u16 field) and far above every
vanilla icon id (max ~8490 across the 13 `SB_Icon` atlas pages), so they
cannot collide with an existing icon.

## Why not the title-screen atlas trick

[title-screen.md](title-screen.md) redirects a single sprite name out of
`01_common.tpf.dcx` into a tiny companion TPF (`02_title.tpf.dcx`, 70 KB)
that already existed as the title screen's own resource block. Item icons
have no such companion: `MENU_ItemIcon_00383` and `MENU_ItemIcon_00384`
live only inside `menu/{hi,low}/01_common.tpf.dcx`
(67 MB hi / 53 MB low) with their layout entries in
`01_common.sblytbnd.dcx`, and there is no small TPF in that group to host
a couple of standalone replacements. Shipping a modified copy of the full
atlas (the "in-place splice" approach) would cost close to ~120 MB
per seed, for a handful of item icons. That cost, not a technical blocker, is
why this plugin does not touch `01_common` at all.

## The chosen mechanism: iconId redirect + 05_dummy superset

Instead of adding entries to the atlas, the icons are added to
`menu/{hi,low}/05_dummy.tpf.dcx`, the tiny (24 KB) TPF the engine already
loads everywhere as its fallback-texture container (42 `MENU_Dummy*`
textures: blank/placeholder icons and menu elements shown when a name
resolves to nothing). The name-fallback lookup documented in
[title-screen.md](title-screen.md) ("How the game resolves the title
image") is the load-bearing mechanism here too: names not found in any
`.layout` atlas index are looked up directly among loaded TPF texture
names. `05_dummy.tpf.dcx` carries no layouts of its own, so every texture
in it (including the two new ones) is only reachable through that
fallback path, never through an atlas UV rect.

`EquipParamGoods.iconId` is then simply repointed from the vanilla id
to the new one (383 -> 60383, 384 -> 60384), so the
engine looks up `MENU_ItemIcon_60383` / `MENU_ItemIcon_60384` by name
wherever it draws
that item's icon, and the fallback lookup resolves them from the shipped
`05_dummy.tpf.dcx` superset. `01_common` and its layouts are never
touched; a seed with the plugin disabled ships nothing extra at all.

## The three moving parts

1. **`HalloweenIconPatcher`** (`writer/StaticModBuilder/HalloweenIconPatcher.cs`,
   runs at bootstrap, not per-seed): reads the icon PNGs from
   `data/plugins/`, encodes each once to raw BC7 blocks (BcEncoder, `Balanced`
   quality, `IsParallel = false`, same Wine-crash workaround as
   `TitleScreenPatcher`), then for each of `menu/{hi,low}`: reads the
   vanilla `05_dummy.tpf.dcx`, validates its `MENU_DummyTransparent`
   entry as a well-formed single-mip BC7 DX10 DDS
   (`TryValidateBc7Template`, warn-and-skip on a malformed or
   too-short template instead of throwing), borrows only that entry's
   148-byte DX10 header as a template for `DdsAtlas.BuildStandaloneDds`
   (width, height and pixel data are fully replaced; there is no
   icon-sized vanilla DDS to splice a header from, unlike the title
   patch's own sprite), adds or replaces the `MENU_ItemIcon_*`
   textures by name (idempotent reruns), and writes the superset TPF to
   `data/mods/speedfog-halloween/menu/{hi,low}/05_dummy.tpf.dcx`. Invoked
   by `tools/bootstrap.py` via `--halloween-dir`, unconditionally
   (`HALLOWEEN_MOD_DEST`), independent of whether any seed enables the
   plugin.
2. **`HalloweenIconInjector`** (`writer/FogModWrapper/HalloweenIconInjector.cs`,
   runs per seed): `ApplyTo(RegulationEditor reg)` reads
   `EquipParamGoods`, and for rows 10010 and 10020 sets `iconId` to
   `SpeedFogIds.HalloweenGoldenSeedIcon` (60383) /
   `HalloweenSacredTearIcon` (60384). Called from `Program.cs`'s
   `ApplyRegulation`, inside the same `IsPluginEnabled("halloween")`
   block as `AmbientSpawnInjector.ApplyPassiveThinkRow`. Prints
   `Halloween icons: repointed N item icon id(s)`; a missing row logs a
   warning and is skipped rather than failing the build.
3. **Packaging conditional overlay** (`speedfog/packaging.py`,
   `package_seed` / `write_modengine_config`): copies
   `data/mods/speedfog-halloween/` into `seed_dir/mods/speedfog-halloween`
   and registers it as a ModEngine 2 mod, right after the always-on
   `speedfog` static mod entry and before `fogmod`.

### Gating: the double condition

Both the regulation redirect and the packaging copy are gated on
`[plugin.halloween] enabled = true`, but packaging additionally requires
the overlay directory to actually exist and hold files:

```python
halloween_mod_enabled = (
    halloween_enabled
    and halloween_dir.is_dir()
    and any(f.is_file() for f in halloween_dir.rglob("*"))
)
```

This mirrors the existing `static_mod_enabled` check for `data/mods/speedfog/`.
It matters because the two conditions are independent in time: the plugin
flag is a per-seed config choice, while the overlay directory is built once
at bootstrap and only if `tools/bootstrap.py`'s `run_static_mod_builder`
actually ran with a game dir that has `05_dummy.tpf.dcx`. A seed can
therefore have the plugin enabled with no overlay to ship (bootstrap not
run, or `HalloweenIconPatcher` warn-skipped for a missing PNG or
malformed template); in that case the iconId redirect still fires in
FogModWrapper (it only touches `regulation.bin`, no dependency on the
overlay), but nothing is copied or registered, so the game falls back to
its own dummy/blank icon instead of a broken reference to a mod file that
was never shipped. The reverse (overlay built, plugin disabled) never
ships the overlay either, since `halloween_enabled` is the first term.

## The art and how to replace it

`data/plugins/halloween_icon_pumpkin_seed.png` (a jack-o'-lantern) and
`data/plugins/halloween_icon_profane_tear.png` (a chalice of burning
liquid) are the user's AUTHORED 160x160 RGBA icons (landed 2026-09-01,
replacing the original Pillow placeholders). 160x160 matches every
vanilla item icon's size; `HalloweenIconPatcher` warns and skips the
whole overlay if a PNG is missing or not exactly that size.

`tools/generate_halloween_icons.py` regenerates the ORIGINAL flat
placeholders (Pillow only, no fonts). Running it now OVERWRITES the
authored art with placeholders; only do that deliberately, and recover
with `git checkout -- data/plugins/halloween_icon_*.png` if run by
mistake.

To replace the art again:

1. Overwrite the PNGs under `data/plugins/` with authored art (same
   filenames, 160x160 RGBA).
2. Re-run `tools/bootstrap.py`, or invoke `StaticModBuilder` directly
   with `--halloween-dir data/mods/speedfog-halloween --data-dir data`
   (same shape as the title screen re-run in title-screen.md).
3. Rebuild or re-copy a seed to pick up the new overlay.

## EXPERIMENT STATUS

This feature has not been verified in-game yet. Two things are unknown
until it is:

1. **Name-fallback resolution for inventory icons specifically.** The
   fallback lookup is documented and already validated in-game for the
   title screen sprite (a Scaleform GFX image reference), but inventory
   icons are drawn through a different UI path (SB_Icon layouts read by
   the inventory menu widgets). It is not yet confirmed that the same
   loaded-TPF-name fallback applies there, as opposed to, say, only
   resolving names present in some layout.
2. **`05_dummy.tpf.dcx` residency.** `title-screen.md` notes an untested
   corner where `02_title` residency after quitting out to the title
   screen was suspect; `05_dummy` is a different, always-loaded menu
   texture group, but whether it stays resident across every menu context
   the inventory can be opened from (in a run, after quitting out, after
   a warp) is unverified.

**In-game check owed** (see the task's smoke report for the seed used):
open the inventory on a Halloween seed, check the Golden Seed
("Pumpkin Seed") and Sacred Tear ("Profane Tear") icons. Custom
jack-o'-lantern / burning-chalice art
means the experiment is confirmed and the mechanism is
solid; a dummy or blank icon means the name-fallback path does not
resolve for inventory icons (or does not stay resident), and one of the
fallbacks below applies.

### Revert path

If the in-game check fails, remove the `HalloweenIconInjector.ApplyTo(reg)`
call from `Program.cs`'s `ApplyRegulation` (or simply disable the plugin
in config, `[plugin.halloween] enabled = false`): `iconId` stays vanilla,
and with the plugin off packaging never ships the overlay either. No
other part of the Halloween theme depends on this feature.

### Fallbacks if the experiment fails

1. **Repoint `iconId` to an existing thematic vanilla icon** (zero
   shipping cost): pick an existing vanilla `MENU_ItemIcon_*` id that
   reads as pumpkin/candy-adjacent (or just visually distinct) and set
   `HalloweenIconInjector`'s redirects to that vanilla id instead of a
   new one. No overlay TPF needed at all; this only works if the
   engine's per-item icon slot is not itself hardcoded to specific vanilla
   ids elsewhere (it should not be, since `iconId` is the only field
   read).
2. **Full-atlas in-place splice via `DdsAtlas`** (last resort, ~120 MB
   per seed): follow the `TitleScreenPatcher` recipe against
   `01_common.tpf.dcx` and `01_common.sblytbnd.dcx` directly instead of
   `05_dummy.tpf.dcx`, extracting and replacing the two `SB_Icon_*`
   atlas regions in place. This is the atlas approach the icon redirect
   was built specifically to avoid; only fall back to it if the
   name-fallback path is confirmed not to work for inventory icons at
   all.
