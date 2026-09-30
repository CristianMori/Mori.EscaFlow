using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plc.Generator;

/// <summary>Everything a reader of the generated solution should know that is not visible in the code itself.</summary>
public sealed class GenerationReport
{
    public string Target { get; set; } = "";
    public string Generator { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }

    /// <summary>Alias variables and what they resolve to; every use in the generated code goes to the target.</summary>
    public List<AliasEntry> Aliases { get; } = new();

    /// <summary>Every identifier whose generated name differs from the source name.</summary>
    public List<NamingEntry> Naming { get; } = new();

    /// <summary>Vendor services used by the logic that the runtime does not implement (they compile to <c>Services.Unsupported</c>).</summary>
    public List<UnsupportedService> UnsupportedServices { get; } = new();

    /// <summary>Structure layouts computed by rule rather than taken from the source (vendor types with unknown packing).</summary>
    public List<string> LayoutNotes { get; } = new();

    /// <summary>Service usage counts (the instruction inventory).</summary>
    public SortedDictionary<string, int> ServiceInventory { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedDictionary<string, int> StandardBlockInventory { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedDictionary<string, int> FunctionInventory { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ReportMessage> Messages { get; } = new();

    [JsonIgnore]
    public bool HasErrors => Messages.Any(m => m.Severity == "error");

    public void Error(string code, string message, string? location = null) => Messages.Add(new ReportMessage("error", code, message, location));
    public void Warning(string code, string message, string? location = null) => Messages.Add(new ReportMessage("warning", code, message, location));
    public void Info(string code, string message, string? location = null) => Messages.Add(new ReportMessage("info", code, message, location));

    public void Count(SortedDictionary<string, int> inventory, string name) => inventory[name] = inventory.GetValueOrDefault(name) + 1;

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
}

public sealed record AliasEntry(string Alias, string Scope, string Target, string TargetType);

public sealed record NamingEntry(string Scope, string Source, string Generated, string Reason);

public sealed record UnsupportedService(string Catalog, string Service, int Uses, List<string> Locations);

public sealed record ReportMessage(string Severity, string Code, string Message, string? Location);
