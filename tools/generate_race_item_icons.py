#!/usr/bin/env python3
"""Generate placeholder race item icons (the 11 icons of race_items.toml).

Standalone from the project venv: requires Pillow only
(uv run --with pillow python tools/generate_race_item_icons.py). Draws one
simple flat shape and colour per icon (shield, pouch, banana, red shell,
mushroom, hammer, ink splash, blue shell, lightning bolt, stoppered flask,
flower) on transparent 1024x1024 canvases, the size of the final art:
ItemIconPatcher derives every in-game texture (160x160, and 1024x1024 or
512x512 for the large icon) from each file. Outputs are committed in
data/plugins/ as race_items_icon_<icon>.png.

The final art is authored by the graphic artist and simply overwrites these
files: once it has landed, never re-run this script, it would overwrite the
art. It is only for changing the placeholder design. After changing any PNG,
re-run tools/bootstrap.py (or StaticModBuilder directly) to rebuild
data/mods/speedfog-icons/. See docs/plugins/race-items.md.
"""

from collections.abc import Callable, Sequence
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw

SIZE = 1024
# The shapes below are drawn on a DESIGN x DESIGN grid, scaled to SIZE.
DESIGN = 160
PLUGINS_DIR = Path(__file__).resolve().parent.parent / "data" / "plugins"

OUTLINE = (30, 24, 28, 255)
WHITE = (246, 246, 240, 255)

STEEL = (150, 160, 176, 255)
STEEL_DARK = (96, 106, 124, 255)
GOLD = (222, 178, 52, 255)
LEATHER = (150, 98, 52, 255)
LEATHER_DARK = (104, 64, 30, 255)
BANANA_YELLOW = (244, 208, 52, 255)
BANANA_TIP = (110, 78, 36, 255)
RED = (214, 44, 44, 255)
RED_DARK = (150, 24, 28, 255)
CAP_RED = (200, 52, 48, 255)
STEM_CREAM = (240, 226, 190, 255)
HAMMER_HEAD = (120, 126, 140, 255)
WOOD = (160, 108, 60, 255)
INK = (44, 38, 92, 255)
INK_SHINE = (110, 100, 190, 255)
BLUE = (48, 104, 216, 255)
BLUE_DARK = (28, 60, 150, 255)
BOLT_YELLOW = (252, 226, 40, 255)
GLASS = (170, 214, 232, 255)
POTION = (84, 196, 120, 255)
CORK = (176, 128, 76, 255)
PETAL = (224, 80, 150, 255)
PETAL_DARK = (160, 40, 104, 255)
FLOWER_CENTER = (250, 214, 64, 255)
STEM_GREEN = (70, 150, 70, 255)


class ScaledDraw:
    """ImageDraw calls in design units: coordinates, widths and radii scaled."""

    def __init__(self, draw: ImageDraw.ImageDraw, scale: float) -> None:
        self._draw = draw
        self._scale = scale

    def __getattr__(self, name: str) -> Callable[..., None]:
        method = getattr(self._draw, name)
        scale = self._scale

        def scaled(xy: Sequence[Any], *args: Any, **kwargs: Any) -> None:
            if isinstance(xy[0], int | float):
                points: list[Any] = [v * scale for v in xy]
            else:
                points = [(x * scale, y * scale) for x, y in xy]
            kwargs["width"] = round(kwargs.get("width", 1) * scale)
            if "radius" in kwargs:
                kwargs["radius"] *= scale
            method(points, *args, **kwargs)

        return scaled


def draw_shield(draw: ScaledDraw) -> None:
    """A heater shield with a gold boss."""
    outline = [(24, 28), (136, 28), (136, 84), (80, 148), (24, 84)]
    draw.polygon(outline, fill=STEEL, outline=OUTLINE, width=4)
    draw.polygon([(80, 28), (136, 28), (136, 84), (80, 148)], fill=STEEL_DARK)
    draw.line(outline + [outline[0]], fill=OUTLINE, width=4, joint="curve")
    draw.ellipse((60, 56, 100, 96), fill=GOLD, outline=OUTLINE, width=4)


def draw_rune_pouch(draw: ScaledDraw) -> None:
    """A drawstring pouch with a golden rune coin on it."""
    draw.ellipse((26, 52, 134, 146), fill=LEATHER, outline=OUTLINE, width=4)
    draw.polygon(
        [(58, 56), (102, 56), (118, 30), (42, 30)],
        fill=LEATHER_DARK,
        outline=OUTLINE,
    )
    draw.rectangle((54, 50, 106, 62), fill=GOLD, outline=OUTLINE, width=3)
    draw.ellipse((62, 82, 98, 118), fill=GOLD, outline=OUTLINE, width=3)
    draw.line((80, 90, 80, 110), fill=OUTLINE, width=4)


def draw_banana(draw: ScaledDraw) -> None:
    """A crescent banana: a thick yellow arc with a brown tip and stem."""
    draw.arc((18, 14, 142, 138), start=20, end=160, fill=OUTLINE, width=34)
    draw.arc((18, 14, 142, 138), start=22, end=158, fill=BANANA_YELLOW, width=26)
    draw.ellipse((118, 92, 134, 108), fill=BANANA_TIP, outline=OUTLINE, width=2)
    draw.rectangle((22, 88, 36, 106), fill=BANANA_TIP, outline=OUTLINE, width=2)


def draw_red_shell(draw: ScaledDraw) -> None:
    """A red turtle shell: a dome with a white rim and a pale belly band."""
    draw.pieslice(
        (18, 28, 142, 156), start=180, end=360, fill=RED, outline=OUTLINE, width=4
    )
    draw.rectangle((18, 90, 142, 112), fill=WHITE, outline=OUTLINE, width=4)
    draw.ellipse((62, 46, 98, 82), fill=RED_DARK)
    draw.arc((34, 44, 126, 136), start=200, end=250, fill=WHITE, width=5)


def draw_mushroom(draw: ScaledDraw) -> None:
    """A red-capped, white-spotted mushroom on a cream stem."""
    draw.rounded_rectangle(
        (56, 84, 104, 140), radius=10, fill=STEM_CREAM, outline=OUTLINE, width=4
    )
    draw.pieslice(
        (16, 22, 144, 150), start=180, end=360, fill=CAP_RED, outline=OUTLINE, width=4
    )
    draw.rectangle((16, 85, 144, 92), fill=CAP_RED)
    draw.line((16, 86, 144, 86), fill=OUTLINE, width=4)
    for x, y, r in ((46, 62, 11), (82, 44, 12), (112, 66, 10)):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=WHITE)


def draw_hammer(draw: ScaledDraw) -> None:
    """A war hammer: a heavy steel head on a diagonal wooden haft."""
    draw.line((44, 128, 112, 54), fill=OUTLINE, width=22)
    draw.line((44, 128, 112, 54), fill=WOOD, width=14)
    draw.polygon(
        [(70, 22), (142, 62), (124, 96), (52, 56)],
        fill=HAMMER_HEAD,
        outline=OUTLINE,
        width=4,
    )
    draw.polygon([(70, 22), (142, 62), (136, 72), (62, 34)], fill=STEEL)


def draw_ink(draw: ScaledDraw) -> None:
    """An ink splash: a blob with droplets around it."""
    draw.ellipse((34, 40, 126, 126), fill=INK, outline=OUTLINE, width=3)
    for x0, y0, x1, y1 in (
        (14, 54, 52, 92),
        (108, 28, 148, 68),
        (96, 104, 142, 148),
        (22, 108, 50, 136),
        (64, 18, 86, 40),
    ):
        draw.ellipse((x0, y0, x1, y1), fill=INK, outline=OUTLINE, width=3)
    draw.ellipse((52, 58, 72, 78), fill=INK_SHINE)


def draw_blue_shell(draw: ScaledDraw) -> None:
    """A blue turtle shell with spikes, to stay distinct from the red one."""
    draw.pieslice(
        (18, 36, 142, 164), start=180, end=360, fill=BLUE, outline=OUTLINE, width=4
    )
    draw.rectangle((18, 98, 142, 118), fill=WHITE, outline=OUTLINE, width=4)
    for x in (44, 80, 116):
        draw.polygon([(x - 12, 74), (x + 12, 74), (x, 34)], fill=WHITE, outline=OUTLINE)
    draw.ellipse((62, 76, 98, 96), fill=BLUE_DARK)


def draw_lightning(draw: ScaledDraw) -> None:
    """A jagged yellow lightning bolt."""
    bolt = [(92, 10), (36, 88), (72, 88), (56, 150), (126, 64), (88, 64), (112, 10)]
    draw.polygon(bolt, fill=BOLT_YELLOW)
    draw.line(bolt + [bolt[0]], fill=OUTLINE, width=4, joint="curve")


def draw_flask_stopper(draw: ScaledDraw) -> None:
    """A round flask of green liquid sealed with a cork stopper."""
    draw.rectangle((64, 30, 96, 62), fill=GLASS, outline=OUTLINE, width=4)
    draw.ellipse((26, 54, 134, 148), fill=GLASS, outline=OUTLINE, width=4)
    draw.pieslice((34, 62, 126, 140), start=0, end=180, fill=POTION)
    draw.rectangle((60, 14, 100, 36), fill=CORK, outline=OUTLINE, width=4)
    draw.line((120, 96, 40, 96), fill=OUTLINE, width=3)


def draw_toxic_blossom(draw: ScaledDraw) -> None:
    """A five-petal flower on a green stem."""
    draw.line((80, 100, 80, 150), fill=STEM_GREEN, width=10)
    draw.ellipse((84, 120, 124, 142), fill=STEM_GREEN, outline=OUTLINE, width=2)
    for x, y in ((80, 28), (112, 50), (100, 88), (60, 88), (48, 50)):
        draw.ellipse(
            (x - 22, y - 22, x + 22, y + 22), fill=PETAL, outline=OUTLINE, width=3
        )
    draw.ellipse((62, 46, 98, 82), fill=FLOWER_CENTER, outline=PETAL_DARK, width=3)


ICONS: dict[str, Callable[[ScaledDraw], None]] = {
    "shield": draw_shield,
    "rune_pouch": draw_rune_pouch,
    "banana": draw_banana,
    "red_shell": draw_red_shell,
    "mushroom": draw_mushroom,
    "hammer": draw_hammer,
    "ink": draw_ink,
    "blue_shell": draw_blue_shell,
    "lightning": draw_lightning,
    "flask_stopper": draw_flask_stopper,
    "toxic_blossom": draw_toxic_blossom,
}


def main() -> None:
    for icon, painter in ICONS.items():
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        painter(ScaledDraw(ImageDraw.Draw(img), SIZE / DESIGN))
        out = PLUGINS_DIR / f"race_items_icon_{icon}.png"
        img.save(out)
        print(f"wrote {out}")


if __name__ == "__main__":
    main()
