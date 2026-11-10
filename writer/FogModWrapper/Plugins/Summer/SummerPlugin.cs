namespace FogModWrapper.Plugins.Summer;

/// <summary>[plugin.summer]: cosmetic text reskin (data/plugins/summer.toml,
/// see docs/plugins/summer-theme.md).</summary>
internal sealed class SummerPlugin : SpeedFogPlugin
{
    public override string Name => "summer";

    public override string? TextTheme => "summer";
}
