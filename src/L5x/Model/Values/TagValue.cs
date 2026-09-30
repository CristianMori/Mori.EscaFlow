using System.Text.Json.Serialization;

namespace L5x.Model.Values;

/// <summary>
/// The typed value tree of a tag, member or array element. Produced directly from Decorated data
/// or by shaping an L5K initializer against the tag's data type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScalarValue), "scalar")]
[JsonDerivedType(typeof(StringValue), "string")]
[JsonDerivedType(typeof(StructValue), "struct")]
[JsonDerivedType(typeof(ArrayValue), "array")]
public abstract record TagValue
{
    public abstract string DataType { get; }
}

/// <summary>
/// An atomic value. Integer kinds (including BOOL) live in <see cref="Bits"/>; REAL/LREAL live in <see cref="Real"/>.
/// <see cref="RawText"/> is only set when the source text could not be parsed as a number (e.g. DT# timestamps).
/// </summary>
public sealed record ScalarValue(AtomicKind Kind, long Bits, double Real, Radix Radix) : TagValue
{
    public string? ForceValue { get; init; }
    public string? RawText { get; init; }

    public override string DataType => Kind.ToLogixName();

    public bool AsBool => Kind.IsFloatingPoint() ? Real != 0 : Bits != 0;

    public double AsDouble => Kind.IsFloatingPoint() ? Real : Bits;

    public static ScalarValue FromInteger(AtomicKind kind, long value, Radix radix = Radix.Decimal) => new(kind, value, 0, radix);

    public static ScalarValue FromReal(AtomicKind kind, double value, Radix radix = Radix.Float) => new(kind, 0, value, radix);

    public override string ToString() => RawText ?? (Kind.IsFloatingPoint()
        ? Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        : Bits.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>A STRING-family value: the LEN member and the DATA characters collapsed into text.</summary>
public sealed record StringValue(int Length, string Text, string DataType) : TagValue
{
    public override string DataType { get; } = DataType;
    public override string ToString() => Text;
}

/// <summary>A structure value; members are kept in declaration order.</summary>
public sealed record StructValue(string DataType, IReadOnlyList<KeyValuePair<string, TagValue>> Members) : TagValue
{
    public override string DataType { get; } = DataType;

    public TagValue? this[string memberName]
    {
        get
        {
            foreach (var kv in Members)
                if (string.Equals(kv.Key, memberName, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
    }
}

/// <summary>An array value; Elements are in row-major order over <see cref="Dimensions"/>.</summary>
public sealed record ArrayValue(string ElementType, int[] Dimensions, Radix Radix, IReadOnlyList<TagValue> Elements) : TagValue
{
    public override string DataType => ElementType + "[" + string.Join(",", Dimensions) + "]";

    public int Rank => Dimensions.Length;

    public int TotalLength
    {
        get
        {
            var n = 1;
            foreach (var d in Dimensions) n *= d;
            return n;
        }
    }

    public TagValue? At(params int[] indices)
    {
        if (indices.Length != Dimensions.Length) return null;
        var flat = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0 || indices[i] >= Dimensions[i]) return null;
            flat = flat * Dimensions[i] + indices[i];
        }
        return flat < Elements.Count ? Elements[flat] : null;
    }
}
