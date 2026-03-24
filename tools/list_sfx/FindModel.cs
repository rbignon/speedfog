// Standalone: find all parts matching a model name in an MSB
static class FindModel
{
    public static void Run(string msbPath, string modelName)
    {
        var msb = SoulsFormats.MSBE.Read(msbPath);
        foreach (var a in msb.Parts.Assets)
        {
            if (a.ModelName == modelName)
                System.Console.WriteLine($"  {a.Name} EntityID={a.EntityID} Pos=({a.Position.X:F1},{a.Position.Y:F1},{a.Position.Z:F1})");
        }
    }
}
