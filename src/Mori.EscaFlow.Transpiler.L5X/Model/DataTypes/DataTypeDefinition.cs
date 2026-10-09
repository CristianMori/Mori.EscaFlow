using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;

public enum DataTypeFamily
{
    NoFamily,
    StringFamily,
}

/// <summary>Where a data type comes from, which decides whether its layout is known up front or only learned from the export.</summary>
public enum DataTypeClass
{
    /// <summary>BOOL, SINT, DINT, REAL, ...</summary>
    Atomic,
    /// <summary>TIMER, COUNTER, STRING, MESSAGE, ... shipped with the controller.</summary>
    Predefined,
    /// <summary>A user-defined type from the DataTypes section.</summary>
    User,
    /// <summary>The backing structure of an add-on instruction.</summary>
    AddOn,
    /// <summary>
    /// An I/O module type such as <c>AB:1756_DI:I:0</c>, whose shape is only visible through Decorated data.
    /// Module-defined types are never pre-typed in this toolchain: the catalog of every module a project might
    /// use is far larger than Logix itself ships a schema for, and the export always carries the actual member
    /// layout inline wherever a tag of the type has a value, so there is nothing to gain and much to get wrong
    /// by guessing the shape ahead of time.
    /// </summary>
    ModuleDefined,
    /// <summary>A name that was referenced but never defined.</summary>
    Unresolved,
}

/// <summary>A named data type: either an atomic scalar or a structure.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AtomicType), "atomic")]
[JsonDerivedType(typeof(StructuredType), "struct")]
public abstract class DataTypeDefinition : L5xElement
{
    public required string Name { get; init; }
    public DataTypeClass Class { get; init; }
    public string? Description { get; set; }

    [JsonIgnore]
    public bool IsAtomic => this is AtomicType;

    public override string ToString() => Name;
}

public sealed class AtomicType : DataTypeDefinition
{
    public required AtomicKind Kind { get; init; }
    public int BitSize => Kind.BitSize();
}

/// <summary>A structure: user UDT, predefined structure, add-on backing type or module-defined type.</summary>
public sealed class StructuredType : DataTypeDefinition
{
    public DataTypeFamily Family { get; set; }

    /// <summary>Members in declaration order, including hidden bit-host members.</summary>
    public List<DataTypeMember> Members { get; init; } = new();

    /// <summary>True when the member list is not known (e.g. AXIS_* and other object-backed types); see <see cref="BuiltInTypes"/>.</summary>
    public bool IsOpaque { get; set; }

    /// <summary>
    /// For module-defined I/O types (<c>AB:1756_DI:I:0</c>, <c>_0015:935700_001_B2B4CBB0:C:0</c>): the parsed name.
    /// Null for every other class of type. These types are never predefined; their layout comes from the export.
    /// </summary>
    public ModuleTypeInfo? ModuleType { get; set; }

    [JsonIgnore]
    public bool IsModuleDefined => Class == DataTypeClass.ModuleDefined;

    /// <summary>Extra attributes that only the spec mentions (EngineeringUnit, CustomProperties, ...).</summary>
    public string? EngineeringUnit { get; set; }

    public DataTypeMember? FindMember(string name)
    {
        foreach (var m in Members)
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    /// <summary>Members that are not hidden bit hosts: what the designer and a consumer of this model actually see.</summary>
    [JsonIgnore]
    public IEnumerable<DataTypeMember> VisibleMembers => Members.Where(m => !m.Hidden);

    [JsonIgnore]
    public bool IsStringFamily => Family == DataTypeFamily.StringFamily;
}

/// <summary>One member of a structured type.</summary>
public sealed class DataTypeMember : L5xElement
{
    public required string Name { get; init; }
    public required DataTypeRef DataType { get; init; }

    /// <summary>0 for a scalar member; otherwise the single array dimension (UDT members are at most 1-D).</summary>
    public int Dimension { get; init; }

    public Radix Radix { get; init; }

    /// <summary>True for a bit-host member synthesized to back one or more <see cref="IsBitOverlay"/> members; Decorated data never shows it directly.</summary>
    public bool Hidden { get; init; }
    public string? ExternalAccess { get; init; }

    /// <summary>For BIT members: the name of the hidden host member that stores the bit.</summary>
    public string? Target { get; init; }

    public int? BitNumber { get; init; }
    public string? Description { get; set; }
    public string? EngineeringUnit { get; set; }
    public string? State0 { get; set; }
    public string? State1 { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }

    /// <summary>Resolved <see cref="Target"/> (set by the linker).</summary>
    [JsonIgnore]
    public DataTypeMember? BitHost { get; set; }

    [JsonIgnore]
    public bool IsBitOverlay => Target is not null;

    [JsonIgnore]
    public bool IsArray => Dimension > 0;

    public override string ToString() => Dimension > 0 ? $"{Name} : {DataType.Name}[{Dimension}]" : $"{Name} : {DataType.Name}";
}

/// <summary>The addressing role a module member image plays: an input, an output, its configuration, or a safety variant of either.</summary>
public enum IoDataKind
{
    Unknown,
    Input,
    Output,
    Config,
    SafetyInput,
    SafetyOutput,
}

/// <summary>
/// The parts of a module-defined type name: <c>&lt;vendor&gt;:&lt;product&gt;:&lt;kind&gt;:&lt;index&gt;</c>.
/// Vendor is "AB" for Rockwell modules or "_hhhh" (hex CIP vendor id) for third-party modules.
/// </summary>
public sealed record ModuleTypeInfo(string Vendor, string Product, string KindCode, int Index)
{
    public IoDataKind Kind => KindCode.ToUpperInvariant() switch
    {
        "I" => IoDataKind.Input,
        "O" => IoDataKind.Output,
        "C" => IoDataKind.Config,
        "SI" => IoDataKind.SafetyInput,
        "SO" => IoDataKind.SafetyOutput,
        _ => IoDataKind.Unknown,
    };

    /// <summary>True for third-party modules identified by a hex vendor id ("_0015") rather than the "AB" literal.</summary>
    public bool IsThirdParty => Vendor.StartsWith('_');

    /// <summary>Parses a module-defined type name; returns null when the text is not of that form.</summary>
    public static ModuleTypeInfo? TryParse(string name)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOf(':') < 0) return null;
        var parts = name.Split(':');
        if (parts.Length < 3) return null;
        var index = int.TryParse(parts[^1], out var i) ? i : 0;
        var hasIndex = parts.Length >= 4 && int.TryParse(parts[^1], out _);
        var kind = hasIndex ? parts[^2] : parts[^1];
        var productParts = hasIndex ? parts[1..^2] : parts[1..^1];
        return new ModuleTypeInfo(parts[0], string.Join(':', productParts), kind, hasIndex ? index : 0);
    }
}

/// <summary>A reference to a data type by name; <see cref="Definition"/> is filled by the linker once every type is known.</summary>
public sealed class DataTypeRef
{
    public required string Name { get; init; }

    [JsonIgnore]
    public DataTypeDefinition? Definition { get; set; }

    [JsonIgnore]
    public bool IsResolved => Definition is not null;

    [JsonIgnore]
    public bool IsAtomic => Definition is AtomicType || AtomicKindExtensions.TryParseAtomic(Name, out _);

    public static DataTypeRef Of(string name) => new() { Name = name };

    public override string ToString() => Name;
}
