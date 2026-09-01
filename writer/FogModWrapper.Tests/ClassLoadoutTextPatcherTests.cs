using SoulsFormats;
using Xunit;
using Swap = FogModWrapper.ClassLoadoutInjector.LoadoutSwap;

namespace FogModWrapper.Tests;

public class ClassLoadoutTextPatcherTests
{
    private const int Base = ClassLoadoutTextPatcher.CLASS_DESC_FMG_BASE;

    private static readonly Dictionary<int, string> Names = new()
    {
        [1000] = "Longsword",
        [1001] = "Eleonora's Poleblade",
        [2000] = "Heater Shield",
        [3560000] = "Leontiel's Greatsword",
        [31540000] = "Silver Grooved Shield",
    };

    private static FMG MakeLineHelp(params (int classIndex, string text)[] entries)
    {
        var fmg = new FMG();
        foreach (var (classIndex, text) in entries)
            fmg.Entries.Add(new FMG.Entry(Base + classIndex, text));
        return fmg;
    }

    private static string? Lookup(int id) => Names.GetValueOrDefault(id);

    [Fact]
    public void PatchLineHelp_ReplacesWeaponAndShieldNamesAndRewraps()
    {
        // Width of the original entry (25 chars) is preserved by the rewrap.
        var fmg = MakeLineHelp((0, "Longsword, Heater Shield,\nGolden Rune [1]"));
        var swaps = new List<Swap> { new(0, 1000, 3560000, 2000, 31540000) };

        int replaced = ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        Assert.Equal(2, replaced);
        Assert.Equal(
            "Leontiel's Greatsword,\nSilver Grooved Shield,\nGolden Rune [1]",
            fmg.Entries[0].Text);
    }

    [Fact]
    public void PatchLineHelp_MatchesNameWrappedAcrossLines_AndStripsStatDiff()
    {
        // CharacterWriter wraps MID-NAME and appends the old weapon's stat
        // diff: both must be swallowed by the replacement (the diff belongs
        // to the old weapon and would be wrong next to the new name).
        var fmg = MakeLineHelp((0, "Eleonora's\nPoleblade (-20), Heater\nShield"));
        var swaps = new List<Swap> { new(0, 1001, 3560000, 0, 0) };

        int replaced = ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        Assert.Equal(1, replaced);
        Assert.DoesNotContain("(-20)", fmg.Entries[0].Text);
        Assert.DoesNotContain("Poleblade", fmg.Entries[0].Text);
        Assert.Contains("Leontiel's", fmg.Entries[0].Text);
        Assert.Contains("Heater", fmg.Entries[0].Text);
    }

    [Fact]
    public void PatchLineHelp_NoShieldSwap_OnlyWeaponReplaced()
    {
        var fmg = MakeLineHelp((1, "Longsword, Heater Shield"));
        var swaps = new List<Swap> { new(1, 1000, 3560000, 0, 0) };

        int replaced = ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        Assert.Equal(1, replaced);
        Assert.Contains("Leontiel's Greatsword", fmg.Entries[0].Text.Replace("\n", " "));
        Assert.Contains("Heater Shield", fmg.Entries[0].Text.Replace("\n", " "));
    }

    [Fact]
    public void PatchLineHelp_OldNameAbsentFromText_AppendsNewName()
    {
        // CharacterWriter only lists a priority subset of the gear: when the
        // old name is not in the text, the forced item is appended so it is
        // still named on the class-selection screen.
        var fmg = MakeLineHelp((0, "Some entirely different list"));
        var swaps = new List<Swap> { new(0, 1000, 3560000, 0, 0) };

        int replaced = ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        Assert.Equal(1, replaced);
        Assert.Contains("Leontiel's Greatsword", fmg.Entries[0].Text.Replace("\n", " "));
        Assert.Contains("Some entirely different", fmg.Entries[0].Text.Replace("\n", " "));
    }

    [Fact]
    public void PatchLineHelp_UnknownOldId_StillAppendsNewName()
    {
        // An ash-of-war (custom) weapon id has no WeaponName entry at all:
        // the old text cannot be matched, the new name is appended.
        var fmg = MakeLineHelp((0, "Longsword"));
        var swaps = new List<Swap> { new(0, 999999, 3560000, 0, 0) };

        int replaced = ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        Assert.Equal(1, replaced);
        Assert.Contains("Leontiel's Greatsword", fmg.Entries[0].Text.Replace("\n", " "));
    }

    [Fact]
    public void PatchLineHelp_MissingEntryOrUnknownNewName_SkippedWithoutThrow()
    {
        // Class 5 has no GR_LineHelp entry; class 0's NEW id has no name in
        // the lookup. Both are skipped, nothing throws, text untouched.
        var fmg = MakeLineHelp((0, "Longsword"));
        var swaps = new List<Swap>
        {
            new(5, 1000, 3560000, 0, 0),
            new(0, 1000, 888888, 0, 0),
        };

        int replaced = ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        Assert.Equal(0, replaced);
        Assert.Equal("Longsword", fmg.Entries[0].Text);
    }

    [Fact]
    public void PatchLineHelp_DoesNotMatchInsideALongerItemName()
    {
        // "Longsword" must not match inside "Lordsworn's Longsword": the
        // letter lookarounds reject the substring hit and the plain name
        // later in the list is replaced instead.
        var fmg = MakeLineHelp((0, "Lordsworn's Longsword, Longsword"));
        var swaps = new List<Swap> { new(0, 1000, 3560000, 0, 0) };

        ClassLoadoutTextPatcher.PatchLineHelp(fmg, Lookup, swaps);

        var flat = fmg.Entries[0].Text.Replace("\n", " ");
        Assert.Contains("Lordsworn's Longsword", flat);
        Assert.Contains("Leontiel's Greatsword", flat);
    }

    [Fact]
    public void Rewrap_SpacedLanguage_BreaksAtSpacesWithinWidth()
    {
        // Width floors at 12 to avoid degenerate wrapping.
        var wrapped = ClassLoadoutTextPatcher.Rewrap("aaaa bbbb cccc dddd", 12, useSpaces: true);
        Assert.Equal("aaaa bbbb\ncccc dddd", wrapped);
    }

    [Fact]
    public void Rewrap_CjkLanguage_BreaksAtWidthWithoutSpaces()
    {
        var wrapped = ClassLoadoutTextPatcher.Rewrap("abcdefgh\nijkl", 12, useSpaces: false);
        Assert.Equal("abcdefghijkl", wrapped);
    }
}
