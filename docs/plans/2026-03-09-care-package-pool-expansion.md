# Care Package Pool Expansion — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove somber weapons from the care package pool, expand standard weapons to ~40 items covering all weapon categories, expand armor to ~15 per slot, and simplify the weapon sampling code.

**Architecture:** The weapon pool changes from a `{standard: [], somber: []}` dict to a flat list (like shields). The `sample_weapons()` inner function is replaced by `sample_standard_weapons()`. Catalysts keep their standard/somber sub-pools unchanged. The TOML pool file gets new items with IDs from EquipParamWeapon/EquipParamProtector.

**Tech Stack:** Python 3.10+, TOML (care_package_items.toml), pytest

**ID source:** All IDs verified against `eldenring_all-in-one_Hexinton-v5.0_ce7.5.ct` (Cheat Engine table).

---

### Task 1: Flatten weapon pool in TOML — remove somber, add standard weapons

**Files:**
- Modify: `data/care_package_items.toml`

**Step 1: Remove `weapons.somber` section and convert `weapons.standard` to flat `weapons` list**

Replace the current weapon sections with a flat `[[weapons]]` list. Remove all 12 somber entries (Reduvia, Crystal Knife, Moonveil, Bloodhound's Fang, Sword of Night and Flame, Ornamental Straight Sword, Blasphemous Blade, Rivers of Blood, Wing of Astel, Bolt of Gransax, Dark Moon Greatsword, Eleonora's Poleblade).

Keep all 18 existing standard weapons, changing `[[weapons.standard]]` to `[[weapons]]`.

Add new standard weapons to cover missing/underrepresented categories. All IDs are base EquipParamWeapon row IDs.

New weapons to add (24 items, target ~42 total):

**Curved Swords** (currently 0):
- Scimitar: 7140000
- Falchion: 7000000
- Shamshir: 7030000

**Axes** (currently 0):
- Battle Axe: 14000000
- Highland Axe: 14100000

**Great Axes** (currently 0):
- Greataxe: 15000000
- Crescent Moon Axe: 15030000

**Great Hammers** (currently 0):
- Large Club: 12000000
- Great Stars: 12180000

**Colossal Weapons** (currently 0):
- Giant-Crusher: 23110000
- Duelist Greataxe: 23040000

**Colossal Swords** (currently 0):
- Greatsword: 4000000
- Zweihander: 4040000

**Curved Greatswords** (currently 0):
- Dismounter: 8020000
- Omen Cleaver: 8060000

**Whips** (currently 0):
- Whip: 20000000
- Thorned Whip: 20020000

**Fists** (currently 0):
- Caestus: 21000000
- Spiked Caestus: 21010000

**Halberds** (add 2, currently 1):
- Nightrider Glaive: 18050000
- Guardian's Swordspear: 18110000

**Spears** (add 2, currently 1):
- Pike: 16070000
- Partisan: 16050000

**Hammers** (add 1, currently 1):
- Mace: 11000000

**Twinblades** (add 1, currently 1):
- Twinned Knight Swords: 10030000

Total: 18 existing + 24 new = 42 weapons.

**Step 2: Commit**

```bash
git add data/care_package_items.toml
git commit -m "data: remove somber weapons, expand standard pool to 42 weapons"
```

---

### Task 2: Expand armor pools to ~15 per slot

**Files:**
- Modify: `data/care_package_items.toml`

**Step 1: Add armor pieces to each slot**

Add ~9 new pieces per slot for variety. Mix light/medium/heavy, individual pieces (not full sets). IDs are EquipParamProtector row IDs.

**Head** (6 → 15):
- Banished Knight Helm: 200000
- Vagabond Knight Helm: 660000
- Confessor Hood: 880000
- Land of Reeds Helm: 870000
- Carian Knight Helm: 980000
- Astrologer Hood: 630000
- Radahn's Redmane Helm: 470000
- Cleanrot Helm: 340000
- Twinned Helm: 600000

**Body** (6 → 15):
- Banished Knight Armor: 200100
- Vagabond Knight Armor: 660100
- Confessor Armor: 880100
- Land of Reeds Armor: 870100
- Carian Knight Armor: 980100
- Astrologer Robe: 630100
- Radahn's Lion Armor: 470100
- Cleanrot Armor: 340100
- Twinned Armor: 600100

**Arms** (6 → 15):
- Banished Knight Gauntlets: 200200
- Vagabond Knight Gauntlets: 660200
- Confessor Gloves: 880200
- Land of Reeds Gauntlets: 870200
- Carian Knight Gauntlets: 980200
- Astrologer Gloves: 630200
- Radahn's Gauntlets: 470200
- Cleanrot Gauntlets: 340200
- Twinned Gauntlets: 600200

**Legs** (6 → 15):
- Banished Knight Greaves: 200300
- Vagabond Knight Greaves: 660300
- Confessor Boots: 880300
- Land of Reeds Greaves: 870300
- Carian Knight Greaves: 980300
- Astrologer Trousers: 630300
- Radahn's Greaves: 470300
- Cleanrot Greaves: 340300
- Twinned Greaves: 600300

**Step 2: Commit**

```bash
git add data/care_package_items.toml
git commit -m "data: expand armor pools from 6 to 15 per slot"
```

---

### Task 3: Simplify weapon sampling code

**Files:**
- Modify: `speedfog/care_package.py`
- Test: `tests/test_care_package.py`

**Step 1: Update tests to reflect new flat weapon pool**

In `tests/test_care_package.py`:

a) `test_loads_real_pool` (line 90-99): Change assertions from `weapons.standard`/`weapons.somber` to flat `weapons` list:
```python
def test_loads_real_pool(self):
    pool_path = Path(__file__).parent.parent / "data" / "care_package_items.toml"
    if not pool_path.exists():
        pytest.skip("data/care_package_items.toml not found")
    pool = load_item_pool(pool_path)
    assert "weapons" in pool
    assert isinstance(pool["weapons"], list)
    assert len(pool["weapons"]) >= 30
```

b) `test_pool_items_have_name_and_id` (line 116-125): Change to iterate flat list:
```python
def test_pool_items_have_name_and_id(self):
    pool_path = Path(__file__).parent.parent / "data" / "care_package_items.toml"
    if not pool_path.exists():
        pytest.skip("data/care_package_items.toml not found")
    pool = load_item_pool(pool_path)
    for weapon in pool["weapons"]:
        assert "name" in weapon
        assert "id" in weapon
        assert isinstance(weapon["id"], int)
```

c) `test_weapon_upgrade_applied` (line 202-231): Simplify — weapons are always standard now:
```python
def test_weapon_upgrade_applied(self, pool_path: Path):
    """Weapons should have upgrade level encoded in their ID."""
    config = CarePackageConfig(
        enabled=True,
        weapon_upgrade=8,
        weapons=1,
        shields=0,
        catalysts=0,
        talismans=0,
        sorceries=0,
        incantations=0,
        head_armor=0,
        body_armor=0,
        arm_armor=0,
        leg_armor=0,
        crystal_tears=0,
        ashes_of_war=0,
    )
    items = sample_care_package(config, seed=42, pool_path=pool_path)
    assert len(items) == 1
    weapon = items[0]
    assert weapon.type == ITEM_TYPE_WEAPON
    # Weapons are always standard upgrade now
    upgrade_in_id = weapon.id % 100
    assert upgrade_in_id == 8, f"Expected +8, got +{upgrade_in_id}"
    assert "+8" in weapon.name
```

**Step 2: Run tests, verify failures**

```bash
pytest tests/test_care_package.py::TestLoadItemPool::test_loads_real_pool tests/test_care_package.py::TestLoadItemPool::test_pool_items_have_name_and_id tests/test_care_package.py::TestSampleCarePackage::test_weapon_upgrade_applied -v
```

Expected: `test_loads_real_pool` fails (pool["weapons"] is now a list, not dict).

**Step 3: Update `sample_care_package()` in `care_package.py`**

Replace the `sample_weapons` inner function (lines 148-176) and its call (line 179) with `sample_standard_weapons`:

```python
    # Weapons (flat standard pool)
    sample_standard_weapons(pool.get("weapons", []), config.weapons)
```

The existing `sample_standard_weapons` inner function (lines 182-200) already does exactly what we need for a flat list with standard upgrade.

**Step 4: Run all tests**

```bash
pytest tests/test_care_package.py -v
```

Expected: All pass.

**Step 5: Commit**

```bash
git add speedfog/care_package.py tests/test_care_package.py
git commit -m "refactor: flatten weapon pool, remove somber weapon sampling"
```

---

### Task 4: Update documentation

**Files:**
- Modify: `docs/care-package.md`

**Step 1: Update the doc**

In `docs/care-package.md`:

a) Remove `Weapons (somber)` row from the Categories table (line 21).

b) Change `Weapons (standard)` row to just `Weapons` with TOML key `weapons` (line 20).

c) Update the text at line 35: remove "Weapons and catalysts have sub-pools... merged before sampling" — only catalysts have sub-pools now.

d) In "Weapon Upgrade Calculation" section (line 60-77): clarify that somber upgrade only applies to catalysts, not weapons.

**Step 2: Commit**

```bash
git add docs/care-package.md
git commit -m "docs: update care package doc for flat weapon pool"
```

---

### Task 5: Run full test suite and verify

**Step 1: Run Python tests**

```bash
pytest -v
```

Expected: All pass.

**Step 2: Run C# tests**

```bash
cd writer/FogModWrapper.Tests && dotnet test
```

Expected: All pass (C# side is unaffected — it just reads care_package items from graph.json).
