namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>Position of a node inside a logic text. Line is the L5X Line Number (0-based) when known, Column is 1-based.</summary>
public readonly record struct TextSpan(int Start, int Length, int Line, int Column)
{
    public int End => Start + Length;
    public static readonly TextSpan Empty = new(0, 0, 0, 0);
    public static TextSpan Between(TextSpan first, TextSpan last) => new(first.Start, Math.Max(0, last.End - first.Start), first.Line, first.Column);
}
