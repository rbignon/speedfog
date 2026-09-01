using System.Text;
using System.Text.RegularExpressions;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Updates the class-selection equipment text after
/// <see cref="ClassLoadoutInjector"/> forced new hand items.
///
/// The character-creation screen lists each class's gear as TEXT:
/// RandomizerCommon's CharacterWriter builds that list from the weapon names
/// it drew and writes it to <c>GR_LineHelp[297130 + classIndex]</c>
/// (Vagabond..Heavy Knight, menu order) in every language's
/// <c>menu.msgbnd.dcx</c>. ClassLoadoutInjector then replaces the weapons in
/// CharaInitParam, so without this patch the screen keeps showing
/// CharacterWriter's weapon names. For each recorded
/// <see cref="ClassLoadoutInjector.LoadoutSwap"/>, the old weapon/shield
/// name (looked up per language in the WeaponName FMGs of
/// <c>item.msgbnd.dcx</c> and <c>item_dlc02.msgbnd.dcx</c>) is replaced
/// in-place by the new one (or appended when the old name is not listed),
/// and the entry is re-wrapped to its original width.
///
/// The patched <c>menu_dlc02.msgbnd.dcx</c> (the bundle the game actually
/// reads and the randomizer writes) always ends up in the FogMod output
/// dir: when only the Item Randomizer wrote it (merge dir), the file is
/// copied first, which is safe because mods/fogmod wins the seed's
/// ModEngine load order with otherwise identical content.
/// </summary>
public static class ClassLoadoutTextPatcher
{
    /// <summary>GR_LineHelp entry for the first class (Vagabond); menu order follows.</summary>
    internal const int CLASS_DESC_FMG_BASE = 297130;

    public static void Patch(string modDir, string? mergeDir, string? gameDir, IReadOnlyList<ClassLoadoutInjector.LoadoutSwap> swaps)
    {
        if (swaps.Count == 0 || !swaps.Any(s => s.NewWeaponId > 0 || s.NewShieldId > 0))
            return;

        var languages = new SortedSet<string>();
        foreach (var baseDir in new[] { modDir, mergeDir })
        {
            if (baseDir == null)
                continue;
            var msgDir = Path.Combine(baseDir, "msg");
            if (!Directory.Exists(msgDir))
                continue;
            foreach (var langDir in Directory.GetDirectories(msgDir))
                languages.Add(Path.GetFileName(langDir));
        }

        Console.WriteLine("Patching class-selection equipment text for the forced loadout...");
        int patchedLangs = 0;
        foreach (var lang in languages)
        {
            // CharacterWriter's edits ship in the DLC bundle (the game
            // prefers dlc02 FMG entries over the base msgbnd at runtime and
            // the randomizer only writes the dlc02 bundles).
            var menuPath = ResolveIntoModDir(modDir, mergeDir, lang, "menu_dlc02.msgbnd.dcx")
                ?? ResolveIntoModDir(modDir, mergeDir, lang, "menu.msgbnd.dcx");
            if (menuPath == null)
            {
                Console.WriteLine($"  {lang}: no menu msgbnd found in mod or merge dir, skipping");
                continue;
            }

            var weaponNames = LoadWeaponNames(modDir, mergeDir, gameDir, lang);
            if (weaponNames.Count == 0)
            {
                Console.WriteLine($"  {lang}: no WeaponName FMG found, skipping");
                continue;
            }

            var bnd = BND4.Read(menuPath);
            var fmgFile = bnd.Files.Find(f => f.Name.EndsWith("GR_LineHelp.fmg", StringComparison.OrdinalIgnoreCase));
            if (fmgFile == null)
            {
                Console.WriteLine($"  {lang}: GR_LineHelp.fmg not in menu.msgbnd.dcx, skipping");
                continue;
            }

            var fmg = FMG.Read(fmgFile.Bytes);
            bool useSpaces = !lang.StartsWith("jpn") && !lang.StartsWith("zho");
            int replaced = PatchLineHelp(fmg, id => weaponNames.GetValueOrDefault(id), swaps, useSpaces, lang);
            if (replaced > 0)
            {
                fmgFile.Bytes = fmg.Write();
                File.WriteAllBytes(menuPath, bnd.Write());
                patchedLangs++;
            }
        }
        Console.WriteLine($"  Class-selection text patched in {patchedLangs} language(s)");
    }

    /// <summary>
    /// Replace the old weapon/shield names with the new ones in each class's
    /// GR_LineHelp entry. Names are looked up through
    /// <paramref name="weaponName"/> so the caller controls the language.
    /// CharacterWriter wraps lines MID-NAME (a space may have become a line
    /// break) and appends a stat-diff suffix like " (-9)" that belongs to the
    /// old weapon, so the match tolerates wrapped spaces and swallows the
    /// suffix; patched entries are then re-wrapped to their original width.
    /// Returns the number of name replacements performed.
    /// </summary>
    internal static int PatchLineHelp(
        FMG lineHelp,
        Func<int, string?> weaponName,
        IReadOnlyList<ClassLoadoutInjector.LoadoutSwap> swaps,
        bool useSpaces = true,
        string lang = "?")
    {
        int replaced = 0;
        foreach (var swap in swaps)
        {
            var entry = lineHelp.Entries.Find(e => e.ID == CLASS_DESC_FMG_BASE + swap.ClassIndex);
            if (entry == null || string.IsNullOrEmpty(entry.Text))
            {
                Console.WriteLine($"  {lang}: GR_LineHelp[{CLASS_DESC_FMG_BASE + swap.ClassIndex}] missing or empty, class {swap.ClassIndex} text not patched");
                continue;
            }

            var text = entry.Text;
            int width = text.Split('\n').Max(l => l.Length);
            bool touched = false;
            foreach (var (oldId, newId) in new[] { (swap.OldWeaponId, swap.NewWeaponId), (swap.OldShieldId, swap.NewShieldId) })
            {
                if (newId <= 0)
                    continue;
                var oldName = weaponName(oldId);
                var newName = weaponName(newId);
                if (string.IsNullOrEmpty(newName))
                {
                    Console.WriteLine($"  {lang}: no WeaponName for {newId}, class {swap.ClassIndex} slot not patched");
                    continue;
                }
                // The old name can be unmatchable: CharacterWriter only
                // lists a priority subset of the gear, and an ash-of-war
                // (custom) weapon id has no WeaponName at all. The forced
                // item must still be named, so append it instead.
                var match = string.IsNullOrEmpty(oldName)
                    ? System.Text.RegularExpressions.Match.Empty
                    : NamePattern(oldName).Match(text);
                if (match.Success)
                {
                    text = text.Substring(0, match.Index) + newName + text.Substring(match.Index + match.Length);
                }
                else
                {
                    Console.WriteLine($"  {lang}: old name for {oldId} not in GR_LineHelp[{CLASS_DESC_FMG_BASE + swap.ClassIndex}], appending \"{newName}\"");
                    text = text + (useSpaces ? ", " : "\u3001") + newName;
                }
                touched = true;
                replaced++;
            }
            if (touched)
                entry.Text = Rewrap(text, width, useSpaces);
        }
        return replaced;
    }

    /// <summary>
    /// Regex matching a weapon name whose spaces may have been turned into
    /// line breaks by CharacterWriter's wrapping, optionally followed by its
    /// stat-diff suffix (" (-9)" spaced, or fullwidth CJK parentheses).
    /// </summary>
    private static Regex NamePattern(string name)
    {
        var body = string.Join("[ \n]", name.Split(' ').Select(Regex.Escape));
        // Anchor on the LIST separators, not letter boundaries: a standalone
        // name is preceded by start-of-text or a comma (ASCII or ideographic,
        // whose following space may have become a line break) and followed by
        // a comma or end-of-text (after its optional stat-diff suffix).
        // Letter lookarounds are not enough: "Longsword" inside
        // "Lordsworn's Longsword" is preceded by a plain space.
        return new Regex(
            "(?<=^|[,\u3001][ \n]?)"
            + body
            + "(?:[ \n]\\(-\\d+\\)|\uFF08-\\d+\uFF09)?"
            + "(?=[,\u3001]|$)");
    }

    /// <summary>
    /// Greedy re-wrap to the given character width: spaced languages break at
    /// spaces, CJK breaks at any character. An overlong single word stays on
    /// its own line.
    /// </summary>
    internal static string Rewrap(string text, int width, bool useSpaces)
    {
        width = Math.Max(width, 12);
        var flat = text.Replace("\n", useSpaces ? " " : "");
        var sb = new StringBuilder();
        if (useSpaces)
        {
            int lineLen = 0;
            foreach (var word in flat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (lineLen == 0)
                {
                    sb.Append(word);
                    lineLen = word.Length;
                }
                else if (lineLen + 1 + word.Length <= width)
                {
                    sb.Append(' ').Append(word);
                    lineLen += 1 + word.Length;
                }
                else
                {
                    sb.Append('\n').Append(word);
                    lineLen = word.Length;
                }
            }
        }
        else
        {
            for (int i = 0; i < flat.Length; i += width)
                sb.Append(flat, i, Math.Min(width, flat.Length - i)).Append('\n');
            if (sb.Length > 0)
                sb.Length--;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Return the mod-dir path of msg/{lang}/{fileName}, copying it from the
    /// merge dir when only the Item Randomizer wrote it; null when neither
    /// side has it.
    /// </summary>
    private static string? ResolveIntoModDir(string modDir, string? mergeDir, string lang, string fileName)
    {
        var modPath = Path.Combine(modDir, "msg", lang, fileName);
        if (File.Exists(modPath))
            return modPath;
        if (mergeDir != null)
        {
            var mergePath = Path.Combine(mergeDir, "msg", lang, fileName);
            if (File.Exists(mergePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(modPath)!);
                File.Copy(mergePath, modPath);
                return modPath;
            }
        }
        return null;
    }

    /// <summary>
    /// Load WeaponName entries for one language from item.msgbnd.dcx,
    /// item_dlc01.msgbnd.dcx (SOTE names) and item_dlc02.msgbnd.dcx (mod dir
    /// first, then merge dir, then the installed game for bundles the seed
    /// does not override; read-only). Later bundles override earlier ones on
    /// ID collision.
    /// </summary>
    private static Dictionary<int, string> LoadWeaponNames(string modDir, string? mergeDir, string? gameDir, string lang)
    {
        var names = new Dictionary<int, string>();
        foreach (var fileName in new[] { "item.msgbnd.dcx", "item_dlc01.msgbnd.dcx", "item_dlc02.msgbnd.dcx" })
        {
            string? path = null;
            foreach (var baseDir in new[] { modDir, mergeDir, gameDir })
            {
                if (baseDir == null)
                    continue;
                var candidate = Path.Combine(baseDir, "msg", lang, fileName);
                if (File.Exists(candidate))
                { path = candidate; break; }
            }
            if (path == null)
                continue;

            var bnd = BND4.Read(path);
            foreach (var file in bnd.Files.Where(f => f.Name.EndsWith("WeaponName.fmg", StringComparison.OrdinalIgnoreCase)))
            {
                var fmg = FMG.Read(file.Bytes);
                foreach (var entry in fmg.Entries)
                    if (!string.IsNullOrWhiteSpace(entry.Text))
                        names[entry.ID] = entry.Text;
            }
        }
        return names;
    }
}
