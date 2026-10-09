using Mori.EscaFlow.Converter.Layout;
using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Type mapping for the C# target: wrapper classes per atomic, generated classes per structure, arrays.</summary>
/// <remarks>
/// Generated code never holds a plain <c>int</c> field for a tag. Every atomic leaf is a wrapper object bound to a
/// range of tag memory (the byte image the EtherNet/IP server also reads), so a store through <c>.Value</c> is
/// immediately visible to clients and the controller's own byte layout is preserved. Structures become classes whose
/// fields are such wrappers at fixed offsets; arrays become <c>PlcArray&lt;T&gt;</c> views (or the DWORD-packed
/// <c>PlcBoolArray</c> for BOOL arrays, as Logix stores them).
/// </remarks>
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

    /// <summary>Naming scope of the generated class names (structures, add-ons, programs, the controller).</summary>
    public const string TypeScope = "types";

    /// <summary>Wrapper class for an atomic (BOOL → WrapBOOL, ...).</summary>
    public static string Wrapper(string atomic) => "Wrap" + AtomicTypes.Get(atomic)?.Name ?? "WrapDINT";

    /// <summary>C# value type of an atomic wrapper's Value.</summary>
    /// <remarks>Unsigned, bit-string and time types share the storage of the unsigned integer of the same width.</remarks>
    public static string ValueType(string atomic) => (AtomicTypes.Get(atomic)?.Name ?? "DINT") switch
    {
        "BOOL" => "bool", "SINT" => "sbyte", "INT" => "short", "DINT" => "int", "LINT" => "long",
        "USINT" or "BYTE" or "CHAR" => "byte", "UINT" or "WORD" or "WCHAR" => "ushort", "UDINT" or "DWORD" or "TIME" or "DATE" or "TOD" or "DT" => "uint", "ULINT" or "LWORD" or "LTIME" or "LDATE" or "LTOD" or "LDT" => "ulong",
        "REAL" => "float", "LREAL" => "double",
        _ => "int",
    };

    /// <summary>Alias atomics (BYTE, WORD, TIME...) onto the Logix wrapper that stores them.</summary>
    /// <remarks>Logix has no bit-string or time atomics; the runtime offers wrappers for the Logix set only, so the IEC extras map onto the unsigned integers of the same width.</remarks>
    public static string CanonicalAtomic(string atomic) => (AtomicTypes.Get(atomic)?.Name ?? "DINT") switch
    {
        "BYTE" or "CHAR" => "USINT", "WORD" or "WCHAR" => "UINT", "DWORD" or "TIME" or "DATE" or "TOD" or "DT" => "UDINT", "LWORD" or "LTIME" or "LDATE" or "LTOD" or "LDT" => "ULINT",
        var n => n,
    };

    /// <summary>True for a named reference to an elementary type.</summary>
    public static bool IsAtomic(IrTypeRef? t) => t is { IsNamed: true, Name: { } n } && AtomicTypes.IsAtomic(n);

    /// <summary>True for BOOL.</summary>
    public static bool IsBool(IrTypeRef? t) => t is { } x && x.Is("BOOL");

    /// <summary>True for signed, unsigned and bit-string integers (the types that support bit access and bitwise operators).</summary>
    public static bool IsIntegerAtomic(IrTypeRef? t) => t is { IsNamed: true, Name: { } n } && AtomicTypes.Get(n) is { } a && (a.IsInteger || a.Category == AtomicCategory.BitString);

    /// <summary>True for REAL and LREAL.</summary>
    public static bool IsRealAtomic(IrTypeRef? t) => t is { IsNamed: true, Name: { } n } && AtomicTypes.Get(n) is { Category: AtomicCategory.Real };

    /// <summary>Generated class name for a structure, string type or function block.</summary>
    /// <remarks>STRING keeps its name verbatim because the runtime's string wrapper and the generated class agree on it.</remarks>
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
                // BOOL arrays are packed 32 per DINT in the controller's memory; the dedicated array type indexes bits.
                if (element.Is("BOOL")) return "PlcBoolArray";
                return $"PlcArray<{FieldType(element)}>";
            }
            case "string":
                // An anonymous string type of a given capacity gets a class named after that capacity.
                return ClassName($"STRING{type.Length}");
            default:
                return "WrapDINT";
        }
    }

    /// <summary>Construction expression for a field of the given type at <paramref name="tag"/> + <paramref name="offset"/>.</summary>
    /// <param name="controller">
    /// Expression reaching the controller, needed by add-on instances (they call services). Null means the field is
    /// built inside a structure class, which has no controller of its own: the instance then binds to
    /// <c>PlcController.Current</c>, the controller constructed in this process.
    /// </param>
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
                // The element factory lets the array create element views lazily at their computed offsets.
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
    /// <remarks>
    /// The literal is what the generated class registers with the tag store so the EtherNet/IP server presents the
    /// same members, offsets and bit positions as the source controller. Structure members reference their template by
    /// name and carry CIP type code 0; atomics carry their CIP code (0xC1 BOOL ... 0xCB LREAL).
    /// </remarks>
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

    /// <summary>A C# string literal with backslashes and quotes escaped.</summary>
    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
