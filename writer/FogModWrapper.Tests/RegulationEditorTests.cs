using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class RegulationEditorTests
{
    private static BND4 CreateEmptyBnd() => new BND4();

    private static BND4 CreateBndWithFile(string fileName, byte[] bytes)
    {
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, fileName, bytes));
        return bnd;
    }

    private static string EmptyDefsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"regedit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void GetParam_ReturnsNull_WhenBinderFileMissing()
    {
        var editor = new RegulationEditor(CreateEmptyBnd(), EmptyDefsDir());
        var result = editor.GetParam("ShopLineupParam");
        Assert.Null(result);
    }

    [Fact]
    public void GetParam_ReturnsNull_WhenParamdefMissing()
    {
        // BND4 has the binder file, but defsDir has no matching XML.
        var bnd = CreateBndWithFile("N:/GR/data/Param/GameParam/ShopLineupParam.param", new byte[0]);
        var editor = new RegulationEditor(bnd, EmptyDefsDir());
        var result = editor.GetParam("ShopLineupParam");
        Assert.Null(result);
    }

    [Fact]
    public void Save_IsNoOp_WhenNoParamAccessed()
    {
        // No GetParam calls, no accessed PARAMs, null path so no encryption attempt.
        var editor = new RegulationEditor(CreateEmptyBnd(), EmptyDefsDir());
        var exception = Record.Exception(() => editor.Save());
        Assert.Null(exception);
    }
}
