using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

/// <summary>
/// The parsed form of one ladder rung's neutral text. The text is a series of instructions and parallel branches;
/// the rung condition flows left to right through the series and a branch ORs the conditions at the end of its
/// legs, which is what the instruction semantics of the controller are defined against.
/// </summary>
public sealed record RungAst(IReadOnlyList<RungElement> Elements)
{
    public static readonly RungAst Empty = new(Array.Empty<RungElement>());

    /// <summary>Enumerates every instruction in the rung, depth first, including those inside branches.</summary>
    public IEnumerable<InstructionElement> AllInstructions()
    {
        // An explicit stack instead of recursion: deeply nested branches occur in real projects.
        var stack = new Stack<IEnumerator<RungElement>>();
        stack.Push(Elements.GetEnumerator());
        while (stack.Count > 0)
        {
            var e = stack.Peek();
            if (!e.MoveNext())
            {
                stack.Pop();
                continue;
            }
            switch (e.Current)
            {
                case InstructionElement instr:
                    yield return instr;
                    break;
                case BranchElement branch:
                    // Legs are pushed in reverse so they are visited in drawing order.
                    for (var i = branch.Legs.Count - 1; i >= 0; i--) stack.Push(branch.Legs[i].GetEnumerator());
                    break;
            }
        }
    }
}

/// <summary>An element on a rung: either an instruction or a parallel branch. Offset is the character index in the rung text.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(InstructionElement), "instruction")]
[JsonDerivedType(typeof(BranchElement), "branch")]
public abstract record RungElement(int Offset);

/// <summary><c>MNEMONIC(arg, arg, ...)</c>. IsAddOn is true when the mnemonic names an add-on instruction definition.</summary>
public sealed record InstructionElement(int Offset, string Mnemonic, IReadOnlyList<Operand> Arguments, bool IsAddOn) : RungElement(Offset);

/// <summary><c>[leg , leg , ...]</c>. A leg may be empty (a straight wire).</summary>
public sealed record BranchElement(int Offset, IReadOnlyList<IReadOnlyList<RungElement>> Legs) : RungElement(Offset);

/// <summary>
/// One argument of an instruction. Text is the exact source text (trimmed). The concrete kind is decided by the
/// operand's position in the instruction signature and by its spelling; bare identifiers that name no tag are
/// reclassified as keywords by the linker once every tag is known.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TagRefOperand), "tag")]
[JsonDerivedType(typeof(LiteralOperand), "literal")]
[JsonDerivedType(typeof(ExpressionOperand), "expression")]
[JsonDerivedType(typeof(RoutineRefOperand), "routine")]
[JsonDerivedType(typeof(LabelOperand), "label")]
[JsonDerivedType(typeof(KeywordOperand), "keyword")]
[JsonDerivedType(typeof(PlaceholderOperand), "placeholder")]
[JsonDerivedType(typeof(EmptyOperand), "empty")]
[JsonDerivedType(typeof(RawOperand), "raw")]
public abstract record Operand(string Text)
{
    public override string ToString() => Text;
}

/// <summary>A tag reference, possibly with members, indices and a bit (<c>Line[1].Motor.Fault</c>, <c>Word.3</c>).</summary>
public sealed record TagRefOperand(string Text, OperandPath Path) : Operand(Text);

/// <summary>An immediate value (integer in any radix, real, string).</summary>
public sealed record LiteralOperand(string Text, LiteralValue Value) : Operand(Text);

/// <summary>An arithmetic or comparison expression, as used by CPT, CMP, FSC and FAL.</summary>
public sealed record ExpressionOperand(string Text, StExpression Expression) : Operand(Text);

/// <summary>A routine name (JSR, SBR, RET, FOR).</summary>
public sealed record RoutineRefOperand(string Text, string RoutineName) : Operand(Text);

/// <summary>A jump label (JMP, LBL).</summary>
public sealed record LabelOperand(string Text, string Label) : Operand(Text);

/// <summary>An enumeration-like keyword operand (GSV class names, SWPB order, FSC/FAL mode, ...).</summary>
public sealed record KeywordOperand(string Text) : Operand(Text);

/// <summary>The <c>?</c> (or <c>??</c>) placeholder used for operands that are not supplied (timer presets and accumulators shown on the instruction face).</summary>
public sealed record PlaceholderOperand(string Text) : Operand(Text)
{
    public static readonly PlaceholderOperand Single = new("?");
    public static readonly PlaceholderOperand Double = new("??");
}

/// <summary>An elided operand between two commas (<c>GSV(WallClockTime,,DateTime,Dest)</c>).</summary>
public sealed record EmptyOperand() : Operand("")
{
    public static readonly EmptyOperand Instance = new();
}

/// <summary>Text that could not be classified; a diagnostic explains why.</summary>
public sealed record RawOperand(string Text, string Error) : Operand(Text);
