using System.Text;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;

public enum UnaryOp
{
    Negate,
    Not,
}

public enum BinaryOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessOrEqual,
    GreaterOrEqual,
    And,
    Or,
    Xor,
}

public static class BinaryOpExtensions
{
    public static string ToLogixText(this BinaryOp op) => op switch
    {
        BinaryOp.Add => "+",
        BinaryOp.Subtract => "-",
        BinaryOp.Multiply => "*",
        BinaryOp.Divide => "/",
        BinaryOp.Modulo => "MOD",
        BinaryOp.Power => "**",
        BinaryOp.Equal => "=",
        BinaryOp.NotEqual => "<>",
        BinaryOp.Less => "<",
        BinaryOp.Greater => ">",
        BinaryOp.LessOrEqual => "<=",
        BinaryOp.GreaterOrEqual => ">=",
        BinaryOp.And => "AND",
        BinaryOp.Or => "OR",
        BinaryOp.Xor => "XOR",
        _ => op.ToString(),
    };

    /// <summary>Higher binds tighter. Mirrors the grammar levels in the ST parser.</summary>
    public static int Precedence(this BinaryOp op) => op switch
    {
        BinaryOp.Or => 1,
        BinaryOp.Xor => 2,
        BinaryOp.And => 3,
        BinaryOp.Equal or BinaryOp.NotEqual or BinaryOp.Less or BinaryOp.Greater or BinaryOp.LessOrEqual or BinaryOp.GreaterOrEqual => 4,
        BinaryOp.Add or BinaryOp.Subtract => 5,
        BinaryOp.Multiply or BinaryOp.Divide or BinaryOp.Modulo => 6,
        BinaryOp.Power => 8,
        _ => 0,
    };
}

/// <summary>Base of every structured text AST node: every node carries its source span for diagnostics and round-tripping.</summary>
public abstract record StNode(TextSpan Span);

/// <summary>A sequence of statements (a routine body or the body of a control construct).</summary>
public sealed record StBlock(TextSpan Span, IReadOnlyList<StStatement> Statements) : StNode(Span)
{
    public static readonly StBlock Empty = new(TextSpan.Empty, Array.Empty<StStatement>());
}

/// <summary>One statement: an assignment, a call, a control construct (IF/CASE/FOR/WHILE/REPEAT), EXIT/RETURN, an empty <c>;</c>, or text the parser could not understand.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StAssignment), "assign")]
[JsonDerivedType(typeof(StCallStatement), "call")]
[JsonDerivedType(typeof(StIf), "if")]
[JsonDerivedType(typeof(StCase), "case")]
[JsonDerivedType(typeof(StFor), "for")]
[JsonDerivedType(typeof(StWhile), "while")]
[JsonDerivedType(typeof(StRepeat), "repeat")]
[JsonDerivedType(typeof(StExit), "exit")]
[JsonDerivedType(typeof(StReturn), "return")]
[JsonDerivedType(typeof(StEmptyStatement), "empty")]
[JsonDerivedType(typeof(StErrorStatement), "error")]
public abstract record StStatement(TextSpan Span) : StNode(Span);

/// <summary><c>target := value;</c> or the SFC non-retentive form <c>target [:=] value;</c> (the assigned value is not retained through a power cycle).</summary>
public sealed record StAssignment(TextSpan Span, StVariable Target, StExpression Value, bool NonRetentive) : StStatement(Span);

/// <summary>An instruction or add-on instruction invoked as a statement, e.g. <c>TONR(t);</c> or <c>JSR(Sub);</c>. Null arguments are elided positions.</summary>
public sealed record StCallStatement(TextSpan Span, string Name, IReadOnlyList<StExpression?> Arguments, bool IsAddOn) : StStatement(Span);

/// <summary>One <c>IF</c>/<c>ELSIF</c> condition and the body that runs when it (and no earlier branch) is true.</summary>
public sealed record StIfBranch(StExpression Condition, StBlock Body);

public sealed record StIf(TextSpan Span, IReadOnlyList<StIfBranch> Branches, StBlock? Else) : StStatement(Span);

/// <summary>A single value (<c>To</c> null) or an inclusive range <c>From..To</c> that a CASE arm matches.</summary>
public sealed record StCaseLabel(long From, long? To)
{
    public bool IsRange => To.HasValue;
}

/// <summary>One CASE arm: the label(s) that select it and the body that runs when the selector matches one of them.</summary>
public sealed record StCaseArm(IReadOnlyList<StCaseLabel> Labels, StBlock Body);

public sealed record StCase(TextSpan Span, StExpression Selector, IReadOnlyList<StCaseArm> Arms, StBlock? Else) : StStatement(Span);

/// <summary><c>FOR Variable := From TO To [BY By] DO Body END_FOR;</c>.</summary>
public sealed record StFor(TextSpan Span, StVariable Variable, StExpression From, StExpression To, StExpression? By, StBlock Body) : StStatement(Span);

public sealed record StWhile(TextSpan Span, StExpression Condition, StBlock Body) : StStatement(Span);

/// <summary><c>REPEAT Body UNTIL Until END_REPEAT;</c> — the body always runs at least once.</summary>
public sealed record StRepeat(TextSpan Span, StBlock Body, StExpression Until) : StStatement(Span);

/// <summary>Breaks out of the innermost enclosing FOR/WHILE/REPEAT.</summary>
public sealed record StExit(TextSpan Span) : StStatement(Span);

/// <summary>Ends the routine immediately.</summary>
public sealed record StReturn(TextSpan Span) : StStatement(Span);

/// <summary>A lone <c>;</c>.</summary>
public sealed record StEmptyStatement(TextSpan Span) : StStatement(Span);

/// <summary>Text the parser could not understand; the diagnostic carries the reason.</summary>
public sealed record StErrorStatement(TextSpan Span, string RawText) : StStatement(Span);

/// <summary>One expression: a literal, a variable, a unary/binary operation, a function call, a reclassified keyword, or an unparseable fragment.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StLiteral), "literal")]
[JsonDerivedType(typeof(StVariable), "var")]
[JsonDerivedType(typeof(StUnary), "unary")]
[JsonDerivedType(typeof(StBinary), "binary")]
[JsonDerivedType(typeof(StCall), "call")]
[JsonDerivedType(typeof(StKeyword), "keyword")]
[JsonDerivedType(typeof(StErrorExpression), "error")]
public abstract record StExpression(TextSpan Span) : StNode(Span)
{
    public abstract string ToLogixText();
    public override string ToString() => ToLogixText();
}

public sealed record StLiteral(TextSpan Span, LiteralValue Value) : StExpression(Span)
{
    public override string ToLogixText() => Value.ToLogixText();
}

/// <summary>A tag reference (possibly with members, indices and a bit number).</summary>
public sealed record StVariable(TextSpan Span, OperandPath Path) : StExpression(Span)
{
    public override string ToLogixText() => Path.ToLogixText();
}

public sealed record StUnary(TextSpan Span, UnaryOp Op, StExpression Operand) : StExpression(Span)
{
    public override string ToLogixText()
    {
        var inner = Operand is StBinary ? "(" + Operand.ToLogixText() + ")" : Operand.ToLogixText();
        return Op == UnaryOp.Not ? "NOT " + inner : "-" + inner;
    }
}

public sealed record StBinary(TextSpan Span, BinaryOp Op, StExpression Left, StExpression Right) : StExpression(Span)
{
    public override string ToLogixText()
    {
        var sb = new StringBuilder();
        AppendOperand(sb, Left, isRight: false);
        var op = Op.ToLogixText();
        if (char.IsLetter(op[0])) sb.Append(' ').Append(op).Append(' ');
        else sb.Append(' ').Append(op).Append(' ');
        AppendOperand(sb, Right, isRight: true);
        return sb.ToString();
    }

    /// <summary>Adds parentheses around a nested binary operand whose precedence would otherwise change the parse on re-reading, including the right operand of a same-precedence left-associative operator.</summary>
    private void AppendOperand(StringBuilder sb, StExpression operand, bool isRight)
    {
        var needParens = operand is StBinary b &&
            (b.Op.Precedence() < Op.Precedence() || (isRight && b.Op.Precedence() == Op.Precedence() && Op != BinaryOp.Power));
        if (needParens) sb.Append('(');
        sb.Append(operand.ToLogixText());
        if (needParens) sb.Append(')');
    }
}

/// <summary>A function call inside an expression, e.g. <c>ABS(x)</c>. Null arguments are elided positions.</summary>
public sealed record StCall(TextSpan Span, string Name, IReadOnlyList<StExpression?> Arguments) : StExpression(Span)
{
    public override string ToLogixText()
    {
        var sb = new StringBuilder(Name).Append('(');
        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0) sb.Append(',');
            if (Arguments[i] is { } a) sb.Append(a.ToLogixText());
        }
        return sb.Append(')').ToString();
    }
}

/// <summary>An expression the parser could not make sense of; <see cref="RawText"/> preserves it verbatim so nothing is lost even when it cannot be interpreted.</summary>
public sealed record StErrorExpression(TextSpan Span, string RawText) : StExpression(Span)
{
    public override string ToLogixText() => RawText;
}

public enum StKeywordKind
{
    /// <summary>An enumeration operand such as <c>WallClockTime</c>, <c>Trapezoidal</c> or <c>S-Curve</c>.</summary>
    Keyword,
    /// <summary>A routine name (JSR, SBR, RET, FOR).</summary>
    Routine,
    /// <summary>A jump label (JMP, LBL).</summary>
    Label,
}

/// <summary>
/// An instruction argument that is not a variable: an enumeration keyword, a routine name or a label.
/// Produced by the linker once every tag is known (see <c>L5xLinker.ReclassifyStCalls</c>); the parser itself
/// always emits <see cref="StVariable"/> for a bare identifier, since it cannot yet tell a keyword from a tag.
/// </summary>
public sealed record StKeyword(TextSpan Span, string Text, StKeywordKind Kind) : StExpression(Span)
{
    public override string ToLogixText() => Text;
}

/// <summary>A comment found in structured text, kept for transpilers that want to preserve documentation.</summary>
public sealed record StComment(string Text, int Line, int Column, bool IsBlock);
