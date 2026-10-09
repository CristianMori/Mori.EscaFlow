using System.Reflection;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;

/// <summary>How an operand position of a ladder instruction should be interpreted.</summary>
public enum OperandRole
{
    /// <summary>Literal, tag or bare identifier; no further hint (e.g. GSV instance name, motion enumerations).</summary>
    Any,
    /// <summary>A tag reference (immediate numbers are still accepted).</summary>
    Tag,
    /// <summary>An enumeration-like word: GSV/SSV class and attribute names, SWPB order, FAL/FSC mode, ...</summary>
    Keyword,
    /// <summary>A routine name (JSR, SBR, FOR, SFP, SFR).</summary>
    Routine,
    /// <summary>A jump label (JMP, LBL).</summary>
    Label,
    /// <summary>An arithmetic / comparison expression (CPT, CMP, FAL, FSC).</summary>
    Expression,
    /// <summary>A literal constant.</summary>
    Literal,
}

/// <summary>
/// The built-in ladder instruction table (mnemonic and per-position operand roles), loaded from the embedded
/// <c>InstructionSignatures.txt</c> resource. Unknown mnemonics are treated as all-tag instructions.
/// <para>
/// Resource format: one <c>MNEMONIC | role, role, ...</c> line per instruction, <c>#</c> comments, blank lines
/// ignored; a trailing <c>...</c> repeats the last role for every further operand (JSR input/return lists, CMP and
/// FSC variants). The table lives in a text file rather than in code so the instruction set can be extended
/// without touching the parser, and it is loaded lazily and once because the parser runs on many threads.
/// </para>
/// </summary>
public static class InstructionSignatures
{
    private sealed record Signature(OperandRole[] Roles, bool RepeatLast);

    private static readonly Lazy<Dictionary<string, Signature>> Table = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>All mnemonics in the built-in table.</summary>
    public static IReadOnlyCollection<string> Mnemonics => Table.Value.Keys;

    public static bool IsKnown(string mnemonic) => Table.Value.ContainsKey(mnemonic);

    /// <summary>The declared number of operands, or null for an unknown mnemonic.</summary>
    public static int? OperandCount(string mnemonic) => Table.Value.TryGetValue(mnemonic, out var s) ? s.Roles.Length : null;

    /// <summary>Role of operand <paramref name="index"/> (0-based). Unknown mnemonics and positions past the table give <see cref="OperandRole.Tag"/> unless the signature repeats its last role.</summary>
    public static OperandRole RoleAt(string mnemonic, int index)
    {
        if (index < 0 || !Table.Value.TryGetValue(mnemonic, out var s)) return OperandRole.Tag;
        if (index < s.Roles.Length) return s.Roles[index];
        if (s.RepeatLast && s.Roles.Length > 0) return s.Roles[^1];
        return OperandRole.Tag;
    }

    /// <summary>Reads the embedded resource; a missing resource gives an empty table (every instruction then counts as unknown) rather than a failure.</summary>
    private static Dictionary<string, Signature> Load()
    {
        var table = new Dictionary<string, Signature>(StringComparer.OrdinalIgnoreCase);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mori.EscaFlow.Transpiler.L5X.InstructionSignatures.txt");
        if (stream is null) return table;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var bar = line.IndexOf('|');
            if (bar <= 0) continue;
            var mnemonic = line[..bar].Trim();
            var roleText = line[(bar + 1)..].Trim();
            var roles = new List<OperandRole>();
            var repeat = false;
            if (roleText.Length > 0)
            {
                foreach (var part in roleText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part == "...")
                    {
                        repeat = true;
                        continue;
                    }
                    roles.Add(ParseRole(part));
                }
            }
            table[mnemonic] = new Signature(roles.ToArray(), repeat);
        }
        return table;
    }

    private static OperandRole ParseRole(string text) => text.ToLowerInvariant() switch
    {
        "tag" => OperandRole.Tag,
        "keyword" => OperandRole.Keyword,
        "routine" => OperandRole.Routine,
        "label" => OperandRole.Label,
        "expression" => OperandRole.Expression,
        "literal" => OperandRole.Literal,
        _ => OperandRole.Any,
    };
}
