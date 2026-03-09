# Care Package Pool Expansion

## Problem

Item pools in `data/care_package_items.toml` are too small, causing players to see
the same items repeatedly across runs. Somber weapons are also problematic: they come
with fixed weapon arts (potentially broken), can't use Ashes of War, and their power
level is subjective and contentious.

## Design

### 1. Weapons: remove somber, expand standard

Remove `weapons.somber` (12 items). Expand `weapons.standard` from 18 to ~40 items,
covering underrepresented weapon categories:

- Currently missing: axes, colossal weapons, colossal swords, curved swords, whips,
  fists, great hammers
- Currently thin: halberds (1), spears (1), twinblades (1), hammers (1)

Standard weapons are neutral: the player chooses their power level via infusion and
Ash of War. This avoids the "broken weapon" debate entirely.

### 2. Code: simplify weapon sampling

The `weapons` pool becomes a flat list (like `shields`) instead of `standard`/`somber`
sub-pools. `sample_weapons()` is replaced by `sample_standard_weapons()`.

`_somber_upgrade` is kept for catalysts (which retain their somber sub-pool since
catalysts are never infusable regardless of upgrade path).

### 3. Armor: expand to ~15 per slot

Expand from 6 to ~15 items per armor slot (head/body/arm/leg). Mix of light, medium,
and heavy pieces chosen individually (not as complete sets).

### 4. Unchanged

- Catalysts (standard + somber, never infusable)
- Shields (already standard-only)
- Talismans (25), sorceries (10), incantations (10), crystal tears (10), ashes (13)
- Pipeline: graph.json format, C# StartingItemInjector, _somber_upgrade for catalysts
