"""Seed data/plugins/summer.toml boss skeleton from generated game data.

Joins boss_arena_tags.json (roster + names, keyed by entity id as decimal string),
clusters.json (major-boss filter), and the randomizer enemy.txt
(entity id -> NpcName FMG id). Emits boss entries with npc_name_id + name
for a human to fill `en`/`fr`.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

_ID_RE = re.compile(r"^- ID:\s*(\d+)\s*$")
_NPC_RE = re.compile(r"^\s*NpcName:\s*(\d+)\s*$")


def parse_npc_names(enemy_txt_path: str | Path) -> dict[int, int]:
    """Map entity id -> NpcName FMG id from enemy.txt."""
    result: dict[int, int] = {}
    current: int | None = None
    for line in Path(enemy_txt_path).read_text(encoding="utf-8").splitlines():
        m = _ID_RE.match(line)
        if m:
            current = int(m.group(1))
            continue
        m = _NPC_RE.match(line)
        if m and current is not None:
            result[current] = int(m.group(1))
            current = None
    return result


def major_entity_ids(clusters_path: Path) -> set[int]:
    """Entity ids of clusters whose type is major_boss."""
    data = json.loads(Path(clusters_path).read_text(encoding="utf-8"))
    ids: set[int] = set()
    for cluster in data.get("clusters", []):
        if cluster.get("type") != "major_boss":
            continue
        for zone in cluster.get("zones", []):
            eid = zone.get("entity_id")
            if isinstance(eid, int):
                ids.add(eid)
    return ids


def build_summer_skeleton(
    boss_tags: dict[str, dict],
    npc_name_by_entity: dict[int, int],
    major_entity_ids: set[int],
) -> list[dict]:
    """Return [{npc_name_id, name}] for major bosses that have an NpcName.

    boss_tags keys are entity ids as decimal strings.
    Deduplicates by npc_name_id; when two entity ids share a name id, the
    lower entity id wins (iteration is sorted ascending).
    """
    out: list[dict] = []
    seen: set[int] = set()
    for eid in sorted(major_entity_ids):
        tag = boss_tags.get(str(eid))
        if tag is None:
            continue
        npc = npc_name_by_entity.get(eid)
        if npc is None or npc in seen:
            continue
        seen.add(npc)
        out.append({"npc_name_id": npc, "name": tag.get("name", "")})
    return out


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print("usage: seed_summer_catalog.py <path/to/enemy.txt>", file=sys.stderr)
        return 1
    root = Path(__file__).resolve().parent.parent
    boss_tags = json.loads((root / "data" / "boss_arena_tags.json").read_text())
    npc_by_entity = parse_npc_names(Path(argv[1]))
    major = major_entity_ids(root / "data" / "clusters.json")
    skeleton = build_summer_skeleton(boss_tags, npc_by_entity, major)
    for row in skeleton:
        print(
            f'[[bosses]]\nnpc_name_id = {row["npc_name_id"]}'
            f'\nname = "{row["name"]}"\nen = ""\nfr = ""\n'
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
