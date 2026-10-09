namespace Mori.EscaFlow.Transpiler.L5X.Diagnostics;

/// <summary>
/// Raised only for I/O failures or malformed XML. Content problems become diagnostics instead, because a project
/// with one odd rung is still worth parsing; a file that cannot be read or is not well-formed XML is not.
/// </summary>
public sealed class L5xParseException : Exception
{
    /// <summary>Line of the XML error when the underlying reader reported one.</summary>
    public int? LineNumber { get; }
    public int? LinePosition { get; }

    public L5xParseException(string message, Exception? inner = null, int? lineNumber = null, int? linePosition = null)
        : base(message, inner)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }
}
