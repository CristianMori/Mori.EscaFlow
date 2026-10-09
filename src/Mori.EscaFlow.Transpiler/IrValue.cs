using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>Initial value of a variable, member or element. <see cref="Type"/> is the type name (atomic or structure); arrays name the element type.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrScalarValue), "scalar")]
[JsonDerivedType(typeof(IrStringValue), "string")]
[JsonDerivedType(typeof(IrStructValue), "struct")]
[JsonDerivedType(typeof(IrArrayValue), "array")]
public abstract class IrValue
{
    public string Type { get; set; } = "";
}

public sealed class IrScalarValue : IrValue
{
    /// <summary>Integer kinds including BOOL (0/1), bit strings, and enum ordinals.</summary>
    public long? Int { get; set; }

    /// <summary>REAL / LREAL.</summary>
    public double? Real { get; set; }

    /// <summary>TIME / DATE / TOD / DT as an IEC literal (<c>T#1s500ms</c>, <c>D#2026-09-29</c>), or text kept when the source value could not be parsed (NaN spellings).</summary>
    public string? Raw { get; set; }
}

public sealed class IrStringValue : IrValue
{
    public int Length { get; set; }
    public string Text { get; set; } = "";
}

public sealed class IrStructValue : IrValue
{
    /// <summary>Members in declaration order.</summary>
    public List<IrNamedValue> Members { get; set; } = new();
}

public sealed class IrNamedValue
{
    public string Name { get; set; } = "";
    public IrValue Value { get; set; } = new IrScalarValue();
}

public sealed class IrArrayValue : IrValue
{
    /// <summary>Element count per dimension, outermost first.</summary>
    public int[] Dims { get; set; } = Array.Empty<int>();

    /// <summary>Row-major.</summary>
    public List<IrValue> Elements { get; set; } = new();
}
