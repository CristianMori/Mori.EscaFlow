using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>How a ladder instruction is represented in the neutral ladder model and lowered.</summary>
public enum LadderRole
{
    /// <summary>Conditions the rung (XIC, comparisons, ONS, AFI, ...).</summary>
    Contact,

    /// <summary>Writes the rung condition (OTE, OTL, OTU, OSR, OSF).</summary>
    Coil,

    /// <summary>Runs every scan with the rung condition bound to its enable (timers, counters, add-ons, always-scan services).</summary>
    BlockInput,

    /// <summary>Runs only when the rung condition is true.</summary>
    BlockGate,

    /// <summary>No effect on execution (NOP, SBR).</summary>
    Passive,
    Jump,
    Label,
    McrBegin,
    Return,
}

/// <summary>The single table that classifies Logix ladder instructions and names their IEC equivalents.</summary>
public static class LogixInstructionMap
{
    private static readonly Dictionary<string, LadderRole> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XIC"] = LadderRole.Contact, ["XIO"] = LadderRole.Contact,
        ["EQU"] = LadderRole.Contact, ["NEQ"] = LadderRole.Contact, ["GRT"] = LadderRole.Contact, ["GEQ"] = LadderRole.Contact, ["LES"] = LadderRole.Contact, ["LEQ"] = LadderRole.Contact,
        ["LIM"] = LadderRole.Contact, ["MEQ"] = LadderRole.Contact, ["CMP"] = LadderRole.Contact, ["DTR"] = LadderRole.Contact,
        ["ONS"] = LadderRole.Contact, ["AFI"] = LadderRole.Contact,
        ["OTE"] = LadderRole.Coil, ["OTL"] = LadderRole.Coil, ["OTU"] = LadderRole.Coil, ["OSR"] = LadderRole.Coil, ["OSF"] = LadderRole.Coil,
        ["NOP"] = LadderRole.Passive, ["SBR"] = LadderRole.Passive,
        ["JMP"] = LadderRole.Jump, ["LBL"] = LadderRole.Label, ["MCR"] = LadderRole.McrBegin, ["TND"] = LadderRole.Return, ["RET"] = LadderRole.Return,
    };

    /// <summary>Logix mnemonic to the IEC standard function used in the lowered assignment (dest := fn(args)).</summary>
    private static readonly Dictionary<string, string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = "ABS", ["SQR"] = "SQRT", ["SIN"] = "SIN", ["COS"] = "COS", ["TAN"] = "TAN", ["ASN"] = "ASIN", ["ACS"] = "ACOS", ["ATN"] = "ATAN",
        ["LN"] = "LN", ["LOG"] = "LOG", ["XPY"] = "EXPT", ["DEG"] = "DEG", ["RAD"] = "RAD", ["TRN"] = "TRUNC", ["TOD"] = "TO_BCD", ["FRD"] = "FROM_BCD",
        ["UPPER"] = "UPPER", ["LOWER"] = "LOWER", ["STOD"] = "STRING_TO_INT", ["STOR"] = "STRING_TO_REAL", ["DTOS"] = "INT_TO_STRING", ["RTOS"] = "REAL_TO_STRING",
        ["CONCAT"] = "CONCAT", ["DELETE"] = "DELETE", ["INSERT"] = "INSERT", ["MID"] = "MID", ["FIND"] = "FIND",
        ["MVM"] = "MASKED_MOVE", ["BTD"] = "BIT_FIELD_DISTRIBUTE", ["SWPB"] = "SWAP_BYTES",
    };

    /// <summary>ST expression function names (Logix spelling) to IEC names.</summary>
    private static readonly Dictionary<string, string> StFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = "ABS", ["SQRT"] = "SQRT", ["SQR"] = "SQRT", ["LN"] = "LN", ["LOG"] = "LOG", ["SIN"] = "SIN", ["COS"] = "COS", ["TAN"] = "TAN",
        ["ASIN"] = "ASIN", ["ACOS"] = "ACOS", ["ATAN"] = "ATAN", ["ASN"] = "ASIN", ["ACS"] = "ACOS", ["ATN"] = "ATAN",
        ["DEG"] = "DEG", ["RAD"] = "RAD", ["TRUNC"] = "TRUNC", ["TRN"] = "TRUNC", ["XPY"] = "EXPT", ["SIZE"] = "SIZE",
    };

    /// <summary>Members cleared by RES per structure type.</summary>
    private static readonly Dictionary<string, string[]> ResetMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TIMER"] = new[] { "ACC", "EN", "TT", "DN" },
        ["COUNTER"] = new[] { "ACC", "CU", "CD", "DN", "OV", "UN" },
        ["CONTROL"] = new[] { "POS", "EN", "EU", "DN", "EM", "ER", "UL", "IN", "FD" },
    };

    public static LadderRole RoleOf(string mnemonic, bool isAddOn)
    {
        if (isAddOn) return LadderRole.BlockInput;
        if (Roles.TryGetValue(mnemonic, out var role)) return role;
        return LogixServiceCatalog.Instance.RunsEveryScan(mnemonic) ? LadderRole.BlockInput : LadderRole.BlockGate;
    }

    public static string? FunctionFor(string mnemonic) => Functions.GetValueOrDefault(mnemonic);
    public static string? StFunctionFor(string name) => StFunctions.GetValueOrDefault(name);
    public static string[]? ResetMembersOf(string typeName) => ResetMembers.GetValueOrDefault(typeName);

    /// <summary>Instructions lowered to an assignment of the result to their last operand: dest := left op right.</summary>
    public static string? BinaryOperatorFor(string mnemonic) => mnemonic.ToUpperInvariant() switch
    {
        "ADD" => "add", "SUB" => "sub", "MUL" => "mul", "DIV" => "div", "MOD" => "mod",
        "AND" => "and", "OR" => "or", "XOR" => "xor",
        _ => null,
    };

    public static string? ComparisonFor(string mnemonic) => mnemonic.ToUpperInvariant() switch
    {
        "EQU" => "eq", "NEQ" => "ne", "GRT" => "gt", "GEQ" => "ge", "LES" => "lt", "LEQ" => "le",
        _ => null,
    };
}
