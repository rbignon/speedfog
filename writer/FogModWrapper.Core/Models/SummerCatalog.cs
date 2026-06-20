namespace FogModWrapper.Models;

/// <summary>One boss healthbar-name (NpcName) override.</summary>
public sealed record SummerBossEntry(int NpcNameId, string Name, string En, string? Fr);

/// <summary>One UI string override (banner/label) in a specific FMG.</summary>
public sealed record SummerUiEntry(string Bnd, string Fmg, int Id, string En, string? Fr);

/// <summary>Parsed summer text catalogue (data/plugins/summer.toml).</summary>
public sealed record SummerCatalog(
    IReadOnlyList<SummerBossEntry> Bosses,
    IReadOnlyList<SummerUiEntry> Ui)
{
    public bool IsEmpty => Bosses.Count == 0 && Ui.Count == 0;

    public static SummerCatalog Empty { get; } =
        new(new List<SummerBossEntry>(), new List<SummerUiEntry>());
}
