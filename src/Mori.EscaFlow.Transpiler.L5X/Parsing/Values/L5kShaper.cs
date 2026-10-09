using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

/// <summary>
/// Converts an untyped L5K initializer tree into a typed <see cref="TagValue"/> by walking the tag's data type.
/// Used when Decorated data is absent, and to cross-check the two formats. Never throws.
/// </summary>
public static class L5kShaper
{
    public const string CodeOpaqueType = "L5K003";
    public const string CodeShapeMismatch = "L5K004";

    /// <summary>
    /// Shapes <paramref name="value"/> as a tag of type <paramref name="dataTypeName"/> with the given dimensions
    /// (empty for a scalar). <paramref name="reported"/> may be passed to suppress repeated opaque-type notices.
    /// </summary>
    public static TagValue? Shape(L5kValue value, string dataTypeName, int[] dimensions, DataTypeRegistry registry, IDiagnosticSink diagnostics, HashSet<string>? reported = null, ValueInterner? interner = null)
    {
        var ctx = new Context(registry, diagnostics, reported, interner ?? ValueInterner.Shared);
        return ctx.Shape(value, dataTypeName, dimensions ?? Array.Empty<int>());
    }

    private sealed class Context
    {
        private readonly DataTypeRegistry _registry;
        private readonly IDiagnosticSink _diag;
        private readonly HashSet<string> _reported;
        private readonly ValueInterner _interner;

        public Context(DataTypeRegistry registry, IDiagnosticSink diag, HashSet<string>? reported, ValueInterner interner)
        {
            _registry = registry;
            _diag = diag;
            _reported = reported ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _interner = interner;
        }

        public TagValue? Shape(L5kValue value, string typeName, int[] dims)
        {
            if (dims.Length > 0)
            {
                return ShapeArray(value, typeName, dims);
            }
            return ShapeScalarOrStruct(value, typeName);
        }

        private TagValue ShapeArray(L5kValue value, string elementType, int[] dims)
        {
            var total = 1;
            foreach (var d in dims) total *= d;
            var leaves = new List<L5kValue>(total);
            Flatten(value, dims, 0, leaves);
            if (leaves.Count != total)
            {
                _diag.Warning(CodeShapeMismatch, $"L5K array data has {leaves.Count} elements but {elementType}[{string.Join(",", dims)}] needs {total}");
            }
            AtomicKindExtensions.TryParseAtomic(elementType, out var kind);
            var isAtomic = AtomicKindExtensions.TryParseAtomic(elementType, out _);
            var radix = isAtomic ? DefaultRadix(kind) : Radix.Unknown;
            var elements = new TagValue[total];
            for (var i = 0; i < total; i++)
            {
                TagValue? el = i < leaves.Count ? ShapeScalarOrStruct(leaves[i], elementType) : null;
                el ??= isAtomic
                    ? _interner.Integer(kind, 0, radix)
                    : new StructValue(elementType, Array.Empty<KeyValuePair<string, TagValue>>());
                elements[i] = el;
            }
            return new ArrayValue(elementType, dims, radix, elements);
        }

        /// <summary>Flattens nested lists row-major. L5K nests one list level per dimension, but tolerate a flat list.</summary>
        private static void Flatten(L5kValue value, int[] dims, int depth, List<L5kValue> leaves)
        {
            if (value is L5kList list)
            {
                if (depth >= dims.Length)
                {
                    // Deeper than the array rank: this is an element that is itself a structure / string list.
                    leaves.Add(value);
                    return;
                }
                // Heuristic: a list at this depth is a dimension level when the remaining rank > 0 and its items are
                // lists (or scalars for the innermost level). A structure-typed element list at the innermost level
                // would also be a list; distinguish by count vs dimension.
                var remaining = dims.Length - depth;
                if (remaining == 1)
                {
                    foreach (var item in list.Items) leaves.Add(item);
                    return;
                }
                foreach (var item in list.Items) Flatten(item, dims, depth + 1, leaves);
                return;
            }
            leaves.Add(value);
        }

        private TagValue? ShapeScalarOrStruct(L5kValue value, string typeName)
        {
            if (AtomicKindExtensions.TryParseAtomic(typeName, out var kind))
            {
                return ShapeAtomic(value, kind, DefaultRadix(kind));
            }

            var def = _registry.TryGet(typeName);
            if (def is AtomicType at)
            {
                return ShapeAtomic(value, at.Kind, DefaultRadix(at.Kind));
            }
            if (def is StructuredType st && !st.IsOpaque && st.Members.Count > 0)
            {
                if (st.IsStringFamily || IsStringShape(st))
                {
                    return ShapeString(value, st);
                }
                return ShapeStructure(value, st);
            }

            if (_reported.Add(typeName))
            {
                _diag.Info(CodeOpaqueType, def is null
                    ? $"Data type '{typeName}' is unknown; L5K data shaped best-effort"
                    : $"Data type '{typeName}' has no known layout; L5K data shaped best-effort");
            }
            return BestEffort(value, typeName);
        }

        private static bool IsStringShape(StructuredType st)
        {
            if (st.Members.Count != 2) return false;
            return st.Members[0].Name.Equals("LEN", StringComparison.OrdinalIgnoreCase)
                && st.Members[1].Name.Equals("DATA", StringComparison.OrdinalIgnoreCase);
        }

        private TagValue ShapeString(L5kValue value, StructuredType st)
        {
            if (value is L5kList { Items.Count: 2 } list && list.Items[0] is L5kInteger len)
            {
                var text = list.Items[1] switch
                {
                    L5kString s => s.Value,
                    L5kList chars => new string(chars.Items.Select(c => c is L5kInteger ci ? (char)(ci.Value & 0xFF) : '\0').ToArray()),
                    _ => "",
                };
                var n = (int)len.Value;
                if (n >= 0 && text.Length > n) text = text[..n];
                return new StringValue(n, text, st.Name);
            }
            if (value is L5kString str)
            {
                return new StringValue(str.Value.Length, str.Value, st.Name);
            }
            _diag.Warning(CodeShapeMismatch, $"L5K data for string type '{st.Name}' is not [LEN,'text']");
            return new StringValue(0, "", st.Name);
        }

        private TagValue ShapeStructure(L5kValue value, StructuredType st)
        {
            var items = value is L5kList list ? list.Items : new[] { value };
            var members = new List<KeyValuePair<string, TagValue>>(st.Members.Count);
            var slot = 0;

            // Bit-overlay members do not occupy an L5K slot; their host does. Pre-compute host values as we go.
            var hostValues = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in st.Members)
            {
                if (member.IsBitOverlay)
                {
                    long host = 0;
                    if (member.Target is not null) hostValues.TryGetValue(member.Target, out host);
                    var bit = member.BitNumber ?? 0;
                    var on = ((host >> bit) & 1) != 0;
                    var kind = AtomicKindExtensions.TryParseAtomic(member.DataType.Name, out var k) && k == AtomicKind.Bit ? AtomicKind.Bool : AtomicKind.Bool;
                    members.Add(new KeyValuePair<string, TagValue>(member.Name, _interner.Integer(kind, on ? 1 : 0, Radix.Decimal)));
                    continue;
                }

                L5kValue? item = slot < items.Count ? items[slot] : null;
                slot++;

                if (member.Hidden)
                {
                    // Hidden bit host: consume the slot, remember its value, do not emit.
                    if (item is L5kInteger hi) hostValues[member.Name] = hi.Value;
                    else if (item is L5kList hl && hl.Items.Count > 0 && hl.Items[0] is L5kInteger first) hostValues[member.Name] = first.Value;
                    continue;
                }

                if (item is null)
                {
                    _diag.Warning(CodeShapeMismatch, $"L5K data for '{st.Name}' has fewer values than members (missing '{member.Name}')");
                    members.Add(new KeyValuePair<string, TagValue>(member.Name, Default(member)));
                    continue;
                }

                var dims = member.Dimension > 0 ? new[] { member.Dimension } : Array.Empty<int>();
                var shaped = Shape(item, member.DataType.Name, dims) ?? Default(member);
                if (shaped is ScalarValue sv && member.Radix != Radix.Unknown && sv.Radix != member.Radix && sv.RawText is null)
                {
                    shaped = sv with { Radix = member.Radix };
                }
                members.Add(new KeyValuePair<string, TagValue>(member.Name, shaped));
            }

            if (slot < items.Count)
            {
                _diag.Warning(CodeShapeMismatch, $"L5K data for '{st.Name}' has {items.Count - slot} more values than members");
            }
            return new StructValue(st.Name, members);
        }

        private TagValue Default(DataTypeMember member)
        {
            if (AtomicKindExtensions.TryParseAtomic(member.DataType.Name, out var kind))
            {
                var scalar = _interner.Integer(kind, 0, DefaultRadix(kind));
                if (member.Dimension > 0)
                {
                    var arr = new TagValue[member.Dimension];
                    Array.Fill(arr, scalar);
                    return new ArrayValue(member.DataType.Name, new[] { member.Dimension }, DefaultRadix(kind), arr);
                }
                return scalar;
            }
            return new StructValue(member.DataType.Name, Array.Empty<KeyValuePair<string, TagValue>>());
        }

        private TagValue ShapeAtomic(L5kValue value, AtomicKind kind, Radix radix)
        {
            switch (value)
            {
                case L5kInteger i:
                    if (kind.IsFloatingPoint()) return _interner.Real(kind, i.Value, Radix.Float);
                    return _interner.Integer(kind, kind.IsBoolean() ? (i.Value != 0 ? 1 : 0) : i.Value, i.RadixText is null ? radix : RadixFromPrefix(i.RadixText));
                case L5kReal r:
                    if (kind.IsFloatingPoint()) return _interner.Real(kind, r.Value, Radix.Float);
                    return _interner.Integer(kind, (long)r.Value, radix);
                case L5kString s:
                {
                    long v = 0;
                    foreach (var ch in s.Value) v = (v << 8) | ((long)ch & 0xFF);
                    if (kind.IsFloatingPoint()) return _interner.Real(kind, v, Radix.Float);
                    return _interner.Integer(kind, v, Radix.Ascii);
                }
                case L5kList list:
                    _diag.Warning(CodeShapeMismatch, $"Expected a {kind.ToLogixName()} value but found a list of {list.Items.Count}");
                    return list.Items.Count > 0 ? ShapeAtomic(list.Items[0], kind, radix) : _interner.Integer(kind, 0, radix);
                case L5kRaw raw:
                    return new ScalarValue(kind, 0, 0, radix) { RawText = raw.Text };
                default:
                    return _interner.Integer(kind, 0, radix);
            }
        }

        private TagValue BestEffort(L5kValue value, string typeName)
        {
            switch (value)
            {
                case L5kInteger i:
                    return _interner.Integer(AtomicKind.Dint, i.Value, i.RadixText is null ? Radix.Decimal : RadixFromPrefix(i.RadixText));
                case L5kReal r:
                    return _interner.Real(AtomicKind.Real, r.Value, Radix.Float);
                case L5kString s:
                    return new StringValue(s.Value.Length, s.Value, "STRING");
                case L5kRaw raw:
                    return new ScalarValue(AtomicKind.Dint, 0, 0, Radix.Decimal) { RawText = raw.Text };
                case L5kList list:
                {
                    var members = new List<KeyValuePair<string, TagValue>>(list.Items.Count);
                    for (var i = 0; i < list.Items.Count; i++)
                    {
                        members.Add(new KeyValuePair<string, TagValue>("[" + i + "]", BestEffort(list.Items[i], typeName)));
                    }
                    return new StructValue(typeName, members);
                }
                default:
                    return new StructValue(typeName, Array.Empty<KeyValuePair<string, TagValue>>());
            }
        }

        private static Radix DefaultRadix(AtomicKind kind) => kind.IsFloatingPoint() ? Radix.Float : Radix.Decimal;

        private static Radix RadixFromPrefix(string prefix) => prefix switch
        {
            "2#" => Radix.Binary,
            "8#" => Radix.Octal,
            "16#" => Radix.Hex,
            _ => Radix.Decimal,
        };
    }
}
