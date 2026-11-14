#!/usr/bin/env python3
"""Generate placeholder race item icons (the 11 icons of race_items.toml).

Standalone from the project venv: requires Pillow only
(uv run --with pillow python tools/generate_race_item_icons.py). Draws one
flat, outlined drawing per icon (war horn, rune coin, banana, red shell,
mushroom, hammer, squid, blue shell, lightning bolt, ghost holding a cork,
flytrap) on transparent 1024x1024 canvases, the size asked of the final art:
ItemIconPatcher derives every in-game texture (160x160, the large icon
included) from each file. Outputs are committed in
data/plugins/ as race_items_icon_<icon>.png.

The final art is authored by the graphic artist and simply overwrites these
files: once it has landed, never re-run this script, it would overwrite the
art. It is only for changing the placeholder design. After changing any PNG,
re-run tools/bootstrap.py (or StaticModBuilder directly) to rebuild
data/mods/speedfog-icons/. See docs/plugins/race-items.md.
"""

import math
from collections.abc import Callable, Sequence
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw

SIZE = 1024
# The shapes below are drawn on a DESIGN x DESIGN grid, scaled to SIZE.
DESIGN = 160
PLUGINS_DIR = Path(__file__).resolve().parent.parent / "data" / "plugins"

Point = tuple[float, float]

OUTLINE = (30, 24, 28, 255)
WHITE = (246, 246, 240, 255)

GOLD = (222, 178, 52, 255)
GOLD_DARK = (164, 120, 28, 255)
GOLD_LIGHT = (252, 228, 132, 255)
HORN = (238, 224, 188, 255)
HORN_SHADE = (196, 172, 124, 255)
HORN_MOUTH = (120, 92, 56, 255)
BANANA_YELLOW = (244, 208, 52, 255)
BANANA_TIP = (110, 78, 36, 255)
RED = (214, 44, 44, 255)
RED_DARK = (150, 24, 28, 255)
CAP_RED = (200, 52, 48, 255)
STEM_CREAM = (240, 226, 190, 255)
STEEL = (150, 160, 176, 255)
HAMMER_HEAD = (120, 126, 140, 255)
WOOD = (160, 108, 60, 255)
INK = (44, 38, 92, 255)
SQUID = (122, 78, 184, 255)
SQUID_DARK = (82, 50, 140, 255)
SQUID_SPOT = (176, 142, 226, 255)
BLUE = (48, 104, 216, 255)
BLUE_DARK = (28, 60, 150, 255)
BOLT_YELLOW = (252, 226, 40, 255)
GHOST = (228, 234, 248, 255)
GHOST_SHADE = (170, 180, 212, 255)
GHOST_EYE = (52, 46, 84, 255)
CORK = (176, 128, 76, 255)
CORK_DARK = (124, 84, 44, 255)
TRAP = (136, 58, 156, 255)
TRAP_INNER = (206, 112, 198, 255)
THROAT = (64, 22, 70, 255)
CILIA = (234, 244, 200, 255)
TOXIC = (156, 222, 64, 255)
STEM_GREEN = (70, 150, 70, 255)
LEAF_DARK = (44, 104, 50, 255)
BOX_FRONT = (36, 76, 196, 255)
BOX_SIDE = (22, 46, 132, 255)
BOX_TOP = (74, 120, 232, 255)


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


def bezier(controls: Sequence[Point], steps: int = 24) -> list[Point]:
    """Points along the Bezier curve of the control points (any degree)."""
    out = []
    for i in range(steps + 1):
        t = i / steps
        pts = list(controls)
        while len(pts) > 1:
            pts = [
                ((1 - t) * x0 + t * x1, (1 - t) * y0 + t * y1)
                for (x0, y0), (x1, y1) in zip(pts, pts[1:], strict=False)
            ]
        out.append(pts[0])
    return out


def ellipse_arc(
    center: Point,
    radii: Point,
    start: float,
    end: float,
    angle: float = 0.0,
    steps: int = 24,
) -> list[Point]:
    """Points along an ellipse arc, rotated by angle (degrees, y down)."""
    cx, cy = center
    rx, ry = radii
    a = math.radians(angle)
    out = []
    for i in range(steps + 1):
        th = math.radians(start + (end - start) * i / steps)
        x, y = rx * math.cos(th), ry * math.sin(th)
        out.append(
            (
                cx + x * math.cos(a) - y * math.sin(a),
                cy + x * math.sin(a) + y * math.cos(a),
            )
        )
    return out


def outline(draw: ScaledDraw, points: Sequence[Point], width: float = 4) -> None:
    """A closed, round-jointed outline (ImageDraw only rounds interior vertices,
    so the path runs one vertex past its start to round the closing one)."""
    draw.line([*points, points[0], points[1]], fill=OUTLINE, width=width, joint="curve")


def outlined(
    draw: ScaledDraw, points: Sequence[Point], fill: Any, width: float = 4
) -> None:
    """A filled polygon with a round-jointed outline (closed)."""
    draw.polygon(points, fill=fill)
    outline(draw, points, width)


def draw_shield(draw: ScaledDraw) -> None:
    """A sounding war horn: ivory, gold bands, its bell opening upwards."""
    spine = bezier([(30, 50), (30, 136), (116, 148), (116, 58)], steps=48)
    left, right = [], []
    for i, (x, y) in enumerate(spine):
        (px, py), (nx, ny) = spine[max(i - 1, 0)], spine[min(i + 1, len(spine) - 1)]
        length = math.hypot(nx - px, ny - py)
        ux, uy = -(ny - py) / length, (nx - px) / length
        half = 4 + 18 * (i / (len(spine) - 1)) ** 1.6
        left.append((x + ux * half, y + uy * half))
        right.append((x - ux * half, y - uy * half))
    shade = [
        (x + (lx - x) * 0.15, y + (ly - y) * 0.15)
        for (x, y), (lx, ly) in zip(spine, left, strict=True)
    ]
    draw.polygon(left + right[::-1], fill=HORN)
    draw.polygon(shade + right[::-1], fill=HORN_SHADE)
    outline(draw, left + right[::-1])
    last = len(spine) - 1
    band_len = round(0.06 * last)
    for i in (round(0.46 * last), round(0.79 * last)):
        band = [left[i], left[i + band_len], right[i + band_len], right[i]]
        outlined(draw, band, GOLD, width=3)
    draw.rounded_rectangle(
        (25, 34, 35, 52), radius=3, fill=GOLD, outline=OUTLINE, width=3
    )
    end_x, end_y = spine[-1]
    rim = (end_x - 25, end_y - 9, end_x + 25, end_y + 9)
    draw.ellipse(rim, fill=GOLD, outline=OUTLINE, width=4)
    draw.ellipse((end_x - 15, end_y - 4, end_x + 15, end_y + 5), fill=HORN_MOUTH)
    for r in (26, 38):
        box = (end_x - r, end_y - 6 - r, end_x + r, end_y - 6 + r)
        draw.arc(box, start=232, end=308, fill=OUTLINE, width=8)
        inset = (box[0] + 1.5, box[1] + 1.5, box[2] - 1.5, box[3] - 1.5)
        draw.arc(inset, start=234, end=306, fill=GOLD_LIGHT, width=5)


def draw_rune_pouch(draw: ScaledDraw) -> None:
    """A gold coin struck with a rune, catching the light."""
    draw.ellipse((22, 24, 138, 140), fill=GOLD_DARK, outline=OUTLINE, width=5)
    draw.ellipse((22, 20, 134, 132), fill=GOLD, outline=OUTLINE, width=5)
    draw.ellipse((36, 34, 120, 118), outline=GOLD_DARK, width=4)
    draw.line((78, 50, 78, 104), fill=GOLD_DARK, width=8)
    draw.line((56, 52, 78, 76, 100, 52), fill=GOLD_DARK, width=8, joint="curve")
    draw.arc((30, 28, 126, 124), start=196, end=252, fill=GOLD_LIGHT, width=6)
    sparkle = [
        (130, 14), (134, 30), (150, 34), (134, 38),
        (130, 54), (126, 38), (110, 34), (126, 30),
    ]  # fmt: skip
    outlined(draw, sparkle, WHITE, width=3)


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
    """A violet squid darting up out of a puff of ink."""
    splat = [(80, 123, 30), (48, 129, 16), (114, 127, 18), (66, 145, 8), (100, 147, 7)]
    for x, y, r in splat:
        draw.ellipse((x - r - 3, y - r - 3, x + r + 3, y + r + 3), fill=OUTLINE)
    for x, y, r in splat:
        draw.ellipse((x - r, y - r, x + r, y + r), fill=INK)
    for x, y, r in ((30, 107, 5), (134, 103, 6), (138, 145, 4)):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=INK, outline=OUTLINE, width=3)
    for x0 in (60, 70, 80, 90, 100):
        x1 = x0 + (x0 - 80) * 0.6
        arm = bezier([(x0, 86), (x0 + 8, 104), (x1 - 8, 120), (x1, 138)])
        draw.line(arm, fill=OUTLINE, width=12, joint="curve")
        draw.line(arm, fill=SQUID_SPOT, width=6, joint="curve")
    for fin in ([(66, 32), (28, 50), (56, 66)], [(94, 32), (132, 50), (104, 66)]):
        outlined(draw, fin, SQUID_DARK)
    mantle = (
        bezier([(80, 10), (100, 28), (110, 50), (108, 70)])
        + ellipse_arc((80, 70), (28, 34), 0, 180)[1:]
        + bezier([(52, 70), (50, 50), (60, 28), (80, 10)])[1:]
    )
    outlined(draw, mantle, SQUID)
    for x, y, r in ((74, 34, 4), (88, 44, 5), (68, 50, 3), (92, 26, 3)):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=SQUID_SPOT)
    for x in (58, 84):
        draw.ellipse((x, 64, x + 18, 86), fill=WHITE, outline=OUTLINE, width=3)
        draw.ellipse((x + 7, 70, x + 16, 82), fill=OUTLINE)
        draw.ellipse((x + 8, 71, x + 11, 74), fill=WHITE)


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
    """A sheet ghost making off with a flask's cork stopper."""
    left, right, top, base, lean = 20, 108, 62, 124, -8
    scallop = (right - left) / 3
    hem: list[Point] = []
    for i in range(3):
        cx = right - scallop * (i + 0.5) + lean
        hem += ellipse_arc((cx, base), (scallop / 2, 12), 0, 180, steps=12)
    body = (
        ellipse_arc(((left + right) / 2, top), ((right - left) / 2, 46), 180, 360) + hem
    )
    draw.polygon(body, fill=GHOST_SHADE)
    inner = [(30 + (x - 30) * 0.86, 24 + (y - 24) * 0.86) for x, y in body]
    draw.polygon(inner, fill=GHOST)
    outline(draw, body)
    cork = [(112, 40), (142, 40), (137, 78), (117, 78)]
    outlined(draw, cork, CORK)
    for x, y in ((122, 50), (132, 58), (124, 68)):
        draw.ellipse((x - 2.5, y - 2.5, x + 2.5, y + 2.5), fill=CORK_DARK)
    draw.ellipse((96, 72, 124, 94), fill=GHOST, outline=OUTLINE, width=4)
    for x in (44, 66):
        draw.ellipse((x, 50, x + 12, 72), fill=GHOST_EYE)
        draw.ellipse((x + 3, 53, x + 7, 59), fill=WHITE)
    draw.ellipse((55, 82, 65, 94), fill=GHOST_EYE)


def draw_toxic_blossom(draw: ScaledDraw) -> None:
    """A purple flytrap gaping on its stem, puffing toxic bubbles."""
    stem = bezier([(80, 100), (68, 122), (90, 136), (80, 154)])
    draw.line(stem, fill=OUTLINE, width=16, joint="curve")
    draw.line(stem, fill=STEM_GREEN, width=10, joint="curve")
    for leaf_center, leaf_angle in (((56, 136), 24), ((106, 128), -28)):
        leaf = ellipse_arc(leaf_center, (22, 9), 0, 360, leaf_angle, steps=32)[:-1]
        outlined(draw, leaf, STEM_GREEN, width=3)
        tip_a, tip_b = ellipse_arc(leaf_center, (18, 0), 0, 180, leaf_angle, steps=1)
        draw.line((tip_a, tip_b), fill=LEAF_DARK, width=3)
    hinge = (80.0, 98.0)
    tips = {-1: (34.0, 34.0), 1: (126.0, 34.0)}
    throat = [hinge, tips[-1]] + bezier([tips[-1], (80, 70), tips[1]])[1:]
    outlined(draw, throat, THROAT)
    for side, (tx, ty) in tips.items():
        hx, hy = hinge
        center = ((hx + tx) / 2, (hy + ty) / 2)
        half = math.hypot(tx - hx, ty - hy) / 2
        angle = math.degrees(math.atan2(ty - hy, tx - hx))
        start = 0 if side > 0 else 180
        a = math.radians(angle)
        inward = (side * math.sin(a), -side * math.cos(a))
        for t in (0.3, 0.45, 0.6, 0.75, 0.9):
            base_a = (hx + (tx - hx) * (t - 0.06), hy + (ty - hy) * (t - 0.06))
            base_b = (hx + (tx - hx) * (t + 0.06), hy + (ty - hy) * (t + 0.06))
            mid = (hx + (tx - hx) * t, hy + (ty - hy) * t)
            apex = (mid[0] + inward[0] * 12, mid[1] + inward[1] * 12)
            outlined(draw, [base_a, apex, base_b], CILIA, width=2)
        lobe = ellipse_arc(center, (half, 26), start, start + 180, angle)
        draw.polygon(lobe, fill=TRAP)
        band = ellipse_arc(center, (half * 0.94, 11), start, start + 180, angle)
        draw.polygon(band, fill=TRAP_INNER)
        outline(draw, lobe)
    for x, y, r in ((80, 52, 8), (92, 32, 6), (70, 22, 5)):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=TOXIC, outline=OUTLINE, width=3)
        draw.ellipse((x - r / 2, y - r / 2, x - r / 6, y - r / 6), fill=WHITE)


def draw_item_box(draw: ScaledDraw) -> None:
    """A closed mystery box seen from above: gold rim, blue faces, a white "?"."""
    front = [(24, 54), (108, 54), (108, 132), (24, 132)]
    side = [(108, 54), (136, 34), (136, 112), (108, 132)]
    top = [(24, 54), (52, 34), (136, 34), (108, 54)]
    outlined(draw, side, BOX_SIDE)
    outlined(draw, front, BOX_FRONT)
    outlined(draw, top, BOX_TOP)
    # gold rim: a lid band and a base band, wrapping the front and the side
    outlined(draw, [(24, 54), (108, 54), (108, 68), (24, 68)], GOLD, width=3)
    outlined(draw, [(108, 54), (136, 34), (136, 48), (108, 68)], GOLD_DARK, width=3)
    outlined(draw, [(24, 120), (108, 120), (108, 132), (24, 132)], GOLD, width=3)
    outlined(draw, [(108, 120), (136, 100), (136, 112), (108, 132)], GOLD_DARK, width=3)
    draw.line([(28, 59), (104, 59)], fill=GOLD_LIGHT, width=3)
    hook = [*ellipse_arc((65, 87), (17, 10), 180, 430, steps=40), (66, 99)]
    ends = (hook[0], hook[-1])
    draw.line(hook, fill=OUTLINE, width=15, joint="curve")
    for x, y in ends:
        draw.ellipse((x - 7.5, y - 7.5, x + 7.5, y + 7.5), fill=OUTLINE)
    draw.line(hook, fill=WHITE, width=9, joint="curve")
    for x, y in ends:
        draw.ellipse((x - 4.5, y - 4.5, x + 4.5, y + 4.5), fill=WHITE)
    draw.ellipse((60, 108, 72, 120), fill=WHITE, outline=OUTLINE, width=3)


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
    "item_box": draw_item_box,
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
