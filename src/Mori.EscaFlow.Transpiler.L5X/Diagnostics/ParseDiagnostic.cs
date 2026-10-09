namespace Mori.EscaFlow.Transpiler.L5X.Diagnostics;

/// <summary>
/// Info: something worth knowing that does not affect the model (a cross-check, an unknown attribute kept verbatim).
/// Warning: the model is complete but a construct was interpreted loosely. Error: part of the content could not be
/// represented (an unparsable rung, an unresolved type); the rest of the project is still usable.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Where a diagnostic was raised. Every field is optional; fill what is known.</summary>
/// <param name="XmlLine">Line of the XML element in the export.</param>
/// <param name="ElementPath">Slash-separated element names from the controller down, when the XML phase raised it.</param>
/// <param name="Container">Program or add-on instruction definition.</param>
/// <param name="Routine">Routine within the container.</param>
/// <param name="Rung">Rung number within a ladder routine.</param>
/// <param name="StLine">Zero-based line within a structured text body.</param>
/// <param name="Column">One-based column within that line.</param>
/// <param name="Tag">Tag (or SFC element operand) the diagnostic is about.</param>
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

    /// <summary>Human-readable form, most specific part last (e.g. <c>Cell / Main / rung 12 / xml:3851531</c>).</summary>
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

/// <summary>One parser finding. <paramref name="Code"/> is a stable identifier (family prefix plus number) that tests and tools filter on.</summary>
public sealed record ParseDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DiagnosticLocation Location)
{
    public override string ToString() => $"{Severity} {Code}: {Message} [{Location}]";
}
