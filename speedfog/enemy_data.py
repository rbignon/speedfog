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
# boss_arena_tags.json disambiguation suffix ("Fire Knight (before Messmer)").
_PARENTHETICAL_SUFFIX_RE = re.compile(r"\s*\([^()]*\)$")

_ENEMY_ID_RE = re.compile(r"^- ID:\s*(\d+)")
_NEXT_PHASE_RE = re.compile(r"^  NextPhase:\s*(\d+)")
_EXTRA_NAME_RE = re.compile(r"^\s+ExtraName:\s*(.+)")
_KEY_NAME_RE = re.compile(r"^      Key:\s*(.+)")
_IMPORTANT_NPC_NAME_RE = re.compile(r"^    NpcName:\s*(\d+)")
_NAME_RE = re.compile(r"^  Name:\s*(\S+)")
_CLASS_RE = re.compile(r"^  Class:\s*(\S+)")
_OWNED_BY_RE = re.compile(r"^  OwnedBy:\s*(\d+)")


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


def parse_boss_npc_names(enemy_txt_path: Path) -> dict[int, int]:
    """Parse enemy.txt to build an entity_id -> Important.NpcName mapping.

    ``NpcName`` (4-space indent under ``Important:``) is the vanilla NpcName
    FMG id the enemy randomizer carries along when it relocates that enemy:
    entities that have one show the right healthbar name wherever they are
    placed. Entities without one (regular mobs promoted to boss arenas) are
    the ones ``build_boss_names`` exports for the C# side. First per entity
    wins (every ``Class: Boss`` entry has one).

    Returns an empty dict if the file is missing.
    """
    if not enemy_txt_path.exists():
        return {}

    npc_names: dict[int, int] = {}
    current_id: int | None = None
    with open(enemy_txt_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("- ID:"):
                m = _ENEMY_ID_RE.match(line)
                if m:
                    current_id = int(m.group(1))
            elif line.startswith("    NpcName:") and current_id is not None:
                if current_id in npc_names:
                    continue
                m = _IMPORTANT_NPC_NAME_RE.match(line)
                if m:
                    npc_names[current_id] = int(m.group(1))
    return npc_names


def parse_helper_models(enemy_txt_path: Path) -> dict[int, list[str]]:
    """Models of each boss's helper enemies, keyed by owner entity id.

    The enemy randomizer clones every ``Class: Helper`` entry ``OwnedBy`` a
    placed boss into the target arena (RandomizerCommon EnemyRandomizer,
    ``owners`` loop). The clone keeps the helper's model, and its part name
    is ``{model}_{index}``, so the model is what FogModWrapper can match in
    the merge-dir MSB to scale the clones like the boss. ``Name`` is the
    vanilla part name (``c3000_9008``, or ``m60_48_55_00-c3160_9000`` on
    open-world tiles); the model is its ``cXXXX`` prefix. Entries of another
    class, or helpers without an owner, are never cloned and are ignored.

    Returns ``{owner_id: sorted unique models}``, empty if the file is
    missing.
    """
    if not enemy_txt_path.exists():
        return {}

    models: dict[int, set[str]] = {}
    name: str | None = None
    klass: str | None = None
    owner: int | None = None

    def flush() -> None:
        if klass == "Helper" and owner is not None and name is not None:
            model = name.rsplit("-", 1)[-1].split("_", 1)[0]
            models.setdefault(owner, set()).add(model)

    with open(enemy_txt_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("- ID:"):
                flush()
                name = klass = owner = None
            elif line.startswith("  Name:"):
                m = _NAME_RE.match(line)
                if m:
                    name = m.group(1)
            elif line.startswith("  Class:"):
                m = _CLASS_RE.match(line)
                if m:
                    klass = m.group(1)
            elif line.startswith("  OwnedBy:"):
                m = _OWNED_BY_RE.match(line)
                if m:
                    owner = int(m.group(1))
    flush()
    return {owner_id: sorted(found) for owner_id, found in models.items()}


def build_helper_models(
    enemy_assignments: Mapping[str, str],
    helper_models: Mapping[int, list[str]],
) -> dict[str, list[str]]:
    """Helper models per arena for graph.json ``helper_models`` (v4.9).

    ``{arena_id: models}`` for every assignment whose source owns helpers
    (``parse_helper_models``); arenas receiving a helper-less boss are
    omitted. FogModWrapper's HelperAreaResolver uses it to give the
    randomizer's helper clones the arena's scaling area.
    """
    result: dict[str, list[str]] = {}
    for arena_id, source_id in enemy_assignments.items():
        models = helper_models.get(int(source_id))
        if models:
            result[arena_id] = list(models)
    return result


def patch_graph_helper_models(
    graph_path: Path, helper_models: dict[str, list[str]]
) -> None:
    """Patch graph.json with the helper_models mapping (v4.9, see build_helper_models).

    Empty or missing mappings leave the file untouched.
    """
    _patch_graph_key(graph_path, "helper_models", helper_models)


def event_map_for_entity(entity_id: int) -> str | None:
    """Map whose EMEVD runs the boss events of a vanilla entity id.

    Entity ids encode their map: 8 digits ``AABBxxxx`` for legacy and minor
    dungeons (``m{AA}_{BB}_00_00``), 10 digits ``WxCCDDxxxx`` for the
    overworld (``m60_{CC}_{DD}_00`` when W is 1, ``m61_...`` when W is 2).
    This is the small tile even when the enemy.txt ``Map:`` (the MSB part's
    map) is a ``_02`` large tile (Fire Giant, Radahn): the boss events live
    in the ``_00`` EMEVD. Returns None for ids of another shape.
    """
    digits = str(entity_id)
    if len(digits) == 8:
        return f"m{digits[0:2]}_{digits[2:4]}_00_00"
    if len(digits) == 10 and digits[0] in "12":
        return f"m6{int(digits[0]) - 1}_{digits[2:4]}_{digits[4:6]}_00"
    return None


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


def _patch_graph_key(graph_path: Path, key: str, value: dict[str, Any]) -> None:
    """Set one top-level graph.json key; an empty value leaves the file untouched."""
    if not value:
        return

    with open(graph_path, encoding="utf-8") as f:
        graph: dict[str, Any] = json.load(f)

    graph[key] = dict(value)

    with open(graph_path, "w", encoding="utf-8") as f:
        json.dump(graph, f, indent=2)


def patch_graph_enemy_assignments(
    graph_path: Path, assignments: dict[str, str]
) -> None:
    """Patch graph.json with the enemy_assignments mapping (v4.5).

    FogModWrapper uses it to locate enemy-randomizer boss placements
    (arena entity id -> source entity id, both decimal strings). Empty
    or missing assignments leave the file untouched.
    """
    _patch_graph_key(graph_path, "enemy_assignments", assignments)


def build_boss_names(
    enemy_assignments: Mapping[str, str],
    placements: Mapping[str, Mapping[str, Any]],
    npc_names: Mapping[int, int],
) -> dict[str, dict[str, str]]:
    """Healthbar names the C# BossNameInjector must patch (graph.json v4.8).

    The enemy randomizer names a relocated boss correctly only when the
    source has a vanilla ``Important.NpcName`` (it copies the source's
    healthbar event); a promoted mob keeps the arena's vanilla name. This
    returns ``{arena_id: {"name", "map"}}`` for exactly those arenas, the
    name being the one already resolved for ``placements`` (spoiler and
    racing overlay) minus any trailing parenthetical (the boss_arena_tags
    disambiguation suffix, "Divine Bird Warrior (Frost)"), and the map the
    arena's EMEVD (``event_map_for_entity``). Arenas whose id encodes no
    map are skipped.
    """
    boss_names: dict[str, dict[str, str]] = {}
    for arena_id, source_id in enemy_assignments.items():
        if int(source_id) in npc_names:
            continue
        map_id = event_map_for_entity(int(arena_id))
        placement = placements.get(arena_id)
        if map_id is None or placement is None:
            continue
        full_name = str(placement["name"])
        name = _PARENTHETICAL_SUFFIX_RE.sub("", full_name).strip() or full_name
        boss_names[arena_id] = {"name": name, "map": map_id}
    return boss_names


def patch_graph_boss_names(
    graph_path: Path, boss_names: dict[str, dict[str, str]]
) -> None:
    """Patch graph.json with the boss_names mapping (v4.8, see build_boss_names).

    Empty or missing names leave the file untouched.
    """
    _patch_graph_key(graph_path, "boss_names", boss_names)


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
