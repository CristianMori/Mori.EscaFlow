using System.Globalization;
using System.Text;

namespace Mori.EscaFlow.Transpiler.Printing;

/// <summary>
/// Renders bodies, statements and expressions as IEC 61131-3 structured text, for reading and for goldens.
/// Neutral constructs that ST has no syntax for are printed as pseudo-functions: <c>R_EDGE(clk, storage)</c> /
/// <c>F_EDGE(...)</c> for edges, <c>TO_&lt;type&gt;#&lt;mode&gt;(x)</c> for conversions, <c>COPY(src, dst, n)</c> and
/// <c>FILL(value, dst, n)</c> for block operations; vendor services print as <c>Service(args) (* catalog *)</c>.
/// Variable ids resolve to names through the document when one is given, else to the id's last segment.
/// </summary>
/// <remarks>
/// The printer exists for two audiences: a human reading a diff or a golden file, and the conformance suite that
/// compares printed output against a committed <c>.st</c> fixture. It is intentionally not a code generator — it
/// never claims to produce text any real ST compiler would accept for the neutral constructs above, it only needs
/// to be a faithful, stable rendering of the IR. Because of that, printed text is never parsed back; the only
/// contract is "the same tree always prints the same text" and "a human can tell what the node means".
/// </remarks>
public sealed class StPrinter
{
    private readonly IrDocument? _doc;
    private const string IndentUnit = "    ";

    /// <param name="document">Optional owning document, used to resolve <c>var</c> reference ids to their declared names; without it, a reference prints the last segment of its id.</param>
    public StPrinter(IrDocument? document = null) => _doc = document;

    // ------------------------------------------------------------------ public API

    /// <summary>Prints a body: its <c>VAR_TEMP</c> section (if any) followed by its statements.</summary>
    public string Print(IrBody body)
    {
        var sb = new StringBuilder();
        if (body.Temporaries is { Count: > 0 })
        {
            sb.Append("VAR_TEMP\n");
            foreach (var t in body.Temporaries) sb.Append(IndentUnit).Append(t.Name).Append(" : ").Append(t.Type).Append(";\n");
            sb.Append("END_VAR\n");
        }
        PrintStatements(sb, body.Statements, 0);
        return sb.ToString();
    }

    /// <summary>Prints a bare statement list with no declaration section, at indent level 0.</summary>
    public string Print(IEnumerable<IrStatement> statements)
    {
        var sb = new StringBuilder();
        PrintStatements(sb, statements, 0);
        return sb.ToString();
    }

    /// <summary>Prints one statement, including its trailing newline.</summary>
    public string Print(IrStatement statement)
    {
        var sb = new StringBuilder();
        PrintStatement(sb, statement, 0);
        return sb.ToString();
    }

    /// <summary>Prints one expression with no trailing punctuation.</summary>
    public string Print(IrExpression expression)
    {
        var sb = new StringBuilder();
        Expr(sb, expression, 0);
        return sb.ToString();
    }

    /// <summary>Declaration header (VAR sections), body and actions of a POU, as a complete ST unit.</summary>
    public string PrintPou(IrPou pou)
    {
        var sb = new StringBuilder();
        var keyword = pou.Kind switch { "functionBlock" => "FUNCTION_BLOCK", "function" => "FUNCTION", "interface" => "INTERFACE", _ => "PROGRAM" };
        sb.Append(keyword).Append(' ').Append(pou.Name);
        if (pou.ReturnType is not null) sb.Append(" : ").Append(pou.ReturnType);
        if (pou.Extends is not null) sb.Append(" EXTENDS ").Append(pou.Extends);
        if (pou.Implements is { Count: > 0 }) sb.Append(" IMPLEMENTS ").Append(string.Join(", ", pou.Implements));
        sb.Append('\n');
        // Variables are grouped by (section, retain, constant) so each VAR...END_VAR block in the output carries
        // one consistent set of qualifiers, matching how the source declares them rather than one block per var.
        foreach (var group in pou.Vars.GroupBy(v => (v.Section, v.Retain, v.Constant)))
        {
            sb.Append(SectionKeyword(group.Key.Section));
            if (group.Key.Retain == "retain") sb.Append(" RETAIN");
            else if (group.Key.Retain == "persistent") sb.Append(" PERSISTENT");
            if (group.Key.Constant) sb.Append(" CONSTANT");
            sb.Append('\n');
            foreach (var v in group)
            {
                sb.Append(IndentUnit).Append(v.Name);
                if (v.Address is not null) sb.Append(" AT ").Append(v.Address);
                sb.Append(" : ").Append(v.Type);
                if (v.Initial is not null && TryValue(v.Initial, out var init)) sb.Append(" := ").Append(init);
                sb.Append(';');
                if (v.Description is not null) sb.Append(" (* ").Append(v.Description.Replace("*)", "* )")).Append(" *)");
                sb.Append('\n');
            }
            sb.Append("END_VAR\n");
        }
        if (pou.Body is not null)
        {
            sb.Append('\n');
            sb.Append(Print(pou.Body));
        }
        // Actions are printed after the main body; a hook action (postscan, enableFalse, ...) carries a comment
        // naming the hook so a reader can tell it is not called from the main body's ordinary logic.
        foreach (var a in pou.Actions)
        {
            sb.Append("\nACTION ").Append(a.Name);
            if (a.Hook is not null) sb.Append(" (* hook: ").Append(a.Hook).Append(" *)");
            sb.Append('\n');
            var body = Print(a.Body);
            foreach (var line in body.Split('\n'))
                if (line.Length > 0) sb.Append(IndentUnit).Append(line).Append('\n');
            sb.Append("END_ACTION\n");
        }
        sb.Append("END_").Append(keyword).Append('\n');
        return sb.ToString();
    }

    private static string SectionKeyword(string section) => section switch
    {
        "input" => "VAR_INPUT",
        "output" => "VAR_OUTPUT",
        "inOut" => "VAR_IN_OUT",
        "temp" => "VAR_TEMP",
        "static" => "VAR_STAT",
        "global" => "VAR_GLOBAL",
        "external" => "VAR_EXTERNAL",
        _ => "VAR",
    };

    /// <summary>Renders an initial value as an ST literal/structured constructor; false when the value has nothing printable (e.g. an empty scalar).</summary>
    private static bool TryValue(IrValue value, out string text)
    {
        switch (value)
        {
            case IrScalarValue s:
                // Raw wins when the front-end kept the source literal text verbatim; otherwise fall back to the
                // typed fields, rendering BOOL integers as TRUE/FALSE rather than 1/0.
                text = s.Raw ?? (s.Real is { } r ? r.ToString("R", CultureInfo.InvariantCulture) : s.Int is { } i ? (s.Type.Equals("BOOL", StringComparison.OrdinalIgnoreCase) ? (i != 0 ? "TRUE" : "FALSE") : i.ToString(CultureInfo.InvariantCulture)) : "");
                return text.Length > 0;
            case IrStringValue str:
                text = "'" + str.Text.Replace("'", "$'") + "'";
                return true;
            case IrStructValue st:
            {
                var parts = new List<string>();
                foreach (var m in st.Members)
                    if (TryValue(m.Value, out var mv)) parts.Add(m.Name + " := " + mv);
                text = "(" + string.Join(", ", parts) + ")";
                return parts.Count > 0;
            }
            case IrArrayValue a:
            {
                var parts = new List<string>();
                foreach (var e in a.Elements)
                    if (TryValue(e, out var ev)) parts.Add(ev);
                text = "[" + string.Join(", ", parts) + "]";
                return parts.Count > 0;
            }
            default:
                text = "";
                return false;
        }
    }

    // ------------------------------------------------------------------ statements

    private void PrintStatements(StringBuilder sb, IEnumerable<IrStatement> statements, int level)
    {
        foreach (var s in statements) PrintStatement(sb, s, level);
    }

    /// <summary>Dispatches one statement node to its ST rendering. Every <c>IrStatement</c> subtype has a case; an unrecognised one prints as a comment rather than throwing, so a printer run never aborts on a future node kind.</summary>
    private void PrintStatement(StringBuilder sb, IrStatement s, int level)
    {
        var indent = Indent(level);
        switch (s)
        {
            case IrAssign a:
                sb.Append(indent);
                Expr(sb, a.Target, 0);
                // [:=] is the non-retentive assignment used for some pulse/one-shot semantics; ordinary ST has
                // no such operator, but the bracket form reads unambiguously next to :=.
                sb.Append(a.NonRetentive ? " [:=] " : " := ");
                Expr(sb, a.Value, 0);
                sb.Append(";\n");
                break;
            case IrFbCall c:
            {
                sb.Append(indent);
                if (c.Instance is not null) Expr(sb, c.Instance, 0);
                else sb.Append(c.Fb ?? c.Pou ?? "?");
                if (c.Method is not null) sb.Append('.').Append(c.Method);
                sb.Append('(');
                // EN first (the ladder rung condition under the en-as-bind convention), then inputs, then outputs,
                // then ENO last — the order a reader expects an FB call's argument list to follow.
                var first = true;
                if (c.En is not null)
                {
                    sb.Append("EN := ");
                    Expr(sb, c.En, 0);
                    first = false;
                }
                foreach (var (pin, value) in c.Inputs)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(pin).Append(" := ");
                    Expr(sb, value, 0);
                }
                foreach (var (pin, target) in c.Outputs)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(pin).Append(" => ");
                    Expr(sb, target, 0);
                }
                if (c.Eno is not null)
                {
                    if (!first) sb.Append(", ");
                    sb.Append("ENO => ");
                    Expr(sb, c.Eno, 0);
                }
                sb.Append(");");
                // The instance's declared type/profile and a non-normal mode (prescan/postscan/enableFalse) are not
                // part of standard ST call syntax, so they are appended as a trailing comment instead of invented syntax.
                var notes = new List<string>();
                if (c.Instance is not null && (c.Fb ?? c.Pou) is { } typeName) notes.Add(typeName + (c.Profile is not null ? "/" + c.Profile : ""));
                if (c.Mode != "normal") notes.Add("mode: " + c.Mode);
                if (notes.Count > 0) sb.Append(" (* ").Append(string.Join(", ", notes)).Append(" *)");
                sb.Append('\n');
                break;
            }
            case IrFnCall f:
                sb.Append(indent);
                // A standard function with an explicit en that is not implied by argument evaluation (e.g. a
                // gated vendor extension function) prints its gate as a leading comment, since ST functions have
                // no enable concept.
                if (f.En is not null)
                {
                    sb.Append("(* EN := ");
                    Expr(sb, f.En, 0);
                    sb.Append(" *) ");
                }
                sb.Append(f.Name ?? f.Pou ?? "?").Append('(');
                Args(sb, f.Args);
                if (f.Outputs is { Count: > 0 })
                {
                    if (f.Args.Count > 0) sb.Append(", ");
                    sb.Append(string.Join(", ", f.Outputs.Select(o => o.Key + " => " + Print(o.Value))));
                }
                sb.Append(");\n");
                break;
            case IrActionCall ac:
                // An action call with en prints as a guarded IF because ST has no conditional-call syntax of its own;
                // this mirrors how a ladder rung's condition gates a JSR/action call in the source.
                sb.Append(indent);
                if (ac.En is not null)
                {
                    sb.Append("IF ");
                    Expr(sb, ac.En, 0);
                    sb.Append(" THEN ");
                }
                if (ac.Pou is not null) sb.Append(ac.Pou).Append('.');
                sb.Append(ac.Action).Append('(');
                sb.Append(string.Join(", ", ac.Inputs.Select(Print)));
                if (ac.Outputs.Count > 0)
                {
                    sb.Append("; ");
                    sb.Append(string.Join(", ", ac.Outputs.Select(Print)));
                }
                sb.Append(");");
                if (ac.En is not null) sb.Append(" END_IF;");
                sb.Append('\n');
                break;
            case IrSysCall sc:
                // Vendor services have no ST syntax at all; printed as a call with the owning catalog noted,
                // so a reader always knows which vendor's manual to check for the service's behaviour.
                sb.Append(indent);
                if (sc.En is not null)
                {
                    sb.Append("(* EN := ");
                    Expr(sb, sc.En, 0);
                    sb.Append(" *) ");
                }
                sb.Append(sc.Service).Append('(');
                Args(sb, sc.Args);
                sb.Append("); (* ").Append(sc.Catalog).Append(" *)\n");
                break;
            case IrCopy cp:
                // COPY / COPY_ATOMIC are the printer's pseudo-functions for block-memory copy (no IEC syntax exists);
                // "bytes" mode is noted because it changes count's unit from elements to raw bytes.
                sb.Append(indent);
                if (cp.En is not null)
                {
                    sb.Append("(* EN := ");
                    Expr(sb, cp.En, 0);
                    sb.Append(" *) ");
                }
                sb.Append(cp.Atomic ? "COPY_ATOMIC(" : "COPY(");
                Expr(sb, cp.Source, 0);
                sb.Append(", ");
                Expr(sb, cp.Dest, 0);
                sb.Append(", ");
                Expr(sb, cp.Count, 0);
                sb.Append(");");
                if (cp.Mode == "bytes") sb.Append(" (* bytes *)");
                sb.Append('\n');
                break;
            case IrFill fl:
                sb.Append(indent);
                if (fl.En is not null)
                {
                    sb.Append("(* EN := ");
                    Expr(sb, fl.En, 0);
                    sb.Append(" *) ");
                }
                sb.Append("FILL(");
                Expr(sb, fl.Value, 0);
                sb.Append(", ");
                Expr(sb, fl.Dest, 0);
                sb.Append(", ");
                Expr(sb, fl.Count, 0);
                sb.Append(");\n");
                break;
            case IrIf i:
                for (var b = 0; b < i.Branches.Count; b++)
                {
                    sb.Append(indent).Append(b == 0 ? "IF " : "ELSIF ");
                    Expr(sb, i.Branches[b].Condition, 0);
                    sb.Append(" THEN\n");
                    PrintStatements(sb, i.Branches[b].Body, level + 1);
                }
                if (i.Else is not null)
                {
                    sb.Append(indent).Append("ELSE\n");
                    PrintStatements(sb, i.Else, level + 1);
                }
                sb.Append(indent).Append("END_IF;\n");
                break;
            case IrCase c:
                sb.Append(indent).Append("CASE ");
                Expr(sb, c.Selector, 0);
                sb.Append(" OF\n");
                foreach (var arm in c.Arms)
                {
                    sb.Append(Indent(level + 1));
                    // A labels.To forms an IEC "a..b" range label; single-value labels print bare.
                    sb.Append(string.Join(", ", arm.Labels.Select(l => l.To is null ? Print(l.Value) : Print(l.Value) + ".." + Print(l.To))));
                    sb.Append(":\n");
                    PrintStatements(sb, arm.Body, level + 2);
                }
                if (c.Else is not null)
                {
                    sb.Append(Indent(level + 1)).Append("ELSE\n");
                    PrintStatements(sb, c.Else, level + 2);
                }
                sb.Append(indent).Append("END_CASE;\n");
                break;
            case IrFor f:
                sb.Append(indent).Append("FOR ");
                Expr(sb, f.Variable, 0);
                sb.Append(" := ");
                Expr(sb, f.From, 0);
                sb.Append(" TO ");
                Expr(sb, f.To, 0);
                if (f.By is not null)
                {
                    sb.Append(" BY ");
                    Expr(sb, f.By, 0);
                }
                sb.Append(" DO\n");
                PrintStatements(sb, f.Body, level + 1);
                sb.Append(indent).Append("END_FOR;\n");
                break;
            case IrWhile w:
                sb.Append(indent).Append("WHILE ");
                Expr(sb, w.Condition, 0);
                sb.Append(" DO\n");
                PrintStatements(sb, w.Body, level + 1);
                sb.Append(indent).Append("END_WHILE;\n");
                break;
            case IrRepeat r:
                sb.Append(indent).Append("REPEAT\n");
                PrintStatements(sb, r.Body, level + 1);
                sb.Append(indent).Append("UNTIL ");
                Expr(sb, r.Until, 0);
                sb.Append("\n").Append(indent).Append("END_REPEAT;\n");
                break;
            case IrExit:
                sb.Append(indent).Append("EXIT;\n");
                break;
            case IrContinue:
                sb.Append(indent).Append("CONTINUE;\n");
                break;
            case IrReturn r:
                // A conditional return (JSR-RET chains lowered from a ladder branch) prints as IF...THEN RETURN;
                // since ST's RETURN itself takes no condition.
                sb.Append(indent);
                if (r.En is not null)
                {
                    sb.Append("IF ");
                    Expr(sb, r.En, 0);
                    sb.Append(" THEN RETURN; END_IF;\n");
                }
                else sb.Append("RETURN;\n");
                break;
            case IrJump j:
                // JMP is not standard ST (IEC has no goto in the textual language) but is kept because it is how
                // ladder's rung-to-rung jumps lower without restructuring control flow; conditional like RETURN above.
                sb.Append(indent);
                if (j.En is not null)
                {
                    sb.Append("IF ");
                    Expr(sb, j.En, 0);
                    sb.Append(" THEN JMP ").Append(j.Label).Append("; END_IF;\n");
                }
                else sb.Append("JMP ").Append(j.Label).Append(";\n");
                break;
            case IrLabel l:
                sb.Append(indent).Append(l.Name).Append(":\n");
                break;
            case IrErrorStatement e:
                // A statement the front-end could not lower is kept in the tree (rather than dropped) so the
                // document still round-trips; the printer surfaces it as a comment instead of failing.
                sb.Append(indent).Append("(* error: ").Append(e.Text.Replace("*)", "* )")).Append(" *)\n");
                break;
            default:
                sb.Append(indent).Append("(* unsupported statement ").Append(s.GetType().Name).Append(" *)\n");
                break;
        }
    }

    private void Args(StringBuilder sb, List<IrExpression?> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            // A null slot is an omitted optional argument (e.g. a skipped output parameter); printed as nothing
            // between the commas, matching how IEC allows eliding trailing/positional arguments.
            if (args[i] is { } a) Expr(sb, a, 0);
        }
    }

    private static string Indent(int level)
    {
        var sb = new StringBuilder(level * IndentUnit.Length);
        for (var i = 0; i < level; i++) sb.Append(IndentUnit);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ expressions

    /// <summary>Binding strength of each binary operator, IEC order: OR weakest, exponentiation strongest.</summary>
    private static int Precedence(string op) => op switch
    {
        "or" => 1,
        "xor" => 2,
        "and" => 3,
        "eq" or "ne" or "lt" or "gt" or "le" or "ge" => 4,
        "add" or "sub" => 5,
        "mul" or "div" or "mod" => 6,
        "pow" => 8,
        _ => 9,
    };

    private const int UnaryPrecedence = 7;
    private const int Atom = 10;

    private static int PrecedenceOf(IrExpression e) => e switch
    {
        IrBinary b => Precedence(b.Operator),
        IrUnary => UnaryPrecedence,
        _ => Atom,
    };

    private static string OperatorText(string op) => op switch
    {
        "add" => "+",
        "sub" => "-",
        "mul" => "*",
        "div" => "/",
        "mod" => "MOD",
        "pow" => "**",
        "eq" => "=",
        "ne" => "<>",
        "lt" => "<",
        "gt" => ">",
        "le" => "<=",
        "ge" => ">=",
        "and" => "AND",
        "or" => "OR",
        "xor" => "XOR",
        _ => op.ToUpperInvariant(),
    };

    /// <summary>Prints <paramref name="e"/>, parenthesizing it when its precedence is below <paramref name="minPrecedence"/>.</summary>
    /// <remarks>This is the standard "minimum binding power" algorithm for precedence-correct printing without a full re-parse: each call tells its child the weakest precedence it may print unparenthesized at that position.</remarks>
    private void Expr(StringBuilder sb, IrExpression e, int minPrecedence)
    {
        var p = PrecedenceOf(e);
        var parens = p < minPrecedence;
        if (parens) sb.Append('(');
        switch (e)
        {
            case IrRef r:
                Ref(sb, r);
                break;
            case IrLiteral l:
                Literal(sb, l);
                break;
            case IrUnary u:
                sb.Append(u.Operator == "not" ? "NOT " : "-");
                Expr(sb, u.Operand, UnaryPrecedence);
                break;
            case IrBinary b:
            {
                var prec = Precedence(b.Operator);
                Expr(sb, b.Left, prec);
                sb.Append(' ').Append(OperatorText(b.Operator)).Append(' ');
                // Right operand of a left-associative operator at the same level needs parentheses (a - (b - c));
                // pow is right-associative in IEC, so its right side may print at its own precedence unparenthesized.
                Expr(sb, b.Right, b.Operator == "pow" ? prec : prec + 1);
                break;
            }
            case IrFunction f:
                sb.Append(f.Name ?? f.Pou ?? "?").Append('(');
                Args(sb, f.Args);
                sb.Append(')');
                break;
            case IrConv c:
                // conv prints as TO_<type>#<mode>(x): the #<mode> suffix is not real ST syntax, but no single IEC
                // conversion function distinguishes truncating, rounding and reinterpreting conversions the way
                // this node's mode does, so the suffix keeps that distinction visible.
                sb.Append("TO_").Append(c.To.ToString().ToUpperInvariant()).Append('#').Append(c.Mode).Append('(');
                Expr(sb, c.Operand, 0);
                sb.Append(')');
                break;
            case IrBitAccess ba:
                // A literal integer bit index prints as .N (the common "word.7" form); a computed index prints as
                // .[expr], since IEC has no syntax for a variable bit index at all.
                Expr(sb, ba.Of, Atom);
                sb.Append('.');
                if (ba.Bit is IrLiteral { Kind: "int" } bl) sb.Append(bl.Int?.ToString(CultureInfo.InvariantCulture) ?? "0");
                else
                {
                    sb.Append('[');
                    Expr(sb, ba.Bit, 0);
                    sb.Append(']');
                }
                break;
            case IrEdge ed:
                sb.Append(ed.Kind == "falling" ? "F_EDGE(" : "R_EDGE(");
                Expr(sb, ed.Clk, 0);
                sb.Append(", ");
                Expr(sb, ed.Storage, 0);
                sb.Append(')');
                break;
            case IrSysFn sf:
                sb.Append(sf.Service).Append('(');
                Args(sb, sf.Args);
                sb.Append(')');
                break;
            case IrKeyword k:
                // A keyword expression carries vendor enumeration text (e.g. a GSV class/attribute name) that the
                // model does not otherwise type; printed verbatim since it already is valid-looking ST-ish text.
                sb.Append(k.Text);
                break;
            case IrPlaceholder:
                sb.Append('?');
                break;
            case IrErrorExpression err:
                sb.Append("(* error: ").Append(err.Text.Replace("*)", "* )")).Append(" *)");
                break;
            default:
                sb.Append("(* unsupported expression ").Append(e.GetType().Name).Append(" *)");
                break;
        }
        if (parens) sb.Append(')');
    }

    /// <summary>Prints a reference: its root followed by each path segment in order.</summary>
    private void Ref(StringBuilder sb, IrRef r)
    {
        sb.Append(RootName(r.Root));
        foreach (var seg in r.Path)
        {
            switch (seg)
            {
                case IrMemberSegment m:
                    sb.Append('.').Append(m.Name);
                    break;
                case IrIndexSegment i:
                    sb.Append('[');
                    for (var k = 0; k < i.Indices.Count; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        Expr(sb, i.Indices[k], 0);
                    }
                    sb.Append(']');
                    break;
                case IrLengthSegment:
                    // STRING length has no IEC accessor syntax of its own; LEN mirrors the structure's LEN member
                    // the way most vendor runtimes actually store a string's current length.
                    sb.Append(".LEN");
                    break;
                case IrCharSegment c:
                    sb.Append(".DATA[");
                    Expr(sb, c.Index, 0);
                    sb.Append(']');
                    break;
                case IrDerefSegment:
                    sb.Append('^');
                    break;
            }
        }
    }

    /// <summary>Resolves a reference root to display text: a <c>var</c> root looks up the declared name through the document when one was given, else falls back to the id's last path segment; other roots print their own name (<c>this</c> prints <c>THIS^</c>).</summary>
    private string RootName(IrRefRoot root)
    {
        switch (root.Kind)
        {
            case "var":
            {
                if (root.Id is null) return "?";
                if (_doc?.FindVariable(root.Id) is { } v) return v.Name;
                var slash = root.Id.LastIndexOf('/');
                return slash >= 0 ? root.Id[(slash + 1)..] : root.Id;
            }
            case "this":
                return "THIS^";
            default:
                return root.Name ?? "?";
        }
    }

    /// <summary>Prints a literal in its IEC radix/quoting form.</summary>
    private static void Literal(StringBuilder sb, IrLiteral l)
    {
        switch (l.Kind)
        {
            case "bool":
                sb.Append(l.Bool == true ? "TRUE" : "FALSE");
                break;
            case "int":
            {
                var v = l.Int ?? 0;
                switch (l.Radix)
                {
                    case 2: sb.Append("2#").Append(Convert.ToString(v, 2)); break;
                    case 8: sb.Append("8#").Append(Convert.ToString(v, 8)); break;
                    case 16: sb.Append("16#").Append(v.ToString("X", CultureInfo.InvariantCulture)); break;
                    default: sb.Append(v.ToString(CultureInfo.InvariantCulture)); break;
                }
                break;
            }
            case "real":
            {
                var v = l.Real ?? 0;
                var text = v.ToString("R", CultureInfo.InvariantCulture);
                // A real that round-trips to an integer-looking string ("3") still needs a decimal point so the
                // printed text is unambiguously a REAL literal, not a DINT one.
                if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e') && !text.Contains("NaN") && !text.Contains("Infinity")) text += ".0";
                sb.Append(text);
                break;
            }
            case "string":
                // IEC single-quoted strings escape '$' and the quote character with a leading '$'.
                sb.Append('\'').Append((l.String ?? "").Replace("$", "$$").Replace("'", "$'")).Append('\'');
                break;
            case "wstring":
                sb.Append('"').Append((l.String ?? "").Replace("$", "$$").Replace("\"", "$\"")).Append('"');
                break;
            default:
                sb.Append(l.Text ?? l.Int?.ToString(CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }
}
