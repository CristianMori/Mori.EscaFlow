using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.St;

/// <summary>
/// Rebuilds a structured text AST with call statements transformed. Returns the same instance when nothing changed,
/// so untouched routines keep their original nodes. The linker uses it for the same purpose as
/// <see cref="Rll.RungRewriter"/>: reclassifying call arguments once every tag name is known. Only statement
/// bodies are walked (IF, CASE, FOR, WHILE, REPEAT); expressions never contain call statements.
/// </summary>
public static class StRewriter
{
    /// <summary>Applies <paramref name="transform"/> to every call statement in the block, recursively; a null result keeps the original.</summary>
    public static StBlock RewriteCalls(StBlock block, Func<StCallStatement, StCallStatement?> transform)
    {
        var changed = false;
        StStatement[]? copy = null;
        for (var i = 0; i < block.Statements.Count; i++)
        {
            var s = block.Statements[i];
            var r = Rewrite(s, transform);
            if (!ReferenceEquals(r, s))
            {
                copy ??= block.Statements.ToArray();
                copy[i] = r;
                changed = true;
            }
        }
        return changed ? block with { Statements = copy! } : block;
    }

    private static StStatement Rewrite(StStatement s, Func<StCallStatement, StCallStatement?> transform)
    {
        switch (s)
        {
            case StCallStatement call:
                return transform(call) ?? call;
            case StIf i:
            {
                var changed = false;
                var branches = new StIfBranch[i.Branches.Count];
                for (var b = 0; b < branches.Length; b++)
                {
                    var body = RewriteCalls(i.Branches[b].Body, transform);
                    changed |= !ReferenceEquals(body, i.Branches[b].Body);
                    branches[b] = ReferenceEquals(body, i.Branches[b].Body) ? i.Branches[b] : i.Branches[b] with { Body = body };
                }
                var els = i.Else is null ? null : RewriteCalls(i.Else, transform);
                changed |= !ReferenceEquals(els, i.Else);
                return changed ? i with { Branches = branches, Else = els } : i;
            }
            case StCase c:
            {
                var changed = false;
                var arms = new StCaseArm[c.Arms.Count];
                for (var a = 0; a < arms.Length; a++)
                {
                    var body = RewriteCalls(c.Arms[a].Body, transform);
                    changed |= !ReferenceEquals(body, c.Arms[a].Body);
                    arms[a] = ReferenceEquals(body, c.Arms[a].Body) ? c.Arms[a] : c.Arms[a] with { Body = body };
                }
                var els = c.Else is null ? null : RewriteCalls(c.Else, transform);
                changed |= !ReferenceEquals(els, c.Else);
                return changed ? c with { Arms = arms, Else = els } : c;
            }
            case StFor f:
            {
                var body = RewriteCalls(f.Body, transform);
                return ReferenceEquals(body, f.Body) ? f : f with { Body = body };
            }
            case StWhile w:
            {
                var body = RewriteCalls(w.Body, transform);
                return ReferenceEquals(body, w.Body) ? w : w with { Body = body };
            }
            case StRepeat r:
            {
                var body = RewriteCalls(r.Body, transform);
                return ReferenceEquals(body, r.Body) ? r : r with { Body = body };
            }
            default:
                return s;
        }
    }
}
