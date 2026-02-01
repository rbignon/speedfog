// writer/SpeedFogWriter/Program.cs
namespace SpeedFogWriter;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("SpeedFogWriter v0.1");

        if (args.Length < 3)
        {
            Console.WriteLine("Usage: SpeedFogWriter <graph.json> <game_dir> <output_dir>");
            return 1;
        }

        Console.WriteLine($"Graph: {args[0]}");
        Console.WriteLine($"Game dir: {args[1]}");
        Console.WriteLine($"Output dir: {args[2]}");

        return 0;
    }
}
