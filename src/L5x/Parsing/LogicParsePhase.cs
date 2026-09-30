using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Logic;
using L5x.Model.Logic.Fbd;
using L5x.Model.Logic.Sfc;
using L5x.Model.Logic.St;
using L5x.Model.Programs;
using L5x.Parsing.Rll;
using L5x.Parsing.St;

namespace L5x.Parsing;

/// <summary>
/// Parses every rung, structured text body and SFC snippet in parallel. Each work item writes only into its
/// own node and a private diagnostic list; the lists are merged in file order afterwards.
/// </summary>
public static class LogicParsePhase
{
    private abstract record WorkItem(DiagnosticLocation Location)
    {
        public abstract void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink);
    }

    private sealed record RungItem(DiagnosticLocation Location, Rung Rung) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
            => Rung.Ast = rungParser.Parse(Rung.Text, sink);
    }

    private sealed record StItem(DiagnosticLocation Location, StContent Content) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
        {
            var comments = new List<StComment>();
            Content.Ast = StParser.ParseBlock(Content.Lines, context, sink, comments);
            Content.Comments = comments.Count > 0 ? comments : null;
        }
    }

    private sealed record SnippetItem(DiagnosticLocation Location, StSnippet Snippet, bool IsExpression) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
        {
            var comments = new List<StComment>();
            if (IsExpression)
            {
                if (Snippet.Lines.Count == 0 || Snippet.Lines.All(l => string.IsNullOrWhiteSpace(l.Text))) return;
                Snippet.Expression = StParser.ParseExpression(Snippet.Lines, context, sink, comments);
            }
            else
            {
                Snippet.Ast = StParser.ParseBlock(Snippet.Lines, context, sink, comments);
            }
            Snippet.Comments = comments.Count > 0 ? comments : null;
        }
    }

    private sealed record FbdRefItem(DiagnosticLocation Location, FbdElement Element) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
        {
            switch (Element)
            {
                case FbdIRef i when i.Operand is not null:
                    i.ParsedOperand = OperandParser.ClassifyStandalone(i.Operand, context, sink);
                    break;
                case FbdORef o when o.Operand is not null:
                    o.ParsedOperand = OperandParser.ClassifyStandalone(o.Operand, context, sink);
                    break;
            }
        }
    }

    public static void Run(L5xProject project, LogicParseContext context, ParseOptions options, DiagnosticBag diagnostics)
    {
        var items = new List<WorkItem>();
        foreach (var program in project.Controller.Programs) Collect(program, items);
        foreach (var aoi in project.Controller.AddOnInstructions) Collect(aoi, items);
        if (items.Count == 0) return;

        var results = new List<ParseDiagnostic>?[items.Count];
        var rungParser = new RungParser(context);
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.EffectiveThreads };
        Parallel.For(0, items.Count, parallel, i =>
        {
            var item = items[i];
            var bag = new DiagnosticBag();
            var sink = new ScopedDiagnosticSink(bag, item.Location);
            try
            {
                item.Run(context, rungParser, sink);
            }
            catch (Exception ex)
            {
                sink.Error("LOG999", $"Internal parser failure: {ex.GetType().Name}: {ex.Message}");
            }
            if (bag.Count > 0) results[i] = bag.ToList();
        });

        foreach (var list in results)
        {
            if (list is not null) diagnostics.AddRange(list);
        }
    }

    private static void Collect(IRoutineContainer container, List<WorkItem> items)
    {
        foreach (var routine in container.Routines)
        {
            var baseLoc = new DiagnosticLocation(Container: container.Name, Routine: routine.Name);
            if (routine.Content is not null) Collect(routine.Content, baseLoc, items);
            foreach (var extra in routine.OnlineEditContents) Collect(extra, baseLoc, items);
        }
    }

    private static void Collect(RoutineContent content, DiagnosticLocation baseLoc, List<WorkItem> items)
    {
        switch (content)
        {
            case RllContent rll:
                foreach (var rung in rll.Rungs)
                    items.Add(new RungItem(baseLoc with { Rung = rung.Number, XmlLine = rung.SourceLine }, rung));
                break;
            case StContent st:
                items.Add(new StItem(baseLoc with { XmlLine = st.SourceLine }, st));
                break;
            case SfcContent sfc:
                foreach (var node in sfc.Nodes)
                {
                    switch (node)
                    {
                        case SfcStep step:
                        {
                            var loc = baseLoc with { XmlLine = step.SourceLine, Tag = step.Operand };
                            if (step.Preset is not null) items.Add(new SnippetItem(loc, step.Preset, IsExpression: true));
                            if (step.LimitHigh is not null) items.Add(new SnippetItem(loc, step.LimitHigh, IsExpression: true));
                            if (step.LimitLow is not null) items.Add(new SnippetItem(loc, step.LimitLow, IsExpression: true));
                            foreach (var action in step.Actions)
                            {
                                var aloc = baseLoc with { XmlLine = action.SourceLine, Tag = action.Operand };
                                if (action.Preset is not null) items.Add(new SnippetItem(aloc, action.Preset, IsExpression: true));
                                if (action.Body is not null) items.Add(new SnippetItem(aloc, action.Body, IsExpression: false));
                            }
                            break;
                        }
                        case SfcTransition t when t.Condition is not null:
                            items.Add(new SnippetItem(baseLoc with { XmlLine = t.SourceLine, Tag = t.Operand }, t.Condition, IsExpression: true));
                            break;
                    }
                }
                break;
            case FbdContent fbd:
                foreach (var sheet in fbd.Sheets)
                    foreach (var el in sheet.Elements)
                        if (el is FbdIRef or FbdORef) items.Add(new FbdRefItem(baseLoc with { XmlLine = el.SourceLine }, el));
                break;
        }
    }
}
