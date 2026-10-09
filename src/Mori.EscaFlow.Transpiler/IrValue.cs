using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>Initial value of a variable, member or element. <see cref="Type"/> is the type name (atomic or structure); arrays name the element type.</summary>
/// <remarks>
/// Values are kept as a typed tree rather than as a flat byte image so they survive a change of layout between
/// vendors: a converter that packs structures differently still knows which member holds which value. Values are
/// the bulk of a large document (initial values of every tag), so the shape is kept lean: type names are plain
/// strings and scalars carry a single number.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrScalarValue), "scalar")]
[JsonDerivedType(typeof(IrStringValue), "string")]
[JsonDerivedType(typeof(IrStructValue), "struct")]
[JsonDerivedType(typeof(IrArrayValue), "array")]
public abstract class IrValue
{
    /// <summary>Atomic or structure type name; for arrays, the element type name.</summary>
    public string Type { get; set; } = "";
}

/// <summary>An atomic value.</summary>
/// <remarks>Integers of every width, BOOL and bit strings share <see cref="Int"/> (a 64-bit integer holds them all); reals use <see cref="Real"/>; time and date kinds and unparsable text use <see cref="Raw"/>.</remarks>
public sealed class IrScalarValue : IrValue
{
    /// <summary>Integer kinds including BOOL (0/1), bit strings, and enum ordinals.</summary>
    public long? Int { get; set; }

    /// <summary>REAL / LREAL.</summary>
    public double? Real { get; set; }

    /// <summary>TIME / DATE / TOD / DT as an IEC literal (<c>T#1s500ms</c>, <c>D#2026-09-29</c>), or text kept when the source value could not be parsed (NaN spellings).</summary>
    public string? Raw { get; set; }
}

/// <summary>A string-family value: current length and text, independent of how the vendor stores the characters.</summary>
public sealed class IrStringValue : IrValue
{
    /// <summary>Current length (Logix LEN), which may be shorter than the text's capacity.</summary>
    public int Length { get; set; }

    /// <summary>The characters.</summary>
    public string Text { get; set; } = "";
}

/// <summary>A structure value.</summary>
public sealed class IrStructValue : IrValue
{
    /// <summary>Members in declaration order.</summary>
    public List<IrNamedValue> Members { get; set; } = new();
}

/// <summary>One member of a structure value.</summary>
public sealed class IrNamedValue
{
    /// <summary>Member name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The member's value.</summary>
    public IrValue Value { get; set; } = new IrScalarValue();
}

/// <summary>An array value.</summary>
public sealed class IrArrayValue : IrValue
{
    /// <summary>Element count per dimension, outermost first.</summary>
    public int[] Dims { get; set; } = Array.Empty<int>();

    /// <summary>Row-major.</summary>
    public List<IrValue> Elements { get; set; } = new();
}
