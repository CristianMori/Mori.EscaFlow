using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>A named data type of the project. Atomic (elementary) types are never listed here.</summary>
public sealed class IrType : IrNode
{
    public string Name { get; set; } = "";

    /// <summary>struct: members; union: overlapping members; enum: values; alias: <see cref="Type"/>; subrange: <see cref="Type"/> with bounds; array: <see cref="Type"/> as element with <see cref="Dims"/>; string: <see cref="Length"/>.</summary>
    [IrEnum("struct", "union", "enum", "alias", "subrange", "array", "string")]
    public string Kind { get; set; } = "struct";

    /// <summary>user: declared in the project; vendor: supplied by the controller firmware (TIMER, PID, AXIS_*); io: defined by an I/O device; library: from a referenced library; unresolved: only referenced.</summary>
    [IrEnum("user", "vendor", "io", "library", "unresolved")]
    public string Class { get; set; } = "user";

    /// <summary>True when the member list is unknown or incomplete.</summary>
    public bool Opaque { get; set; }

    public List<IrMember>? Members { get; set; }

    /// <summary>Enumeration values, for <c>enum</c>.</summary>
    public List<IrEnumValue>? Values { get; set; }

    /// <summary>Base type for alias, subrange, array (element) and enum (underlying integer).</summary>
    public IrTypeRef? Type { get; set; }
    public List<IrDim>? Dims { get; set; }
    public long? Lower { get; set; }
    public long? Upper { get; set; }

    /// <summary>Character capacity for <c>string</c> kinds.</summary>
    public int? Length { get; set; }

    /// <summary>True for wide (WSTRING) strings.</summary>
    public bool Wide { get; set; }

    /// <summary>Set when this vendor structure is the instance type of a standard function block (Logix TIMER for TON/TOF/RTO, COUNTER for CTU/CTD).</summary>
    public IrStdFbBinding? StdFb { get; set; }
}

public sealed class IrStdFbBinding
{
    /// <summary>Standard function block names this type backs (TON, TOF, TONR, CTU, CTD, ...).</summary>
    public List<string> Fbs { get; set; } = new();

    /// <summary>Pin profile: how the standard pins map onto this structure (see <see cref="Catalogs.StandardFunctionBlocks"/>).</summary>
    public string Profile { get; set; } = "iec";
}

public sealed class IrMember : IrNode
{
    public string Name { get; set; } = "";
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");
    public IrValue? Initial { get; set; }
}

public sealed class IrEnumValue : IrNode
{
    public string Name { get; set; } = "";
    public long Value { get; set; }
}

/// <summary>Array bounds, inclusive.</summary>
public sealed class IrDim
{
    public IrDim() { }
    public IrDim(long lower, long upper) { Lower = lower; Upper = upper; }
    public long Lower { get; set; }
    public long Upper { get; set; }

    [JsonIgnore]
    public long Count => Upper - Lower + 1;
}

/// <summary>
/// A reference to a type from a member, variable, expression or temporary. Serialized as a plain string for named
/// types (atomics, structures, POUs used as instance types, standard function blocks) and as an object with
/// <c>kind</c> for arrays, anonymous strings, pointers, references and the untyped <c>any</c>.
/// </summary>
[JsonConverter(typeof(IrTypeRefConverter))]
public sealed class IrTypeRef : IEquatable<IrTypeRef>
{
    /// <summary>named | array | string | pointer | reference | any</summary>
    [IrEnum("named", "array", "string", "pointer", "reference", "any")]
    public string Kind { get; set; } = "named";

    /// <summary>The type name for <c>named</c>.</summary>
    public string? Name { get; set; }

    /// <summary>Array dimensions, outermost first (row-major).</summary>
    public List<IrDim>? Dims { get; set; }
    public IrTypeRef? Element { get; set; }
    public int? Length { get; set; }
    public bool Wide { get; set; }

    /// <summary>Pointed-to type for pointer / reference.</summary>
    public IrTypeRef? To { get; set; }

    public static IrTypeRef Named(string name) => new() { Kind = "named", Name = name };
    public static IrTypeRef Any => new() { Kind = "any" };
    public static IrTypeRef Array(IrTypeRef element, params IrDim[] dims) => new() { Kind = "array", Element = element, Dims = dims.ToList() };
    public static IrTypeRef Array(IrTypeRef element, IReadOnlyList<int> counts) => new() { Kind = "array", Element = element, Dims = counts.Select(c => new IrDim(0, c - 1)).ToList() };
    public static IrTypeRef String(int length, bool wide = false) => new() { Kind = "string", Length = length, Wide = wide };

    [JsonIgnore] public bool IsNamed => Kind == "named";
    [JsonIgnore] public bool IsArray => Kind == "array";
    [JsonIgnore] public bool IsAny => Kind == "any";

    /// <summary>The innermost named type (through arrays, pointers and references), or null.</summary>
    [JsonIgnore]
    public string? LeafName => Kind switch
    {
        "named" => Name,
        "array" => Element?.LeafName,
        "pointer" or "reference" => To?.LeafName,
        _ => null,
    };

    /// <summary>True when the reference names <paramref name="name"/> (case-insensitive).</summary>
    public bool Is(string name) => IsNamed && string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

    public bool Equals(IrTypeRef? other)
    {
        if (other is null) return false;
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            "named" => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase),
            "array" => Equals(Element, other.Element) && DimsEqual(Dims, other.Dims),
            "string" => Length == other.Length && Wide == other.Wide,
            "pointer" or "reference" => Equals(To, other.To),
            _ => true,
        };
    }

    private static bool DimsEqual(List<IrDim>? a, List<IrDim>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Lower != b[i].Lower || a[i].Upper != b[i].Upper) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as IrTypeRef);
    public override int GetHashCode() => HashCode.Combine(Kind, Name?.ToUpperInvariant(), Length, Element, To);

    public override string ToString() => Kind switch
    {
        "named" => Name ?? "?",
        "array" => $"ARRAY[{string.Join(",", (Dims ?? new()).Select(d => $"{d.Lower}..{d.Upper}"))}] OF {Element}",
        "string" => (Wide ? "WSTRING" : "STRING") + (Length is { } l ? $"({l})" : ""),
        "pointer" => $"POINTER TO {To}",
        "reference" => $"REFERENCE TO {To}",
        _ => "ANY",
    };
}

/// <summary>Writes named type references as a JSON string and every other kind as an object.</summary>
public sealed class IrTypeRefConverter : JsonConverter<IrTypeRef>
{
    public override IrTypeRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String) return IrTypeRef.Named(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("type reference must be a string or an object");
        var result = new IrTypeRef();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var prop = reader.GetString()!;
            reader.Read();
            switch (prop)
            {
                case "kind": result.Kind = reader.GetString() ?? "named"; break;
                case "name": result.Name = reader.GetString(); break;
                case "dims": result.Dims = JsonSerializer.Deserialize<List<IrDim>>(ref reader, options); break;
                case "element": result.Element = Read(ref reader, typeToConvert, options); break;
                case "length": result.Length = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32(); break;
                case "wide": result.Wide = reader.GetBoolean(); break;
                case "to": result.To = Read(ref reader, typeToConvert, options); break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException("unterminated type reference");
    }

    public override void Write(Utf8JsonWriter writer, IrTypeRef value, JsonSerializerOptions options)
    {
        if (value.IsNamed)
        {
            writer.WriteStringValue(value.Name ?? "");
            return;
        }
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        if (value.Name is not null) writer.WriteString("name", value.Name);
        if (value.Dims is not null)
        {
            writer.WritePropertyName("dims");
            JsonSerializer.Serialize(writer, value.Dims, options);
        }
        if (value.Element is not null)
        {
            writer.WritePropertyName("element");
            Write(writer, value.Element, options);
        }
        if (value.Length is { } length) writer.WriteNumber("length", length);
        if (value.Wide) writer.WriteBoolean("wide", true);
        if (value.To is not null)
        {
            writer.WritePropertyName("to");
            Write(writer, value.To, options);
        }
        writer.WriteEndObject();
    }
}
