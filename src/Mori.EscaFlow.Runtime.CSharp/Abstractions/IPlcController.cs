namespace Mori.EscaFlow.Runtime;

public enum ControllerState
{
    Stopped,
    Running,
    Faulted,
}

public enum ControllerMode
{
    Program,
    Run,
}

/// <summary>Scan timing of one task.</summary>
public sealed class TaskStatistics
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "continuous";
    public long Scans { get; set; }
    public double LastScanMs { get; set; }
    public double AverageScanMs { get; set; }
    public double MaxScanMs { get; set; }
    public double? IntervalMs { get; init; }
    public int Overruns { get; set; }
}

public sealed class ScanStatistics
{
    public DateTimeOffset? StartedAt { get; set; }
    public List<TaskStatistics> Tasks { get; } = new();
    public string? Fault { get; set; }
}

/// <summary>A tag as the host lists it.</summary>
public sealed record TagInfo(string Path, string Type, int[] Dims, string? Program, string? Description);

/// <summary>A value read through the host API: the text form plus the type it was read as.</summary>
public sealed record TagValueText(string Path, string Type, string Text);

/// <summary>The contract between a generated controller and the hosts (web page, console, tests).</summary>
public interface IPlcController
{
    string Name { get; }
    ControllerState State { get; }
    ControllerMode Mode { get; }
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

    void SaveSnapshot(Stream stream);
    void LoadSnapshot(Stream stream);
}
