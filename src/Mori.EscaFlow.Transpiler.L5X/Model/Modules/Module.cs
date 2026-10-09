using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Modules;

/// <summary>An I/O or communication module from the Modules section.</summary>
public sealed class Module : L5xElement
{
    public required string Name { get; init; }
    public string? CatalogNumber { get; init; }
    public string? Vendor { get; init; }
    public string? ProductType { get; init; }
    public string? ProductCode { get; init; }
    public int? Major { get; init; }
    public int? Minor { get; init; }
    public string? ParentModule { get; init; }
    public int? ParentModPortId { get; init; }
    public bool Inhibited { get; init; }
    public bool MajorFault { get; init; }
    public bool? SafetyEnabled { get; init; }
    public string? SafetyNetwork { get; init; }
    public string? Description { get; set; }

    public GenericElement? EKey { get; set; }
    public List<ModulePort> Ports { get; init; } = new();
    public ModuleCommunications? Communications { get; set; }
    public GenericElement? ExtendedProperties { get; set; }

    /// <summary>Resolved <see cref="ParentModule"/> (set by the linker; null for the local chassis root).</summary>
    [JsonIgnore]
    public Module? Parent { get; set; }

    [JsonIgnore]
    public List<Module> Children { get; } = new();

    public override string ToString() => $"{Name} ({CatalogNumber})";
}

public sealed class ModulePort : L5xElement
{
    public int Id { get; init; }
    public string? Address { get; init; }
    public string? Type { get; init; }
    public bool Upstream { get; init; }
    public int? Width { get; init; }
    public GenericElement? Bus { get; set; }
}

public sealed class ModuleCommunications : L5xElement
{
    public string? CommMethod { get; init; }
    public int? PrimCxnInputSize { get; init; }
    public int? PrimCxnOutputSize { get; init; }
    public ModuleTagData? ConfigTag { get; set; }
    public ModuleTagData? ConfigData { get; set; }
    public GenericElement? ConfigScript { get; set; }
    public List<ModuleConnection> Connections { get; init; } = new();
    public GenericElement? RackConnection { get; set; }
    public GenericElement? SafetyScript { get; set; }
}

public sealed class ModuleConnection : L5xElement
{
    public required string Name { get; init; }
    public int? Rpi { get; init; }
    public string? Type { get; init; }
    public int? EventId { get; init; }
    public bool? Unicast { get; init; }
    public bool? ProgrammaticallySendEventTrigger { get; init; }
    public ModuleTagData? InputTag { get; set; }
    public ModuleTagData? OutputTag { get; set; }
}

/// <summary>Config / input / output data of a module. Shares the value model with ordinary tags.</summary>
public sealed class ModuleTagData : L5xElement
{
    public string? ExternalAccess { get; init; }
    public int? ConfigSize { get; init; }
    public TagData Data { get; set; } = TagData.Empty;
    public List<TagComment> Comments { get; init; } = new();

    /// <summary>The module-defined data type name seen in the Decorated data, e.g. <c>AB:1756_DI:I:0</c>.</summary>
    public string? DataTypeName => Data.Decorated?.DataType;
}
