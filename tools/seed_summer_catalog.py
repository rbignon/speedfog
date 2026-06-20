"""Seed data/plugins/summer.toml boss skeleton from generated game data.

Joins clusters.json (major-boss clusters, each carrying `defeat_flag` and
`boss_name`) with the randomizer enemy.txt (which carries both `DefeatFlag`
and `NpcName` per entity). The defeat flag is the join key: a major-boss
cluster's `defeat_flag` matches an enemy.txt entity's `DefeatFlag`, whose
`NpcName` is the FMG id to theme. Emits boss entries with npc_name_id + name
for a human to fill `en`/`fr`.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

_ID_RE = re.compile(r"^- ID:\s*\d+\s*$")
_DEFEAT_RE = re.compile(r"^\s*DefeatFlag:\s*(\d+)\s*$")
_NPC_RE = re.compile(r"^\s*NpcName:\s*(\d+)\s*$")


def parse_defeat_flag_npc_names(enemy_txt_path: str | Path) -> dict[int, int]:
    """Map DefeatFlag -> NpcName FMG id from enemy.txt.

    Only entities carrying both fields are included. On a duplicate DefeatFlag,
    the first occurrence wins (later ones are ignored).
    """
    result: dict[int, int] = {}
    defeat: int | None = None
    npc: int | None = None

    def flush() -> None:
        if defeat is not None and npc is not None and defeat not in result:
            result[defeat] = npc

    for line in Path(enemy_txt_path).read_text(encoding="utf-8").splitlines():
        if _ID_RE.match(line):
            flush()
            defeat = None
            npc = None
            continue
        m = _DEFEAT_RE.match(line)
        if m:
            defeat = int(m.group(1))
            continue
        m = _NPC_RE.match(line)
        if m:
            npc = int(m.group(1))
    flush()
    return result


def major_boss_entries(clusters_path: str | Path) -> list[dict]:
    """Return [{defeat_flag, name}] for clusters whose type is major_boss.

    Name comes from the cluster's `boss_name`, falling back to `display_name`.
    Clusters without a `defeat_flag` are skipped.
    """
    data = json.loads(Path(clusters_path).read_text(encoding="utf-8"))
    out: list[dict] = []
    for cluster in data.get("clusters", []):
        if cluster.get("type") != "major_boss":
            continue
        flag = cluster.get("defeat_flag")
        if not isinstance(flag, int):
            continue
        name = cluster.get("boss_name") or cluster.get("display_name", "")
        out.append({"defeat_flag": flag, "name": name})
    return out


def build_summer_skeleton(
    major_bosses: list[dict],
    defeat_flag_to_npc: dict[int, int],
) -> list[dict]:
    """Return [{npc_name_id, name}] for major bosses that resolve to an NpcName.

    Joins each major boss's `defeat_flag` to its NpcName id. Bosses whose flag
    has no NpcName are dropped. Deduplicates by npc_name_id (first wins).
    """
    out: list[dict] = []
    seen: set[int] = set()
    for boss in major_bosses:
        npc = defeat_flag_to_npc.get(boss["defeat_flag"])
        if npc is None or npc in seen:
            continue
        seen.add(npc)
        out.append({"npc_name_id": npc, "name": boss["name"]})
    return out


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print("usage: seed_summer_catalog.py <path/to/enemy.txt>", file=sys.stderr)
        return 1
    root = Path(__file__).resolve().parent.parent
    defeat_to_npc = parse_defeat_flag_npc_names(Path(argv[1]))
    majors = major_boss_entries(root / "data" / "clusters.json")
    skeleton = build_summer_skeleton(majors, defeat_to_npc)
    for row in skeleton:
        print(
            f'[[bosses]]\nnpc_name_id = {row["npc_name_id"]}'
            f'\nname = "{row["name"]}"\nen = ""\nfr = ""\n'
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
