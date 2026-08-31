# Halloween Theme

Cosmetic, opt-in text reskin (`[plugin.halloween] enabled = true`), applied
by `writer/FogModWrapper/TextTheme.cs` (shared with the summer theme; see
[summer-theme.md](summer-theme.md) for the mechanism, schema, and id
discovery workflow). Catalogue: `data/plugins/halloween.toml`.

Register: commercial kitsch Halloween (candy, costumes, jack-o'-lanterns),
never genuine-spooky. See the design spec:
`docs/superpowers/specs/2026-08-03-halloween-theme-design.md`.

Beyond the summer-style boss epithets and banners, the catalogue also
renames Golden Seed (`Pumpkin Seed`), Larval Tear (`Gummy Worm`) and Sacred
Tear (`Sacred Gumdrop`, text-only) via `GoodsName`/`GoodsCaption` in
`item_dlc02.msgbnd.dcx` (ids 10010 / 8185 / 10020), the first two matching
the icon replacement (spec 1.2), and overrides five loading tips
(`LoadingText.fmg` in `menu_dlc02.msgbnd.dcx`; the `.fmg` extension in the
`fmg` field pins the base file against the `_dlc01` variant, which the
applier's substring match would otherwise hit).

Both text themes can be enabled at once; they apply in order (summer then
halloween) and the later one overwrites colliding entries, with a warning
logged by `Program.cs`.
