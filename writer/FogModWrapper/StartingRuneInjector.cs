using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Sets starting runes on all character classes via CharaInitParam.soul field.
/// Modifies regulation.bin after FogMod writes it (same pattern as WeaponUpgradeInjector).
/// </summary>
public static class StartingRuneInjector
{
    // CharaInitParam row IDs for the 10 base classes (Vagabond through Wretch)
    private const int CLASS_ROW_MIN = 3000;
    private const int CLASS_ROW_MAX = 3009;

    // CharaInitParam.soul max value from paramdef
    private const int MAX_SOUL = 10_000_000;

    /// <summary>
    /// Set starting runes on all character classes in CharaInitParam.
    /// Opens regulation.bin, sets the soul field, re-encrypts.
    /// </summary>
    public static void Inject(string modDir, int startingRunes)
    {
        if (startingRunes <= 0)
            return;

        var regulationPath = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(regulationPath))
        {
            Console.WriteLine("Warning: regulation.bin not found, skipping starting rune injection");
            return;
        }

        // Load paramdef
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var charaDefPath = Path.Combine(baseDir, "eldendata", "Defs", "CharaInitParam.xml");

        if (!File.Exists(charaDefPath))
        {
            Console.WriteLine("Warning: CharaInitParam paramdef not found, skipping starting rune injection");
            return;
        }

        var charaDef = PARAMDEF.XmlDeserialize(charaDefPath);

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

        // Load CharaInitParam
        var charaFile = regulation.Files.Find(f => f.Name.EndsWith("CharaInitParam.param"));
        if (charaFile == null)
        {
            Console.WriteLine("Warning: CharaInitParam.param not found in regulation.bin");
            return;
        }

        var charaParam = PARAM.Read(charaFile.Bytes);
        charaParam.ApplyParamdef(charaDef);

        int clampedRunes = Math.Clamp(startingRunes, 0, MAX_SOUL);
        if (clampedRunes != startingRunes)
        {
            Console.WriteLine($"Warning: starting_runes capped at {MAX_SOUL:N0}");
        }

        Console.WriteLine($"Setting starting runes to {clampedRunes:N0} on all classes...");

        int updated = 0;
        foreach (var row in charaParam.Rows)
        {
            if (row.ID < CLASS_ROW_MIN || row.ID > CLASS_ROW_MAX)
                continue;

            row["soul"].Value = clampedRunes;
            updated++;
        }

        if (updated == 0)
        {
            Console.WriteLine("  No character classes found");
            return;
        }

        // Write back
        charaFile.Bytes = charaParam.Write();
        SoulsFormats.Cryptography.RegulationDecryptor.EncryptERRegulation(regulationPath, regulation);
        Console.WriteLine($"  Set {clampedRunes:N0} starting runes on {updated} classes");
    }
}
