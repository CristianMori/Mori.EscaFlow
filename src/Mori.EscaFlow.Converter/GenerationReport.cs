using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Converter;

/// <summary>Everything a reader of the generated solution should know that is not visible in the code itself.</summary>
/// <remarks>
/// The report is written next to the generated solution as <c>generation-report.json</c>. It is the place where a
/// converter is honest about what it changed (renamed identifiers, resolved aliases), what it approximated (structure
/// layouts computed by rule) and what it could not do (services the runtime lacks). Nothing is dropped silently: a
/// construct the target cannot execute must leave a trace here.
/// </remarks>
public sealed class GenerationReport
{
    /// <summary>Target id of the generator that produced the output.</summary>
    public string Target { get; set; } = "";

    /// <summary>Generator name and version, also written into the file headers.</summary>
    public string Generator { get; set; } = "";

    /// <summary>When the output was produced (UTC).</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The source file the document came from, when known.</summary>
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

    /// <summary>Standard function block usage counts (TON, CTU, ...).</summary>
    public SortedDictionary<string, int> StandardBlockInventory { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Standard function usage counts (ABS, CONCAT, ...).</summary>
    public SortedDictionary<string, int> FunctionInventory { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Errors, warnings and notes raised while generating, with their location in the source project.</summary>
    public List<ReportMessage> Messages { get; } = new();

    /// <summary>True when at least one error was raised; the command line tool then exits with a failure code.</summary>
    [JsonIgnore]
    public bool HasErrors => Messages.Any(m => m.Severity == "error");

    public void Error(string code, string message, string? location = null) => Messages.Add(new ReportMessage("error", code, message, location));
    public void Warning(string code, string message, string? location = null) => Messages.Add(new ReportMessage("warning", code, message, location));
    public void Info(string code, string message, string? location = null) => Messages.Add(new ReportMessage("info", code, message, location));

    /// <summary>Increments one entry of an inventory.</summary>
    public void Count(SortedDictionary<string, int> inventory, string name) => inventory[name] = inventory.GetValueOrDefault(name) + 1;

    /// <summary>The report as indented camelCase JSON, the form written next to the generated solution.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
}

/// <summary>An alias variable (by id), the section it lives in, the target path as written in the source, and the target's type.</summary>
public sealed record AliasEntry(string Alias, string Scope, string Target, string TargetType);

/// <summary>A renamed identifier: the naming scope, the source spelling, the generated spelling and why it changed (keyword, duplicate, characters, ...).</summary>
public sealed record NamingEntry(string Scope, string Source, string Generated, string Reason);

/// <summary>A vendor service the runtime lacks: its catalog, name, number of uses and up to twenty locations.</summary>
public sealed record UnsupportedService(string Catalog, string Service, int Uses, List<string> Locations);

/// <summary>A generation message with a stable code (GENnnn) for filtering.</summary>
public sealed record ReportMessage(string Severity, string Code, string Message, string? Location);
