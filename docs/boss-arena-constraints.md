# Boss Arena Compatibility Constraints

**Date:** 2026-04-21
**Status:** Active

SpeedFog restricts boss randomization so that each arena only receives bosses
compatible with its geometry and gameplay constraints (arena size, dragon
feasibility, two-phase space, NPC terrain, Evergaol specifics).

## Source of truth

Tags are ported from [BossArenaRandomizer](https://github.com/ignitesouls/BossArenaRandomize)'s
`bosses.json` and `bossArena.json`. The merged form lives in
`data/boss_arena_tags.json`. Re-run the porter with:

    uv run python tools/port_boss_arena_tags.py \
        --bar-dir ../BossArenaRandomizer/BossArenaRandomizer \
        --out data/boss_arena_tags.json

## Data model

`data/boss_arena_tags.json` is a JSON object keyed by **entity ID string**
(the ID found in the game's MSB, e.g. `"18000850"`). Each value is a per-entity
record.

### Entity record

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Human-readable name (from BAR, or the `ExtraMinorBossPoolIds` C# comment for source-only entries). |
| `boss` | object | yes | Boss tags, see below. Describes the entity in its "source" role. |
| `arena` | object | no | Arena tags, see below. Present only for entities that also correspond to an arena slot in the MSB (vanilla boss bindings). Absent for source-only promoted entries. |
| `pool` | string | no | `"minor"` or `"major"`. Declares pool membership for source-only entries (no `arena`). Absent for entries with an `arena` block: their pool is derived from `cluster.type` in `clusters.json`. |
| `region` | int | yes | BAR's region identifier (1-15). Loaded but not consulted by the current compatibility check. |
| `scaling` | int | yes | BAR's scaling tier. Loaded but not consulted by the current compatibility check. |
| `dlc` | bool | yes | True for DLC entities. Loaded but not consulted by the current compatibility check. |

### `boss` block

Describes what the entity **is**, for compat-filtering when it is a source.

| Field | Type | Used by compat? | Description |
|-------|------|-----------------|-------------|
| `size` | int (1-5) | yes (size gate) | Boss physical footprint. |
| `type` | int (1-7) | no | BAR category. Loaded but not consulted. |
| `is_two_phase` | bool | yes | Boss scripts a phase transition in place. |
| `is_dragon` | bool | yes | Boss is a dragon-type encounter. |
| `is_npc` | bool | yes | Boss is an NPC invader. |
| `can_escape` | bool | yes | Boss has scripted flee/despawn behavior. |
| `night_boss` | bool | no | Boss is a night-only encounter. Loaded but not consulted. |
| `exclude_from_pool` | bool | yes (source filter) | When `true`, this entity is never chosen as a source by the matcher. Its own arena can still receive another boss. |

### `arena` block

Describes what the slot **requires**, for compat-filtering when it is a target.

| Field | Type | Used by compat? | Description |
|-------|------|-----------------|-------------|
| `size` | int (1-5) | yes (size gate) | Arena physical capacity (boss size must fit). |
| `type` | int (1-7) | no | BAR category. Loaded but not consulted. |
| `two_phase_not_allowed` | bool | yes | Arena geometry or scripting cannot host a two-phase boss. |
| `dragon_not_allowed` | bool | yes | Arena too confined or mis-shaped for a dragon. |
| `npc_not_allowed` | bool | yes | Arena cannot host an NPC invader fight. |
| `is_escapable` | bool | yes | Arena has an exit path the player can use; a boss that can flee would break the encounter. |
| `night_boss` | bool | no | Arena is a night-only trigger. Loaded but not consulted. |

### Examples

Vanilla binding (entity is both a source and a target):

```json
"18000850": {
  "name": "Soldier of Godrick",
  "boss": {"size": 1, "type": 4, "is_two_phase": false, "is_dragon": false,
           "is_npc": false, "can_escape": false, "night_boss": false,
           "exclude_from_pool": false},
  "arena": {"size": 3, "type": 4, "two_phase_not_allowed": false,
            "dragon_not_allowed": false, "npc_not_allowed": false,
            "is_escapable": false, "night_boss": false},
  "region": 1, "scaling": 1, "dlc": false
}
```

Source-only promoted entry (field enemy tagged for minor-boss placement, no
arena of its own, neutral boss defaults that fit everywhere):

```json
"1051400299": {
  "name": "Guardian Golem",
  "boss": {"size": 1, "type": 1, "is_two_phase": false, "is_dragon": false,
           "is_npc": false, "can_escape": false, "night_boss": false,
           "exclude_from_pool": false},
  "pool": "minor",
  "region": 0, "scaling": 0, "dlc": false
}
```

Excluded archetype (vanilla boss whose archetype does not replay well as a
random replacement; still a valid target for other bosses):

```json
"1043370340": {
  "name": "Night's Cavalry Limgrave",
  "boss": {"size": 1, "type": 3, "is_two_phase": false, "is_dragon": false,
           "is_npc": false, "can_escape": false, "night_boss": true,
           "exclude_from_pool": true},
  "arena": {"size": 3, "type": 3, "two_phase_not_allowed": false,
            "dragon_not_allowed": false, "npc_not_allowed": false,
            "is_escapable": false, "night_boss": true},
  "region": 1, "scaling": 4, "dlc": false
}
```

## Compatibility rules

Given arena ``A`` and candidate boss ``B``, they are compatible iff all of:

| Rule | Trigger |
|------|---------|
| Dragon fit | ``not (A.dragon_not_allowed and B.is_dragon)`` |
| Two-phase fit | ``not (A.two_phase_not_allowed and B.is_two_phase)`` |
| NPC terrain | ``not (A.npc_not_allowed and B.is_npc)`` |
| Escape path | ``not (A.is_escapable and B.can_escape)`` |
| Size fit (optional) | ``B.size <= A.size`` when ``[enemy].ignore_arena_size = false`` |

BAR's C# code declares additional flags (`isMessmer`, `isMaliketh`,
`isGodskinDuo`, `isEvergaolIncompatible`, `isHard`) but no source data
populates them. They are omitted here. Use the future `exclude_bosses`
mechanism if per-boss arena restrictions become necessary.

Fields marked "no" in the per-block tables above (`type`, `night_boss`,
`region`, `scaling`, plus the entity-level `dlc`) are preserved for
round-trip fidelity with BossArenaRandomizer. Future rules can reference them
without schema migration.

## Matching algorithm

`speedfog/boss_arena_constraints.py::match_arenas_to_bosses` runs a random
perfect matching via greedy + backtracking. Majors (``major_boss`` clusters)
and minors (``boss_arena`` clusters) are matched independently so their pools
do not mix. The RNG is derived from ``run_seed ^ 0xBA7A5A5A`` so the matching
is deterministic per run seed while orthogonal to RandomizerCommon's own RNG.

## Multi-phase bosses

Some major bosses are implemented as two distinct entities with a
despawn/respawn transition (Fire Giant, Rennala, Godfrey/Hoarah Loux,
Radagon/Elden Beast). The DAG carries only the leader (phase 2) entity in
`defeat_flag`. Each phase entity is an independent slot in the MSB, so both
must appear in ``Preset.Enemies`` to avoid the non-listed phase being
randomized incoherently (or remaining vanilla) by RandomizerCommon's
class-based logic.

Phase relationships come from ``writer/ItemRandomizerWrapper/diste/Base/enemy.txt``
(``NextPhase`` field), parsed by ``speedfog/output.py::parse_boss_phases``.
When a cluster's leader has a phase-1 sibling in that mapping,
``_build_enemy_assignments`` adds the phase-1 entity ID as an additional
arena slot. Both slots are drawn from the same pool without pairing
constraint: Fire Giant's phase 1 can legally receive a single-phase boss.

## Config flags

| Flag | Effect |
|------|--------|
| ``[enemy].randomize_bosses = "none"`` | No assignment computed. Boss randomization disabled entirely. |
| ``[enemy].randomize_bosses = "minor"`` | Only ``boss_arena`` clusters receive arena-matched bosses. Majors stay vanilla. |
| ``[enemy].randomize_bosses = "all"`` | Both majors and minors receive arena-matched bosses. |
| ``[enemy].ignore_arena_size`` | Skip the size gate. Other rules still apply. |

## Wire format

``item_config.json`` gains one optional field:

```json
{
  "seed": 123,
  "enemy_assignments": {
    "18000850": "10000850",
    "1042360800": "1043360800"
  }
}
```

- ``enemy_assignments``: arena entity ID (vanilla boss slot in the MSB) ->
  boss source entity ID. Threaded into ``Preset.Enemies`` by
  ``ItemRandomizerWrapper``, which short-circuits class-based randomization
  for those specific slots via ``forceMap`` in
  ``EnemyRandomizer.cs:1846-1849``.

The source pool membership (which entities can be placed as replacements)
and exclusions (entities that cannot be sources) live entirely in
``data/boss_arena_tags.json`` and are applied in the Python matcher before
emission. The C# side no longer carries hardcoded promoted-pool data.

## How RandomizerCommon honors the assignments

See ``RandomizerCommon/Preset.cs:1259-1299`` (ProcessEnemyPreset) and
``EnemyRandomizer.cs:1846-1849`` (forceMap). Each ``{target: source}`` entry
resolves to ``forceMap[target_entity_id] = source_entity_id``, short-circuiting
the class-based pool randomization for those specific slots. The MinorBoss
class merge logic in ``BuildEnemyPreset`` still applies to arenas NOT listed
in ``Enemies``.
