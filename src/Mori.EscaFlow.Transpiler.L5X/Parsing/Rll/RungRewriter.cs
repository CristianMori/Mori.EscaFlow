using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;

/// <summary>
/// Rebuilds a rung AST with instructions transformed; returns the same instance when nothing changed.
/// The linker uses it to replace operands after reclassification (bare identifiers that turned out to be
/// keywords). Copy-on-write keeps the common case (no change) allocation free and lets callers detect a
/// change by reference comparison.
/// </summary>
public static class RungRewriter
{
    /// <summary>Applies <paramref name="transform"/> to every instruction, inside branches too; a null result keeps the original instruction.</summary>
    public static RungAst Rewrite(RungAst ast, Func<InstructionElement, InstructionElement?> transform)
    {
        var elements = RewriteList(ast.Elements, transform, out var changed);
        return changed ? new RungAst(elements) : ast;
    }

    private static IReadOnlyList<RungElement> RewriteList(IReadOnlyList<RungElement> elements, Func<InstructionElement, InstructionElement?> transform, out bool changed)
    {
        changed = false;
        RungElement[]? copy = null;
        for (var i = 0; i < elements.Count; i++)
        {
            RungElement? replacement = null;
            switch (elements[i])
            {
                case InstructionElement instr:
                    replacement = transform(instr);
                    break;
                case BranchElement branch:
                {
                    // Recurse into each leg; copy the leg array only when some leg actually changed.
                    var legsChanged = false;
                    IReadOnlyList<RungElement>[]? legs = null;
                    for (var l = 0; l < branch.Legs.Count; l++)
                    {
                        var newLeg = RewriteList(branch.Legs[l], transform, out var legChanged);
                        if (legChanged)
                        {
                            legs ??= branch.Legs.ToArray();
                            legs[l] = newLeg;
                            legsChanged = true;
                        }
                    }
                    if (legsChanged) replacement = branch with { Legs = legs! };
                    break;
                }
            }
            if (replacement is not null && !ReferenceEquals(replacement, elements[i]))
            {
                copy ??= elements.ToArray();
                copy[i] = replacement;
                changed = true;
            }
        }
        return copy ?? elements;
    }
}
