using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Sets starting runes on all character classes via CharaInitParam.soul field.
/// Runs inside a shared <see cref="RegulationEditor"/> block alongside other
/// regulation.bin consumers.
/// </summary>
public static class StartingRuneInjector
{
    // CharaInitParam.soul max value from paramdef
    private const int MAX_SOUL = 10_000_000;

    /// <summary>
    /// Set starting runes on all character classes in CharaInitParam.
    /// </summary>
    public static void ApplyTo(RegulationEditor reg, int startingRunes)
    {
        if (startingRunes <= 0)
            return;

        var charaParam = reg.GetParam("CharaInitParam");
        if (charaParam == null)
            return;

        int clampedRunes = Math.Clamp(startingRunes, 0, MAX_SOUL);
        if (clampedRunes != startingRunes)
        {
            Console.WriteLine($"Warning: starting_runes capped at {MAX_SOUL:N0}");
        }

        Console.WriteLine($"Setting starting runes to {clampedRunes:N0} on all classes...");

        int updated = Apply(charaParam, StartingClassRows.Resolve(reg), clampedRunes);

        if (updated == 0)
        {
            Console.WriteLine("  No character classes found");
            return;
        }

        Console.WriteLine($"  Set {clampedRunes:N0} starting runes on {updated} class rows");
    }

    /// <summary>Set soul on every CharaInitParam row listed in classRows; returns the count.</summary>
    internal static int Apply(PARAM charaParam, ISet<int> classRows, int runes)
    {
        int updated = 0;
        foreach (var row in charaParam.Rows)
        {
            if (!classRows.Contains(row.ID))
                continue;
            row["soul"].Value = runes;
            updated++;
        }
        return updated;
    }
}
