using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;
using Xunit;
using Xunit.Abstractions;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Integration;

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

    public static IEnumerable<object[]> Expectations => new[]
    {
        new object[] { SampleFileFixture.Aib102, 615, 102, 10382, 38, 675, 461, 160, 54, 14706 },
        new object[] { SampleFileFixture.Stlib106, 110, 88, 6476, 18, 504, 409, 95, 0, 8362 },
        new object[] { SampleFileFixture.Stl107, 159, 74, 9318, 22, 647, 556, 91, 0, 13048 },
    };

    private L5xProject Load(string name)
    {
        if (!_fx.Has(name))
        {
            _out.WriteLine($"SKIPPED: sample {name} not found");
            return null!;
        }
        return _fx.Get(name);
    }

    [Theory]
    [MemberData(nameof(Expectations))]
    public void CountsMatchTheSurvey(string file, int dataTypes, int aois, int tags, int programs, int routines, int rll, int st, int sfc, int rungs)
    {
        var p = Load(file);
        if (p is null) return;
        var s = p.Statistics;
        Assert.Equal(dataTypes, s.DataTypes);
        Assert.Equal(aois, s.AddOnInstructions);
        Assert.Equal(tags, s.ControllerTags + s.ProgramTags);
        Assert.Equal(programs, s.Programs);
        Assert.Equal(routines, s.Routines);
        Assert.Equal(rll, s.RllRoutines);
        Assert.Equal(st, s.StRoutines);
        Assert.Equal(sfc, s.SfcRoutines);
        Assert.Equal(rungs, s.Rungs);
        Assert.Equal(0, s.FbdRoutines);
        _out.WriteLine($"{file}: xml {s.XmlPhase.TotalSeconds:F2}s logic {s.LogicPhase.TotalSeconds:F2}s link {s.LinkPhase.TotalSeconds:F2}s total {s.Total.TotalSeconds:F2}s, {s.ValueNodes} value nodes");
    }

    [Theory]
    [InlineData(SampleFileFixture.Aib102)]
    [InlineData(SampleFileFixture.Stlib106)]
    [InlineData(SampleFileFixture.Stl107)]
    public void NoLogicDiagnostics(string file)
    {
        var p = Load(file);
        if (p is null) return;
        var bad = p.Diagnostics.Where(d => d.Code.StartsWith("RLL") || d.Code.StartsWith("ST") || d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (var d in bad.Take(20)) _out.WriteLine(d.ToString());
        Assert.Empty(bad);
        // L5K vs Decorated cross-check
        Assert.Empty(p.Diagnostics.Where(d => d.Code == "LNK006"));
        // Warnings of any kind
        foreach (var d in p.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).Take(20)) _out.WriteLine(d.ToString());
        Assert.Empty(p.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning));
    }

    [Theory]
    [InlineData(SampleFileFixture.Aib102)]
    [InlineData(SampleFileFixture.Stlib106)]
    [InlineData(SampleFileFixture.Stl107)]
    public void EveryRungRoundTrips(string file)
    {
        var p = Load(file);
        if (p is null) return;
        var failures = 0;
        foreach (var routine in p.Controller.AllRoutines)
        {
            if (routine.Content is not RllContent rll) continue;
            foreach (var rung in rll.Rungs)
            {
                Assert.NotNull(rung.Ast);
                var written = RungTextWriter.Normalize(RungTextWriter.Write(rung.Ast!));
                var original = RungTextWriter.Normalize(rung.Text);
                if (written != original)
                {
                    if (failures++ < 10) _out.WriteLine($"{routine.Owner?.Name}/{routine.Name} rung {rung.Number}:\n  {original}\n  {written}");
                }
            }
        }
        Assert.Equal(0, failures);
    }

    [Theory]
    [InlineData(SampleFileFixture.Aib102)]
    [InlineData(SampleFileFixture.Stlib106)]
    [InlineData(SampleFileFixture.Stl107)]
    public void EverythingResolves(string file)
    {
        var p = Load(file);
        if (p is null) return;
        var c = p.Controller;
        Assert.All(c.AllTags.Where(t => t.DataType is not null), t => Assert.NotNull(t.DataType!.Definition));
        Assert.All(c.AllTags.Where(t => t.TagType == TagType.Alias), t => Assert.NotNull(t.AliasPath));
        Assert.All(c.Programs.Where(pr => pr.MainRoutineName is not null), pr => Assert.NotNull(pr.MainRoutine));
        Assert.All(c.Tasks, t => Assert.Equal(t.ScheduledProgramNames.Count, t.ScheduledPrograms.Count));
        Assert.All(c.AllTags.Where(t => t.DataType is not null && t.TagType != TagType.Alias && t.Data.L5k is not null), t => Assert.NotNull(t.Data.Value));
        foreach (var routine in c.AllRoutines)
        {
            switch (routine.Content)
            {
                case StContent st:
                    Assert.NotNull(st.Ast);
                    Assert.DoesNotContain(st.Ast!.Statements, s => s is StErrorStatement);
                    break;
                case Model.Logic.Sfc.SfcContent sfc:
                    Assert.All(sfc.Links, l => Assert.True(l.From is not null && l.To is not null));
                    Assert.NotNull(sfc.InitialStep);
                    foreach (var step in sfc.Steps)
                        foreach (var a in step.Actions)
                            if (a.Body is not null && a.Body.Lines.Count > 0) Assert.NotNull(a.Body.Ast);
                    foreach (var t in sfc.Transitions)
                        if (t.Condition is not null && t.Condition.Lines.Count > 0) Assert.NotNull(t.Condition.Expression);
                    break;
            }
        }
    }

    [Fact]
    public void SpotChecksOnAib102()
    {
        var p = Load(SampleFileFixture.Aib102);
        if (p is null) return;
        var c = p.Controller;

        var dateTime = c.DataTypes["DateTime"];
        Assert.NotNull(dateTime);
        Assert.Contains(dateTime!.Members, m => m.Name == "Year" && m.DataType.Name == "DINT");

        var alarm = c.Tags["AccessAreaQueueFullWarning"];
        Assert.NotNull(alarm);
        var sv = Assert.IsType<Model.Values.StructValue>(alarm!.Data.Decorated);
        Assert.Equal("SymAlarm", sv.DataType);
        var id = Assert.IsType<Model.Values.ScalarValue>(sv["ID"]);
        Assert.Equal(3918, id.Bits);

        var program = c.FindProgram("Access_Areas");
        Assert.NotNull(program);
        Assert.NotNull(program!.MainRoutine);
        Assert.Equal(program, c.Tasks[0].ScheduledPrograms.First(x => x.Name == "Access_Areas"));

        var sfcRoutine = c.AllRoutines.First(r => r.Type == RoutineType.SFC);
        var sfc = Assert.IsType<Model.Logic.Sfc.SfcContent>(sfcRoutine.Content);
        Assert.NotEmpty(sfc.Steps);
        Assert.NotEmpty(sfc.Links);
        var actionWithBody = sfc.Steps.SelectMany(s => s.Actions).First(a => a.Body is { Lines.Count: > 0 });
        Assert.NotNull(actionWithBody.Body!.Ast);

        var aoi = c.FindAoi("ADD_ON_ANALOG_INPUT_SCALING");
        Assert.NotNull(aoi);
        Assert.NotNull(aoi!.Logic);
        Assert.Contains(aoi.Parameters, x => x.Name == "AOInRawAnalogValue" && x.ParameterUsage == Model.Aoi.AoiParameterUsage.Input);
        Assert.NotNull(aoi.BackingType);

        var aoiCalls = c.AllRoutines.Where(r => r.Content is RllContent).SelectMany(r => ((RllContent)r.Content!).Rungs).Where(r => r.Ast is not null).SelectMany(r => r.Ast!.AllInstructions()).Count(i => i.IsAddOn);
        Assert.True(aoiCalls > 100, $"expected many AOI calls, got {aoiCalls}");

        var jsr = c.AllRoutines.First(r => r.CalledRoutines.Count > 0);
        Assert.Contains(jsr, jsr.CalledRoutines[0].Callers);
    }

    [Fact]
    public void PerformanceIsReasonable()
    {
        var p = Load(SampleFileFixture.Aib102);
        if (p is null) return;
        _out.WriteLine($"Total {p.Statistics.Total.TotalSeconds:F2}s, peak working set {p.Statistics.PeakWorkingSetBytes / 1048576} MB");
        Assert.True(p.Statistics.Total < TimeSpan.FromSeconds(60), $"parse took {p.Statistics.Total}");
    }
}
