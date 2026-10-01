namespace FogModWrapper.Models;

/// <summary>One boss healthbar-name (NpcName) override, keyed by exactly one of
/// NpcNameId (a fixed vanilla id) or BossName (a name BossNameInjector
/// allocates a seed-dependent id for, resolved by TextTheme).</summary>
public sealed record ThemeBossEntry(int? NpcNameId, string? BossName, string Name, string En, string? Fr);

/// <summary>One UI string override (banner/label) in a specific FMG.</summary>
public sealed record ThemeUiEntry(string Bnd, string Fmg, int Id, string En, string? Fr);

/// <summary>Parsed theme text catalogue (data/plugins/&lt;theme&gt;.toml).</summary>
public sealed record ThemeCatalog(
    IReadOnlyList<ThemeBossEntry> Bosses,
    IReadOnlyList<ThemeUiEntry> Ui)
{
    public bool IsEmpty => Bosses.Count == 0 && Ui.Count == 0;

    public static ThemeCatalog Empty { get; } =
        new(new List<ThemeBossEntry>(), new List<ThemeUiEntry>());
}
