using System.Text.Json;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.Logix;

/// <summary>A Logix instruction as listed in the embedded <c>logix-services.json</c>.</summary>
public sealed class LogixService
{
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";

    /// <summary>input | gate: how ladder binds the rung condition.</summary>
    public string Enable { get; set; } = "gate";
    public bool Variadic { get; set; }

    /// <summary>True when the front-end lowers the instruction to neutral nodes instead of emitting a sysCall.</summary>
    public bool Lowered { get; set; }
    public List<ServiceParam> Params { get; set; } = new();
}

/// <summary>The Logix runtime service catalog (<c>sysCall.catalog = "logix"</c>): every Logix instruction with its operand roles.</summary>
public sealed class LogixServiceCatalog : IServiceCatalog
{
    private sealed class File
    {
        public string Id { get; set; } = "logix";
        public List<LogixService> Services { get; set; } = new();
    }

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
    public LogixService? GetService(string name) => _byName.GetValueOrDefault(name);
    public IEnumerable<LogixService> All => _byName.Values;

    /// <summary>True for instructions that execute every scan with the rung condition as their enable.</summary>
    public bool RunsEveryScan(string mnemonic) => _byName.TryGetValue(mnemonic, out var s) && s.Enable == "input";

    public static string CatalogJson()
    {
        using var stream = typeof(LogixServiceCatalog).Assembly.GetManifestResourceStream("Mori.EscaFlow.Transpiler.Logix.catalog.logix-services.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
