namespace Mori.EscaFlow.Runtime;

/// <summary>Life-cycle state of a controller.</summary>
public enum ControllerState
{
    /// <summary>Not scanning; tags readable and writable (Logix Program mode without a fault).</summary>
    Stopped,

    /// <summary>Tasks are scanning.</summary>
    Running,

    /// <summary>A major fault stopped the tasks; <c>ClearFault</c> is needed before the next start.</summary>
    Faulted,
}

/// <summary>The Logix key-switch modes the runtime distinguishes.</summary>
public enum ControllerMode
{
    /// <summary>Program mode: no scanning.</summary>
    Program,

    /// <summary>Run mode: tasks scanning.</summary>
    Run,
}

/// <summary>Scan timing of one task.</summary>
public sealed class TaskStatistics
{
    /// <summary>Task name.</summary>
    public string Name { get; init; } = "";

    /// <summary>continuous | periodic | event.</summary>
    public string Kind { get; init; } = "continuous";

    /// <summary>Scans completed since the last start.</summary>
    public long Scans { get; set; }

    /// <summary>Duration of the most recent scan.</summary>
    public double LastScanMs { get; set; }

    /// <summary>Mean scan duration since the last start.</summary>
    public double AverageScanMs { get; set; }

    /// <summary>Longest scan since the last start (the first scans include JIT compilation).</summary>
    public double MaxScanMs { get; set; }

    /// <summary>Period of a periodic task.</summary>
    public double? IntervalMs { get; init; }

    /// <summary>Scans that took longer than the period (Logix task overlap count).</summary>
    public int Overruns { get; set; }
}

/// <summary>Controller-wide run statistics.</summary>
public sealed class ScanStatistics
{
    /// <summary>When Run was entered, or null while stopped.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>One entry per task.</summary>
    public List<TaskStatistics> Tasks { get; } = new();

    /// <summary>The major fault text, or null.</summary>
    public string? Fault { get; set; }
}

/// <summary>A tag as the host lists it.</summary>
/// <param name="Path">Logix path: <c>Tag</c>, <c>Program:P.Tag</c> or <c>Module:I</c>.</param>
/// <param name="Type">Type name (atomic or structure).</param>
/// <param name="Dims">Array dimensions, empty for scalars.</param>
/// <param name="Program">Owning program for program-scoped tags.</param>
/// <param name="Description">Description from the project, when kept.</param>
public sealed record TagInfo(string Path, string Type, int[] Dims, string? Program, string? Description);

/// <summary>A value read through the host API: the text form plus the type it was read as.</summary>
public sealed record TagValueText(string Path, string Type, string Text);

/// <summary>
/// The contract between a generated controller and the hosts (web page, console, tests).
/// </summary>
/// <remarks>
/// Hosts never see wrappers or tag buffers: they address values by Logix path and exchange text, which keeps a
/// host independent of the generated types and lets the same web console drive any generated controller.
/// </remarks>
public interface IPlcController
{
    /// <summary>Controller name from the project.</summary>
    string Name { get; }

    /// <summary>Current life-cycle state.</summary>
    ControllerState State { get; }

    /// <summary>Current mode.</summary>
    ControllerMode Mode { get; }

    /// <summary>Run statistics (updated live while running).</summary>
    ScanStatistics Statistics { get; }

    /// <summary>Enters Run: runs every program's prescan once, then starts the tasks.</summary>
    void Start();

    /// <summary>Leaves Run: stops the tasks; tag values are kept.</summary>
    void Stop();

    /// <summary>Every tag (controller, program-scoped as <c>Program:P.Tag</c>, module images as <c>Module:I</c>), optionally filtered by a case-insensitive substring of the path.</summary>
    IEnumerable<TagInfo> Tags(string? filter = null);

    /// <summary>Reads a value by Logix path (members, elements and bits allowed: <c>M1.Timer.PRE</c>, <c>Flags[3]</c>, <c>Word.5</c>, <c>Program:P.Idx</c>).</summary>
    bool TryRead(string path, out TagValueText? value);

    /// <summary>Writes a value given as text (numbers, TRUE/FALSE/0/1, quoted or bare strings).</summary>
    bool TryWrite(string path, string text, out string? error);

    /// <summary>Writes every tag's bytes to the stream (tags are retentive; a snapshot is the controller's memory).</summary>
    void SaveSnapshot(Stream stream);

    /// <summary>Restores tag bytes from a snapshot, by name, tolerating tags added or removed since it was taken.</summary>
    void LoadSnapshot(Stream stream);
}
