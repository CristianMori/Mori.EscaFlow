using System.Globalization;
using Plc.Ir;
using Plc.Ir.Catalogs;
using Plc.Ir.Logix;

namespace Plc.Generator.CSharp;

/// <summary>Emits C# expressions for IR expressions. Atomic leaves read <c>.Value</c>; objects (structures, arrays, whole wrappers) are passed as such.</summary>
public sealed class CSharpExpressions
{
    private readonly IrDocument _doc;
    private readonly NameMapper _names;
    private readonly CSharpTypes _types;
    private readonly GenerationReport _report;

    public CSharpExpressions(IrDocument doc, NameMapper names, CSharpTypes types, GenerationReport report)
    {
        _doc = doc;
        _names = names;
        _types = types;
        _report = report;
    }

    public const string ControllerScope = "controller";

    /// <summary>Field name of a program instance in the controller (keyed apart from controller tags of the same name).</summary>
    public string ProgramInstance(string pouName) => _names.Map(ControllerScope, "program:" + pouName);

    /// <summary>Method name of an action (routine) in its POU class (keyed apart from variables of the same name).</summary>
    public string ActionMethod(IrPou pou, string action) => _names.Map(pou.Kind == "functionBlock" ? MemberScope : PouScope(pou.Name), "action:" + action);

    /// <summary>Structure members and function block parameters / locals share one naming scope, so member access paths agree with the declaring class.</summary>
    public const string MemberScope = "members";
    public static string PouScope(string pou) => "pou:" + pou;

    // ------------------------------------------------------------------ variable access

    /// <summary>The C# expression that reaches the object bound to a variable id from the current context.</summary>
    public string Access(string id, EmitContext ctx)
    {
        var v = _doc.FindVariable(id);
        if (v is null) return $"/* unresolved {id} */ null!";
        var parts = id.Split('/');
        if (parts[0] == "global")
        {
            var field = _names.Map(ControllerScope, v.Name);
            return ctx.Kind == EmitContext.Scope.Controller ? field : ctx.ControllerAccess + "." + field;
        }
        // pou/<pou>/<name>: program variables live in the program's scope, function block variables in the shared member scope.
        var pouName = parts[1];
        var isBlock = _doc.FindPou(pouName) is { Kind: "functionBlock" };
        var member = _names.Map(isBlock ? MemberScope : PouScope(pouName), v.Name);
        if (ctx.Pou is not null && ctx.Pou.Name.Equals(pouName, StringComparison.OrdinalIgnoreCase)) return member;
        // Cross-program reference (\Prog.Tag): through the controller's program instance.
        var instance = _names.Map(ControllerScope, pouName);
        var prefix = ctx.Kind == EmitContext.Scope.Controller ? "" : ctx.ControllerAccess + ".";
        return prefix + instance + "." + member;
    }

    /// <summary>The object expression for a reference (no trailing .Value).</summary>
    public string Object(IrRef r, EmitContext ctx)
    {
        string root;
        switch (r.Root.Kind)
        {
            case "var": root = Access(r.Root.Id ?? "", ctx); break;
            case "temp": root = r.Root.Name ?? "_t"; break;
            case "system": return $"{ctx.Sys}.GetBit({CSharpTypes.Quote(r.Root.Name ?? "")})";
            case "this": root = "this"; break;
            default: return $"/* unresolved {r.Root.Name} */ null!";
        }
        var sb = new System.Text.StringBuilder(root);
        var stringType = false;
        foreach (var seg in r.Path)
        {
            switch (seg)
            {
                case IrMemberSegment m:
                    sb.Append('.').Append(MemberName(m.Name));
                    break;
                case IrIndexSegment i:
                    sb.Append('[').Append(string.Join(", ", i.Indices.Select(x => Int(x, ctx)))).Append(']');
                    break;
                case IrLengthSegment:
                    sb.Append(".LEN");
                    stringType = true;
                    break;
                case IrCharSegment c:
                    sb.Append(".DATA[").Append(Int(c.Index, ctx)).Append(']');
                    stringType = true;
                    break;
                case IrDerefSegment:
                    break;
            }
        }
        _ = stringType;
        return sb.ToString();
    }

    private string MemberName(string name) => _names.Map(MemberScope, name);

    /// <summary>True when the reference's leaf is an atomic wrapper (reads need .Value).</summary>
    public static bool IsAtomicLeaf(IrRef r) => r.Type is { } t && CSharpTypes.IsAtomic(t) && !t.IsArray;

    // ------------------------------------------------------------------ values

    /// <summary>The C# value expression (rvalue) for an expression.</summary>
    public string Value(IrExpression e, EmitContext ctx)
    {
        switch (e)
        {
            case IrRef r:
                if (r.Root.Kind == "temp") return r.Root.Name ?? "_t";
                if (r.Root.Kind == "system") return Object(r, ctx);
                return IsAtomicLeaf(r) ? Object(r, ctx) + ".Value" : Object(r, ctx);
            case IrLiteral l:
                return Literal(l);
            case IrUnary u:
            {
                var operand = Value(u.Operand, ctx);
                if (u.Operator == "not") return CSharpTypes.IsBool(u.Operand.Type) || u.Operand.Type is null ? $"!{Paren(operand)}" : $"~{Paren(operand)}";
                return $"-{Paren(operand)}";
            }
            case IrBinary b:
                return Binary(b, ctx);
            case IrFunction f:
            {
                // Standard functions take values; structures, arrays and strings are passed as objects.
                var args = string.Join(", ", f.Args.Select(a => a is null ? "null" : a is IrRef r && !IsAtomicLeaf(r) ? Object(r, ctx) : Value(a, ctx)));
                if (f.Pou is not null) return $"{_names.Map(CSharpTypes.TypeScope, f.Pou)}.Invoke({args})";
                _report.Count(_report.FunctionInventory, f.Name ?? "?");
                return $"PlcFn.{f.Name}({args})";
            }
            case IrConv c:
            {
                var operand = Value(c.Operand, ctx);
                var to = CSharpTypes.ValueType(c.To.Name ?? "DINT");
                return c.Mode switch
                {
                    "roundEven" when CSharpTypes.IsIntegerAtomic(c.To) => $"({to})PlcMath.RoundEven({operand})",
                    "roundEven" => $"({to})({operand})",
                    "trunc" => $"({to})PlcMath.Trunc({operand})",
                    "saturate" => $"PlcMath.Saturate<{to}>({operand})",
                    _ => $"unchecked(({to})({operand}))",
                };
            }
            case IrBitAccess ba:
                return $"{Object((IrRef)ba.Of, ctx)}[{Int(ba.Bit, ctx)}]";
            case IrEdge ed:
                return $"PlcLogic.{(ed.Kind == "rising" ? "RisingEdge" : "FallingEdge")}({Value(ed.Clk, ctx)}, {Arg(ed.Storage, ctx)})";
            case IrSysFn sf:
            {
                _report.Count(_report.ServiceInventory, sf.Service);
                var args = string.Join(", ", sf.Args.Select(a => a is null ? "null" : Arg(a, ctx)));
                return $"{ctx.Services}.{SafeService(sf.Service)}({args})";
            }
            case IrKeyword k:
                return CSharpTypes.Quote(k.Text);
            case IrPlaceholder:
                return "null";
            case IrErrorExpression err:
                return $"/* error: {err.Text.Replace("*/", "* /")} */ default";
            default:
                return "default";
        }
    }

    /// <summary>An argument passed to a runtime call: wrappers and structures as objects, everything else as values.</summary>
    public string Arg(IrExpression e, EmitContext ctx) => e switch
    {
        IrRef r when r.Root.Kind is "var" or "this" => Object(r, ctx),
        IrBitAccess ba => $"new PlcBitRef({Object((IrRef)ba.Of, ctx)}, {Int(ba.Bit, ctx)})",
        _ => Value(e, ctx),
    };

    /// <summary>An index or count: an int expression.</summary>
    public string Int(IrExpression e, EmitContext ctx)
    {
        var v = Value(e, ctx);
        return e.Type is { } t && t.Is("DINT") || e is IrLiteral { Kind: "int" } ? v : $"(int)({v})";
    }

    /// <summary>A condition: a bool expression.</summary>
    public string Bool(IrExpression e, EmitContext ctx) => AsBool(e, Value(e, ctx));

    /// <summary>Turns an already emitted value into a bool expression (no re-evaluation of the tree).</summary>
    private static string AsBool(IrExpression e, string value)
    {
        if (CSharpTypes.IsBool(e.Type) || e is IrLiteral { Kind: "bool" } || e is IrBinary { IsComparison: true } || e is IrEdge || e is IrBitAccess) return value;
        if (e.Type is null) return value;
        return $"({value} != 0)";
    }

    private string Binary(IrBinary b, EmitContext ctx)
    {
        var l = Value(b.Left, ctx);
        var r = Value(b.Right, ctx);
        var bools = CSharpTypes.IsBool(b.Left.Type) && CSharpTypes.IsBool(b.Right.Type) || b.IsLogical && b.Type is { } bt && bt.Is("BOOL");
        var op = b.Operator switch
        {
            "add" => "+", "sub" => "-", "mul" => "*", "div" => "/", "mod" => "%",
            "eq" => "==", "ne" => "!=", "lt" => "<", "gt" => ">", "le" => "<=", "ge" => ">=",
            "and" => bools ? "&&" : "&", "or" => bools ? "||" : "|", "xor" => "^",
            "pow" => null,
            _ => "+",
        };
        if (op is null)
        {
            var real = CSharpTypes.IsRealAtomic(b.Type) || b.Type is null;
            return real ? $"({CSharpTypes.ValueType(b.Type?.LeafName ?? "REAL")})PlcMath.Pow({l}, {r})" : $"({CSharpTypes.ValueType(b.Type?.LeafName ?? "DINT")})PlcMath.Pow({l}, {r})";
        }
        // Division and modulo follow Logix: a zero divisor yields the saturated value (integers) or infinity (reals) and never faults.
        if (b.Operator == "div") return $"PlcMath.DIV({l}, {r})";
        if (b.Operator == "mod") return $"PlcMath.MOD({l}, {r})";
        if (bools && b.Operator is "and" or "or" or "xor")
            return $"{Paren(AsBool(b.Left, l))} {op} {Paren(AsBool(b.Right, r))}";
        // Comparisons between BOOL and integers, or logical ops on mixed operands, need both sides as the same kind.
        if (b.IsComparison && CSharpTypes.IsBool(b.Left.Type) != CSharpTypes.IsBool(b.Right.Type) && b.Left.Type is not null && b.Right.Type is not null)
        {
            if (CSharpTypes.IsBool(b.Left.Type)) l = $"({l} ? 1 : 0)";
            else r = $"({r} ? 1 : 0)";
        }
        if (b.IsLogical && !bools && (CSharpTypes.IsBool(b.Left.Type) || CSharpTypes.IsBool(b.Right.Type)))
        {
            if (CSharpTypes.IsBool(b.Left.Type)) l = $"({l} ? 1 : 0)";
            if (CSharpTypes.IsBool(b.Right.Type)) r = $"({r} ? 1 : 0)";
        }
        return $"{Paren(l)} {op} {Paren(r)}";
    }

    private static string Paren(string s)
    {
        if (s.Length == 0) return s;
        var simple = true;
        var depth = 0;
        foreach (var ch in s)
        {
            if (ch == '(' || ch == '[') depth++;
            else if (ch == ')' || ch == ']') depth--;
            else if (depth == 0 && (ch == ' ' || ch == '?')) { simple = false; break; }
        }
        return simple ? s : "(" + s + ")";
    }

    public static string Literal(IrLiteral l) => l.Kind switch
    {
        "bool" => l.Bool == true ? "true" : "false",
        "int" => IntLiteral(l),
        "real" => RealLiteral(l),
        "string" or "wstring" => CSharpTypes.Quote(l.String ?? ""),
        "enum" => CSharpTypes.Quote(l.Text ?? ""),
        _ => l.Int is { } i ? i.ToString(CultureInfo.InvariantCulture) : CSharpTypes.Quote(l.Text ?? ""),
    };

    private static string IntLiteral(IrLiteral l)
    {
        var v = l.Int ?? 0;
        var type = l.Type?.LeafName ?? "DINT";
        return type.ToUpperInvariant() switch
        {
            "LINT" => v.ToString(CultureInfo.InvariantCulture) + "L",
            "ULINT" or "LWORD" => ((ulong)v).ToString(CultureInfo.InvariantCulture) + "UL",
            "UDINT" or "DWORD" => v > int.MaxValue ? ((uint)v).ToString(CultureInfo.InvariantCulture) + "U" : v.ToString(CultureInfo.InvariantCulture),
            "REAL" => v.ToString(CultureInfo.InvariantCulture) + "f",
            "LREAL" => v.ToString(CultureInfo.InvariantCulture) + ".0",
            _ => v is > int.MaxValue or < int.MinValue ? v.ToString(CultureInfo.InvariantCulture) + "L" : v.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string RealLiteral(IrLiteral l)
    {
        var v = l.Real ?? 0;
        var lreal = l.Type is { } t && t.Is("LREAL");
        if (double.IsNaN(v)) return lreal ? "double.NaN" : "float.NaN";
        if (double.IsPositiveInfinity(v)) return lreal ? "double.PositiveInfinity" : "float.PositiveInfinity";
        if (double.IsNegativeInfinity(v)) return lreal ? "double.NegativeInfinity" : "float.NegativeInfinity";
        var text = v.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e')) text += ".0";
        return lreal ? text : text + "f";
    }

    public static string SafeService(string service) => service.All(c => char.IsLetterOrDigit(c) || c == '_') ? service : "Call_" + new string(service.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
