using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class FlverMaterialTableTests
{
    /// <summary>
    /// An Elden Ring FLVER with two materials: the first drawn by a mesh whose buffer layout
    /// carries BoneIndices and BoneWeights (skinned), the second by a static mesh. One dummy
    /// and one bone sit before the mesh table, as in game files.
    /// </summary>
    internal static byte[] MakeSyntheticFlver(
        string skinnedMtd = @"N:\x\m10_00_519.matbin",
        string staticMtd = @"N:\x\AEG001_001.matbin")
    {
        var flver = new FLVER2();
        flver.Header.Version = 0x2001A;
        flver.Skeletons = new FLVER2.SkeletonSet();
        flver.Dummies.Add(new FLVER.Dummy());
        flver.Nodes.Add(new FLVER.Node { Name = "root" });
        flver.Materials.Add(new FLVER2.Material("bridge", skinnedMtd, 0));
        flver.Materials.Add(new FLVER2.Material("rock", staticMtd, 0));
        flver.BufferLayouts.Add(new FLVER2.BufferLayout
        {
            new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position),
        });
        flver.BufferLayouts.Add(new FLVER2.BufferLayout
        {
            new FLVER.LayoutMember(FLVER.LayoutType.Float3, FLVER.LayoutSemantic.Position),
            new FLVER.LayoutMember(FLVER.LayoutType.UByte4, FLVER.LayoutSemantic.BoneIndices),
            new FLVER.LayoutMember(FLVER.LayoutType.UByte4Norm, FLVER.LayoutSemantic.BoneWeights),
        });
        flver.Meshes.Add(MakeMesh(materialIndex: 0, layoutIndex: 1));
        flver.Meshes.Add(MakeMesh(materialIndex: 1, layoutIndex: 0));
        return flver.Write();
    }

    private static FLVER2.Mesh MakeMesh(int materialIndex, int layoutIndex)
    {
        var mesh = new FLVER2.Mesh { MaterialIndex = materialIndex };
        mesh.VertexBuffers.Add(new FLVER2.VertexBuffer(layoutIndex));
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
        Assert.Equal(new FlverMaterialEntry("m10_00_519", true), actual[0]);
        Assert.Equal(new FlverMaterialEntry("AEG001_001", false), actual[1]);
    }

    [Fact]
    public void Read_RejectsBytesThatAreNotAFlver()
    {
        Assert.Throws<InvalidDataException>(() => FlverMaterialTable.Read(new byte[0x100]));
    }
}
