namespace Plc.Ir;

/// <summary>
/// The middle file: a vendor-neutral, fully resolved and typed description of one PLC project.
/// Serialized as JSON by <see cref="IrJson"/>. See docs/plc-ir-2.0.md for the format contract.
/// </summary>
public sealed class IrDocument : IrNode
{
    public const string FormatId = "plc-ir";
    public const string CurrentVersion = "2.0";

    public string Format { get; set; } = FormatId;
    public string IrVersion { get; set; } = CurrentVersion;
    public IrSource Source { get; set; } = new();
    public IrConfiguration Configuration { get; set; } = new();

    /// <summary>User, vendor, I/O and library structure types referenced by the project. Atomic types are not listed; see <see cref="Catalogs.AtomicTypes"/>.</summary>
    public List<IrType> Types { get; set; } = new();

    /// <summary>Global variable lists.</summary>
    public List<IrGlobalVariableList> Globals { get; set; } = new();

    public IrIo Io { get; set; } = new();

    /// <summary>Program organisation units: programs, function blocks, functions, interfaces.</summary>
    public List<IrPou> Pous { get; set; } = new();

    public List<IrDiagnostic> Diagnostics { get; set; } = new();

    // ------------------------------------------------------------------ lookups (not serialized)

    private Dictionary<string, IrType>? _types;
    private Dictionary<string, IrVariable>? _variables;
    private Dictionary<string, IrPou>? _pous;
    private Dictionary<string, IrIoChannel>? _channels;

    /// <summary>Drops the lookup caches after the document was modified.</summary>
    public void InvalidateIndex()
    {
        _types = null;
        _variables = null;
        _pous = null;
        _channels = null;
    }

    public IrType? FindType(string name)
    {
        _types ??= BuildIndex(Types, t => t.Name, StringComparer.OrdinalIgnoreCase);
        return _types.GetValueOrDefault(name);
    }

    public IrVariable? FindVariable(string id)
    {
        _variables ??= BuildIndex(AllVariables(), v => v.Id, StringComparer.Ordinal);
        return _variables.GetValueOrDefault(id);
    }

    public IrPou? FindPou(string name)
    {
        _pous ??= BuildIndex(Pous, p => p.Name, StringComparer.OrdinalIgnoreCase);
        return _pous.GetValueOrDefault(name);
    }

    public IrIoChannel? FindChannel(string id)
    {
        _channels ??= BuildIndex(Io.Channels, c => c.Id, StringComparer.Ordinal);
        return _channels.GetValueOrDefault(id);
    }

    /// <summary>Every variable of every global list and every POU (including methods), in document order.</summary>
    public IEnumerable<IrVariable> AllVariables()
    {
        foreach (var g in Globals)
            foreach (var v in g.Vars) yield return v;
        foreach (var p in Pous)
        {
            foreach (var v in p.Vars) yield return v;
            foreach (var m in p.Methods)
                foreach (var v in m.Vars) yield return v;
        }
    }

    private static Dictionary<string, T> BuildIndex<T>(IEnumerable<T> items, Func<T, string> key, StringComparer comparer)
    {
        var d = new Dictionary<string, T>(comparer);
        foreach (var item in items) d.TryAdd(key(item), item);
        return d;
    }
}

public sealed class IrSource
{
    /// <summary>Source vendor id: logix | s7 | twincat | codesys | ...</summary>
    public string Vendor { get; set; } = "";

    /// <summary>Source dialect or file family, e.g. <c>l5x</c>, <c>tia-openness</c>, <c>plcopen-xml</c>.</summary>
    public string? Dialect { get; set; }
    public string? File { get; set; }
    public string? ExportDate { get; set; }
    public string? SoftwareRevision { get; set; }
    public string? Generator { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

public sealed class IrDiagnostic
{
    [IrEnum("info", "warning", "error")]
    public string Severity { get; set; } = "info";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Location { get; set; }
}
