namespace Plc.Ir;

/// <summary>Where a statement or expression sits in the document.</summary>
public sealed class IrWalkContext
{
    public IrPou? Pou { get; init; }
    public IrMethod? Method { get; init; }
    public IrAction? Action { get; init; }
    public IrBody? Body { get; init; }

    /// <summary>The variable whose alias target or initial value is being visited, when outside logic.</summary>
    public IrVariable? Variable { get; init; }

    /// <summary>Program instance name, when the walk started from a task.</summary>
    public string Location => Pou is null ? Variable?.Id ?? "document" : Pou.Name + (Method is not null ? "." + Method.Name : Action is not null ? "/" + Action.Name : "");
}

/// <summary>Visits every body, statement and expression of a document. Override what you need; base methods recurse.</summary>
public class IrWalker
{
    public void Walk(IrDocument document)
    {
        foreach (var g in document.Globals)
            foreach (var v in g.Vars) WalkVariable(v);
        foreach (var pou in document.Pous)
        {
            foreach (var v in pou.Vars) WalkVariable(v, pou);
            if (pou.Body is not null) WalkBody(pou.Body, new IrWalkContext { Pou = pou, Body = pou.Body });
            foreach (var a in pou.Actions) WalkBody(a.Body, new IrWalkContext { Pou = pou, Action = a, Body = a.Body });
            foreach (var m in pou.Methods)
            {
                foreach (var v in m.Vars) WalkVariable(v, pou);
                WalkBody(m.Body, new IrWalkContext { Pou = pou, Method = m, Body = m.Body });
            }
            foreach (var p in pou.Properties)
            {
                if (p.Get is not null) WalkBody(p.Get, new IrWalkContext { Pou = pou, Body = p.Get });
                if (p.Set is not null) WalkBody(p.Set, new IrWalkContext { Pou = pou, Body = p.Set });
            }
        }
    }

    protected virtual void WalkVariable(IrVariable variable, IrPou? pou = null)
    {
        if (variable.Alias?.Target is { } target) VisitExpression(target, new IrWalkContext { Pou = pou, Variable = variable });
    }

    public virtual void WalkBody(IrBody body, IrWalkContext ctx)
    {
        VisitBody(body, ctx);
        VisitStatements(body.Statements, ctx);
        switch (body.Diagram)
        {
            case IrLdDiagram ld:
                foreach (var rung in ld.Rungs) VisitElements(rung.Elements, ctx);
                break;
            case IrNetDiagram net:
                foreach (var sheet in net.Sheets)
                    foreach (var e in sheet.Elements)
                    {
                        if (e.Operand is not null) VisitExpression(e.Operand, ctx);
                        if (e.Parameters is not null) foreach (var p in e.Parameters.Values) VisitExpression(p, ctx);
                    }
                break;
            case IrSfcDiagram sfc:
                foreach (var step in sfc.Steps)
                {
                    if (step.Preset is not null) VisitExpression(step.Preset, ctx);
                    if (step.LimitHigh is not null) VisitExpression(step.LimitHigh, ctx);
                    if (step.LimitLow is not null) VisitExpression(step.LimitLow, ctx);
                    foreach (var a in step.Actions)
                    {
                        if (a.Preset is not null) VisitExpression(a.Preset, ctx);
                        if (a.Body is not null) WalkBody(a.Body, new IrWalkContext { Pou = ctx.Pou, Action = ctx.Action, Method = ctx.Method, Body = a.Body });
                    }
                }
                foreach (var t in sfc.Transitions)
                    if (t.Condition is not null) VisitExpression(t.Condition, ctx);
                break;
        }
    }

    protected virtual void VisitBody(IrBody body, IrWalkContext ctx)
    {
    }

    private void VisitElements(List<IrLdElement> elements, IrWalkContext ctx)
    {
        foreach (var e in elements)
        {
            VisitElement(e, ctx);
            switch (e)
            {
                case IrLdContact c:
                    if (c.Operand is not null) VisitExpression(c.Operand, ctx);
                    if (c.Storage is not null) VisitExpression(c.Storage, ctx);
                    if (c.Args is not null) foreach (var a in c.Args) VisitExpression(a, ctx);
                    break;
                case IrLdCoil coil:
                    VisitExpression(coil.Operand, ctx);
                    if (coil.Storage is not null) VisitExpression(coil.Storage, ctx);
                    break;
                case IrLdBlock b:
                    foreach (var a in b.Args) VisitExpression(a, ctx);
                    if (b.Pins is not null) foreach (var p in b.Pins.Values) VisitExpression(p, ctx);
                    break;
                case IrLdBranch br:
                    foreach (var leg in br.Legs) VisitElements(leg, ctx);
                    break;
                case IrLdReturn r:
                    if (r.Outputs is not null) foreach (var o in r.Outputs) VisitExpression(o, ctx);
                    break;
            }
        }
    }

    protected virtual void VisitElement(IrLdElement element, IrWalkContext ctx)
    {
    }

    public void VisitStatements(IEnumerable<IrStatement> statements, IrWalkContext ctx)
    {
        foreach (var s in statements) VisitStatement(s, ctx);
    }

    /// <summary>Called for every statement (before its children). Base recurses into nested expressions and statements.</summary>
    public virtual void VisitStatement(IrStatement s, IrWalkContext ctx)
    {
        switch (s)
        {
            case IrAssign a:
                VisitExpression(a.Target, ctx);
                VisitExpression(a.Value, ctx);
                break;
            case IrFbCall c:
                if (c.Instance is not null) VisitExpression(c.Instance, ctx);
                foreach (var x in c.Inputs.Values) VisitExpression(x, ctx);
                foreach (var x in c.Outputs.Values) VisitExpression(x, ctx);
                if (c.En is not null) VisitExpression(c.En, ctx);
                if (c.Eno is not null) VisitExpression(c.Eno, ctx);
                break;
            case IrFnCall f:
                foreach (var x in f.Args) if (x is not null) VisitExpression(x, ctx);
                if (f.Outputs is not null) foreach (var x in f.Outputs.Values) VisitExpression(x, ctx);
                if (f.En is not null) VisitExpression(f.En, ctx);
                break;
            case IrActionCall ac:
                foreach (var x in ac.Inputs) VisitExpression(x, ctx);
                foreach (var x in ac.Outputs) VisitExpression(x, ctx);
                if (ac.En is not null) VisitExpression(ac.En, ctx);
                break;
            case IrSysCall sc:
                foreach (var x in sc.Args) if (x is not null) VisitExpression(x, ctx);
                if (sc.En is not null) VisitExpression(sc.En, ctx);
                break;
            case IrCopy cp:
                VisitExpression(cp.Source, ctx);
                VisitExpression(cp.Dest, ctx);
                VisitExpression(cp.Count, ctx);
                if (cp.En is not null) VisitExpression(cp.En, ctx);
                break;
            case IrFill fl:
                VisitExpression(fl.Value, ctx);
                VisitExpression(fl.Dest, ctx);
                VisitExpression(fl.Count, ctx);
                if (fl.En is not null) VisitExpression(fl.En, ctx);
                break;
            case IrIf i:
                foreach (var b in i.Branches)
                {
                    VisitExpression(b.Condition, ctx);
                    VisitStatements(b.Body, ctx);
                }
                if (i.Else is not null) VisitStatements(i.Else, ctx);
                break;
            case IrCase c:
                VisitExpression(c.Selector, ctx);
                foreach (var arm in c.Arms)
                {
                    foreach (var l in arm.Labels)
                    {
                        VisitExpression(l.Value, ctx);
                        if (l.To is not null) VisitExpression(l.To, ctx);
                    }
                    VisitStatements(arm.Body, ctx);
                }
                if (c.Else is not null) VisitStatements(c.Else, ctx);
                break;
            case IrFor f:
                VisitExpression(f.Variable, ctx);
                VisitExpression(f.From, ctx);
                VisitExpression(f.To, ctx);
                if (f.By is not null) VisitExpression(f.By, ctx);
                VisitStatements(f.Body, ctx);
                break;
            case IrWhile w:
                VisitExpression(w.Condition, ctx);
                VisitStatements(w.Body, ctx);
                break;
            case IrRepeat r:
                VisitStatements(r.Body, ctx);
                VisitExpression(r.Until, ctx);
                break;
            case IrReturn r:
                if (r.En is not null) VisitExpression(r.En, ctx);
                break;
            case IrJump j:
                if (j.En is not null) VisitExpression(j.En, ctx);
                break;
        }
    }

    /// <summary>Called for every expression (before its children). Base recurses.</summary>
    public virtual void VisitExpression(IrExpression e, IrWalkContext ctx)
    {
        switch (e)
        {
            case IrRef r:
                foreach (var seg in r.Path)
                {
                    switch (seg)
                    {
                        case IrIndexSegment i: foreach (var x in i.Indices) VisitExpression(x, ctx); break;
                        case IrCharSegment c: VisitExpression(c.Index, ctx); break;
                    }
                }
                break;
            case IrUnary u: VisitExpression(u.Operand, ctx); break;
            case IrBinary b: VisitExpression(b.Left, ctx); VisitExpression(b.Right, ctx); break;
            case IrFunction f: foreach (var a in f.Args) if (a is not null) VisitExpression(a, ctx); break;
            case IrConv c: VisitExpression(c.Operand, ctx); break;
            case IrBitAccess ba: VisitExpression(ba.Of, ctx); VisitExpression(ba.Bit, ctx); break;
            case IrEdge ed: VisitExpression(ed.Clk, ctx); VisitExpression(ed.Storage, ctx); break;
            case IrSysFn sf: foreach (var a in sf.Args) if (a is not null) VisitExpression(a, ctx); break;
        }
    }

    // ------------------------------------------------------------------ convenience enumerators

    /// <summary>Every expression in the document (logic, diagrams and alias targets), depth first.</summary>
    public static IEnumerable<(IrExpression Expression, IrWalkContext Context)> Expressions(IrDocument document)
    {
        var collector = new Collector();
        collector.Walk(document);
        return collector.ExpressionList;
    }

    /// <summary>Every statement in the document, depth first.</summary>
    public static IEnumerable<(IrStatement Statement, IrWalkContext Context)> Statements(IrDocument document)
    {
        var collector = new Collector();
        collector.Walk(document);
        return collector.StatementList;
    }

    private sealed class Collector : IrWalker
    {
        public readonly List<(IrExpression, IrWalkContext)> ExpressionList = new();
        public readonly List<(IrStatement, IrWalkContext)> StatementList = new();

        public override void VisitStatement(IrStatement s, IrWalkContext ctx)
        {
            StatementList.Add((s, ctx));
            base.VisitStatement(s, ctx);
        }

        public override void VisitExpression(IrExpression e, IrWalkContext ctx)
        {
            ExpressionList.Add((e, ctx));
            base.VisitExpression(e, ctx);
        }
    }
}
