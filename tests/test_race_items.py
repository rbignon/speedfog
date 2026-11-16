"""Tests for the race items catalog and its loader."""

import tomllib
from pathlib import Path

import pytest

from speedfog.race_items import ICON_ID_RANGE, load_race_items_catalog

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
    assert len(catalog["items"]) == 20
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
# Hidden Tear. 4101 (Lightning's slow walk) lasts 0.1 s, a negligible tail.
VANILLA_ENDURANCE = {102360: 15, 4101: 0}


MEMORY_EFFECTS = {"vanish", "unseen_foes", "slow_mo", "mini", "giant", "camera"}


def test_duration_matches_the_speffect_that_carries_it():
    for item in _raw_items():
        duration = item.get("duration_s")
        fields = item.get("effect", {}).get("fields", {})
        if "effectEndurance" in fields:
            # A cycled vanilla row lasts its own endurance after its last
            # application.
            tail = 0
            cycled = fields.get("cycleOccurrenceSpEffectId")
            if cycled is not None:
                assert cycled in VANILLA_ENDURANCE, item["key"]
                tail = VANILLA_ENDURANCE[cycled]
            assert fields["effectEndurance"] + tail == duration, item["key"]
        elif item["key"] == "ink":
            # Overlay only: no SpEffect row.
            assert duration == 30
        elif item["key"] in MEMORY_EFFECTS:
            # Applied by the racing mod through memory writes: no SpEffect row.
            assert duration == 15, item["key"]
        else:
            assert duration is None, item["key"]


def test_every_icon_id_is_in_range_and_unique():
    with CATALOG_PATH.open("rb") as fp:
        icons = tomllib.load(fp)["icons"]
    ids = list(icons.values())
    assert len(ids) == len(set(ids))
    assert all(icon_id in ICON_ID_RANGE for icon_id in ids)


def test_star_row_makes_its_user_invincible_for_its_duration():
    star = next(item for item in _raw_items() if item["key"] == "star")
    fields = star["effect"]["fields"]
    assert star["effect"]["template"] == 10600
    assert fields["noDead"] == 1
    assert (
        fields["effectEndurance"] * 1000
        == (load_race_items_catalog(CATALOG_PATH)["items"]["star"]["duration_ms"])
    )
    for rate in (
        "slashDamageCutRate", "blowDamageCutRate", "thrustDamageCutRate",
        "neutralDamageCutRate", "magicDamageCutRate", "fireDamageCutRate",
        "thunderDamageCutRate", "darkDamageCutRate", "toughnessDamageCutRate",
    ):  # fmt: skip
        assert fields[rate] == 0, rate
    # Every status: immune (no proc) and no build-up, so none fires once the
    # Star ends either.
    for status in ("poison", "disease", "blood", "curse", "freeze", "sleep", "madness"):
        assert fields[f"disable{status.capitalize()}"] == 1, status
        assert fields[f"{status}DefDamageRate"] == 0, status


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


def test_loader_rejects_a_goods_id_in_the_effect_slice(tmp_path):
    # A usable goods at 1450820 would give a marker id an item effect can take.
    path = _write_catalog(tmp_path, _goods(goods_id=1450820))
    with pytest.raises(ValueError, match="goods_id 1450820 outside 1450800-1450819"):
        load_race_items_catalog(path)


@pytest.mark.parametrize("field", ["name", "info", "caption"])
def test_loader_rejects_a_goods_without_its_texts(tmp_path, field):
    goods = "\n".join(
        line for line in _goods().split("\n") if not line.startswith(f"{field} =")
    )
    path = _write_catalog(tmp_path, goods)
    with pytest.raises(ValueError, match=f"missing required field '{field}'"):
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
    path = _write_catalog(tmp_path, _goods(), icons="[icons]\nfoo = 60415\n")
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
