using System.Collections;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Tags;

/// <summary>Base (ordinary storage), Alias (another operand under a second name, no storage of its own), Produced/Consumed (controller-to-controller tag exchange over a CIP connection).</summary>
public enum TagType
{
    Base,
    Alias,
    Produced,
    Consumed,
}

/// <summary>Where a tag lives, which also decides its visibility and lifetime.</summary>
public enum TagScope
{
    Controller,
    Program,
    AoiParameter,
    AoiLocal,
    /// <summary>Module config / input / output data that behaves like a tag.</summary>
    Module,
}

/// <summary>
/// A tag declaration with its documentation and value. Add-on parameters (<see cref="AoiParameter"/>) and local
/// tags derive from this same class, since Logix lets a parameter be addressed and commented exactly like any
/// other tag; only <see cref="AoiParameter"/> adds the parameter-specific flags.
/// <para>
/// Scope matters for name resolution: a program's own tag of the same name as a controller tag shadows it for
/// every routine of that program (and for an add-on instance's parameters relative to the program that placed
/// it), which is why logic in different programs can reference "the same" short tag name and mean different
/// storage. This class only records where a tag was declared; <c>ScopeResolver</c> applies the shadowing rule.
/// </para>
/// </summary>
public class Tag : L5xElement
{
    public required string Name { get; init; }
    public TagType TagType { get; init; } = TagType.Base;

    /// <summary>Null for alias tags: an alias has no type of its own, it takes on the type of whatever it points to.</summary>
    public DataTypeRef? DataType { get; init; }

    /// <summary>Empty for scalars; otherwise the dimensions in declaration order ("5 10 2" -> [5,10,2]).</summary>
    public int[] Dimensions { get; init; } = Array.Empty<int>();

    public Radix Radix { get; init; }
    public bool Constant { get; init; }
    public string? ExternalAccess { get; init; }
    public string? OpcUaAccess { get; init; }

    /// <summary>For alias tags: the aliased operand text, exactly as exported (e.g. <c>Local:2:I.Data.3</c>).</summary>
    public string? AliasFor { get; init; }

    /// <summary>Input / Output / InOut / Public for program parameters; null for ordinary tags.</summary>
    public string? Usage { get; init; }
    public string? Class { get; init; }
    public string? Sequencing { get; init; }
    public string? Description { get; set; }

    public TagScope Scope { get; init; }

    /// <summary>Owning program for program-scoped tags.</summary>
    [JsonIgnore]
    public Program? Program { get; set; }

    /// <summary>Owning add-on definition for parameters and local tags.</summary>
    [JsonIgnore]
    public AddOnInstructionDefinition? Aoi { get; set; }

    /// <summary>Per-member or per-element comments (<c>Comment Operand=".Member"</c>).</summary>
    public List<TagComment> Comments { get; init; } = new();

    public Dictionary<string, string>? EngineeringUnits { get; set; }
    public Dictionary<string, string>? Mins { get; set; }
    public Dictionary<string, string>? Maxes { get; set; }
    public Dictionary<string, string>? State0s { get; set; }
    public Dictionary<string, string>? State1s { get; set; }

    /// <summary>Settings for a Consumed tag: the producing controller, remote tag name and connection parameters; kept verbatim.</summary>
    public GenericElement? ConsumeInfo { get; set; }

    /// <summary>Settings for a Produced tag: how many consumers and the connection parameters; kept verbatim.</summary>
    public GenericElement? ProduceInfo { get; set; }
    public GenericElement? ForceData { get; set; }
    public GenericElement? AlarmConditions { get; set; }

    public TagData Data { get; set; } = TagData.Empty;

    /// <summary>Resolved alias target (set by the linker).</summary>
    [JsonIgnore]
    public Tag? AliasTarget { get; set; }

    /// <summary>Parsed <see cref="AliasFor"/> (set by the linker).</summary>
    public OperandPath? AliasPath { get; set; }

    [JsonIgnore]
    public bool IsArray => Dimensions.Length > 0;

    [JsonIgnore]
    public bool IsAlias => TagType == TagType.Alias;

    public override string ToString() => DataType is null ? $"{Name} (alias {AliasFor})" : IsArray ? $"{Name} : {DataType.Name}[{string.Join(",", Dimensions)}]" : $"{Name} : {DataType.Name}";
}

/// <summary>A comment attached to part of a tag. Operand is the raw specifier (".Member", "[4]", "[7].Input.0") as exported; <see cref="Path"/> is its parsed form, filled by the logic phase.</summary>
public sealed class TagComment
{
    public required string Operand { get; init; }
    public OperandPath? Path { get; set; }
    public required string Text { get; init; }
}

/// <summary>Case-insensitive, order-preserving tag collection, used for every tag scope (controller, program, add-on parameters/locals).</summary>
public sealed class TagCollection : IReadOnlyList<Tag>
{
    private readonly List<Tag> _items = new();
    private readonly Dictionary<string, Tag> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Count;
    public Tag this[int index] => _items[index];
    public Tag? this[string name] => _byName.TryGetValue(name, out var t) ? t : null;

    public void Add(Tag tag)
    {
        _items.Add(tag);
        _byName[tag.Name] = tag;
    }

    public bool Contains(string name) => _byName.ContainsKey(name);

    public bool TryGet(string name, out Tag tag) => _byName.TryGetValue(name, out tag!);

    public IEnumerator<Tag> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
