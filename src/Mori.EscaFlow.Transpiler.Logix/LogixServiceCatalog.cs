using System.Text.Json;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.Logix;

/// <summary>A Logix instruction as listed in the embedded <c>logix-services.json</c>, generated from Rockwell's instruction reference by <c>tools/gen-logix-catalog.sh</c>.</summary>
public sealed class LogixService
{
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";

    /// <summary>input | gate: how ladder binds the rung condition.</summary>
    public string Enable { get; set; } = "gate";
    public bool Variadic { get; set; }

    /// <summary>True when the front-end lowers the instruction to neutral nodes instead of emitting a sysCall. Kept in the catalog (rather than only in the front-end's lowering code) so the catalog remains the single complete list of every Logix instruction, lowered or not, for documentation and coverage checks.</summary>
    public bool Lowered { get; set; }
    public List<ServiceParam> Params { get; set; } = new();
}

/// <summary>
/// The Logix runtime service catalog (<c>sysCall.catalog = "logix"</c>): every Logix instruction with its operand
/// roles, loaded once from the embedded <c>logix-services.json</c> (255 services at the time the catalog was
/// generated, covering system, messaging, control, file, string, ASCII, phase, motion and safety instructions).
/// </summary>
public sealed class LogixServiceCatalog : IServiceCatalog
{
    /// <summary>Shape of the embedded JSON file.</summary>
    private sealed class File
    {
        public string Id { get; set; } = "logix";
        public List<LogixService> Services { get; set; } = new();
    }

    /// <summary>The catalog is a fixed, read-only table (not configuration), so one shared instance is enough; nothing needs a second copy.</summary>
    public static readonly LogixServiceCatalog Instance = new();

    private readonly Dictionary<string, LogixService> _byName;
    private readonly Dictionary<string, ServiceDefinition> _definitions;

    private LogixServiceCatalog()
    {
        using var stream = typeof(LogixServiceCatalog).Assembly.GetManifestResourceStream("Mori.EscaFlow.Transpiler.Logix.catalog.logix-services.json")
            ?? throw new InvalidOperationException("Embedded catalog logix-services.json is missing");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var file = JsonSerializer.Deserialize<File>(stream, options) ?? throw new InvalidOperationException("Empty logix-services.json");
        _byName = file.Services.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        // The validator only needs the IServiceCatalog shape (ServiceDefinition); LogixService carries Logix-specific
        // extras (Lowered) that the generic validator has no use for, so a parallel, narrower dictionary is built
        // alongside the full one rather than making ServiceDefinition itself carry vendor-specific fields.
        _definitions = file.Services.ToDictionary(s => s.Name, s => new ServiceDefinition
        {
            Name = s.Name,
            Group = s.Group,
            Enable = s.Enable,
            Variadic = s.Variadic,
            Params = s.Params,
        }, StringComparer.OrdinalIgnoreCase);
        Services = _definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
    }

    public string Id => LogixVendor.Id;
    public IReadOnlyList<ServiceDefinition> Services { get; }
    public ServiceDefinition? Get(string name) => _definitions.GetValueOrDefault(name);

    /// <summary>The full Logix-specific record for a service (including <see cref="LogixService.Lowered"/>), for converters that need more than the generic <see cref="IServiceCatalog"/> view.</summary>
    public LogixService? GetService(string name) => _byName.GetValueOrDefault(name);
    public IEnumerable<LogixService> All => _byName.Values;

    /// <summary>True for instructions that execute every scan with the rung condition as their enable (timers, counters, PID, alarms): these must observe a false condition to reset their state, unlike a gated instruction that simply does nothing when the rung is false.</summary>
    public bool RunsEveryScan(string mnemonic) => _byName.TryGetValue(mnemonic, out var s) && s.Enable == "input";

    /// <summary>The raw catalog JSON, for documentation and for non-C# consumers.</summary>
    public static string CatalogJson()
    {
        using var stream = typeof(LogixServiceCatalog).Assembly.GetManifestResourceStream("Mori.EscaFlow.Transpiler.Logix.catalog.logix-services.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
