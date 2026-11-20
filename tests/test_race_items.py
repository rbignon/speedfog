"""Tests for the race items catalog and its loader."""

import re
import tomllib
from pathlib import Path

import pytest

from speedfog.race_items import (
    EFFECT_ID_RANGE,
    GOODS_ID_RANGE,
    HUNTER_TIERS,
    ICON_ID_RANGE,
    NPC_PARAM_RANGE,
    REMOVE_FLAG_BASE,
    TALISMAN_FLAG_BASE,
    TALISMAN_FLAG_COUNT,
    USE_FLAG_BASE,
    load_race_items_catalog,
)

CATALOG_PATH = Path(__file__).parent.parent / "data" / "plugins" / "race_items.toml"


def _raw_items() -> list[dict]:
    with CATALOG_PATH.open("rb") as fp:
        return tomllib.load(fp)["item"]


def test_shipped_catalog_matches_the_contract():
    catalog = load_race_items_catalog(CATALOG_PATH)
    assert set(catalog) == {"goods", "items", "talismans", "junk_weapons", "hunter"}
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
    assert len(catalog["items"]) == 22
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


MEMORY_EFFECTS = {"vanish", "unseen_foes", "slow_mo", "giant", "camera"}
# Applied by the racing mod without a SpEffect row: the Hunter lasts 15 s of
# play clock, the Talisman Thief and the Junk Weapon are permanent.
MOD_APPLIED_DURATIONS = {"hunter": 15, "talisman_thief": None, "junk_weapon": None}


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
        elif item["key"] in MOD_APPLIED_DURATIONS:
            assert duration == MOD_APPLIED_DURATIONS[item["key"]], item["key"]
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


def _goods(key="g", goods_id=1450800, extra="", icon="foo"):
    lines = [
        "\n[[goods]]",
        f'key = "{key}"',
        f'icon = "{icon}"',
        'name = "G"',
        'info = "I"',
        'caption = "C"',
        f"goods_id = {goods_id}",
    ]
    return "\n".join(lines) + "\n" + extra


def _usable_goods(key="g", goods_id=1450801):
    return _goods(key, goods_id, extra=MARKER)


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


def test_loader_derives_the_flags_from_the_goods_id(tmp_path):
    # The flags are not in the catalog: two goods cannot share one, and only
    # a goods with a marker (one that can be used) gets a use flag.
    path = _write_catalog(
        tmp_path, _goods("passive", 1450804), _usable_goods("usable", 1450807)
    )
    goods = load_race_items_catalog(path)["goods"]
    assert (goods["passive"]["use_flag"], goods["passive"]["remove_flag"]) == (
        None,
        1050290204,
    )
    assert (goods["usable"]["use_flag"], goods["usable"]["remove_flag"]) == (
        1050290107,
        1050290207,
    )


def test_loader_rounds_a_fractional_duration_to_the_millisecond(tmp_path):
    # 4.35 * 1000 is 4349.999... in binary floating point.
    path = _write_catalog(tmp_path, _usable_goods(), _item(extra="duration_s = 4.35\n"))
    assert load_race_items_catalog(path)["items"]["a"]["duration_ms"] == 4350


def test_loader_reads_a_catalog_saved_with_a_bom(tmp_path):
    # A Windows editor's UTF-8 BOM is not a syntax error; the C# loader's
    # File.ReadAllText drops it too. Not in the shared corpus: the check-toml
    # pre-commit hook refuses such a file.
    path = _write_catalog(tmp_path, _goods())
    path.write_bytes(b"\xef\xbb\xbf" + path.read_bytes())
    assert "g" in load_race_items_catalog(path)["goods"]


CORPUS = Path(__file__).parent / "fixtures" / "race_items"
CS_IDS = Path(__file__).parent.parent / "writer/FogModWrapper.Core/SpeedFogIds.cs"


def _corpus(prefix: str) -> list[Path]:
    return sorted(CORPUS.glob(f"{prefix}_*.toml"))


def test_corpus_has_both_kinds():
    assert _corpus("valid") and _corpus("invalid")


@pytest.mark.parametrize("path", _corpus("valid"), ids=lambda p: p.stem)
def test_loader_accepts_the_shared_valid_catalogs(path):
    # The C# loader replays the same files (RaceItemsCatalogCorpusTests).
    load_race_items_catalog(path)


@pytest.mark.parametrize("path", _corpus("invalid"), ids=lambda p: p.stem)
def test_loader_rejects_the_shared_invalid_catalogs(path):
    first_line = path.read_text(encoding="utf-8").splitlines()[0]
    assert first_line.startswith("# expect: "), path.name
    expected = first_line.removeprefix("# expect: ")
    with pytest.raises(ValueError, match=re.escape(expected)):
        load_race_items_catalog(path)


@pytest.mark.parametrize(
    ("constant", "value"),
    [
        ("RaceItemUseFlagBase", USE_FLAG_BASE),
        ("RaceItemRemoveFlagBase", REMOVE_FLAG_BASE),
        ("RaceItemTalismanRemoveFlagBase", TALISMAN_FLAG_BASE),
        ("RaceItemTalismanRemoveFlagCount", TALISMAN_FLAG_COUNT),
        ("RaceItemParamIdMin", GOODS_ID_RANGE.start),
        ("RaceItemGoodsIdMax", GOODS_ID_RANGE.stop - 1),
        ("RaceItemEffectIdMin", EFFECT_ID_RANGE.start),
        ("RaceItemEffectIdMax", EFFECT_ID_RANGE.stop - 1),
        ("RaceItemNpcParamIdMin", NPC_PARAM_RANGE.start),
        ("RaceItemNpcParamIdMax", NPC_PARAM_RANGE.stop - 1),
        ("RaceItemHunterTierCount", HUNTER_TIERS),
        ("RaceItemIconIdMin", ICON_ID_RANGE.start),
        ("RaceItemIconIdMax", ICON_ID_RANGE.stop - 1),
    ],
)
def test_reserved_ranges_match_the_csharp_ones(constant, value):
    # Both loaders number and bound the same ids from their own constants.
    match = re.search(rf"\b{constant}\s*=\s*(\d+)", CS_IDS.read_text(encoding="utf-8"))
    assert match and int(match.group(1)) == value


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


def test_shipped_catalog_exports_the_talisman_table():
    talismans = load_race_items_catalog(CATALOG_PATH)["talismans"]
    assert len(talismans) == 155
    ids = [t["id"] for t in talismans]
    assert ids == sorted(set(ids))
    assert [t["flag"] for t in talismans] == [
        TALISMAN_FLAG_BASE + i for i in range(len(talismans))
    ]


def test_shipped_hunter_block_covers_every_tier():
    hunter = load_race_items_catalog(CATALOG_PATH)["hunter"]
    assert hunter == {
        "chr_id": 2130,
        "think_param": 1450850,
        "npc_params": list(range(1450850, 1450884)),
    }
    assert len(hunter["npc_params"]) == HUNTER_TIERS
    assert all(npc in NPC_PARAM_RANGE for npc in hunter["npc_params"])


def test_shipped_junk_weapons_are_plain():
    # The loader already rejects duplicate ids and empty names.
    weapons = load_race_items_catalog(CATALOG_PATH)["junk_weapons"]
    ids = [w["id"] for w in weapons]
    assert ids
    # +0 standard (a weapon id ends in its affinity and upgrade level): the
    # racing mod finds the given copy by this base, whatever level it lands at.
    assert all(id_ % 10000 == 0 for id_ in ids)


def _talismans_file(tmp_path: Path, body: str) -> None:
    (tmp_path / "race_items_talismans.toml").write_text(body, encoding="utf-8")


def _talisman(id_, name="T"):
    return f'\n[[talisman]]\nid = {id_}\nname = "{name}"\n'


@pytest.mark.parametrize(
    ("body", "match"),
    [
        pytest.param(_talisman(5) + _talisman(5), "ascending", id="duplicate"),
        pytest.param(_talisman(6) + _talisman(5), "ascending", id="descending"),
        pytest.param(_talisman(5, ""), "empty name", id="empty-name"),
        pytest.param(_talisman(0), "positive", id="non-positive"),
        pytest.param(
            "".join(_talisman(i + 1) for i in range(201)), "more than", id="too-many"
        ),
    ],
)
def test_loader_rejects_a_bad_talisman_table(tmp_path, body, match):
    _talismans_file(tmp_path, body)
    path = _write_catalog(tmp_path, _goods())
    with pytest.raises(ValueError, match=match):
        load_race_items_catalog(path)


@pytest.mark.parametrize(
    ("body", "match"),
    [
        pytest.param(
            _talisman(5).replace("[[talisman]]", "[[talismans]]"),
            "unknown key 'talismans'",
            id="table",
        ),
        pytest.param(_talisman(5) + "flag = 3\n", "unknown key 'flag'", id="entry"),
    ],
)
def test_loader_rejects_an_unknown_key_in_the_talisman_table(tmp_path, body, match):
    # Same cases as the C# RaceItemTalismansTests.Rejects_an_unknown_key.
    _talismans_file(tmp_path, body)
    path = _write_catalog(tmp_path, _goods())
    with pytest.raises(ValueError, match=match):
        load_race_items_catalog(path)


def _junk(id_, name="W"):
    return f'\n[[junk_weapon]]\nid = {id_}\nname = "{name}"\n'


@pytest.mark.parametrize(
    ("entries", "match"),
    [
        pytest.param((_junk(0),), "positive", id="zero-id"),
        pytest.param((_junk(-4),), "positive", id="negative-id"),
        pytest.param((_junk(5, ""),), "empty name", id="empty-name"),
        pytest.param((_junk(5), _junk(5, "X")), "duplicate", id="duplicate"),
    ],
)
def test_loader_rejects_a_bad_junk_weapon(tmp_path, entries, match):
    path = _write_catalog(tmp_path, _goods(), *entries)
    with pytest.raises(ValueError, match=match):
        load_race_items_catalog(path)


def _hunter(base=1450850, **overrides):
    fields = {
        "chr_id": 2130,
        "source_npc_param": 21300014,
        "source_think": 21300000,
        "npc_param_base": base,
        "think_param": 1450850,
    } | overrides
    return "\n[hunter]\n" + "".join(f"{k} = {v}\n" for k, v in fields.items())


@pytest.mark.parametrize("base", [1450849, 1450867, 1450900])
def test_loader_rejects_a_hunter_base_leaving_the_range(tmp_path, base):
    path = _write_catalog(tmp_path, _goods(), _hunter(base))
    with pytest.raises(ValueError, match="outside"):
        load_race_items_catalog(path)


def test_loader_keeps_the_csharp_only_hunter_fields_out_of_the_export(tmp_path):
    path = _write_catalog(tmp_path, _goods(), _hunter())
    hunter = load_race_items_catalog(path)["hunter"]
    assert set(hunter) == {"chr_id", "think_param", "npc_params"}
