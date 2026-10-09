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
public sealed class CallEmitter
{
    private readonly LogixIrBuilder _owner;

    public CallEmitter(LogixIrBuilder owner) => _owner = owner;

    /// <summary>Appends the statements for <paramref name="name"/>(<paramref name="args"/>) to <paramref name="output"/>. <paramref name="en"/> is bound to the enable of statements that have one.</summary>
    public void Emit(LogixExpressionBuilder x, string name, bool isAddOn, List<IrExpression?> args, IrExpression? en, List<IrStatement> output)
    {
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
                return;
            case "RET":
                output.Add(new IrReturn { En = en });
                return;
            case "TND":
                output.Add(new IrReturn { En = en });
                return;
            case "COP" or "CPS":
                output.Add(new IrCopy { Source = Arg(0), Dest = Arg(1), Count = Arg(2), Mode = "bytes", Atomic = upper == "CPS", En = en });
                return;
            case "FLL":
                output.Add(new IrFill { Value = Arg(0), Dest = Arg(1), Count = Arg(2), En = en });
                return;
            case "FOR":
            {
                var routine = Arg(0) is IrKeyword k ? k.Text : "?";
                var body = new List<IrStatement>();
                EmitActionCall(x, routine, Array.Empty<IrExpression>(), Array.Empty<IrExpression>(), null, body);
                output.Add(new IrFor { Variable = Arg(1), From = Arg(2), To = Arg(3), By = Arg(4) is IrPlaceholder ? null : Arg(4), Body = body });
                return;
            }
            default:
                output.Add(new IrSysCall { Catalog = LogixVendor.Id, Service = upper, Args = args, En = en });
                return;
        }
    }

    private IrFbCall AddOnCall(LogixExpressionBuilder x, string name, List<IrExpression?> args, IrExpression? en)
    {
        var call = new IrFbCall { Pou = name, En = en, Profile = null };
        var aoi = _owner.Controller.FindAoi(name);
        if (args.Count > 0) call.Instance = args[0];
        if (aoi is null)
        {
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
