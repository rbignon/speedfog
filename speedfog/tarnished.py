"""Tarnished Pack showcase: class loadout and Torrent skin draws.

This is the ``[tarnished]`` policy module. It produces the generic
``class_loadout`` / ``torrent_skins`` fields written to graph.json (see the
spec), which the C# writer then applies without any further randomization
decisions.

``SKIN_FLAGS`` maps the three Torrent skin names to their SpEffect/unlock
flags (6701-6703 on ``common.emevd`` event 780, which tests flags
6700-6703). These values are the single place to adjust once the in-game
name-to-flag mapping is confirmed.

The RNG is seeded with ``f"{seed}-tarnished"`` (see ``tarnished_rng``) so
draws here stay uncorrelated with the care package's ``random.Random(seed)``
in ``care_package.py``.
"""

from __future__ import annotations

import random
from dataclasses import dataclass
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from speedfog.config import TarnishedConfig


@dataclass
class HandItem:
    """A hand-equippable item (weapon or shield) for the class loadout draw."""

    id: int  # Param row ID (EquipParamWeapon)
    slot: str  # "right" or "left"
    name: str  # Display name for spoiler log


# Right-hand weapons (Global Constraints ID table)
HAND_ITEMS: list[HandItem] = [
    HandItem(3560000, "right", "Leontiel's Greatsword"),
    HandItem(8530000, "right", "Hefty Scimitar"),
    HandItem(13510000, "right", "Golden Order Flail"),
    HandItem(64530000, "right", "Reverse-Bladed Sword"),
    HandItem(66530000, "right", "Reed Great Katana"),
    HandItem(67530000, "right", "Idus Sword"),
    # Left-hand shields
    HandItem(31540000, "left", "Silver Grooved Shield"),
    HandItem(62520000, "left", "Ritual Thrusting Shield"),
]

# Armor sets, each a (head, body, arms, legs) quadruple (ProtectorParam rows)
ARMOR_SETS: list[list[int]] = [
    [5340000, 5340100, 5340200, 5340300],  # Gold Tattoo (Broken Gold Mask)
    [5350000, 5350100, 5350200, 5350300],  # Silver Grooved
    [5360000, 5360100, 5360200, 5360300],  # Leontiel's
    [5370000, 5370100, 5370200, 5370300],  # Steel
]

# Torrent skin name -> unlock/default flag (common.emevd event 780 tests
# flags 6700-6703; in-game confirmation pending, see module docstring).
SKIN_FLAGS: dict[str, int] = {
    "tree-sentinel": 6701,
    "carian-silver": 6702,
    "funereal-night": 6703,
}


def tarnished_rng(seed: int) -> random.Random:
    """Build the RNG for tarnished showcase draws.

    Seeded independently from the care package's ``random.Random(seed)`` so
    the two draws never correlate.
    """
    return random.Random(f"{seed}-tarnished")


def build_class_loadout(
    rng: random.Random,
) -> tuple[list[HandItem], list[list[int]]]:
    """Draw one shuffled permutation of hand items and armor sets.

    Returns a copy of ``HAND_ITEMS`` and ``ARMOR_SETS``, each independently
    shuffled with ``rng``, so every item/set is used exactly once (a
    covering permutation, not a sample).
    """
    hand = list(HAND_ITEMS)
    rng.shuffle(hand)
    armor = [list(a) for a in ARMOR_SETS]
    rng.shuffle(armor)
    return hand, armor


def build_torrent_skins(
    config: TarnishedConfig, rng: random.Random
) -> dict[str, Any] | None:
    """Draw the Torrent skin unlock/default settings from config.

    Returns ``None`` when ``unlock_torrent_skins`` is false. Otherwise
    returns ``{"unlock": True}``, plus a ``"default_flag"`` key when
    ``default_torrent_skin`` names a fixed skin or requests ``"random"``
    (resolved here with ``rng``).
    """
    if not config.unlock_torrent_skins:
        return None
    result: dict[str, Any] = {"unlock": True}
    skin = config.default_torrent_skin
    if skin == "random":
        result["default_flag"] = rng.choice(list(SKIN_FLAGS.values()))
    elif skin:
        result["default_flag"] = SKIN_FLAGS[skin]
    return result
