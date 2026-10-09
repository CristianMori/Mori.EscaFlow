namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// The middle file: a vendor-neutral, fully resolved and typed description of one PLC project.
/// Serialized as JSON by <see cref="IrJson"/>. See docs/plc-ir-2.0.md for the format contract.
/// </summary>
/// <remarks>
/// <para>
/// "Fully resolved" means every reference in logic carries the id of the declaring variable (aliases already
/// expanded), every call names an existing POU, action or catalog service, and every expression carries its
/// result type. A converter therefore never has to search scopes or infer types; it only translates.
/// </para>
/// <para>
/// The document is versioned by <see cref="IrVersion"/> (<c>major.minor</c>). Readers accept any document of
/// the same major version and ignore unknown members, so minor additions never break existing consumers.
/// </para>
/// </remarks>
public sealed class IrDocument : IrNode
{
    /// <summary>The value of <see cref="Format"/> that identifies a plc-ir document.</summary>
    public const string FormatId = "plc-ir";

    /// <summary>The format version written by this library.</summary>
    public const string CurrentVersion = "2.0";

    /// <summary>Always <see cref="FormatId"/>; lets a reader reject unrelated JSON early.</summary>
    public string Format { get; set; } = FormatId;

    /// <summary><c>major.minor</c>; readers accept the same major.</summary>
    public string IrVersion { get; set; } = CurrentVersion;

    /// <summary>Where the document came from (vendor, dialect, file, export metadata).</summary>
    public IrSource Source { get; set; } = new();

    /// <summary>The IEC configuration: resources, tasks and program instances.</summary>
    public IrConfiguration Configuration { get; set; } = new();

    /// <summary>User, vendor, I/O and library structure types referenced by the project. Atomic types are not listed; see <see cref="Catalogs.AtomicTypes"/>.</summary>
    public List<IrType> Types { get; set; } = new();

    /// <summary>Global variable lists (IEC VAR_GLOBAL blocks). A Logix import produces one list per controller plus a synthetic <c>IO</c> list of module images.</summary>
    public List<IrGlobalVariableList> Globals { get; set; } = new();

    /// <summary>The hardware tree and its data channels.</summary>
    public IrIo Io { get; set; } = new();

    /// <summary>Program organisation units: programs, function blocks, functions, interfaces.</summary>
    public List<IrPou> Pous { get; set; } = new();

    /// <summary>Front-end diagnostics worth carrying to the converter (unresolved references, opaque types, unsupported constructs).</summary>
    public List<IrDiagnostic> Diagnostics { get; set; } = new();

    // ------------------------------------------------------------------ lookups (not serialized)

    // Lookup tables are built on first use because documents are often built incrementally and read many times:
    // the importer fills the lists, converters and validators query them millions of times.
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

    /// <summary>The type named <paramref name="name"/>, case-insensitively (PLC identifiers are case-insensitive), or null.</summary>
    public IrType? FindType(string name)
    {
        _types ??= BuildIndex(Types, t => t.Name, StringComparer.OrdinalIgnoreCase);
        return _types.GetValueOrDefault(name);
    }

    /// <summary>The variable with the given id (<c>global/&lt;list&gt;/&lt;name&gt;</c>, <c>pou/&lt;pou&gt;/&lt;name&gt;</c>), or null. Ids are case-sensitive because they are generated, not written by users.</summary>
    public IrVariable? FindVariable(string id)
    {
        _variables ??= BuildIndex(AllVariables(), v => v.Id, StringComparer.Ordinal);
        return _variables.GetValueOrDefault(id);
    }

    /// <summary>The POU named <paramref name="name"/>, case-insensitively, or null.</summary>
    public IrPou? FindPou(string name)
    {
        _pous ??= BuildIndex(Pous, p => p.Name, StringComparer.OrdinalIgnoreCase);
        return _pous.GetValueOrDefault(name);
    }

    /// <summary>The I/O channel with the given id (<c>io/&lt;device&gt;/&lt;name&gt;</c>), or null.</summary>
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

    /// <summary>Builds a lookup; on duplicate keys the first occurrence wins so a malformed document still indexes (the validator reports the duplicate).</summary>
    private static Dictionary<string, T> BuildIndex<T>(IEnumerable<T> items, Func<T, string> key, StringComparer comparer)
    {
        var d = new Dictionary<string, T>(comparer);
        foreach (var item in items) d.TryAdd(key(item), item);
        return d;
    }
}

/// <summary>Provenance of the whole document: which vendor and file it was imported from and by what.</summary>
public sealed class IrSource
{
    /// <summary>Source vendor id: logix | s7 | twincat | codesys | ...</summary>
    public string Vendor { get; set; } = "";

    /// <summary>Source dialect or file family, e.g. <c>l5x</c>, <c>tia-openness</c>, <c>plcopen-xml</c>.</summary>
    public string? Dialect { get; set; }

    /// <summary>File name of the export (no directory, to keep the document portable).</summary>
    public string? File { get; set; }

    /// <summary>Export timestamp as the vendor wrote it.</summary>
    public string? ExportDate { get; set; }

    /// <summary>Version of the engineering software that produced the export.</summary>
    public string? SoftwareRevision { get; set; }

    /// <summary>Name and version of the front-end that built the document.</summary>
    public string? Generator { get; set; }

    /// <summary>When the document was built.</summary>
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>A diagnostic carried from the front-end into the document, so a converter can show why something is missing or approximate.</summary>
public sealed class IrDiagnostic
{
    /// <summary>info | warning | error</summary>
    [IrEnum("info", "warning", "error")]
    public string Severity { get; set; } = "info";

    /// <summary>Stable code (parser codes such as <c>LNK002</c>, importer codes such as <c>IRB001</c>).</summary>
    public string Code { get; set; } = "";

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; set; } = "";

    /// <summary>Where in the source project it applies.</summary>
    public string? Location { get; set; }
}
