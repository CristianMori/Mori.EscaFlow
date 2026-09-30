namespace L5x.Model;

/// <summary>The Logix atomic data types. BIT is the synthetic type used for bit-overlay UDT members.</summary>
public enum AtomicKind
{
    Bool,
    Bit,
    Sint,
    Int,
    Dint,
    Lint,
    Usint,
    Uint,
    Udint,
    Ulint,
    Real,
    Lreal,
}

public static class AtomicKindExtensions
{
    public static bool TryParseAtomic(string? name, out AtomicKind kind)
    {
        kind = default;
        if (string.IsNullOrEmpty(name)) return false;
        switch (name.ToUpperInvariant())
        {
            case "BOOL": kind = AtomicKind.Bool; return true;
            case "BIT": kind = AtomicKind.Bit; return true;
            case "SINT": kind = AtomicKind.Sint; return true;
            case "INT": kind = AtomicKind.Int; return true;
            case "DINT": kind = AtomicKind.Dint; return true;
            case "LINT": kind = AtomicKind.Lint; return true;
            case "USINT": kind = AtomicKind.Usint; return true;
            case "UINT": kind = AtomicKind.Uint; return true;
            case "UDINT": kind = AtomicKind.Udint; return true;
            case "ULINT": kind = AtomicKind.Ulint; return true;
            case "REAL": kind = AtomicKind.Real; return true;
            case "LREAL": kind = AtomicKind.Lreal; return true;
            default: return false;
        }
    }

    public static string ToLogixName(this AtomicKind kind) => kind switch
    {
        AtomicKind.Bool => "BOOL",
        AtomicKind.Bit => "BIT",
        AtomicKind.Sint => "SINT",
        AtomicKind.Int => "INT",
        AtomicKind.Dint => "DINT",
        AtomicKind.Lint => "LINT",
        AtomicKind.Usint => "USINT",
        AtomicKind.Uint => "UINT",
        AtomicKind.Udint => "UDINT",
        AtomicKind.Ulint => "ULINT",
        AtomicKind.Real => "REAL",
        AtomicKind.Lreal => "LREAL",
        _ => kind.ToString(),
    };

    public static int BitSize(this AtomicKind kind) => kind switch
    {
        AtomicKind.Bool or AtomicKind.Bit => 1,
        AtomicKind.Sint or AtomicKind.Usint => 8,
        AtomicKind.Int or AtomicKind.Uint => 16,
        AtomicKind.Dint or AtomicKind.Udint or AtomicKind.Real => 32,
        AtomicKind.Lint or AtomicKind.Ulint or AtomicKind.Lreal => 64,
        _ => 0,
    };

    public static bool IsFloatingPoint(this AtomicKind kind) => kind is AtomicKind.Real or AtomicKind.Lreal;

    public static bool IsBoolean(this AtomicKind kind) => kind is AtomicKind.Bool or AtomicKind.Bit;
}
