# Summer Theme

Cosmetic, opt-in text reskin (`[plugin.summer] enabled = true`). Two layers,
both FMG edits (mirror `RunCompleteInjector`), applied by
`writer/FogModWrapper/SummerTheme.cs` during `ApplyModDirInjectors`:

1. **Boss epithets** - rewrite boss `NpcName` entries (in `item.msgbnd.dcx` /
   `item_dlc02.msgbnd.dcx`). Tolerant: bosses absent from the catalogue keep
   vanilla names.
2. **UI banners** - rewrite selected `GR_MenuText` entries in
   `menu_dlc02.msgbnd.dcx` (felled/slain family, `YOU DIED`,
   `LOST GRACE DISCOVERED`).

Independent of the item/enemy randomizer; applies to every run.

## Catalogue: data/plugins/summer.toml

`[[bosses]]`: `npc_name_id` (required, unique), `name` (reference), `en`
(required), `fr` (optional).
`[[ui]]`: `bnd`, `fmg`, `id`, `en` (required), `fr` (optional).

`en` is required; `fr` is written to `frafr` (falls back to `en`). Every other
language gets `en`. Missing file = silent no-op.

**Reserved:** `GR_MenuText[331314]` (VICTORY) is used by `RunCompleteInjector`;
the loader rejects it.

## Discovering UI string ids

`game_inspect dump-fmg <msgbnd.dcx> [substring]` walks all FMGs and prints
`<fmgName> <id> <text>`:

```
wine tools/game_inspect/publish/win-x64/game_inspect.exe \
  dump-fmg <game-dir>/msg/engus/menu_dlc02.msgbnd.dcx "FELLED"
```

Resolved v1 ids (GR_MenuText): 331301 DEMIGOD FELLED, 331302 LEGEND FELLED,
331303 GREAT ENEMY FELLED, 331304 ENEMY FELLED, 331305 YOU DIED,
331322 GOD SLAIN, 331311 LOST GRACE DISCOVERED.

## Adding boss epithets

`tools/seed_summer_catalog.py <path-to-enemy.txt>` prints `[[bosses]]`
skeletons for major bosses. It joins `clusters.json` (each `major_boss`
cluster carries `defeat_flag` and `boss_name`) with `enemy.txt` (each entity
carries both `DefeatFlag` and `NpcName`), using the defeat flag as the join
key to resolve each boss's `NpcName` FMG id. Fill `en`/`fr`, paste into the
catalogue.

See the design spec: `docs/superpowers/specs/2026-06-19-summer-theme-plugin-design.md`.
