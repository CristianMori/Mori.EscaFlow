using System.Text;

namespace Mori.EscaFlow.Converter;

/// <summary>
/// Maps source identifiers to target identifiers per scope, keeping names as written where legal, appending an
/// underscore on keyword collisions, resolving case-only duplicates deterministically and recording every change.
/// </summary>
/// <remarks>
/// <para>
/// A scope is the set of names that must be distinct in one generated declaration space: the members of one class,
/// the fields of the controller, the type names of the namespace. Scopes are independent, so a program tag and a
/// structure member may both be called <c>Timer</c>.
/// </para>
/// <para>
/// Logix identifiers are case-insensitive while C# identifiers are not, so a scope can look up source names
/// case-insensitively (<c>Flags</c> and <c>FLAGS</c> are the same tag) while guaranteeing the generated names are
/// distinct under the target's rules. The first spelling seen wins; converters therefore map declarations before
/// uses whenever they can, and the report lists every name that changed.
/// </para>
/// </remarks>
public sealed class NameMapper
{
    /// <summary>Reserved words of the target language; a source name equal to one gets a trailing underscore.</summary>
    private readonly HashSet<string> _keywords;

    /// <summary>True when the target distinguishes names by case (C#); false when case-only duplicates would collide.</summary>
    private readonly bool _caseSensitiveTarget;

    /// <summary>Per scope: source name to generated name.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _scopes = new(StringComparer.Ordinal);

    /// <summary>Per scope: every generated name already handed out or reserved.</summary>
    private readonly Dictionary<string, HashSet<string>> _taken = new(StringComparer.Ordinal);
    private readonly GenerationReport _report;
    private readonly bool _caseInsensitiveSources;

    /// <param name="keywords">Reserved words of the target language.</param>
    /// <param name="caseSensitiveTarget">Whether the target language distinguishes identifiers by case.</param>
    /// <param name="report">Receives one <see cref="NamingEntry"/> per changed name.</param>
    /// <param name="caseInsensitiveSources">True when the source language is case-insensitive (Logix): <c>Flags</c> and <c>FLAGS</c> are the same identifier and map to one target name.</param>
    public NameMapper(IEnumerable<string> keywords, bool caseSensitiveTarget, GenerationReport report, bool caseInsensitiveSources = true)
    {
        _keywords = new HashSet<string>(keywords, StringComparer.Ordinal);
        _caseSensitiveTarget = caseSensitiveTarget;
        _report = report;
        _caseInsensitiveSources = caseInsensitiveSources;
    }

    /// <summary>The target name for <paramref name="source"/> in <paramref name="scope"/>, allocating it on first use.</summary>
    /// <param name="reserved">A name to reserve when the scope is created (the enclosing class name, for instance).</param>
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

        // Sanitize first (illegal characters, leading digit), then dodge keywords, then dodge names already taken in
        // the scope by appending a counter; the first reason that forced a change is what the report records.
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
    /// <remarks>A source identifier equal to a reserved name is then renamed instead of colliding with the runtime's member.</remarks>
    public void Reserve(string scope, string name)
    {
        if (!_scopes.ContainsKey(scope)) Map(scope, "\u0000" + name); // creates the scope
        _taken[scope].Add(name);
    }

    /// <summary>Replaces characters that are not letters, digits or underscores (Logix allows none, but module image names contain colons) and guards against a leading digit.</summary>
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

    /// <summary>C# reserved and contextual words that cannot be used as plain identifiers in generated code.</summary>
    public static readonly string[] CSharpKeywords =
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "record", "var", "dynamic", "async", "await", "value", "nameof",
    };
}
