"""Tests for import_race_item_icons.py.

Skipped where Pillow is missing (it is not a project dependency): run them with
uv run --with pillow pytest tools/test_import_race_item_icons.py.
"""

import sys
from pathlib import Path

import pytest

pytest.importorskip("PIL")

from PIL import Image  # noqa: E402

sys.path.insert(0, str(Path(__file__).parent))

from import_race_item_icons import reframe  # noqa: E402


def _solid_box(image: Image.Image) -> tuple[int, int, int, int]:
    box = image.getchannel("A").point(lambda a: 255 if a > 128 else 0).getbbox()
    assert box is not None
    return box


def test_reframe_centres_an_off_centre_drawing_and_fits_its_longer_side() -> None:
    source = Image.new("RGBA", (400, 300), (0, 0, 0, 0))
    source.paste((200, 40, 40, 255), (20, 30, 120, 230))  # 100 wide, 200 tall

    left, top, right, bottom = _solid_box(reframe(source, size=200, fill=0.5))

    assert bottom - top == pytest.approx(100, abs=2)
    assert right - left == pytest.approx(50, abs=2)
    assert (left + right) / 2 == pytest.approx(100, abs=1)
    assert (top + bottom) / 2 == pytest.approx(100, abs=1)


def test_reframe_measures_the_drawing_without_its_faint_glow() -> None:
    source = Image.new("RGBA", (300, 300), (0, 0, 0, 0))
    source.paste((255, 255, 255, 4), (0, 0, 130, 130))  # glow around the drawing
    source.paste((40, 40, 200, 255), (10, 10, 110, 110))

    framed = reframe(source, size=200, fill=0.5)

    left, top, right, bottom = _solid_box(framed)
    assert (right - left, bottom - top) == pytest.approx((100, 100), abs=2)
    # The glow is kept, it only stays out of the measure.
    assert framed.getpixel((165, 100))[3] > 0


def test_reframe_shrinks_the_drawing_rather_than_cut_a_wide_glow() -> None:
    source = Image.new("RGBA", (300, 300), (0, 0, 0, 0))
    source.paste((255, 255, 255, 4), (0, 100, 300, 200))  # glow band, 3x the drawing
    source.paste((40, 40, 200, 255), (100, 100, 200, 200))

    framed = reframe(source, size=200, fill=0.9)

    left, _top, right, _bottom = _solid_box(framed)
    assert right - left < 100  # 180 without the glow
    alpha = framed.getchannel("A")
    assert alpha.getpixel((0, 100)) == 0 and alpha.getpixel((199, 100)) == 0
    assert alpha.getpixel((3, 100)) > 0 and alpha.getpixel((196, 100)) > 0


def test_reframe_returns_none_for_an_empty_image() -> None:
    assert reframe(Image.new("RGBA", (64, 64), (0, 0, 0, 0))) is None
