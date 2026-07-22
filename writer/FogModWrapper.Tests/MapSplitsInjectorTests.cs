using FogMod;
using FogModWrapper;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class MapSplitsInjectorTests
{
    private static MapSplits MakeSplits(List<string>? cols = null, List<string>? enemies = null) => new(
        new List<SplitZone>
        {
            new("upper", "m99_00_00_00", "Upper Half", new List<string> { "dlc" },
                "lower", cols ?? new List<string>(), enemies ?? new List<string>()),
        },
        new List<SplitFog>
        {
            new("AEG099_002_9900", "m99_00_00_00", 990001, "Split gate",
                "AEG099_002 AEG099_002_9000 1.0 2.0 3.0 90.0",
                "lower", "going up", "upper", "arriving up"),
        });

    private static AnnotationData MakeAnn() => new()
    {
        Areas = new List<AnnotationData.Area>
        {
            new() { Name = "lower", Text = "Lower", Maps = "m99_00_00_00" },
        },
        Entrances = new List<AnnotationData.Entrance>(),
        Warps = new List<AnnotationData.Entrance>(),
    };

    [Fact]
    public void Apply_AddsAreaAndEntrance()
    {
        var ann = MakeAnn();
        MapSplitsInjector.Apply(ann, MakeSplits());
        var area = ann.Areas.Single(a => a.Name == "upper");
        Assert.Equal("m99_00_00_00", area.Maps);
        Assert.Equal("Upper Half", area.Text);
        Assert.True(area.HasTag("dlc"));
        var e = ann.Entrances.Single(x => x.Name == "AEG099_002_9900");
        Assert.Equal(990001, e.ID);
        Assert.Equal("m99_00_00_00", e.Area);
        Assert.StartsWith("AEG099_002 ", e.MakeFrom);
        Assert.Equal("lower", e.ASide.Area);
        Assert.Equal("going up", e.ASide.Text);
        Assert.Equal("upper", e.BSide.Area);
    }

    [Fact]
    public void Apply_BossAreaSide_GetsBossDefeatName()
    {
        // Sides whose area is a boss area (DefeatFlag set) must carry
        // BossDefeatName = "area", the universal fog.txt convention: without
        // it FogMod compiles the side's fogwarp without the
        // IfEventFlag(defeat flag) wait and the gate is traversable before
        // the boss dies (GameDataWriterE getNameFlag/fogwarp compilation).
        var ann = MakeAnn();
        ann.Areas.Add(new AnnotationData.Area
        {
            Name = "boss_room", Text = "Boss", Maps = "m99_00_00_00", DefeatFlag = 990800,
        });
        var splits = new MapSplits(
            new List<SplitZone>(),
            new List<SplitFog>
            {
                new("AEG099_001_9900", "m99_00_00_00", 990002, "Boss door",
                    "AEG099_001 AEG099_002_9000 1.0 2.0 3.0 90.0",
                    "boss_room", "in the boss room", "lower", "before the boss room"),
            });
        MapSplitsInjector.Apply(ann, splits);
        var e = ann.Entrances.Single(x => x.Name == "AEG099_001_9900");
        Assert.Equal("area", e.ASide.BossDefeatName);
        Assert.Null(e.BSide.BossDefeatName);
    }

    [Fact]
    public void Apply_BossAreaSide_GetsMainTag()
    {
        // FogMod resolves a boss area's spawn point through its "main"-tagged
        // entrance side (getMainSpawnPoint); every vanilla boss side carries
        // the tag.
        var ann = MakeAnn();
        ann.Areas.Add(new AnnotationData.Area
        {
            Name = "boss_room", Text = "Boss", Maps = "m99_00_00_00", DefeatFlag = 990800,
        });
        var splits = new MapSplits(
            new List<SplitZone>(),
            new List<SplitFog>
            {
                new("AEG099_001_9900", "m99_00_00_00", 990002, "Boss door",
                    "AEG099_001 AEG099_002_9000 1.0 2.0 3.0 90.0",
                    "boss_room", "in the boss room", "lower", "before the boss room"),
            });
        MapSplitsInjector.Apply(ann, splits);
        var e = ann.Entrances.Single(x => x.Name == "AEG099_001_9900");
        Assert.True(e.ASide.HasTag("main"));
        Assert.False(e.BSide.HasTag("main"));
    }

    [Fact]
    public void Apply_BossAreaWithExistingMainSide_DoesNotAddSecondMain()
    {
        var ann = MakeAnn();
        ann.Areas.Add(new AnnotationData.Area
        {
            Name = "boss_room", Text = "Boss", Maps = "m99_00_00_00", DefeatFlag = 990800,
        });
        ann.Entrances.Add(new AnnotationData.Entrance
        {
            Name = "AEG099_001_0500",
            Area = "m99_00_00_00",
            ASide = new AnnotationData.Side { Area = "boss_room", Text = "vanilla front", Tags = "main" },
            BSide = new AnnotationData.Side { Area = "lower", Text = "outside" },
        });
        var splits = new MapSplits(
            new List<SplitZone>(),
            new List<SplitFog>
            {
                new("AEG099_001_9900", "m99_00_00_00", 990002, "Boss back door",
                    "AEG099_001 AEG099_002_9000 1.0 2.0 3.0 90.0",
                    "boss_room", "in the boss room", "lower", "before the boss room"),
            });
        MapSplitsInjector.Apply(ann, splits);
        var e = ann.Entrances.Single(x => x.Name == "AEG099_001_9900");
        Assert.False(e.ASide.HasTag("main"));
        Assert.Equal("area", e.ASide.BossDefeatName);
    }

    [Fact]
    public void Apply_ExistingZone_Throws()
    {
        var ann = MakeAnn();
        ann.Areas.Add(new AnnotationData.Area { Name = "upper" });
        Assert.Throws<InvalidDataException>(() => MapSplitsInjector.Apply(ann, MakeSplits()));
    }

    [Fact]
    public void Apply_ExistingFogNameInSameArea_Throws()
    {
        // FogMod keys entrances by FullName = Area + "_" + Name (Graph.cs),
        // so a same-map collision is a genuine duplicate.
        var ann = MakeAnn();
        ann.Entrances.Add(new AnnotationData.Entrance { Name = "AEG099_002_9900", Area = "m99_00_00_00" });
        Assert.Throws<InvalidDataException>(() => MapSplitsInjector.Apply(ann, MakeSplits()));
    }

    [Fact]
    public void Apply_SameFogNameInDifferentArea_DoesNotThrow()
    {
        // fog.txt reuses asset-derived Names (e.g. "AEG099_002_9000") across
        // dozens of unrelated maps: only the (Area, Name) pair is unique, not
        // Name alone. A collision in an unrelated map must not block injection.
        var ann = MakeAnn();
        ann.Entrances.Add(new AnnotationData.Entrance { Name = "AEG099_002_9900", Area = "m35_00_00_00" });
        MapSplitsInjector.Apply(ann, MakeSplits());
        Assert.Contains(ann.Entrances, x => x.Name == "AEG099_002_9900" && x.Area == "m99_00_00_00");
    }

    [Fact]
    public void Apply_ExistingFogNameInSameArea_InWarps_Throws()
    {
        // FogMod's Graph.Construct keys entrances by FullName across BOTH
        // Entrances and Warps (EntranceIds built from ann.Entrances.Concat(ann.Warps)
        // in Graph.cs), so a same-map collision living in Warps must be caught too.
        var ann = MakeAnn();
        ann.Warps.Add(new AnnotationData.Entrance { Name = "AEG099_002_9900", Area = "m99_00_00_00" });
        Assert.Throws<InvalidDataException>(() => MapSplitsInjector.Apply(ann, MakeSplits()));
    }

    [Fact]
    public void Apply_TaglessZone_LeavesTagsNull()
    {
        var ann = MakeAnn();
        var splits = new MapSplits(
            new List<SplitZone>
            {
                new("upper", "m99_00_00_00", "Upper Half", new List<string>(),
                    "lower", new List<string>(), new List<string>()),
            },
            new List<SplitFog>());
        MapSplitsInjector.Apply(ann, splits);
        var area = ann.Areas.Single(a => a.Name == "upper");
        Assert.Null(area.Tags);
        Assert.False(area.HasTag(""));
    }

    [Fact]
    public void Apply_SplitsEnemyArea()
    {
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new()
                {
                    Name = "lower",
                    Cols = "m99_00_00_00_h001000 m99_00_00_00_h002000",
                    MainMap = "m99_00_00_00",
                    ScalingTier = 20,
                },
            },
            Enemies = new List<AnnotationData.EnemyLoc>
            {
                new() { Map = "m99_00_00_00", ID = "c1000_0001", Col = "h001000", AArea = "lower" },
                new() { Map = "m99_00_00_00", ID = "c1000_0002", Col = "h002000", AArea = "lower" },
            },
        };
        MapSplitsInjector.Apply(ann, MakeSplits(new List<string> { "h002000" }));

        var src = ann.Locations.EnemyAreas.Single(a => a.Name == "lower");
        Assert.Equal("m99_00_00_00_h001000", src.Cols);
        var dst = ann.Locations.EnemyAreas.Single(a => a.Name == "upper");
        Assert.Equal("m99_00_00_00_h002000", dst.Cols);
        Assert.Equal(20, dst.ScalingTier);
        Assert.Equal("upper", ann.Locations.Enemies.Single(e => e.ID == "c1000_0002").Area);
        Assert.Null(ann.Locations.Enemies.Single(e => e.ID == "c1000_0001").Area);
    }

    [Fact]
    public void Apply_EmptyCols_LeavesEnemyAreasUntouched()
    {
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new() { Name = "lower", Cols = "m99_00_00_00_h001000", ScalingTier = 20 },
            },
            Enemies = new List<AnnotationData.EnemyLoc>(),
        };
        MapSplitsInjector.Apply(ann, MakeSplits());
        Assert.Single(ann.Locations.EnemyAreas);
        Assert.Equal("m99_00_00_00_h001000", ann.Locations.EnemyAreas[0].Cols);
    }

    [Fact]
    public void Apply_ReassignsEnemiesById()
    {
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new() { Name = "lower", MainMap = "m99_00_00_00", ScalingTier = 20 },
            },
            Enemies = new List<AnnotationData.EnemyLoc>
            {
                new() { Map = "m99_00_00_00", ID = "c5651_9000", AArea = "lower" },
                new() { Map = "m99_00_00_00", ID = "c5651_9001", AArea = "lower" },
                new() { Map = "m88_00_00_00", ID = "c5651_9000", AArea = "elsewhere" },
            },
        };
        MapSplitsInjector.Apply(ann, MakeSplits(enemies: new List<string> { "c5651_9000" }));

        // A dedicated EnemyArea is created even with cols empty
        var dst = ann.Locations.EnemyAreas.Single(a => a.Name == "upper");
        Assert.Equal(20, dst.ScalingTier);
        // Only the listed ID on the zone's map is reassigned
        Assert.Equal("upper", ann.Locations.Enemies[0].Area);
        Assert.Null(ann.Locations.Enemies[1].Area);
        Assert.Null(ann.Locations.Enemies[2].Area);
    }

    [Fact]
    public void Apply_EnemyIdMissing_Throws()
    {
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new() { Name = "lower", ScalingTier = 20 },
            },
            Enemies = new List<AnnotationData.EnemyLoc>(),
        };
        var ex = Assert.Throws<InvalidDataException>(() =>
            MapSplitsInjector.Apply(ann, MakeSplits(enemies: new List<string> { "c9999_0000" })));
        Assert.Contains("c9999_0000", ex.Message);
    }

    [Fact]
    public void Apply_EnemyWrongArea_Throws()
    {
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new() { Name = "lower", ScalingTier = 20 },
            },
            Enemies = new List<AnnotationData.EnemyLoc>
            {
                new() { Map = "m99_00_00_00", ID = "c5651_9000", AArea = "someboss" },
            },
        };
        Assert.Throws<InvalidDataException>(() =>
            MapSplitsInjector.Apply(ann, MakeSplits(enemies: new List<string> { "c5651_9000" })));
    }

    [Fact]
    public void Apply_QualifiedEnemy_ReassignsFromDeclaredArea()
    {
        // "area:cNNNN_NNNN" entries declare their source area explicitly, for
        // enemies FogRando attributed to an overlapping area instead of the
        // zone's split_from (e.g. the Fort of Reprimand gatehouse trio under
        // scadualtus_high). Unqualified entries keep the split_from guard.
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new() { Name = "lower", ScalingTier = 20 },
            },
            Enemies = new List<AnnotationData.EnemyLoc>
            {
                new() { Map = "m99_00_00_00", ID = "c5401_9020", AArea = "overlay" },
                new() { Map = "m99_00_00_00", ID = "c5651_9000", AArea = "lower" },
            },
        };
        MapSplitsInjector.Apply(ann, MakeSplits(
            enemies: new List<string> { "overlay:c5401_9020", "c5651_9000" }));

        Assert.Equal("upper", ann.Locations.Enemies[0].Area);
        Assert.Equal("upper", ann.Locations.Enemies[1].Area);
    }

    [Fact]
    public void Apply_QualifiedEnemyWrongArea_Throws()
    {
        var ann = MakeAnn();
        ann.Locations = new AnnotationData.FogLocations
        {
            EnemyAreas = new List<AnnotationData.EnemyLocArea>
            {
                new() { Name = "lower", ScalingTier = 20 },
            },
            Enemies = new List<AnnotationData.EnemyLoc>
            {
                new() { Map = "m99_00_00_00", ID = "c5401_9020", AArea = "overlay" },
            },
        };
        var ex = Assert.Throws<InvalidDataException>(() =>
            MapSplitsInjector.Apply(ann, MakeSplits(
                enemies: new List<string> { "elsewhere:c5401_9020" })));
        Assert.Contains("elsewhere", ex.Message);
    }

    private static EMEVD MakeEmevdWithEvent0()
    {
        var emevd = new EMEVD();
        emevd.Events.Add(new EMEVD.Event(0));
        return emevd;
    }

    [Fact]
    public void InjectShowSfx_AppendsInitCommonEventToEvent0()
    {
        var emevd = MakeEmevdWithEvent0();
        int n = MapSplitsInjector.InjectShowSfx(emevd, "m99_00_00_00", MakeSplits(), 9005775);

        Assert.Equal(1, n);
        var ins = Assert.Single(emevd.Events[0].Instructions);
        Assert.Equal(2000, ins.Bank);
        Assert.Equal(6, ins.ID); // InitializeCommonEvent
        Assert.Equal(0, BitConverter.ToInt32(ins.ArgData, 0));       // slot
        Assert.Equal(9005775, BitConverter.ToInt32(ins.ArgData, 4)); // showsfx event
        Assert.Equal(990001, BitConverter.ToInt32(ins.ArgData, 8));  // fog gate entity
        Assert.Equal(MapSplitsInjector.WhiteFogSfx, BitConverter.ToInt32(ins.ArgData, 12));
    }

    [Fact]
    public void InjectShowSfx_OtherMap_NoOp()
    {
        var emevd = MakeEmevdWithEvent0();
        int n = MapSplitsInjector.InjectShowSfx(emevd, "m35_00_00_00", MakeSplits(), 9005775);
        Assert.Equal(0, n);
        Assert.Empty(emevd.Events[0].Instructions);
    }

    [Fact]
    public void InjectShowSfx_MissingEvent0_ReturnsZero()
    {
        var emevd = new EMEVD();
        int n = MapSplitsInjector.InjectShowSfx(emevd, "m99_00_00_00", MakeSplits(), 9005775);
        Assert.Equal(0, n);
    }

    [Fact]
    public void RealDataFile_ParsesAndInjects()
    {
        // Guard against drift between data/map_splits.toml and the loader/injector.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.GetFullPath(
            Path.Combine(baseDir, "../../../../..", "data", "map_splits.toml"));
        Assert.True(File.Exists(path), $"tracked data file missing: {path}");

        var splits = MapSplitsLoader.Load(path);
        Assert.Equal(4, splits.Fogs.Count);
        Assert.All(splits.Fogs, f => Assert.Equal(6, f.MakeFrom.Split(' ').Length));

        var ann = new AnnotationData
        {
            Areas = new List<AnnotationData.Area>
            {
                new() { Name = "enirilim" },
                new() { Name = "enirilim_stairs" },
            },
            Entrances = new List<AnnotationData.Entrance>(),
            Warps = new List<AnnotationData.Entrance>(),
        };
        MapSplitsInjector.Apply(ann, splits);
        Assert.Contains(ann.Areas, a => a.Name == "enirilim_upper");
        Assert.Contains(ann.Areas, a => a.Name == "reprimand");
        Assert.Equal(4, ann.Areas.Count); // enirilim, enirilim_stairs, enirilim_upper, reprimand
        Assert.Equal(4, ann.Entrances.Count);

        // Synthetic fogs are grouped per map: InjectShowSfx aggregates fogs
        // by target map (m20_01_00_00 has Enir-Ilim pair, m61_49_43_00 has Fort
        // of Reprimand pair). This guards the per-map aggregation and the fog
        // 'map' field vs. the EMEVD filename convention.
        var emevd = MakeEmevdWithEvent0();
        int n = MapSplitsInjector.InjectShowSfx(emevd, "m20_01_00_00", splits, 9005775);
        Assert.Equal(2, n);
        Assert.Equal(2, emevd.Events[0].Instructions.Count);
        var entities = emevd.Events[0].Instructions
            .Select(i => BitConverter.ToInt32(i.ArgData, 8))
            .ToList();
        Assert.Equal(new List<int> { 20011960, 20011961 }, entities);

        // Parallel check for Fort of Reprimand fogs in m61_49_43_00
        var emevd2 = MakeEmevdWithEvent0();
        int n2 = MapSplitsInjector.InjectShowSfx(emevd2, "m61_49_43_00", splits, 9005775);
        Assert.Equal(2, n2);
        Assert.Equal(2, emevd2.Events[0].Instructions.Count);
        var entities2 = emevd2.Events[0].Instructions
            .Select(i => BitConverter.ToInt32(i.ArgData, 8))
            .ToList();
        Assert.Equal(new List<int> { 2049431960, 2049431961 }, entities2);
    }
}
