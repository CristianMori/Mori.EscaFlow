using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Modules;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;

namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>
/// The controller: identity and revision, then the six sections of a Logix project in file order (data types,
/// modules, add-on instruction definitions, controller-scoped tags, programs, tasks). Everything else the
/// <c>Controller</c> element can contain is kept as generic elements.
/// </summary>
public sealed class Controller : L5xElement
{
    public required string Name { get; init; }

    /// <summary><c>Context</c> for a partial export that only carries the surroundings of its target, otherwise <c>Target</c>.</summary>
    public string? Use { get; init; }

    /// <summary>Catalog number of the processor (1756-L85E, 5069-L320ER, ...).</summary>
    public string? ProcessorType { get; init; }

    /// <summary>Firmware major revision; decides which instructions and types exist.</summary>
    public int? MajorRev { get; init; }
    public int? MinorRev { get; init; }
    public string? ProjectCreationDate { get; init; }
    public string? LastModifiedDate { get; init; }

    /// <summary>SFC execution options of the controller (CurrentActive / UntilFalse, MostRecent / InitialStep, DontScan / ProgrammaticReset / AutomaticReset).</summary>
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

    /// <summary>Controller-scoped tags. Program-scoped tags live on their <see cref="Program"/>.</summary>
    public TagCollection Tags { get; } = new();
    public List<Program> Programs { get; } = new();
    public List<L5xTask> Tasks { get; } = new();

    /// <summary>CST, WallClockTime, Trends, DataLogs, TimeSynchronize, EthernetPorts, ParameterConnections, ... in file order.</summary>
    public List<GenericElement> Other { get; } = new();

    /// <summary>Every data type visible to this controller (built-in, user, add-on, module-defined). Populated by the linker.</summary>
    [JsonIgnore]
    public DataTypeRegistry Types { get; set; } = new();

    /// <summary>Lookups are case-insensitive because Logix names are.</summary>
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

/// <summary>GuardLogix safety settings: signature, lock state and the standard-to-safety tag mapping.</summary>
public sealed class SafetyInfo : L5xElement
{
    public string? SafetySignature { get; init; }
    public bool? SafetyLocked { get; init; }
    public bool? SignatureRunModeProtect { get; init; }
    public bool? ConfigureSafetyIOAlways { get; init; }

    /// <summary>SIL2 / SIL3 (PLd / PLe).</summary>
    public string? SafetyLevel { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();

    /// <summary>Pairs of (standard tag, safety tag) from the SafetyTagMap text.</summary>
    public List<KeyValuePair<string, string>> SafetyTagMap { get; init; } = new();
}
