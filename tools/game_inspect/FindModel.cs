using SoulsFormats;

static class FindModel
{
    public static void Run(string msbPath, string modelName)
    {
        var msb = MSBE.Read(msbPath);
        foreach (var a in msb.Parts.Assets)
        {
            if (a.ModelName == modelName)
                Console.WriteLine($"  {a.Name} EntityID={a.EntityID} Pos=({a.Position.X:F1},{a.Position.Y:F1},{a.Position.Z:F1})");
        }
    }
}
