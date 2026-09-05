using System.Linq;
using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// A theme text plugin (summer, halloween, ...): reskins boss healthbar names
/// (NpcName) and recurring UI banners to a theme by editing FMG entries,
/// mirroring RunCompleteInjector. Runs only when [plugin.&lt;theme&gt;]
/// enabled = true. Boss ids absent from the game's FMGs are skipped
/// (tolerant); UI ids absent are added as new FMG entries instead. A
/// missing FMG file or bnd is skipped either way.
///
/// Only the English (engus) and French (frafr) message archives are edited
/// (MsgBndEditor.TargetLanguages); the catalogue carries content for those
/// two languages only. Other languages keep their vanilla names.
/// </summary>
public static class TextTheme
{
    private static readonly string[] BossBnds = { "item.msgbnd.dcx", "item_dlc02.msgbnd.dcx" };

    public static void Apply(string theme, string modDir, string gameDir, string dataDir)
    {
        var catalog = TextThemeCatalogLoader.Load(
            Path.Combine(dataDir, "plugins", $"{theme}.toml"), theme);
        if (catalog.IsEmpty)
            return;

        var gameMsgDir = Path.Combine(gameDir, "msg");
        if (!Directory.Exists(gameMsgDir))
        {
            Console.WriteLine($"{theme} theme: game msg directory not found, skipping");
            return;
        }

        var bossById = catalog.Bosses.ToDictionary(b => b.NpcNameId);
        int touchedLangs = 0;
        // Languages touch disjoint files; process them in parallel.
        Parallel.ForEach(Directory.GetDirectories(gameMsgDir), langDir =>
        {
            var lang = Path.GetFileName(langDir);
            if (!MsgBndEditor.TargetLanguages.Contains(lang))
                return;
            int n = ApplyBossEpithets(modDir, langDir, lang, bossById)
                  + ApplyUiStrings(modDir, langDir, lang, catalog.Ui);
            if (n > 0)
                Interlocked.Increment(ref touchedLangs);
        });

        Console.WriteLine(
            $"{theme} theme: applied {catalog.Bosses.Count} boss + {catalog.Ui.Count} UI overrides across {touchedLangs} languages");
    }

    private static int ApplyBossEpithets(string modDir, string langDir, string lang,
        IReadOnlyDictionary<int, ThemeBossEntry> bossById)
    {
        int total = 0;
        foreach (var bndName in BossBnds)
            total += MsgBndEditor.EditBnd(modDir, langDir, lang, bndName, bnd =>
            {
                int n = 0;
                foreach (var file in bnd.Files.Where(f => f.Name.Contains("NpcName")))
                {
                    FMG fmg;
                    try
                    { fmg = FMG.Read(file.Bytes); }
                    catch { continue; }

                    bool changed = false;
                    foreach (var entry in fmg.Entries)
                    {
                        if (entry.Text == null)
                            continue;
                        if (bossById.TryGetValue(entry.ID, out var b))
                        {
                            entry.Text = MsgBndEditor.LocalizedText(lang, b.En, b.Fr);
                            changed = true;
                            n++;
                        }
                    }
                    if (changed)
                        file.Bytes = fmg.Write();
                }
                return n;
            });
        return total;
    }

    private static int ApplyUiStrings(string modDir, string langDir, string lang,
        IReadOnlyList<ThemeUiEntry> ui)
    {
        int total = 0;
        foreach (var group in ui.GroupBy(u => u.Bnd))
            total += MsgBndEditor.EditBnd(modDir, langDir, lang, group.Key, bnd =>
            {
                int n = 0;
                foreach (var u in group)
                {
                    var file = bnd.Files.Find(f => f.Name.Contains(u.Fmg));
                    if (file == null)
                        continue;

                    FMG fmg;
                    try
                    { fmg = FMG.Read(file.Bytes); }
                    catch { continue; }

                    var text = MsgBndEditor.LocalizedText(lang, u.En, u.Fr);
                    var existing = fmg.Entries.Find(e => e.ID == u.Id);
                    if (existing != null)
                        existing.Text = text;
                    else
                        fmg.Entries.Add(new FMG.Entry(u.Id, text));

                    file.Bytes = fmg.Write();
                    n++;
                }
                return n;
            });
        return total;
    }
}
