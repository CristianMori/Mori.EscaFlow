namespace Plc.Runtime;

/// <summary>Controller status the logic can read: first-scan flag (per task, thread-local), scan counters, wall clock, mode.</summary>
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

    public DateTime WallClock => DateTime.Now;
    public DateTime WallClockUtc => DateTime.UtcNow;
    public ControllerMode Mode => _controller.Mode;
    public bool Faulted => _controller.State == ControllerState.Faulted;
    public bool MinorFault { get; set; }

    /// <summary>System bits by Logix name: S:FS, S:N, S:Z, S:V, S:C, S:MINOR, S:MAJOR.</summary>
    public bool GetBit(string name) => name.ToUpperInvariant() switch
    {
        "S:FS" => FirstScan,
        "S:MINOR" => MinorFault,
        "S:MAJOR" => Faulted,
        _ => false,
    };

    internal static void SetFirstScan(bool value) => t_firstScan = value;
    internal void CountScan() => Interlocked.Increment(ref _scanCount);
    internal void Reset()
    {
        Interlocked.Exchange(ref _scanCount, 0);
        MinorFault = false;
    }
}
