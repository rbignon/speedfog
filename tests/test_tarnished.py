from speedfog.config import TarnishedConfig
from speedfog.tarnished import (
    ARMOR_SETS,
    SHIELDS,
    SKIN_FLAGS,
    WEAPONS,
    build_class_loadout,
    build_torrent_skins,
    tarnished_rng,
)


def test_pack_item_pools():
    """Six right-hand weapons, two left-hand shields."""
    assert len(WEAPONS) == 6
    assert {s.id for s in SHIELDS} == {31540000, 62520000}


def test_build_class_loadout_is_covering_permutation():
    weapons, shields, armor = build_class_loadout(tarnished_rng(123))
    assert sorted(i.id for i in weapons) == sorted(i.id for i in WEAPONS)
    assert sorted(i.id for i in shields) == sorted(i.id for i in SHIELDS)
    assert sorted(map(tuple, armor)) == sorted(map(tuple, ARMOR_SETS))


def test_build_class_loadout_deterministic():
    a = build_class_loadout(tarnished_rng(555))
    b = build_class_loadout(tarnished_rng(555))
    c = build_class_loadout(tarnished_rng(556))
    assert a == b
    assert a != c  # one seed collision would be astronomically unlucky


def test_build_torrent_skins_disabled_and_fixed():
    off = TarnishedConfig(enabled=True)
    assert build_torrent_skins(off, tarnished_rng(1)) is None
    fixed = TarnishedConfig(
        enabled=True, unlock_torrent_skins=True, default_torrent_skin="carian-silver"
    )
    assert build_torrent_skins(fixed, tarnished_rng(1)) == {
        "unlock": True,
        "default_flag": SKIN_FLAGS["carian-silver"],
    }
    unlock_only = TarnishedConfig(enabled=True, unlock_torrent_skins=True)
    assert build_torrent_skins(unlock_only, tarnished_rng(1)) == {"unlock": True}


def test_build_torrent_skins_random_is_deterministic():
    cfg = TarnishedConfig(
        enabled=True, unlock_torrent_skins=True, default_torrent_skin="random"
    )
    a = build_torrent_skins(cfg, tarnished_rng(42))
    b = build_torrent_skins(cfg, tarnished_rng(42))
    assert a == b
    assert a["default_flag"] in SKIN_FLAGS.values()
