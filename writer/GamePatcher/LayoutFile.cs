using System.Text.RegularExpressions;

namespace GamePatcher;

/// <summary>
/// Helpers for FromSoft menu .layout files (TextureAtlas XML bundled in
/// 01_common.sblytbnd.dcx): machine-generated, one SubTexture tag per line.
/// Edits are line-based so every untouched line survives byte-for-byte.
/// </summary>
internal static class LayoutFile
{
    /// <summary>
    /// Return the (x, y, width, height) rect of a sprite entry. The name is
    /// matched exactly against the layout's name="{spriteName}.png" attribute.
    /// Assumes the machine-generated attribute order (name, x, y, width,
    /// height); a reordered file degrades to a warn+skip in the caller.
    /// </summary>
    internal static (int X, int Y, int Width, int Height) FindSubTexture(string layoutXml, string spriteName)
    {
        var regex = new Regex(
            "name=\"" + Regex.Escape(spriteName) + "\\.png\""
            + "\\s+x=\"(\\d+)\"\\s+y=\"(\\d+)\"\\s+width=\"(\\d+)\"\\s+height=\"(\\d+)\"");
        var m = regex.Match(layoutXml);
        if (!m.Success)
        {
            throw new InvalidDataException($"sprite '{spriteName}' not found in layout");
        }
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
    }

    /// <summary>
    /// Return the layout with the sprite's SubTexture line removed (so the
    /// atlas no longer resolves the name and the engine falls back to a
    /// standalone texture lookup).
    /// </summary>
    internal static string RemoveSubTexture(string layoutXml, string spriteName)
    {
        var lineRegex = new Regex(
            "^.*name=\"" + Regex.Escape(spriteName) + "\\.png\".*(\\r?\\n|$)",
            RegexOptions.Multiline);
        if (!lineRegex.IsMatch(layoutXml))
        {
            throw new InvalidDataException($"sprite '{spriteName}' not found in layout");
        }
        return lineRegex.Replace(layoutXml, "", 1);
    }
}
