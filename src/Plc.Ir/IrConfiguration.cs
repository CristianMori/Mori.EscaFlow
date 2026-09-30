namespace Plc.Ir;

/// <summary>IEC 61131-3 CONFIGURATION: the controller and its resources.</summary>
public sealed class IrConfiguration : IrNode
{
    public string Name { get; set; } = "";
    public List<IrResource> Resources { get; set; } = new();
}

/// <summary>IEC RESOURCE: one processor with its tasks and program instances. Logix controllers have exactly one.</summary>
public sealed class IrResource : IrNode
{
    public string Name { get; set; } = "";
    public string? ProcessorType { get; set; }
    public List<IrTask> Tasks { get; set; } = new();

    /// <summary>Program instances (IEC <c>PROGRAM inst WITH task : Pou;</c>).</summary>
    public List<IrProgramInstance> Programs { get; set; } = new();

    /// <summary>Program instance run on a controller fault, when the platform has one.</summary>
    [IrRefKind("programInstance")]
    public string? FaultProgram { get; set; }
}

public sealed class IrTask : IrNode
{
    public string Name { get; set; } = "";

    /// <summary>continuous: free-running; periodic: fixed interval; event: triggered by <see cref="Event"/>; startup: once at start.</summary>
    [IrEnum("continuous", "periodic", "event", "startup")]
    public string Kind { get; set; } = "continuous";
    public double? IntervalMs { get; set; }
    public int? Priority { get; set; }
    public double? WatchdogMs { get; set; }
    public IrTaskEvent? Event { get; set; }

    /// <summary>Program instances in scan order.</summary>
    [IrRefKind("programInstance")]
    public List<string> Programs { get; set; } = new();
    public bool Disabled { get; set; }
}

public sealed class IrTaskEvent
{
    /// <summary>Trigger kind as the vendor names it (e.g. Logix "Module Input Data State Change", "Consumed Tag", "EVENT Instruction", "Axis Registration").</summary>
    public string Kind { get; set; } = "";

    /// <summary>Event source: a variable id, a device id or a name, depending on <see cref="Kind"/>.</summary>
    public string? Source { get; set; }
}

public sealed class IrProgramInstance : IrNode
{
    public string Name { get; set; } = "";

    [IrRefKind("pou")]
    public string Pou { get; set; } = "";

    [IrRefKind("task")]
    public string? Task { get; set; }
    public bool Disabled { get; set; }
}
