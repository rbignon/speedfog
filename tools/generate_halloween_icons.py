#!/usr/bin/env python3
"""Generate placeholder Halloween item icons (Golden Seed, Larval Tear, Sacred Tear).

Standalone from the project venv: requires Pillow only. Draws simple flat
shapes (a pumpkin, a gummy worm, a gumdrop) on transparent 160x160 canvases; the real
art is authored by the user (Smithbox export + editing) and simply
overwrites these files. Outputs are committed; re-run this script only to
change the placeholder design, then re-run tools/bootstrap.py (or
StaticModBuilder directly) to rebuild data/mods/speedfog-halloween/.
Outputs live in data/plugins/ next to the halloween catalogues.
See docs/plugins/halloween-icons.md.
"""

from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 160
PLUGINS_DIR = Path(__file__).resolve().parent.parent / "data" / "plugins"

PUMPKIN_ORANGE = (222, 118, 28, 255)
PUMPKIN_DARK = (160, 78, 12, 255)
STEM_GREEN = (96, 128, 48, 255)
WORM_RED = (214, 60, 84, 255)
WORM_YELLOW = (238, 196, 80, 255)
GUMDROP_TEAL = (72, 190, 168, 255)
GUMDROP_SHINE = (232, 250, 244, 255)
OUTLINE = (40, 24, 16, 255)


def draw_pumpkin_seed(draw: ImageDraw.ImageDraw) -> None:
    """A plump pumpkin with a stem, reading as a seed-sized treat."""
    draw.ellipse((20, 48, 140, 140), fill=PUMPKIN_ORANGE, outline=OUTLINE, width=4)
    for x0, x1 in ((44, 76), (84, 116)):
        draw.arc((x0, 48, x1, 140), start=270, end=90, fill=PUMPKIN_DARK, width=4)
        draw.arc((x0, 48, x1, 140), start=90, end=270, fill=PUMPKIN_DARK, width=4)
    draw.rectangle((72, 26, 88, 54), fill=STEM_GREEN, outline=OUTLINE, width=3)


def draw_gummy_worm(draw: ImageDraw.ImageDraw) -> None:
    """A two-tone segmented worm in an S curve."""
    segments = [
        ((16, 84, 64, 132), WORM_RED),
        ((44, 60, 92, 108), WORM_YELLOW),
        ((72, 36, 120, 84), WORM_RED),
        ((100, 24, 148, 72), WORM_YELLOW),
    ]
    for box, color in segments:
        draw.ellipse(box, fill=color, outline=OUTLINE, width=4)
    draw.ellipse((110, 34, 122, 46), fill=OUTLINE)  # eye


def draw_sacred_gumdrop(draw: ImageDraw.ImageDraw) -> None:
    """A dome-shaped sugar gumdrop with a shine, sitting on a flat base."""
    draw.pieslice(
        (28, 32, 132, 200),
        start=180,
        end=360,
        fill=GUMDROP_TEAL,
        outline=OUTLINE,
        width=4,
    )
    draw.rectangle((28, 112, 132, 132), fill=GUMDROP_TEAL, outline=OUTLINE, width=4)
    draw.ellipse((52, 52, 76, 84), fill=GUMDROP_SHINE)
    for x, y in ((92, 60), (64, 96), (100, 100)):
        draw.ellipse((x, y, x + 8, y + 8), fill=GUMDROP_SHINE)


def main() -> None:
    for name, painter in (
        ("halloween_icon_pumpkin_seed.png", draw_pumpkin_seed),
        ("halloween_icon_gummy_worm.png", draw_gummy_worm),
        ("halloween_icon_sacred_gumdrop.png", draw_sacred_gumdrop),
    ):
        img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        painter(ImageDraw.Draw(img))
        out = PLUGINS_DIR / name
        img.save(out)
        print(f"wrote {out}")


if __name__ == "__main__":
    main()
