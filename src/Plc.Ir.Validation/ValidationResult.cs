using System.Text;
using Plc.Ir.Catalogs;

namespace Plc.Ir.Validation;

public sealed class ValidationIssue
{
    public ValidationIssue(string severity, string code, string message, string location)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Location = location;
    }

    /// <summary>error | warning | info</summary>
    public string Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string Location { get; }

    public override string ToString() => $"{Severity} {Code}: {Message} [{Location}]";
}

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = new();

    public bool HasErrors => Issues.Any(i => i.Severity == "error");
    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == "error");
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == "warning");

    public void Add(string severity, string code, string message, string location) => Issues.Add(new ValidationIssue(severity, code, message, location));
    public void Error(string code, string message, string location) => Add("error", code, message, location);
    public void Warning(string code, string message, string location) => Add("warning", code, message, location);
    public void Info(string code, string message, string location) => Add("info", code, message, location);

    public IEnumerable<IrDiagnostic> ToDiagnostics()
        => Issues.Select(i => new IrDiagnostic { Severity = i.Severity, Code = i.Code, Message = i.Message, Location = i.Location });

    public override string ToString()
    {
        var errors = Issues.Count(i => i.Severity == "error");
        var warnings = Issues.Count(i => i.Severity == "warning");
        var infos = Issues.Count - errors - warnings;
        var sb = new StringBuilder();
        sb.Append(errors).Append(" error(s), ").Append(warnings).Append(" warning(s), ").Append(infos).Append(" info");
        foreach (var group in Issues.GroupBy(i => i.Code).OrderBy(g => g.Key))
            sb.Append('\n').Append("  ").Append(group.Key).Append(" x").Append(group.Count()).Append(": ").Append(ValidationCodes.Describe(group.Key));
        return sb.ToString();
    }
}

public sealed class ValidationOptions
{
    /// <summary>Vendor service catalogs used to check <c>sysCall</c> / <c>sysFn</c> nodes. A catalog id that is not registered yields a warning (or nothing) per <see cref="TreatUnknownServiceCatalogAsWarning"/>.</summary>
    public List<IServiceCatalog> ServiceCatalogs { get; init; } = new();
    public bool TreatUnknownServiceCatalogAsWarning { get; init; } = true;
    public bool CheckTyping { get; init; } = true;
}
