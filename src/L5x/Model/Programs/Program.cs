using System.Text.Json.Serialization;
using L5x.Model.Logic;
using L5x.Model.Tags;

namespace L5x.Model.Programs;

/// <summary>Something that owns routines and a tag scope: a program or an add-on instruction definition.</summary>
public interface IRoutineContainer
{
    string Name { get; }
    IReadOnlyList<Routine> Routines { get; }
    TagCollection Tags { get; }
    IReadOnlyList<GenericElement> EncodedRoutines { get; }
}

public sealed class Program : L5xElement, IRoutineContainer
{
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Class { get; init; }
    public bool TestEdits { get; init; }
    public bool Disabled { get; init; }
    public bool UseAsFolder { get; init; }
    public string? MainRoutineName { get; init; }
    public string? FaultRoutineName { get; init; }
    public bool? SynchronizeRedundancyDataAfterExecution { get; init; }
    public string? Description { get; set; }

    public TagCollection Tags { get; init; } = new();
    public List<Routine> Routines { get; init; } = new();
    public List<string> ChildProgramNames { get; init; } = new();

    /// <summary>Source-protected routines exported as <c>&lt;EncodedData&gt;</c> blobs; their names and logic are not available.</summary>
    public List<GenericElement> EncodedRoutines { get; init; } = new();

    IReadOnlyList<Routine> IRoutineContainer.Routines => Routines;
    IReadOnlyList<GenericElement> IRoutineContainer.EncodedRoutines => EncodedRoutines;

    [JsonIgnore]
    public Routine? MainRoutine { get; set; }

    [JsonIgnore]
    public Routine? FaultRoutine { get; set; }

    [JsonIgnore]
    public List<Program> ChildPrograms { get; } = new();

    [JsonIgnore]
    public Program? ParentProgram { get; set; }

    [JsonIgnore]
    public L5xTask? Task { get; set; }

    public Routine? FindRoutine(string name) => Routines.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Name;
}

public enum TaskType
{
    Continuous,
    Periodic,
    Event,
    Unknown,
}

public sealed class L5xTask : L5xElement
{
    public required string Name { get; init; }
    public TaskType Type { get; init; }
    public string? Class { get; init; }
    public double? Rate { get; init; }
    public int? Priority { get; init; }
    public double? Watchdog { get; init; }
    public bool DisableUpdateOutputs { get; init; }
    public bool InhibitTask { get; init; }
    public string? Description { get; set; }
    public GenericElement? EventInfo { get; set; }
    public List<string> ScheduledProgramNames { get; init; } = new();

    [JsonIgnore]
    public List<Program> ScheduledPrograms { get; } = new();

    public override string ToString() => $"{Name} ({Type})";
}
