#!/usr/bin/env python3
"""Generate data/title_screen_overlay.png (2532x1532 RGBA, mostly transparent).

The overlay is alpha-composited by GamePatcher's TitleScreenPatcher onto the
vanilla title screen artwork (MENU_Title_EldenRing_01): a sober SpeedFog badge
at the top right of the ELDEN RING logo, in the erased TM glyph's area. Identity
from speedfog-racing's og-image.svg: Inter Bold wordmark in the site's gold
#c8a44e, and a small waving checkered flag dissolving into fog toward the
right (progressive blur + alpha fade). The badge also paints an opaque black
patch over the vanilla TM glyph, whose spot it takes.

The output is committed; re-run this script only to change the design, then
regenerate the overlay (see docs/title-screen.md). Standalone from the project
venv: requires Pillow and the Inter fonts (Debian: fonts-inter). Deterministic,
but glyph rasterization and blur vary across Pillow/FreeType/font versions, so
a different environment may produce a byte-different (visually identical) file.
"""

import math
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

W, H = 2532, 1532
INTER_BOLD = "/usr/share/fonts/opentype/inter/Inter-Bold.otf"


def main() -> int:
    if not Path(INTER_BOLD).exists():
        print(
            f"Error: {INTER_BOLD} not found (Debian: apt install fonts-inter)",
            file=sys.stderr,
        )
        return 1

    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # --- badge geometry: anchored top-right, in the erased TM glyph's area ---
    TEXT = "SpeedFog"
    font = ImageFont.truetype(INTER_BOLD, 118)
    bbox = font.getbbox(TEXT)
    text_w = bbox[2] - bbox[0]
    right_edge = 2510
    baseline_y = 525
    text_x = right_edge - text_w

    # erase the vanilla TM glyph: the badge takes its place
    pd = ImageDraw.Draw(overlay)
    pd.rectangle([2445, 495, 2520, 555], fill=(0, 0, 0, 255))

    # the wordmark, in the site's gold #c8a44e
    txt = Image.new("L", (W, H), 0)
    td = ImageDraw.Draw(txt)
    td.text((text_x, baseline_y), TEXT, font=font, anchor="ls", fill=255)
    overlay.paste(Image.new("RGBA", (W, H), (200, 164, 78, 255)), (0, 0), txt)

    # mini waving checkered flag to the left of the text, dissolving into fog
    # toward the right: progressive blur plus alpha fade (fog reads as the
    # simultaneous loss of sharpness and opacity)
    FCELL = 30
    FROWS, FCOLS = 3, 5
    pad = 40  # room for the blur to bleed outside the checker
    core_w = FCOLS * FCELL
    core_h = FROWS * FCELL
    flag_w = core_w + pad * 2
    flag_h = core_h + pad * 2
    flag = Image.new("RGBA", (flag_w, flag_h), (0, 0, 0, 0))
    fd = ImageDraw.Draw(flag)
    for col in range(FCOLS):
        wave = 7 * math.sin(col * 0.9)
        for row in range(FROWS):
            light = (row + col) % 2 == 1
            color = (232, 230, 225, 235) if light else (22, 32, 48, 215)
            x0 = pad + col * FCELL
            y0 = pad + row * FCELL + wave
            fd.rectangle([x0, y0, x0 + FCELL - 1, y0 + FCELL - 1], fill=color)

    def hramp(x0: int, x1: int, a0: int, a1: int) -> Image.Image:
        """Horizontal ramp mask: a0 left of x0, a1 right of x1, linear between."""
        ramp = Image.new("L", (flag_w, flag_h), 0)
        rd = ImageDraw.Draw(ramp)
        for x in range(flag_w):
            if x <= x0:
                v = a0
            elif x >= x1:
                v = a1
            else:
                v = int(a0 + (a1 - a0) * (x - x0) / (x1 - x0))
            rd.line([(x, 0), (x, flag_h)], fill=v)
        return ramp

    blur_mid = flag.filter(ImageFilter.GaussianBlur(2.5))
    blur_far = flag.filter(ImageFilter.GaussianBlur(6))
    flag = Image.composite(blur_mid, flag, hramp(pad + 30, pad + 90, 0, 255))
    flag = Image.composite(blur_far, flag, hramp(pad + 90, pad + core_w + 20, 0, 255))
    r, g, b, a = flag.split()
    a = ImageChops.multiply(a, hramp(pad + 35, pad + core_w + 15, 255, 70))
    flag = Image.merge("RGBA", (r, g, b, a))

    flag = flag.rotate(-8, resample=Image.BICUBIC, expand=False)
    # anchor the checker itself (pad-independent position)
    checker_x = text_x - core_w - 42
    checker_top = baseline_y - 94
    overlay.alpha_composite(flag, (checker_x - pad, checker_top - pad))

    out = Path(__file__).resolve().parent.parent / "data" / "title_screen_overlay.png"
    overlay.save(out, optimize=True)
    print(f"saved {overlay.size} -> {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
