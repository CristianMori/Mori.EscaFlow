using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;

namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>
/// Turns a rung into the neutral ladder elements (as drawn) and the lowered statements of the enclosing body.
/// The rung condition flows left to right as a boolean expression; parallel branches OR their legs; compound
/// conditions shared by several outputs are stored once in a body temporary. See docs/plc-ir-2.0.md for the contract.
/// </summary>
/// <remarks>
/// <para>
/// Logix evaluates a rung as a boolean "rung-condition-in" that starts true at the left rail. Every input
/// instruction ANDs its result into it; every output instruction acts on the current value and passes it on
/// unchanged, so several outputs on one rung share the same condition. A branch evaluates each leg from the
/// condition at the branch's start and ORs the leg results at its end.
/// </para>
/// <para>
/// The lowering keeps two expressions per series: <c>incoming</c> (the condition at the series start, already
/// absolute) and <c>rel</c> (what this series has ANDed so far). The absolute condition of an output is
/// <c>incoming AND rel</c>. When that compound expression would be used by more than one output, it is stored once
/// in a body temporary (<c>_rcN</c>) instead of being duplicated into every output's statement; without that rule the
/// lowered form of a long rung grew to several times the size of the rung.
/// </para>
/// <para>
/// One instance serves a whole body: master-control (MCR) zones and the temporaries must span rungs, because an MCR
/// opened on one rung conditions every rung up to the closing MCR.
/// </para>
/// </remarks>
public sealed class LadderLowering
{
    private readonly LogixExpressionBuilder _x;
    private readonly CallEmitter _calls;

    /// <summary>Body-level temporaries (<c>_rcN</c> conditions, <c>_mcrN</c> zone conditions); shared with the net lowering of the same body.</summary>
    private readonly List<IrTemporary> _temps;

    /// <summary>The statement list of the body being lowered.</summary>
    private List<IrStatement> _out = new();

    /// <summary>Current rung number, stamped on every emitted statement.</summary>
    private int _rung;

    /// <summary>Depth-first index of the element being lowered inside the rung, stamped on its statements.</summary>
    private int _element;

    /// <summary>The condition of the open MCR zone, or null outside a zone. Rungs inside the zone start from it instead of TRUE.</summary>
    private IrRef? _mcr;
    private int _mcrCount;

    public LadderLowering(LogixExpressionBuilder expressions, CallEmitter calls, List<IrTemporary> temporaries)
    {
        _x = expressions;
        _calls = calls;
        _temps = temporaries;
    }

    /// <summary>Lowers <paramref name="ast"/>: statements are appended to <paramref name="body"/>, the elements as drawn are returned.</summary>
    public List<IrLdElement> Lower(RungAst ast, int rungNumber, List<IrStatement> body)
    {
        _out = body;
        _rung = rungNumber;
        _element = 0;
        // Inside an MCR zone the left rail is the zone condition: when it is false every output in the zone sees a
        // false rung (OTE clears, timers reset), which is exactly what the controller does.
        var incoming = _mcr is null ? IrLiteral.True : (IrExpression)_mcr;
        var (_, drawn) = Series(ast.Elements, incoming);
        return drawn;
    }

    /// <summary>The prescan behaviour of the rung: coils cleared, one-shot storages set, function blocks and always-scan services invoked in prescan mode.</summary>
    public List<IrStatement> LowerPrescan(RungAst ast, int rungNumber)
    {
        var list = new List<IrStatement>();
        Prescan(ast.Elements, list, rungNumber);
        return list;
    }

    // ------------------------------------------------------------------ normal scan

    /// <summary>
    /// Lowers one series of elements (a rung or a branch leg) under <paramref name="incoming"/> and returns the
    /// condition the series contributes, relative to <paramref name="incoming"/>, together with the drawn elements.
    /// </summary>
    private (IrExpression Rel, List<IrLdElement> Drawn) Series(IReadOnlyList<RungElement> elements, IrExpression incoming)
    {
        var drawn = new List<IrLdElement>(elements.Count);
        IrExpression rel = IrLiteral.True;
        var relIsAbsolute = false; // set after an edge contact: rel already implies incoming
        IrExpression? absolute = null;

        // The absolute condition at this point of the series. Materialized into a temporary when it is compound and
        // another element after this one will need it again (so the expression is evaluated once per scan, as the
        // controller evaluates the rung once).
        IrExpression Abs(bool willBeReused)
        {
            if (absolute is not null) return absolute;
            var a = relIsAbsolute ? rel : And(incoming, rel);
            if (willBeReused && IsCompound(a)) a = Materialize(a, "_rc");
            absolute = a;
            return a;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            var moreFollow = i < elements.Count - 1;
            var index = _element++;
            switch (elements[i])
            {
                case InstructionElement instr:
                {
                    var role = LogixInstructionMap.RoleOf(instr.Mnemonic, instr.IsAddOn);
                    var args = instr.Arguments.Select(_x.Operand).ToList();
                    switch (role)
                    {
                        case LadderRole.Contact:
                        {
                            var contact = new IrLdContact { Name = instr.Mnemonic };
                            drawn.Add(contact);
                            if (instr.Mnemonic.Equals("ONS", StringComparison.OrdinalIgnoreCase))
                            {
                                // ONS passes the rung condition for one scan on its rising edge, remembering the previous
                                // value in its storage bit. The edge of the condition so far replaces the condition; it is
                                // materialized when compound so the edge tree stays small.
                                var clk = Abs(false);
                                if (IsCompound(clk)) clk = Materialize(clk, "_rc");
                                contact.Edge = "rising";
                                contact.Storage = Arg(args, 0);
                                rel = new IrEdge { Kind = "rising", Clk = clk, Storage = contact.Storage, Type = IrTypeRef.Named("BOOL") };
                                relIsAbsolute = true;
                                absolute = null;
                                break;
                            }
                            var c = Condition(instr, args, contact);
                            if (c is not null)
                            {
                                rel = And(rel, c);
                                absolute = null;
                            }
                            break;
                        }
                        case LadderRole.Coil:
                        {
                            // OSR/OSF name the storage bit first and the output bit second.
                            var coil = new IrLdCoil { Name = instr.Mnemonic, Operand = Arg(args, instr.Mnemonic.ToUpperInvariant() is "OSR" or "OSF" ? 1 : 0) };
                            drawn.Add(coil);
                            EmitCoil(instr.Mnemonic, coil, args, Abs(moreFollow), index);
                            break;
                        }
                        case LadderRole.BlockInput or LadderRole.BlockGate:
                        {
                            var block = new IrLdBlock { Name = instr.Mnemonic, Args = args, Enable = role == LadderRole.BlockInput ? "input" : "gate", IsPou = instr.IsAddOn };
                            drawn.Add(block);
                            EmitBlock(instr, args, role, Abs(moreFollow), index);
                            break;
                        }
                        case LadderRole.Passive:
                            // NOP and SBR are drawn but execute nothing (SBR's parameters are bound at the JSR).
                            drawn.Add(new IrLdBlock { Name = instr.Mnemonic, Args = args, Enable = "none" });
                            break;
                        case LadderRole.Jump:
                        {
                            var label = Arg(args, 0) is IrKeyword k ? k.Text : "?";
                            drawn.Add(new IrLdJump { Name = instr.Mnemonic, Label = label });
                            Emit(When(Abs(moreFollow), new IrJump { Label = label }), index);
                            break;
                        }
                        case LadderRole.Label:
                        {
                            // A label is a position, never conditioned by its rung.
                            var label = Arg(args, 0) is IrKeyword k ? k.Text : "?";
                            drawn.Add(new IrLdLabel { Name = instr.Mnemonic, Label = label });
                            Emit(new IrLabel { Name = label }, index);
                            break;
                        }
                        case LadderRole.McrBegin:
                        {
                            // The first MCR opens the zone with the rung condition at that point; the next MCR closes it.
                            if (_mcr is null)
                            {
                                drawn.Add(new IrLdMcr { Name = instr.Mnemonic, Begin = true });
                                var zone = Abs(false);
                                _mcr = Materialize(zone, "_mcr", force: true);
                            }
                            else
                            {
                                drawn.Add(new IrLdMcr { Name = instr.Mnemonic, Begin = false });
                                _mcr = null;
                            }
                            break;
                        }
                        case LadderRole.Return:
                        {
                            // TND / RET leave the routine when the rung is true; RET's operands are the values the JSR copies out.
                            drawn.Add(new IrLdReturn { Name = instr.Mnemonic, Outputs = args.Count > 0 ? args : null });
                            Emit(When(Abs(moreFollow), new IrReturn()), index);
                            break;
                        }
                    }
                    break;
                }
                case BranchElement branch:
                {
                    // Every leg starts from the condition at the branch; a leg that contains outputs reuses it, so it is
                    // materialized once rather than copied into each leg.
                    var into = Abs(HasOutputs(branch));
                    var drawnBranch = new IrLdBranch();
                    drawn.Add(drawnBranch);
                    IrExpression? merged = null;
                    foreach (var leg in branch.Legs)
                    {
                        var (legRel, legDrawn) = Series(leg, into);
                        drawnBranch.Legs.Add(legDrawn);
                        merged = merged is null ? legRel : Or(merged, legRel);
                    }
                    // Combine the branch's contribution (merged, relative to "into") with what came before it in
                    // this series (rel) by a plain AND. This is correct whether or not an earlier element in this
                    // series has already made "rel" absolute (relIsAbsolute, set after a one-shot/ONS: see above):
                    // "into" was computed by Abs() from "rel" either way (incoming-AND-rel when still relative, or
                    // rel itself once absolute), so appending "merged" by ANDing it onto "rel" reproduces "into AND
                    // merged" under either reading once Abs() is applied again later. relIsAbsolute therefore only
                    // ever changes how a later Abs() call interprets "rel", never how new conditions are folded into
                    // it here — which is why both branches of what used to be written as a condition on
                    // relIsAbsolute were identical. See LadderLoweringTests.EdgeThenBranchCombinesWithPlainAnd for
                    // a worked example (a one-shot immediately followed by a branch).
                    rel = And(rel, merged ?? IrLiteral.True);
                    absolute = null;
                    break;
                }
            }
        }
        return (rel, drawn);
    }

    /// <summary>The i-th operand, or an empty placeholder when the instruction has fewer operands.</summary>
    private static IrExpression Arg(List<IrExpression> args, int i) => i < args.Count ? args[i] : new IrPlaceholder { Empty = true };

    /// <summary>True when any leg (recursively) contains an element that acts on the rung condition; legs made only of contacts do not need the branch condition materialized.</summary>
    private static bool HasOutputs(BranchElement branch)
    {
        foreach (var leg in branch.Legs)
            foreach (var e in leg)
            {
                switch (e)
                {
                    case InstructionElement i when LogixInstructionMap.RoleOf(i.Mnemonic, i.IsAddOn) is not (LadderRole.Contact or LadderRole.Passive):
                        return true;
                    case BranchElement b when HasOutputs(b):
                        return true;
                }
            }
        return false;
    }

    /// <summary>The boolean contribution of a contact instruction (null for no effect), filling the drawn contact.</summary>
    /// <remarks>
    /// Comparisons become typed BOOL expressions; LIM and MEQ become expression trees that spell out their Logix
    /// semantics; DTR (data transitional, which also updates its reference operand) and any unknown boolean
    /// instruction become a vendor function so no information is lost.
    /// </remarks>
    private IrExpression? Condition(InstructionElement instr, List<IrExpression> args, IrLdContact contact)
    {
        var name = instr.Mnemonic.ToUpperInvariant();
        switch (name)
        {
            case "XIC":
                contact.Operand = Bool(Arg(args, 0));
                return contact.Operand;
            case "XIO":
                contact.Operand = Bool(Arg(args, 0));
                contact.Negated = true;
                return Not(contact.Operand);
            case "CMP":
                // CMP carries an arbitrary boolean expression written in ST syntax.
                contact.Operand = Bool(Arg(args, 0));
                return contact.Operand;
            case "AFI":
                // Always false: everything after it on the rung is disabled.
                contact.Operand = IrLiteral.False;
                return IrLiteral.False;
            case "LIM":
            {
                var low = Arg(args, 0);
                var test = Arg(args, 1);
                var high = Arg(args, 2);
                contact.Args = args;
                // Logix LIM: low <= high -> low <= test <= high; low > high -> test >= low OR test <= high.
                var normal = And(Cmp("le", low, high), And(Cmp("le", low, test), Cmp("le", test, high)));
                var wrapped = And(Cmp("gt", low, high), Or(Cmp("ge", test, low), Cmp("le", test, high)));
                contact.Operand = Or(normal, wrapped);
                return contact.Operand;
            }
            case "MEQ":
            {
                // Masked equal: only the bits set in the mask take part in the comparison.
                var source = Arg(args, 0);
                var mask = Arg(args, 1);
                var compare = Arg(args, 2);
                contact.Args = args;
                contact.Operand = Cmp("eq", LogixExpressionBuilder.Binary("and", source, mask), LogixExpressionBuilder.Binary("and", compare, mask));
                return contact.Operand;
            }
            case "DTR":
                contact.Args = args;
                contact.Operand = new IrSysFn { Catalog = LogixVendor.Id, Service = "DTR", Args = args.Cast<IrExpression?>().ToList(), Type = IrTypeRef.Named("BOOL") };
                return contact.Operand;
            default:
            {
                if (LogixInstructionMap.ComparisonFor(name) is { } op)
                {
                    contact.Args = args;
                    contact.Operand = Cmp(op, Arg(args, 0), Arg(args, 1));
                    return contact.Operand;
                }
                contact.Operand = new IrSysFn { Catalog = LogixVendor.Id, Service = name, Args = args.Cast<IrExpression?>().ToList(), Type = IrTypeRef.Named("BOOL") };
                return contact.Operand;
            }
        }
    }

    /// <summary>
    /// Emits the statement of a coil under <paramref name="condition"/>: OTE writes the condition every scan (so a false
    /// rung clears the bit), OTL/OTU latch only when the rung is true, OSR/OSF write the edge of the condition using
    /// their storage bit.
    /// </summary>
    private void EmitCoil(string mnemonic, IrLdCoil coil, List<IrExpression> args, IrExpression condition, int index)
    {
        switch (mnemonic.ToUpperInvariant())
        {
            case "OTE":
                coil.Mode = "assign";
                Emit(new IrAssign { Target = coil.Operand, Value = condition }, index);
                return;
            case "OTL":
                coil.Mode = "set";
                Emit(When(condition, new IrAssign { Target = coil.Operand, Value = IrLiteral.True }), index);
                return;
            case "OTU":
                coil.Mode = "reset";
                Emit(When(condition, new IrAssign { Target = coil.Operand, Value = IrLiteral.False }), index);
                return;
            case "OSR" or "OSF":
            {
                var rising = mnemonic.Equals("OSR", StringComparison.OrdinalIgnoreCase);
                coil.Mode = rising ? "risingPulse" : "fallingPulse";
                coil.Storage = Arg(args, 0);
                Emit(new IrAssign { Target = coil.Operand, Value = new IrEdge { Kind = rising ? "rising" : "falling", Clk = condition, Storage = coil.Storage, Type = IrTypeRef.Named("BOOL") } }, index);
                return;
            }
        }
    }

    /// <summary>
    /// Emits the statement(s) of an output block. Timers and counters become standard blocks with the rung condition
    /// on their enable pin (they must run every scan to time and to reset); RES becomes member assignments; the
    /// arithmetic, move, logic, string and bit instructions become assignments with the implicit Logix conversion;
    /// everything else goes through <see cref="CallEmitter"/>, every scan with the condition as enable
    /// (always-scan services, add-ons) or inside an IF (gated services).
    /// </summary>
    private void EmitBlock(InstructionElement instr, List<IrExpression> args, LadderRole role, IrExpression condition, int index)
    {
        var name = instr.Mnemonic.ToUpperInvariant();

        if (!instr.IsAddOn)
        {
            if (LogixProfiles.StandardBlockFor(name) is { } fb)
            {
                // TON/TOF/RTO read the rung condition as IN, CTU as CU, CTD as CD; the preset and accumulator operands
                // of the ladder form are display-only (the values live in the TIMER / COUNTER structure).
                var pin = name is "CTU" ? "CU" : name is "CTD" ? "CD" : "IN";
                var call = new IrFbCall { Instance = Arg(args, 0), Fb = fb, Profile = LogixVendor.Id };
                call.Inputs[pin] = condition;
                Emit(call, index);
                return;
            }
            switch (name)
            {
                case "RES":
                {
                    // Reset: the accumulator and the status bits of the structure are cleared; the preset is kept.
                    var target = Arg(args, 0);
                    var members = LogixInstructionMap.ResetMembersOf(target.Type?.LeafName ?? "") ?? new[] { "ACC", "DN" };
                    var body = new List<IrStatement>();
                    foreach (var m in members)
                    {
                        if (target is not IrRef r) break;
                        var member = new IrRef { Root = r.Root, Path = r.Path.Concat(new[] { new IrMemberSegment { Name = m } }).ToList(), Io = r.Io, Type = IrTypeRef.Named(m == "ACC" || m == "POS" ? "DINT" : "BOOL") };
                        body.Add(new IrAssign { Target = member, Value = m is "ACC" or "POS" ? IrLiteral.OfInt(0) : IrLiteral.False });
                    }
                    Emit(When(condition, body), index);
                    return;
                }
                case "MOV":
                    Emit(When(condition, Assign(Arg(args, 1), Arg(args, 0))), index);
                    return;
                case "CLR":
                    Emit(When(condition, Assign(Arg(args, 0), IrLiteral.OfInt(0))), index);
                    return;
                case "CPT":
                    Emit(When(condition, Assign(Arg(args, 0), Arg(args, 1))), index);
                    return;
                case "NOT":
                    Emit(When(condition, Assign(Arg(args, 1), new IrUnary { Operator = "not", Operand = Arg(args, 0), Type = Arg(args, 0).Type })), index);
                    return;
                case "NEG":
                    Emit(When(condition, Assign(Arg(args, 1), new IrUnary { Operator = "neg", Operand = Arg(args, 0), Type = Arg(args, 0).Type })), index);
                    return;
                case "BTD":
                    // Bit field distribute writes into its third operand; the function receives all five so the
                    // destination's other bits are preserved.
                    Emit(When(condition, Assign(Arg(args, 2), Fn("BIT_FIELD_DISTRIBUTE", args, Arg(args, 2).Type))), index);
                    return;
                case "MVM":
                    // dest := MASKED_MOVE(source, mask, dest): bits outside the mask keep the destination's value.
                    Emit(When(condition, Assign(Arg(args, 2), Fn("MASKED_MOVE", args, Arg(args, 2).Type))), index);
                    return;
            }
            if (LogixInstructionMap.BinaryOperatorFor(name) is { } op)
            {
                // ADD(a, b, dest) and friends: dest := a op b with the destination's implicit conversion.
                Emit(When(condition, Assign(Arg(args, 2), LogixExpressionBuilder.Binary(op, Arg(args, 0), Arg(args, 1)))), index);
                return;
            }
            if (LogixInstructionMap.FunctionFor(name) is { } fn)
            {
                // Function-style instructions put their destination last: dest := FN(inputs...).
                var dest = Arg(args, args.Count - 1);
                var inputs = args.Take(args.Count - 1).ToList();
                var resultType = Catalogs.StandardFunctions.Get(fn)?.ResultType(inputs.Select(a => a.Type?.LeafName).ToList());
                Emit(When(condition, Assign(dest, Fn(fn, inputs, resultType is null ? dest.Type : IrTypeRef.Named(resultType)))), index);
                return;
            }
        }

        var list = new List<IrStatement>();
        _calls.Emit(_x, instr.Mnemonic, instr.IsAddOn, args.Cast<IrExpression?>().ToList(), role == LadderRole.BlockInput ? condition : null, list);
        if (role == LadderRole.BlockInput)
            foreach (var s in list) Emit(s, index);
        else
            Emit(When(condition, list), index);
    }

    private static IrFunction Fn(string name, List<IrExpression> args, IrTypeRef? type) => new() { Name = name, Args = args.Cast<IrExpression?>().ToList(), Type = type };

    /// <summary>An assignment with the destination's implicit conversion; a destination that is not a reference becomes an error reference rather than an exception.</summary>
    private static IrAssign Assign(IrExpression target, IrExpression value)
        => new() { Target = target is IrRef or IrBitAccess ? target : new IrRef { Root = IrRefRoot.Unresolved(target is IrKeyword k ? k.Text : "?"), Error = "operand is not a tag reference" }, Value = LogixExpressionBuilder.Convert(value, target.Type) };

    // ------------------------------------------------------------------ prescan

    /// <summary>
    /// Prescan form of a series. Logix prescans every routine once when the controller enters Run, treating every
    /// rung as false: OTE clears its bit, OTL/OTU do nothing, ONS and the one-shot coils set their storage bit (so a
    /// condition that is already true on the first real scan does not fire) and clear their output, timers and
    /// counters are reset through their own prescan rules, add-on instances run their prescan logic, and always-scan
    /// services get their prescan call; gated services, moves and arithmetic are skipped.
    /// </summary>
    private void Prescan(IReadOnlyList<RungElement> elements, List<IrStatement> list, int rung)
    {
        foreach (var e in elements)
        {
            switch (e)
            {
                case BranchElement b:
                    foreach (var leg in b.Legs) Prescan(leg, list, rung);
                    break;
                case InstructionElement instr:
                {
                    var name = instr.Mnemonic.ToUpperInvariant();
                    var role = LogixInstructionMap.RoleOf(instr.Mnemonic, instr.IsAddOn);
                    IrStatement? s = null;
                    switch (role)
                    {
                        case LadderRole.Contact when name == "ONS":
                            s = new IrAssign { Target = _x.Operand(instr.Arguments[0]), Value = IrLiteral.True };
                            break;
                        case LadderRole.Coil when name == "OTE":
                            s = new IrAssign { Target = _x.Operand(instr.Arguments[0]), Value = IrLiteral.False };
                            break;
                        case LadderRole.Coil when name is "OSR" or "OSF":
                            if (instr.Arguments.Count > 1) list.Add(Stamp(new IrAssign { Target = _x.Operand(instr.Arguments[1]), Value = IrLiteral.False }, rung));
                            s = new IrAssign { Target = _x.Operand(instr.Arguments[0]), Value = IrLiteral.True };
                            break;
                        case LadderRole.BlockInput when instr.IsAddOn:
                        {
                            // The prescan call keeps the InOut bindings: the instance's InOut parameters must alias their arguments during prescan too.
                            var args = instr.Arguments.Select(a => (IrExpression?)_x.Operand(a)).ToList();
                            var call = new IrFbCall { Instance = args.Count > 0 ? args[0] : null, Pou = instr.Mnemonic, Mode = "prescan" };
                            if (_x.Owner.Controller.FindAoi(instr.Mnemonic) is { } aoi)
                            {
                                var required = aoi.Parameters.Where(p => p.Required).ToList();
                                for (var i = 0; i < required.Count && i + 1 < args.Count; i++)
                                    if (required[i].ParameterUsage == Mori.EscaFlow.Transpiler.L5X.Model.Aoi.AoiParameterUsage.InOut && args[i + 1] is { } a && a is not IrPlaceholder) call.Inputs[required[i].Name] = a;
                            }
                            s = call;
                            break;
                        }
                        case LadderRole.BlockInput when LogixProfiles.StandardBlockFor(name) is { } fb:
                        {
                            // Timers and counters are prescanned with a false enable; the runtime's prescan form clears
                            // EN/TT/DN (and sets CU/CD so the first real scan cannot count).
                            var call = new IrFbCall { Instance = _x.Operand(instr.Arguments[0]), Fb = fb, Profile = LogixVendor.Id, Mode = "prescan" };
                            call.Inputs[name is "CTU" ? "CU" : name is "CTD" ? "CD" : "IN"] = IrLiteral.False;
                            s = call;
                            break;
                        }
                        case LadderRole.BlockInput:
                            s = new IrSysCall { Catalog = LogixVendor.Id, Service = name, Args = instr.Arguments.Select(a => (IrExpression?)_x.Operand(a)).ToList(), Mode = "prescan" };
                            break;
                    }
                    if (s is not null) list.Add(Stamp(s, rung));
                    break;
                }
            }
        }
    }

    /// <summary>Stamps the rung number on a prescan statement.</summary>
    private static IrStatement Stamp(IrStatement s, int rung)
    {
        s.Src = new IrSrc { Rung = rung };
        return s;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Appends a statement to the body with its rung and element provenance.</summary>
    private void Emit(IrStatement s, int element)
    {
        s.Src = new IrSrc { Rung = _rung, Element = element };
        _out.Add(s);
    }

    /// <summary>Stores a condition in a new body temporary (<c>_rcN</c> or <c>_mcrN</c>) and returns a reference to it.</summary>
    private IrRef Materialize(IrExpression condition, string prefix, bool force = false)
    {
        var name = prefix == "_mcr" ? $"_mcr{++_mcrCount}" : $"_rc{_temps.Count(t => t.Name.StartsWith("_rc", StringComparison.Ordinal)) + 1}";
        var temp = new IrTemporary { Name = name, Type = IrTypeRef.Named("BOOL") };
        _temps.Add(temp);
        var r = IrRef.Temp(temp.Name);
        Emit(new IrAssign { Target = r, Value = condition }, _element - 1);
        return IrRef.Temp(temp.Name);
    }

    /// <summary>Literals, references and bit accesses are cheap to repeat; anything else is worth a temporary when reused.</summary>
    private static bool IsCompound(IrExpression e) => e is not (IrLiteral or IrRef or IrBitAccess);

    private static IrStatement When(IrExpression condition, IrStatement statement) => When(condition, new List<IrStatement> { statement });

    /// <summary>Wraps statements in <c>IF condition</c>, except when the condition is the constant TRUE.</summary>
    private static IrStatement When(IrExpression condition, List<IrStatement> statements)
    {
        if (condition is IrLiteral { IsTrue: true } && statements.Count == 1) return statements[0];
        return new IrIf { Branches = { new IrIfBranch { Condition = condition, Body = statements } } };
    }

    /// <summary>Boolean AND with constant folding, so TRUE rails and AFI do not clutter the lowered form.</summary>
    private static IrExpression And(IrExpression a, IrExpression b)
    {
        if (a is IrLiteral { IsTrue: true }) return b;
        if (b is IrLiteral { IsTrue: true }) return a;
        if (a is IrLiteral { IsFalse: true } || b is IrLiteral { IsFalse: true }) return IrLiteral.False;
        return new IrBinary { Operator = "and", Left = a, Right = b, Type = IrTypeRef.Named("BOOL") };
    }

    /// <summary>Boolean OR with constant folding.</summary>
    private static IrExpression Or(IrExpression a, IrExpression b)
    {
        if (a is IrLiteral { IsFalse: true }) return b;
        if (b is IrLiteral { IsFalse: true }) return a;
        if (a is IrLiteral { IsTrue: true } || b is IrLiteral { IsTrue: true }) return IrLiteral.True;
        return new IrBinary { Operator = "or", Left = a, Right = b, Type = IrTypeRef.Named("BOOL") };
    }

    /// <summary>Boolean NOT with constant folding and double-negation removal.</summary>
    private static IrExpression Not(IrExpression a) => a switch
    {
        IrLiteral { IsTrue: true } => IrLiteral.False,
        IrLiteral { IsFalse: true } => IrLiteral.True,
        IrUnary { Operator: "not" } u => u.Operand,
        _ => new IrUnary { Operator = "not", Operand = a, Type = IrTypeRef.Named("BOOL") },
    };

    /// <summary>Marks an untyped operand (an opaque reference) as BOOL, since it is used as a contact.</summary>
    private static IrExpression Bool(IrExpression e)
    {
        e.Type ??= IrTypeRef.Named("BOOL");
        return e;
    }

    private static IrExpression Cmp(string op, IrExpression a, IrExpression b) => new IrBinary { Operator = op, Left = a, Right = b, Type = IrTypeRef.Named("BOOL") };
}
