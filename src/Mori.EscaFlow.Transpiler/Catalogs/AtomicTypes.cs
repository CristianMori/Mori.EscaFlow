namespace Mori.EscaFlow.Transpiler.Catalogs;

/// <summary>Family of an elementary type, which decides the operations the validator allows on it and how converters store it.</summary>
public enum AtomicCategory
{
    /// <summary>BOOL.</summary>
    Bool,

    /// <summary>SINT, INT, DINT, LINT.</summary>
    SignedInt,

    /// <summary>USINT, UINT, UDINT, ULINT.</summary>
    UnsignedInt,

    /// <summary>REAL, LREAL.</summary>
    Real,

    /// <summary>BYTE, WORD, DWORD, LWORD: integers meant for bit manipulation, not arithmetic.</summary>
    BitString,

    /// <summary>TIME, LTIME: durations.</summary>
    Time,

    /// <summary>DATE, LDATE, TOD, LTOD, DT, LDT: calendar values.</summary>
    Date,

    /// <summary>CHAR, WCHAR.</summary>
    Char,
}

/// <summary>One IEC 61131-3 elementary type.</summary>
/// <param name="Name">Canonical IEC name.</param>
/// <param name="Category">Family.</param>
/// <param name="Bits">Width in bits (1 for BOOL, which vendors pack differently).</param>
public sealed record AtomicType(string Name, AtomicCategory Category, int Bits)
{
    /// <summary>True for the signed and unsigned integer families.</summary>
    public bool IsInteger => Category is AtomicCategory.SignedInt or AtomicCategory.UnsignedInt;

    /// <summary>True for integers and reals (arithmetic operands).</summary>
    public bool IsNumeric => IsInteger || Category == AtomicCategory.Real;

    /// <summary>True for BOOL and the bit-string family (logical operands).</summary>
    public bool IsBitString => Category is AtomicCategory.BitString or AtomicCategory.Bool;

    /// <summary>True when bit access (<c>bit</c> expressions) is meaningful: integers and bit strings.</summary>
    public bool SupportsBits => IsInteger || Category == AtomicCategory.BitString;
}

/// <summary>The IEC 61131-3 elementary types. Names are case-insensitive.</summary>
/// <remarks>
/// The catalog is the full IEC set, not the Logix subset, so a Siemens or Codesys front-end can use TIME, DATE,
/// BYTE and WORD natively. Logix only has BOOL, the signed and unsigned integers, REAL and LREAL; a converter
/// for a Logix-style runtime maps the others onto the integer of the same width.
/// </remarks>
public static class AtomicTypes
{
    /// <summary>Every elementary type, in IEC order.</summary>
    public static readonly IReadOnlyList<AtomicType> All = new[]
    {
        new AtomicType("BOOL", AtomicCategory.Bool, 1),
        new AtomicType("SINT", AtomicCategory.SignedInt, 8),
        new AtomicType("INT", AtomicCategory.SignedInt, 16),
        new AtomicType("DINT", AtomicCategory.SignedInt, 32),
        new AtomicType("LINT", AtomicCategory.SignedInt, 64),
        new AtomicType("USINT", AtomicCategory.UnsignedInt, 8),
        new AtomicType("UINT", AtomicCategory.UnsignedInt, 16),
        new AtomicType("UDINT", AtomicCategory.UnsignedInt, 32),
        new AtomicType("ULINT", AtomicCategory.UnsignedInt, 64),
        new AtomicType("REAL", AtomicCategory.Real, 32),
        new AtomicType("LREAL", AtomicCategory.Real, 64),
        new AtomicType("BYTE", AtomicCategory.BitString, 8),
        new AtomicType("WORD", AtomicCategory.BitString, 16),
        new AtomicType("DWORD", AtomicCategory.BitString, 32),
        new AtomicType("LWORD", AtomicCategory.BitString, 64),
        new AtomicType("CHAR", AtomicCategory.Char, 8),
        new AtomicType("WCHAR", AtomicCategory.Char, 16),
        new AtomicType("TIME", AtomicCategory.Time, 32),
        new AtomicType("LTIME", AtomicCategory.Time, 64),
        new AtomicType("DATE", AtomicCategory.Date, 32),
        new AtomicType("LDATE", AtomicCategory.Date, 64),
        new AtomicType("TOD", AtomicCategory.Date, 32),
        new AtomicType("LTOD", AtomicCategory.Date, 64),
        new AtomicType("DT", AtomicCategory.Date, 32),
        new AtomicType("LDT", AtomicCategory.Date, 64),
    };

    private static readonly Dictionary<string, AtomicType> ByName = All.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Alternative spellings accepted on input.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TIME_OF_DAY"] = "TOD",
        ["DATE_AND_TIME"] = "DT",
        ["LTIME_OF_DAY"] = "LTOD",
        ["LDATE_AND_TIME"] = "LDT",
    };

    /// <summary>The elementary type named <paramref name="name"/> (canonical name or long-form alias), or null.</summary>
    public static AtomicType? Get(string name)
    {
        if (ByName.TryGetValue(name, out var t)) return t;
        return Aliases.TryGetValue(name, out var canonical) ? ByName[canonical] : null;
    }

    /// <summary>True when <paramref name="name"/> is an elementary type.</summary>
    public static bool IsAtomic(string name) => Get(name) is not null;

    /// <summary>True when the reference names an elementary type (arrays of atomics are not atomic).</summary>
    public static bool IsAtomic(IrTypeRef? type) => type is { IsNamed: true, Name: { } n } && IsAtomic(n);

    /// <summary>Numeric promotion order used for expression typing: the wider of two numeric types.</summary>
    /// <remarks>Follows the Logix rule that mixed arithmetic is computed in the wider operand type (any REAL operand makes the result REAL); BOOL sits at the bottom so BOOL arithmetic promotes to the other operand.</remarks>
    private static readonly string[] Rank = { "BOOL", "SINT", "USINT", "INT", "UINT", "DINT", "UDINT", "LINT", "ULINT", "REAL", "LREAL" };

    /// <summary>The wider of two numeric type names (null-tolerant: an unknown side yields the other).</summary>
    public static string? Wider(string? a, string? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        var ia = Array.FindIndex(Rank, r => r.Equals(a, StringComparison.OrdinalIgnoreCase));
        var ib = Array.FindIndex(Rank, r => r.Equals(b, StringComparison.OrdinalIgnoreCase));
        if (ia < 0) return b;
        if (ib < 0) return a;
        return Rank[Math.Max(ia, ib)];
    }

    /// <summary>The smallest signed integer type that holds <paramref name="value"/> (DINT for anything within 32 bits, else LINT).</summary>
    /// <remarks>Integer literals are typed this way because Logix evaluates untyped literals as DINT; anything wider needs LINT arithmetic.</remarks>
    public static string IntegerTypeFor(long value) => value is >= int.MinValue and <= int.MaxValue ? "DINT" : "LINT";
}
