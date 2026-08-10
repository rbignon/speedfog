# Static Mod Sources

This directory holds source files that `tools/bootstrap.py` repacks into
`data/mods/speedfog/` with WitchyBND (see `build_static_mod_scripts()` in
`tools/bootstrap.py`).

Layout: one WitchyBND-unpacked directory per archive under `speedfog/`, e.g.
`speedfog/script/<name>-luabnd-dcx/` containing the unpacked files plus the
`_witchy-bnd4.xml` manifest. Bootstrap repacks each such directory into the
corresponding `.dcx` under `data/mods/speedfog/`.

The directory is currently empty on purpose: the only content it ever had
(the Rykard AI script) was reverted in commit c3f921d ("Revert 'overlay:
import Rykard AI script'"). The mechanism is kept so future sources can be
dropped here and become reproducible from the repository. When there are no
unpacked archives, the repack step is a no-op.
