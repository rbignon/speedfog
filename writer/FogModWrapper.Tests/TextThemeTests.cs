using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class TextThemeTests
{
    private const string ITEM_BND = "item_dlc02.msgbnd.dcx";
    private const int MARGIT_NAME = 902130000;
    // NpcName ids BossNameInjector allocates per seed for names vanilla lacks.
    private static readonly int SPEEDFOG_NAME = SpeedFogIds.BossNameFmgIds.Base;
    private static readonly int SPEEDFOG_NAME_2 = SpeedFogIds.BossNameFmgIds.Base + 1;

    private const string CATALOGUE = """
        [[bosses]]
        npc_name_id = 902130000
        en = "Margit, the Grumpy Ghost"
        fr = "Margit, le Fantôme grognon"

        [[bosses]]
        boss_name = "Devonia"
        en = "Demonia"
        fr = "Démonia"
        """;

    private static void WriteItemBnd(string msgDir, string lang, IEnumerable<(int id, string text)> entries)
    {
        var fmg = new FMG();
        foreach (var (id, text) in entries)
            fmg.Entries.Add(new FMG.Entry(id, text));
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0,
            $@"N:\GR\data\INTERROOT_win64\msg\{lang}\NpcName.fmg", fmg.Write()));
        var langDir = Path.Combine(msgDir, lang);
        Directory.CreateDirectory(langDir);
        bnd.Write(Path.Combine(langDir, ITEM_BND));
    }

    private static string? NpcNameText(string modDir, string lang, int id)
    {
        var bnd = BND4.Read(Path.Combine(modDir, "msg", lang, ITEM_BND));
        var fmg = FMG.Read(bnd.Files.Single(f => f.Name.EndsWith("NpcName.fmg")).Bytes);
        return fmg.Entries.Find(e => e.ID == id)?.Text;
    }

    /// <summary>Vanilla engus/frafr NpcName with Margit; the mod copy
    /// BossNameInjector left behind, adding <paramref name="speedFogEntries"/>
    /// (English text in both languages, as it writes them); the halloween
    /// catalogue. Returns (modDir, gameDir, dataDir).</summary>
    private static (string, string, string) Setup(TempDir tmp, params (int id, string text)[] speedFogEntries)
    {
        var gameMsg = Path.Combine(tmp.Path, "game", "msg");
        var modMsg = Path.Combine(tmp.Path, "mod", "msg");
        foreach (var (lang, margit) in new[] { ("engus", "Margit, the Fell Omen"), ("frafr", "Margit le Déchu") })
        {
            WriteItemBnd(gameMsg, lang, new[] { (MARGIT_NAME, margit) });
            WriteItemBnd(modMsg, lang, new[] { (MARGIT_NAME, margit) }.Concat(speedFogEntries));
        }
        var plugins = Path.Combine(tmp.Path, "data", "plugins");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "halloween.toml"), CATALOGUE);
        return (Path.Combine(tmp.Path, "mod"), Path.Combine(tmp.Path, "game"), Path.Combine(tmp.Path, "data"));
    }

    [Fact]
    public void Apply_RewritesTheEntryBossNameInjectorCreatedForANameKeyedBoss()
    {
        using var tmp = new TempDir();
        var (modDir, gameDir, dataDir) = Setup(tmp, (SPEEDFOG_NAME, "Aging Untouchable"), (SPEEDFOG_NAME_2, "Devonia"));
        var bossNameIds = new Dictionary<string, int>
        {
            ["Aging Untouchable"] = SPEEDFOG_NAME,
            ["Devonia"] = SPEEDFOG_NAME_2,
        };

        TextTheme.Apply("halloween", modDir, gameDir, dataDir, bossNameIds);

        Assert.Equal("Demonia", NpcNameText(modDir, "engus", SPEEDFOG_NAME_2));
        Assert.Equal("Démonia", NpcNameText(modDir, "frafr", SPEEDFOG_NAME_2));
        Assert.Equal("Aging Untouchable", NpcNameText(modDir, "engus", SPEEDFOG_NAME));
        Assert.Equal("Margit, le Fantôme grognon", NpcNameText(modDir, "frafr", MARGIT_NAME));
    }

    [Fact]
    public void Apply_IgnoresANameKeyedBossThatIsNotPlaced()
    {
        using var tmp = new TempDir();
        var (modDir, gameDir, dataDir) = Setup(tmp, (SPEEDFOG_NAME, "Aging Untouchable"));
        var bossNameIds = new Dictionary<string, int> { ["Aging Untouchable"] = SPEEDFOG_NAME };

        TextTheme.Apply("halloween", modDir, gameDir, dataDir, bossNameIds);

        Assert.Equal("Aging Untouchable", NpcNameText(modDir, "engus", SPEEDFOG_NAME));
        Assert.Equal("Aging Untouchable", NpcNameText(modDir, "frafr", SPEEDFOG_NAME));
        Assert.Equal("Margit, the Grumpy Ghost", NpcNameText(modDir, "engus", MARGIT_NAME));
    }

    [Fact]
    public void Apply_FindsANameKeyedBossByIdEvenAfterAnEarlierThemeRewroteItsText()
    {
        using var tmp = new TempDir();
        // The summer theme ran first and already replaced the text.
        var (modDir, gameDir, dataDir) = Setup(tmp, (SPEEDFOG_NAME, "Sunburnt Devonia"));
        var bossNameIds = new Dictionary<string, int> { ["Devonia"] = SPEEDFOG_NAME };

        TextTheme.Apply("halloween", modDir, gameDir, dataDir, bossNameIds);

        Assert.Equal("Demonia", NpcNameText(modDir, "engus", SPEEDFOG_NAME));
    }
}
