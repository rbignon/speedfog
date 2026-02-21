using System.Text;

namespace ItemRandomizerWrapper;

/// <summary>
/// TextWriter that writes to two outputs simultaneously.
/// Used to capture Console.Out while still displaying to the user.
/// </summary>
public class TeeTextWriter : TextWriter
{
    private readonly TextWriter _primary;
    private readonly TextWriter _secondary;

    public TeeTextWriter(TextWriter primary, TextWriter secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public override Encoding Encoding => _primary.Encoding;

    public override void Write(char value)
    {
        _primary.Write(value);
        _secondary.Write(value);
    }

    public override void Write(string? value)
    {
        _primary.Write(value);
        _secondary.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _primary.WriteLine(value);
        _secondary.WriteLine(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        _primary.Write(buffer, index, count);
        _secondary.Write(buffer, index, count);
    }

    public override void Flush()
    {
        _primary.Flush();
        _secondary.Flush();
    }
}
