"""enemy.txt parsing and randomized boss placement patching.

Line-based scanners over FogRando's enemy.txt (a full YAML parse takes ~6s
under PyYAML's Python loader; we only need a few fields per entry) plus the
logic that patches randomized boss names back into an exported graph.json.
"""

from __future__ import annotations

import json
import re
from collections.abc import Callable, Mapping
from pathlib import Path
from typing import Any

from speedfog.dag import Dag

_PHASE_SUFFIX_RE = re.compile(r" \d+$")

_ENEMY_ID_RE = re.compile(r"^- ID:\s*(\d+)")
_NEXT_PHASE_RE = re.compile(r"^  NextPhase:\s*(\d+)")
_EXTRA_NAME_RE = re.compile(r"^\s+ExtraName:\s*(.+)")
_KEY_NAME_RE = re.compile(r"^      Key:\s*(.+)")


def parse_boss_phases(enemy_txt_path: Path) -> dict[int, int]:
    """Parse enemy.txt to build a reverse NextPhase mapping.

    For multi-phase bosses, enemy.txt links phase 1 to phase 2 via NextPhase.
    This returns a reverse mapping: phase2_entity_id -> phase1_entity_id.

    A full YAML parse of enemy.txt takes ~6s under PyYAML's Python loader; we
    only need two fields per entry, so a line-based scan keyed on the entry's
    2-space indentation is ~200x faster and the result is identical (the same
    applies to the other parse_boss_* scanners below).

    Returns an empty dict if the file is missing.
    """
    if not enemy_txt_path.exists():
        return {}

    phase_mapping: dict[int, int] = {}
    current_id: int | None = None
    with open(enemy_txt_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("- ID:"):
                m = _ENEMY_ID_RE.match(line)
                if m:
                    current_id = int(m.group(1))
            elif line.startswith("  NextPhase:") and current_id is not None:
                m = _NEXT_PHASE_RE.match(line)
                if m:
                    phase_mapping[int(m.group(1))] = current_id
    return phase_mapping


def parse_boss_extra_names(enemy_txt_path: Path) -> dict[int, str]:
    """Parse enemy.txt to build an entity_id -> ExtraName mapping.

    ExtraName is the legacy display name (e.g. "Margit, the Fell Omen"), now
    secondary to the canonical Important.Names.Key (see parse_boss_key_names).
    Retained as a fallback by resolve_boss_name for entities that carry an
    ExtraName but no Key. tools/generate_clusters.py::parse_boss_names
    similarly prefers Names.Key with ExtraName as fallback. Keyed by entity ID
    so a randomized boss source is named consistently with non-randomized
    bosses.

    Returns an empty dict if the file is missing.
    """
    if not enemy_txt_path.exists():
        return {}

    extra_names: dict[int, str] = {}
    current_id: int | None = None
    with open(enemy_txt_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("- ID:"):
                m = _ENEMY_ID_RE.match(line)
                if m:
                    current_id = int(m.group(1))
            elif line.startswith("  ExtraName:") and current_id is not None:
                m = _EXTRA_NAME_RE.match(line)
                if m:
                    name = m.group(1).strip()
                    if name:
                        extra_names[current_id] = name
    return extra_names


def parse_boss_key_names(enemy_txt_path: Path) -> dict[int, str]:
    """Parse enemy.txt to build an entity_id -> Important.Names.Key mapping.

    ``Key`` is the canonical display name in the post-update enemy.txt format
    (nested under ``Important: Names:`` at 6-space indent). It is cleaner than
    the legacy ``ExtraName``: no ``Boss``/``Duo`` suffixes or phase numbers,
    proper full names ("Rennala, Queen of the Full Moon" rather than
    "Rennala 2"), and it fixes typos ("Godfrey" not "Goldfrey"). Preferred over
    ExtraName by ``resolve_boss_name``. First Key per entity wins.

    Returns an empty dict if the file is missing.
    """
    if not enemy_txt_path.exists():
        return {}

    key_names: dict[int, str] = {}
    current_id: int | None = None
    with open(enemy_txt_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("- ID:"):
                m = _ENEMY_ID_RE.match(line)
                if m:
                    current_id = int(m.group(1))
            elif line.startswith("      Key:") and current_id is not None:
                if current_id in key_names:
                    continue
                m = _KEY_NAME_RE.match(line)
                if m:
                    name = m.group(1).strip()
                    if name:
                        key_names[current_id] = name
    return key_names


def resolve_boss_name(
    entity_id: int,
    key_names: Mapping[int, str],
    extra_names: Mapping[int, str],
    tag_names: Mapping[int, str],
) -> str:
    """Resolve a boss source entity to a display name.

    Names.Key (the post-update canonical name) first, then the legacy enemy.txt
    ExtraName, then the boss_arena_tags.json name (covers promoted sources with
    neither), then the raw ID string. Using Key first unifies naming with the
    non-randomized boss_name and yields cleaner strings (no ``Boss``/``Duo``
    suffixes, proper full names, typo fixes).
    """
    return (
        key_names.get(entity_id)
        or extra_names.get(entity_id)
        or tag_names.get(entity_id)
        or str(entity_id)
    )


def build_boss_placements(
    enemy_assignments: Mapping[str, str],
    resolve_name: Callable[[int], str],
) -> dict[str, dict[str, Any]]:
    """Reshape {arena_id: boss_id} into the placements dict format.

    The result is keyed by arena entity ID string, consumed unchanged by
    patch_graph_boss_placements and
    spoiler.append_boss_placements_to_spoiler. Both keys and the boss IDs
    arrive as strings from enemy_assignments; the boss ID is resolved to a
    name and stored as an int entity_id.
    """
    placements: dict[str, dict[str, Any]] = {}
    for arena_id, boss_id in enemy_assignments.items():
        bid = int(boss_id)
        placements[arena_id] = {"name": resolve_name(bid), "entity_id": bid}
    return placements


def patch_graph_boss_placements(
    graph_path: Path,
    dag: Dag,
    placements: dict[str, dict[str, Any]],
    phase_mapping: dict[int, int] | None = None,
) -> None:
    """Patch graph.json nodes with randomized boss names.

    Sets:
    - randomized_bosses: list of boss names (both phases for multi-phase bosses)
    - boss_name: canonical name from phase 2 (suffix-stripped)

    Args:
        graph_path: Path to existing graph.json to patch
        dag: The DAG with cluster defeat_flags
        placements: Boss placements from build_boss_placements()
        phase_mapping: Optional reverse NextPhase mapping (phase2_id -> phase1_id)
    """
    if not placements:
        return

    with open(graph_path, encoding="utf-8") as f:
        graph: dict[str, Any] = json.load(f)

    nodes = graph.get("nodes", {})

    for node in dag.nodes.values():
        defeat_flag = node.cluster.defeat_flag
        if defeat_flag == 0:
            continue

        phase2_name = _match_boss_placement(defeat_flag, placements)
        if phase2_name and node.cluster.id in nodes:
            boss_list: list[str] = []

            if phase_mapping:
                entity_id = resolve_entity_id(defeat_flag)
                phase1_entity_id = phase_mapping.get(entity_id)
                if phase1_entity_id:
                    phase1_key = str(phase1_entity_id)
                    if phase1_key in placements:
                        boss_list.append(str(placements[phase1_key]["name"]))

            boss_list.append(phase2_name)

            nodes[node.cluster.id]["randomized_bosses"] = boss_list
            nodes[node.cluster.id]["boss_name"] = _PHASE_SUFFIX_RE.sub("", phase2_name)

    with open(graph_path, "w", encoding="utf-8") as f:
        json.dump(graph, f, indent=2)


def _match_boss_placement(
    defeat_flag: int, placements: dict[str, dict[str, Any]]
) -> str | None:
    """Match a defeat_flag to a boss placement entry.

    Args:
        defeat_flag: Cluster's DefeatFlag from fog.txt
        placements: Boss placements keyed by entity ID string

    Returns:
        Boss name if matched, None otherwise.
    """
    key = str(defeat_flag)
    if key in placements:
        return str(placements[key]["name"])

    entity_id = resolve_entity_id(defeat_flag)
    if entity_id != defeat_flag:
        key = str(entity_id)
        if key in placements:
            return str(placements[key]["name"])

    return None


# Some boss DefeatFlags (Radahn, Fire Giant) are the boss's entity ID plus a
# fixed 200M offset; flags in the 1.2-2.0 billion band are the offset form.
_OFFSET_FLAG_MIN = 1_200_000_000
_OFFSET_FLAG_MAX = 2_000_000_000
_DEFEAT_FLAG_OFFSET = 200_000_000


def resolve_entity_id(defeat_flag: int) -> int:
    """Resolve defeat_flag to entity_id (handles Radahn/Fire Giant 200M offset)."""
    if _OFFSET_FLAG_MIN <= defeat_flag < _OFFSET_FLAG_MAX:
        return defeat_flag - _DEFEAT_FLAG_OFFSET
    return defeat_flag
