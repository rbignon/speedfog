"""Inventory unique warps with exactly one 'open' side and no 'opensplit' tag.

These are candidates for an opensplit override: FogMod currently marks them
entirely 'unused' in crawl mode, so their core side is unreachable as an
entry/exit edge in the graph.
"""

from __future__ import annotations

import sys
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parent.parent
FOG_TXT = REPO_ROOT / "data" / "fog.txt"


def split_tags(value) -> list[str]:
    if not value:
        return []
    if isinstance(value, list):
        return [str(t).lower() for t in value]
    return [t.lower() for t in str(value).split()]


def main() -> int:
    text = FOG_TXT.read_text()
    doc = yaml.safe_load(text)

    warps = doc.get("Warps") or []
    entrances = doc.get("Entrances") or []
    candidates: list[dict] = []

    for entry_list, kind in ((warps, "Warp"), (entrances, "Entrance")):
        for e in entry_list:
            gate_tags = split_tags(e.get("Tags"))
            if "unique" not in gate_tags:
                continue
            if "opensplit" in gate_tags:
                continue
            if "unused" in gate_tags:
                continue

            aside = e.get("ASide") or {}
            bside = e.get("BSide") or {}
            aside_tags = split_tags(aside.get("Tags"))
            bside_tags = split_tags(bside.get("Tags"))

            aside_open = "open" in aside_tags or "open" in gate_tags
            bside_open = "open" in bside_tags or "open" in gate_tags

            # Exactly one side is open (XOR)
            if aside_open == bside_open:
                continue

            # Skip ones already disabled by other mechanisms (rare)
            candidates.append(
                {
                    "kind": kind,
                    "id": e.get("ID") or e.get("Name"),
                    "name": e.get("Name"),
                    "text": e.get("Text"),
                    "tags": gate_tags,
                    "aside_area": aside.get("Area"),
                    "aside_text": aside.get("Text"),
                    "aside_tags": aside_tags,
                    "aside_open": aside_open,
                    "aside_cond": aside.get("Cond"),
                    "bside_area": bside.get("Area"),
                    "bside_text": bside.get("Text"),
                    "bside_tags": bside_tags,
                    "bside_open": bside_open,
                    "bside_cond": bside.get("Cond"),
                }
            )

    print(
        f"Found {len(candidates)} unique warps with exactly one 'open' side and no 'opensplit'.\n"
    )
    for c in candidates:
        core_label = "BSide core" if c["aside_open"] else "ASide core"
        print(f"--- {c['kind']} {c['id']} ({c['name']}) ---")
        print(f"  Text: {c['text']}")
        print(f"  Gate tags: {c['tags']}")
        print(
            f"  ASide: area={c['aside_area']!r} tags={c['aside_tags']} cond={c['aside_cond']!r}"
        )
        print(f"         text={c['aside_text']!r}")
        print(
            f"  BSide: area={c['bside_area']!r} tags={c['bside_tags']} cond={c['bside_cond']!r}"
        )
        print(f"         text={c['bside_text']!r}")
        print(f"  Verdict: {core_label} -> would gain edge if opensplit added")
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main())
