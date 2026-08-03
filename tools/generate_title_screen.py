#!/usr/bin/env python3
"""Generate data/title_screen.png (2532x1532), the SpeedFog title artwork.

Identity from speedfog-racing's og-image.svg: a checkered finish-line band
dissolving into fog, palette #0f1923 / #c8a44e / #e8e6e1 with blue fog wisps.
Elden Ring anchors: golden EB Garamond wordmark + fractured golden ring.

The output is committed; re-run this script only to change the design, then
regenerate the overlay (see docs/title-screen.md). Standalone from the project
venv: requires Pillow and the EB Garamond fonts (Debian: fonts-ebgaramond-extra).
The random stream is seeded, so the output is reproducible on a fixed
environment (generated with Pillow 11.1.0 + fonts-ebgaramond-extra on Debian
13); other Pillow/FreeType/font versions may produce byte-different files.
"""

import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 2532, 1532
CX = W // 2
FONT_PATH = "/usr/share/fonts/opentype/ebgaramond/EBGaramondSC08-Regular.otf"


def composite_color(base: Image.Image, color: tuple, mask: Image.Image) -> Image.Image:
    return Image.composite(Image.new("RGB", (W, H), color), base, mask)


def main() -> int:
    if not Path(FONT_PATH).exists():
        print(
            f"Error: {FONT_PATH} not found (Debian: apt install fonts-ebgaramond-extra)",
            file=sys.stderr,
        )
        return 1

    random.seed(11)

    img = Image.new("RGB", (W, H), (0, 0, 0))

    # --- deep blue-grey atmosphere (og-image background tone), subtle ---
    atmo = Image.new("L", (W, H), 0)
    ad = ImageDraw.Draw(atmo)
    ad.ellipse([CX - 1100, 60, CX + 1100, 1450], fill=70)
    atmo = atmo.filter(ImageFilter.GaussianBlur(300))
    img = composite_color(img, (28, 42, 63), atmo)  # #1c2a3f

    # --- soft golden glow behind ring/wordmark ---
    glow = Image.new("L", (W, H), 0)
    gd = ImageDraw.Draw(glow)
    gd.ellipse([CX - 850, 140, CX + 850, 1250], fill=54)
    glow = glow.filter(ImageFilter.GaussianBlur(260))
    img = composite_color(img, (100, 78, 36), glow)

    # --- fractured golden ring ---
    ring = Image.new("L", (W, H), 0)
    rd = ImageDraw.Draw(ring)
    ring_cy = 690
    r_out = 545
    a = 0.0
    while a < 360.0:
        seg = random.uniform(20, 75)
        gap = random.uniform(1.5, 6) if random.random() < 0.45 else 0.0
        rd.arc(
            [CX - r_out, ring_cy - r_out, CX + r_out, ring_cy + r_out],
            start=a,
            end=a + seg,
            fill=200,
            width=15,
        )
        a += seg + gap
    r_in = 420
    rd.arc(
        [CX - r_in - 40, ring_cy - r_in + 55, CX + r_in - 40, ring_cy + r_in + 55],
        start=200,
        end=340,
        fill=145,
        width=9,
    )
    rd.line(
        [CX + 28, ring_cy - r_out - 40, CX - 18, ring_cy + r_out + 40],
        fill=115,
        width=6,
    )

    ring_soft = ring.filter(ImageFilter.GaussianBlur(6))
    ring_glow = ring.filter(ImageFilter.GaussianBlur(42))
    img = composite_color(img, (140, 108, 44), ring_glow)
    img = composite_color(img, (212, 178, 106), ring_soft)

    # --- checkered finish-line band, dissolving into fog toward the right.
    # The band's two rows end at y=1300, above the zone where the game draws
    # the "Press any button" prompt, which stays near-black.
    CELL = 96
    BAND_ROWS = 2
    band_y = 1108
    band = Image.new("L", (W, H), 0)
    bd = ImageDraw.Draw(band)
    band_dark = Image.new("L", (W, H), 0)
    bdd = ImageDraw.Draw(band_dark)
    SKEW = -0.20  # px of x-shift per px of y: cells lean like a waved flag

    def cell_quad(draw, x0, y0, alpha):
        sk0 = SKEW * (y0 - band_y)
        sk1 = SKEW * (y0 + CELL - band_y)
        draw.polygon(
            [
                (x0 + sk0, y0),
                (x0 + CELL + sk0, y0),
                (x0 + CELL + sk1, y0 + CELL),
                (x0 + sk1, y0 + CELL),
            ],
            fill=alpha,
        )

    def sub_quads(draw, x0, y0, t, base_alpha):
        """Shatter a cell into 3x3 sub-quads: chunks fall out and drift as t grows."""
        n = 3
        s = CELL / n
        cs = s * 0.88  # slight inset so cracks show between chunks
        for i in range(n):
            for j in range(n):
                if random.random() < t * 1.15:
                    continue
                alpha = int(base_alpha * (1.0 - t * 0.8) + random.uniform(-10, 10))
                if alpha <= 0:
                    continue
                drift = t * random.uniform(0, 34)
                sx = x0 + i * s + drift
                sy = y0 + j * s - drift * random.uniform(0.2, 0.8)
                sk = SKEW * (sy - band_y)
                draw.polygon(
                    [
                        (sx + sk, sy),
                        (sx + cs + sk, sy),
                        (sx + cs + sk, sy + cs),
                        (sx + sk, sy + cs),
                    ],
                    fill=alpha,
                )

    for row in range(BAND_ROWS):
        for col in range(26):
            x0 = 60 + col * CELL
            y0 = band_y + row * CELL
            light = (row + col) % 2 == 1
            # dissolve ramp: whole cells to the left, shattering chunks, then dust
            t = max(0.0, min(1.0, (x0 - 700) / 800))
            draw, base = (bd, 135) if light else (bdd, 60)
            if t < 0.12:
                cell_quad(
                    draw, x0, y0, int(base + random.uniform(-base * 0.09, base * 0.09))
                )
            else:
                sub_quads(draw, x0, y0, t, base)

    band = band.filter(ImageFilter.GaussianBlur(3))
    band_dark = band_dark.filter(ImageFilter.GaussianBlur(3))
    img = composite_color(img, (48, 66, 92), band_dark)
    img = composite_color(img, (232, 230, 225), band)  # #e8e6e1

    # --- fog wisps eating the band's right end (og-image blues + a gold breath) ---
    fog = Image.new("L", (W, H), 0)
    fd = ImageDraw.Draw(fog)
    for cx, cy, rx, ry, a_ in [
        (1300, 1200, 280, 75, 80),
        (1650, 1240, 320, 85, 95),
        (2000, 1160, 300, 95, 85),
        (2320, 1240, 260, 80, 70),
        (1500, 1330, 240, 55, 65),
    ]:
        fd.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=a_)
    fog = fog.filter(ImageFilter.GaussianBlur(75))
    img = composite_color(img, (45, 64, 96), fog)  # #2d4060

    fog2 = Image.new("L", (W, H), 0)
    f2 = ImageDraw.Draw(fog2)
    for cx, cy, rx, ry, a_ in [
        (1620, 1190, 200, 45, 55),
        (2050, 1240, 180, 40, 45),
    ]:
        f2.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=a_)
    fog2 = fog2.filter(ImageFilter.GaussianBlur(50))
    img = composite_color(img, (90, 122, 154), fog2)  # #5a7a9a

    foggold = Image.new("L", (W, H), 0)
    fg = ImageDraw.Draw(foggold)
    fg.ellipse([1050, 1080, 1650, 1300], fill=80)
    foggold = foggold.filter(ImageFilter.GaussianBlur(65))
    img = composite_color(img, (200, 164, 78), foggold)  # #c8a44e

    # --- SPEEDFOG wordmark ---
    TEXT = "SPEEDFOG"
    TRACKING = 36

    def text_layer(size: int) -> Image.Image:
        font = ImageFont.truetype(FONT_PATH, size)
        widths = [font.getbbox(ch)[2] - font.getbbox(ch)[0] for ch in TEXT]
        total = sum(widths) + TRACKING * (len(TEXT) - 1)
        layer = Image.new("L", (W, H), 0)
        ld = ImageDraw.Draw(layer)
        x = CX - total // 2
        baseline_y = 745
        for ch, w in zip(TEXT, widths, strict=True):
            bbox = font.getbbox(ch)
            ld.text((x - bbox[0], baseline_y), ch, font=font, anchor="ls", fill=255)
            x += w + TRACKING
        return layer

    size = 420
    mask = text_layer(size)
    bbox = mask.getbbox()
    size = int(size * 2050 / (bbox[2] - bbox[0]))
    mask = text_layer(size)

    grad = Image.new("RGB", (W, H))
    top, mid, bot = (246, 228, 178), (216, 178, 104), (146, 104, 44)
    gb = mask.getbbox()
    gdraw = ImageDraw.Draw(grad)
    for y in range(H):
        if gb[1] <= y <= gb[3]:
            t = (y - gb[1]) / max(1, gb[3] - gb[1])
            if t < 0.55:
                u = t / 0.55
                c = tuple(int(p + (q - p) * u) for p, q in zip(top, mid, strict=True))
            else:
                u = (t - 0.55) / 0.45
                c = tuple(int(p + (q - p) * u) for p, q in zip(mid, bot, strict=True))
        else:
            c = mid
        gdraw.line([(0, y), (W, y)], fill=c)

    text_glow = mask.filter(ImageFilter.GaussianBlur(26))
    img = composite_color(img, (150, 112, 48), text_glow)
    img = Image.composite(grad, img, mask)

    # --- thin gold accent line under the wordmark (og-image accent) ---
    accent = Image.new("L", (W, H), 0)
    acd = ImageDraw.Draw(accent)
    acd.rectangle([CX - 400, 962, CX + 400, 966], fill=155)
    accent = accent.filter(ImageFilter.GaussianBlur(1))
    img = composite_color(img, (200, 164, 78), accent)  # #c8a44e

    # --- vignette to pure black (sprite must blend into the black title screen) ---
    vign = Image.new("L", (W, H), 0)
    vd = ImageDraw.Draw(vign)
    vd.ellipse([-330, -240, W + 330, H + 240], fill=255)
    vign = vign.filter(ImageFilter.GaussianBlur(230))
    img = Image.composite(img, Image.new("RGB", (W, H), (0, 0, 0)), vign)

    out = Path(__file__).resolve().parent.parent / "data" / "title_screen.png"
    img.save(out, optimize=True)
    print(f"saved {img.size} -> {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
