using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>
/// Turns an instruction invoked with positional operands (a ladder block or an ST call statement) into neutral
/// statements: add-on calls become <c>fbCall</c> on the POU, JSR becomes parameter assignments around an
/// <c>actionCall</c>, COP/CPS/FLL become <c>copy</c>/<c>fill</c>, FOR becomes a <c>for</c>, everything else a
/// <c>sysCall</c> into the Logix catalog. SBR contributes nothing (its parameters are bound at the JSR) and RET returns.
/// </summary>
/// <remarks>
/// Ladder and structured text invoke the same instructions with the same positional operand lists, so one emitter
/// serves both: <see cref="LadderLowering"/> hands it blocks with the rung condition as the enable, the ST path hands it
/// call statements with no enable. The neutral statements it produces are what every converter consumes, so this is the
/// place where Logix instruction vocabulary stops and the vendor-neutral vocabulary starts.
/// </remarks>
public sealed class CallEmitter
{
    /// <summary>The builder that owns the controller model, the subroutine index and the variable ids.</summary>
    private readonly LogixIrBuilder _owner;

    public CallEmitter(LogixIrBuilder owner) => _owner = owner;

    /// <summary>Appends the statements for <paramref name="name"/>(<paramref name="args"/>) to <paramref name="output"/>. <paramref name="en"/> is bound to the enable of statements that have one.</summary>
    /// <param name="x">The expression builder of the routine container being emitted (needed to resolve callee parameters in the same scope).</param>
    /// <param name="name">The instruction mnemonic or add-on name as written.</param>
    /// <param name="isAddOn">True when <paramref name="name"/> is an add-on instruction definition rather than a built-in instruction.</param>
    /// <param name="args">Operands in source order; null entries are elided positions.</param>
    /// <param name="en">The rung condition for ladder blocks whose statement reads an enable every scan; null for gated blocks (the caller wraps them in an IF) and for ST.</param>
    /// <param name="output">Receives zero or more statements; a bound JSR produces several.</param>
    public void Emit(LogixExpressionBuilder x, string name, bool isAddOn, List<IrExpression?> args, IrExpression? en, List<IrStatement> output)
    {
        // An elided or missing operand reads as an empty placeholder so positional lookups never throw.
        IrExpression Arg(int i) => i < args.Count && args[i] is { } a ? a : new IrPlaceholder { Empty = true };
        var upper = name.ToUpperInvariant();
        // Elided operands (",,") are null positions whether they came from ladder or ST.
        for (var i = 0; i < args.Count; i++) if (args[i] is IrPlaceholder { Empty: true }) args[i] = null;

        if (isAddOn)
        {
            output.Add(AddOnCall(x, name, args, en));
            return;
        }

        switch (upper)
        {
            case "JSR":
                EmitJsr(x, args, en, output);
                return;
            case "SBR":
                // SBR only names the input parameters of the routine it opens; the values arrive through the
                // assignments emitted at the JSR site, so the instruction itself has no run-time effect.
                return;
            case "RET":
                // RET names the values returned to the caller; those tags are read by the JSR site after the call,
                // so here it is only the end of the routine.
                output.Add(new IrReturn { En = en });
                return;
            case "TND":
                // Temporary end: leaves the routine when the rung is true (the enable is turned into IF by the caller).
                output.Add(new IrReturn { En = en });
                return;
            case "COP" or "CPS":
                // COP copies raw bytes (the destination type is overlaid on the source); CPS is the same copy done
                // without interruption by other tasks, which the runtime expresses as an atomic copy.
                output.Add(new IrCopy { Source = Arg(0), Dest = Arg(1), Count = Arg(2), Mode = "bytes", Atomic = upper == "CPS", En = en });
                return;
            case "FLL":
                output.Add(new IrFill { Value = Arg(0), Dest = Arg(1), Count = Arg(2), En = en });
                return;
            case "FOR":
            {
                // FOR(routine, index, initial, terminal, step) runs the named routine once per iteration; the loop
                // variable is a real tag the callee may read, which is why the neutral FOR carries a reference.
                var routine = Arg(0) is IrKeyword k ? k.Text : "?";
                var body = new List<IrStatement>();
                EmitActionCall(x, routine, Array.Empty<IrExpression>(), Array.Empty<IrExpression>(), null, body);
                output.Add(new IrFor { Variable = Arg(1), From = Arg(2), To = Arg(3), By = Arg(4) is IrPlaceholder ? null : Arg(4), Body = body });
                return;
            }
            default:
                // Everything else is a vendor runtime service: the catalog (logix-services.json) documents its
                // parameters and whether it runs every scan; the converter decides what its runtime can do with it.
                output.Add(new IrSysCall { Catalog = LogixVendor.Id, Service = upper, Args = args, En = en });
                return;
        }
    }

    /// <summary>
    /// An add-on instruction call. Logix passes the backing tag first, then the <em>required</em> parameters in
    /// declaration order (optional parameters are set through the backing tag before the call), so the positional
    /// arguments are matched against the required parameters only: inputs and InOut parameters land in
    /// <c>inputs</c>, outputs in <c>outputs</c>. The rung condition becomes the neutral <c>en</c>, which the converter
    /// maps to EnableIn (and to the EnableInFalse scan when false).
    /// </summary>
    private IrFbCall AddOnCall(LogixExpressionBuilder x, string name, List<IrExpression?> args, IrExpression? en)
    {
        var call = new IrFbCall { Pou = name, En = en, Profile = null };
        var aoi = _owner.Controller.FindAoi(name);
        if (args.Count > 0) call.Instance = args[0];
        if (aoi is null)
        {
            // The definition is missing from the export (should not happen after linking); keep the arguments by position.
            for (var i = 1; i < args.Count; i++) if (args[i] is { } a) call.Inputs[$"arg{i}"] = a;
            return call;
        }
        var required = aoi.Parameters.Where(p => p.Required).ToList();
        for (var i = 0; i < required.Count && i + 1 < args.Count; i++)
        {
            var p = required[i];
            var a = args[i + 1];
            if (a is null || a is IrPlaceholder) continue;
            if (p.ParameterUsage == AoiParameterUsage.Output) call.Outputs[p.Name] = a;
            else call.Inputs[p.Name] = a;
        }
        return call;
    }

    /// <summary>JSR(routine, inputCount, inputs..., returns...): bind to the callee's SBR / RET operands with assignments around the action call.</summary>
    /// <remarks>
    /// The Logix neutral text lists the number of input parameters explicitly; the remaining operands are the return
    /// targets. Splitting them here lets <see cref="EmitActionCall"/> pair each one with the callee's declaration.
    /// </remarks>
    private void EmitJsr(LogixExpressionBuilder x, List<IrExpression?> args, IrExpression? en, List<IrStatement> output)
    {
        var routine = args.Count > 0 && args[0] is IrKeyword k ? k.Text : "?";
        var inputs = new List<IrExpression>();
        var outputs = new List<IrExpression>();
        if (args.Count > 1)
        {
            var count = args[1] is IrLiteral { Kind: "int", Int: { } n } ? (int)n : 0;
            for (var i = 2; i < args.Count; i++)
            {
                if (args[i] is not { } a || a is IrPlaceholder { Empty: true }) continue;
                if (i - 2 < count) inputs.Add(a);
                else outputs.Add(a);
            }
        }
        EmitActionCall(x, routine, inputs, outputs, en, output);
    }

    /// <summary>
    /// Emits a call of a routine of the same container. When the callee declares parameters (see
    /// <see cref="SubroutineIndex"/>) the call is emitted in its <em>bound</em> form: the inputs are assigned to the
    /// callee's SBR tags before the call and the callee's RET tags are copied to the caller's targets after it. Logix
    /// passes parameters by copying values exactly this way, so the neutral <c>actionCall</c> needs no parameter
    /// passing at run time and converters can emit a plain method call. Only arguments the callee does not declare are
    /// left on the call itself, for diagnostics.
    /// </summary>
    private void EmitActionCall(LogixExpressionBuilder x, string routine, IReadOnlyList<IrExpression> inputs, IReadOnlyList<IrExpression> outputs, IrExpression? en, List<IrStatement> output)
    {
        var signature = x.Container is null ? null : _owner.Subroutines.Get(x.Container, routine);
        var call = new IrActionCall { Action = routine, En = en };
        if (signature is null || (inputs.Count == 0 && outputs.Count == 0))
        {
            call.Inputs.AddRange(inputs);
            call.Outputs.AddRange(outputs);
            output.Add(call);
            return;
        }
        // Bound form: callee SBR parameters receive the inputs before the call, RET values are copied out after it.
        for (var i = 0; i < inputs.Count && i < signature.Inputs.Count; i++)
        {
            var target = x.Operand(signature.Inputs[i]);
            if (target is IrRef or IrBitAccess)
                output.Add(new IrAssign { Target = target, Value = LogixExpressionBuilder.Convert(inputs[i], target.Type) });
        }
        for (var i = signature.Inputs.Count; i < inputs.Count; i++) call.Inputs.Add(inputs[i]);
        output.Add(call);
        for (var i = 0; i < outputs.Count && i < signature.Outputs.Count; i++)
        {
            var target = outputs[i];
            if (target is IrRef or IrBitAccess)
                output.Add(new IrAssign { Target = target, Value = LogixExpressionBuilder.Convert(x.Operand(signature.Outputs[i]), target.Type) });
        }
        for (var i = signature.Outputs.Count; i < outputs.Count; i++) call.Outputs.Add(outputs[i]);
    }
}
