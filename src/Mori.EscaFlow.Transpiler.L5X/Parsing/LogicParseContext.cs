namespace Mori.EscaFlow.Transpiler.L5X.Parsing;

/// <summary>
/// Immutable, shareable context for the logic text parsers: which names are add-on instructions.
/// Built once the AddOnInstructionDefinitions section has been read, before any rung or ST text is parsed.
/// </summary>
/// <remarks>
/// The neutral text of a rung does not say whether <c>Foo(a, b)</c> is a built-in instruction or an add-on
/// instruction call, and the two have different operand rules (built-ins have a fixed signature from
/// <c>InstructionSignatures.txt</c>, add-ons take their backing tag first and then the required parameters), so the
/// parsers need the set of add-on names up front. Logix names are case-insensitive, hence the comparer.
/// </remarks>
public sealed class LogicParseContext
{
    /// <summary>A context with no add-on instructions, for parsing isolated snippets.</summary>
    public static readonly LogicParseContext Empty = new(Array.Empty<string>());

    private readonly HashSet<string> _aoiNames;

    public LogicParseContext(IEnumerable<string> addOnInstructionNames)
    {
        _aoiNames = new HashSet<string>(addOnInstructionNames, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> AddOnInstructionNames => _aoiNames;

    public bool IsAddOnInstruction(string name) => _aoiNames.Contains(name);
}
