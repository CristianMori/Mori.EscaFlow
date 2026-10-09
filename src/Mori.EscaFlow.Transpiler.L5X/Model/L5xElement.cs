using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>
/// Base of every model object that maps to an XML element. The readers consume the attributes and children they
/// understand and park the rest here, so nothing in an export is lost silently and a later version of the format
/// shows up as unknown content rather than as a parse failure.
/// </summary>
public abstract class L5xElement
{
    /// <summary>Line number of the element's start tag in the L5X file (0 when unknown).</summary>
    public int SourceLine { get; set; }

    /// <summary>Attributes present in the XML that no reader consumed. Null when there were none.</summary>
    public Dictionary<string, string>? UnknownAttributes { get; set; }

    /// <summary>Child elements the reader did not understand. Null when there were none.</summary>
    public List<GenericElement>? UnknownChildren { get; set; }

    [JsonIgnore]
    public bool HasUnknownContent => UnknownAttributes is { Count: > 0 } || UnknownChildren is { Count: > 0 };
}

/// <summary>
/// A verbatim copy of an XML subtree for parts of the format that are not modelled in detail (redundancy
/// settings, trends, e-keying, encoded routines, ...). Attributes and children are kept in file order.
/// </summary>
public sealed class GenericElement
{
    public required string Name { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();
    public List<GenericElement> Children { get; init; } = new();

    /// <summary>Text or CDATA content, with the surrounding CDATA newlines removed.</summary>
    public string? Text { get; set; }
    public int SourceLine { get; init; }

    /// <summary>An attribute value by name, or null.</summary>
    public string? this[string attribute] => Attributes.TryGetValue(attribute, out var v) ? v : null;

    /// <summary>The first child with the given element name (XML names are case-sensitive).</summary>
    public GenericElement? Child(string name) => Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public IEnumerable<GenericElement> ChildrenNamed(string name) => Children.Where(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public override string ToString() => $"<{Name}> ({Attributes.Count} attrs, {Children.Count} children)";
}
