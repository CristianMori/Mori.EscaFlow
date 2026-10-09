using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Programs;

/// <summary>
/// Something that owns routines and a tag scope: a program or an add-on instruction definition. The scope rule
/// that matters here is that an unqualified tag name in a routine resolves against its container's own tags
/// first and only then against the controller's, which is why scope resolution (see <c>ScopeResolver</c>) walks
/// this interface rather than the concrete <see cref="Program"/> and <see cref="Aoi.AddOnInstructionDefinition"/> types separately.
/// </summary>
public interface IRoutineContainer
{
    string Name { get; }
    IReadOnlyList<Routine> Routines { get; }
    TagCollection Tags { get; }

    /// <summary>Source-protected routines whose body could not be read (see <see cref="Program.EncodedRoutines"/>).</summary>
    IReadOnlyList<GenericElement> EncodedRoutines { get; }
}

/// <summary>
/// A program: its own tags (which shadow same-named controller tags for routines in this program), its routines,
/// and optionally child programs nested under it (<c>UseAsFolder</c> programs are organizational only and never
/// run). A program only executes while scheduled into a task (<see cref="Task"/>), and even then only its
/// <see cref="MainRoutine"/> runs directly; the rest run through JSR or as the fault handler.
/// </summary>
public sealed class Program : L5xElement, IRoutineContainer
{
    public required string Name { get; init; }
    public string? Type { get; init; }
    public string? Class { get; init; }

    /// <summary>True when the program's routines currently hold unaccepted online edits (test edits) rather than only accepted ones.</summary>
    public bool TestEdits { get; init; }
    public bool Disabled { get; init; }

    /// <summary>True for a program that exists only to group child programs in the project tree and never itself runs.</summary>
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

    /// <summary>Resolved <see cref="MainRoutineName"/> (linker): the routine the task runs on every scan.</summary>
    [JsonIgnore]
    public Routine? MainRoutine { get; set; }

    /// <summary>Resolved <see cref="FaultRoutineName"/> (linker): runs instead of faulting the controller when this program raises a recoverable fault.</summary>
    [JsonIgnore]
    public Routine? FaultRoutine { get; set; }

    [JsonIgnore]
    public List<Program> ChildPrograms { get; } = new();

    [JsonIgnore]
    public Program? ParentProgram { get; set; }

    /// <summary>The task this program is scheduled into (linker); null if the project leaves it unscheduled.</summary>
    [JsonIgnore]
    public L5xTask? Task { get; set; }

    public Routine? FindRoutine(string name) => Routines.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Name;
}

/// <summary>How a task is triggered: continuous (runs whenever no higher-priority task needs the processor), periodic (a fixed interval) or event (an external trigger such as a module's data-available event).</summary>
public enum TaskType
{
    Continuous,
    Periodic,
    Event,
    Unknown,
}

/// <summary>
/// A controller task: the unit the controller actually schedules. Programs do not run on their own; they run
/// only because a task lists them in <see cref="ScheduledProgramNames"/>, in that order, once per task
/// invocation (once per <see cref="Rate"/> for a periodic task, or on every opportunity for the continuous one).
/// </summary>
public sealed class L5xTask : L5xElement
{
    public required string Name { get; init; }
    public TaskType Type { get; init; }
    public string? Class { get; init; }

    /// <summary>Periodic task interval in milliseconds; null for continuous and event tasks.</summary>
    public double? Rate { get; init; }
    public int? Priority { get; init; }

    /// <summary>Maximum execution time in milliseconds before the controller declares a watchdog fault.</summary>
    public double? Watchdog { get; init; }
    public bool DisableUpdateOutputs { get; init; }
    public bool InhibitTask { get; init; }
    public string? Description { get; set; }

    /// <summary>Event trigger configuration for an event task (the module and condition that fires it); kept verbatim.</summary>
    public GenericElement? EventInfo { get; set; }
    public List<string> ScheduledProgramNames { get; init; } = new();

    /// <summary>Resolved <see cref="ScheduledProgramNames"/>, in schedule order (linker).</summary>
    [JsonIgnore]
    public List<Program> ScheduledPrograms { get; } = new();

    public override string ToString() => $"{Name} ({Type})";
}
