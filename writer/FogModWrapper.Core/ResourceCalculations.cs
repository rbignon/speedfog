namespace FogModWrapper;

/// <summary>
/// Pure calculation functions for starting resources.
/// Extracted for testability.
/// </summary>
public static class ResourceCalculations
{
    /// <summary>
    /// Maximum golden seeds that can be given.
    /// </summary>
    public const int MaxGoldenSeeds = 99;

    /// <summary>
    /// Maximum sacred tears that can be given.
    /// </summary>
    public const int MaxSacredTears = 12;

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
}
