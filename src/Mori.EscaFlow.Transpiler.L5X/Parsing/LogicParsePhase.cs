using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Fbd;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Sfc;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;
using Mori.EscaFlow.Transpiler.L5X.Parsing.St;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing;

/// <summary>
/// The second parse phase: every rung, structured text body, SFC snippet and FBD reference operand is parsed into
/// its AST. The work items are independent, so they run in parallel; each writes only into its own model node and
/// into a private diagnostic list, and the lists are merged back in file order so diagnostics stay deterministic.
/// </summary>
/// <remarks>
/// The logic text is parsed after the XML phase rather than during it because the rung and ST grammars need to know
/// which identifiers name add-on instructions (<c>Blink(Blk, 100)</c> is a call, <c>LIM(0, X, 10)</c> a built-in),
/// and the add-on definitions precede the programs in the file but can be referenced by them in any order.
/// </remarks>
public static class LogicParsePhase
{
    /// <summary>One unit of parallel work; <see cref="Location"/> stamps every diagnostic the item raises.</summary>
    private abstract record WorkItem(DiagnosticLocation Location)
    {
        public abstract void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink);
    }

    /// <summary>Parses one rung's neutral text.</summary>
    private sealed record RungItem(DiagnosticLocation Location, Rung Rung) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
            => Rung.Ast = rungParser.Parse(Rung.Text, sink);
    }

    /// <summary>Parses a whole structured text routine (all of its lines as one block, so statements may span lines).</summary>
    private sealed record StItem(DiagnosticLocation Location, StContent Content) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
        {
            var comments = new List<StComment>();
            Content.Ast = StParser.ParseBlock(Content.Lines, context, sink, comments);
            Content.Comments = comments.Count > 0 ? comments : null;
        }
    }

    /// <summary>
    /// Parses structured text embedded in an SFC: action bodies are statement blocks, while step presets, limits and
    /// transition conditions are single expressions (<paramref name="IsExpression"/>).
    /// </summary>
    private sealed record SnippetItem(DiagnosticLocation Location, StSnippet Snippet, bool IsExpression) : WorkItem(Location)
    {
        public override void Run(LogicParseContext context, RungParser rungParser, IDiagnosticSink sink)
        {
            var comments = new List<StComment>();
            if (IsExpression)
            {
                // An empty transition condition is legal in the export (the designer left it blank); there is nothing to parse.
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

    /// <summary>Classifies the operand text of FBD input/output references (a tag path or a literal) with the ladder operand rules.</summary>
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

    /// <summary>Collects every work item of the project, runs them in parallel and merges the diagnostics in file order.</summary>
    public static void Run(L5xProject project, LogicParseContext context, ParseOptions options, DiagnosticBag diagnostics)
    {
        var items = new List<WorkItem>();
        foreach (var program in project.Controller.Programs) Collect(program, items);
        foreach (var aoi in project.Controller.AddOnInstructions) Collect(aoi, items);
        if (items.Count == 0) return;

        // One slot per item keeps the merge order independent of the scheduling order.
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
                // A parser bug on one rung must not abort the whole project: record it where the rung is and go on.
                sink.Error("LOG999", $"Internal parser failure: {ex.GetType().Name}: {ex.Message}");
            }
            if (bag.Count > 0) results[i] = bag.ToList();
        });

        foreach (var list in results)
        {
            if (list is not null) diagnostics.AddRange(list);
        }
    }

    /// <summary>Collects the work items of every routine of a program or add-on definition, including pending and test online edits.</summary>
    private static void Collect(IRoutineContainer container, List<WorkItem> items)
    {
        foreach (var routine in container.Routines)
        {
            var baseLoc = new DiagnosticLocation(Container: container.Name, Routine: routine.Name);
            if (routine.Content is not null) Collect(routine.Content, baseLoc, items);
            foreach (var extra in routine.OnlineEditContents) Collect(extra, baseLoc, items);
        }
    }

    /// <summary>Collects the work items of one routine body according to its language.</summary>
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
                // Steps carry three optional expressions (preset, limits) and their actions carry a preset and a body;
                // transitions carry one condition. Each snippet is its own item so an error is located at its element.
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
