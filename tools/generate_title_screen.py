#!/usr/bin/env python3
"""Generate data/title_screen_overlay.png (2532x1532 RGBA, mostly transparent).

The overlay is alpha-composited by GamePatcher's TitleScreenPatcher onto the
vanilla title screen artwork (MENU_Title_EldenRing_01): a sober SpeedFog badge
at the top right of the ELDEN RING logo, slightly overlapping the G. Identity
from speedfog-racing's og-image.svg: Inter Bold wordmark in off-white #e8e6e1,
gold accent line #c8a44e, and a small waving checkered flag. The badge also
paints an opaque black patch over the vanilla TM glyph, whose spot it takes.

CAUTION: the committed data/title_screen_overlay.png has been hand-polished
after generation (commit 920905a) and is the authoritative artwork; running
this script OVERWRITES it with the script's own (older) design. Use it as a
starting point for a redesign, not to reproduce the current file. After any
change, regenerate the overlay (see docs/title-screen.md). Standalone from
the project venv: requires Pillow and the Inter fonts (Debian: fonts-inter).
"""

import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

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

    # --- badge geometry: anchored top-right, slightly overlapping the G ---
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

    # glow behind the text (og-image textGlow, scaled)
    glow = Image.new("L", (W, H), 0)
    gd = ImageDraw.Draw(glow)
    gd.text((text_x, baseline_y), TEXT, font=font, anchor="ls", fill=170)
    glow = glow.filter(ImageFilter.GaussianBlur(10))
    overlay.paste(
        Image.new("RGBA", (W, H), (232, 230, 225, 255)),
        (0, 0),
        glow.point(lambda v: int(v * 0.55)),
    )

    # thin gold accent line under the text, behind it so descenders stay on
    # top; its right end runs over the G's top serif (the requested subtle
    # superposition with the vanilla logo)
    line = Image.new("L", (W, H), 0)
    ld = ImageDraw.Draw(line)
    ld.rectangle([text_x - 220, baseline_y + 26, right_edge, baseline_y + 30], fill=110)
    line = line.filter(ImageFilter.GaussianBlur(1))
    overlay.paste(Image.new("RGBA", (W, H), (200, 164, 78, 255)), (0, 0), line)

    # the text itself, #e8e6e1
    txt = Image.new("L", (W, H), 0)
    td = ImageDraw.Draw(txt)
    td.text((text_x, baseline_y), TEXT, font=font, anchor="ls", fill=255)
    overlay.paste(Image.new("RGBA", (W, H), (232, 230, 225, 255)), (0, 0), txt)

    # mini waving checkered flag glyph to the left of the text
    FCELL = 30
    FROWS, FCOLS = 3, 5
    pad = 24
    flag_w = FCOLS * FCELL + pad * 2
    flag_h = FROWS * FCELL + pad * 2
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
    flag = flag.rotate(-8, resample=Image.BICUBIC, expand=False)
    flag = flag.filter(ImageFilter.GaussianBlur(0.6))
    flag_x = text_x - flag_w - 18
    flag_top = baseline_y - 118
    overlay.alpha_composite(flag, (flag_x, int(flag_top)))

    out = Path(__file__).resolve().parent.parent / "data" / "title_screen_overlay.png"
    overlay.save(out, optimize=True)
    print(f"saved {overlay.size} -> {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
