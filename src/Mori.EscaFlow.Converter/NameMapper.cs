using System.Text;

namespace Mori.EscaFlow.Converter;

/// <summary>
/// Maps source identifiers to target identifiers per scope, keeping names as written where legal, appending an
/// underscore on keyword collisions, resolving case-only duplicates deterministically and recording every change.
/// </summary>
public sealed class NameMapper
{
    private readonly HashSet<string> _keywords;
    private readonly bool _caseSensitiveTarget;
    private readonly Dictionary<string, Dictionary<string, string>> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _taken = new(StringComparer.Ordinal);
    private readonly GenerationReport _report;
    private readonly bool _caseInsensitiveSources;

    /// <param name="caseInsensitiveSources">True when the source language is case-insensitive (Logix): <c>Flags</c> and <c>FLAGS</c> are the same identifier and map to one target name.</param>
    public NameMapper(IEnumerable<string> keywords, bool caseSensitiveTarget, GenerationReport report, bool caseInsensitiveSources = true)
    {
        _keywords = new HashSet<string>(keywords, StringComparer.Ordinal);
        _caseSensitiveTarget = caseSensitiveTarget;
        _report = report;
        _caseInsensitiveSources = caseInsensitiveSources;
    }

    /// <summary>The target name for <paramref name="source"/> in <paramref name="scope"/>, allocating it on first use.</summary>
    public string Map(string scope, string source, string? reserved = null)
    {
        if (!_scopes.TryGetValue(scope, out var map))
        {
            map = new Dictionary<string, string>(_caseInsensitiveSources ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            _scopes[scope] = map;
            _taken[scope] = new HashSet<string>(_caseSensitiveTarget ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            if (reserved is not null) _taken[scope].Add(reserved);
        }
        if (map.TryGetValue(source, out var existing)) return existing;

        var sanitized = Sanitize(source, out var reason);
        var candidate = sanitized;
        var taken = _taken[scope];
        if (_keywords.Contains(candidate))
        {
            candidate += "_";
            reason ??= "keyword";
        }
        var n = 1;
        while (taken.Contains(candidate))
        {
            candidate = sanitized + "_" + (++n);
            reason ??= "duplicate";
        }
        taken.Add(candidate);
        map[source] = candidate;
        if (candidate != source) _report.Naming.Add(new NamingEntry(scope, source, candidate, reason ?? "changed"));
        return candidate;
    }

    /// <summary>Reserves a target name in a scope (members inherited from a base class, generated helpers).</summary>
    public void Reserve(string scope, string name)
    {
        if (!_scopes.ContainsKey(scope)) Map(scope, "\u0000" + name); // creates the scope
        _taken[scope].Add(name);
    }

    private static string Sanitize(string source, out string? reason)
    {
        reason = null;
        var sb = new StringBuilder(source.Length + 1);
        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else
            {
                sb.Append('_');
                reason = "characters";
            }
        }
        if (sb.Length == 0) { sb.Append('_'); reason = "empty"; }
        if (char.IsDigit(sb[0])) { sb.Insert(0, '_'); reason = "leading digit"; }
        return sb.ToString();
    }

    public static readonly string[] CSharpKeywords =
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "record", "var", "dynamic", "async", "await", "value", "nameof",
    };
}
