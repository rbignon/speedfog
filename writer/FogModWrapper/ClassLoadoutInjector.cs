using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Forces starting-class weapons, shields and armor sets onto CharaInitParam,
/// one class group per BaseChrSelectMenuParam menu row (see
/// <see cref="StartingClassRows.ResolveGroups"/>). Produced today only by
/// <c>[tarnished] starting_loadout</c>, but the mechanism itself is
/// pack-agnostic.
///
/// Application model (revised after the 2026-09-01 in-game pass):
/// - Weapons: <c>weapons[i % count]</c> is ALWAYS written to
///   <c>equip_Wep_Right</c> of class group <c>i</c> (covering shuffle
///   wrapping over the classes).
/// - Shields: each entry of <c>shields</c> is placed exactly once, on the
///   next class group in menu order whose <c>equip_Wep_Left</c> slot is
///   occupied; classes with an empty left hand are skipped, mirroring the
///   armor rule below.
/// - Armor: <c>armor_sets[i % count]</c> maps to <c>equip_Helm</c>,
///   <c>equip_Armer</c>, <c>equip_Gaunt</c>, <c>equip_Leg</c>, but each
///   piece only replaces a slot the class already fills (field value not
///   <c>-1</c>): Wretch stays bare, a class without gauntlets keeps none.
///
/// Presence decisions are read from the FIRST row of each group and the
/// resulting writes are applied uniformly to every row (origin, chrInit and
/// its odd twin stay identical, as CharacterWriter keeps them).
///
/// Every other CharaInitParam field is left to CharacterWriter's existing
/// randomization. For each hand slot written, the companion
/// <c>wepParamType_Right1</c>/<c>wepParamType_Left1</c> field is forced to 0
/// (EquipParamWeapon): CharacterWriter (merged item-randomizer output) sets
/// it to 1 when it drew an ash-of-war weapon into the slot; left stale, it
/// makes <see cref="WeaponUpgradeInjector"/> take the EquipParamCustomWeapon
/// path for a raw EquipParamWeapon ID, skipping the upgrade and misreading
/// the weapon. These writes are disjoint from
/// <see cref="StartingRuneInjector"/>'s <c>soul</c> field, per
/// <see cref="RegulationEditor"/>'s co-residency rule (see that file's
/// remarks for the one deliberate, ordered exception with
/// <see cref="WeaponUpgradeInjector"/>).
///
/// MUST run before <see cref="WeaponUpgradeInjector"/> in Phase 7 of
/// Program.cs: forced weapons need to go through the same weapon-upgrade
/// initialization pass as any other starting weapon.
/// </summary>
public static class ClassLoadoutInjector
{
    /// <summary>CharaInitParam equipment fields hold -1 when the slot is empty.</summary>
    private const int EmptySlot = -1;

    /// <summary>
    /// One class's hand-slot replacements, recorded so
    /// <see cref="ClassLoadoutTextPatcher"/> can update the class-selection
    /// equipment text (GR_LineHelp) that CharacterWriter wrote for the
    /// pre-loadout weapons. Shield ids are 0 when no shield was placed.
    /// </summary>
    public record LoadoutSwap(int ClassIndex, int OldWeaponId, int NewWeaponId, int OldShieldId, int NewShieldId);

    /// <summary>
    /// Resolve class groups from the regulation and apply the loadout.
    /// No-op (empty result) when <paramref name="loadout"/> is null or has
    /// no entries at all. Returns the per-class hand-slot swaps for the
    /// class-selection text patch.
    /// </summary>
    public static List<LoadoutSwap> ApplyTo(RegulationEditor reg, ClassLoadoutData? loadout)
    {
        if (loadout == null
            || (loadout.Weapons.Count == 0 && loadout.Shields.Count == 0 && loadout.ArmorSets.Count == 0))
            return new List<LoadoutSwap>();

        var charaParam = reg.GetParam("CharaInitParam")
            ?? throw new InvalidOperationException("CharaInitParam unavailable: cannot apply the starting class loadout");

        Console.WriteLine("Applying starting class loadout (Tarnished Pack showcase)...");

        var groups = StartingClassRows.ResolveGroups(reg);
        var swaps = Apply(charaParam, groups, loadout);

        Console.WriteLine($"  Applied loadout to {swaps.Count} class group(s)");
        return swaps;
    }

    /// <summary>
    /// Apply the loadout to every class group, in menu order. Returns one
    /// <see cref="LoadoutSwap"/> per group touched.
    /// </summary>
    internal static List<LoadoutSwap> Apply(PARAM charaInit, List<List<int>> groups, ClassLoadoutData loadout)
    {
        var rowsById = charaInit.Rows.ToDictionary(r => r.ID);
        var swaps = new List<LoadoutSwap>();
        int nextShield = 0;

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Count == 0 || !rowsById.TryGetValue(group[0], out var decisionRow))
                continue;

            PackItemData? weapon = loadout.Weapons.Count > 0
                ? loadout.Weapons[i % loadout.Weapons.Count]
                : null;

            int oldWeaponId = Convert.ToInt32(decisionRow["equip_Wep_Right"].Value);
            int oldShieldId = Convert.ToInt32(decisionRow["equip_Wep_Left"].Value);

            PackItemData? shield = null;
            if (nextShield < loadout.Shields.Count && oldShieldId != EmptySlot)
            {
                shield = loadout.Shields[nextShield++];
            }

            List<int>? armor = loadout.ArmorSets.Count > 0
                ? loadout.ArmorSets[i % loadout.ArmorSets.Count]
                : null;
            if (armor != null && armor.Count != 4)
            {
                Console.WriteLine($"  Warning: armor set for class row {group[0]} has {armor.Count} entries, expected 4 (head, body, arms, legs); skipping armor for this class");
                armor = null;
            }

            // Presence decisions from the first row, applied uniformly below
            // so origin/chrInit/twin rows stay identical.
            var armorFields = new[] { "equip_Helm", "equip_Armer", "equip_Gaunt", "equip_Leg" };
            var wornPieces = new bool[4];
            if (armor != null)
                for (int p = 0; p < 4; p++)
                    wornPieces[p] = Convert.ToInt32(decisionRow[armorFields[p]].Value) != EmptySlot;

            foreach (var rowId in group)
            {
                if (!rowsById.TryGetValue(rowId, out var row))
                    continue;

                if (weapon != null)
                {
                    row["equip_Wep_Right"].Value = weapon.Id;
                    // CharacterWriter (merged item-randomizer output) may
                    // have left this at 1 (EquipParamCustomWeapon) when it
                    // drew an ash-of-war weapon into the slot. Our loadout
                    // always writes a plain EquipParamWeapon row ID, so
                    // reset the companion type field on every row.
                    row["wepParamType_Right1"].Value = (byte)0;
                }

                if (shield != null)
                {
                    row["equip_Wep_Left"].Value = shield.Id;
                    row["wepParamType_Left1"].Value = (byte)0;
                }

                if (armor != null)
                    for (int p = 0; p < 4; p++)
                        if (wornPieces[p])
                            row[armorFields[p]].Value = armor[p];
            }

            string weaponDesc = weapon != null ? weapon.Name : "none";
            string shieldDesc = shield != null ? shield.Name : "-";
            string armorDesc = armor != null
                ? $"{armor[0]} ({string.Join("", wornPieces.Select(w => w ? "x" : "."))})"
                : "none";
            Console.WriteLine($"  Class row {group[0]}: weapon {weaponDesc}, shield {shieldDesc}, armor {armorDesc}");
            swaps.Add(new LoadoutSwap(
                i,
                weapon != null ? oldWeaponId : 0,
                weapon?.Id ?? 0,
                shield != null ? oldShieldId : 0,
                shield?.Id ?? 0));
        }

        if (nextShield < loadout.Shields.Count)
            Console.WriteLine($"  Warning: {loadout.Shields.Count - nextShield} shield(s) not placed, not enough classes with an occupied left hand");

        return swaps;
    }
}
