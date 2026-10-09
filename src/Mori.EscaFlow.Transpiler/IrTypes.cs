using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>A named data type of the project. Atomic (elementary) types are never listed here.</summary>
/// <remarks>
/// <para>
/// Only non-elementary types appear in <see cref="IrDocument.Types"/>: structures, unions, enumerations,
/// aliases, subranges, named arrays and string types. The elementary types (BOOL, DINT, REAL, TIME, ...) are a
/// fixed IEC catalog (<see cref="Catalogs.AtomicTypes"/>), so listing them would only add noise and invite
/// inconsistency. Function blocks are not types either: a variable of a function block type names the POU.
/// </para>
/// <para>
/// Members are the neutral view of the type: what logic can address. Layout details that only matter for a
/// byte-exact image (Logix hidden BIT hosts, bit numbers, packing) live in the <c>vendor.logix</c> slot of the
/// type and of its members, so converters that do not need the Rockwell layout see plain BOOL members.
/// </para>
/// </remarks>
public sealed class IrType : IrNode
{
    /// <summary>Type name as declared (case-insensitive lookups).</summary>
    public string Name { get; set; } = "";

    /// <summary>struct: members; union: overlapping members; enum: values; alias: <see cref="Type"/>; subrange: <see cref="Type"/> with bounds; array: <see cref="Type"/> as element with <see cref="Dims"/>; string: <see cref="Length"/>.</summary>
    [IrEnum("struct", "union", "enum", "alias", "subrange", "array", "string")]
    public string Kind { get; set; } = "struct";

    /// <summary>user: declared in the project; vendor: supplied by the controller firmware (TIMER, PID, AXIS_*); io: defined by an I/O device; library: from a referenced library; unresolved: only referenced.</summary>
    /// <remarks>The class tells a converter where the layout came from: user types are exact, vendor types follow the manuals, io types were learned from the export's data, unresolved types have no members at all.</remarks>
    [IrEnum("user", "vendor", "io", "library", "unresolved")]
    public string Class { get; set; } = "user";

    /// <summary>True when the member list is unknown or incomplete.</summary>
    /// <remarks>References into an opaque type are marked opaque too, so a converter can degrade gracefully (skip the statement, report it) instead of failing.</remarks>
    public bool Opaque { get; set; }

    /// <summary>Members in declaration order, for struct and union kinds (and the LEN / DATA members of string kinds, for vendors that address them).</summary>
    public List<IrMember>? Members { get; set; }

    /// <summary>Enumeration values, for <c>enum</c>.</summary>
    public List<IrEnumValue>? Values { get; set; }

    /// <summary>Base type for alias, subrange, array (element) and enum (underlying integer).</summary>
    public IrTypeRef? Type { get; set; }

    /// <summary>Dimensions of a named array type.</summary>
    public List<IrDim>? Dims { get; set; }

    /// <summary>Lower bound of a subrange.</summary>
    public long? Lower { get; set; }

    /// <summary>Upper bound of a subrange.</summary>
    public long? Upper { get; set; }

    /// <summary>Character capacity for <c>string</c> kinds.</summary>
    public int? Length { get; set; }

    /// <summary>True for wide (WSTRING) strings.</summary>
    public bool Wide { get; set; }

    /// <summary>Set when this vendor structure is the instance type of a standard function block (Logix TIMER for TON/TOF/RTO, COUNTER for CTU/CTD).</summary>
    public IrStdFbBinding? StdFb { get; set; }
}

/// <summary>Links a vendor structure to the standard function blocks it backs.</summary>
/// <remarks>
/// The format keeps the vendor's own instance structure (a Logix TIMER with PRE/ACC/EN/TT/DN) instead of
/// replacing it with the IEC block type, because the structure is what the rest of the program and every HMI
/// address. The binding says which standard blocks operate on it and under which pin profile, so a converter
/// knows to implement <c>TON</c> on a TIMER structure rather than on an IEC TON instance.
/// </remarks>
public sealed class IrStdFbBinding
{
    /// <summary>Standard function block names this type backs (TON, TOF, TONR, CTU, CTD, ...).</summary>
    public List<string> Fbs { get; set; } = new();

    /// <summary>Pin profile: how the standard pins map onto this structure (see <see cref="Catalogs.StandardFunctionBlocks"/>).</summary>
    public string Profile { get; set; } = "iec";
}

/// <summary>A member of a structure or union.</summary>
public sealed class IrMember : IrNode
{
    /// <summary>Member name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Member type (arrays inline their dimensions in the type reference).</summary>
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");

    /// <summary>Default value declared on the member, when the vendor supports member defaults.</summary>
    public IrValue? Initial { get; set; }
}

/// <summary>One named value of an enumeration type.</summary>
public sealed class IrEnumValue : IrNode
{
    /// <summary>Value name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Ordinal.</summary>
    public long Value { get; set; }
}

/// <summary>Array bounds, inclusive.</summary>
/// <remarks>IEC arrays may start anywhere (<c>ARRAY[1..10]</c>, <c>ARRAY[-5..5]</c>); Logix arrays always start at 0. Keeping both bounds lets a converter preserve the source's indexing instead of renumbering.</remarks>
public sealed class IrDim
{
    /// <summary>Creates an unset dimension (for deserialization).</summary>
    public IrDim() { }

    /// <summary>Creates the inclusive range <paramref name="lower"/>..<paramref name="upper"/>.</summary>
    public IrDim(long lower, long upper) { Lower = lower; Upper = upper; }

    /// <summary>First valid index.</summary>
    public long Lower { get; set; }

    /// <summary>Last valid index.</summary>
    public long Upper { get; set; }

    /// <summary>Number of elements in this dimension.</summary>
    [JsonIgnore]
    public long Count => Upper - Lower + 1;
}

/// <summary>
/// A reference to a type from a member, variable, expression or temporary. Serialized as a plain string for named
/// types (atomics, structures, POUs used as instance types, standard function blocks) and as an object with
/// <c>kind</c> for arrays, anonymous strings, pointers, references and the untyped <c>any</c>.
/// </summary>
/// <remarks>
/// The string shorthand is what keeps the JSON readable: the overwhelming majority of references are to a
/// named type, and <c>"type": "DINT"</c> is both smaller and clearer than an object. The object form is only
/// used where there is structure to express. Equality is structural and case-insensitive on names, matching
/// PLC identifier rules, so a converter can compare declared and resolved types directly.
/// </remarks>
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

    /// <summary>Element type of an array.</summary>
    public IrTypeRef? Element { get; set; }

    /// <summary>Capacity of an anonymous string type.</summary>
    public int? Length { get; set; }

    /// <summary>True for an anonymous wide string.</summary>
    public bool Wide { get; set; }

    /// <summary>Pointed-to type for pointer / reference.</summary>
    public IrTypeRef? To { get; set; }

    /// <summary>A reference to the named type.</summary>
    public static IrTypeRef Named(string name) => new() { Kind = "named", Name = name };

    /// <summary>The untyped reference, used when the source did not carry a type (a module image without exported data).</summary>
    public static IrTypeRef Any => new() { Kind = "any" };

    /// <summary>An array of <paramref name="element"/> with explicit bounds.</summary>
    public static IrTypeRef Array(IrTypeRef element, params IrDim[] dims) => new() { Kind = "array", Element = element, Dims = dims.ToList() };

    /// <summary>A zero-based array of <paramref name="element"/> with the given element counts per dimension (the Logix convention).</summary>
    public static IrTypeRef Array(IrTypeRef element, IReadOnlyList<int> counts) => new() { Kind = "array", Element = element, Dims = counts.Select(c => new IrDim(0, c - 1)).ToList() };

    /// <summary>An anonymous string type of the given capacity.</summary>
    public static IrTypeRef String(int length, bool wide = false) => new() { Kind = "string", Length = length, Wide = wide };

    /// <summary>True for a named type.</summary>
    [JsonIgnore] public bool IsNamed => Kind == "named";

    /// <summary>True for an array.</summary>
    [JsonIgnore] public bool IsArray => Kind == "array";

    /// <summary>True for the untyped reference.</summary>
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

    /// <summary>Structural equality: same kind, same name (case-insensitive) or same element / bounds / capacity / target.</summary>
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

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as IrTypeRef);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Name?.ToUpperInvariant(), Length, Element, To);

    /// <summary>IEC spelling of the reference (<c>ARRAY[0..9] OF DINT</c>, <c>STRING(20)</c>, <c>POINTER TO X</c>).</summary>
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
/// <remarks>A hand-written converter rather than the default object serialization so the string shorthand round-trips exactly and so unknown members in the object form are skipped (forward compatibility with later minor versions).</remarks>
public sealed class IrTypeRefConverter : JsonConverter<IrTypeRef>
{
    /// <inheritdoc />
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
                default: reader.Skip(); break; // forward compatibility: members added by later versions are ignored
            }
        }
        throw new JsonException("unterminated type reference");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IrTypeRef value, JsonSerializerOptions options)
    {
        if (value.IsNamed)
        {
            writer.WriteStringValue(value.Name ?? "");
            return;
        }
        // Object form: only the members that apply to the kind are written, keeping the JSON minimal and deterministic.
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
