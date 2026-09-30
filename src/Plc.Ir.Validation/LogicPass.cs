using Plc.Ir.Catalogs;

namespace Plc.Ir.Validation;

/// <summary>Walks every body: references, calls, labels (referential) and expression / statement typing.</summary>
internal sealed class LogicPass : IrWalker
{
    private readonly IrDocument _doc;
    private readonly ValidationResult _result;
    private readonly TypeResolver _types;
    private readonly ValidationOptions _options;
    private readonly Dictionary<string, IServiceCatalog> _catalogs;
    private readonly HashSet<string> _unknownCatalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IrBody, HashSet<string>> _labels = new(ReferenceEqualityComparer.Instance);

    public LogicPass(IrDocument doc, ValidationResult result, TypeResolver types, ValidationOptions options)
    {
        _doc = doc;
        _result = result;
        _types = types;
        _options = options;
        _catalogs = options.ServiceCatalogs.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
    }

    public void Run() => Walk(_doc);

    private string Loc(IrWalkContext ctx, IrNode node)
    {
        var l = ctx.Location;
        if (node.Src is { } s)
        {
            if (s.Rung is { } r) l += $" rung {r}";
            if (s.Line is { } line) l += $" line {line}";
            if (s.Element is { } e) l += $" element {e}";
        }
        return l;
    }

    // ------------------------------------------------------------------ bodies

    protected override void VisitBody(IrBody body, IrWalkContext ctx)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectLabels(body.Statements, labels);
        _labels[body] = labels;
    }

    private static void CollectLabels(IEnumerable<IrStatement> statements, HashSet<string> labels)
    {
        foreach (var s in statements)
        {
            switch (s)
            {
                case IrLabel l: labels.Add(l.Name); break;
                case IrIf i:
                    foreach (var b in i.Branches) CollectLabels(b.Body, labels);
                    if (i.Else is not null) CollectLabels(i.Else, labels);
                    break;
                case IrCase c:
                    foreach (var arm in c.Arms) CollectLabels(arm.Body, labels);
                    if (c.Else is not null) CollectLabels(c.Else, labels);
                    break;
                case IrFor f: CollectLabels(f.Body, labels); break;
                case IrWhile w: CollectLabels(w.Body, labels); break;
                case IrRepeat r: CollectLabels(r.Body, labels); break;
            }
        }
    }

    // ------------------------------------------------------------------ statements

    public override void VisitStatement(IrStatement s, IrWalkContext ctx)
    {
        switch (s)
        {
            case IrAssign a: CheckAssign(a, ctx); break;
            case IrFbCall c: CheckFbCall(c, ctx); break;
            case IrActionCall ac: CheckActionCall(ac, ctx); break;
            case IrSysCall sc: CheckService(sc.Catalog, sc.Service, sc.Args.Count, false, Loc(ctx, sc)); break;
            case IrFnCall f: CheckFunctionName(f.Name, f.Pou, Loc(ctx, f)); break;
            case IrJump j:
                if (ctx.Body is null || !_labels.TryGetValue(ctx.Body, out var labels) || !labels.Contains(j.Label))
                    _result.Error(ValidationCodes.UndefinedLabel, $"jump to undefined label '{j.Label}'", Loc(ctx, j));
                if (j.En is not null) RequireBool(j.En, "jump enable", ctx, j);
                break;
            case IrIf i:
                foreach (var b in i.Branches) RequireBool(b.Condition, "if condition", ctx, i);
                break;
            case IrWhile w: RequireBool(w.Condition, "while condition", ctx, w); break;
            case IrRepeat r: RequireBool(r.Until, "repeat until", ctx, r); break;
            case IrFor f:
                if (_options.CheckTyping && f.Variable.Type is { } vt && !TypeResolver.IsIntegerLike(_types.Classify(vt)) && _types.Classify(vt) != TypeResolver.Cat.Unknown)
                    _result.Error(ValidationCodes.ForVariableNotInteger, $"for variable has type {vt}", Loc(ctx, f));
                break;
            case IrCase c:
                if (_options.CheckTyping && c.Selector.Type is { } st)
                {
                    var cat = _types.Classify(st);
                    if (cat != TypeResolver.Cat.Unknown && !TypeResolver.IsIntegerLike(cat))
                        _result.Error(ValidationCodes.CaseSelectorType, $"case selector has type {st}", Loc(ctx, c));
                }
                break;
            case IrReturn r:
                if (r.En is not null) RequireBool(r.En, "return enable", ctx, r);
                break;
            case IrCopy cp:
                if (cp.En is not null) RequireBool(cp.En, "copy enable", ctx, cp);
                break;
            case IrFill fl:
                if (fl.En is not null) RequireBool(fl.En, "fill enable", ctx, fl);
                break;
        }
        base.VisitStatement(s, ctx);
    }

    private void CheckAssign(IrAssign a, IrWalkContext ctx)
    {
        if (a.Target is not (IrRef or IrBitAccess))
            _result.Error(ValidationCodes.AssignTargetKind, "assignment target must be a ref or a bit expression", Loc(ctx, a));
        if (!_options.CheckTyping) return;
        if (a.Target.Type is { } tt && a.Value.Type is { } vt && !_types.Compatible(tt, vt))
            _result.Error(ValidationCodes.AssignTypeMismatch, $"cannot assign {vt} to {tt}", Loc(ctx, a));
    }

    private void CheckFbCall(IrFbCall c, IrWalkContext ctx)
    {
        var loc = Loc(ctx, c);
        var hasFb = !string.IsNullOrEmpty(c.Fb);
        var hasPou = !string.IsNullOrEmpty(c.Pou);
        if (hasFb == hasPou)
        {
            _result.Error(ValidationCodes.FbCallTarget, "fbCall must name exactly one of fb or pou", loc);
            return;
        }
        if (c.En is not null) RequireBool(c.En, "fbCall enable", ctx, c);
        if (hasFb)
        {
            var fb = StandardFunctionBlocks.Get(c.Fb!);
            if (fb is null)
            {
                _result.Error(ValidationCodes.UnknownStandardFb, $"unknown standard function block '{c.Fb}'", loc);
                return;
            }
            var profileName = c.Profile ?? "iec";
            var profile = StandardFunctionBlocks.GetProfile(profileName);
            if (profile is null)
            {
                _result.Error(ValidationCodes.UnknownProfile, $"unknown profile '{profileName}'", loc);
                return;
            }
            if (profile.InstanceTypes.TryGetValue(fb.Name, out var instanceType) && c.Instance?.Type?.LeafName is { } actual
                && !string.Equals(actual, instanceType, StringComparison.OrdinalIgnoreCase))
                _result.Error(ValidationCodes.InstanceTypeMismatch, $"{fb.Name} under profile {profileName} needs an instance of {instanceType}, got {actual}", loc);
            var extra = profile.ExtraPins(fb.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (pin, expr) in c.Inputs.Concat(c.Outputs))
            {
                if (!fb.HasPin(pin) && !extra.Contains(pin))
                {
                    _result.Error(ValidationCodes.UnknownPin, $"{fb.Name} has no pin '{pin}' under profile {profileName}", loc);
                    continue;
                }
                if (!_options.CheckTyping) continue;
                var pinType = profile.PinType(fb, pin);
                if (pinType is not null && expr.Type is { } et && !_types.Compatible(IrTypeRef.Named(pinType), et))
                    _result.Error(ValidationCodes.PinTypeMismatch, $"pin {pin} of {fb.Name} expects {pinType}, got {et}", loc);
            }
        }
        else
        {
            var pou = _doc.FindPou(c.Pou!);
            if (pou is null) return; // reported by the structural pass
            foreach (var (pin, expr) in c.Inputs)
            {
                var v = pou.FindVar(pin);
                if (v is null || v.Section is not ("input" or "inOut"))
                {
                    _result.Error(ValidationCodes.PouPinMismatch, $"'{pin}' is not an input or inOut of pou '{pou.Name}'", loc);
                    continue;
                }
                if (_options.CheckTyping && expr.Type is { } et && !_types.Compatible(v.Type, et))
                {
                    // Device image types are interchangeable on Logix InOut pins (generic ETHERNET-MODULE images of different sizes); warn only.
                    if (IsIoType(v.Type) && IsIoType(et))
                        _result.Warning(ValidationCodes.PinTypeMismatch, $"pin {pin} of {pou.Name} expects device image {v.Type}, got {et}", loc);
                    else
                        _result.Error(ValidationCodes.PinTypeMismatch, $"pin {pin} of {pou.Name} expects {v.Type}, got {et}", loc);
                }
            }
            foreach (var (pin, expr) in c.Outputs)
            {
                var v = pou.FindVar(pin);
                if (v is null || v.Section is not ("output" or "inOut"))
                {
                    _result.Error(ValidationCodes.PouPinMismatch, $"'{pin}' is not an output or inOut of pou '{pou.Name}'", loc);
                    continue;
                }
                if (_options.CheckTyping && expr.Type is { } et && !_types.Compatible(et, v.Type))
                {
                    if (IsIoType(v.Type) && IsIoType(et))
                        _result.Warning(ValidationCodes.PinTypeMismatch, $"pin {pin} of {pou.Name} yields device image {v.Type}, stored into {et}", loc);
                    else
                        _result.Error(ValidationCodes.PinTypeMismatch, $"pin {pin} of {pou.Name} yields {v.Type}, stored into {et}", loc);
                }
            }
        }
    }

    private bool IsIoType(IrTypeRef? type) => type?.LeafName is { } name && _doc.FindType(name) is { Class: "io" };

    private void CheckActionCall(IrActionCall ac, IrWalkContext ctx)
    {
        var pou = ac.Pou is null ? ctx.Pou : _doc.FindPou(ac.Pou);
        if (pou is null) return;
        if (pou.FindAction(ac.Action) is null)
            _result.Error(ValidationCodes.UnresolvedAction, $"action '{ac.Action}' is not declared in pou '{pou.Name}'", Loc(ctx, ac));
        if (ac.En is not null) RequireBool(ac.En, "actionCall enable", ctx, ac);
    }

    private void CheckService(string catalogId, string service, int argCount, bool isFunction, string loc)
    {
        if (!_catalogs.TryGetValue(catalogId, out var catalog))
        {
            if (_options.TreatUnknownServiceCatalogAsWarning && _unknownCatalogs.Add(catalogId))
                _result.Warning(ValidationCodes.UnknownServiceCatalog, $"service catalog '{catalogId}' is not registered; its services are not checked", loc);
            return;
        }
        var def = catalog.Get(service);
        if (def is null)
        {
            _result.Error(ValidationCodes.UnknownService, $"catalog '{catalogId}' has no service '{service}'", loc);
            return;
        }
        var min = def.Params.Count(p => !p.Optional);
        var max = def.Variadic ? int.MaxValue : def.Params.Count;
        if (argCount < min || argCount > max)
            _result.Error(ValidationCodes.ServiceArgCount, $"{service} takes {(def.Variadic ? $"at least {min}" : min == max ? $"{min}" : $"{min} to {max}")} argument(s), got {argCount}", loc);
    }

    private void CheckFunctionName(string? name, string? pou, string loc)
    {
        if (pou is not null) return; // pou existence checked structurally
        if (name is not null && !StandardFunctions.IsStandard(name))
            _result.Warning(ValidationCodes.UnresolvedPou, $"'{name}' is not a standard function and names no pou", loc);
    }

    private void RequireBool(IrExpression e, string what, IrWalkContext ctx, IrNode owner)
    {
        if (!_options.CheckTyping) return;
        if (e.Type is { } t && _types.Classify(t) is var cat && cat != TypeResolver.Cat.Unknown && cat != TypeResolver.Cat.Bool)
            _result.Error(ValidationCodes.ConditionNotBool, $"{what} has type {t}", Loc(ctx, owner));
    }

    // ------------------------------------------------------------------ expressions

    public override void VisitExpression(IrExpression e, IrWalkContext ctx)
    {
        switch (e)
        {
            case IrRef r: CheckRef(r, ctx); break;
            case IrSysFn sf: CheckService(sf.Catalog, sf.Service, sf.Args.Count, true, Loc(ctx, sf)); break;
            case IrFunction f: CheckFunctionName(f.Name, f.Pou, Loc(ctx, f)); break;
        }
        if (_options.CheckTyping) CheckTyping(e, ctx);
        base.VisitExpression(e, ctx);
    }

    private void CheckTyping(IrExpression e, IrWalkContext ctx)
    {
        switch (e)
        {
            case IrBinary b:
            {
                var lc = _types.Classify(b.Left.Type);
                var rc = _types.Classify(b.Right.Type);
                if (b.IsComparison || b.IsLogical && lc == TypeResolver.Cat.Bool && rc == TypeResolver.Cat.Bool)
                {
                    if (b.Type is { } t && _types.Classify(t) != TypeResolver.Cat.Bool && _types.Classify(t) != TypeResolver.Cat.Unknown)
                        _result.Error(ValidationCodes.BoolResultExpected, $"{b.Operator} expression typed {t}, expected BOOL", Loc(ctx, b));
                }
                if (b.IsLogical && lc != TypeResolver.Cat.Unknown && rc != TypeResolver.Cat.Unknown)
                {
                    var ok = lc == TypeResolver.Cat.Bool && rc == TypeResolver.Cat.Bool || TypeResolver.IsIntegerLike(lc) && TypeResolver.IsIntegerLike(rc);
                    if (!ok) _result.Error(ValidationCodes.LogicalOperandTypes, $"{b.Operator} between {b.Left.Type} and {b.Right.Type}", Loc(ctx, b));
                }
                if (b.Operator is "add" or "sub" or "mul" or "div" or "mod" or "pow")
                {
                    foreach (var (side, cat) in new[] { (b.Left, lc), (b.Right, rc) })
                        if (cat != TypeResolver.Cat.Unknown && !TypeResolver.IsNumericCat(cat) && cat != TypeResolver.Cat.Time)
                            _result.Error(ValidationCodes.ArithmeticOperandTypes, $"{b.Operator} operand has type {side.Type}", Loc(ctx, b));
                }
                break;
            }
            case IrUnary u when u.Operator == "neg":
            {
                var c = _types.Classify(u.Operand.Type);
                if (c != TypeResolver.Cat.Unknown && !TypeResolver.IsNumericCat(c) && c != TypeResolver.Cat.Time)
                    _result.Error(ValidationCodes.ArithmeticOperandTypes, $"neg operand has type {u.Operand.Type}", Loc(ctx, u));
                break;
            }
            case IrBitAccess ba:
            {
                var oc = _types.Classify(ba.Of.Type);
                if (oc != TypeResolver.Cat.Unknown && oc is not (TypeResolver.Cat.Integer or TypeResolver.Cat.BitString))
                    _result.Error(ValidationCodes.BitOfNotInteger, $"bit access on {ba.Of.Type}", Loc(ctx, ba));
                var bc = _types.Classify(ba.Bit.Type);
                if (bc != TypeResolver.Cat.Unknown && !TypeResolver.IsIntegerLike(bc))
                    _result.Error(ValidationCodes.BitIndexNotInteger, $"bit index has type {ba.Bit.Type}", Loc(ctx, ba));
                break;
            }
            case IrEdge ed:
            {
                var cc = _types.Classify(ed.Clk.Type);
                if (cc != TypeResolver.Cat.Unknown && cc != TypeResolver.Cat.Bool)
                    _result.Error(ValidationCodes.EdgePinNotBool, $"edge clk has type {ed.Clk.Type}", Loc(ctx, ed));
                var sc = _types.Classify(ed.Storage.Type);
                if (sc != TypeResolver.Cat.Unknown && sc != TypeResolver.Cat.Bool)
                    _result.Error(ValidationCodes.EdgePinNotBool, $"edge storage has type {ed.Storage.Type}", Loc(ctx, ed));
                break;
            }
            case IrConv cv:
            {
                if (!AtomicTypes.IsAtomic(cv.To))
                    _result.Error(ValidationCodes.ConvTypes, $"conv target {cv.To} is not atomic", Loc(ctx, cv));
                var oc = _types.Classify(cv.Operand.Type);
                if (oc != TypeResolver.Cat.Unknown && !TypeResolver.IsNumericCat(oc) && oc is not (TypeResolver.Cat.Time or TypeResolver.Cat.Char))
                    _result.Error(ValidationCodes.ConvTypes, $"conv operand has type {cv.Operand.Type}", Loc(ctx, cv));
                break;
            }
        }
    }

    private void CheckRef(IrRef r, IrWalkContext ctx)
    {
        var loc = Loc(ctx, r);
        IrTypeRef? current = null;
        switch (r.Root.Kind)
        {
            case "var":
            {
                if (r.Root.Id is null) { _result.Error(ValidationCodes.UnresolvedVariable, "ref root has no id", loc); return; }
                var v = _doc.FindVariable(r.Root.Id);
                if (v is null) return; // reported structurally
                current = v.Type;
                break;
            }
            case "temp":
            {
                var temp = ctx.Body?.Temporaries?.FirstOrDefault(t => string.Equals(t.Name, r.Root.Name, StringComparison.OrdinalIgnoreCase));
                if (temp is null)
                {
                    _result.Error(ValidationCodes.UndeclaredTemporary, $"temporary '{r.Root.Name}' is not declared in the body", loc);
                    return;
                }
                current = temp.Type;
                break;
            }
            case "this":
                if (ctx.Pou is not { Kind: "functionBlock" })
                    _result.Error(ValidationCodes.ThisOutsideFunctionBlock, "this used outside a function block", loc);
                current = ctx.Pou is null ? null : IrTypeRef.Named(ctx.Pou.Name);
                break;
            case "unresolved":
                _result.Error(ValidationCodes.UnresolvedReference, $"unresolved reference '{r.Root.Name}'", loc);
                return;
            case "system":
                return;
            default:
                return; // enum value reported structurally
        }
        if (!_options.CheckTyping || current is null) return;

        foreach (var seg in r.Path)
        {
            if (current is null) return;
            switch (seg)
            {
                case IrMemberSegment m:
                {
                    if (current.Kind != "named" || current.Name is null) { if (current.Kind is "array") _result.Error(ValidationCodes.PathMemberNotFound, $"member '{m.Name}' on an array value", loc); return; }
                    if (AtomicTypes.IsAtomic(current.Name))
                    {
                        _result.Error(ValidationCodes.PathMemberNotFound, $"member '{m.Name}' on atomic {current.Name}", loc);
                        return;
                    }
                    var members = _types.MembersOf(current.Name);
                    if (members is null) return; // opaque or unknown: stop silently
                    if (!members.TryGetValue(m.Name, out var next))
                    {
                        // string family members (LEN / DATA) are modelled as string kind; allow them
                        if (_types.IsStringType(current)) return;
                        _result.Error(ValidationCodes.PathMemberNotFound, $"type {current.Name} has no member '{m.Name}'", loc);
                        return;
                    }
                    current = next;
                    break;
                }
                case IrIndexSegment ix:
                {
                    var shape = _types.ArrayShape(current);
                    if (shape is null)
                    {
                        if (_types.IsStringType(current)) return; // character indexing of a string value
                        if (_types.Classify(current) == TypeResolver.Cat.Unknown) return;
                        _result.Error(ValidationCodes.PathIndexCount, $"index on non-array {current}", loc);
                        return;
                    }
                    if (shape.Value.Dims.Count != ix.Indices.Count)
                    {
                        _result.Error(ValidationCodes.PathIndexCount, $"{ix.Indices.Count} index(es) on an array of {shape.Value.Dims.Count} dimension(s)", loc);
                        return;
                    }
                    current = shape.Value.Element;
                    break;
                }
                case IrLengthSegment:
                    if (!_types.IsStringType(current) && _types.Classify(current) != TypeResolver.Cat.Unknown)
                        _result.Error(ValidationCodes.PathStringSegment, $"length of non-string {current}", loc);
                    current = IrTypeRef.Named("DINT");
                    break;
                case IrCharSegment:
                    if (!_types.IsStringType(current) && _types.Classify(current) != TypeResolver.Cat.Unknown)
                        _result.Error(ValidationCodes.PathStringSegment, $"char of non-string {current}", loc);
                    // A character is CHAR in IEC terms; vendors that expose string bytes as small integers (Logix SINT DATA)
                    // may declare the ref as any 8/16-bit integer or bit string instead.
                    current = r.Type is { IsNamed: true, Name: { } declaredName } && Catalogs.AtomicTypes.Get(declaredName) is { Bits: <= 16 } small && (small.IsInteger || small.Category == Catalogs.AtomicCategory.BitString)
                        ? IrTypeRef.Named(small.Name)
                        : IrTypeRef.Named("CHAR");
                    break;
                case IrDerefSegment:
                    current = current.Kind is "pointer" or "reference" ? current.To : null;
                    break;
            }
        }
        if (current is not null && r.Type is { } declared && !declared.IsAny && !r.Opaque && !current.Equals(declared))
        {
            // alias / subrange bases and string kinds compare loosely
            if (_types.Compatible(declared, current) && _types.Classify(declared) == _types.Classify(current)) return;
            _result.Warning(ValidationCodes.RefLeafTypeMismatch, $"ref declares type {declared} but its path reaches {current}", loc);
        }
    }
}
