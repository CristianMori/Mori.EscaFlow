namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Controller status the logic can read: first-scan flag (per task, thread-local), scan counters, wall clock, mode.
/// </summary>
/// <remarks>
/// Logix exposes these through the <c>S:</c> status bits and GSV. The first-scan flag is the important one: logic
/// commonly initialises state on <c>S:FS</c>, and the controller sets it only during each task's first scan after
/// entering Run. Because every task runs on its own thread and reaches its first scan at its own time, the flag is
/// thread-static: the task thread sets it before its first scan and clears it after, and other tasks are unaffected.
/// </remarks>
public sealed class SystemStatus
{
    [ThreadStatic] private static bool t_firstScan;

    private readonly PlcController _controller;
    private long _scanCount;

    internal SystemStatus(PlcController controller) => _controller = controller;

    /// <summary>True only while a task performs its first scan after the controller entered Run (Logix S:FS).</summary>
    public bool FirstScan => t_firstScan;

    /// <summary>Total scans of all tasks since the last Start.</summary>
    public long ScanCount => Interlocked.Read(ref _scanCount);

    /// <summary>Local wall-clock time (GSV WallClockTime LocalDateTime).</summary>
    public DateTime WallClock => DateTime.Now;

    /// <summary>UTC wall-clock time (GSV WallClockTime DateTime).</summary>
    public DateTime WallClockUtc => DateTime.UtcNow;

    /// <summary>The controller mode (Program / Run).</summary>
    public ControllerMode Mode => _controller.Mode;

    /// <summary>True while the controller is in a major fault (S:MAJOR).</summary>
    public bool Faulted => _controller.State == ControllerState.Faulted;

    /// <summary>Minor fault flag (S:MINOR), set by instructions such as a division by zero; cleared on Start.</summary>
    public bool MinorFault { get; set; }

    /// <summary>System bits by Logix name: S:FS, S:N, S:Z, S:V, S:C, S:MINOR, S:MAJOR.</summary>
    /// <remarks>The arithmetic flags (S:N, S:Z, S:V, S:C) are not tracked; they read as false. Logix itself discourages their use and the sample projects never read them.</remarks>
    public bool GetBit(string name) => name.ToUpperInvariant() switch
    {
        "S:FS" => FirstScan,
        "S:MINOR" => MinorFault,
        "S:MAJOR" => Faulted,
        _ => false,
    };

    /// <summary>Sets the calling task thread's first-scan flag.</summary>
    internal static void SetFirstScan(bool value) => t_firstScan = value;

    /// <summary>Counts one completed scan of any task.</summary>
    internal void CountScan() => Interlocked.Increment(ref _scanCount);

    /// <summary>Resets the counters on entering Run.</summary>
    internal void Reset()
    {
        Interlocked.Exchange(ref _scanCount, 0);
        MinorFault = false;
    }
}
