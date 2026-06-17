using SoulsFormats;

/// <summary>
/// Dump FMG (text) entries from a msgbnd.dcx archive, optionally filtered by a
/// case-insensitive substring. Used to map menu strings (e.g. "Shadow Realm
/// Blessing") to their FMG message IDs.
/// </summary>
static class DumpFmg
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: game_inspect dump-fmg <msgbnd.dcx> [substring]");
            return 1;
        }

        string bndPath = args[1];
        string? needle = args.Length > 2 ? args[2] : null;

        var bnd = BND4.Read(bndPath);
        foreach (var file in bnd.Files)
        {
            if (!file.Name.EndsWith(".fmg", StringComparison.OrdinalIgnoreCase))
                continue;

            FMG fmg;
            try { fmg = FMG.Read(file.Bytes); }
            catch { continue; }

            string fmgName = Path.GetFileName(file.Name);
            foreach (var entry in fmg.Entries)
            {
                if (entry.Text == null) continue;
                if (needle != null &&
                    entry.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string text = entry.Text.Replace("\n", "\\n");
                Console.WriteLine($"{fmgName,-30} {entry.ID,-10} {text}");
            }
        }
        return 0;
    }
}
