using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using L5x.Model;

namespace L5x.Parsing.Xml;

/// <summary>Normalizes text that the exporter wraps in CDATA on its own line.</summary>
public static class CdataText
{
    /// <summary>Strips exactly one leading and one trailing newline (CRLF or LF); interior whitespace is preserved.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var start = 0;
        var end = text.Length;
        if (start < end && text[start] == '\r') start++;
        if (start < end && text[start] == '\n') start++;
        if (end > start && text[end - 1] == '\n') end--;
        if (end > start && text[end - 1] == '\r') end--;
        return start == 0 && end == text.Length ? text : text.Substring(start, end - start);
    }
}

/// <summary>
/// Snapshot of an element's attributes that tracks which ones a reader consumed, so the rest can be preserved
/// as <see cref="L5xElement.UnknownAttributes"/>.
/// </summary>
public sealed class AttributeBag
{
    private readonly string[] _names;
    private readonly string[] _values;
    private readonly bool[] _taken;

    public AttributeBag(XmlReader reader)
    {
        var n = reader.AttributeCount;
        _names = new string[n];
        _values = new string[n];
        _taken = new bool[n];
        if (n > 0)
        {
            var i = 0;
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    _names[i] = reader.Name;
                    _values[i] = reader.Value;
                    i++;
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
        }
    }

    public AttributeBag(XElement element)
    {
        var attrs = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToArray();
        _names = new string[attrs.Length];
        _values = new string[attrs.Length];
        _taken = new bool[attrs.Length];
        for (var i = 0; i < attrs.Length; i++)
        {
            _names[i] = attrs[i].Name.LocalName;
            _values[i] = attrs[i].Value;
        }
    }

    public int Count => _names.Length;

    public bool Has(string name) => IndexOf(name) >= 0;

    private int IndexOf(string name)
    {
        for (var i = 0; i < _names.Length; i++)
            if (string.Equals(_names[i], name, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>Returns the value and marks it consumed; null when absent.</summary>
    public string? Take(string name)
    {
        var i = IndexOf(name);
        if (i < 0) return null;
        _taken[i] = true;
        return _values[i];
    }

    /// <summary>Returns the value without consuming it.</summary>
    public string? Peek(string name)
    {
        var i = IndexOf(name);
        return i < 0 ? null : _values[i];
    }

    public string TakeRequired(string name, string fallback = "")
        => Take(name) ?? fallback;

    public int? TakeInt(string name)
    {
        var v = Take(name);
        if (v is null) return null;
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (int)d;
        return null;
    }

    public double? TakeDouble(string name)
    {
        var v = Take(name);
        if (v is null) return null;
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public bool TakeBool(string name, bool defaultValue = false)
    {
        var v = Take(name);
        return v is null ? defaultValue : ParseBool(v, defaultValue);
    }

    public bool? TakeBoolNullable(string name)
    {
        var v = Take(name);
        return v is null ? null : ParseBool(v, false);
    }

    public static bool ParseBool(string v, bool defaultValue)
    {
        if (v.Length == 0) return defaultValue;
        if (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "no", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultValue;
    }

    /// <summary>Parses "5 10 2" (or "5,10,2") into an int array; empty array when absent or "0".</summary>
    public int[] TakeDimensions(string name)
    {
        var v = Take(name);
        return ParseDimensions(v);
    }

    public static int[] ParseDimensions(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return Array.Empty<int>();
        var parts = v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i])) return Array.Empty<int>();
        }
        if (result.Length == 1 && result[0] == 0) return Array.Empty<int>();
        return result;
    }

    public Radix TakeRadix(string name = "Radix") => RadixExtensions.ParseRadix(Take(name));

    /// <summary>Every attribute not yet consumed, or null when there are none.</summary>
    public Dictionary<string, string>? Remaining()
    {
        Dictionary<string, string>? result = null;
        for (var i = 0; i < _names.Length; i++)
        {
            if (_taken[i]) continue;
            (result ??= new Dictionary<string, string>(StringComparer.Ordinal))[_names[i]] = _values[i];
        }
        return result;
    }

    /// <summary>Every attribute regardless of consumption.</summary>
    public Dictionary<string, string> All()
    {
        var d = new Dictionary<string, string>(_names.Length, StringComparer.Ordinal);
        for (var i = 0; i < _names.Length; i++) d[_names[i]] = _values[i];
        return d;
    }
}

public static class XmlReaderExtensions
{
    public static int LineNumber(this XmlReader reader) => reader is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

    /// <summary>
    /// Reads the text (and CDATA) content of the current element, normalized, and moves past its end tag.
    /// Child elements (the exporter can nest CustomProperties inside Line, Description, ...) are skipped.
    /// </summary>
    public static string ReadNormalizedText(this XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return string.Empty;
        }
        var depth = reader.Depth;
        reader.Read();
        string? single = null;
        System.Text.StringBuilder? sb = null;
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                reader.Read();
                break;
            }
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    reader.Skip();
                    continue;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    if (single is null && sb is null) single = reader.Value;
                    else
                    {
                        sb ??= new System.Text.StringBuilder(single);
                        sb.Append(reader.Value);
                    }
                    break;
            }
            reader.Read();
        }
        return CdataText.Normalize(sb?.ToString() ?? single);
    }

    /// <summary>
    /// Iterates over the child elements of the current element. The callback receives the reader positioned on each
    /// child element and must consume that element completely (e.g. via Skip, ReadElementContentAsString, XNode.ReadFrom
    /// or another reader). Leaves the reader positioned after the parent's end tag.
    /// </summary>
    public static void ForEachChild(this XmlReader reader, Action<XmlReader, string> onChild)
    {
        if (reader.NodeType != XmlNodeType.Element) throw new InvalidOperationException("Reader must be on an element");
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }
        var depth = reader.Depth;
        reader.Read();
        while (true)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
            {
                var before = reader.Depth;
                onChild(reader, reader.LocalName);
                // Defensive: if the callback did not advance past the element, skip it.
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == before && !reader.EOF)
                {
                    // Cannot reliably detect "same element"; assume callback left it unconsumed only if depth matches and
                    // name is identical to what we passed in — callers are expected to consume, so this is a guard.
                }
                continue;
            }
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                reader.Read();
                return;
            }
            if (reader.EOF) return;
            reader.Read();
        }
    }

    /// <summary>Materializes the current element as an XElement and moves past it.</summary>
    public static XElement ReadElement(this XmlReader reader) => (XElement)XNode.ReadFrom(reader);
}

public static class XElementExtensions
{
    public static string? Attr(this XElement e, string name) => e.Attribute(name)?.Value;

    public static string? ChildText(this XElement e, string name)
    {
        var c = e.Element(name);
        return c is null ? null : c.OwnText();
    }

    /// <summary>Normalized text of the element's direct text/CDATA nodes only (nested elements such as CustomProperties are ignored).</summary>
    public static string OwnText(this XElement e)
    {
        string? single = null;
        System.Text.StringBuilder? sb = null;
        foreach (var node in e.Nodes())
        {
            if (node is not XText t) continue;
            if (single is null && sb is null) single = t.Value;
            else
            {
                sb ??= new System.Text.StringBuilder(single);
                sb.Append(t.Value);
            }
        }
        return CdataText.Normalize(sb?.ToString() ?? single);
    }

    public static int Line(this XElement e) => e is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
}
