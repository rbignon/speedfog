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
the icon replacement (spec 1.2).

Loading-screen tips were shipped in v1 and dropped after in-game testing:
the engine reads `GR_MenuText` from `menu_dlc02.msgbnd.dcx` (themed banners
display) but `LoadingText` from the base `menu.msgbnd.dcx`, which SpeedFog
does not ship, so tips written to the dlc02 copy never display. See the
design spec's Rejected section before retrying.

Both text themes can be enabled at once; they apply in order (summer then
halloween) and the later one overwrites colliding entries, with a warning
logged by `Program.cs`.
