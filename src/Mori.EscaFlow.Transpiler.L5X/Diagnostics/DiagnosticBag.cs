using System.Collections;

namespace Mori.EscaFlow.Transpiler.L5X.Diagnostics;

/// <summary>Receives diagnostics from a parser. Implementations are not required to be thread-safe.</summary>
public interface IDiagnosticSink
{
    void Add(ParseDiagnostic diagnostic);
}

public static class DiagnosticSinkExtensions
{
    public static void Info(this IDiagnosticSink sink, string code, string message, DiagnosticLocation? location = null)
        => sink.Add(new ParseDiagnostic(DiagnosticSeverity.Info, code, message, location ?? DiagnosticLocation.None));

    public static void Warning(this IDiagnosticSink sink, string code, string message, DiagnosticLocation? location = null)
        => sink.Add(new ParseDiagnostic(DiagnosticSeverity.Warning, code, message, location ?? DiagnosticLocation.None));

    public static void Error(this IDiagnosticSink sink, string code, string message, DiagnosticLocation? location = null)
        => sink.Add(new ParseDiagnostic(DiagnosticSeverity.Error, code, message, location ?? DiagnosticLocation.None));
}

/// <summary>A simple list-backed diagnostic collection.</summary>
public sealed class DiagnosticBag : IDiagnosticSink, IReadOnlyList<ParseDiagnostic>
{
    private readonly List<ParseDiagnostic> _items = new();

    public int Count => _items.Count;

    public ParseDiagnostic this[int index] => _items[index];

    public void Add(ParseDiagnostic diagnostic) => _items.Add(diagnostic);

    public void AddRange(IEnumerable<ParseDiagnostic> diagnostics) => _items.AddRange(diagnostics);

    public int CountBySeverity(DiagnosticSeverity severity) => _items.Count(d => d.Severity == severity);

    public int CountByCodePrefix(string prefix) => _items.Count(d => d.Code.StartsWith(prefix, StringComparison.Ordinal));

    public bool HasErrors => _items.Any(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerator<ParseDiagnostic> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A sink that stamps a fixed location context (container / routine / rung) onto every diagnostic it forwards.
/// Used by the logic parsers, which only know the column or offset within their own text.
/// </summary>
public sealed class ScopedDiagnosticSink : IDiagnosticSink
{
    private readonly IDiagnosticSink _inner;
    private readonly DiagnosticLocation _context;

    public ScopedDiagnosticSink(IDiagnosticSink inner, DiagnosticLocation context)
    {
        _inner = inner;
        _context = context;
    }

    public void Add(ParseDiagnostic diagnostic)
    {
        var loc = diagnostic.Location;
        var merged = _context with
        {
            XmlLine = loc.XmlLine ?? _context.XmlLine,
            ElementPath = loc.ElementPath ?? _context.ElementPath,
            Rung = loc.Rung ?? _context.Rung,
            StLine = loc.StLine ?? _context.StLine,
            Column = loc.Column ?? _context.Column,
            Tag = loc.Tag ?? _context.Tag,
        };
        _inner.Add(diagnostic with { Location = merged });
    }
}
