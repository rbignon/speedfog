namespace FogModWrapper;

/// <summary>
/// Pure calculation functions for starting resources.
/// Extracted for testability.
/// </summary>
public static class ResourceCalculations
{
    /// <summary>
    /// Value of one Lord's Rune when consumed.
    /// </summary>
    public const int LordsRuneValue = 50_000;

    /// <summary>
    /// Maximum number of Lord's Runes that can be given (caps at 10M runes).
    /// </summary>
    public const int MaxLordsRunes = 200;

    /// <summary>
    /// Maximum golden seeds that can be given.
    /// </summary>
    public const int MaxGoldenSeeds = 99;

    /// <summary>
    /// Maximum sacred tears that can be given.
    /// </summary>
    public const int MaxSacredTears = 12;

    /// <summary>
    /// Convert a rune amount to the number of Lord's Runes needed.
    /// Uses ceiling division: any partial rune value rounds up.
    /// </summary>
    /// <param name="runes">Desired rune amount</param>
    /// <returns>Number of Lord's Runes (each worth 50,000)</returns>
    public static int ConvertRunesToLordsRunes(int runes)
    {
        if (runes <= 0)
        {
            return 0;
        }

        // Ceiling division: (runes + 49999) / 50000
        return (runes + LordsRuneValue - 1) / LordsRuneValue;
    }

    /// <summary>
    /// Clamp golden seeds to valid range.
    /// </summary>
    /// <param name="count">Requested count</param>
    /// <param name="wasClamped">True if value was reduced</param>
    /// <returns>Clamped value (0-99)</returns>
    public static int ClampGoldenSeeds(int count, out bool wasClamped)
    {
        wasClamped = count > MaxGoldenSeeds;
        return Math.Clamp(count, 0, MaxGoldenSeeds);
    }

    /// <summary>
    /// Clamp sacred tears to valid range.
    /// </summary>
    /// <param name="count">Requested count</param>
    /// <param name="wasClamped">True if value was reduced</param>
    /// <returns>Clamped value (0-12)</returns>
    public static int ClampSacredTears(int count, out bool wasClamped)
    {
        wasClamped = count > MaxSacredTears;
        return Math.Clamp(count, 0, MaxSacredTears);
    }

    /// <summary>
    /// Clamp Lord's Runes count to valid range.
    /// </summary>
    /// <param name="count">Requested count</param>
    /// <param name="wasClamped">True if value was reduced</param>
    /// <returns>Clamped value (0-200)</returns>
    public static int ClampLordsRunes(int count, out bool wasClamped)
    {
        wasClamped = count > MaxLordsRunes;
        return Math.Clamp(count, 0, MaxLordsRunes);
    }

    /// <summary>
    /// Calculate the actual rune value from a number of Lord's Runes.
    /// </summary>
    public static int LordsRunesToRunes(int lordsRunes)
    {
        return lordsRunes * LordsRuneValue;
    }
}
