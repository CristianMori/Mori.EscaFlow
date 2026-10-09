using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Catalogs;
using Mori.EscaFlow.Transpiler.Logix;

namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Emits C# statements for IR statements into a <see cref="CodeWriter"/>.</summary>
public sealed class CSharpStatements
{
    private readonly IrDocument _doc;
    private readonly NameMapper _names;
    private readonly CSharpTypes _types;
    private readonly CSharpExpressions _x;
    private readonly GenerationReport _report;
    private readonly CSharpGeneratorOptions _options;
    private readonly Dictionary<string, List<string>> _unsupported = new(StringComparer.OrdinalIgnoreCase);

    public CSharpStatements(IrDocument doc, NameMapper names, CSharpTypes types, CSharpExpressions expressions, GenerationReport report, CSharpGeneratorOptions options, Func<IrTypeRef, int> elementSize)
    {
        _doc = doc;
        _names = names;
        _types = types;
        _x = expressions;
        _report = report;
        _options = options;
        _layoutSize = elementSize;
    }

    public IReadOnlyDictionary<string, List<string>> Unsupported => _unsupported;
    private readonly HashSet<string> _opaqueReported = new(StringComparer.Ordinal);

    private sealed class OpaqueFinder : IrWalker
    {
        private bool _found;

        public static bool Contains(IrStatement s)
        {
            var f = new OpaqueFinder();
            f.VisitStatement(s, new IrWalkContext());
            return f._found;
        }

        public override void VisitStatement(IrStatement s, IrWalkContext ctx)
        {
            // Compound statements: only their header expressions count; nested statements are checked when emitted.
            switch (s)
            {
                case IrIf i: foreach (var b in i.Branches) VisitExpression(b.Condition, ctx); return;
                case IrCase c: VisitExpression(c.Selector, ctx); return;
                case IrFor f: VisitExpression(f.Variable, ctx); VisitExpression(f.From, ctx); VisitExpression(f.To, ctx); if (f.By is not null) VisitExpression(f.By, ctx); return;
                case IrWhile w: VisitExpression(w.Condition, ctx); return;
                case IrRepeat r: VisitExpression(r.Until, ctx); return;
            }
            base.VisitStatement(s, ctx);
        }

        public override void VisitExpression(IrExpression e, IrWalkContext ctx)
        {
            if (e is IrRef { Opaque: true }) _found = true;
            base.VisitExpression(e, ctx);
        }
    }

    public void Body(CodeWriter w, IrBody body, EmitContext ctx, string location)
    {
        ctx.Temporaries.Clear();
        ctx.Labels.Clear();
        ctx.LoopDepth = 0;
        CollectLabels(body.Statements, ctx.Labels);
        foreach (var t in body.Temporaries ?? new())
        {
            ctx.Temporaries.Add(t.Name);
            var type = CSharpTypes.IsAtomic(t.Type) ? CSharpTypes.ValueType(t.Type.Name!) : "object";
            w.Line($"{type} {t.Name} = default;");
        }
        if (body.Language == "encrypted")
        {
            w.Line("// source-protected routine: body not available in the export");
            return;
        }
        if (body.Language == "sfc")
        {
            w.Line("// SFC routine: executed by the runtime chart engine (not yet generated)");
            _report.Warning("GEN010", "SFC routine bodies are not generated yet", location);
            return;
        }
        int? lastRung = null;
        var ld = body.Diagram as IrLdDiagram;
        foreach (var s in body.Statements)
        {
            if (_options.SourceComments && ld is not null && s.Src?.Rung is { } rung && rung != lastRung)
            {
                lastRung = rung;
                var r = ld.Rungs.FirstOrDefault(x => x.Number == rung);
                if (r is not null)
                {
                    w.Line();
                    if (r.Comment is not null) foreach (var line in r.Comment.Split('\n')) w.Line("// " + line.TrimEnd('\r'));
                    if (r.Text is not null) w.Line("// rung " + rung + ": " + r.Text.Replace("\n", " "));
                }
            }
            Statement(w, s, ctx, location);
        }
    }

    private static void CollectLabels(IEnumerable<IrStatement> statements, HashSet<string> labels)
    {
        foreach (var s in statements)
        {
            switch (s)
            {
                case IrLabel l: labels.Add(l.Name); break;
                case IrIf i: foreach (var b in i.Branches) CollectLabels(b.Body, labels); if (i.Else is not null) CollectLabels(i.Else, labels); break;
            }
        }
    }

    public void Statements(CodeWriter w, IEnumerable<IrStatement> statements, EmitContext ctx, string location)
    {
        foreach (var s in statements) Statement(w, s, ctx, location);
    }

    public void Statement(CodeWriter w, IrStatement s, EmitContext ctx, string location)
    {
        if (OpaqueFinder.Contains(s))
        {
            // The statement reaches into a type whose layout the source project does not define (an I/O module
            // that is not in the project): its members cannot be generated.
            w.Line("// skipped: references a member of an opaque type (layout unknown in the source project)");
            if (_opaqueReported.Add(location)) _report.Warning("GEN020", "statements referencing opaque types were skipped", location);
            return;
        }
        switch (s)
        {
            case IrAssign a:
                Assign(w, a, ctx);
                break;
            case IrFbCall c:
                FbCall(w, c, ctx, location);
                break;
            case IrFnCall f:
            {
                var args = string.Join(", ", f.Args.Select(a => a is null ? "null" : _x.Arg(a, ctx)));
                w.Line(f.Pou is not null ? $"{_names.Map(CSharpTypes.TypeScope, f.Pou)}.Invoke({args});" : $"PlcFn.{f.Name}({args});");
                break;
            }
            case IrActionCall ac:
                ActionCall(w, ac, ctx, location);
                break;
            case IrSysCall sc:
                SysCall(w, sc, ctx, location);
                break;
            case IrCopy cp:
                Gate(w, cp.En, ctx, () => w.Line($"PlcMem.Copy({_x.Arg(cp.Source, ctx)}, {_x.Arg(cp.Dest, ctx)}, {_x.Int(cp.Count, ctx)});"));
                break;
            case IrFill fl:
                Gate(w, fl.En, ctx, () => w.Line($"PlcMem.Fill({_x.Arg(fl.Value, ctx)}, {_x.Arg(fl.Dest, ctx)}, {_x.Int(fl.Count, ctx)});"));
                break;
            case IrIf i:
            {
                for (var k = 0; k < i.Branches.Count; k++)
                {
                    var b = i.Branches[k];
                    var cond = _x.Bool(b.Condition, ctx);
                    if (b.Condition is IrLiteral { IsTrue: true } && i.Branches.Count == 1 && i.Else is null)
                    {
                        Statements(w, b.Body, ctx, location);
                        return;
                    }
                    using (w.Block((k == 0 ? "if (" : "else if (") + cond + ")"))
                        Statements(w, b.Body, ctx, location);
                }
                if (i.Else is not null)
                    using (w.Block("else"))
                        Statements(w, i.Else, ctx, location);
                break;
            }
            case IrCase c:
            {
                using (w.Block($"switch ({_x.Value(c.Selector, ctx)})"))
                {
                    foreach (var arm in c.Arms)
                    {
                        foreach (var label in arm.Labels)
                        {
                            if (label.To is not null) w.Line($"case >= {_x.Value(label.Value, ctx)} and <= {_x.Value(label.To, ctx)}:");
                            else w.Line($"case {_x.Value(label.Value, ctx)}:");
                        }
                        using (w.IndentScope())
                        {
                            ctx.LoopDepth++; // EXIT inside a case arm is not valid IEC; break here ends the arm
                            Statements(w, arm.Body, ctx, location);
                            ctx.LoopDepth--;
                            w.Line("break;");
                        }
                    }
                    if (c.Else is not null)
                    {
                        w.Line("default:");
                        using (w.IndentScope())
                        {
                            Statements(w, c.Else, ctx, location);
                            w.Line("break;");
                        }
                    }
                }
                break;
            }
            case IrFor f:
            {
                var v = _x.Value(f.Variable, ctx);
                var vt = CSharpTypes.ValueType(f.Variable.Type?.LeafName ?? "DINT");
                var to = _x.Value(f.To, ctx);
                var by = f.By is null ? "1" : _x.Value(f.By, ctx);
                var id = "_for" + (ctx.LoopDepth + 1);
                using (w.BareBlock())
                {
                    w.Line($"var {id}To = ({vt})({to});");
                    w.Line($"var {id}By = ({vt})({by});");
                    using (w.Block($"for ({v} = ({vt})({_x.Value(f.From, ctx)}); {id}By >= 0 ? {v} <= {id}To : {v} >= {id}To; {v} = ({vt})({v} + {id}By))"))
                    {
                        ctx.LoopDepth++;
                        Statements(w, f.Body, ctx, location);
                        ctx.LoopDepth--;
                    }
                }
                break;
            }
            case IrWhile wh:
                using (w.Block($"while ({_x.Bool(wh.Condition, ctx)})"))
                {
                    ctx.LoopDepth++;
                    Statements(w, wh.Body, ctx, location);
                    ctx.LoopDepth--;
                }
                break;
            case IrRepeat rp:
            {
                w.Line("do");
                w.Line("{");
                using (w.IndentScope())
                {
                    ctx.LoopDepth++;
                    Statements(w, rp.Body, ctx, location);
                    ctx.LoopDepth--;
                }
                w.Line($"}} while (!({_x.Bool(rp.Until, ctx)}));");
                break;
            }
            case IrExit:
                w.Line(ctx.LoopDepth > 0 ? "break;" : "return; // EXIT outside a loop");
                break;
            case IrContinue:
                w.Line("continue;");
                break;
            case IrReturn r:
                Gate(w, r.En, ctx, () => w.Line("return;"));
                break;
            case IrJump j:
                Gate(w, j.En, ctx, () => w.Line(ctx.Labels.Contains(j.Label) ? $"goto {LabelName(j.Label)};" : $"/* jump to unknown label {j.Label} */"));
                break;
            case IrLabel l:
                w.Line($"{LabelName(l.Name)}: ;");
                break;
            case IrErrorStatement err:
                w.Line($"// error: {err.Text.Replace("\n", " ")}");
                _report.Warning("GEN011", "statement the front-end could not interpret: " + err.Text, location);
                break;
        }
    }

    private string LabelName(string label) => "L_" + _names.Map("labels", label);

    private void Gate(CodeWriter w, IrExpression? en, EmitContext ctx, Action body)
    {
        if (en is null || en is IrLiteral { IsTrue: true })
        {
            body();
            return;
        }
        using (w.Block($"if ({_x.Bool(en, ctx)})")) body();
    }

    // ------------------------------------------------------------------ assignments

    private void Assign(CodeWriter w, IrAssign a, EmitContext ctx)
    {
        var value = _x.Value(a.Value, ctx);
        switch (a.Target)
        {
            case IrRef { Root.Kind: "temp" } t:
                w.Line($"{t.Root.Name} = {value};");
                return;
            case IrBitAccess ba:
                w.Line($"{_x.Object((IrRef)ba.Of, ctx)}[{_x.Int(ba.Bit, ctx)}] = {_x.Bool(a.Value, ctx)};");
                return;
            case IrRef r when CSharpExpressions.IsAtomicLeaf(r):
            {
                var target = _x.Object(r, ctx);
                var type = r.Type!.Name!;
                if (type.Equals("BOOL", StringComparison.OrdinalIgnoreCase))
                    w.Line($"{target}.Value = {_x.Bool(a.Value, ctx)};");
                else
                {
                    var vt = CSharpTypes.ValueType(type);
                    var sameType = a.Value.Type is { } st && st.LeafName is { } sn && CSharpTypes.IsAtomic(st) && CSharpTypes.ValueType(sn) == vt;
                    var needsCast = !(sameType && a.Value is (IrRef or IrLiteral or IrConv));
                    w.Line(needsCast ? $"{target}.Value = ({vt})({value});" : $"{target}.Value = {value};");
                }
                return;
            }
            case IrRef r when IsStringType(r.Type):
                w.Line($"{_x.Object(r, ctx)}.Value = {StringValue(a.Value, ctx)};");
                return;
            case IrRef r:
                // Structure or array copy.
                w.Line($"PlcMem.CopyAll({_x.Arg(a.Value, ctx)}, {_x.Object(r, ctx)});");
                return;
            default:
                w.Line($"// unsupported assignment target");
                return;
        }
    }

    // ------------------------------------------------------------------ calls

    private void FbCall(CodeWriter w, IrFbCall c, EmitContext ctx, string location)
    {
        if (c.Fb is not null)
        {
            _report.Count(_report.StandardBlockInventory, c.Fb);
            var instance = c.Instance is null ? "null!" : _x.Arg(c.Instance, ctx);
            var enablePin = c.Fb switch { "CTU" => "CU", "CTD" => "CD", _ => "IN" };
            var enable = c.Inputs.TryGetValue(enablePin, out var en) ? _x.Bool(en, ctx) : c.En is not null ? _x.Bool(c.En, ctx) : "true";
            if (c.Profile is not null && c.Profile != LogixVendor.Id)
            {
                _report.Error("GEN001", $"standard block profile '{c.Profile}' is not supported by the C# runtime", location);
                w.Line($"// unsupported profile {c.Profile}");
                return;
            }
            w.Line(c.Mode == "prescan" ? $"Logix.Prescan{c.Fb}({instance});" : $"Logix.{c.Fb}({instance}, {enable});");
            return;
        }
        if (c.Pou is null) return;
        var pou = _doc.FindPou(c.Pou);
        var inst = c.Instance is null ? null : _x.Arg(c.Instance, ctx);
        if (pou is null || inst is null)
        {
            w.Line($"// call to unknown function block {c.Pou}");
            _report.Warning("GEN012", $"unknown function block {c.Pou}", location);
            return;
        }
        var scope = CSharpExpressions.MemberScope;
        if (c.Mode is "prescan" or "postscan")
        {
            // InOut parameters must alias their arguments during prescan / postscan as well.
            foreach (var (pin, expr) in c.Inputs)
            {
                var v = pou.FindVar(pin);
                if (v is { Section: "inOut" }) BindInOut(w, inst, _names.Map(scope, v.Name), v, expr, ctx);
            }
            w.Line(c.Mode == "prescan" ? $"{inst}.Prescan();" : $"{inst}.Postscan();");
            return;
        }
        // InOut parameters are rebound; inputs are copied in; then the block executes; outputs are copied out.
        foreach (var (pin, expr) in c.Inputs)
        {
            var v = pou.FindVar(pin);
            if (v is null) continue;
            var member = _names.Map(scope, v.Name);
            if (v.Section == "inOut") BindInOut(w, inst, member, v, expr, ctx);
            else AssignTo($"{inst}.{member}", v.Type, expr, w, ctx);
        }
        var enableIn = c.En is null ? "true" : _x.Bool(c.En, ctx);
        w.Line($"{inst}.Execute({enableIn});");
        foreach (var (pin, expr) in c.Outputs)
        {
            var v = pou.FindVar(pin);
            if (v is null) continue;
            var member = _names.Map(scope, v.Name);
            if (v.Section == "inOut") continue;
            if (expr is IrRef target && CSharpExpressions.IsAtomicLeaf(target))
            {
                var tt = target.Type!.Name!;
                var cast = tt.Equals("BOOL", StringComparison.OrdinalIgnoreCase) || v.Type.Is(tt) ? "" : $"({CSharpTypes.ValueType(tt)})";
                w.Line($"{_x.Object(target, ctx)}.Value = {cast}{inst}.{member}.Value;");
            }
            else if (expr is IrBitAccess ba)
                w.Line($"{_x.Object((IrRef)ba.Of, ctx)}[{_x.Int(ba.Bit, ctx)}] = {inst}.{member}.Value;");
            else if (expr is IrRef target2)
                w.Line($"PlcMem.CopyAll({inst}.{member}, {_x.Object(target2, ctx)});");
        }
    }

    /// <summary>
    /// InOut parameters alias the argument's memory through a view of the declared type (Logix overlays the declared
    /// layout on whatever structure is passed, e.g. device images of different sizes). MODULE parameters receive the
    /// module's name; when a tag shares that name, the module is meant.
    /// </summary>
    private void BindInOut(CodeWriter w, string inst, string member, IrVariable v, IrExpression expr, EmitContext ctx)
    {
        if (v.Type.IsNamed && CSharpTypes.IsModuleType(v.Type.Name!) || expr is IrKeyword)
        {
            var name = expr switch
            {
                IrKeyword k => k.Text,
                IrRef { Root.Kind: "var", Root.Id: { } id } when _doc.FindVariable(id) is { } mv => mv.Logix()?.Path ?? mv.Name,
                _ => null,
            };
            w.Line($"{inst}.{member} = {(name is null ? _x.Value(expr, ctx) : CSharpTypes.Quote(name))};");
            return;
        }
        if (v.Type.IsArray)
        {
            // Array parameters take their size from the argument (Logix declares them with dimension 1).
            var element = v.Type.Element ?? IrTypeRef.Named("DINT");
            if (element.Is("BOOL"))
                w.Line($"{inst}.{member} = PlcView.OfBoolArray({inst}.{member}, {_x.Arg(expr, ctx)});");
            else
                w.Line($"{inst}.{member} = PlcView.OfArray({inst}.{member}, {_x.Arg(expr, ctx)}, {_layoutSize(element)}, (t, o) => {_types.Construct(element, "t", "o")});");
            return;
        }
        w.Line($"{inst}.{member} = PlcView.Of({inst}.{member}, {_x.Arg(expr, ctx)}, (t, o) => {_types.Construct(v.Type, "t", "o")});");
    }

    private readonly Func<IrTypeRef, int> _layoutSize;

    private void AssignTo(string target, IrTypeRef type, IrExpression value, CodeWriter w, EmitContext ctx)
    {
        if (CSharpTypes.IsAtomic(type) && !type.IsArray)
        {
            if (type.Is("BOOL")) w.Line($"{target}.Value = {_x.Bool(value, ctx)};");
            else w.Line($"{target}.Value = ({CSharpTypes.ValueType(type.Name!)})({_x.Value(value, ctx)});");
        }
        else if (IsStringType(type))
            w.Line($"{target}.Value = {StringValue(value, ctx)};");
        else
            w.Line($"PlcMem.CopyAll({_x.Arg(value, ctx)}, {target});");
    }

    private bool IsStringType(IrTypeRef? type) => type is { Kind: "string" } || (type is { IsNamed: true, Name: { } n } && (n.Equals("STRING", StringComparison.OrdinalIgnoreCase) || _doc.FindType(n) is { Kind: "string" }));

    /// <summary>A C# string expression: a literal, the .Value of a string variable, or a string-valued function result.</summary>
    private string StringValue(IrExpression value, EmitContext ctx) => value switch
    {
        IrLiteral => _x.Value(value, ctx),
        IrRef r when IsStringType(r.Type) => _x.Object(r, ctx) + ".Value",
        IrRef r => _x.Value(r, ctx) + ".ToString()",
        _ => _x.Value(value, ctx),
    };

    private void ActionCall(CodeWriter w, IrActionCall ac, EmitContext ctx, string location)
    {
        var pou = ac.Pou is null ? ctx.Pou : _doc.FindPou(ac.Pou);
        var action = pou?.FindAction(ac.Action);
        if (pou is null || action is null)
        {
            w.Line($"// call to unknown action {ac.Action}");
            _report.Warning("GEN013", $"unknown action {ac.Action}", location);
            return;
        }
        var method = _x.ActionMethod(pou, action.Name);
        var target = ac.Pou is null || ctx.Pou is not null && ctx.Pou.Name.Equals(pou.Name, StringComparison.OrdinalIgnoreCase) ? method : $"{_x.ProgramInstance(pou.Name)}.{method}";
        if (ac.Inputs.Count > 0 || ac.Outputs.Count > 0)
            _report.Warning("GEN014", $"action call {ac.Action} passes {ac.Inputs.Count} inputs / {ac.Outputs.Count} outputs that could not be bound to the callee's parameters", location);
        Gate(w, ac.En, ctx, () => w.Line($"{target}();"));
    }

    private void SysCall(CodeWriter w, IrSysCall sc, EmitContext ctx, string location)
    {
        _report.Count(_report.ServiceInventory, sc.Service);
        var args = sc.Args.Select(a => a is null ? "null" : _x.Arg(a, ctx)).ToList();
        var supported = _options.ImplementedServices.Contains(sc.Service);
        if (!supported)
        {
            if (!_unsupported.TryGetValue(sc.Service, out var list)) _unsupported[sc.Service] = list = new List<string>();
            list.Add(location);
            var call = $"{ctx.Services}.Unsupported({CSharpTypes.Quote(sc.Service)}{(args.Count > 0 ? ", " + string.Join(", ", args) : "")});";
            Gate(w, sc.En, ctx, () => w.Line(call));
            return;
        }
        if (sc.Mode == "prescan")
        {
            w.Line($"{ctx.Services}.Prescan({CSharpTypes.Quote(sc.Service)}{(args.Count > 0 ? ", " + string.Join(", ", args) : "")});");
            return;
        }
        var enableArg = sc.En is not null && LogixServiceCatalog.Instance.RunsEveryScan(sc.Service) ? _x.Bool(sc.En, ctx) + ", " : "";
        var line = $"{ctx.Services}.{CSharpExpressions.SafeService(sc.Service)}({enableArg}{string.Join(", ", args)});";
        if (enableArg.Length > 0) w.Line(line);
        else Gate(w, sc.En, ctx, () => w.Line(line));
    }
}
