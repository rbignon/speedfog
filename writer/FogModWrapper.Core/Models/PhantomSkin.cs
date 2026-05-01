namespace FogModWrapper.Models;

/// <summary>
/// One entry of the phantom skins catalog. Materialized at build time into
/// three paired param rows (PhantomParam, SpEffectVfxParam, SpEffectParam),
/// all sharing the same id.
/// </summary>
public sealed record PhantomSkin(
    int Id,
    string Name,
    string DisplayName,
    byte EdgeColorR,
    byte EdgeColorG,
    byte EdgeColorB,
    float EdgePower,
    float GlowScale,
    float Alpha);
