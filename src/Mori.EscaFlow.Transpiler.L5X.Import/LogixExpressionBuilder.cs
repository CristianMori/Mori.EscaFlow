using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Linking;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>Converts parsed ST / ladder operand trees into typed IR expressions and statements for one routine container (program or add-on).</summary>
public sealed class LogixExpressionBuilder
{
    private readonly LogixIrBuilder _owner;
    private readonly ScopeResolver _resolver;
    private readonly IRoutineContainer? _container;
    private readonly Program? _program;
    private readonly AddOnInstructionDefinition? _aoi;

    public LogixExpressionBuilder(LogixIrBuilder owner, ScopeResolver resolver, IRoutineContainer? container)
    {
        _owner = owner;
        _resolver = resolver;
        _container = container;
        _program = container as Program;
        _aoi = container as AddOnInstructionDefinition;
    }

    public IRoutineContainer? Container => _container;
    public LogixIrBuilder Owner => _owner;

    // ------------------------------------------------------------------ references

    /// <summary>A tag path as an expression: a <c>ref</c>, a <c>bit</c> access, or a <c>keyword</c> when the path names a module (GSV/SSV/MSG instance operands).</summary>
    public IrExpression Ref(OperandPath path)
    {
        var r = _resolver.Resolve(path, _container);
        if (r.IsModuleReference)
            return new IrKeyword { Text = path.ToLogixText(), Kind = "name" };

        // An alias whose target is a module I/O path (AliasFor="Local:3:I.Data.4") is not bound to a tag by the
        // linker; resolve the target path directly and note the alias on the result.
        if (r.Error is not null && r.Tag is { TagType: Mori.EscaFlow.Transpiler.L5X.Model.Tags.TagType.Alias, AliasPath: { } aliasPath } aliasTag && aliasPath.Root.Contains(':') && aliasPath.ProgramName is null)
        {
            var merged = new OperandPath(aliasPath.Root, aliasPath.Segments.Concat(path.Segments).ToList());
            var expanded = Ref(merged);
            var aliasId = _owner.VarId(aliasTag);
            switch (expanded)
            {
                case IrRef er when er.Error is null: er.Alias = aliasId; return er;
                case IrBitAccess { Of: IrRef inner } ba when inner.Error is null: inner.Alias = aliasId; return ba;
            }
        }

        var ir = new IrRef();
        IReadOnlyList<PathSegment> segments = path.Segments;
        DataTypeDefinition? startType = null;
        int[] startDims = Array.Empty<int>();

        if (r.Tag is not null)
        {
            ir.Root = IrRefRoot.Variable(_owner.VarId(r.Tag));
            segments = r.ExpandedPath.Segments;
            startType = r.Tag.DataType?.Definition;
            startDims = r.Tag.Dimensions;
            var written = L5xLinker.FindTag(_owner.Controller, path, _program, _aoi);
            if (written is not null && !ReferenceEquals(written, r.Tag)) ir.Alias = _owner.VarId(written);
        }
        else if (r.Module is not null && r.ModuleData is not null)
        {
            var id = _owner.ModuleVarId(r.ModuleData);
            if (id is null)
            {
                ir.Root = IrRefRoot.Unresolved(path.ToLogixText());
                ir.Error = $"Module data of '{r.Module.Name}' is not an I/O image";
            }
            else
            {
                ir.Root = IrRefRoot.Variable(id);
                startType = r.ModuleData.DataTypeName is { } tn ? _owner.Controller.Types.TryGet(tn) : null;
                // Rack-optimized slot (Parent:slot:I): the data lives in the parent image's Data array, element = slot.
                var rootParts = path.Root.Split(':');
                if (rootParts.Length == 3 && int.TryParse(rootParts[1], out var slot) && id.StartsWith($"global/{LogixVendor.IoGvl}/{rootParts[0]}:", StringComparison.OrdinalIgnoreCase) && (startType as StructuredType)?.FindMember("Data") is { IsArray: true } dataMember && r.LeafType is Mori.EscaFlow.Transpiler.L5X.Model.DataTypes.AtomicType)
                {
                    ir.Path.Add(new IrMemberSegment { Name = "Data" });
                    ir.Path.Add(new IrIndexSegment { Indices = { IrLiteral.OfInt(slot) } });
                    startType = dataMember.DataType.Definition ?? _owner.Controller.Types.TryGet(dataMember.DataType.Name);
                }
            }
        }
        else if (r.Error is null && r.Tag is null && IsSystemName(path.Root))
        {
            ir.Root = IrRefRoot.System(path.Root);
        }
        else
        {
            ir.Root = IrRefRoot.Unresolved(path.ToLogixText());
            ir.Error = r.Error ?? "unresolved";
        }

        ir.Io = r.IsIoData || r.ModuleData is not null;
        // Opaque: the reference enters a type whose layout is unknown, or ends on a member whose type the project does not define.
        ir.Opaque = r.Error is null && r.LeafType is null && !r.IsBit && ir.Root.Kind == "var";
        if (r.Error is not null && ir.Error is null) ir.Error = r.Error;

        // Segments: BIT-overlay members stay members (BOOL members of the neutral type); .n / .[i] / integer[n] become bit accesses.
        var bitAt = -1;
        if (r.IsBit && segments.Count > 0 && r.LeafMember?.IsBitOverlay != true)
        {
            var last = segments[^1];
            if (last is BitSegment or DynamicBitSegment || (last is IndexSegment && r.BitHostType is not null)) bitAt = segments.Count - 1;
        }
        AppendSegments(ir, segments, bitAt < 0 ? segments.Count : bitAt, startType, startDims);

        if (bitAt >= 0)
        {
            ir.Type = r.BitHostType is null ? null : IrTypeRef.Named(r.BitHostType.Name);
            ir.Opaque = r.BitHostType is null && r.Error is null && ir.Root.Kind == "var";
            var bit = segments[bitAt] switch
            {
                BitSegment b => (IrExpression)IrLiteral.OfInt(b.Bit),
                DynamicBitSegment d => Expr(d.Bit),
                IndexSegment i => Expr(i.Indices[0]),
                _ => IrLiteral.OfInt(0),
            };
            return new IrBitAccess { Of = ir, Bit = bit, Type = IrTypeRef.Named("BOOL") };
        }

        ir.Type = TypeOf(r);
        return ir;
    }

    /// <summary>The reference as an assignment target; a keyword or error becomes an error reference.</summary>
    public IrExpression Target(OperandPath path)
    {
        var e = Ref(path);
        return e is IrRef or IrBitAccess ? e : new IrRef { Root = IrRefRoot.Unresolved(path.ToLogixText()), Error = "operand is not a tag reference" };
    }

    private void AppendSegments(IrRef ir, IReadOnlyList<PathSegment> segments, int count, DataTypeDefinition? type, int[] dims)
    {
        for (var i = 0; i < count; i++)
        {
            switch (segments[i])
            {
                case MemberSegment m:
                {
                    var st = type as StructuredType;
                    if (st is { IsStringFamily: true })
                    {
                        if (m.Name.Equals("LEN", StringComparison.OrdinalIgnoreCase))
                        {
                            ir.Path.Add(new IrLengthSegment());
                            type = _owner.Controller.Types.TryGet("DINT");
                            dims = Array.Empty<int>();
                            continue;
                        }
                        if (m.Name.Equals("DATA", StringComparison.OrdinalIgnoreCase) && i + 1 < count && segments[i + 1] is IndexSegment { Indices.Count: 1 } idx)
                        {
                            ir.Path.Add(new IrCharSegment { Index = Expr(idx.Indices[0]) });
                            type = _owner.Controller.Types.TryGet("SINT");
                            dims = Array.Empty<int>();
                            i++;
                            continue;
                        }
                    }
                    ir.Path.Add(new IrMemberSegment { Name = m.Name });
                    var member = st?.FindMember(m.Name);
                    type = member is null ? null : member.IsBitOverlay ? _owner.Controller.Types.TryGet("BOOL") : member.DataType.Definition ?? _owner.Controller.Types.TryGet(member.DataType.Name);
                    dims = member is { Dimension: > 0 } ? new[] { member.Dimension } : Array.Empty<int>();
                    break;
                }
                case IndexSegment idx:
                    ir.Path.Add(new IrIndexSegment { Indices = idx.Indices.Select(Expr).ToList() });
                    dims = Array.Empty<int>();
                    break;
                case BitSegment b:
                    // A bit segment that is not the trailing one (tolerated by the resolver) is kept as a member named by its number.
                    ir.Path.Add(new IrMemberSegment { Name = b.Bit.ToString() });
                    break;
                case DynamicBitSegment:
                    ir.Path.Add(new IrMemberSegment { Name = "[?]" });
                    break;
            }
        }
    }

    private static bool IsSystemName(string root) => root.Length > 2 && (root[0] is 'S' or 's') && root[1] == ':';

    private static IrTypeRef? TypeOf(ResolvedReference r)
    {
        if (r.IsBit) return IrTypeRef.Named("BOOL");
        if (r.LeafType is null) return null;
        var leaf = IrTypeRef.Named(r.LeafType.Name);
        return r.LeafDimensions.Length > 0 ? IrTypeRef.Array(leaf, r.LeafDimensions) : leaf;
    }

    // ------------------------------------------------------------------ expressions

    public IrExpression Expr(StExpression e)
    {
        switch (e)
        {
            case StLiteral l:
                return Literal(l.Value);
            case StVariable v:
                return Ref(v.Path);
            case StUnary u:
            {
                var operand = Expr(u.Operand);
                return new IrUnary { Operator = u.Op == UnaryOp.Not ? "not" : "neg", Operand = operand, Type = operand.Type };
            }
            case StBinary b:
            {
                var left = Expr(b.Left);
                var right = Expr(b.Right);
                var op = b.Op switch
                {
                    BinaryOp.Add => "add",
                    BinaryOp.Subtract => "sub",
                    BinaryOp.Multiply => "mul",
                    BinaryOp.Divide => "div",
                    BinaryOp.Modulo => "mod",
                    BinaryOp.Power => "pow",
                    BinaryOp.Equal => "eq",
                    BinaryOp.NotEqual => "ne",
                    BinaryOp.Less => "lt",
                    BinaryOp.Greater => "gt",
                    BinaryOp.LessOrEqual => "le",
                    BinaryOp.GreaterOrEqual => "ge",
                    BinaryOp.And => "and",
                    BinaryOp.Or => "or",
                    BinaryOp.Xor => "xor",
                    _ => b.Op.ToString().ToLowerInvariant(),
                };
                return Binary(op, left, right);
            }
            case StCall c:
            {
                var args = c.Arguments.Select(a => a is null ? null : Expr(a)).ToList();
                if (LogixInstructionMap.StFunctionFor(c.Name) is { } fn)
                {
                    var def = StandardFunctions.Get(fn);
                    return new IrFunction { Name = fn, Args = args, Type = def?.ResultType(args.Select(a => a?.Type?.LeafName).ToList()) is { } t ? IrTypeRef.Named(t) : null };
                }
                return new IrSysFn { Catalog = LogixVendor.Id, Service = c.Name.ToUpperInvariant(), Args = args };
            }
            case StKeyword k:
                return new IrKeyword { Text = k.Text, Kind = k.Kind switch { StKeywordKind.Routine => "action", StKeywordKind.Label => "label", _ => "enum" } };
            case StErrorExpression err:
                return new IrErrorExpression { Text = err.RawText };
            default:
                return new IrErrorExpression { Text = e.ToLogixText() };
        }
    }

    public static IrBinary Binary(string op, IrExpression left, IrExpression right)
        => new() { Operator = op, Left = left, Right = right, Type = BinaryType(op, left.Type, right.Type) };

    public static IrLiteral Literal(LiteralValue v) => v switch
    {
        IntegerLiteral i => new IrLiteral { Kind = "int", Int = i.Value, Radix = i.RadixBase == 10 ? null : i.RadixBase, Type = IrTypeRef.Named(AtomicTypes.IntegerTypeFor(i.Value)) },
        RealLiteral r => new IrLiteral { Kind = "real", Real = r.Value, Type = IrTypeRef.Named("REAL") },
        StringLiteral s => new IrLiteral { Kind = "string", String = s.Value, Type = IrTypeRef.Named("STRING") },
        _ => new IrLiteral { Kind = "string", String = v.ToLogixText() },
    };

    private static IrTypeRef? BinaryType(string op, IrTypeRef? left, IrTypeRef? right)
    {
        var l = left?.LeafName;
        var r = right?.LeafName;
        switch (op)
        {
            case "eq" or "ne" or "lt" or "gt" or "le" or "ge":
                return IrTypeRef.Named("BOOL");
            case "and" or "or" or "xor":
                if (IsBool(l) && IsBool(r)) return IrTypeRef.Named("BOOL");
                if (IsBool(l) && r is null || IsBool(r) && l is null) return IrTypeRef.Named("BOOL");
                return Wider(l, r);
            case "div" or "pow":
            {
                var w = Wider(l, r);
                return w;
            }
            default:
                return Wider(l, r);
        }
    }

    private static bool IsBool(string? t) => t is not null && t.Equals("BOOL", StringComparison.OrdinalIgnoreCase);

    private static IrTypeRef? Wider(string? a, string? b)
    {
        var w = AtomicTypes.Wider(a, b);
        return w is null ? null : IrTypeRef.Named(w);
    }

    /// <summary>
    /// Wraps <paramref name="value"/> in the conversion Logix performs implicitly when storing it into a variable of
    /// <paramref name="target"/> type: REAL to integer rounds half to even, integer narrowing wraps. Widening and
    /// same-type stores need no conversion.
    /// </summary>
    public static IrExpression Convert(IrExpression value, IrTypeRef? target)
    {
        var to = target?.LeafName;
        var from = value.Type?.LeafName;
        if (to is null || from is null || target is { IsArray: true }) return value;
        var tt = AtomicTypes.Get(to);
        var ft = AtomicTypes.Get(from);
        if (tt is null || ft is null || tt.Name.Equals(ft.Name, StringComparison.OrdinalIgnoreCase)) return value;
        if (tt.Category == AtomicCategory.Bool || ft.Category == AtomicCategory.Bool) return value;
        if (tt.IsInteger && ft.Category == AtomicCategory.Real)
            return new IrConv { To = IrTypeRef.Named(tt.Name), Mode = "roundEven", Operand = value, Type = IrTypeRef.Named(tt.Name) };
        if (tt.IsInteger && ft.IsInteger && tt.Bits < ft.Bits)
            return new IrConv { To = IrTypeRef.Named(tt.Name), Mode = "wrap", Operand = value, Type = IrTypeRef.Named(tt.Name) };
        if (tt.Category == AtomicCategory.Real && ft.Category == AtomicCategory.Real && tt.Bits < ft.Bits)
            return new IrConv { To = IrTypeRef.Named(tt.Name), Mode = "roundEven", Operand = value, Type = IrTypeRef.Named(tt.Name) };
        return value;
    }

    // ------------------------------------------------------------------ statements

    public List<IrStatement> Block(StBlock block)
    {
        var list = new List<IrStatement>(block.Statements.Count);
        foreach (var s in block.Statements)
        {
            var ir = Statement(s);
            if (ir is not null) list.Add(ir);
        }
        return list;
    }

    private IrStatement WithSrc(IrStatement s, TextSpan span)
    {
        s.Src = new IrSrc { Line = span.Line, Col = span.Column };
        return s;
    }

    public IrStatement? Statement(StStatement s)
    {
        switch (s)
        {
            case StAssignment a:
            {
                var target = Target(a.Target.Path);
                return WithSrc(new IrAssign { Target = target, Value = Convert(Expr(a.Value), target.Type), NonRetentive = a.NonRetentive }, a.Span);
            }
            case StCallStatement c:
            {
                var args = c.Arguments.Select(x => x is null ? null : Expr(x)).ToList();
                var list = new List<IrStatement>();
                _owner.Calls.Emit(this, c.Name, c.IsAddOn, args, null, list);
                if (list.Count == 0) return null;
                if (list.Count == 1) return WithSrc(list[0], c.Span);
                // JSR with bound parameters expands to several statements: keep them together in a block-if TRUE is not neutral, so use an IF TRUE wrapper.
                foreach (var st in list) st.Src = new IrSrc { Line = c.Span.Line, Col = c.Span.Column };
                return new IrIf { Branches = { new IrIfBranch { Condition = IrLiteral.True, Body = list } }, Src = new IrSrc { Line = c.Span.Line, Col = c.Span.Column } };
            }
            case StIf i:
                return WithSrc(new IrIf
                {
                    Branches = i.Branches.Select(b => new IrIfBranch { Condition = Expr(b.Condition), Body = Block(b.Body) }).ToList(),
                    Else = i.Else is null ? null : Block(i.Else),
                }, i.Span);
            case StCase c:
                return WithSrc(new IrCase
                {
                    Selector = Expr(c.Selector),
                    Arms = c.Arms.Select(arm => new IrCaseArm
                    {
                        Labels = arm.Labels.Select(l => new IrCaseLabel { Value = IrLiteral.OfInt(l.From), To = l.To is { } to ? IrLiteral.OfInt(to) : null }).ToList(),
                        Body = Block(arm.Body),
                    }).ToList(),
                    Else = c.Else is null ? null : Block(c.Else),
                }, c.Span);
            case StFor f:
                return WithSrc(new IrFor { Variable = Target(f.Variable.Path), From = Expr(f.From), To = Expr(f.To), By = f.By is null ? null : Expr(f.By), Body = Block(f.Body) }, f.Span);
            case StWhile w:
                return WithSrc(new IrWhile { Condition = Expr(w.Condition), Body = Block(w.Body) }, w.Span);
            case StRepeat r:
                return WithSrc(new IrRepeat { Body = Block(r.Body), Until = Expr(r.Until) }, r.Span);
            case StExit:
                return WithSrc(new IrExit(), s.Span);
            case StReturn:
                return WithSrc(new IrReturn(), s.Span);
            case StErrorStatement e:
                return WithSrc(new IrErrorStatement { Text = e.RawText }, e.Span);
            case StEmptyStatement:
                return null;
            default:
                return WithSrc(new IrErrorStatement { Text = s.ToString() ?? "" }, s.Span);
        }
    }

    // ------------------------------------------------------------------ ladder operands

    public IrExpression Operand(Operand op)
    {
        switch (op)
        {
            case TagRefOperand t: return Ref(t.Path);
            case LiteralOperand l: return Literal(l.Value);
            case ExpressionOperand e: return Expr(e.Expression);
            case RoutineRefOperand r: return new IrKeyword { Text = r.RoutineName, Kind = "action" };
            case LabelOperand l: return new IrKeyword { Text = l.Label, Kind = "label" };
            case KeywordOperand k: return new IrKeyword { Text = k.Text, Kind = "enum" };
            case PlaceholderOperand: return new IrPlaceholder();
            case EmptyOperand: return new IrPlaceholder { Empty = true };
            case RawOperand raw: return new IrErrorExpression { Text = raw.Text };
            default: return new IrErrorExpression { Text = op.Text };
        }
    }
}
