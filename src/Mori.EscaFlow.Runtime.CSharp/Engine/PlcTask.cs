using System.Diagnostics;

namespace Mori.EscaFlow.Runtime;

/// <summary>The three Logix task kinds.</summary>
public enum TaskKind
{
    /// <summary>Free-running: scans again as soon as the previous scan ends (the one continuous task of a project).</summary>
    Continuous,

    /// <summary>Scans once per interval, with an overrun recorded when a scan takes longer than the interval.</summary>
    Periodic,

    /// <summary>Scans once per trigger (EVENT instruction, consumed tag, module input change).</summary>
    Event,
}

/// <summary>
/// A Logix task: its programs scanned in order on a dedicated thread, free-running, periodic or on a trigger.
/// </summary>
/// <remarks>
/// <para>
/// One thread per task is the closest cheap model of the Logix scheduler: the programs of a task run strictly in
/// their scheduled order within one scan, different tasks run concurrently (a 1756 preempts by priority; here the
/// operating system schedules, with high-priority tasks mapped to an above-normal thread priority), and a task
/// that throws takes the whole controller to a major fault, as an unhandled program fault does on the real
/// controller.
/// </para>
/// <para>
/// Periodic pacing is done against the stopwatch, not by sleeping the whole interval: the thread sleeps until
/// about a millisecond before the next period and then spins, which keeps the period accurate on a general-purpose
/// operating system. A scan that overruns its period starts the next one immediately and increments the overrun
/// counter, mirroring the task overlap count Logix reports.
/// </para>
/// </remarks>
public sealed class PlcTask
{
    private readonly object _gate = new();
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    /// <summary>Event tasks wait on this; <see cref="Trigger"/> releases one scan.</summary>
    private readonly SemaphoreSlim _trigger = new(0);
    private PlcController? _controller;
    private double _totalMs;

    /// <summary>Defines a task; the generated controller adds it to <see cref="PlcController.Tasks"/>.</summary>
    /// <param name="intervalMs">Period for periodic tasks; ignored otherwise.</param>
    /// <param name="priority">Logix priority (1 highest .. 15 lowest).</param>
    /// <param name="watchdogMs">Maximum scan time before a major fault; null disables the check.</param>
    /// <param name="programs">The program instances in scan order.</param>
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

    /// <summary>Task name as in the project.</summary>
    public string Name { get; }

    /// <summary>Continuous, periodic or event.</summary>
    public TaskKind Kind { get; }

    /// <summary>Period of a periodic task in milliseconds.</summary>
    public double? IntervalMs { get; }

    /// <summary>Logix priority (1 highest .. 15 lowest).</summary>
    public int Priority { get; }

    /// <summary>Scan time limit in milliseconds; exceeding it is a major fault.</summary>
    public double? WatchdogMs { get; }

    /// <summary>Scans during which the watchdog is not enforced (JIT warm-up).</summary>
    /// <remarks>The first scans compile the whole logic just in time and can take seconds for a large project, which a 20 ms watchdog would otherwise treat as a fault.</remarks>
    public int WarmupScans { get; set; } = 5;

    /// <summary>The program instances in scan order.</summary>
    public List<PlcProgram> Programs { get; }

    /// <summary>Scan counters and timings, updated after every scan.</summary>
    public TaskStatistics Statistics { get; }

    /// <summary>True while the task thread is alive.</summary>
    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Releases one scan of an event task (Logix EVENT instruction, consumed tag, module input state change).</summary>
    /// <remarks>Triggers are not queued: like the controller, a task already pending to run does not run twice for two triggers that arrive before it starts.</remarks>
    public void Trigger()
    {
        if (Kind == TaskKind.Event && _trigger.CurrentCount == 0) _trigger.Release();
    }

    /// <summary>Starts the task thread (called by the controller when entering Run).</summary>
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
                // Logix priorities 1..5 are the time-critical band; give them a scheduling edge over the rest.
                Priority = Priority <= 5 ? ThreadPriority.AboveNormal : ThreadPriority.Normal,
            };
            _thread.Start();
        }
    }

    /// <summary>Cancels the task and waits for its thread, unless called from that thread (a faulting task stops itself by returning).</summary>
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

    /// <summary>The scan loop: wait for the trigger (event tasks), scan the programs in order, record timing, enforce the watchdog, pace to the next period.</summary>
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
                // S:FS is per task: each task's first scan after Run sees it, independently of the others.
                SystemStatus.SetFirstScan(first);
                foreach (var program in Programs)
                {
                    try
                    {
                        program.Main();
                    }
                    catch (Exception ex)
                    {
                        // The program's fault routine runs first (it may clear the condition on a real controller);
                        // the exception then propagates and faults the controller.
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
                            // The scan took longer than the period: count the overrun and resynchronise.
                            Statistics.Overruns++;
                            next = now;
                        }
                        else
                        {
                            // Sleep coarsely, then spin for the last millisecond for an accurate period.
                            var waitMs = (next - now) * 1000.0 / frequency;
                            if (waitMs > 1.5) Thread.Sleep((int)(waitMs - 1));
                            while (Stopwatch.GetTimestamp() < next && !token.IsCancellationRequested) Thread.SpinWait(20);
                        }
                        break;
                    case TaskKind.Continuous:
                        // Yield between scans so the other tasks and the server get the core on a busy machine.
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

    /// <summary>Updates the per-task statistics after a scan.</summary>
    private void Record(double elapsedMs)
    {
        Statistics.Scans++;
        Statistics.LastScanMs = elapsedMs;
        _totalMs += elapsedMs;
        Statistics.AverageScanMs = _totalMs / Statistics.Scans;
        if (elapsedMs > Statistics.MaxScanMs) Statistics.MaxScanMs = elapsedMs;
    }

    /// <summary>Name and kind, for diagnostics.</summary>
    public override string ToString() => $"{Name} ({Kind})";
}
