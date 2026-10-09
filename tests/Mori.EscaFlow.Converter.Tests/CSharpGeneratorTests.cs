using Mori.EscaFlow.Transpiler.Logix;
using System.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Converter.CSharp;
using Mori.EscaFlow.Converter.Layout;
using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.L5X.Import;
using Xunit;
using Xunit.Abstractions;

namespace Mori.EscaFlow.Converter.Tests;

/// <summary>
/// Checks the C# generator against the conformance fixtures: structure layouts follow the documented Logix packing
/// rules, every fixture generates a solution report with no errors, specific ladder constructs lower to the
/// expected C# shapes, and (for the fixtures worth the cost) the generated solution actually compiles against the
/// real runtime project.
/// </summary>
public class CSharpGeneratorTests
{
    private readonly ITestOutputHelper _out;

    public CSharpGeneratorTests(ITestOutputHelper output) => _out = output;

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Mori.EscaFlow.slnx"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static IrDocument BuildFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Conformance", "fixtures", "logix", name + ".L5X");
        var project = L5xParser.ParseFile(path);
        return new LogixIrBuilder(project).Build();
    }

    [Fact]
    public void LayoutFollowsLogixRules()
    {
        var ir = BuildFixture("types");
        var layouts = new LogixLayoutCalculator(ir);
        var timer = layouts.Get("TIMER")!;
        Assert.Equal(12, timer.Size);
        Assert.Equal(4, timer.Find("PRE")!.Offset);
        Assert.Equal(8, timer.Find("ACC")!.Offset);
        var en = timer.Find("EN")!;
        Assert.Equal(3, en.Offset);
        Assert.Equal(7, en.Bit);
        var str = layouts.Get("STRING")!;
        Assert.Equal(88, str.Size);
        Assert.Equal(4, str.Find("DATA")!.Offset);
        foreach (var l in layouts.All())
        {
            Assert.True(l.Size % l.Alignment == 0, l.Name);
            foreach (var m in l.Members)
            {
                Assert.True(m.Offset + m.ElementSize * Math.Max(1, m.Count) <= l.Size || m.PackedBools, $"{l.Name}.{m.Name} overflows");
                if (!m.IsStruct && m.TypeName != "BOOL") Assert.True(m.Offset % Math.Min(m.ElementSize, 8) == 0, $"{l.Name}.{m.Name} misaligned");
            }
        }
    }

    [Theory]
    [InlineData("types")]
    [InlineData("ladder")]
    [InlineData("st")]
    [InlineData("aoi")]
    [InlineData("modules")]
    [InlineData("tasks")]
    [InlineData("messages")]
    [InlineData("fbd")]
    [InlineData("sfc")]
    public void GeneratesSolutionWithoutErrors(string fixture)
    {
        var ir = BuildFixture(fixture);
        var result = new CSharpGenerator(new CSharpGeneratorOptions { Name = "Fx" + fixture }).Generate(ir);
        Assert.False(result.Report.HasErrors, string.Join("\n", result.Report.Messages.Where(m => m.Severity == "error").Select(m => m.Message)));
        Assert.Contains(result.Files, f => f.Path.EndsWith(".slnx"));
        Assert.Contains(result.Files, f => f.Path.EndsWith("Controller.cs"));
        Assert.Contains(result.Files, f => f.Path == "generation-report.json");
        foreach (var pou in ir.Pous.Where(p => p.Kind == "program"))
            Assert.Contains(result.Files, f => f.Path.Contains("/Programs/") && f.Content.Contains($"class ") && f.Content.Contains(pou.Name));
    }

    [Fact]
    public void LadderLoweringIsEmittedAsCSharp()
    {
        var ir = BuildFixture("ladder");
        var result = new CSharpGenerator(new CSharpGeneratorOptions { Name = "Ladder" }).Generate(ir);
        var program = result.Files.Single(f => f.Path.EndsWith("/Programs/P.cs")).Content;
        Assert.Contains("Logix.TON(C.T1, C.A.Value);", program);
        Assert.Contains("C.Out1.Value = _rc1;", program);
        Assert.Contains("PlcLogic.RisingEdge(", program);
        Assert.Contains("goto L_", program);
        Assert.Contains("PlcMem.Copy(", program);
        Assert.Contains(".Execute(", program);
        var report = result.Report;
        Assert.True(report.StandardBlockInventory["TON"] >= 1);
        Assert.Contains("GSV", report.ServiceInventory.Keys);
    }

    [Theory]
    [InlineData("ladder")]
    [InlineData("st")]
    [InlineData("aoi")]
    [InlineData("types")]
    [InlineData("modules")]
    [Trait("Category", "Integration")]
    public async Task GeneratedSolutionCompiles(string fixture)
    {
        var root = RepoRoot();
        var ir = BuildFixture(fixture);
        var outDir = Path.Combine(Path.GetTempPath(), "plc-gen-tests", fixture);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        var runtime = Path.Combine(root, "src", "Mori.EscaFlow.Runtime.CSharp", "Mori.EscaFlow.Runtime.CSharp.csproj");
        var host = Path.Combine(root, "src", "Mori.EscaFlow.Runtime.Host", "Mori.EscaFlow.Runtime.Host.csproj");
        var result = new CSharpGenerator(new CSharpGeneratorOptions { Name = "Fx" + fixture, RuntimeProject = runtime, HostProject = host }).Generate(ir);
        result.WriteTo(outDir);

        var psi = new ProcessStartInfo("dotnet", $"build \"{Path.Combine(outDir, "Fx" + fixture + ".slnx")}\" -nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = outDir,
            // The nested build must never block on finding or starting a persistent MSBuild/Roslyn server node:
            // an ephemeral, one-shot build like this should not create one, and should not wait for one left
            // behind by a previous, unrelated build on the machine.
            EnvironmentVariables = { ["MSBUILDDISABLENODEREUSE"] = "1" },
        };
        using var p = Process.Start(psi)!;

        // Both streams are redirected, so both must be drained concurrently with the process running, not read to
        // completion one after the other: a child process that writes enough to stderr to fill its pipe buffer
        // while nothing is reading it (because the parent is still blocked inside stdout's ReadToEnd) blocks
        // forever on the write, and the parent then blocks forever on the read that would unblock it — the
        // classic Process redirection deadlock. How much a given `dotnet build` writes to stderr (warnings,
        // first-run restore chatter) is not something this test controls, so this is read as two tasks in
        // parallel, each only ever waiting on its own stream.
        var stdOutTask = p.StandardOutput.ReadToEndAsync();
        var stdErrTask = p.StandardError.ReadToEndAsync();
        var exited = p.WaitForExit(TimeSpan.FromMinutes(3));
        if (!exited)
        {
            // A genuine hang (as opposed to a slow but finishing build) must fail the test instead of blocking the
            // rest of the suite indefinitely: killing the process also unblocks the two read tasks above.
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            Assert.Fail($"dotnet build for {fixture} did not finish within 3 minutes; killed the process tree.");
        }
        var output = await Task.WhenAll(stdOutTask, stdErrTask) is [var stdOut, var stdErr] ? stdOut + stdErr : "";
        var errors = output.Split('\n').Where(l => l.Contains(" error ")).Distinct().Take(15).ToList();
        foreach (var e in errors) _out.WriteLine(e);
        Assert.True(p.ExitCode == 0, $"dotnet build failed with {errors.Count} error line(s):\n{string.Join("\n", errors)}");
    }
}
