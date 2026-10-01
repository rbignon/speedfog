# Summer Theme

Cosmetic, opt-in text reskin (`[plugin.summer] enabled = true`). Two layers,
both FMG edits (mirror `RunCompleteInjector`), applied by
`writer/FogModWrapper/TextTheme.cs` during `ApplyModDirInjectors`:

1. **Boss epithets** - rewrite boss `NpcName` entries (in `item.msgbnd.dcx` /
   `item_dlc02.msgbnd.dcx`). Tolerant: bosses absent from the catalogue keep
   vanilla names.
2. **UI banners** - rewrite selected `GR_MenuText` entries in
   `menu_dlc02.msgbnd.dcx` (felled/slain family, `YOU DIED`,
   `LOST GRACE DISCOVERED`).

Independent of the item/enemy randomizer and applied to every run, except
`boss_name` entries (below), which only exist on seeds where the enemy
randomizer placed that enemy.

The applier and loader are theme-parameterized (TextTheme /
TextThemeCatalogLoader); the halloween theme reuses them with its own
catalogue.

## Catalogue: data/plugins/summer.toml

`[[bosses]]`: exactly one of `npc_name_id` or `boss_name` (each unique),
`name` (reference), `en` (required), `fr` (optional).
`npc_name_id` targets a fixed NpcName id. `boss_name` is for an enemy with no
vanilla healthbar name (halloween's "Devonia"): when the enemy randomizer
places it in an arena, `BossNameInjector` writes its name under a NpcName id
allocated per seed (see [boss-healthbar-names.md](../boss-healthbar-names.md)),
and returns {name -> id} so the theme reskins that id. The key is the name as
graph.json `boss_names` carries it, trimmed and minus a trailing
parenthetical the injector drops: the quoted name of the FogModWrapper log
line `arena N -> "Devonia" (NpcName ..., new, ...)`. The loader rejects a key
that could never match (empty, padded, trailing parenthetical) and an
`npc_name_id` inside the injector's per-seed range. On a seed that does not
place the enemy, or where the name resolved to a vanilla id, the entry does
nothing; the theme logs one line per `boss_name` entry either way.
`[[ui]]`: `bnd`, `fmg`, `id`, `en` (required), `fr` (optional).

`en` is required; `fr` is optional. Only the English (`engus`) and French
(`frafr`) archives are edited: `engus` gets `en`, `frafr` gets `fr` (falling
back to `en`). All other game languages keep their vanilla names. Editing only
two languages instead of all ~15 keeps the per-seed cost low. Missing file =
silent no-op.

**French conventions** (both themes): `fr` is an adaptation, not a
translation; when an English pun does not carry over, use a French joke
instead (halloween's Godskin Duo: "Trick-or-Treat Duo" / "Duo Chair de
poule"), and start from the official frafr name, not from the English one
("Larme imitatrice", "chevaleresse" for Loretta and Rellana). Official
names: `data/i18n/fmg_names.json` (`NpcName`, base game only), and for the
DLC `game_inspect dump-fmg <game-dir>/msg/frafr/item_dlc02.msgbnd.dcx`
(`NpcName_dlc01`). `data/i18n/fr.toml` is SpeedFog's own translation and
is not a source for official names.
Casing follows vanilla frafr typography, not English title case. The
article decides: without one, an apposition is lowercase ("Rykard,
seigneur du blasphème" -> "Rykard, seigneur du barbecue"); with one, it is
a nickname, comma or not, that capitalizes its head noun and any adjective
before it, nothing after ("Godrick le Greffé", "Maliketh la Lame d'ébène",
"Margit, le Fantôme grognon", "le Premier Bourreau"). In-world proper
nouns keep their capitals, and so does a parody of one ("Ordre d'or" ->
"Ordre ensoleillé", "Seigneur d'Elden" -> "Fantôme d'Elden",
"Arbre-Sacré" -> "Arbre-Sucré", "mère des Doigts" -> "mère des Doigts de
sorcière"). Banners are all caps and unaffected.

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

`tools/seed_theme_catalog.py <path-to-enemy.txt>` prints `[[bosses]]`
skeletons for major-boss and final-boss clusters. It joins `clusters.json`
(each such cluster carries `defeat_flag` and `boss_name`) with `enemy.txt`
(each entity carries both `DefeatFlag` and `NpcName`), using the defeat flag
as the join key to resolve each boss's `NpcName` FMG id. Fill `en`/`fr`, paste
into the catalogue.

Distinct phase-1 healthbar names (e.g. `God-Devouring Serpent`, `Beast
Clergyman`, `Messmer the Impaler`) are SEPARATE `NpcName` FMG entries with no
`DefeatFlag`, so the tool cannot find them. Add them by hand: find the id with
`game_inspect dump-fmg <item.msgbnd.dcx> "<name>"` (the phase ids are usually
adjacent, e.g. `904710000`/`904710001`).

See the design spec: `docs/superpowers/specs/2026-06-19-summer-theme-plugin-design.md`
(local working file, not tracked in git).
