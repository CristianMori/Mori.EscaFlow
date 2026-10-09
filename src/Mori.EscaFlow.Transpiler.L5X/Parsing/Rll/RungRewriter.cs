using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;

/// <summary>Rebuilds a rung AST with instructions transformed; returns the same instance when nothing changed.</summary>
public static class RungRewriter
{
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
