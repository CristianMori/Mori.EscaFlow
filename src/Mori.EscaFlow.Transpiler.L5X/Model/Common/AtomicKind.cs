namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>
/// The Logix atomic data types: the built-in scalars that a controller stores directly rather than as a
/// structure. <see cref="Bit"/> is not a real Logix type; it is synthesized by this parser for a UDT member
/// declared as an overlay of a single bit of a host integer member (see the BIT overlay handling in
/// <c>DataTypeReader</c>), so the rest of the toolchain can treat it like any other atomic kind.
/// </summary>
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
    /// <summary>Parses the Logix spelling of an atomic type (case-insensitive). False for anything else, including structure and module-defined type names.</summary>
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

    /// <summary>Storage width. BOOL and the BIT overlay both report 1: the overlay occupies one bit of its host, never a byte of its own.</summary>
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
