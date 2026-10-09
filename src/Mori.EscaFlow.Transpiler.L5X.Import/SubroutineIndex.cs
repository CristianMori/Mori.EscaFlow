using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;

namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>The parameters a routine declares with SBR (inputs) and returns with RET (outputs), so JSR sites can be bound to plain assignments.</summary>
/// <param name="Inputs">The SBR operands in declaration order: the tags that receive the JSR input values.</param>
/// <param name="Outputs">The RET operands in declaration order: the tags whose values the JSR copies to its return targets.</param>
public sealed record SubroutineSignature(IReadOnlyList<Operand> Inputs, IReadOnlyList<Operand> Outputs);

/// <summary>Indexes the SBR / RET operand lists of every routine in the controller.</summary>
/// <remarks>
/// Logix has no call frames: JSR copies its input operands into the tags the callee names in SBR, runs the routine,
/// then copies the tags named in RET back into its return operands. Knowing every callee's SBR and RET lists up
/// front lets <see cref="CallEmitter"/> turn each JSR into ordinary assignments around an action call, so converters
/// never need parameter passing. A routine with several RET instructions is assumed to return the same tags from
/// each (the first list wins), which is how Logix programs are written in practice.
/// </remarks>
public sealed class SubroutineIndex
{
    /// <summary>Signatures keyed by (container, routine name); the container distinguishes same-named routines of different programs.</summary>
    private readonly Dictionary<(IRoutineContainer, string), SubroutineSignature> _signatures = new(new KeyComparer());

    public SubroutineIndex(Controller controller)
    {
        foreach (var p in controller.Programs) foreach (var r in p.Routines) Index(p, r);
        foreach (var a in controller.AddOnInstructions) foreach (var r in a.Routines) Index(a, r);
    }

    /// <summary>The signature of a routine, or null when it declares neither SBR nor RET parameters.</summary>
    public SubroutineSignature? Get(IRoutineContainer container, string routine) => _signatures.GetValueOrDefault((container, routine));

    /// <summary>Scans one routine for its first SBR and first RET-with-operands, in ladder or structured text.</summary>
    private void Index(IRoutineContainer container, Routine r)
    {
        IReadOnlyList<Operand>? sbr = null;
        IReadOnlyList<Operand>? ret = null;
        switch (r.Content)
        {
            case RllContent rll:
                foreach (var rung in rll.Rungs)
                {
                    if (rung.Ast is null) continue;
                    foreach (var i in rung.Ast.AllInstructions())
                    {
                        // SBR lists the input tags directly (there is no routine-name operand); a bare RET() ends the
                        // routine without returning values and must not shadow a later RET that does.
                        if (sbr is null && i.Mnemonic.Equals("SBR", StringComparison.OrdinalIgnoreCase)) sbr = Skip(i.Arguments, 0);
                        else if (ret is null && i.Mnemonic.Equals("RET", StringComparison.OrdinalIgnoreCase) && i.Arguments.Count > 0) ret = i.Arguments;
                    }
                }
                break;
            case StContent st when st.Ast is not null:
                Visit(st.Ast, ref sbr, ref ret);
                break;
        }
        if (sbr is not null || ret is not null)
            _signatures[(container, r.Name)] = new SubroutineSignature(sbr ?? Array.Empty<Operand>(), ret ?? Array.Empty<Operand>());
    }

    /// <summary>The operands after the first <paramref name="n"/> (an empty list when there are none).</summary>
    private static IReadOnlyList<Operand> Skip(IReadOnlyList<Operand> args, int n) => args.Count <= n ? Array.Empty<Operand>() : args.Skip(n).ToList();

    /// <summary>Walks a structured-text block (and its nested blocks) for SBR and RET call statements.</summary>
    private static void Visit(StBlock block, ref IReadOnlyList<Operand>? sbr, ref IReadOnlyList<Operand>? ret)
    {
        foreach (var s in block.Statements)
        {
            switch (s)
            {
                case StCallStatement c when c.Name.Equals("SBR", StringComparison.OrdinalIgnoreCase) && sbr is null:
                    sbr = ToOperands(c.Arguments, 0);
                    break;
                case StCallStatement c when c.Name.Equals("RET", StringComparison.OrdinalIgnoreCase) && ret is null && c.Arguments.Count > 0:
                    ret = ToOperands(c.Arguments, 0);
                    break;
                case StIf i:
                    foreach (var b in i.Branches) Visit(b.Body, ref sbr, ref ret);
                    if (i.Else is not null) Visit(i.Else, ref sbr, ref ret);
                    break;
                case StCase c:
                    foreach (var arm in c.Arms) Visit(arm.Body, ref sbr, ref ret);
                    if (c.Else is not null) Visit(c.Else, ref sbr, ref ret);
                    break;
                case StFor f: Visit(f.Body, ref sbr, ref ret); break;
                case StWhile w: Visit(w.Body, ref sbr, ref ret); break;
                case StRepeat r: Visit(r.Body, ref sbr, ref ret); break;
            }
        }
    }

    /// <summary>ST call arguments as ladder-style operands (only tag references and literals are meaningful for SBR / RET).</summary>
    /// <remarks>Sharing the ladder operand model lets the emitter resolve both spellings of a signature with the same code.</remarks>
    private static IReadOnlyList<Operand> ToOperands(IReadOnlyList<StExpression?> args, int skip)
    {
        var list = new List<Operand>();
        for (var i = skip; i < args.Count; i++)
        {
            switch (args[i])
            {
                case StVariable v: list.Add(new TagRefOperand(v.Path.ToLogixText(), v.Path)); break;
                case StLiteral l: list.Add(new LiteralOperand(l.Value.ToLogixText(), l.Value)); break;
                case StKeyword k when i == 0: list.Add(new RoutineRefOperand(k.Text, k.Text)); break;
                case { } e: list.Add(new ExpressionOperand(e.ToLogixText(), e)); break;
                default: list.Add(EmptyOperand.Instance); break;
            }
        }
        return list;
    }

    /// <summary>Compares the container by reference (programs and add-ons are singletons) and the routine name as Logix does, case-insensitively.</summary>
    private sealed class KeyComparer : IEqualityComparer<(IRoutineContainer, string)>
    {
        public bool Equals((IRoutineContainer, string) x, (IRoutineContainer, string) y) => ReferenceEquals(x.Item1, y.Item1) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((IRoutineContainer, string) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1), obj.Item2.ToUpperInvariant());
    }
}
