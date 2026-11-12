"""Tests for the race items catalog and its loader."""

import tomllib
from pathlib import Path

import pytest

from speedfog.race_items import load_race_items_catalog

CATALOG = Path(__file__).parent.parent / "data" / "plugins" / "race_items.toml"


def _raw_items() -> list[dict]:
    with CATALOG.open("rb") as fp:
        return tomllib.load(fp)["item"]


def test_shipped_catalog_matches_the_contract():
    cat = load_race_items_catalog(CATALOG)
    assert len(cat) == 13
    assert cat["hammer"] == {
        "name": "Hammer",
        "icon": "hammer",
        "goods_id": 1450807,
        "use_flag": 1050290107,
        "remove_flag": 1050290207,
        "effect_speffect": 1450827,
        "duration_ms": 15000,
    }
    assert cat["shield"]["use_flag"] is None
    assert cat["shield"]["effect_speffect"] is None
    assert cat["mushroom"]["effect_speffect"] is None
    assert cat["mushroom"]["duration_ms"] == 15000


def test_shipped_catalog_ids_follow_the_numbering():
    for entry in load_race_items_catalog(CATALOG).values():
        n = entry["goods_id"] - 1450800
        assert entry["use_flag"] in (None, 1050290100 + n)
        assert entry["remove_flag"] == 1050290200 + n
        assert entry["effect_speffect"] in (None, entry["goods_id"] + 20)


def test_duration_matches_the_speffect_that_carries_it():
    for item in _raw_items():
        duration = item.get("duration_s")
        effect = item.get("effect")
        marker_endurance = (
            item.get("marker", {}).get("fields", {}).get("effectEndurance")
        )
        if effect is not None and "effectEndurance" in effect["fields"]:
            assert effect["fields"]["effectEndurance"] == duration, item["key"]
        elif marker_endurance is not None:
            assert marker_endurance == duration, item["key"]
        elif item["key"] == "ink":
            assert duration == 15
        else:
            assert duration is None, item["key"]


def _write_catalog(
    tmp_path: Path, *items: str, icons: str = "[icons]\nfoo = 1\n"
) -> Path:
    path = tmp_path / "cat.toml"
    path.write_text(icons + "".join(items), encoding="utf-8")
    return path


def _item(key="a", goods_id=1450800, extra="", use_flag=None, remove_flag=1050290200):
    lines = [
        "\n[[item]]",
        f'key = "{key}"',
        'icon = "foo"',
        'name = "A"',
        f"goods_id = {goods_id}",
        f"remove_flag = {remove_flag}",
    ]
    if use_flag is not None:
        lines.append(f"use_flag = {use_flag}")
    return "\n".join(lines) + "\n" + extra


def test_loader_rejects_duplicate_goods_id(tmp_path):
    path = _write_catalog(tmp_path, _item("a"), _item("b"))
    with pytest.raises(ValueError, match="duplicate goods_id"):
        load_race_items_catalog(path)


def test_loader_rejects_id_outside_reserved_range(tmp_path):
    path = _write_catalog(tmp_path, _item(goods_id=1450900))
    with pytest.raises(ValueError, match="outside"):
        load_race_items_catalog(path)


def test_loader_rejects_use_flag_without_marker(tmp_path):
    path = _write_catalog(tmp_path, _item(use_flag=1050290100))
    with pytest.raises(ValueError, match="go together"):
        load_race_items_catalog(path)


def test_loader_rejects_marker_without_use_flag(tmp_path):
    marker = "marker = { template = 10600, fields = {} }\n"
    path = _write_catalog(tmp_path, _item(extra=marker))
    with pytest.raises(ValueError, match="go together"):
        load_race_items_catalog(path)


def test_loader_rejects_unknown_icon(tmp_path):
    path = _write_catalog(tmp_path, _item(), icons="[icons]\nbar = 1\n")
    with pytest.raises(ValueError, match="unknown icon"):
        load_race_items_catalog(path)
