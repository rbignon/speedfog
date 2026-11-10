namespace StaticModBuilder.Winter;

/// <summary>Raised when the winter build cannot proceed (bad catalogue, missing input).</summary>
public sealed class WinterBuildException : Exception
{
    public WinterBuildException(string message) : base(message) { }
}
