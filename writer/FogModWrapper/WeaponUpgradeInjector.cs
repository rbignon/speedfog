using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Upgrades starting class weapons in CharaInitParam to a target upgrade level.
/// Modifies regulation.bin after FogMod writes it (same pattern as ShopInjector).
/// </summary>
public static class WeaponUpgradeInjector
{
    // CharaInitParam row IDs for the 10 base classes (Vagabond through Wretch)
    private const int CLASS_ROW_MIN = 3000;
    private const int CLASS_ROW_MAX = 3009;

    // Weapon equipment fields in CharaInitParam, mapped to their
    // wepParamType companion fields (0 = EquipParamWeapon, 1 = EquipParamCustomWeapon).
    // The randomizer also maps Subwep_Right3/Left3, but vanilla base classes (3000-3009)
    // never use the third slots, so we omit them.
    private static readonly Dictionary<string, string> WeaponTypeFields = new()
    {
        ["equip_Wep_Right"] = "wepParamType_Right1",
        ["equip_Wep_Left"] = "wepParamType_Left1",
        ["equip_Subwep_Right"] = "wepParamType_Right2",
        ["equip_Subwep_Left"] = "wepParamType_Left2",
    };

    /// <summary>
    /// Convert standard upgrade level (0-25) to somber equivalent.
    /// Matches care_package.py _somber_upgrade: floor(standard / 2.5).
    /// </summary>
    public static int SomberUpgrade(int standardLevel)
    {
        return (int)Math.Floor(standardLevel / 2.5);
    }

    /// <summary>
    /// Calculate the upgraded weapon param ID.
    /// Strips any existing upgrade, then applies the target level.
    /// Returns original ID if weapon is not in either regular or somber set.
    /// </summary>
    public static int UpgradeWeaponId(int weaponId, int level, HashSet<int> regularWeapons, HashSet<int> somberWeapons)
    {
        int baseId = weaponId - weaponId % 100;

        if (regularWeapons.Contains(baseId))
            return baseId + level;

        if (somberWeapons.Contains(baseId))
            return baseId + SomberUpgrade(level);

        // Unknown weapon type, leave as-is
        return weaponId;
    }

    /// <summary>
    /// Compute the upgrade level for a custom weapon based on its base weapon type.
    /// Returns the target reinforceLv, or -1 if the base weapon is unknown.
    /// </summary>
    public static int CustomWeaponUpgradeLevel(int baseWepId, int level, HashSet<int> regularWeapons, HashSet<int> somberWeapons)
    {
        if (regularWeapons.Contains(baseWepId))
            return level;

        if (somberWeapons.Contains(baseWepId))
            return SomberUpgrade(level);

        return -1;
    }

    /// <summary>
    /// Upgrade starting class weapons in CharaInitParam to the given level.
    /// Handles both standard weapons (EquipParamWeapon, ID-encoded upgrade)
    /// and custom weapons with ashes of war (EquipParamCustomWeapon, reinforceLv field).
    /// Opens regulation.bin, modifies params, re-encrypts.
    /// </summary>
    public static void Inject(string modDir, int weaponUpgrade)
    {
        if (weaponUpgrade <= 0)
            return;

        var regulationPath = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(regulationPath))
        {
            Console.WriteLine("Warning: regulation.bin not found, skipping weapon upgrade injection");
            return;
        }

        // Load paramdefs
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var charaDefPath = Path.Combine(baseDir, "eldendata", "Defs", "CharaInitParam.xml");
        var weaponDefPath = Path.Combine(baseDir, "eldendata", "Defs", "EquipParamWeapon.xml");
        var customWepDefPath = Path.Combine(baseDir, "eldendata", "Defs", "EquipParamCustomWeapon.xml");

        if (!File.Exists(charaDefPath) || !File.Exists(weaponDefPath))
        {
            Console.WriteLine("Warning: Required paramdefs not found, skipping weapon upgrade injection");
            return;
        }

        var charaDef = PARAMDEF.XmlDeserialize(charaDefPath);
        var weaponDef = PARAMDEF.XmlDeserialize(weaponDefPath);

        PARAMDEF? customWepDef = null;
        if (File.Exists(customWepDefPath))
            customWepDef = PARAMDEF.XmlDeserialize(customWepDefPath);

        // Decrypt regulation.bin
        BND4 regulation;
        try
        {
            regulation = SoulsFormats.Cryptography.RegulationDecryptor.DecryptERRegulation(regulationPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to decrypt regulation.bin: {ex.Message}");
            return;
        }

        // Load EquipParamWeapon to build regular/somber sets
        var weaponFile = regulation.Files.Find(f => f.Name.EndsWith("EquipParamWeapon.param"));
        if (weaponFile == null)
        {
            Console.WriteLine("Warning: EquipParamWeapon.param not found in regulation.bin");
            return;
        }

        var weaponParam = PARAM.Read(weaponFile.Bytes);
        weaponParam.ApplyParamdef(weaponDef);

        var regularWeapons = new HashSet<int>();
        var somberWeapons = new HashSet<int>();
        foreach (var row in weaponParam.Rows)
        {
            if ((int)row["originEquipWep25"].Value > 0)
                regularWeapons.Add(row.ID);
            else if ((int)row["originEquipWep10"].Value > 0)
                somberWeapons.Add(row.ID);
        }

        // Load EquipParamCustomWeapon (for weapons with ashes of war)
        PARAM? customWepParam = null;
        BinderFile? customWepFile = null;
        if (customWepDef != null)
        {
            customWepFile = regulation.Files.Find(f => f.Name.EndsWith("EquipParamCustomWeapon.param"));
            if (customWepFile != null)
            {
                customWepParam = PARAM.Read(customWepFile.Bytes);
                customWepParam.ApplyParamdef(customWepDef);
            }
        }

        // Load CharaInitParam
        var charaFile = regulation.Files.Find(f => f.Name.EndsWith("CharaInitParam.param"));
        if (charaFile == null)
        {
            Console.WriteLine("Warning: CharaInitParam.param not found in regulation.bin");
            return;
        }

        var charaParam = PARAM.Read(charaFile.Bytes);
        charaParam.ApplyParamdef(charaDef);

        Console.WriteLine($"Upgrading starting class weapons to +{weaponUpgrade} (somber +{SomberUpgrade(weaponUpgrade)})...");

        int upgraded = 0;
        bool customWepModified = false;

        foreach (var row in charaParam.Rows)
        {
            if (row.ID < CLASS_ROW_MIN || row.ID > CLASS_ROW_MAX)
                continue;

            foreach (var (fieldName, typeFieldName) in WeaponTypeFields)
            {
                int weaponId = (int)row[fieldName].Value;
                if (weaponId <= 0)
                    continue;

                byte wepType = (byte)row[typeFieldName].Value;

                if (wepType == 1 && customWepParam != null)
                {
                    // Custom weapon (has ash of war): modify reinforceLv in EquipParamCustomWeapon
                    var customRow = customWepParam.Rows.Find(r => r.ID == weaponId);
                    if (customRow == null)
                    {
                        Console.WriteLine($"  Warning: custom weapon {weaponId} not found in EquipParamCustomWeapon");
                        continue;
                    }

                    int baseWepId = (int)customRow["baseWepId"].Value;
                    int targetLevel = CustomWeaponUpgradeLevel(baseWepId, weaponUpgrade, regularWeapons, somberWeapons);
                    if (targetLevel < 0)
                    {
                        Console.WriteLine($"  Warning: base weapon {baseWepId} for custom weapon {weaponId} not found in weapon tables");
                        continue;
                    }

                    byte currentLevel = (byte)customRow["reinforceLv"].Value;
                    if (currentLevel != (byte)targetLevel)
                    {
                        customRow["reinforceLv"].Value = (byte)targetLevel;
                        customWepModified = true;
                        upgraded++;
                    }
                }
                else
                {
                    // Standard weapon: upgrade via ID encoding
                    int newId = UpgradeWeaponId(weaponId, weaponUpgrade, regularWeapons, somberWeapons);
                    if (newId != weaponId)
                    {
                        row[fieldName].Value = newId;
                        upgraded++;
                    }
                }
            }
        }

        if (upgraded == 0)
        {
            Console.WriteLine("  No weapons to upgrade");
            return;
        }

        // Write back modified params
        charaFile.Bytes = charaParam.Write();
        if (customWepModified && customWepFile != null && customWepParam != null)
            customWepFile.Bytes = customWepParam.Write();
        SoulsFormats.Cryptography.RegulationDecryptor.EncryptERRegulation(regulationPath, regulation);
        Console.WriteLine($"  Upgraded {upgraded} weapon slots across starting classes");
    }
}
