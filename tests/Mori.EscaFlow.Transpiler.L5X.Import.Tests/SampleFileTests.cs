using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Import;
using Mori.EscaFlow.Transpiler.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Mori.EscaFlow.Transpiler.L5X.Import.Tests;

/// <summary>
/// End-to-end conformance check against real exported projects (skipped when the sample files are not present):
/// builds the plc-ir document, requires every reference to resolve or be explicitly opaque, requires every rung
/// that has an output to produce at least one statement, validates the document, then round-trips it through JSON
/// and checks the two documents agree on variable and type counts.
/// </summary>
[Collection("Samples")]
[Trait("Category", "Integration")]
public class SampleFileTests
{
    private readonly SampleFileFixture _fx;
    private readonly ITestOutputHelper _out;

    public SampleFileTests(SampleFileFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Theory]
    [InlineData(SampleFileFixture.Stlib106)]
    [InlineData(SampleFileFixture.Aib102)]
    [InlineData(SampleFileFixture.Stl107)]
    public void BuildsValidatesAndRoundTrips(string file)
    {
        if (!_fx.Has(file)) return;
        var project = _fx.Get(file);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ir = new LogixIrBuilder(project, new LogixBuildOptions { IncludeValues = false }).Build();
        var buildTime = sw.Elapsed;

        // Every reference in logic is resolved unless it enters an opaque type; every expression is typed unless opaque.
        int refs = 0, unresolved = 0, opaque = 0, untyped = 0;
        var samples = new List<string>();
        foreach (var (e, ctx) in IrWalker.Expressions(ir))
        {
            switch (e)
            {
                case IrRef r:
                    refs++;
                    if (r.Opaque) opaque++;
                    else if (r.Root.Kind == "unresolved" || r.Error is not null)
                    {
                        unresolved++;
                        if (samples.Count < 10) samples.Add($"{ctx.Location}: {r.Root} : {r.Error}");
                    }
                    else if (r.Type is null) untyped++;
                    break;
                case IrKeyword or IrPlaceholder or IrErrorExpression:
                    break;
                default:
                    if (e.Type is null && !ContainsOpaque(e))
                    {
                        untyped++;
                        if (samples.Count < 20) samples.Add($"untyped {ctx.Location}: {new Printing.StPrinter(ir).Print(e)}");
                    }
                    break;
            }
        }

        var rungs = 0;
        var emptyRungs = 0;
        foreach (var pou in ir.Pous)
            foreach (var action in pou.Actions.Concat(pou.Body is null ? Array.Empty<IrAction>() : new[] { new IrAction { Body = pou.Body } }))
            {
                if (action.Body.Diagram is not IrLdDiagram ld) continue;
                var byRung = action.Body.Statements.Where(s => s.Src?.Rung is not null).GroupBy(s => s.Src!.Rung!.Value).ToDictionary(g => g.Key, g => g.Count());
                foreach (var rung in ld.Rungs)
                {
                    rungs++;
                    if (!byRung.ContainsKey(rung.Number) && HasOutput(rung.Elements)) emptyRungs++;
                }
            }

        sw.Restart();
        var validation = new IrValidator().Validate(ir, new ValidationOptions { ServiceCatalogs = { LogixServiceCatalog.Instance } });
        var validateTime = sw.Elapsed;
        foreach (var group in validation.Errors.GroupBy(i => i.Code))
            foreach (var issue in group.Take(6)) _out.WriteLine("  " + issue);
        var errorCodes = validation.Errors.GroupBy(i => i.Code).Select(g => $"{g.Key}x{g.Count()}").ToList();

        var path = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(file) + ".ir.json");
        sw.Restart();
        IrJson.WriteFile(ir, path);
        var writeTime = sw.Elapsed;
        var size = new FileInfo(path).Length;
        sw.Restart();
        var back = IrJson.ReadFile(path);
        var readTime = sw.Elapsed;
        File.Delete(path);

        _out.WriteLine($"{file}: build {buildTime.TotalSeconds:F1}s, validate {validateTime.TotalSeconds:F1}s, write {writeTime.TotalSeconds:F1}s ({size / 1048576.0:F1} MB), read {readTime.TotalSeconds:F1}s; {ir.Types.Count} types, {ir.AllVariables().Count()} variables, {ir.Pous.Count} pous, {rungs} rungs, {refs} refs ({opaque} opaque, {unresolved} unresolved, {untyped} untyped); validation: {validation.Errors.Count()} errors [{string.Join(", ", errorCodes)}], {validation.Warnings.Count()} warnings");
        foreach (var s in samples) _out.WriteLine("  " + s);

        Assert.Equal(0, unresolved);
        Assert.Equal(0, untyped);
        Assert.Equal(0, emptyRungs);
        Assert.Equal(project.Statistics.Rungs, rungs);
        Assert.False(validation.HasErrors, string.Join("\n", validation.Errors.Take(20)));
        Assert.Equal(ir.AllVariables().Count(), back.AllVariables().Count());
        Assert.Equal(ir.Types.Count, back.Types.Count);
        var missingTypes = ir.AllVariables().Where(v => v.Type.LeafName is { } n && !Catalogs.AtomicTypes.IsAtomic(n) && ir.FindType(n) is null && ir.FindPou(n) is null).Select(v => v.Id + " : " + v.Type).ToList();
        Assert.Empty(missingTypes);
    }

    private static bool ContainsOpaque(IrExpression e)
    {
        var found = false;
        var walker = new OpaqueFinder(() => found = true);
        walker.VisitExpression(e, new IrWalkContext());
        return found;
    }

    private sealed class OpaqueFinder : IrWalker
    {
        private readonly Action _hit;
        public OpaqueFinder(Action hit) => _hit = hit;
        public override void VisitExpression(IrExpression e, IrWalkContext ctx)
        {
            if (e is IrRef { Opaque: true } or IrRef { Type: null, Root.Kind: "unresolved" } or IrErrorExpression or IrPlaceholder or IrSysFn) _hit();
            base.VisitExpression(e, ctx);
        }
    }

    private static bool HasOutput(IEnumerable<IrLdElement> elements) => elements.Any(e => e switch
    {
        IrLdContact => false,
        IrLdBlock { Enable: "none" } => false,
        IrLdBranch b => b.Legs.Any(HasOutput),
        _ => true,
    });
}
