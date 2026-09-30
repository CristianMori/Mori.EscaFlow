namespace Plc.Ir.Catalogs;

public enum AtomicCategory
{
    Bool,
    SignedInt,
    UnsignedInt,
    Real,
    BitString,
    Time,
    Date,
    Char,
}

public sealed record AtomicType(string Name, AtomicCategory Category, int Bits)
{
    public bool IsInteger => Category is AtomicCategory.SignedInt or AtomicCategory.UnsignedInt;
    public bool IsNumeric => IsInteger || Category == AtomicCategory.Real;
    public bool IsBitString => Category is AtomicCategory.BitString or AtomicCategory.Bool;

    /// <summary>True when bit access (<c>bit</c> expressions) is meaningful: integers and bit strings.</summary>
    public bool SupportsBits => IsInteger || Category == AtomicCategory.BitString;
}

/// <summary>The IEC 61131-3 elementary types. Names are case-insensitive.</summary>
public static class AtomicTypes
{
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

    public static AtomicType? Get(string name)
    {
        if (ByName.TryGetValue(name, out var t)) return t;
        return Aliases.TryGetValue(name, out var canonical) ? ByName[canonical] : null;
    }

    public static bool IsAtomic(string name) => Get(name) is not null;

    public static bool IsAtomic(IrTypeRef? type) => type is { IsNamed: true, Name: { } n } && IsAtomic(n);

    /// <summary>Numeric promotion order used for expression typing: the wider of two numeric types.</summary>
    private static readonly string[] Rank = { "BOOL", "SINT", "USINT", "INT", "UINT", "DINT", "UDINT", "LINT", "ULINT", "REAL", "LREAL" };

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
    public static string IntegerTypeFor(long value) => value is >= int.MinValue and <= int.MaxValue ? "DINT" : "LINT";
}
