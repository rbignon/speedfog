using FogMod;
using Xunit;

namespace FogModWrapper.Tests;

public class OpenSplitInjectorTests
{
    private static AnnotationData MakeAnnWithWarp(string name, string? gateTags = "unique legacy")
    {
        var ann = new AnnotationData();
        var warp = new AnnotationData.Entrance
        {
            Name = name,
            ID = int.TryParse(name, out var id) ? id : 0,
            ASide = new AnnotationData.Side { Area = "snowfield", Tags = "open" },
            BSide = new AnnotationData.Side { Area = "haligtree" },
            Tags = gateTags,
        };
        ann.Warps.Add(warp);
        return ann;
    }

    [Fact]
    public void Apply_AddsOpensplitTag_ToMatchingWarp()
    {
        var ann = MakeAnnWithWarp("15002600");

        var applied = OpenSplitInjector.Apply(ann, new HashSet<string> { "15002600" });

        Assert.Equal(1, applied);
        Assert.True(ann.Warps[0].HasTag("opensplit"));
    }

    [Fact]
    public void Apply_DoesNotAddTag_ToUnrelatedWarp()
    {
        var ann = MakeAnnWithWarp("15002600");

        OpenSplitInjector.Apply(ann, new HashSet<string> { "99999999" });

        Assert.False(ann.Warps[0].HasTag("opensplit"));
    }

    [Fact]
    public void Apply_PreservesExistingTags()
    {
        var ann = MakeAnnWithWarp("15002600", gateTags: "unique legacy");

        OpenSplitInjector.Apply(ann, new HashSet<string> { "15002600" });

        Assert.True(ann.Warps[0].HasTag("unique"));
        Assert.True(ann.Warps[0].HasTag("legacy"));
        Assert.True(ann.Warps[0].HasTag("opensplit"));
    }

    [Fact]
    public void Apply_Idempotent_DoesNotDuplicateOpensplit()
    {
        var ann = MakeAnnWithWarp("15002600", gateTags: "unique legacy opensplit");

        var applied = OpenSplitInjector.Apply(ann, new HashSet<string> { "15002600" });

        // Already tagged: nothing applied.
        Assert.Equal(0, applied);
        // And the tag list is not duplicated.
        var tagOccurrences = ann.Warps[0].Tags.Split(' ').Count(t => t == "opensplit");
        Assert.Equal(1, tagOccurrences);
    }

    [Fact]
    public void Apply_AlsoSearchesEntrances()
    {
        // opensplit can be applied to either Warps or Entrances; for our use case
        // 15002600 is a Warp, but the injector should not skip Entrances.
        var ann = new AnnotationData();
        ann.Entrances.Add(new AnnotationData.Entrance
        {
            Name = "fake_entrance",
            ASide = new AnnotationData.Side { Area = "a", Tags = "open" },
            BSide = new AnnotationData.Side { Area = "b" },
            Tags = "unique legacy",
        });

        OpenSplitInjector.Apply(ann, new HashSet<string> { "fake_entrance" });

        Assert.True(ann.Entrances[0].HasTag("opensplit"));
    }

    [Fact]
    public void Apply_EmptyOverrides_NoOp()
    {
        var ann = MakeAnnWithWarp("15002600");

        var applied = OpenSplitInjector.Apply(ann, new HashSet<string>());

        Assert.Equal(0, applied);
        Assert.False(ann.Warps[0].HasTag("opensplit"));
    }

    [Fact]
    public void Apply_UnknownId_LogsButDoesNotThrow()
    {
        var ann = MakeAnnWithWarp("15002600");

        var applied = OpenSplitInjector.Apply(ann, new HashSet<string> { "does_not_exist" });

        // No-op for unmatched override; no exception.
        Assert.Equal(0, applied);
        Assert.False(ann.Warps[0].HasTag("opensplit"));
    }
}
