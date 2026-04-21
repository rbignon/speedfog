# Boss Arena Compatibility Constraints

**Date:** 2026-04-21
**Status:** Active

SpeedFog restricts boss randomization so that each arena only receives bosses
compatible with its geometry and gameplay constraints (arena size, dragon
feasibility, two-phase space, NPC terrain, Evergaol specifics).

## Source of truth

Tags are ported from [BossArenaRandomizer](https://github.com/thefifthmatt/BossArenaRandomizer)'s
`bosses.json` and `bossArena.json`. The merged form lives in
`data/boss_arena_tags.json`. Re-run the porter with:

    uv run python tools/port_boss_arena_tags.py \
        --bar-dir ../BossArenaRandomizer/BossArenaRandomizer \
        --out data/boss_arena_tags.json

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
