using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Logic.Fbd;
using L5x.Parsing;
using L5x.Parsing.St;

namespace Plc.Ir.Logix.Build;

/// <summary>
/// Builds the neutral network diagram of an FBD routine and lowers it to statements in dataflow order: every
/// wire into a block pin becomes an assignment to the block's backing structure member, then the block is
/// invoked; tagless functions produce body temporaries; output references are assigned last in their own
/// position. Feedback wires are ignored for ordering, so their destination reads the previous scan's value.
/// </summary>
public sealed class NetLowering
{
    private readonly LogixExpressionBuilder _x;
    private readonly CallEmitter _calls;
    private readonly List<IrTemporary> _temps;
    private readonly List<IrDiagnostic> _diagnostics;
    private readonly string _location;
    private readonly DiagnosticBag _scratch = new();
    private readonly Dictionary<string, (FbdSheet Sheet, FbdWire Wire)> _connectors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FbdElement, IrRef> _functionResults = new(ReferenceEqualityComparer.Instance);

    public NetLowering(LogixExpressionBuilder expressions, CallEmitter calls, List<IrTemporary> temporaries, List<IrDiagnostic> diagnostics, string location)
    {
        _x = expressions;
        _calls = calls;
        _temps = temporaries;
        _diagnostics = diagnostics;
        _location = location;
    }

    public IrNetDiagram Lower(FbdContent fbd, List<IrStatement> body)
    {
        var diagram = new IrNetDiagram();
        // Output connectors are routine-wide: an input connector reads the wire feeding the output connector of the same name.
        foreach (var sheet in fbd.Sheets)
            foreach (var wire in sheet.Wires)
                if (sheet.ById.TryGetValue(wire.ToId, out var to) && to is FbdOCon { Name: { } n })
                    _connectors.TryAdd(n, (sheet, wire));

        foreach (var sheet in fbd.Sheets)
        {
            diagram.Sheets.Add(BuildSheet(sheet));
            LowerSheet(sheet, body);
        }
        return diagram;
    }

    // ------------------------------------------------------------------ diagram

    private IrNetSheet BuildSheet(FbdSheet sheet)
    {
        var s = new IrNetSheet { Number = sheet.Number, Description = sheet.Description };
        foreach (var e in sheet.Elements)
        {
            var el = new IrNetElement { Id = e.Id, X = e.X, Y = e.Y };
            switch (e)
            {
                case FbdIRef r: el.Kind = "input"; el.Operand = r.ParsedOperand is null ? null : _x.Operand(r.ParsedOperand); break;
                case FbdORef r: el.Kind = "output"; el.Operand = r.ParsedOperand is null ? null : _x.Operand(r.ParsedOperand); break;
                case FbdICon c: el.Kind = "connectorIn"; el.Name = c.Name; break;
                case FbdOCon c: el.Kind = "connectorOut"; el.Name = c.Name; break;
                case FbdBlock b:
                    el.Kind = "block";
                    el.Type = b.Type;
                    el.Operand = b.Operand is null ? null : RefOf(b.Operand);
                    el.Pins = b.VisiblePins.Count > 0 ? b.VisiblePins : null;
                    if (b.Arrays is { Count: > 0 }) el.Parameters = b.Arrays.ToDictionary(p => p.Key, p => RefOf(p.Value));
                    break;
                case FbdFunction f: el.Kind = "function"; el.Type = f.Type; break;
                case FbdAddOnInstruction a:
                    el.Kind = "block";
                    el.Type = a.Name;
                    el.IsPou = true;
                    el.Operand = a.Operand is null ? null : RefOf(a.Operand);
                    el.Pins = a.VisiblePins.Count > 0 ? a.VisiblePins : null;
                    if (a.InOutParameters.Count > 0) el.Parameters = a.InOutParameters.ToDictionary(p => p.Key, p => RefOf(p.Value));
                    break;
                case FbdJsr j: el.Kind = "call"; el.Name = j.Routine; el.Pins = j.In.Concat(j.Ret).ToList(); break;
                case FbdSbr sb: el.Kind = "entry"; el.Name = sb.Routine; el.Pins = sb.In; break;
                case FbdRet rt: el.Kind = "exit"; el.Name = rt.Routine; el.Pins = rt.Ret; break;
                case FbdTextBox tb: el.Kind = "text"; el.Text = tb.Text; break;
            }
            s.Elements.Add(el);
        }
        foreach (var w in sheet.Wires) s.Wires.Add(new IrNetWire { From = w.FromId, FromPin = w.FromParam, To = w.ToId, ToPin = w.ToParam, Feedback = w.IsFeedback });
        return s;
    }

    private IrExpression RefOf(string operandText)
    {
        var path = StParser.TryParseOperandPath(operandText, LogicParseContext.Empty, _scratch);
        if (path is null) return new IrErrorExpression { Text = operandText };
        return _x.Ref(path);
    }

    private IrExpression MemberRef(string operandText, string member)
        => RefOf(operandText.Contains('.') || operandText.Contains('[') ? operandText + "." + member : operandText + "." + member);

    // ------------------------------------------------------------------ lowering

    private void LowerSheet(FbdSheet sheet, List<IrStatement> body)
    {
        var nodes = sheet.Elements.Where(IsExecutable).ToList();
        var order = TopologicalOrder(sheet, nodes);
        foreach (var node in order)
        {
            var before = body.Count;
            LowerNode(sheet, node, body);
            for (var i = before; i < body.Count; i++) body[i].Src ??= new IrSrc { Sheet = sheet.Number, Element = node.Id };
        }
    }

    private static bool IsExecutable(FbdElement e) => e is FbdBlock or FbdFunction or FbdAddOnInstruction or FbdJsr or FbdORef;

    private static List<FbdElement> TopologicalOrder(FbdSheet sheet, List<FbdElement> nodes)
    {
        var nodeSet = new HashSet<FbdElement>(nodes, ReferenceEqualityComparer.Instance);
        var incoming = new Dictionary<FbdElement, int>(ReferenceEqualityComparer.Instance); foreach (var n in nodes) incoming[n] = 0;
        var outgoing = new Dictionary<FbdElement, List<FbdElement>>(ReferenceEqualityComparer.Instance); foreach (var n in nodes) outgoing[n] = new List<FbdElement>();
        foreach (var w in sheet.Wires)
        {
            if (w.IsFeedback) continue;
            if (!sheet.ById.TryGetValue(w.FromId, out var from) || !sheet.ById.TryGetValue(w.ToId, out var to)) continue;
            if (!nodeSet.Contains(from) || !nodeSet.Contains(to) || ReferenceEquals(from, to)) continue;
            outgoing[from].Add(to);
            incoming[to]++;
        }
        var result = new List<FbdElement>(nodes.Count);
        var ready = new SortedSet<FbdElement>(Comparer<FbdElement>.Create(ByPosition));
        foreach (var n in nodes) if (incoming[n] == 0) ready.Add(n);
        var remaining = new HashSet<FbdElement>(nodes, ReferenceEqualityComparer.Instance);
        while (remaining.Count > 0)
        {
            if (ready.Count == 0)
            {
                // Unmarked feedback cycle: break at the earliest positioned remaining node.
                var pick = remaining.OrderBy(n => n, Comparer<FbdElement>.Create(ByPosition)).First();
                ready.Add(pick);
            }
            var node = ready.Min!;
            ready.Remove(node);
            if (!remaining.Remove(node)) continue;
            result.Add(node);
            foreach (var next in outgoing[node])
            {
                if (--incoming[next] == 0 && remaining.Contains(next)) ready.Add(next);
            }
        }
        return result;
    }

    private static int ByPosition(FbdElement a, FbdElement b)
    {
        var c = Nullable.Compare(a.Y, b.Y);
        if (c != 0) return c;
        c = Nullable.Compare(a.X, b.X);
        return c != 0 ? c : a.Id.CompareTo(b.Id);
    }

    private void LowerNode(FbdSheet sheet, FbdElement node, List<IrStatement> body)
    {
        var inputs = sheet.Wires.Where(w => w.ToId == node.Id).ToList();
        switch (node)
        {
            case FbdORef oref:
            {
                var target = oref.ParsedOperand is null ? null : _x.Operand(oref.ParsedOperand);
                var wire = inputs.FirstOrDefault();
                if (target is IrRef or IrBitAccess && wire is not null)
                    body.Add(new IrAssign { Target = target!, Value = LogixExpressionBuilder.Convert(Source(sheet, wire, 0), target!.Type) });
                break;
            }
            case FbdBlock block:
            {
                if (block.Operand is null) { Warn($"FBD block {block.Type} #{block.Id} has no backing tag"); break; }
                foreach (var w in inputs)
                {
                    if (w.ToParam is null) continue;
                    var target = MemberRef(block.Operand, w.ToParam);
                    if (target is IrRef or IrBitAccess) body.Add(new IrAssign { Target = target, Value = LogixExpressionBuilder.Convert(Source(sheet, w, 0), target.Type) });
                }
                var args = new List<IrExpression?> { RefOf(block.Operand) };
                if (block.Arrays is not null) foreach (var a in block.Arrays.Values) args.Add(RefOf(a));
                body.Add(new IrSysCall { Catalog = Logix.Id, Service = block.Type.ToUpperInvariant(), Args = args });
                break;
            }
            case FbdAddOnInstruction aoi:
            {
                if (aoi.Operand is null) { Warn($"FBD add-on {aoi.Name} #{aoi.Id} has no backing tag"); break; }
                foreach (var w in inputs)
                {
                    if (w.ToParam is null) continue;
                    var target = MemberRef(aoi.Operand, w.ToParam);
                    if (target is IrRef or IrBitAccess) body.Add(new IrAssign { Target = target, Value = LogixExpressionBuilder.Convert(Source(sheet, w, 0), target.Type) });
                }
                var call = new IrFbCall { Instance = RefOf(aoi.Operand), Pou = aoi.Name };
                foreach (var p in aoi.InOutParameters) call.Inputs[p.Key] = RefOf(p.Value);
                body.Add(call);
                break;
            }
            case FbdFunction fn:
            {
                var pins = inputs.Where(w => w.ToParam is not null).OrderBy(w => w.ToParam, StringComparer.OrdinalIgnoreCase).ToList();
                var args = pins.Select(w => Source(sheet, w, 0)).ToList();
                var iec = LogixInstructionMap.FunctionFor(fn.Type) ?? LogixInstructionMap.StFunctionFor(fn.Type) ?? fn.Type.ToUpperInvariant();
                IrExpression value;
                IrTypeRef? type;
                if (LogixInstructionMap.BinaryOperatorFor(fn.Type) is { } op && args.Count >= 2)
                {
                    value = args.Skip(1).Aggregate(args[0], (acc, a) => LogixExpressionBuilder.Binary(op, acc, a));
                    type = value.Type;
                }
                else if (LogixInstructionMap.ComparisonFor(fn.Type) is { } cmp && args.Count >= 2)
                {
                    value = LogixExpressionBuilder.Binary(cmp, args[0], args[1]);
                    type = value.Type;
                }
                else
                {
                    var def = Catalogs.StandardFunctions.Get(iec);
                    type = def?.ResultType(args.Select(a => a.Type?.LeafName).ToList()) is { } t ? IrTypeRef.Named(t) : args.FirstOrDefault()?.Type;
                    value = def is not null
                        ? new IrFunction { Name = iec, Args = args.Cast<IrExpression?>().ToList(), Type = type }
                        : new IrSysFn { Catalog = Logix.Id, Service = fn.Type.ToUpperInvariant(), Args = args.Cast<IrExpression?>().ToList(), Type = type };
                }
                var temp = new IrTemporary { Name = $"_f{_temps.Count(t => t.Name.StartsWith("_f", StringComparison.Ordinal)) + 1}", Type = type ?? IrTypeRef.Named("REAL") };
                _temps.Add(temp);
                var result = IrRef.Temp(temp.Name, temp.Type);
                _functionResults[fn] = result;
                body.Add(new IrAssign { Target = result, Value = value });
                break;
            }
            case FbdJsr jsr:
            {
                var routine = jsr.Routine ?? "?";
                var args = new List<IrExpression?> { new IrKeyword { Text = routine, Kind = "action" } };
                var ins = inputs.Where(w => w.ToParam is not null).OrderBy(w => PinIndex(jsr.In, w.ToParam!)).Select(w => Source(sheet, w, 0)).ToList();
                args.Add(IrLiteral.OfInt(ins.Count));
                args.AddRange(ins);
                _calls.Emit(_x, "JSR", false, args, null, body);
                break;
            }
        }
    }

    private static int PinIndex(List<string> pins, string pin)
    {
        var i = pins.FindIndex(p => p.Equals(pin, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>The value carried by a wire: the source element's operand, pin member, function result or connector feed.</summary>
    private IrExpression Source(FbdSheet sheet, FbdWire wire, int depth)
    {
        if (depth > 32) return new IrErrorExpression { Text = "connector loop" };
        if (!sheet.ById.TryGetValue(wire.FromId, out var from)) return new IrErrorExpression { Text = $"wire from missing element #{wire.FromId}" };
        switch (from)
        {
            case FbdIRef r:
                return r.ParsedOperand is null ? new IrErrorExpression { Text = r.Operand ?? "" } : _x.Operand(r.ParsedOperand);
            case FbdBlock b:
                return b.Operand is null || wire.FromParam is null ? new IrErrorExpression { Text = $"{b.Type}#{b.Id}.{wire.FromParam}" } : MemberRef(b.Operand, wire.FromParam);
            case FbdAddOnInstruction a:
                return a.Operand is null || wire.FromParam is null ? new IrErrorExpression { Text = $"{a.Name}#{a.Id}.{wire.FromParam}" } : MemberRef(a.Operand, wire.FromParam);
            case FbdFunction f:
                return _functionResults.TryGetValue(f, out var temp) ? temp : new IrErrorExpression { Text = $"{f.Type}#{f.Id} evaluated later (feedback)" };
            case FbdICon c:
            {
                if (c.Name is not null && _connectors.TryGetValue(c.Name, out var feed)) return Source(feed.Sheet, feed.Wire, depth + 1);
                return new IrErrorExpression { Text = $"connector {c.Name} has no source" };
            }
            case FbdJsr j:
            {
                // Return pins carry the callee's RET operands.
                var signature = _x.Container is null || j.Routine is null ? null : _x.Owner.Subroutines.Get(_x.Container, j.Routine);
                var idx = wire.FromParam is null ? -1 : PinIndex(j.Ret, wire.FromParam);
                if (signature is not null && idx >= 0 && idx < signature.Outputs.Count) return _x.Operand(signature.Outputs[idx]);
                return new IrErrorExpression { Text = $"JSR {j.Routine} return pin {wire.FromParam}" };
            }
            default:
                return new IrErrorExpression { Text = $"unsupported wire source #{wire.FromId}" };
        }
    }

    private void Warn(string message) => _diagnostics.Add(new IrDiagnostic { Severity = "warning", Code = "IRB010", Message = message, Location = _location });
}
