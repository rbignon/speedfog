"""Boss/arena tag model and compatibility check.

Ported from BossArenaRandomizer: same flag semantics, same bitmap-equivalent
logic. See docs/boss-arena-constraints.md for the compatibility rules.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(frozen=True, slots=True)
class BossTags:
    size: int
    type: int
    is_two_phase: bool
    is_dragon: bool
    is_npc: bool
    can_escape: bool
    night_boss: bool
    exclude_from_pool: bool


@dataclass(frozen=True, slots=True)
class ArenaTags:
    size: int
    type: int
    two_phase_not_allowed: bool
    dragon_not_allowed: bool
    npc_not_allowed: bool
    is_escapable: bool
    night_boss: bool


@dataclass(frozen=True, slots=True)
class EntityTags:
    entity_id: int
    name: str
    boss: BossTags  # always present
    arena: ArenaTags | None  # None for source-only entries
    pool: str | None  # "minor"/"major" for source-only, None otherwise
    region: int
    scaling: int
    dlc: bool


def load_tags(path: Path) -> dict[int, EntityTags]:
    raw: dict[str, dict[str, Any]] = json.loads(Path(path).read_text())
    out: dict[int, EntityTags] = {}
    for key, entry in raw.items():
        eid = int(key)
        out[eid] = EntityTags(
            entity_id=eid,
            name=entry["name"],
            boss=BossTags(**entry["boss"]),
            arena=ArenaTags(**entry["arena"]) if "arena" in entry else None,
            pool=entry.get("pool"),
            region=int(entry.get("region", 0)),
            scaling=int(entry.get("scaling", 0)),
            dlc=bool(entry.get("dlc", False)),
        )
    return out


def is_compatible(arena: ArenaTags, boss: BossTags, *, check_size: bool) -> bool:
    if arena.dragon_not_allowed and boss.is_dragon:
        return False
    if arena.two_phase_not_allowed and boss.is_two_phase:
        return False
    if arena.npc_not_allowed and boss.is_npc:
        return False
    if arena.is_escapable and boss.can_escape:
        return False
    if check_size and boss.size > arena.size:
        return False
    return True
