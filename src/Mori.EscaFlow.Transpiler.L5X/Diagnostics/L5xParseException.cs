namespace Mori.EscaFlow.Transpiler.L5X.Diagnostics;

/// <summary>Raised only for I/O failures or malformed XML. Content problems become diagnostics instead.</summary>
public sealed class L5xParseException : Exception
{
    public int? LineNumber { get; }
    public int? LinePosition { get; }

    public L5xParseException(string message, Exception? inner = null, int? lineNumber = null, int? linePosition = null)
        : base(message, inner)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }
}
