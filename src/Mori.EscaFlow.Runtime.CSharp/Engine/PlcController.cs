using System.Net;
using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>Construction-time settings of a generated controller.</summary>
public sealed class PlcControllerOptions
{
    /// <summary>TCP port of the EtherNet/IP tag server; null disables the server.</summary>
    public int? EipPort { get; init; } = 44818;

    /// <summary>Address to bind the tag server to (all interfaces by default).</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>Vendor id, product code and name presented to EtherNet/IP clients.</summary>
    public ushort VendorId { get; init; } = 1;

    /// <summary>CIP product code reported by the Identity object (0x5C is a ControlLogix-class value clients accept).</summary>
    public ushort ProductCode { get; init; } = 0x5C;

    /// <summary>Product name reported by the Identity object; defaults to the controller name.</summary>
    public string? ProductName { get; init; }

    /// <summary>Receives runtime log lines (defaults to the console).</summary>
    public Action<string>? Log { get; init; }

    /// <summary>When true, a call to a vendor service the runtime does not implement faults the controller; when false (default) it is logged once per call site and skipped.</summary>
    /// <remarks>The default keeps a project with a few unsupported instructions running for everything else; the strict mode is for verifying that a project uses nothing the runtime lacks.</remarks>
    public bool FaultOnUnsupportedService { get; init; }
}

/// <summary>
/// Base of every generated controller: owns the tag store, the tasks with their programs, the system status and
/// the services; implements <see cref="IPlcController"/> for hosts and starts the EtherNet/IP tag server on demand.
/// </summary>
/// <remarks>
/// <para>
/// The generated subclass constructor registers the structure layouts, defines every tag in the store, binds the
/// wrapper fields, builds the program instances and the tasks in scan order. This base class then provides the
/// controller life cycle of a Logix processor: Program mode (stopped, tags readable and writable), the transition
/// to Run (prescan once, then the tasks start scanning), a major fault (every task stops, the fault text is
/// recorded, the controller must be cleared before it can run again), and the EtherNet/IP server that exposes the
/// tag image regardless of the mode.
/// </para>
/// <para>
/// Thread model: each task runs on its own thread; hosts and the EtherNet/IP server touch tag memory from their
/// own threads. Scalars are read and written atomically by the wrappers, structures may tear, and there is no
/// scan-boundary snapshot, which is the consistency model of a physical 1756 and keeps the hot path free of locks.
/// The lock in this class only guards the state transitions.
/// </para>
/// </remarks>
public abstract class PlcController : IPlcController, IAsyncDisposable
{
    private readonly object _gate = new();
    private EipServer? _server;
    private LogixServices? _services;

    /// <summary>The controller constructed most recently in this process: the ambient controller for function block instances nested inside structures.</summary>
    /// <remarks>A user type may contain an add-on instruction instance; the generated structure class has no controller reference to pass to it, so it reads this property. It is set first thing in the constructor, before any field is bound.</remarks>
    public static PlcController? Current { get; private set; }

    /// <summary>Creates the controller shell; the generated subclass fills in tags, programs and tasks.</summary>
    protected PlcController(string name, PlcControllerOptions? options = null)
    {
        Current = this;
        Name = name;
        Options = options ?? new PlcControllerOptions();
        Store = new TagStore(new TagDatabase());
        // Hundreds of thousands of writes per scan: no per-write events, clients read asynchronously.
        Store.Database.SuppressEvents = true;
        Sys = new SystemStatus(this);
        Statistics = new ScanStatistics();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The options the controller was built with.</summary>
    public PlcControllerOptions Options { get; }

    /// <summary>The tag memory: every tag of the project, program scopes, module images and the server templates.</summary>
    public TagStore Store { get; }

    /// <summary>Status the logic reads (first scan, scan counters, status bits, wall clock).</summary>
    public SystemStatus Sys { get; }

    /// <summary>The tasks in the order the generated constructor added them.</summary>
    public List<PlcTask> Tasks { get; } = new();

    /// <summary>Every program instance, whether scheduled in a task or not.</summary>
    public List<PlcProgram> Programs { get; } = new();

    /// <inheritdoc />
    public ControllerState State { get; private set; } = ControllerState.Stopped;

    /// <inheritdoc />
    public ControllerMode Mode { get; private set; } = ControllerMode.Program;

    /// <inheritdoc />
    public ScanStatistics Statistics { get; }

    /// <summary>The vendor service runtime; replace it to customise messaging, GSV objects and the like.</summary>
    public LogixServices Services
    {
        get => _services ??= CreateServices();
        set => _services = value;
    }

    /// <summary>Creates the default services; a derived controller can return a subclass with project-specific behaviour.</summary>
    protected virtual LogixServices CreateServices() => new(this);

    /// <summary>Prescan of every program, generated per controller; run once by <see cref="Start"/>.</summary>
    protected abstract void PrescanAll();

    /// <summary>Writes a runtime log line to the configured sink, or to the console prefixed with the controller name.</summary>
    public void Log(string message)
    {
        var sink = Options.Log;
        if (sink is not null) sink(message);
        else Console.WriteLine($"[{Name}] {message}");
    }

    // ------------------------------------------------------------------ run control

    /// <summary>
    /// Enters Run mode the way a Logix processor does: statistics are reset, the system status is reset so the
    /// first scan of each task sees S:FS, every program and add-on instance is prescanned once (outputs cleared,
    /// one-shot storages set, timers and counters put in their prescan state), and only then do the tasks start.
    /// A failure inside the prescan leaves the controller faulted without starting any task.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (State == ControllerState.Running) return;
            Statistics.Fault = null;
            Statistics.Tasks.Clear();
            foreach (var t in Tasks) Statistics.Tasks.Add(t.Statistics);
            Sys.Reset();
            Mode = ControllerMode.Run;
            try
            {
                PrescanAll();
            }
            catch (Exception ex)
            {
                Mode = ControllerMode.Program;
                State = ControllerState.Faulted;
                Statistics.Fault = "Prescan: " + ex.Message;
                Log(Statistics.Fault);
                return;
            }
            State = ControllerState.Running;
            Statistics.StartedAt = DateTimeOffset.Now;
            foreach (var t in Tasks) t.Start(this);
            Log($"Run ({Tasks.Count} task(s), {Programs.Count} program(s))");
        }
    }

    /// <summary>
    /// Leaves Run: every task thread is asked to stop and joined. Tag values are kept exactly as the last scan left
    /// them (Logix outputs stay at their last state when the processor goes to Program mode; de-energising real
    /// outputs is the I/O layer's business). A faulted controller stays faulted until <see cref="ClearFault"/>.
    /// </summary>
    public void Stop()
    {
        List<PlcTask> tasks;
        lock (_gate)
        {
            if (State == ControllerState.Stopped) return;
            tasks = Tasks.ToList();
        }
        // Joining task threads must happen outside the gate: a task that is faulting takes the gate itself.
        foreach (var t in tasks) t.Stop();
        lock (_gate)
        {
            if (State != ControllerState.Faulted) State = ControllerState.Stopped;
            Mode = ControllerMode.Program;
        }
        Log("Program mode");
    }

    /// <summary>Records a major fault: the controller leaves Run and the tasks stop.</summary>
    /// <remarks>
    /// Called from the faulting task's own thread (an exception escaped a program, a watchdog expired). The first
    /// few stack frames are logged because the fault text alone rarely says which rung failed. The other tasks are
    /// stopped from a pool thread because stopping joins their threads, and the current thread is one of them.
    /// </remarks>
    public void Fault(Exception exception)
    {
        lock (_gate)
        {
            if (State == ControllerState.Faulted) return;
            State = ControllerState.Faulted;
            Statistics.Fault = exception.Message;
            Mode = ControllerMode.Program;
        }
        Log("Major fault: " + exception.Message);
        var frames = (exception.StackTrace ?? "").Split('\n').Where(l => l.Contains(" at ")).Take(4).Select(l => l.Trim());
        foreach (var frame in frames) Log("  " + frame);
        // Stop the other tasks from a thread that is not one of them.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (var t in Tasks) t.Stop();
        });
    }

    /// <summary>Clears a fault so the controller can be started again.</summary>
    public void ClearFault()
    {
        lock (_gate)
        {
            if (State == ControllerState.Faulted) State = ControllerState.Stopped;
            Statistics.Fault = null;
        }
    }

    /// <summary>A task by name (case-insensitive, as GSV / EVENT name them), or null.</summary>
    public PlcTask? FindTask(string name) => Tasks.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A program instance by name (case-insensitive), or null.</summary>
    public PlcProgram? FindProgram(string name) => Programs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ EtherNet/IP

    /// <summary>Starts the tag server when the options ask for one (idempotent).</summary>
    /// <remarks>Separate from <see cref="Start"/> on purpose: the server serves tags in Program mode too, exactly like a real controller, and a host may want a controller without a listening socket (tests, batch runs).</remarks>
    public async Task StartServerAsync(CancellationToken ct = default)
    {
        if (Options.EipPort is not { } port || _server is not null) return;
        _server = new EipServer(Store.Database, new EipServerOptions
        {
            BindAddress = Options.BindAddress,
            Port = port,
            VendorId = Options.VendorId,
            ProductCode = Options.ProductCode,
            ProductName = Options.ProductName ?? Name,
        });
        await _server.StartAsync(ct);
        Log($"EtherNet/IP tag server listening on {Options.BindAddress}:{port}");
    }

    /// <summary>The running tag server, or null before <see cref="StartServerAsync"/>.</summary>
    public EipServer? Server => _server;

    // ------------------------------------------------------------------ IPlcController

    /// <inheritdoc />
    public IEnumerable<TagInfo> Tags(string? filter = null) => Store.Tags(filter);

    /// <inheritdoc />
    public bool TryRead(string path, out TagValueText? value) => Store.TryRead(path, out value);

    /// <inheritdoc />
    public bool TryWrite(string path, string text, out string? error) => Store.TryWrite(path, text, out error);

    /// <inheritdoc />
    public void SaveSnapshot(Stream stream) => Store.SaveSnapshot(stream);

    /// <inheritdoc />
    public void LoadSnapshot(Stream stream) => Store.LoadSnapshot(stream);

    /// <summary>Stops the tasks and closes the tag server.</summary>
    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Name and state, for diagnostics.</summary>
    public override string ToString() => $"{Name} ({State})";
}
