using SoulsFormats;
using Xunit;

namespace StaticModBuilder.Tests;

public class HalloweenIconPatcherTests
{
    /// <summary>
    /// Minimal valid DX10 BC7 DDS: 148-byte header (magic, dwSize=124,
    /// ddspf.dwFourCC="DX10", header10.dxgiFormat=BC7_UNORM=98) plus one 4x4
    /// block. TPF.Texture's (name, format, flags1, bytes, platform)
    /// constructor eagerly parses `bytes` as a real DDS in SoulsFormatsNEXT,
    /// unlike the brief's original fixture of a bare byte[16], so the fixture
    /// needs a real (if minimal) DDS instead.
    /// </summary>
    private static byte[] MakeMinimalDds(int dxgiFormat = 98, int mipCount = 1)
    {
        var dds = new byte[148 + 16];
        "DDS "u8.ToArray().CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);   // dwSize
        BitConverter.GetBytes(4).CopyTo(dds, 12);    // dwHeight
        BitConverter.GetBytes(4).CopyTo(dds, 16);    // dwWidth
        BitConverter.GetBytes(mipCount).CopyTo(dds, 28); // dwMipMapCount
        BitConverter.GetBytes(32).CopyTo(dds, 76);   // ddspf dwSize
        System.Text.Encoding.ASCII.GetBytes("DX10").CopyTo(dds, 84); // ddspf dwFourCC
        BitConverter.GetBytes(dxgiFormat).CopyTo(dds, 128); // header10 dxgiFormat
        BitConverter.GetBytes(3).CopyTo(dds, 132);   // header10 resourceDimension = TEXTURE2D
        BitConverter.GetBytes(1).CopyTo(dds, 140);   // header10 arraySize
        return dds;
    }

    private static TPF MakeDummyTpf()
    {
        var tpf = new TPF();
        tpf.Textures.Add(new TPF.Texture("MENU_DummyFace_01", 102, 0, MakeMinimalDds(), TPF.TPFPlatform.PC));
        return tpf;
    }

    [Fact]
    public void AddTexturesToTpf_AppendsNewTextures()
    {
        var tpf = MakeDummyTpf();
        int added = HalloweenIconPatcher.AddTexturesToTpf(tpf, new[]
        {
            ("MENU_ItemIcon_60383", new byte[] { 1, 2, 3 }),
            ("MENU_ItemIcon_63075", new byte[] { 4, 5, 6 }),
        });
        Assert.Equal(2, added);
        Assert.Equal(3, tpf.Textures.Count);
        Assert.Contains(tpf.Textures, t => t.Name == "MENU_ItemIcon_60383");
        Assert.Contains(tpf.Textures, t => t.Name == "MENU_DummyFace_01");
    }

    [Fact]
    public void AddTexturesToTpf_ReplacesExistingByName()
    {
        var tpf = MakeDummyTpf();
        HalloweenIconPatcher.AddTexturesToTpf(tpf, new[]
        {
            ("MENU_ItemIcon_60383", new byte[] { 1 }),
        });
        int addedAgain = HalloweenIconPatcher.AddTexturesToTpf(tpf, new[]
        {
            ("MENU_ItemIcon_60383", new byte[] { 9, 9 }),
        });
        Assert.Equal(1, addedAgain);
        var tex = tpf.Textures.Single(t => t.Name == "MENU_ItemIcon_60383");
        Assert.Equal(new byte[] { 9, 9 }, tex.Bytes);
        Assert.Equal(2, tpf.Textures.Count);
    }

    [Fact]
    public void TryValidateBc7Template_AcceptsValidTemplate()
    {
        Assert.True(HalloweenIconPatcher.TryValidateBc7Template(MakeMinimalDds(), "test template"));
    }

    [Fact]
    public void TryValidateBc7Template_RejectsNonBc7Format()
    {
        // 71 = BC1_UNORM, not BC7_UNORM (98)
        var nonBc7 = MakeMinimalDds(dxgiFormat: 71);
        Assert.False(HalloweenIconPatcher.TryValidateBc7Template(nonBc7, "test template"));
    }

    [Fact]
    public void TryValidateBc7Template_RejectsMultiMip()
    {
        var multiMip = MakeMinimalDds(mipCount: 2);
        Assert.False(HalloweenIconPatcher.TryValidateBc7Template(multiMip, "test template"));
    }

    [Fact]
    public void TryValidateBc7Template_RejectsTooShortTemplate()
    {
        // Below the 148-byte DX10 header size; DdsAtlas.ParseHeader throws
        // InvalidDataException on this, which must be caught here rather than
        // escaping and hard-failing the whole builder (the bug this guards
        // against: BuildStandaloneDds itself also throws on a too-short
        // template, but only after the header has already been trusted).
        Assert.False(HalloweenIconPatcher.TryValidateBc7Template(new byte[10], "test template"));
    }
}
