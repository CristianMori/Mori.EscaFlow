using System.Text;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;

namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>
/// A parsed reference to a tag or a part of a tag, e.g. <c>Local:1:I.Data[3].5</c> or <c>\Prog.Tag.Member</c>.
/// Shared by the ladder operand parser and the structured text parser.
/// </summary>
public sealed record OperandPath(
    string Root,
    IReadOnlyList<PathSegment> Segments,
    string? ProgramName = null)
{
    public static OperandPath Simple(string root) => new(root, Array.Empty<PathSegment>());

    /// <summary>True when the path has no member, index or bit segments.</summary>
    public bool IsBare => Segments.Count == 0;

    /// <summary>The trailing bit segment when the path ends with <c>.n</c>.</summary>
    public BitSegment? TrailingBit => Segments.Count > 0 ? Segments[^1] as BitSegment : null;

    public string ToLogixText()
    {
        var sb = new StringBuilder();
        if (ProgramName is not null) sb.Append('\\').Append(ProgramName).Append('.');
        sb.Append(Root);
        foreach (var seg in Segments) seg.AppendTo(sb);
        return sb.ToString();
    }

    public override string ToString() => ToLogixText();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MemberSegment), "member")]
[JsonDerivedType(typeof(IndexSegment), "index")]
[JsonDerivedType(typeof(BitSegment), "bit")]
[JsonDerivedType(typeof(DynamicBitSegment), "bitexpr")]
public abstract record PathSegment
{
    public abstract void AppendTo(StringBuilder sb);
    public override string ToString()
    {
        var sb = new StringBuilder();
        AppendTo(sb);
        return sb.ToString();
    }
}

/// <summary><c>.Name</c></summary>
public sealed record MemberSegment(string Name) : PathSegment
{
    public override void AppendTo(StringBuilder sb) => sb.Append('.').Append(Name);
}

/// <summary><c>[i]</c> or <c>[i,j,k]</c>. Each index is an expression: a literal, a tag (indirect addressing) or arithmetic.</summary>
public sealed record IndexSegment(IReadOnlyList<StExpression> Indices) : PathSegment
{
    public override void AppendTo(StringBuilder sb)
    {
        sb.Append('[');
        for (var i = 0; i < Indices.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Indices[i].ToLogixText());
        }
        sb.Append(']');
    }

    /// <summary>True when every index is an integer literal.</summary>
    public bool IsConstant => Indices.All(i => i is StLiteral { Value: IntegerLiteral });

    public int[]? ConstantIndices()
    {
        if (!IsConstant) return null;
        var result = new int[Indices.Count];
        for (var i = 0; i < result.Length; i++) result[i] = (int)((IntegerLiteral)((StLiteral)Indices[i]).Value).Value;
        return result;
    }
}

/// <summary><c>.n</c> — a bit inside an integer.</summary>
public sealed record BitSegment(int Bit) : PathSegment
{
    public override void AppendTo(StringBuilder sb) => sb.Append('.').Append(Bit);
}

/// <summary><c>.[expr]</c> — a bit inside an integer selected at run time (indirect bit addressing).</summary>
public sealed record DynamicBitSegment(StExpression Bit) : PathSegment
{
    public override void AppendTo(StringBuilder sb) => sb.Append(".[").Append(Bit.ToLogixText()).Append(']');
}
