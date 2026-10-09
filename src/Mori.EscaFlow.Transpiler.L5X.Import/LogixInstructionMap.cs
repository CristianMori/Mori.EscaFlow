using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>How a ladder instruction is represented in the neutral ladder model and lowered.</summary>
/// <remarks>
/// Logix evaluates a rung left to right: input instructions combine into the rung-condition-in of whatever follows,
/// output instructions act on that condition. The role decides which neutral element a mnemonic becomes (contact,
/// coil, block, ...) and therefore how <see cref="LadderLowering"/> threads the condition through it.
/// </remarks>
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

    /// <summary>JMP: skips the rungs up to the matching label when the rung condition is true.</summary>
    Jump,

    /// <summary>LBL: the target of a jump; never conditioned.</summary>
    Label,

    /// <summary>MCR: opens a master-control zone (or closes the open one) whose rungs see the zone condition as their incoming condition.</summary>
    McrBegin,

    /// <summary>TND / RET: leaves the routine when the rung condition is true.</summary>
    Return,
}

/// <summary>The single table that classifies Logix ladder instructions and names their IEC equivalents.</summary>
/// <remarks>
/// Keeping the classification in one place means a new instruction is handled by one edit here (plus the service
/// catalog, which answers whether an unlisted instruction runs every scan or only when its rung is true).
/// </remarks>
public static class LogixInstructionMap
{
    /// <summary>Explicit roles for the instructions that are not plain vendor services; everything else is a block whose enable mode comes from the catalog.</summary>
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
    /// <remarks>
    /// These instructions compute a value into their last operand; expressing them as standard functions keeps the
    /// neutral statements free of Logix mnemonics so any converter can implement them from the IEC definition. Logix
    /// spellings that differ from IEC (SQR, ASN, ACS, ATN, XPY, TRN) are translated here; the BCD, string and bit
    /// instructions map onto the catalog extensions of the same name.
    /// </remarks>
    private static readonly Dictionary<string, string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = "ABS", ["SQR"] = "SQRT", ["SIN"] = "SIN", ["COS"] = "COS", ["TAN"] = "TAN", ["ASN"] = "ASIN", ["ACS"] = "ACOS", ["ATN"] = "ATAN",
        ["LN"] = "LN", ["LOG"] = "LOG", ["XPY"] = "EXPT", ["DEG"] = "DEG", ["RAD"] = "RAD", ["TRN"] = "TRUNC", ["TOD"] = "TO_BCD", ["FRD"] = "FROM_BCD",
        ["UPPER"] = "UPPER", ["LOWER"] = "LOWER", ["STOD"] = "STRING_TO_INT", ["STOR"] = "STRING_TO_REAL", ["DTOS"] = "INT_TO_STRING", ["RTOS"] = "REAL_TO_STRING",
        ["CONCAT"] = "CONCAT", ["DELETE"] = "DELETE", ["INSERT"] = "INSERT", ["MID"] = "MID", ["FIND"] = "FIND",
        ["MVM"] = "MASKED_MOVE", ["BTD"] = "BIT_FIELD_DISTRIBUTE", ["SWPB"] = "SWAP_BYTES",
    };

    /// <summary>ST expression function names (Logix spelling) to IEC names.</summary>
    /// <remarks>Structured text accepts both the Logix and the IEC spellings of the math functions; both are normalized to the IEC name.</remarks>
    private static readonly Dictionary<string, string> StFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ABS"] = "ABS", ["SQRT"] = "SQRT", ["SQR"] = "SQRT", ["LN"] = "LN", ["LOG"] = "LOG", ["SIN"] = "SIN", ["COS"] = "COS", ["TAN"] = "TAN",
        ["ASIN"] = "ASIN", ["ACOS"] = "ACOS", ["ATAN"] = "ATAN", ["ASN"] = "ASIN", ["ACS"] = "ACOS", ["ATN"] = "ATAN",
        ["DEG"] = "DEG", ["RAD"] = "RAD", ["TRUNC"] = "TRUNC", ["TRN"] = "TRUNC", ["XPY"] = "EXPT", ["SIZE"] = "SIZE",
    };

    /// <summary>Members cleared by RES per structure type.</summary>
    /// <remarks>
    /// RES resets the accumulated value and the status bits of a timer, counter or control structure (the presets are
    /// kept). Lowering it to member assignments means a converter needs no reset service: the structure simply gets
    /// its members written, exactly as the controller does.
    /// </remarks>
    private static readonly Dictionary<string, string[]> ResetMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TIMER"] = new[] { "ACC", "EN", "TT", "DN" },
        ["COUNTER"] = new[] { "ACC", "CU", "CD", "DN", "OV", "UN" },
        ["CONTROL"] = new[] { "POS", "EN", "EU", "DN", "EM", "ER", "UL", "IN", "FD" },
    };

    /// <summary>The ladder role of a mnemonic: add-ons and every catalog service flagged as always-scan read the rung condition each scan; other services run only when the rung is true.</summary>
    public static LadderRole RoleOf(string mnemonic, bool isAddOn)
    {
        if (isAddOn) return LadderRole.BlockInput;
        if (Roles.TryGetValue(mnemonic, out var role)) return role;
        return LogixServiceCatalog.Instance.RunsEveryScan(mnemonic) ? LadderRole.BlockInput : LadderRole.BlockGate;
    }

    /// <summary>The IEC function a ladder instruction lowers to, or null when it is not a function-style instruction.</summary>
    public static string? FunctionFor(string mnemonic) => Functions.GetValueOrDefault(mnemonic);

    /// <summary>The IEC function behind a structured-text function name, or null when the name is a vendor service.</summary>
    public static string? StFunctionFor(string name) => StFunctions.GetValueOrDefault(name);

    /// <summary>The members RES clears on a structure of the given type, or null when the type is not resettable.</summary>
    public static string[]? ResetMembersOf(string typeName) => ResetMembers.GetValueOrDefault(typeName);

    /// <summary>Instructions lowered to an assignment of the result to their last operand: dest := left op right.</summary>
    /// <remarks>AND/OR/XOR on integers are bitwise in Logix; the typed <c>bin</c> node keeps that distinction through the operand types.</remarks>
    public static string? BinaryOperatorFor(string mnemonic) => mnemonic.ToUpperInvariant() switch
    {
        "ADD" => "add", "SUB" => "sub", "MUL" => "mul", "DIV" => "div", "MOD" => "mod",
        "AND" => "and", "OR" => "or", "XOR" => "xor",
        _ => null,
    };

    /// <summary>The comparison operator of a compare instruction (its two operands become a BOOL contact), or null.</summary>
    public static string? ComparisonFor(string mnemonic) => mnemonic.ToUpperInvariant() switch
    {
        "EQU" => "eq", "NEQ" => "ne", "GRT" => "gt", "GEQ" => "ge", "LES" => "lt", "LEQ" => "le",
        _ => null,
    };
}
