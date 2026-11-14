"""Tests for the race items catalog and its loader."""

import tomllib
from pathlib import Path

import pytest

from speedfog.race_items import load_race_items_catalog

CATALOG_PATH = Path(__file__).parent.parent / "data" / "plugins" / "race_items.toml"


def _raw_items() -> list[dict]:
    with CATALOG_PATH.open("rb") as fp:
        return tomllib.load(fp)["item"]


def test_shipped_catalog_matches_the_contract():
    catalog = load_race_items_catalog(CATALOG_PATH)
    assert set(catalog) == {"goods", "items"}
    assert catalog["goods"]["race_item"] == {
        "goods_id": 1450801,
        "use_flag": 1050290101,
        "remove_flag": 1050290201,
        "icon": "item_box",
        "icon_id": 60396,
    }
    assert catalog["goods"]["shield"] == {
        "goods_id": 1450800,
        "use_flag": None,
        "remove_flag": 1050290200,
        "icon": "shield",
        "icon_id": 60385,
    }
    assert len(catalog["items"]) == 13
    assert catalog["items"]["hammer"] == {
        "name": "Hammer",
        "icon": "hammer",
        "icon_id": 60390,
        "goods": "race_item",
        "effect_speffect": 1450827,
        "duration_ms": 15000,
    }
    assert catalog["items"]["shield"] == {
        "name": "Shield",
        "icon": "shield",
        "icon_id": 60385,
        "goods": "shield",
        "effect_speffect": None,
        "duration_ms": None,
    }


def test_shipped_catalog_ids_follow_the_numbering():
    catalog = load_race_items_catalog(CATALOG_PATH)
    for goods in catalog["goods"].values():
        n = goods["goods_id"] - 1450800
        assert goods["use_flag"] in (None, 1050290100 + n)
        assert goods["remove_flag"] == 1050290200 + n
    effects = [
        item["effect_speffect"]
        for item in catalog["items"].values()
        if item["effect_speffect"] is not None
    ]
    assert len(effects) == len(set(effects))
    assert all(1450820 <= effect < 1450850 for effect in effects)


# Vanilla SpEffects an effect row cycles, with their effectEndurance read from
# the 1.17 regulation (re-check after a game patch): 102360 is the Viridian
# Hidden Tear. A cycled row missing here adds no tail: 4101 (Lightning's slow
# walk) lasts 0.1 s.
VANILLA_ENDURANCE = {102360: 15}


def test_duration_matches_the_speffect_that_carries_it():
    for item in _raw_items():
        duration = item.get("duration_s")
        fields = item.get("effect", {}).get("fields", {})
        if "effectEndurance" in fields:
            # A cycled vanilla row lasts its own endurance after its last
            # application.
            tail = VANILLA_ENDURANCE.get(fields.get("cycleOccurrenceSpEffectId"), 0)
            assert fields["effectEndurance"] + tail == duration, item["key"]
        elif item["key"] == "ink":
            # Overlay only: no SpEffect row.
            assert duration == 15
        else:
            assert duration is None, item["key"]


def test_mushroom_lasts_its_row_plus_the_vanilla_tail():
    mushroom = load_race_items_catalog(CATALOG_PATH)["items"]["mushroom"]
    raw = next(item for item in _raw_items() if item["key"] == "mushroom")
    fields = raw["effect"]["fields"]
    cycled = fields["cycleOccurrenceSpEffectId"]
    assert cycled in VANILLA_ENDURANCE
    assert mushroom["effect_speffect"] == raw["effect"]["id"]
    assert (
        mushroom["duration_ms"]
        == (fields["effectEndurance"] + VANILLA_ENDURANCE[cycled]) * 1000
    )


def _write_catalog(
    tmp_path: Path, *entries: str, icons: str = "[icons]\nfoo = 60385\n"
) -> Path:
    path = tmp_path / "cat.toml"
    path.write_text(icons + "".join(entries), encoding="utf-8")
    return path


MARKER = "marker = { template = 10600, fields = {} }\n"


def _goods(
    key="g",
    goods_id=1450800,
    extra="",
    use_flag=None,
    remove_flag=1050290200,
    icon="foo",
):
    lines = [
        "\n[[goods]]",
        f'key = "{key}"',
        f'icon = "{icon}"',
        'name = "G"',
        'info = "I"',
        'caption = "C"',
        f"goods_id = {goods_id}",
        f"remove_flag = {remove_flag}",
    ]
    if use_flag is not None:
        lines.append(f"use_flag = {use_flag}")
    return "\n".join(lines) + "\n" + extra


def _usable_goods(key="g", goods_id=1450801):
    n = goods_id - 1450800
    return _goods(
        key,
        goods_id,
        extra=MARKER,
        use_flag=1050290100 + n,
        remove_flag=1050290200 + n,
    )


def _item(key="a", goods="g", icon="foo", extra=""):
    lines = [
        "\n[[item]]",
        f'key = "{key}"',
        f'goods = "{goods}"',
        f'icon = "{icon}"',
        'name = "A"',
    ]
    return "\n".join(lines) + "\n" + extra


def _effect(effect_id):
    return f"effect = {{ id = {effect_id}, template = 10600, fields = {{}} }}\n"


def test_loader_rejects_duplicate_goods_id(tmp_path):
    path = _write_catalog(tmp_path, _goods("a"), _goods("b"))
    with pytest.raises(ValueError, match="duplicate goods_id"):
        load_race_items_catalog(path)


def test_loader_rejects_id_outside_reserved_range(tmp_path):
    path = _write_catalog(tmp_path, _goods(goods_id=1450900))
    with pytest.raises(ValueError, match="outside"):
        load_race_items_catalog(path)


def test_loader_rejects_use_flag_without_marker(tmp_path):
    path = _write_catalog(tmp_path, _goods(use_flag=1050290100))
    with pytest.raises(ValueError, match="go together"):
        load_race_items_catalog(path)


def test_loader_rejects_marker_without_use_flag(tmp_path):
    path = _write_catalog(tmp_path, _goods(extra=MARKER))
    with pytest.raises(ValueError, match="go together"):
        load_race_items_catalog(path)


@pytest.mark.parametrize(
    "entries",
    [
        pytest.param((_goods(icon="nope"),), id="goods"),
        pytest.param((_usable_goods(), _item(icon="nope")), id="item"),
    ],
)
def test_loader_rejects_unknown_icon(tmp_path, entries):
    path = _write_catalog(tmp_path, *entries)
    with pytest.raises(ValueError, match="unknown icon"):
        load_race_items_catalog(path)


def test_loader_rejects_icon_id_outside_reserved_range(tmp_path):
    path = _write_catalog(tmp_path, _goods(), icons="[icons]\nfoo = 60400\n")
    with pytest.raises(ValueError, match="icon id"):
        load_race_items_catalog(path)


def test_loader_rejects_duplicate_icon_id(tmp_path):
    path = _write_catalog(
        tmp_path, _goods(), icons="[icons]\nfoo = 60385\nbar = 60385\n"
    )
    with pytest.raises(ValueError, match="share id"):
        load_race_items_catalog(path)


def test_loader_rejects_an_item_on_an_unknown_goods(tmp_path):
    path = _write_catalog(tmp_path, _usable_goods(), _item(goods="nope"))
    with pytest.raises(ValueError, match="unknown goods 'nope'"):
        load_race_items_catalog(path)


def test_loader_rejects_an_effect_on_a_goods_without_use(tmp_path):
    # The Shield goods is never used: nothing would apply the effect.
    path = _write_catalog(
        tmp_path, _goods("shield"), _item(goods="shield", extra=_effect(1450820))
    )
    with pytest.raises(ValueError, match="cannot be used"):
        load_race_items_catalog(path)


def test_loader_rejects_duplicate_effect_ids(tmp_path):
    path = _write_catalog(
        tmp_path,
        _usable_goods(),
        _item("a", extra=_effect(1450820)),
        _item("b", extra=_effect(1450820)),
    )
    with pytest.raises(ValueError, match="duplicate effect id"):
        load_race_items_catalog(path)


def test_loader_rejects_an_effect_outside_the_effect_range(tmp_path):
    # 1450819 is a valid param id, but below the item effect range.
    path = _write_catalog(tmp_path, _usable_goods(), _item(extra=_effect(1450819)))
    with pytest.raises(ValueError, match="effect id 1450819 outside"):
        load_race_items_catalog(path)
