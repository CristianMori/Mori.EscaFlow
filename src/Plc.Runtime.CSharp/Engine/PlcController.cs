using System.Net;
using EthernetIPSharp.Logix;

namespace Plc.Runtime;

public sealed class PlcControllerOptions
{
    /// <summary>TCP port of the EtherNet/IP tag server; null disables the server.</summary>
    public int? EipPort { get; init; } = 44818;

    /// <summary>Address to bind the tag server to (all interfaces by default).</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>Vendor id, product code and name presented to EtherNet/IP clients.</summary>
    public ushort VendorId { get; init; } = 1;
    public ushort ProductCode { get; init; } = 0x5C;
    public string? ProductName { get; init; }

    /// <summary>Receives runtime log lines (defaults to the console).</summary>
    public Action<string>? Log { get; init; }

    /// <summary>When true, a call to a vendor service the runtime does not implement faults the controller; when false (default) it is logged once per call site and skipped.</summary>
    public bool FaultOnUnsupportedService { get; init; }
}

/// <summary>
/// Base of every generated controller: owns the tag store, the tasks with their programs, the system status and
/// the services; implements <see cref="IPlcController"/> for hosts and starts the EtherNet/IP tag server on demand.
/// </summary>
public abstract class PlcController : IPlcController, IAsyncDisposable
{
    private readonly object _gate = new();
    private EipServer? _server;
    private LogixServices? _services;

    /// <summary>The controller constructed most recently in this process: the ambient controller for function block instances nested inside structures.</summary>
    public static PlcController? Current { get; private set; }

    protected PlcController(string name, PlcControllerOptions? options = null)
    {
        Current = this;
        Name = name;
        Options = options ?? new PlcControllerOptions();
        Store = new TagStore(new TagDatabase());
        Store.Database.SuppressEvents = true;
        Sys = new SystemStatus(this);
        Statistics = new ScanStatistics();
    }

    public string Name { get; }
    public PlcControllerOptions Options { get; }
    public TagStore Store { get; }
    public SystemStatus Sys { get; }
    public List<PlcTask> Tasks { get; } = new();
    public List<PlcProgram> Programs { get; } = new();
    public ControllerState State { get; private set; } = ControllerState.Stopped;
    public ControllerMode Mode { get; private set; } = ControllerMode.Program;
    public ScanStatistics Statistics { get; }

    /// <summary>The vendor service runtime; replace it to customise messaging, GSV objects and the like.</summary>
    public LogixServices Services
    {
        get => _services ??= CreateServices();
        set => _services = value;
    }

    protected virtual LogixServices CreateServices() => new(this);

    /// <summary>Prescan of every program, generated per controller; run once by <see cref="Start"/>.</summary>
    protected abstract void PrescanAll();

    public void Log(string message)
    {
        var sink = Options.Log;
        if (sink is not null) sink(message);
        else Console.WriteLine($"[{Name}] {message}");
    }

    // ------------------------------------------------------------------ run control

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

    public void Stop()
    {
        List<PlcTask> tasks;
        lock (_gate)
        {
            if (State == ControllerState.Stopped) return;
            tasks = Tasks.ToList();
        }
        foreach (var t in tasks) t.Stop();
        lock (_gate)
        {
            if (State != ControllerState.Faulted) State = ControllerState.Stopped;
            Mode = ControllerMode.Program;
        }
        Log("Program mode");
    }

    /// <summary>Records a major fault: the controller leaves Run and the tasks stop.</summary>
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

    public PlcTask? FindTask(string name) => Tasks.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public PlcProgram? FindProgram(string name) => Programs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ EtherNet/IP

    /// <summary>Starts the tag server when the options ask for one (idempotent).</summary>
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

    public EipServer? Server => _server;

    // ------------------------------------------------------------------ IPlcController

    public IEnumerable<TagInfo> Tags(string? filter = null) => Store.Tags(filter);
    public bool TryRead(string path, out TagValueText? value) => Store.TryRead(path, out value);
    public bool TryWrite(string path, string text, out string? error) => Store.TryWrite(path, text, out error);
    public void SaveSnapshot(Stream stream) => Store.SaveSnapshot(stream);
    public void LoadSnapshot(Stream stream) => Store.LoadSnapshot(stream);

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

    public override string ToString() => $"{Name} ({State})";
}
