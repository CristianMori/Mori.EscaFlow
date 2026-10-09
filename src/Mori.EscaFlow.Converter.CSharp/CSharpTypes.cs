using Mori.EscaFlow.Converter.Layout;
using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Type mapping for the C# target: wrapper classes per atomic, generated classes per structure, arrays.</summary>
public sealed class CSharpTypes
{
    private readonly IrDocument _doc;
    private readonly NameMapper _names;
    private readonly LogixLayoutCalculator _layouts;

    public CSharpTypes(IrDocument doc, NameMapper names, LogixLayoutCalculator layouts)
    {
        _doc = doc;
        _names = names;
        _layouts = layouts;
    }

    public const string TypeScope = "types";

    /// <summary>Wrapper class for an atomic (BOOL → WrapBOOL, ...).</summary>
    public static string Wrapper(string atomic) => "Wrap" + AtomicTypes.Get(atomic)?.Name ?? "WrapDINT";

    /// <summary>C# value type of an atomic wrapper's Value.</summary>
    public static string ValueType(string atomic) => (AtomicTypes.Get(atomic)?.Name ?? "DINT") switch
    {
        "BOOL" => "bool", "SINT" => "sbyte", "INT" => "short", "DINT" => "int", "LINT" => "long",
        "USINT" or "BYTE" or "CHAR" => "byte", "UINT" or "WORD" or "WCHAR" => "ushort", "UDINT" or "DWORD" or "TIME" or "DATE" or "TOD" or "DT" => "uint", "ULINT" or "LWORD" or "LTIME" or "LDATE" or "LTOD" or "LDT" => "ulong",
        "REAL" => "float", "LREAL" => "double",
        _ => "int",
    };

    /// <summary>Alias atomics (BYTE, WORD, TIME...) onto the Logix wrapper that stores them.</summary>
    public static string CanonicalAtomic(string atomic) => (AtomicTypes.Get(atomic)?.Name ?? "DINT") switch
    {
        "BYTE" or "CHAR" => "USINT", "WORD" or "WCHAR" => "UINT", "DWORD" or "TIME" or "DATE" or "TOD" or "DT" => "UDINT", "LWORD" or "LTIME" or "LDATE" or "LTOD" or "LDT" => "ULINT",
        var n => n,
    };

    public static bool IsAtomic(IrTypeRef? t) => t is { IsNamed: true, Name: { } n } && AtomicTypes.IsAtomic(n);
    public static bool IsBool(IrTypeRef? t) => t is { } x && x.Is("BOOL");
    public static bool IsIntegerAtomic(IrTypeRef? t) => t is { IsNamed: true, Name: { } n } && AtomicTypes.Get(n) is { } a && (a.IsInteger || a.Category == AtomicCategory.BitString);
    public static bool IsRealAtomic(IrTypeRef? t) => t is { IsNamed: true, Name: { } n } && AtomicTypes.Get(n) is { Category: AtomicCategory.Real };

    /// <summary>Generated class name for a structure, string type or function block.</summary>
    public string ClassName(string typeName)
    {
        if (typeName.Equals("STRING", StringComparison.OrdinalIgnoreCase)) return "STRING";
        return _names.Map(TypeScope, typeName);
    }

    /// <summary>The C# field type for a variable or member of the given IR type.</summary>
    public string FieldType(IrTypeRef type)
    {
        switch (type.Kind)
        {
            case "named":
            {
                var n = type.Name ?? "DINT";
                if (IsModuleType(n)) return "string"; // a module reference parameter holds the module name
                return AtomicTypes.IsAtomic(n) ? Wrapper(CanonicalAtomic(n)) : ClassName(n);
            }
            case "array":
            {
                var element = type.Element ?? IrTypeRef.Named("DINT");
                if (element.Is("BOOL")) return "PlcBoolArray";
                return $"PlcArray<{FieldType(element)}>";
            }
            case "string":
                return ClassName($"STRING{type.Length}");
            default:
                return "WrapDINT";
        }
    }

    /// <summary>Construction expression for a field of the given type at <paramref name="tag"/> + <paramref name="offset"/>.</summary>
    public string Construct(IrTypeRef type, string tag, string offset, string? controller = null)
    {
        switch (type.Kind)
        {
            case "named":
            {
                var n = type.Name ?? "DINT";
                if (AtomicTypes.IsAtomic(n))
                    return n.Equals("BOOL", StringComparison.OrdinalIgnoreCase) ? $"new WrapBOOL({tag}, {offset}, 0)" : $"new {Wrapper(CanonicalAtomic(n))}({tag}, {offset})";
                if (IsModuleType(n)) return "\"\"";
                if (_doc.FindPou(n) is { Kind: "functionBlock" })
                    return $"new {ClassName(n)}({tag}, {offset}, {controller ?? "PlcController.Current!"})";
                return $"new {ClassName(n)}({tag}, {offset})";
            }
            case "array":
            {
                var element = type.Element ?? IrTypeRef.Named("DINT");
                var dims = "new[] { " + string.Join(", ", (type.Dims ?? new()).Select(d => d.Count.ToString())) + " }";
                if (element.Is("BOOL")) return $"new PlcBoolArray({tag}, {offset}, {dims})";
                var (size, _) = _layouts.SizeOf(element);
                return $"new PlcArray<{FieldType(element)}>({tag}, {offset}, {size}, {dims}, (t, o) => {Construct(element, "t", "o", controller)})";
            }
            case "string":
                return $"new {ClassName($"STRING{type.Length}")}({tag}, {offset})";
            default:
                return $"new WrapDINT({tag}, {offset})";
        }
    }

    /// <summary>Layout literal for a structure (a <c>PlcLayout</c> construction expression).</summary>
    public string LayoutLiteral(StructLayout layout)
    {
        var members = layout.Members.Select(m =>
        {
            var code = m.IsStruct ? 0 : LogixLayoutCalculator.CipTypeCode(m.TypeName);
            var template = m.IsStruct ? Quote(m.TypeName) : "null";
            var dims = m.Dims.Length == 0 ? "System.Array.Empty<int>()" : "new[] { " + string.Join(", ", m.Dims) + " }";
            return $"new PlcLayoutMember({Quote(m.Name)}, 0x{code:X2}, {template}, {m.Offset}, {m.Bit ?? 0}, {dims}, {m.ElementSize}, {(m.Hidden ? "true" : "false")}, {(m.PackedBools ? "true" : "false")})";
        });
        return $"new PlcLayout({Quote(layout.Name)}, {layout.Size}, {layout.Alignment}, new PlcLayoutMember[] {{ {string.Join(", ", members)} }})";
    }

    /// <summary>The Logix MODULE pseudo-type (add-on InOut parameters that receive a module name).</summary>
    public static bool IsModuleType(string name) => name.Equals("MODULE", StringComparison.OrdinalIgnoreCase);

    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
