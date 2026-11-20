#!/usr/bin/env python3
"""Import delivered race item art into data/plugins/ as race_items_icon_<icon>.png.

Standalone from the project venv: requires Pillow only
(uv run --with pillow python tools/import_race_item_icons.py banana=Banane.webp ...).

The art arrives on canvases of any size with uneven empty space around the
drawing. Each `icon=path` pair is reframed the same way, so every icon fills
its in-game slot alike: the drawing (pixels above ALPHA_FLOOR, which leaves a
faint glow or a stray pixel out of the measure) is centred and scaled so its
longer side takes FILL of a SIZE x SIZE transparent canvas. A glow past the
measured box lands in the margin; one wider than the margin shrinks the
drawing until it fits, so nothing is cut off.

`icon` must be a name of the `[icons]` table of data/plugins/race_items.toml.
Every pair is checked before any file is written. After an import, re-run
tools/bootstrap.py (or StaticModBuilder directly) to rebuild
data/mods/speedfog-icons/. See docs/plugins/race-items.md.
"""

import sys
import tomllib
from pathlib import Path

from PIL import Image

SIZE = 768
FILL = 0.88
ALPHA_FLOOR = 8
PLUGINS_DIR = Path(__file__).resolve().parent.parent / "data" / "plugins"


def reframe(
    image: Image.Image, size: int = SIZE, fill: float = FILL
) -> Image.Image | None:
    """The drawing centred on a size x size canvas, or None for an empty image."""
    image = image.convert("RGBA")
    solid = image.getchannel("A").point(lambda a: 255 if a > ALPHA_FLOOR else 0)
    box = solid.getbbox()
    if box is None:
        return None
    left, top, right, bottom = box
    scale = size * fill / max(right - left, bottom - top)
    # The farthest any pixel sits from the drawing's centre must fit in half
    # the canvas.
    centre_x, centre_y = (left + right) / 2, (top + bottom) / 2
    all_left, all_top, all_right, all_bottom = image.getchannel("A").getbbox()
    reach = max(
        centre_x - all_left,
        all_right - centre_x,
        centre_y - all_top,
        all_bottom - centre_y,
    )
    scale = min(scale, (size / 2 - 1) / reach)
    scaled = image.resize(
        (round(image.width * scale), round(image.height * scale)),
        Image.Resampling.LANCZOS,
    )
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(
        scaled,
        (round(size / 2 - centre_x * scale), round(size / 2 - centre_y * scale)),
    )
    return canvas


def main(argv: list[str]) -> int:
    with (PLUGINS_DIR / "race_items.toml").open("rb") as handle:
        known = set(tomllib.load(handle)["icons"])

    pairs: list[tuple[str, Path]] = []
    problems: list[str] = []
    for arg in argv:
        icon, sep, source = arg.partition("=")
        if not sep:
            problems.append(f"{arg}: expected icon=path")
        elif any(icon == seen for seen, _ in pairs):
            problems.append(f"{icon}: given twice")
        elif icon not in known:
            problems.append(f"{icon}: not in the catalog's [icons]")
        elif not Path(source).is_file():
            problems.append(f"{source}: missing")
        else:
            pairs.append((icon, Path(source)))
    if problems or not pairs:
        print(__doc__ if not argv else "nothing imported:", file=sys.stderr)
        for line in problems:
            print(f"  {line}", file=sys.stderr)
        return 1

    framed: list[tuple[Path, Image.Image]] = []
    for icon, source in pairs:
        with Image.open(source) as image:
            result = reframe(image)
        if result is None:
            print(f"nothing imported: {source} is fully transparent", file=sys.stderr)
            return 1
        framed.append((PLUGINS_DIR / f"race_items_icon_{icon}.png", result))

    for out, image in framed:
        image.save(out, optimize=True)
        print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
