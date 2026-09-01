"""Tarnished Pack showcase: class loadout and Torrent skin draws.

This is the ``[tarnished]`` policy module. It produces the generic
``class_loadout`` / ``torrent_skins`` fields written to graph.json (see the
spec), which the C# writer then applies without any further randomization
decisions.

The loadout model (revised after the 2026-09-01 in-game pass): every
starting class gets a pack WEAPON in the right hand (covering shuffle of
the six weapons, wrapping over the classes), while each of the two pack
SHIELDS is placed exactly once, on the first classes in draw order whose
left-hand slot is occupied. Armor pieces only replace slots the class
already fills (Wretch stays bare); the per-slot checks live in the C#
``ClassLoadoutInjector``, this module only draws the orders.

``SKIN_FLAGS`` maps the three Torrent skin names to their selection flags
(6701-6703, cleared/set alongside 6700 by ``common.emevd`` event 780 and
the grace ESD). The mapping was confirmed in-game on 2026-09-01
(``carian-silver`` -> 6702 applies the Carian Silver attire).

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
class PackItem:
    """A hand-equippable pack item (weapon or shield) for the loadout draw."""

    id: int  # Param row ID (EquipParamWeapon)
    name: str  # Display name for spoiler log


# Right-hand weapons: always applied, covering shuffle wrapping over classes.
WEAPONS: list[PackItem] = [
    PackItem(3560000, "Leontiel's Greatsword"),
    PackItem(8530000, "Hefty Scimitar"),
    PackItem(13510000, "Golden Order Flail"),
    PackItem(64530000, "Reverse-Bladed Sword"),
    PackItem(66530000, "Reed Great Katana"),
    PackItem(67530000, "Idus Sword"),
]

# Left-hand shields: each placed once, on classes with an occupied left hand.
SHIELDS: list[PackItem] = [
    PackItem(31540000, "Silver Grooved Shield"),
    PackItem(62520000, "Ritual Thrusting Shield"),
]

# Armor sets, each a (head, body, arms, legs) quadruple (EquipParamProtector rows)
ARMOR_SETS: list[list[int]] = [
    [5340000, 5340100, 5340200, 5340300],  # Gold Tattoo (Broken Gold Mask)
    [5350000, 5350100, 5350200, 5350300],  # Silver Grooved
    [5360000, 5360100, 5360200, 5360300],  # Leontiel's
    [5370000, 5370100, 5370200, 5370300],  # Steel
]

# Torrent skin name -> selection flag (confirmed in-game 2026-09-01).
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
) -> tuple[list[PackItem], list[PackItem], list[list[int]]]:
    """Draw shuffled permutations of weapons, shields, and armor sets.

    Returns copies of ``WEAPONS``, ``SHIELDS`` and ``ARMOR_SETS``, each
    independently shuffled with ``rng``, so every item/set is used (a
    covering permutation, not a sample). Weapons and armor wrap over the
    classes on the C# side; each shield is placed exactly once.
    """
    weapons = list(WEAPONS)
    rng.shuffle(weapons)
    shields = list(SHIELDS)
    rng.shuffle(shields)
    armor = [list(a) for a in ARMOR_SETS]
    rng.shuffle(armor)
    return weapons, shields, armor


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
