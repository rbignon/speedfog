using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class FlverMaterialTableTests
{
    /// <summary>
    /// An Elden Ring FLVER with two materials: the first drawn by a mesh whose buffer layout
    /// carries BoneIndices and BoneWeights (skinned) and one UV set, the second by a static mesh
    /// with two UV sets. One dummy and one bone sit before the mesh table, as in game files.
    /// </summary>
    internal static byte[] MakeSyntheticFlver(
        string skinnedMtd = @"N:\x\m10_00_519.matbin",
        string staticMtd = @"N:\x\AEG001_001.matbin")
    {
        var flver = NewFlver();
        flver.Materials.Add(new FLVER2.Material("bridge", skinnedMtd, 0));
        flver.Materials.Add(new FLVER2.Material("rock", staticMtd, 0));
        flver.BufferLayouts.Add(Layout(Uv(0), Uv(1)));
        flver.BufferLayouts.Add(Layout(
            new FLVER.LayoutMember(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices),
            new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.BoneWeights),
            Uv(0)));
        flver.Meshes.Add(MakeMesh(materialIndex: 0, 1));
        flver.Meshes.Add(MakeMesh(materialIndex: 1, 0));
        return flver.Write();
    }

    private static FLVER2 NewFlver()
    {
        var flver = new FLVER2();
        flver.Header.Version = 0x2001A;
        flver.Skeletons = new FLVER2.SkeletonSet();
        flver.Dummies.Add(new FLVER.Dummy());
        flver.Nodes.Add(new FLVER.Node { Name = "root" });
        return flver;
    }

    /// <summary>A layout starting with a Float3 position, as every game layout does.</summary>
    private static FLVER2.BufferLayout Layout(params FLVER.LayoutMember[] members)
    {
        var layout = new FLVER2.BufferLayout
        {
            new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position),
        };
        foreach (var member in members)
        {
            layout.Add(member);
        }
        return layout;
    }

    private static FLVER.LayoutMember Uv(int index) => new(FLVER.LayoutType.Short2, FLVER.LayoutSemantic.UV, index);

    private static FLVER2.Mesh MakeMesh(int materialIndex, params int[] layoutIndices)
    {
        var mesh = new FLVER2.Mesh { MaterialIndex = materialIndex };
        foreach (int layoutIndex in layoutIndices)
        {
            mesh.VertexBuffers.Add(new FLVER2.VertexBuffer(layoutIndex));
        }
        mesh.FaceSets.Add(new FLVER2.FaceSet());
        return mesh;
    }

    [Fact]
    public void MatbinStem_HandlesBothSeparatorsAndExtensions()
    {
        Assert.Equal("m10_00_519",
            FlverMaterialTable.MatbinStem(@"N:\GR\data\Material\mtd\Map_m10_00\matxml\m10_00_519.matbin"));
        Assert.Equal("SpeedTree_x", FlverMaterialTable.MatbinStem("a/b/SpeedTree_x.mtd"));
    }

    [Fact]
    public void Read_MatchesSoulsFormatsOnASyntheticFlver()
    {
        var bytes = MakeSyntheticFlver();

        var expected = FlverMaterialTable.ReadWithSoulsFormats(bytes);
        var actual = FlverMaterialTable.Read(bytes);

        Assert.Equal(expected, actual);
        Assert.Equal(new FlverMaterialEntry("m10_00_519", Skinned: true, UvSets: 1), actual[0]);
        Assert.Equal(new FlverMaterialEntry("AEG001_001", Skinned: false, UvSets: 2), actual[1]);
    }

    [Fact]
    public void Read_CountsTheUvSetsEveryMeshOfAMaterialProvides()
    {
        // Layouts: 0 = UV 0 and 1; 1 = UV 0; 2 = UV 0 and 2 (a gap: one set from 0); 3 = UV 1.
        var flver = NewFlver();
        flver.Materials.Add(new FLVER2.Material("two sets then one", @"N:\x\A.matbin", 0));
        flver.Materials.Add(new FLVER2.Material("gap", @"N:\x\B.matbin", 0));
        flver.Materials.Add(new FLVER2.Material("sets split over two buffers", @"N:\x\C.matbin", 0));
        flver.Materials.Add(new FLVER2.Material("drawn by no mesh", @"N:\x\D.matbin", 0));
        flver.BufferLayouts.Add(Layout(Uv(0), Uv(1)));
        flver.BufferLayouts.Add(Layout(Uv(0)));
        flver.BufferLayouts.Add(Layout(Uv(0), Uv(2)));
        flver.BufferLayouts.Add(Layout(Uv(1)));
        flver.Meshes.Add(MakeMesh(0, 0));
        flver.Meshes.Add(MakeMesh(0, 1));
        flver.Meshes.Add(MakeMesh(1, 2));
        flver.Meshes.Add(MakeMesh(2, 1, 3));
        byte[] bytes = flver.Write();

        var actual = FlverMaterialTable.Read(bytes);

        Assert.Equal(FlverMaterialTable.ReadWithSoulsFormats(bytes), actual);
        Assert.Equal(new int?[] { 1, 1, 2, null }, actual.Select(e => e.UvSets));
        Assert.All(actual, e => Assert.False(e.Skinned));
    }

    [Fact]
    public void Read_RejectsBytesThatAreNotAFlver()
    {
        Assert.Throws<InvalidDataException>(() => FlverMaterialTable.Read(new byte[0x100]));
    }
}
