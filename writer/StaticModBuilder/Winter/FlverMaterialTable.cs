using SoulsFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// One FLVER material: the stem of its MTD path, whether a skinned mesh draws it, and how many
/// UV sets (TEXCOORD0 upwards, without a gap) every mesh drawing it provides: the smallest count
/// over those meshes, null when no mesh draws the material.
/// </summary>
public sealed record FlverMaterialEntry(string MatbinStem, bool Skinned, int? UvSets);

/// <summary>
/// Lists a FLVER2's materials with what the vertex layouts of their meshes provide: bone indices
/// or weights (a skinned mesh) and UV sets. A layout member binds the vertex shader input of its
/// semantic and index (UV 1 is TEXCOORD1), and a mesh whose layout lacks an input the bound
/// shader reads is not drawn. <see cref="Read"/> parses only the tables it needs and skips the
/// vertex data; <see cref="ReadWithSoulsFormats"/> is the full-parse reference it is checked
/// against.
/// </summary>
public static class FlverMaterialTable
{
    // FLVER2 binary layout, from SoulsFormats/SoulsFormats/Formats/FLVER/FLVER2/*.cs.
    private const int HeaderSize = 0x80;
    private const int DummySize = 0x40;
    private const int MaterialSize = 0x20;
    private const int BoneSize = 0x80;
    private const int MeshSize = 0x30;
    private const int VertexBufferSize = 0x20;
    private const int BufferLayoutSize = 0x10;
    private const int LayoutMemberSize = 0x14;

    /// <summary>What one buffer layout provides.</summary>
    private readonly record struct LayoutInputs(bool Skinned, IReadOnlySet<int> UvIndices);

    public static IReadOnlyList<FlverMaterialEntry> Read(byte[] flverBytes)
    {
        if (flverBytes.Length < HeaderSize)
        {
            throw new InvalidDataException("not a FLVER2: shorter than its header");
        }

        using var br = new BinaryReaderEx(false, flverBytes);
        if (br.GetASCII(0, 6) != "FLVER\0")
        {
            throw new InvalidDataException("not a FLVER2: bad magic");
        }
        string endian = br.GetASCII(6, 2);
        if (endian != "L\0" && endian != "B\0")
        {
            throw new InvalidDataException("not a FLVER2: bad endianness marker");
        }
        br.BigEndian = endian == "B\0";
        int version = br.GetInt32(0x08);
        if (version < 0x20000)
        {
            throw new InvalidDataException($"not a FLVER2: version 0x{version:X}");
        }

        int dummyCount = Count(br, 0x14, "dummy");
        int materialCount = Count(br, 0x18, "material");
        int boneCount = Count(br, 0x1C, "bone");
        int meshCount = Count(br, 0x20, "mesh");
        int vertexBufferCount = Count(br, 0x24, "vertex buffer");
        bool unicode = br.GetBoolean(0x49);
        int faceSetCount = Count(br, 0x50, "face set");
        int layoutCount = Count(br, 0x54, "buffer layout");
        int faceSetSize = version > 0x20007 ? 0x20 : 0x10;

        // The tables follow the header back to back, in this order.
        long materials = HeaderSize + (long)dummyCount * DummySize;
        long meshes = materials + (long)materialCount * MaterialSize + (long)boneCount * BoneSize;
        long vertexBuffers = meshes + (long)meshCount * MeshSize + (long)faceSetCount * faceSetSize;
        long layouts = vertexBuffers + (long)vertexBufferCount * VertexBufferSize;
        if (layouts + (long)layoutCount * BufferLayoutSize > flverBytes.Length)
        {
            throw new InvalidDataException("FLVER2 tables run past the end of the file");
        }

        var layoutInputs = new LayoutInputs[layoutCount];
        for (int i = 0; i < layoutCount; i++)
        {
            long layout = layouts + (long)i * BufferLayoutSize;
            int memberCount = br.GetInt32(layout);
            int memberOffset = br.GetInt32(layout + 0xC);
            bool skinned = false;
            var uvIndices = new HashSet<int>();
            for (int m = 0; m < memberCount; m++)
            {
                // Semantic at +0xC and index at +0x10 in both the regular and the SpeedTree member layout.
                long member = memberOffset + (long)m * LayoutMemberSize;
                var semantic = (FLVER.LayoutSemantic)br.GetUInt32(member + 0xC);
                skinned |= IsBoneSemantic(semantic);
                if (semantic == FLVER.LayoutSemantic.UV)
                {
                    uvIndices.Add(br.GetInt32(member + 0x10));
                }
            }
            layoutInputs[i] = new LayoutInputs(skinned, uvIndices);
        }

        var skinnedMaterials = new bool[materialCount];
        var uvSets = new int?[materialCount];
        for (int i = 0; i < meshCount; i++)
        {
            long mesh = meshes + (long)i * MeshSize;
            int materialIndex = CheckIndex(br.GetInt32(mesh + 0x04), materialCount, "material");
            int bufferCount = br.GetInt32(mesh + 0x28);
            int bufferIndicesOffset = br.GetInt32(mesh + 0x2C);
            var inputs = new List<LayoutInputs>(bufferCount);
            for (int j = 0; j < bufferCount; j++)
            {
                int bufferIndex = CheckIndex(br.GetInt32(bufferIndicesOffset + 4L * j), vertexBufferCount, "vertex buffer");
                int layoutIndex = CheckIndex(br.GetInt32(vertexBuffers + (long)bufferIndex * VertexBufferSize + 0x04), layoutCount, "buffer layout");
                inputs.Add(layoutInputs[layoutIndex]);
            }
            Accumulate(skinnedMaterials, uvSets, materialIndex, inputs);
        }

        var entries = new List<FlverMaterialEntry>(materialCount);
        for (int i = 0; i < materialCount; i++)
        {
            int mtdOffset = br.GetInt32(materials + (long)i * MaterialSize + 0x04);
            string mtd = unicode ? br.GetUTF16(mtdOffset) : br.GetShiftJIS(mtdOffset);
            entries.Add(new FlverMaterialEntry(MatbinStem(mtd), skinnedMaterials[i], uvSets[i]));
        }
        return entries;
    }

    public static IReadOnlyList<FlverMaterialEntry> ReadWithSoulsFormats(byte[] flverBytes)
    {
        var flver = FLVER2.Read(flverBytes);
        var skinned = new bool[flver.Materials.Count];
        var uvSets = new int?[flver.Materials.Count];
        foreach (var mesh in flver.Meshes)
        {
            int materialIndex = CheckIndex(mesh.MaterialIndex, flver.Materials.Count, "material");
            var inputs = mesh.VertexBuffers
                .Select(buffer => flver.BufferLayouts[buffer.LayoutIndex])
                .Select(layout => new LayoutInputs(
                    layout.Any(member => IsBoneSemantic(member.Semantic)),
                    layout.Where(member => member.Semantic == FLVER.LayoutSemantic.UV).Select(member => member.Index).ToHashSet()))
                .ToList();
            Accumulate(skinned, uvSets, materialIndex, inputs);
        }
        return flver.Materials.Select((m, i) => new FlverMaterialEntry(MatbinStem(m.MTD), skinned[i], uvSets[i])).ToList();
    }

    /// <summary>File name of the path without its extension; both separators are accepted.</summary>
    public static string MatbinStem(string mtdPath)
    {
        string name = mtdPath[(mtdPath.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    /// <summary>
    /// Folds one mesh into its material: skinned when any buffer is, UV sets as the smallest count
    /// over the meshes seen so far (the UV indices of a mesh's buffers are pooled).
    /// </summary>
    private static void Accumulate(bool[] skinned, int?[] uvSets, int materialIndex, IReadOnlyList<LayoutInputs> inputs)
    {
        var uvIndices = new HashSet<int>();
        foreach (var input in inputs)
        {
            skinned[materialIndex] |= input.Skinned;
            uvIndices.UnionWith(input.UvIndices);
        }
        int sets = UvSetsFromZero(uvIndices);
        uvSets[materialIndex] = uvSets[materialIndex] is int known ? Math.Min(known, sets) : sets;
    }

    /// <summary>How many UV indices are present from 0 up without a gap ({0, 2} gives 1).</summary>
    private static int UvSetsFromZero(ISet<int> uvIndices)
    {
        int sets = 0;
        while (uvIndices.Contains(sets))
        {
            sets++;
        }
        return sets;
    }

    private static bool IsBoneSemantic(FLVER.LayoutSemantic semantic) =>
        semantic == FLVER.LayoutSemantic.BoneIndices || semantic == FLVER.LayoutSemantic.BoneWeights;

    private static int Count(BinaryReaderEx br, long offset, string what)
    {
        int count = br.GetInt32(offset);
        if (count < 0)
        {
            throw new InvalidDataException($"negative {what} count {count}");
        }
        return count;
    }

    private static int CheckIndex(int index, int count, string what)
    {
        if (index < 0 || index >= count)
        {
            throw new InvalidDataException($"{what} index {index} out of range ({count})");
        }
        return index;
    }
}
