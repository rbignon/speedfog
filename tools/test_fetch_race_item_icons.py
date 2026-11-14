"""Tests for fetch_race_item_icons.py, served from file:// URLs."""

from __future__ import annotations

import hashlib
import tomllib
from pathlib import Path

from fetch_race_item_icons import ICON_SOURCES, fetch_icons, icon_path

CATALOG = (
    Path(__file__).resolve().parent.parent / "data" / "plugins" / "race_items.toml"
)


def _served(tmp_path: Path, name: str, data: bytes) -> tuple[str, str]:
    """A file:// source serving data, with its hash."""
    served = tmp_path / "site" / name
    served.parent.mkdir(exist_ok=True)
    served.write_bytes(data)
    return served.as_uri(), hashlib.sha256(data).hexdigest()


def test_every_catalog_icon_has_exactly_one_source() -> None:
    # StaticModBuilder skips the whole icons overlay when one is missing.
    with CATALOG.open("rb") as fp:
        icons = tomllib.load(fp)["icons"]
    assert set(ICON_SOURCES) == set(icons)


def test_downloads_missing_icons_and_keeps_up_to_date_ones(tmp_path: Path) -> None:
    dest = tmp_path / "plugins"
    sources = {
        "banana": _served(tmp_path, "banana.png", b"banana"),
        "hammer": _served(tmp_path, "hammer.png", b"hammer"),
    }

    assert fetch_icons(dest, sources) == (2, [])
    assert icon_path(dest, "banana").read_bytes() == b"banana"

    # Up to date: not downloaded again, even with its source gone.
    (tmp_path / "site" / "banana.png").unlink()
    assert fetch_icons(dest, sources) == (0, [])


def test_a_file_with_another_hash_is_replaced(tmp_path: Path) -> None:
    dest = tmp_path / "plugins"
    dest.mkdir()
    icon_path(dest, "banana").write_bytes(b"old placeholder")
    sources = {"banana": _served(tmp_path, "banana.png", b"banana")}

    assert fetch_icons(dest, sources) == (1, [])
    assert icon_path(dest, "banana").read_bytes() == b"banana"


def test_a_changed_or_unreachable_source_fails_only_its_icon(tmp_path: Path) -> None:
    dest = tmp_path / "plugins"
    dest.mkdir()
    icon_path(dest, "hammer").write_bytes(b"previous art")
    url, _ = _served(tmp_path, "banana.png", b"re-uploaded banana")
    sources = {
        "banana": (url, hashlib.sha256(b"banana").hexdigest()),
        "hammer": ((tmp_path / "site" / "gone.png").as_uri(), "0" * 64),
        "ink": _served(tmp_path, "ink.png", b"ink"),
    }

    downloaded, problems = fetch_icons(dest, sources)

    assert downloaded == 1
    assert [p.split(":")[0] for p in problems] == ["banana", "hammer"]
    assert "update ICON_SOURCES" in problems[0]
    assert not icon_path(dest, "banana").exists()
    assert icon_path(dest, "hammer").read_bytes() == b"previous art"
    assert icon_path(dest, "ink").read_bytes() == b"ink"
    assert not list(dest.glob("*.part"))
