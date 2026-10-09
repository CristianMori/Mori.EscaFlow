namespace Mori.EscaFlow.Transpiler;

/// <summary>IEC 61131-3 CONFIGURATION: the controller and its resources.</summary>
/// <remarks>
/// IEC separates the configuration (the whole system), its resources (processors) and the tasks that schedule
/// program instances on each resource. A Logix controller maps onto a configuration with exactly one resource;
/// a multi-CPU Siemens or Codesys system maps onto several. Converters build their "controller" or "task
/// engine" from this tree.
/// </remarks>
public sealed class IrConfiguration : IrNode
{
    /// <summary>Configuration (controller) name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The resources (processors) of the configuration.</summary>
    public List<IrResource> Resources { get; set; } = new();
}

/// <summary>IEC RESOURCE: one processor with its tasks and program instances. Logix controllers have exactly one.</summary>
public sealed class IrResource : IrNode
{
    /// <summary>Resource name (the controller name for Logix).</summary>
    public string Name { get; set; } = "";

    /// <summary>Processor catalog number, when known (for example <c>1756-L85E</c>).</summary>
    public string? ProcessorType { get; set; }

    /// <summary>The tasks scheduled on this resource.</summary>
    public List<IrTask> Tasks { get; set; } = new();

    /// <summary>Program instances (IEC <c>PROGRAM inst WITH task : Pou;</c>).</summary>
    /// <remarks>Instances and POUs are distinct in IEC: one program POU may be instantiated several times under different tasks. Logix has one instance per program, named like the POU.</remarks>
    public List<IrProgramInstance> Programs { get; set; } = new();

    /// <summary>Program instance run on a controller fault, when the platform has one.</summary>
    [IrRefKind("programInstance")]
    public string? FaultProgram { get; set; }
}

/// <summary>A task: how and when a group of program instances is scanned.</summary>
/// <remarks>
/// The scan order within a task is the order of <see cref="Programs"/>. Priorities follow the vendor's
/// convention (Logix: 1 is highest); the runtime maps them onto thread priorities. The watchdog is the maximum
/// scan time before the controller faults, as on the real PLC.
/// </remarks>
public sealed class IrTask : IrNode
{
    /// <summary>Task name.</summary>
    public string Name { get; set; } = "";

    /// <summary>continuous: free-running; periodic: fixed interval; event: triggered by <see cref="Event"/>; startup: once at start.</summary>
    [IrEnum("continuous", "periodic", "event", "startup")]
    public string Kind { get; set; } = "continuous";

    /// <summary>Period of a periodic task in milliseconds.</summary>
    public double? IntervalMs { get; set; }

    /// <summary>Scheduling priority in the vendor's scale.</summary>
    public int? Priority { get; set; }

    /// <summary>Maximum scan time before a major fault, in milliseconds.</summary>
    public double? WatchdogMs { get; set; }

    /// <summary>What triggers an event task.</summary>
    public IrTaskEvent? Event { get; set; }

    /// <summary>Program instances in scan order.</summary>
    [IrRefKind("programInstance")]
    public List<string> Programs { get; set; } = new();

    /// <summary>True when the task is inhibited in the source project.</summary>
    public bool Disabled { get; set; }
}

/// <summary>The trigger of an event task.</summary>
public sealed class IrTaskEvent
{
    /// <summary>Trigger kind as the vendor names it (e.g. Logix "Module Input Data State Change", "Consumed Tag", "EVENT Instruction", "Axis Registration").</summary>
    public string Kind { get; set; } = "";

    /// <summary>Event source: a variable id, a device id or a name, depending on <see cref="Kind"/>.</summary>
    public string? Source { get; set; }
}

/// <summary>An instance of a program POU scheduled on a resource.</summary>
public sealed class IrProgramInstance : IrNode
{
    /// <summary>Instance name (what the task list refers to).</summary>
    public string Name { get; set; } = "";

    /// <summary>The program POU being instantiated.</summary>
    [IrRefKind("pou")]
    public string Pou { get; set; } = "";

    /// <summary>The task that scans the instance; null for an unscheduled program (Logix programs not assigned to a task).</summary>
    [IrRefKind("task")]
    public string? Task { get; set; }

    /// <summary>True when the instance is inhibited in the source project.</summary>
    public bool Disabled { get; set; }
}
