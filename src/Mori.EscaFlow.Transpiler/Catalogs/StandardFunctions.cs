namespace Mori.EscaFlow.Transpiler.Catalogs;

public sealed record StdFunction(string Name, string Group, string ResultRule, int MinArgs, int MaxArgs)
{
    /// <summary>The result type given the argument types, or null when it cannot be decided.</summary>
    public string? ResultType(IReadOnlyList<string?> argTypes)
    {
        switch (ResultRule)
        {
            case "BOOL": return "BOOL";
            case "DINT": return "DINT";
            case "REAL": return argTypes.Count > 0 && argTypes[0] is { } t && t.Equals("LREAL", StringComparison.OrdinalIgnoreCase) ? "LREAL" : "REAL";
            case "STRING": return "STRING";
            case "arg0": return argTypes.Count > 0 ? argTypes[0] : null;
            case "arg1": return argTypes.Count > 1 ? argTypes[1] : null;
            case "widest":
            {
                string? r = null;
                foreach (var a in argTypes) r = AtomicTypes.Wider(r, a);
                return r;
            }
            case "suffix":
                return Name.Contains("_TO_", StringComparison.Ordinal) ? Name[(Name.LastIndexOf("_TO_", StringComparison.Ordinal) + 4)..] : null;
            default: return null;
        }
    }
}

/// <summary>The IEC 61131-3 standard functions usable in <c>fn</c> expressions. Type conversions follow the <c>X_TO_Y</c> pattern and are recognised generically.</summary>
public static class StandardFunctions
{
    private static readonly StdFunction[] Table =
    {
        // numeric
        new("ABS", "numeric", "arg0", 1, 1),
        new("SQRT", "numeric", "REAL", 1, 1),
        new("LN", "numeric", "REAL", 1, 1),
        new("LOG", "numeric", "REAL", 1, 1),
        new("EXP", "numeric", "REAL", 1, 1),
        new("SIN", "numeric", "REAL", 1, 1),
        new("COS", "numeric", "REAL", 1, 1),
        new("TAN", "numeric", "REAL", 1, 1),
        new("ASIN", "numeric", "REAL", 1, 1),
        new("ACOS", "numeric", "REAL", 1, 1),
        new("ATAN", "numeric", "REAL", 1, 1),
        new("EXPT", "numeric", "REAL", 2, 2),
        new("TRUNC", "numeric", "DINT", 1, 1),
        new("ROUND", "numeric", "DINT", 1, 1),
        new("DEG", "numeric", "REAL", 1, 1),
        new("RAD", "numeric", "REAL", 1, 1),
        new("ADD", "arithmetic", "widest", 2, int.MaxValue),
        new("SUB", "arithmetic", "widest", 2, 2),
        new("MUL", "arithmetic", "widest", 2, int.MaxValue),
        new("DIV", "arithmetic", "widest", 2, 2),
        new("MOD", "arithmetic", "widest", 2, 2),
        // bit string
        new("SHL", "bits", "arg0", 2, 2),
        new("SHR", "bits", "arg0", 2, 2),
        new("ROL", "bits", "arg0", 2, 2),
        new("ROR", "bits", "arg0", 2, 2),
        new("AND", "bits", "widest", 2, int.MaxValue),
        new("OR", "bits", "widest", 2, int.MaxValue),
        new("XOR", "bits", "widest", 2, int.MaxValue),
        new("NOT", "bits", "arg0", 1, 1),
        new("SWAP_BYTES", "bits", "arg0", 1, 2),
        // selection
        new("SEL", "selection", "arg1", 3, 3),
        new("MUX", "selection", "arg1", 3, int.MaxValue),
        new("MIN", "selection", "widest", 2, int.MaxValue),
        new("MAX", "selection", "widest", 2, int.MaxValue),
        new("LIMIT", "selection", "arg1", 3, 3),
        // comparison
        new("GT", "comparison", "BOOL", 2, int.MaxValue),
        new("GE", "comparison", "BOOL", 2, int.MaxValue),
        new("EQ", "comparison", "BOOL", 2, int.MaxValue),
        new("LE", "comparison", "BOOL", 2, int.MaxValue),
        new("LT", "comparison", "BOOL", 2, int.MaxValue),
        new("NE", "comparison", "BOOL", 2, 2),
        new("IN_RANGE", "comparison", "BOOL", 3, 3),
        new("MASKED_EQUAL", "comparison", "BOOL", 3, 3),
        // string
        new("LEN", "string", "DINT", 1, 1),
        new("CONCAT", "string", "STRING", 2, int.MaxValue),
        new("LEFT", "string", "STRING", 2, 2),
        new("RIGHT", "string", "STRING", 2, 2),
        new("MID", "string", "STRING", 3, 3),
        new("FIND", "string", "DINT", 2, 2),
        new("INSERT", "string", "STRING", 3, 3),
        new("DELETE", "string", "STRING", 3, 3),
        new("REPLACE", "string", "STRING", 4, 4),
        new("UPPER", "string", "STRING", 1, 1),
        new("LOWER", "string", "STRING", 1, 1),
        // misc
        new("SIZE", "misc", "DINT", 1, 3),
        new("BYTE_SWAP", "bits", "arg0", 1, 1),
        new("BIT_FIELD_DISTRIBUTE", "bits", "arg0", 5, 5),
        new("MASKED_MOVE", "bits", "arg0", 3, 3),
        new("TO_BCD", "conversion", "arg0", 1, 1),
        new("FROM_BCD", "conversion", "arg0", 1, 1),
        new("STRING_TO_INT", "conversion", "DINT", 1, 1),
        new("STRING_TO_REAL", "conversion", "REAL", 1, 1),
        new("INT_TO_STRING", "conversion", "STRING", 1, 1),
        new("REAL_TO_STRING", "conversion", "STRING", 1, 1),
    };

    private static readonly Dictionary<string, StdFunction> ByName = Table.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<StdFunction> All => Table;

    public static StdFunction? Get(string name)
    {
        if (ByName.TryGetValue(name, out var f)) return f;
        // X_TO_Y conversions between atomic types
        var idx = name.IndexOf("_TO_", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            var from = name[..idx];
            var to = name[(idx + 4)..];
            if ((AtomicTypes.IsAtomic(from) || from.Equals("STRING", StringComparison.OrdinalIgnoreCase)) && (AtomicTypes.IsAtomic(to) || to.Equals("STRING", StringComparison.OrdinalIgnoreCase)))
                return new StdFunction(name.ToUpperInvariant(), "conversion", "suffix", 1, 1);
        }
        return null;
    }

    public static bool IsStandard(string name) => Get(name) is not null;
}
