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

    [Fact]
    public void GetParam_WithDefName_LooksUpDifferentXmlName()
    {
        // BND4 has a binder file for "SpEffectParam"; defsDir contains "SpEffect.xml"
        // (no Param suffix). The def name overload should bridge the mismatch.
        var bnd = CreateBndWithFile("N:/GR/data/Param/GameParam/SpEffectParam.param", new byte[0]);
        var defsDir = EmptyDefsDir();
        // We do not write a real paramdef; we only assert the lookup uses the right filename.
        // The expected outcome here is that LoadParamdef logs "paramdef SpEffect.xml not found at <defsDir>/SpEffect.xml"
        // and returns null. Without the overload, the lookup would target SpEffectParam.xml.
        var editor = new RegulationEditor(bnd, defsDir);

        var captured = new StringWriter();
        var prev = Console.Out;
        Console.SetOut(captured);
        try
        {
            var result = editor.GetParam("SpEffectParam", "SpEffect");
            Assert.Null(result);
        }
        finally
        {
            Console.SetOut(prev);
        }

        Assert.Contains("SpEffect.xml", captured.ToString());
        Assert.DoesNotContain("SpEffectParam.xml", captured.ToString());
    }
}
