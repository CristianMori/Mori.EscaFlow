namespace L5x.Parsing;

/// <summary>
/// Immutable, shareable context for the logic text parsers: which names are add-on instructions.
/// Built once the AddOnInstructionDefinitions section has been read, before any rung or ST text is parsed.
/// </summary>
public sealed class LogicParseContext
{
    public static readonly LogicParseContext Empty = new(Array.Empty<string>());

    private readonly HashSet<string> _aoiNames;

    public LogicParseContext(IEnumerable<string> addOnInstructionNames)
    {
        _aoiNames = new HashSet<string>(addOnInstructionNames, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> AddOnInstructionNames => _aoiNames;

    public bool IsAddOnInstruction(string name) => _aoiNames.Contains(name);
}
