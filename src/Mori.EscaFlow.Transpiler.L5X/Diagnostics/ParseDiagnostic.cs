namespace Mori.EscaFlow.Transpiler.L5X.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Where a diagnostic was raised. Every field is optional; fill what is known.</summary>
public sealed record DiagnosticLocation(
    int? XmlLine = null,
    string? ElementPath = null,
    string? Container = null,
    string? Routine = null,
    int? Rung = null,
    int? StLine = null,
    int? Column = null,
    string? Tag = null)
{
    public static readonly DiagnosticLocation None = new();

    public override string ToString()
    {
        var parts = new List<string>(6);
        if (Container is not null) parts.Add(Container);
        if (Routine is not null) parts.Add(Routine);
        if (Rung is not null) parts.Add($"rung {Rung}");
        if (StLine is not null) parts.Add(Column is not null ? $"line {StLine}:{Column}" : $"line {StLine}");
        if (Tag is not null) parts.Add($"tag {Tag}");
        if (ElementPath is not null) parts.Add(ElementPath);
        if (XmlLine is not null) parts.Add($"xml:{XmlLine}");
        return string.Join(" / ", parts);
    }
}

public sealed record ParseDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DiagnosticLocation Location)
{
    public override string ToString() => $"{Severity} {Code}: {Message} [{Location}]";
}
