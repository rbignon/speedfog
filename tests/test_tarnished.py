from speedfog.config import TarnishedConfig
from speedfog.tarnished import (
    ARMOR_SETS,
    HAND_ITEMS,
    SKIN_FLAGS,
    build_class_loadout,
    build_torrent_skins,
    tarnished_rng,
)


def test_hand_items_slots():
    """Six weapons go to the right hand, the two shields to the left."""
    by_slot = {"right": set(), "left": set()}
    for item in HAND_ITEMS:
        by_slot[item.slot].add(item.id)
    assert by_slot["left"] == {31540000, 62520000}
    assert len(by_slot["right"]) == 6


def test_build_class_loadout_is_covering_permutation():
    hand, armor = build_class_loadout(tarnished_rng(123))
    assert sorted(i.id for i in hand) == sorted(i.id for i in HAND_ITEMS)
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
