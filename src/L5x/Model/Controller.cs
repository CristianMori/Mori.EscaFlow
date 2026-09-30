using System.Text.Json.Serialization;
using L5x.Model.Aoi;
using L5x.Model.DataTypes;
using L5x.Model.Modules;
using L5x.Model.Programs;
using L5x.Model.Tags;

namespace L5x.Model;

public sealed class Controller : L5xElement
{
    public required string Name { get; init; }
    public string? Use { get; init; }
    public string? ProcessorType { get; init; }
    public int? MajorRev { get; init; }
    public int? MinorRev { get; init; }
    public string? ProjectCreationDate { get; init; }
    public string? LastModifiedDate { get; init; }
    public string? SfcExecutionControl { get; init; }
    public string? SfcRestartPosition { get; init; }
    public string? SfcLastScan { get; init; }
    public string? ProjectSN { get; init; }
    public string? Description { get; set; }

    /// <summary>All remaining controller attributes (TimeSlice, PassThroughConfiguration, ...) that are not modelled individually.</summary>
    public Dictionary<string, string> Attributes { get; init; } = new();

    public GenericElement? RedundancyInfo { get; set; }
    public GenericElement? Security { get; set; }
    public SafetyInfo? SafetyInfo { get; set; }

    public DataTypeCollection DataTypes { get; } = new();
    public List<Module> Modules { get; } = new();
    public List<AddOnInstructionDefinition> AddOnInstructions { get; } = new();
    public TagCollection Tags { get; } = new();
    public List<Program> Programs { get; } = new();
    public List<L5xTask> Tasks { get; } = new();

    /// <summary>CST, WallClockTime, Trends, DataLogs, TimeSynchronize, EthernetPorts, ParameterConnections, ... in file order.</summary>
    public List<GenericElement> Other { get; } = new();

    /// <summary>Every data type visible to this controller (built-in, user, add-on, module-defined). Populated by the linker.</summary>
    [JsonIgnore]
    public DataTypeRegistry Types { get; set; } = new();

    public Program? FindProgram(string name) => Programs.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public AddOnInstructionDefinition? FindAoi(string name) => AddOnInstructions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public Module? FindModule(string name) => Modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every routine in programs and add-on definitions.</summary>
    [JsonIgnore]
    public IEnumerable<Logic.Routine> AllRoutines => Programs.SelectMany(p => p.Routines).Concat(AddOnInstructions.SelectMany(a => a.Routines));

    /// <summary>Every MESSAGE tag with its configuration, in every scope.</summary>
    [JsonIgnore]
    public IEnumerable<Tag> MessageTags => AllTags.Where(t => t.Data.Message is not null);

    /// <summary>Every tag in every scope (controller, program, add-on parameters and locals).</summary>
    [JsonIgnore]
    public IEnumerable<Tag> AllTags => Tags
        .Concat(Programs.SelectMany(p => p.Tags))
        .Concat(AddOnInstructions.SelectMany(a => a.Parameters.Cast<Tag>().Concat(a.LocalTags)));

    public override string ToString() => $"{Name} ({ProcessorType} v{MajorRev}.{MinorRev})";
}

public sealed class SafetyInfo : L5xElement
{
    public string? SafetySignature { get; init; }
    public bool? SafetyLocked { get; init; }
    public bool? SignatureRunModeProtect { get; init; }
    public bool? ConfigureSafetyIOAlways { get; init; }
    public string? SafetyLevel { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();

    /// <summary>Pairs of (standard tag, safety tag) from the SafetyTagMap text.</summary>
    public List<KeyValuePair<string, string>> SafetyTagMap { get; init; } = new();
}
