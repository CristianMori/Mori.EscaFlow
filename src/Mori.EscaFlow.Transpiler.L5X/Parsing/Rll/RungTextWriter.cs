using System.Text;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;

/// <summary>
/// Renders a <see cref="RungAst"/> back to neutral text in the exporter's canonical layout. Used to round-trip
/// rungs (the conformance tests compare a re-rendered rung with the original after <see cref="Normalize"/>) and
/// to print rewritten rungs. Operands are written from their original text, so no information is lost.
/// </summary>
public static class RungTextWriter
{
    /// <summary>
    /// Instructions on the main line are concatenated without separators; inside a branch leg every element is
    /// followed by a single space, so a leg reads <c>[XIC(a) XIC(b) ,XIC(c) ]</c>. The rung ends with <c>;</c>.
    /// </summary>
    public static string Write(RungAst ast)
    {
        var sb = new StringBuilder();
        WriteElements(sb, ast.Elements, inLeg: false);
        sb.Append(';');
        return sb.ToString();
    }

    /// <summary>Renders a single element (no terminator).</summary>
    public static string Write(RungElement element)
    {
        var sb = new StringBuilder();
        WriteElement(sb, element);
        return sb.ToString();
    }

    private static void WriteElements(StringBuilder sb, IReadOnlyList<RungElement> elements, bool inLeg)
    {
        foreach (var element in elements)
        {
            WriteElement(sb, element);
            if (inLeg) sb.Append(' ');
        }
    }

    private static void WriteElement(StringBuilder sb, RungElement element)
    {
        switch (element)
        {
            case InstructionElement instr:
                sb.Append(instr.Mnemonic).Append('(');
                for (var i = 0; i < instr.Arguments.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(instr.Arguments[i].Text);
                }
                sb.Append(')');
                break;
            case BranchElement branch:
                sb.Append('[');
                for (var i = 0; i < branch.Legs.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteElements(sb, branch.Legs[i], inLeg: true);
                }
                sb.Append(']');
                break;
        }
    }

    /// <summary>Removes all whitespace outside single-quoted string literals so two renderings can be compared.</summary>
    public static string Normalize(string rungText)
    {
        var sb = new StringBuilder(rungText.Length);
        var inString = false;
        for (var i = 0; i < rungText.Length; i++)
        {
            var c = rungText[i];
            if (inString)
            {
                // Inside a string every character is significant, including spaces and escaped quotes ($').
                sb.Append(c);
                if (c == '$' && i + 1 < rungText.Length)
                {
                    sb.Append(rungText[++i]);
                    continue;
                }
                if (c == '\'') inString = false;
                continue;
            }
            if (char.IsWhiteSpace(c)) continue;
            if (c == '\'') inString = true;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
