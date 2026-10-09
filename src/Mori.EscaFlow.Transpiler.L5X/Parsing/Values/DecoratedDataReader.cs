using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

/// <summary>
/// Reads a <c>&lt;Data Format="Decorated"&gt;</c> tree (DataValue / Array / Structure and their *Member forms)
/// from a streaming <see cref="XmlReader"/> into a typed <see cref="TagValue"/>. Never throws on content problems.
/// <para>
/// Decorated data is self-describing (every element carries its own <c>DataType</c> and, for scalars,
/// <c>Radix</c>), unlike L5K, so this reader needs no data type registry at all: it shapes values purely from
/// what each element says about itself. It runs directly off the streaming <see cref="XmlReader"/> used by the
/// XML phase (see <c>L5xParser</c>) rather than materializing a <see cref="XElement"/> tree first, because a
/// large array can otherwise be the single biggest allocation in parsing a project.
/// </para>
/// </summary>
public static class DecoratedDataReader
{
    public const string CodeUnknownElement = "DEC001";
    public const string CodeBadValue = "DEC002";
    public const string CodeBadIndex = "DEC003";

    /// <summary>
    /// Reads the value rooted at the reader's current element. The reader may be positioned on the wrapping
    /// <c>Data</c> / <c>DefaultData</c> element or directly on a DataValue / Array / Structure element.
    /// On return the reader is positioned just after the element that was read.
    /// </summary>
    public static TagValue? Read(XmlReader reader, IDiagnosticSink diagnostics, ValueInterner? interner = null)
    {
        var impl = new Impl(reader, diagnostics, interner ?? ValueInterner.Shared);
        return impl.ReadRoot();
    }

    /// <summary>Reads a value from an already materialized element (used for module config / IO data, whose elements are read as part of a generic subtree rather than streamed directly).</summary>
    public static TagValue? ReadFromElement(XElement element, IDiagnosticSink diagnostics, ValueInterner? interner = null)
    {
        using var reader = element.CreateReader();
        reader.MoveToContent();
        return Read(reader, diagnostics, interner);
    }

    /// <summary>
    /// Parses a decorated scalar text according to the atomic kind and radix. Returns null when unparseable.
    /// Handles plain numbers, the ASCII display form (<c>'$00$0F'</c>, most significant byte first) and the
    /// boolean text forms (<c>0</c>/<c>1</c>, <c>true</c>/<c>false</c>).
    /// </summary>
    public static ScalarValue? TryParseScalar(AtomicKind kind, Radix radix, string text, ValueInterner? interner = null)
    {
        interner ??= ValueInterner.Shared;
        var span = text.AsSpan().Trim();
        if (span.IsEmpty) return null;

        if (kind.IsFloatingPoint())
        {
            if (LiteralParser.TryParseNumber(span, out var lit))
            {
                var d = lit switch
                {
                    RealLiteral r => r.Value,
                    IntegerLiteral i => i.Value,
                    _ => double.NaN,
                };
                return interner.Real(kind, d, radix == Radix.Unknown ? Radix.Float : radix);
            }
            if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var dd))
            {
                return interner.Real(kind, dd, radix == Radix.Unknown ? Radix.Float : radix);
            }
            return null;
        }

        if (kind.IsBoolean())
        {
            if (span.Length == 1)
            {
                if (span[0] == '0') return interner.Integer(kind, 0, Radix.Decimal);
                if (span[0] == '1') return interner.Integer(kind, 1, Radix.Decimal);
            }
            if (span.Equals("true", StringComparison.OrdinalIgnoreCase)) return interner.Integer(kind, 1, Radix.Decimal);
            if (span.Equals("false", StringComparison.OrdinalIgnoreCase)) return interner.Integer(kind, 0, Radix.Decimal);
        }

        if (span[0] == '\'')
        {
            // ASCII display: '$00$0F' is 15 — most significant byte first.
            var decoded = LogixStringDecoder.DecodeQuoted(span);
            if (decoded is null) return null;
            long v = 0;
            foreach (var ch in decoded)
            {
                v = (v << 8) | ((long)ch & 0xFF);
            }
            var bits = kind.BitSize();
            if (bits > 0 && bits < 64) v = SignExtend(v, bits, kind);
            return interner.Integer(kind, v, radix == Radix.Unknown ? Radix.Ascii : radix);
        }

        if (LiteralParser.TryParseNumber(span, out var literal))
        {
            switch (literal)
            {
                case IntegerLiteral i:
                {
                    var v = i.Value;
                    var bits = kind.BitSize();
                    // A radix-prefixed literal (2#/8#/16#) is the bit pattern, not a signed decimal value, so it
                    // must be sign-extended according to the destination type's width rather than taken as-is.
                    if (i.RadixBase != 10 && bits > 0 && bits < 64) v = SignExtend(v, bits, kind);
                    var effectiveRadix = radix != Radix.Unknown ? radix : i.RadixBase switch
                    {
                        2 => Radix.Binary,
                        8 => Radix.Octal,
                        16 => Radix.Hex,
                        _ => Radix.Decimal,
                    };
                    return interner.Integer(kind, v, effectiveRadix);
                }
                case RealLiteral r:
                    return interner.Integer(kind, (long)r.Value, radix == Radix.Unknown ? Radix.Decimal : radix);
            }
        }
        return null;
    }

    /// <summary>Masks to <paramref name="bits"/> width and, for a signed kind, extends the sign bit so the value matches what the same bit pattern means as a signed integer.</summary>
    private static long SignExtend(long value, int bits, AtomicKind kind)
    {
        var mask = (1L << bits) - 1;
        value &= mask;
        var unsigned = kind is AtomicKind.Usint or AtomicKind.Uint or AtomicKind.Udint or AtomicKind.Ulint;
        if (!unsigned && (value & (1L << (bits - 1))) != 0)
        {
            value |= ~mask;
        }
        return value;
    }

    /// <summary>Parses an array's "Dimensions" attribute ("5 10 2" or "5,10,2") into dimension sizes, outermost first.</summary>
    internal static int[] ParseDimensions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<int>();
        var parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var dims = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out dims[i])) return Array.Empty<int>();
        }
        return dims;
    }

    /// <summary>Parses "[i]" or "[i,j,k]" into a flat row-major index. Returns -1 when malformed or out of range.</summary>
    internal static int ParseFlatIndex(string? index, int[] dims)
    {
        if (string.IsNullOrEmpty(index)) return -1;
        var span = index.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '[' && span[^1] == ']') span = span[1..^1];
        var flat = 0;
        var dim = 0;
        while (true)
        {
            var comma = span.IndexOf(',');
            var part = comma < 0 ? span : span[..comma];
            if (dim >= dims.Length) return -1;
            if (!int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var idx)) return -1;
            if (idx < 0 || idx >= dims[dim]) return -1;
            flat = flat * dims[dim] + idx;
            dim++;
            if (comma < 0) break;
            span = span[(comma + 1)..];
        }
        return dim == dims.Length ? flat : -1;
    }

    /// <summary>Per-read cursor state over the streaming reader; one instance per <see cref="Read"/> call.</summary>
    private sealed class Impl
    {
        private readonly XmlReader _reader;
        private readonly IDiagnosticSink _diag;
        private readonly ValueInterner _interner;
        private readonly IXmlLineInfo? _lineInfo;

        public Impl(XmlReader reader, IDiagnosticSink diag, ValueInterner interner)
        {
            _reader = reader;
            _diag = diag;
            _interner = interner;
            _lineInfo = reader as IXmlLineInfo;
        }

        private int? CurrentLine() => _lineInfo is not null && _lineInfo.HasLineInfo() ? _lineInfo.LineNumber : null;

        private DiagnosticLocation Loc(string? path = null)
            => new(XmlLine: CurrentLine(), ElementPath: path);

        /// <summary>Unwraps an optional Data/DefaultData wrapper, then reads the single value element inside (or directly, if the reader was already positioned on it).</summary>
        public TagValue? ReadRoot()
        {
            if (_reader.NodeType != XmlNodeType.Element)
            {
                _reader.MoveToContent();
                if (_reader.NodeType != XmlNodeType.Element) return null;
            }

            var name = _reader.LocalName;
            if (name is "Data" or "DefaultData")
            {
                if (_reader.IsEmptyElement)
                {
                    _reader.Read();
                    return null;
                }
                _reader.Read();
                TagValue? result = null;
                while (!_reader.EOF && _reader.NodeType != XmlNodeType.EndElement)
                {
                    if (_reader.NodeType == XmlNodeType.Element)
                    {
                        if (result is null)
                        {
                            result = ReadNode(out _);
                        }
                        else
                        {
                            // A Data element has exactly one value child by spec; a second one is tolerated and dropped.
                            _diag.Info(CodeUnknownElement, $"Extra element <{_reader.LocalName}> in decorated data ignored", Loc(_reader.LocalName));
                            _reader.Skip();
                        }
                    }
                    else
                    {
                        _reader.Read();
                    }
                }
                if (!_reader.EOF) _reader.Read();
                return result;
            }

            return ReadNode(out _);
        }

        /// <summary>Reads the element at the cursor and advances past it. Returns the member name (for *Member forms).</summary>
        private TagValue? ReadNode(out string? memberName)
        {
            memberName = _reader.GetAttribute("Name");
            switch (_reader.LocalName)
            {
                case "DataValue":
                case "DataValueMember":
                    return ReadScalar();
                case "Array":
                case "ArrayMember":
                    return ReadArray();
                case "Structure":
                case "StructureMember":
                    return ReadStructure();
                default:
                    // A future element kind this reader does not yet know; skip it rather than fail the whole tag.
                    _diag.Info(CodeUnknownElement, $"Unknown decorated data element <{_reader.LocalName}>", Loc(_reader.LocalName));
                    _reader.Skip();
                    return null;
            }
        }

        /// <summary>
        /// Reads a DataValue(Member): an atomic scalar, or (for the non-atomic forms Logix also spells this way,
        /// such as a STRING's DATA member) a string collapsed from its quoted or ASCII text.
        /// </summary>
        private TagValue? ReadScalar()
        {
            var dataType = _reader.GetAttribute("DataType") ?? "";
            var radix = RadixExtensions.ParseRadix(_reader.GetAttribute("Radix"));
            var valueAttr = _reader.GetAttribute("Value");
            var forceValue = _reader.GetAttribute("ForceValue");
            var line = CurrentLine();

            // The value may be the "Value" attribute or, for some exports, the element's text content; the
            // attribute wins when both are present (never expected to disagree, but it is the more structured form).
            string? text = valueAttr;
            if (_reader.IsEmptyElement)
            {
                _reader.Read();
            }
            else
            {
                var content = ReadTextContent();
                if (text is null) text = content;
            }
            text ??= "";

            if (AtomicKindExtensions.TryParseAtomic(dataType, out var kind))
            {
                var scalar = TryParseScalar(kind, radix, text, _interner);
                if (scalar is null)
                {
                    // DT# date/time literals are a known, expected case we do not attempt to parse here; anything
                    // else failing to parse is unusual and worth a warning, with the raw text preserved regardless.
                    if (!text.StartsWith("DT#", StringComparison.OrdinalIgnoreCase))
                    {
                        _diag.Warning(CodeBadValue, $"Cannot parse {dataType} value '{text}'", new DiagnosticLocation(XmlLine: line));
                    }
                    scalar = new ScalarValue(kind, 0, 0, radix) { RawText = text };
                }
                if (forceValue is not null)
                {
                    // A forced tag's real value is overridden for I/O purposes while the force is active; both are kept.
                    scalar = scalar with { ForceValue = forceValue };
                }
                return scalar;
            }

            // Non-atomic DataValueMember: the STRING DATA member carries its characters as text.
            var trimmed = text.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '\'')
            {
                var decoded = LogixStringDecoder.DecodeQuoted(trimmed) ?? "";
                return new StringValue(decoded.Length, decoded, dataType.Length == 0 ? "STRING" : dataType);
            }
            if (radix == Radix.Ascii || dataType.Contains("STRING", StringComparison.OrdinalIgnoreCase))
            {
                return new StringValue(trimmed.Length, trimmed, dataType.Length == 0 ? "STRING" : dataType);
            }

            _diag.Warning(CodeBadValue, $"Cannot parse value '{text}' of type '{dataType}'", new DiagnosticLocation(XmlLine: line));
            return new ScalarValue(AtomicKind.Dint, 0, 0, radix) { RawText = text };
        }

        /// <summary>Reads text/CDATA content of the current element and advances past its end tag. A single text run avoids a StringBuilder; multiple runs (rare) are concatenated.</summary>
        private string ReadTextContent()
        {
            StringBuilder? sb = null;
            string? single = null;
            _reader.Read();
            while (!_reader.EOF && _reader.NodeType != XmlNodeType.EndElement)
            {
                switch (_reader.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                        if (single is null && sb is null) single = _reader.Value;
                        else
                        {
                            sb ??= new StringBuilder(single);
                            sb.Append(_reader.Value);
                        }
                        _reader.Read();
                        break;
                    case XmlNodeType.Element:
                        _diag.Info(CodeUnknownElement, $"Unexpected child <{_reader.LocalName}> inside a scalar value", Loc(_reader.LocalName));
                        _reader.Skip();
                        break;
                    default:
                        _reader.Read();
                        break;
                }
            }
            if (!_reader.EOF) _reader.Read();
            return (sb?.ToString() ?? single ?? "").Trim();
        }

        /// <summary>
        /// Reads an Array(Member). Elements may be sparse (Logix omits elements that equal the type's default,
        /// typically zero, to shrink the export) and may arrive out of index order, so each is placed into a
        /// slot array by its parsed index rather than assumed sequential; a true multi-dimensional array
        /// (<see cref="ParseDimensions"/> non-empty) fills any unwritten slot with the type's default value,
        /// while a dimensionless array (seen for some module/config data) keeps only what was actually written,
        /// in the order it appeared, including a fallback sequential placement when an index cannot be parsed.
        /// </summary>
        private TagValue ReadArray()
        {
            var elementType = _reader.GetAttribute("DataType") ?? "";
            var dims = ParseDimensions(_reader.GetAttribute("Dimensions"));
            var radix = RadixExtensions.ParseRadix(_reader.GetAttribute("Radix"));
            AtomicKindExtensions.TryParseAtomic(elementType, out var kind);
            var isAtomic = AtomicKindExtensions.TryParseAtomic(elementType, out _);

            var total = 1;
            foreach (var d in dims) total *= d;
            if (dims.Length == 0) total = 0;

            var slots = new TagValue?[total];
            var overflow = new List<TagValue>();
            var nextSequential = 0;

            if (_reader.IsEmptyElement)
            {
                _reader.Read();
            }
            else
            {
                _reader.Read();
                while (!_reader.EOF && _reader.NodeType != XmlNodeType.EndElement)
                {
                    if (_reader.NodeType != XmlNodeType.Element)
                    {
                        _reader.Read();
                        continue;
                    }
                    if (_reader.LocalName != "Element")
                    {
                        _diag.Info(CodeUnknownElement, $"Unexpected <{_reader.LocalName}> inside an array", Loc(_reader.LocalName));
                        _reader.Skip();
                        continue;
                    }

                    var indexText = _reader.GetAttribute("Index");
                    var valueAttr = _reader.GetAttribute("Value");
                    var elementLine = CurrentLine();
                    var flat = ParseFlatIndex(indexText, dims);
                    if (flat < 0)
                    {
                        _diag.Warning(CodeBadIndex, $"Array element index '{indexText}' is invalid for dimensions [{string.Join(",", dims)}]", new DiagnosticLocation(XmlLine: elementLine));
                    }

                    // An atomic element's value may sit directly on the "Value" attribute (the common case,
                    // self-closing <Element/>) rather than in a nested DataValue child.
                    TagValue? element = null;
                    if (valueAttr is not null)
                    {
                        if (isAtomic)
                        {
                            element = TryParseScalar(kind, radix, valueAttr, _interner);
                            if (element is null)
                            {
                                _diag.Warning(CodeBadValue, $"Cannot parse {elementType} element value '{valueAttr}'", new DiagnosticLocation(XmlLine: elementLine));
                                element = new ScalarValue(kind, 0, 0, radix) { RawText = valueAttr };
                            }
                        }
                        else
                        {
                            var decoded = LogixStringDecoder.DecodeQuoted(valueAttr);
                            element = decoded is not null
                                ? new StringValue(decoded.Length, decoded, elementType)
                                : new ScalarValue(AtomicKind.Dint, 0, 0, radix) { RawText = valueAttr };
                        }
                        if (_reader.IsEmptyElement)
                        {
                            _reader.Read();
                        }
                        else
                        {
                            _reader.Skip();
                        }
                    }
                    else if (_reader.IsEmptyElement)
                    {
                        _reader.Read();
                    }
                    else
                    {
                        // A structure or nested array element: its value is a child node rather than an attribute.
                        _reader.Read();
                        while (!_reader.EOF && _reader.NodeType != XmlNodeType.EndElement)
                        {
                            if (_reader.NodeType == XmlNodeType.Element)
                            {
                                if (element is null)
                                {
                                    element = ReadNode(out _);
                                }
                                else
                                {
                                    _diag.Info(CodeUnknownElement, $"Extra element <{_reader.LocalName}> inside an array element", Loc(_reader.LocalName));
                                    _reader.Skip();
                                }
                            }
                            else
                            {
                                _reader.Read();
                            }
                        }
                        if (!_reader.EOF) _reader.Read();
                    }

                    if (element is null) continue;
                    if (flat >= 0 && flat < slots.Length)
                    {
                        slots[flat] = element;
                        nextSequential = flat + 1;
                    }
                    else if (dims.Length == 0)
                    {
                        // No declared dimensions: keep elements in file order instead of trying to place them by index.
                        overflow.Add(element);
                    }
                    else if (nextSequential < slots.Length && flat < 0)
                    {
                        // The index could not be parsed; fall back to sequential placement rather than dropping the value.
                        slots[nextSequential++] = element;
                    }
                }
                if (!_reader.EOF) _reader.Read();
            }

            if (dims.Length == 0)
            {
                dims = new[] { overflow.Count };
                return new ArrayValue(elementType, dims, radix, overflow);
            }

            var elements = new TagValue[slots.Length];
            TagValue? filler = null;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] is { } v)
                {
                    elements[i] = v;
                }
                else
                {
                    // An element the export omitted because it held the type's default value.
                    filler ??= isAtomic
                        ? _interner.Integer(kind, 0, radix == Radix.Unknown ? Radix.Decimal : radix)
                        : new StructValue(elementType, Array.Empty<KeyValuePair<string, TagValue>>());
                    elements[i] = filler;
                }
            }
            return new ArrayValue(elementType, dims, radix, elements);
        }

        /// <summary>Reads a Structure(Member): its child *Member elements become the structure's members in file order, then collapses to a string value when the shape matches [LEN, DATA].</summary>
        private TagValue ReadStructure()
        {
            var dataType = _reader.GetAttribute("DataType") ?? "";
            var members = new List<KeyValuePair<string, TagValue>>();

            if (_reader.IsEmptyElement)
            {
                _reader.Read();
            }
            else
            {
                _reader.Read();
                while (!_reader.EOF && _reader.NodeType != XmlNodeType.EndElement)
                {
                    if (_reader.NodeType != XmlNodeType.Element)
                    {
                        _reader.Read();
                        continue;
                    }
                    var value = ReadNode(out var memberName);
                    if (value is not null)
                    {
                        members.Add(new KeyValuePair<string, TagValue>(memberName ?? "", value));
                    }
                }
                if (!_reader.EOF) _reader.Read();
            }

            if (TryCollapseString(dataType, members, out var str)) return str;
            return new StructValue(dataType, members);
        }

        /// <summary>
        /// A Decorated STRING is exported as a structure with a LEN scalar and a DATA member (either already a
        /// string, from an earlier collapse of its own, or a SINT array of character codes); this folds that
        /// back into one <see cref="StringValue"/> so consumers never have to special-case the STRING shape.
        /// </summary>
        private static bool TryCollapseString(string dataType, List<KeyValuePair<string, TagValue>> members, out StringValue result)
        {
            result = null!;
            if (members.Count != 2) return false;
            TagValue? len = null, data = null;
            foreach (var kv in members)
            {
                if (kv.Key.Equals("LEN", StringComparison.OrdinalIgnoreCase)) len = kv.Value;
                else if (kv.Key.Equals("DATA", StringComparison.OrdinalIgnoreCase)) data = kv.Value;
            }
            if (len is not ScalarValue lenScalar || data is null) return false;
            var length = (int)lenScalar.Bits;
            string text;
            switch (data)
            {
                case StringValue sv:
                    text = sv.Text;
                    break;
                case ArrayValue { Elements: var els } av when av.ElementType.Equals("SINT", StringComparison.OrdinalIgnoreCase):
                {
                    var sb = new StringBuilder(Math.Min(length, els.Count));
                    for (var i = 0; i < els.Count && i < length; i++)
                    {
                        if (els[i] is ScalarValue s) sb.Append((char)(s.Bits & 0xFF));
                    }
                    text = sb.ToString();
                    break;
                }
                default:
                    return false;
            }
            if (length >= 0 && text.Length > length) text = text[..length];
            result = new StringValue(length, text, dataType);
            return true;
        }
    }
}
