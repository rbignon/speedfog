using SoulsFormats;

/// <summary>
/// Identify DCX-compressed files by their decompressed content: prints the inner
/// magic and size, and lists the entries of BND3/BND4/TPF containers. Meant for
/// files an archive unpacker could not name (the _unknown/ folder of a
/// dictionary-based unpack after a game patch).
/// </summary>
static class BndList
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: game_inspect bnd-list <file.dcx> [<file.dcx> ...]");
            return 1;
        }

        foreach (var path in args.Skip(1))
        {
            byte[] bytes;
            try { bytes = DCX.Is(path) ? DCX.Decompress(path) : File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Console.WriteLine($"{Path.GetFileName(path)}: cannot decompress ({ex.Message})");
                continue;
            }

            string magic = bytes.Length >= 4 ? System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Replace('\0', '.') : "";
            Console.Write($"{Path.GetFileName(path)}: {magic} {bytes.Length} bytes");
            if (BND4.Is(bytes))
            {
                var bnd = BND4.Read(bytes);
                Console.WriteLine($", BND4 with {bnd.Files.Count} files");
                foreach (var f in bnd.Files) Console.WriteLine($"    {f.Name} ({f.Bytes.Length})");
            }
            else if (BND3.Is(bytes))
            {
                var bnd = BND3.Read(bytes);
                Console.WriteLine($", BND3 with {bnd.Files.Count} files");
                foreach (var f in bnd.Files) Console.WriteLine($"    {f.Name} ({f.Bytes.Length})");
            }
            else if (TPF.Is(bytes))
            {
                var tpf = TPF.Read(bytes);
                Console.WriteLine($", TPF with {tpf.Textures.Count} textures");
                foreach (var t in tpf.Textures) Console.WriteLine($"    {t.Name}");
            }
            else
            {
                Console.WriteLine();
            }
        }
        return 0;
    }
}
