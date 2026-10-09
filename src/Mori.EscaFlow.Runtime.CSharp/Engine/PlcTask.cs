using System.Diagnostics;

namespace Mori.EscaFlow.Runtime;

public enum TaskKind
{
    Continuous,
    Periodic,
    Event,
}

/// <summary>A Logix task: its programs scanned in order on a dedicated thread, free-running, periodic or on a trigger.</summary>
public sealed class PlcTask
{
    private readonly object _gate = new();
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _trigger = new(0);
    private PlcController? _controller;
    private double _totalMs;

    public PlcTask(string name, TaskKind kind, double? intervalMs, int priority, double? watchdogMs, IReadOnlyList<PlcProgram> programs)
    {
        Name = name;
        Kind = kind;
        IntervalMs = intervalMs;
        Priority = priority;
        WatchdogMs = watchdogMs;
        Programs = programs.ToList();
        Statistics = new TaskStatistics { Name = name, Kind = kind.ToString().ToLowerInvariant(), IntervalMs = intervalMs };
    }

    public string Name { get; }
    public TaskKind Kind { get; }
    public double? IntervalMs { get; }
    public int Priority { get; }
    public double? WatchdogMs { get; }

    /// <summary>Scans during which the watchdog is not enforced (JIT warm-up).</summary>
    public int WarmupScans { get; set; } = 5;
    public List<PlcProgram> Programs { get; }
    public TaskStatistics Statistics { get; }
    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Releases one scan of an event task (Logix EVENT instruction, consumed tag, module input state change).</summary>
    public void Trigger()
    {
        if (Kind == TaskKind.Event && _trigger.CurrentCount == 0) _trigger.Release();
    }

    internal void Start(PlcController controller)
    {
        lock (_gate)
        {
            if (IsRunning) return;
            _controller = controller;
            _cts = new CancellationTokenSource();
            Statistics.Scans = 0;
            Statistics.LastScanMs = 0;
            Statistics.AverageScanMs = 0;
            Statistics.MaxScanMs = 0;
            Statistics.Overruns = 0;
            _totalMs = 0;
            var token = _cts.Token;
            _thread = new Thread(() => Run(token))
            {
                Name = $"PlcTask {Name}",
                IsBackground = true,
                Priority = Priority <= 5 ? ThreadPriority.AboveNormal : ThreadPriority.Normal,
            };
            _thread.Start();
        }
    }

    internal void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            _cts?.Cancel();
            _thread = null;
        }
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(5));
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Run(CancellationToken token)
    {
        var controller = _controller!;
        var first = true;
        var frequency = (double)Stopwatch.Frequency;
        var periodTicks = Kind == TaskKind.Periodic && IntervalMs is { } ms ? (long)(ms / 1000.0 * frequency) : 0;
        var next = Stopwatch.GetTimestamp();
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Kind == TaskKind.Event)
                {
                    try { _trigger.Wait(token); }
                    catch (OperationCanceledException) { break; }
                    if (token.IsCancellationRequested) break;
                }

                var start = Stopwatch.GetTimestamp();
                SystemStatus.SetFirstScan(first);
                foreach (var program in Programs)
                {
                    try
                    {
                        program.Main();
                    }
                    catch (Exception ex)
                    {
                        controller.Log($"Program {program.Name} faulted: {ex.Message}");
                        program.Fault();
                        throw;
                    }
                }
                SystemStatus.SetFirstScan(false);
                first = false;
                var elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / frequency;
                Record(elapsed);
                controller.Sys.CountScan();
                // The first scans include JIT compilation of the whole logic; the watchdog applies once warmed up.
                if (WatchdogMs is { } wd && elapsed > wd && Statistics.Scans > WarmupScans)
                {
                    throw new TimeoutException($"Task {Name} exceeded its watchdog ({elapsed:F1} ms > {wd} ms)");
                }

                switch (Kind)
                {
                    case TaskKind.Periodic:
                        next += periodTicks;
                        var now = Stopwatch.GetTimestamp();
                        if (now > next)
                        {
                            Statistics.Overruns++;
                            next = now;
                        }
                        else
                        {
                            var waitMs = (next - now) * 1000.0 / frequency;
                            if (waitMs > 1.5) Thread.Sleep((int)(waitMs - 1));
                            while (Stopwatch.GetTimestamp() < next && !token.IsCancellationRequested) Thread.SpinWait(20);
                        }
                        break;
                    case TaskKind.Continuous:
                        Thread.Yield();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            controller.Fault(ex);
        }
    }

    private void Record(double elapsedMs)
    {
        Statistics.Scans++;
        Statistics.LastScanMs = elapsedMs;
        _totalMs += elapsedMs;
        Statistics.AverageScanMs = _totalMs / Statistics.Scans;
        if (elapsedMs > Statistics.MaxScanMs) Statistics.MaxScanMs = elapsedMs;
    }

    public override string ToString() => $"{Name} ({Kind})";
}
