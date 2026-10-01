# Boss Arena Compatibility Constraints

**Date:** 2026-04-21
**Status:** Active

SpeedFog restricts boss randomization so that each arena only receives bosses
compatible with its geometry and gameplay constraints (arena size, dragon
feasibility, two-phase space, NPC terrain, Evergaol specifics).

## Source of truth

Tags are ported from [BossArenaRandomizer](https://github.com/ignitesouls/BossArenaRandomize)'s
`Data/bosses.json` and `Data/arenas.json` (formerly `bossArena.json` at the
project root, relocated under `Data/` in the post-overhaul layout). The merged
form lives in `data/boss_arena_tags.json`. Re-run the porter with:

    uv run python tools/port_boss_arena_tags.py \
        --bar-dir ../BossArenaRandomizer/BossArenaRandomizer \
        --out data/boss_arena_tags.json

The porter predates the hand edits the file now carries (exclusions,
promoted entries, `boss.weight` annotations): a re-port drops them, so diff
the output against the committed file and restore them.

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
| `pool` | string | no | `"minor"` or `"major"`. When present, it pins the entry to that pool regardless of any `arena` block. Absent for entries that derive their pool from `clusters.json` (or, when no slot exists there, from `arena.type` via the orphan rule below). |
| `region` | int | yes | BAR's region identifier (1-15). Loaded but not consulted by the current compatibility check. |
| `scaling` | int | yes | BAR's scaling tier. Loaded but not consulted by the current compatibility check. |
| `dlc` | bool | yes | True for DLC (Shadow of the Erdtree) entities. Consulted by `_compose_pool` when `enemy.dlc_bosses = false`: DLC entries are filtered from the candidate pool. Arena selection and per-fight compat checks (size, type, etc.) are unaffected. |

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
| `weight` | number or `{early, mid, late}`, optional (default `1.0`) | no (layer balance) | Expected time to beat the boss in minutes, retries included, per scaling band (tier <= 8, 9-14, >= 15), each band brought to a mid-run tier's scale. A number applies to every band. Absent means "not annotated". |

`weight` is indicative and meaningful in comparison rather than in absolute
terms, like zone weights: the same boss dies faster at low scaling than at
high scaling. Values come from median clear times in the run history,
normalized for the scaling tier the fights were played at, so that weights
compare bosses rather than tiers. Bosses react differently to scaling
(Soldier of Godrick stays at 0.2-0.3 minutes at any tier while Commander
O'Neil goes from about 1.1 to 1.6), hence the bands. `load_tags` rejects a
non-numeric, non-finite or non-positive value, or an object without exactly
the three band keys, and names the entity.

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
perfect matching via augmenting-path bipartite matching (Hungarian-style): it
shuffles both sides for seed-driven variety, then for each arena finds an
augmenting path, re-routing earlier assignments when needed. Runs in O(V*E),
so tight or unsatisfiable compatibility graphs no longer trigger the
exponential blow-up the previous backtracking version could hit. Raises
``MatchingError`` if no perfect matching exists.

The signature is narrowed to the tag blocks the matcher actually needs:

```python
match_arenas_to_bosses(
    *,
    arenas: Mapping[int, ArenaTags],
    bosses: Mapping[int, BossTags],
    rng: random.Random,
    check_size: bool,
    forbidden: Mapping[int, frozenset[int]] | None = None,
) -> dict[int, int]
```

Both matchers also enforce a **no-vanilla-placement rule**, always active:
a boss is never assigned to its own original arena, and a multi-phase
family counts as one identity (Fire Giant phase 2 is forbidden in the
phase-1 slot and vice versa). The ``forbidden`` map (arena_id -> excluded
boss IDs) is built by ``item_randomizer._family_forbidden`` from the
``enemy.txt`` phase mapping. ``match_arenas_to_bosses`` applies it
strictly; an unsatisfiable graph raises ``MatchingError`` (in practice the
pools far exceed the DAG's arena count, so this only matters for
pathological hand-built inputs). Assignments for a given seed differ from
pre-rule versions; the seed derivation (``run_seed ^ 0xBA7A5A5A``) is unchanged.

The caller is responsible for any pool pre-filtering (``exclude_from_pool`` is
applied by ``item_randomizer._compose_pool`` before the matcher runs).

Majors (``major_boss`` clusters) and minors (``boss_arena`` clusters) are
matched independently so their pools do not mix. The RNG is derived from
``run_seed ^ 0xBA7A5A5A`` so the matching is deterministic per run seed
while orthogonal to RandomizerCommon's own RNG.

Strict-error policy: both ``_build_enemy_assignments`` and ``_compose_pool``
raise ``KeyError`` when an entity ID from the DAG or ``clusters.json`` is
missing from ``boss_arena_tags.json`` (or has no ``arena`` block when used as
an arena target). A silent skip there would either leave a vanilla boss in
the run or quietly shrink the source pool, both without any user-visible
signal.

## Multi-phase bosses

Some major bosses are implemented as two distinct entities with a
despawn/respawn transition (Fire Giant, Rennala, Godfrey/Hoarah Loux,
Radagon/Elden Beast). The DAG carries only the leader (phase 2) entity in
`defeat_flag`. Each phase entity is an independent slot in the MSB, so both
must appear in ``EnemyPreset.Enemies`` to avoid the non-listed phase being
randomized incoherently (or remaining vanilla) by RandomizerCommon's
class-based logic.

Phase relationships come from ``writer/ItemRandomizerWrapper/diste/Base/enemy.txt``
(``NextPhase`` field), parsed by ``speedfog/enemy_data.py::parse_boss_phases``.
When a cluster's leader has a phase-1 sibling in that mapping,
``_build_enemy_assignments`` adds the phase-1 entity ID as an additional
arena slot. Both slots are drawn from the same pool without pairing
constraint: Fire Giant's phase 1 can legally receive a single-phase boss.

For symmetry, ``_compose_pool`` also adds each phase-1 entity whose
phase-2 leader is in ``vanilla_ids`` to the pool. This mirrors
BossArenaRandomizer, where every entity is both arena and boss, and
prevents ``|arenas| > |pool|`` deficits caused purely by the phase-1
arena expansion above (e.g. six phase-1 majors widening both sides of the
bipartite graph instead of only the arena side).

## Layer weight balance

On the standard path, a DAG layer never holds both a light and a heavy
extreme boss of its job (minor arenas; major arenas, `final_boss` included in
"all" mode). Per job and weight band, with
`k = max(1, round(boss_extreme_fraction * len(pool)))`, the light extremes
weigh at most the k-th lightest `boss.weight` of the job's candidate pool and
the heavy ones at least the k-th heaviest, ties at the cut included
(`extreme_sets`). Two heavy bosses may share a layer, and an extreme facing
average bosses stays allowed: only opposite extremes are kept apart, which
is the owner's rule ("two extremes must not meet"). `[enemy]
boss_extreme_fraction` defaults to 0.10; 0 disables. A pool whose weights
are all equal (no annotation) has no extremes.

The band is the layer's: all boss nodes of a layer share one scaling tier
(checked on 27,214 race layers), and `weight_band` maps it to early
(<= 8), mid (9-14) or late (>= 15). Bosses react differently to scaling, so
the same pair can be fine early and unfair late: Commander O'Neil is an
average minor early but a heavy extreme mid and late, so he never meets
Soldier of Godrick (a light extreme in every band) past tier 8.

`main.py`'s `post_validate` passes `boss_layers` and `boss_tiers` (cluster
ID -> layer, -> tier) to `generate_item_config`. `_build_enemy_assignments`
groups the arena slots of each job by layer (a phase-1 slot takes its
leader's layer), tags each group with its band, and calls
`match_arenas_balanced`, which samples by rejection: it runs the unchanged
`match_arenas_to_bosses` and accepts the matching when no group holds both
extremes, else draws again with the same RNG, up to `BALANCE_ATTEMPTS` (50).
The accepted matching follows the plain matcher's distribution conditioned
on the rule, so an extreme boss only loses the combinations the rule
forbids. A job the plain matcher cannot solve fails at once with the plain
error; exhausted attempts raise a `MatchingError` naming the rule, which
`post_validate` turns into a `GenerationError` (reroll in auto mode, clear
error with a fixed seed). The first draw is exactly the plain matching, so a
seed whose plain matching already satisfies the rule keeps its assignments.

Measured with the 2026-10-01 band weights (150 `standard.toml` DAGs per
mode, one unseeded sample, rule off and on run on the same DAGs): the
extremes are 15-16 minors and 5-7 majors per side and band; no DAG reroll,
at most 10 draws per job; in "all" mode 2.5% of major and 6.3% of minor
placements move, Fire Giant 2 90 -> 71 appearances is the most affected
major, no boss loses more than 50%; in "minor" mode 6.0% of minor
placements move. Soldier of Godrick vs Commander O'Neil is allowed early
(O'Neil 1.13 is just below the early heavy cut of 1.15) and blocked mid and
late; Malenia vs Leonine Misbegotten is blocked in every band.

Limitation: balance is per slot, so a two-slot node (Fire Giant) next to a
one-slot node on the same layer still means two fights against one. That
imbalance belongs to the zone weight.

### Calibration

`boss.weight` values come from `speedfog-racing/tools/extract_boss_weights.py`
run on the race database (band run 2026-10-01: 65,476 minor and 7,847 major
single-slot clears; all 200 pool bosses measured, 197 on at least 20
clears). The tool fits log clear time = arena + boss per band + tier +
player + pool per job by median polish. Per band, the observation-weighted
median boss-band effect is the band's common shift (the scaling everyone
feels); it is removed and the mid band's added back, so a band weight reads
as minutes at a mid-run tier and only the boss's own sensitivity to scaling
varies across bands. Bosses are attributed exactly from
`enemy_assignments`, else from a unique `randomized_bosses` name; ambiguous
names (e.g. "Tree Sentinel", shared with the duo) and multi-slot nodes are
dropped. The boss's overall effect is shrunk toward the pool median
(k = sigma^2 / tau^2, about 0.7-0.9), each band toward that overall with its
own k_band = sigma^2 / tau_band^2 (about 35 for minors and 11 for majors:
band deviations vary far less across bosses than bosses do); a band with
fewer than 20 clears takes the boss's overall value, and a boss with fewer than 20 clears takes each band's pool
median (Ghostflame Dragon Gravesite, Godfrey First Elden Lord, Divine Beast
Dancing Lion: their names are shared with other entities, so only
`enemy_assignments` seeds count). Weights are rounded to 0.01 minute: the
extremes rule counts every boss tied at its cut, and a 0.1 grid inflated the
minor extremes to 37 light instead of 15. Re-run the tool as races
accumulate; `--write` only touches `boss.weight`.

## Pool composition

``_compose_pool`` routes each entity into the major or minor candidate
pool with four ranked branches:

1. **Vanilla IDs** for the requested ``kind``: entity IDs of
   ``clusters.json`` bosses whose ``cluster.type`` is ``major_boss`` (→
   major) or ``boss_arena`` (→ minor).
2. **Phase-1 siblings** of vanilla leaders, via ``phase_mapping`` from
   ``enemy.txt`` (see the multi-phase section below).
3. **Source-only entries** with an explicit ``pool`` field. ``pool`` is
   authoritative: it overrides ``arena.type`` for entries that carry both
   blocks.
4. **Orphan arena fallback**: an entry with an ``arena`` block but no
   ``pool`` field and no slot in ``clusters.json`` joins the major pool
   when ``arena.type == 2`` (BAR's "walled arena" category) and the minor
   pool otherwise. This is what brings BAR vanilla bindings that have no
   matching SpeedFog cluster (DLC field bosses without ``BossTrigger``,
   evergaol variants, alternate-trigger IDs, Leda fight phases, multi-
   phase final boss splits, etc.) into the candidate pool. Entries with
   ``boss.exclude_from_pool = true`` stay out, which is how duplicates
   (Night's Cavalry, Deathbird, Death Rite Bird aliases) are kept from
   polluting the pool.

``boss.exclude_from_pool`` applies to every branch. A boss can be both
a vanilla arena target and excluded as a candidate, in which case its
arena slot is still filled by *some other* compatible boss.

The same "drop-from-pool, keep-as-arena" rule applies to the entity-
level ``dlc`` flag when ``[enemy].dlc_bosses = false``: DLC entries are
skipped at every insertion branch above, but arena selection in
``_build_enemy_assignments`` is untouched. A DLC arena that ends up in
the DAG (because its cluster was selected by ``clusters.json``) still
gets a non-DLC replacement boss.

## Config flags

| Flag | Effect |
|------|--------|
| ``[enemy].randomize_bosses = "none"`` | No assignment computed. Boss randomization disabled entirely. |
| ``[enemy].randomize_bosses = "minor"`` | Only ``boss_arena`` clusters receive arena-matched bosses. Majors stay vanilla. |
| ``[enemy].randomize_bosses = "all"`` | Both majors and minors receive arena-matched bosses. ``final_boss`` terminals (Elden Beast / Promised Consort Radahn) are also treated as major arena targets: each receives an arena-compatible boss and is reported in ``randomized_bosses``/``boss_name``. (They also remain candidate sources in the major pool via the orphan-arena fallback, as before, so they may appear as mid-run replacements like any other major.) |
| ``[enemy].ignore_arena_size`` | Skip the size gate. Other rules still apply. |
| ``[enemy].dlc_bosses = false`` | Filter DLC entries from the candidate pool. Arena selection is untouched (DLC arenas still get a non-DLC replacement). Independent of ``[item_randomizer].dlc``, which controls item-randomizer scope. |
| ``[enemy].boss_extreme_fraction`` | Share of each candidate pool counted as light, and as heavy, extremes per weight band (default 0.10); a layer never holds both. ``0`` disables. See "Layer weight balance". Ignored in allowlist mode. |

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
  boss source entity ID. Threaded into ``EnemyPreset.Enemies`` by
  ``ItemRandomizerWrapper``, which short-circuits class-based randomization
  for those specific slots via ``forceMap`` in
  ``EnemyRandomizer.cs:1846-1849``.

The source pool membership (which entities can be placed as replacements)
and exclusions (entities that cannot be sources) live entirely in
``data/boss_arena_tags.json`` and are applied in the Python matcher before
emission. The C# side no longer carries hardcoded promoted-pool data.

The ``randomized_bosses`` and ``boss_name`` fields on graph nodes are populated
in Python from the same ``enemy_assignments`` map (via ``output.build_boss_placements``
and ``patch_graph_boss_placements`` in ``main.py``). Boss display names resolve
through ``enemy.txt`` ``Important.Names.Key`` (the canonical name), then the
legacy ``ExtraName``, then a ``boss_arena_tags.json`` name as final fallback.
The C# ``ItemRandomizerWrapper`` consumes ``enemy_assignments`` only for ``forceMap``
wiring and emits no placement data back to Python.

## How RandomizerCommon honors the assignments

See ``RandomizerCommon/Preset.cs:1259-1299`` (ProcessEnemyPreset) and
``EnemyRandomizer.cs:1846-1849`` (forceMap). Each ``{target: source}`` entry
resolves to ``forceMap[target_entity_id] = source_entity_id``, short-circuiting
the class-based pool randomization for those specific slots. The MinorBoss
class merge logic in ``BuildEnemyPreset`` still applies to arenas NOT listed
in ``Enemies``.

## Boss allowlist / pinning (`enemy.bosses`)

When `enemy.bosses` is non-empty, boss assignment switches to a uniform mode:

- The allowlist names are resolved by case-insensitive substring against the
  boss display name (`resolve_boss_allowlist`); each name must match exactly
  one entity, else generation aborts with a clear error.
- The major/minor split is collapsed: every randomized arena (minor always,
  major when `randomize_bosses = "all"`) draws from the single allowlist pool.
- Reuse is permitted (`assign_bosses_uniform`, least-used-first), so a one-boss
  allowlist fills every arena (a "Malenia only" run).
- The allowlist is authoritative: a listed boss is included even if tagged
  `exclude_from_pool` or DLC with `dlc_bosses = false`.
- `ignore_arena_size` stays orthogonal. Non-size constraints
  (`two_phase_not_allowed`, `dragon_not_allowed`, `npc_not_allowed`,
  `is_escapable`) still apply, so an arena that cannot host the pinned boss
  triggers a reroll (auto) or a clear
  `MatchingError` (fixed seed). Malenia is two-phase, so arenas tagged
  `two_phase_not_allowed` reject her even with `ignore_arena_size`.
- The no-vanilla-placement rule is best-effort in this mode: a non-self
  candidate is preferred when one is compatible, but when every compatible
  candidate is forbidden the rule is waived for that arena instead of
  failing. A "Malenia only" run stays generable even when Malenia's own
  arena is in the DAG.
- The layer weight balance does not apply: the allowlist stays uniform and
  authoritative.

Example "Malenia only":

    [enemy]
    randomize_bosses = "all"
    ignore_arena_size = true
    bosses = ["Malenia"]

## Promoted allowlist-only sources

Three skeleton entities are hand-promoted into the minor pool as
allowlist-only sources (Halloween mode):

| Entity ID | Name | `dlc` |
|-----------|------|-------|
| `11000295` | Skeletal Militiaman | `false` |
| `31190300` | Giant Skeleton | `false` |
| `43010200` | Shadow Skeleton | `true` |

Each entry carries `pool: "minor"` and `boss.exclude_from_pool: true`. The
`exclude_from_pool` flag makes them inert on the standard (no-allowlist)
path: `_compose_pool` drops `exclude_from_pool` entries at every branch, so
these three never enter the minor candidate pool for ordinary
`randomize_bosses = "minor"`/`"all"` runs. They become reachable only
through the `enemy.bosses` allowlist, which is authoritative and includes a
listed boss even when tagged `exclude_from_pool`:

    [enemy]
    randomize_bosses = "all"
    bosses = ["Skeletal Militiaman", "Giant Skeleton", "Shadow Skeleton"]

These entries are hand-added, not sourced from BossArenaRandomizer's data.
Re-running `tools/port_boss_arena_tags.py` overwrites
`data/boss_arena_tags.json` without them; re-add them after a re-port, or
the `TestPromotedSkeletons` tests will fail.

A fourth entity follows the same pattern with a `boss.size` lever in
addition to `exclude_from_pool`:

| Entity ID | Name | `size` | `dlc` |
|-----------|------|--------|-------|
| `2049420200` | Aging Untouchable | `2` | `true` |

`boss.size: 2` excludes only size-1 arenas from the compatibility match (the
tag model has no dedicated "open arena" flag). Reachable the same way:

    [enemy]
    randomize_bosses = "all"
    bosses = ["Aging Untouchable"]

Placing it also activates `UntouchableBossInjector` in FogModWrapper (a
regulation-side `NpcParam`/`SpEffectParam` clone plus a post-Write MSB
repoint of the placed part); see `docs/untouchable-boss.md` for the
vulnerability mechanism and the injector's two phases.
