# Overlay Sources

This directory holds source files that `tools/bootstrap.py` repacks into
`data/overlay/` with WitchyBND (see `build_overlay_scripts()` in
`tools/bootstrap.py`).

Layout: one WitchyBND-unpacked directory per archive, e.g.
`script/<name>-luabnd-dcx/` containing the unpacked files plus the
`_witchy-bnd4.xml` manifest. Bootstrap repacks each such directory into the
corresponding `.dcx` under `data/overlay/`.

The directory is currently empty on purpose: the only content it ever had
(the Rykard AI script) was reverted in commit c3f921d ("Revert 'overlay:
import Rykard AI script'"). The mechanism is kept so future overlay sources
can be dropped here and become reproducible from the repository. When this
directory contains no unpacked archives, the repack step is a no-op.
