# Enemy Scaling: FogMod Model and the Tier Unclamp

How FogMod rescales enemies between tiers, why it silently clamped SpeedFog's
tiers 22-28 down to 21, and how SpeedFog disables that clamp.

## FogMod's scaling model

Tier numbers are not multipliers. FogMod reads the game's own per-tier area
scaling SpEffects from `SpEffectParam` (`EldenScaling.cs:87-103` in
`reference/fogrando-src/`):

- Tiers 1-20 (base game): rows `7000 + 10 * tier` (7010-7200)
- Tiers 21-34 (DLC): rows `20007000 + 10 * (tier - 21)` (20007000-20007130)

Rescaling an enemy from tier `s` to tier `t` multiplies each stat by
`curve[t] / curve[s]`, where `curve` is the vanilla field value
(`maxHpRate`, `physicsAttackPowerRate`, ...). The curves are strongly
non-linear:

| Tier | maxHpRate | physicsAttackPowerRate |
|------|-----------|------------------------|
| 10   | 3.703     | 2.473                  |
| 16   | 6.875     | 3.640                  |
| 20   | 7.422     | 3.796                  |
| 21   | 7.047     | 3.747                  |
| 25   | 10.031    | 3.774                  |
| 28   | 11.813    | 3.795                  |
| 34   | 15.344    | 3.841                  |

Note the DLC damage curve is flat: DLC enemy damage lives in their base
AtkParam values, halved in vanilla by the player's Scadutree Blessing
(x0.488 damage taken at level 20). The DLC HP curve (+6%/tier) mirrors the
blessing's attack boost (x2.05 at level 20).

Enemies in an area with a `DefeatFlag` (bosses) use a "unique" matrix where
the curves are additionally dampened (`EldenScaling.cs:116-160`): health
divided by `1.275^(tier/15)` up to tier 20, damage divided by
`1.275^(tier/19)` with a x1.5 re-inflation at tier 22 (the `num4 == 21`
branch), after which the per-tier factor flips to `1.5^(-1/15)` so the
divisor keeps shrinking through tier 34. That re-inflation compensates base-game bosses moved to DLC tiers
for the flat damage curve; it creates a +59% damage step between targets 21
and 22 for upscaled base-game bosses.

The generated SpEffect rows start at 7800000, four per `(source, target)`
pair in a deterministic order (pairs iterated `a` 1-34 outer, `b` 1-34
inner, skipping `a == b`; row kinds Fixed, Regular, UniqueFixed,
UniqueRegular). Useful for debugging:
`row = 7800000 + 4 * pairIndex(a, b) + kind`.

## The mixed-mode clamp and its bypass

When `Graph.ExcludeMode == AreaMode.None` (base game and DLC both in the
pool, always the case for SpeedFog), `GameDataWriterE.cs:2161-2171` clamps
every **target** tier 21-28 down to 21 and 29+ down to 22, for all enemies
including DLC bosses. Source tiers are not clamped, so before the unclamp a
DLC boss with vanilla tier 33 placed in a tier-28 area was actually
*downscaled* to tier-21 stats. FogRando's own graph generation also assigns
tiers above 21 and relies on this write-time clamp; the higher values still
feed boss rune rewards (`GameAreaParam.bonusSoul`), which use the unclamped
tier.

The same condition (`flag13`) also neutralizes the Scadutree Blessing and
Revered Spirit Ash player buffs (SpEffectParam rows 20000101-20000120 and
20000201-20000210 overwritten with their level-0 row), since the DLC-tier
enemy curves assume the player does not have them.

SpeedFog wants `final_tier` above 21 to mean what it says, so
`WriteFogMod` (in `FogModWrapper/Program.cs`) sets
`ctx.Graph.ExcludeMode = AreaMode.Base` (the value a `dlconly` run would
have) after `Graph.Construct` and before `Write()`. `ExcludeMode` is read
in exactly four places during `Write()`:

| Site (`GameDataWriterE.cs`) | Effect of `Base` |
|------------------------------|------------------|
| L34 `!= DLC` (data load)     | unchanged (true for both None and Base) |
| L1947 `flag13`               | clamp disabled, blessing neutralization skipped |
| L3098 `tierreq` branch       | dead: SpeedFog never sets `tierreq` |
| L3990 `scadushop` branch     | dead: SpeedFog never sets `scadushop` |

The skipped blessing neutralization is re-applied by
`ScaduBlessingNeutralizer` in the regulation phase (`ApplyRegulation`),
replicating FogMod's row copies. Without it, Scadutree fragments placed by
the item randomizer would buff the player inside DLC-map zones only.

## Difficulty consequences of tiers 22+

- Tiers 21-28 now produce distinct scaling. Reference points for a
  base-game boss with source tier 10 (unique matrix, vs vanilla stats):
  target 21 = HP x2.24 / damage x1.39, target 23 = x2.73 / x2.20,
  target 25 = x3.19 / x2.33, target 28 = x3.75 / x2.54.
- The damage step at target 22 (+59% over 21) is FogMod's hardcoded x1.5
  re-inflation. Smoothing it would require rewriting the generated
  7800000+ rows in `ApplyRegulation` (a possible future
  ScalingMatrixPatcher).
- At equal target tier, a base-game-origin boss keeps a residual x1.18-1.38
  HP advantage over a DLC-origin boss (the dampening only divides source
  tiers <= 20). Base stats and movesets are never normalized.
- Presets using `final_tier` 22+ are now genuinely harder than before the
  unclamp; they were previously capped at effective tier 21.

## Verifying

Generate a seed with `final_tier` above 21, then decode the scaling
SpEffect IDs referenced by the EMEVDs (`tools/dump_emevd_warps` with
`--event all`, scan the raw args for values in 7800000-7804487 and invert
the pair formula). Boss maps must reference `(source, target, UniqueFixed)`
pairs with the graph's actual tiers, e.g. `(33, 28)` for Enir-Ilim Radahn
in a tier-28 area. Blessing neutralization: dump rows 20000110/20000120
from the output regulation.bin and check the `*DamageCutRate` and
`atkPlayerDmgCorrectRate_*` fields are 1.0.
