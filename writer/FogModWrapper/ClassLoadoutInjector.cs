using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Forces starting-class hand items (weapons/shields) and armor sets onto
/// CharaInitParam, one loadout entry per class group in
/// BaseChrSelectMenuParam order (see <see cref="StartingClassRows.ResolveGroups"/>),
/// wrapping with modulo when the loadout has fewer entries than there are
/// class groups: <c>seq[i % seq.Count]</c>, with the hand-item index and the
/// armor-set index each wrapping independently against their own list
/// length. Produced today only by <c>[tarnished] starting_loadout</c>, but
/// the mechanism itself is pack-agnostic.
///
/// Every other CharaInitParam field is left to CharacterWriter's existing
/// randomization. This injector writes only <c>equip_Wep_Right</c>,
/// <c>equip_Wep_Left</c>, <c>equip_Helm</c>, <c>equip_Armer</c>,
/// <c>equip_Gaunt</c>, <c>equip_Leg</c>, disjoint from
/// <see cref="StartingRuneInjector"/>'s <c>soul</c> field, per
/// <see cref="RegulationEditor"/>'s co-residency rule that injectors sharing
/// a PARAM in the same Open/Save block must write disjoint fields.
///
/// MUST run before <see cref="WeaponUpgradeInjector"/> in Phase 7 of
/// Program.cs: forced weapons need to go through the same weapon-upgrade
/// initialization pass as any other starting weapon.
/// </summary>
public static class ClassLoadoutInjector
{
    /// <summary>
    /// Resolve class groups from the regulation and apply the loadout.
    /// No-op when <paramref name="loadout"/> is null, or has neither hand
    /// items nor armor sets (nothing to write).
    /// </summary>
    public static void ApplyTo(RegulationEditor reg, ClassLoadoutData? loadout)
    {
        if (loadout == null || (loadout.HandItems.Count == 0 && loadout.ArmorSets.Count == 0))
            return;

        var charaParam = reg.GetParam("CharaInitParam");
        if (charaParam == null)
            return;

        Console.WriteLine("Applying starting class loadout (Tarnished Pack showcase)...");

        var groups = StartingClassRows.ResolveGroups(reg);
        int updated = Apply(charaParam, groups, loadout);

        Console.WriteLine($"  Applied loadout to {updated} class group(s)");
    }

    /// <summary>
    /// Write the forced hand item and armor set onto every row of each class
    /// group, in menu order, wrapping each of the two loadout lists
    /// independently by group index modulo its own length. Returns the
    /// number of groups touched.
    /// </summary>
    internal static int Apply(PARAM charaInit, List<List<int>> groups, ClassLoadoutData loadout)
    {
        var rowsById = charaInit.Rows.ToDictionary(r => r.ID);
        int updated = 0;

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Count == 0)
                continue;

            HandItemData? hand = loadout.HandItems.Count > 0
                ? loadout.HandItems[i % loadout.HandItems.Count]
                : null;
            List<int>? armor = loadout.ArmorSets.Count > 0
                ? loadout.ArmorSets[i % loadout.ArmorSets.Count]
                : null;

            string? handField = null;
            if (hand != null)
            {
                if (hand.Slot != "left" && hand.Slot != "right")
                    Console.WriteLine($"  Warning: unrecognized hand slot \"{hand.Slot}\" for item {hand.Id} ({hand.Name}), defaulting to right");
                handField = hand.Slot == "left" ? "equip_Wep_Left" : "equip_Wep_Right";
            }

            if (armor != null && armor.Count != 4)
            {
                Console.WriteLine($"  Warning: armor set for class row {group[0]} has {armor.Count} entries, expected 4 (head, body, arms, legs); skipping armor for this class");
                armor = null;
            }

            foreach (var rowId in group)
            {
                if (!rowsById.TryGetValue(rowId, out var row))
                    continue;

                if (handField != null)
                    row[handField].Value = hand!.Id;

                if (armor != null)
                {
                    row["equip_Helm"].Value = armor[0];
                    row["equip_Armer"].Value = armor[1];
                    row["equip_Gaunt"].Value = armor[2];
                    row["equip_Leg"].Value = armor[3];
                }
            }

            string handDesc = hand != null ? $"{hand.Name} ({hand.Slot})" : "none";
            string armorDesc = armor != null ? armor[0].ToString() : "none";
            Console.WriteLine($"  Class row {group[0]}: weapon {handDesc}, armor head {armorDesc}");
            updated++;
        }

        return updated;
    }
}
