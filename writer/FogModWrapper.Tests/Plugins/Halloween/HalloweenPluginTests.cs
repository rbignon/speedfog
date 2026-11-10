using System.Text.Json;
using FogModWrapper.Common;
using FogModWrapper.Models;
using FogModWrapper.Pipeline;
using FogModWrapper.Plugins.Halloween;
using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests.Plugins.Halloween;

public class HalloweenPluginTests
{
    private static HalloweenPlugin GreetersAndAmbushes() => new(new PluginConfig
    {
        Enabled = true,
        Extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"greeters": true, "ambushes": true}""")!,
    });

    private static BinderFile ParamFile(string name, PARAM param) =>
        new(Binder.FileFlags.Flag1, 0, $"N:/GR/data/Param/GameParam/{name}.param", param.Write());

    [Fact]
    public void PlaceableSettings_WithoutRegulationRows_DisablesGreetersAndAmbushes()
    {
        // The regulation hook never ran (regulation.bin unavailable): the
        // greeter and ambusher NpcParam rows do not exist, so the MSB pass
        // must not place parts pointing at them.
        var plugin = GreetersAndAmbushes();

        var settings = plugin.PlaceableSettings();

        Assert.False(settings.Greeters);
        Assert.False(settings.Ambushes);
    }

    [Fact]
    public void PlaceableSettings_RegulationWithoutTheParams_DisablesGreetersAndAmbushes()
    {
        // The regulation hook ran but the row writers found no NpcParam:
        // their failure, not just the absence of a call, turns the flavors off.
        var plugin = GreetersAndAmbushes();
        var reg = new RegulationEditor(new BND4(), DefsDir());

        plugin.OnRegulation(new Context(new Config()), reg);
        var settings = plugin.PlaceableSettings();

        Assert.False(settings.Greeters);
        Assert.False(settings.Ambushes);
    }

    [Fact]
    public void PlaceableSettings_RegulationRowsWritten_KeepsGreetersAndAmbushes()
    {
        var greeter = AmbientSpawnInjector.GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Untouchable);
        var ambusher = AmbientSpawnInjector.AmbusherProfile(HalloweenPluginSettings.AmbusherSpecies.Skeleton);
        var npc = BuildParamFromDef("NpcParam", greeter.NpcTemplate);
        AddRowFromTemplate(npc, ambusher.NpcTemplate);
        var bnd = new BND4();
        bnd.Files.Add(ParamFile("NpcParam", npc));
        bnd.Files.Add(ParamFile("NpcThinkParam", BuildParamFromDef("NpcThinkParam", greeter.ThinkTemplate)));
        bnd.Files.Add(ParamFile("SpEffectParam", BuildParamFromDef("SpEffect", AmbientSpawnInjector.SCALING_TIER1_TEMPLATE_SPEFFECT, "SpEffectParam")));
        var reg = new RegulationEditor(bnd, DefsDir());
        var plugin = GreetersAndAmbushes();

        plugin.OnRegulation(new Context(new Config()), reg);
        var settings = plugin.PlaceableSettings();

        Assert.True(settings.Greeters);
        Assert.True(settings.Ambushes);
    }
}
