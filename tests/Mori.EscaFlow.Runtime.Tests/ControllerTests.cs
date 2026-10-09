using Xunit;

namespace Mori.EscaFlow.Runtime.Tests;

/// <summary>End-to-end controller behaviour with <see cref="TestController"/>: prescan-then-scan on Start, freeze on Stop, host tag access, and GSV/EVENT services.</summary>
public class ControllerTests
{
    [Fact]
    public async Task StartRunsPrescanThenScansAndStopHalts()
    {
        await using var c = new TestController();
        Assert.Equal(ControllerState.Stopped, c.State);
        c.Scans.Value = 100;
        c.Start();
        Assert.Equal(ControllerState.Running, c.State);
        Assert.Equal(ControllerMode.Run, c.Mode);
        Assert.Equal(1, c.PrescanCount.Value);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (c.Scans.Value < 10 && DateTime.UtcNow < deadline) await Task.Delay(5);
        Assert.True(c.Scans.Value >= 10);
        c.Stop();
        Assert.Equal(ControllerState.Stopped, c.State);
        Assert.Equal(ControllerMode.Program, c.Mode);
        var frozen = c.Scans.Value;
        await Task.Delay(30);
        Assert.Equal(frozen, c.Scans.Value);
        Assert.Equal(1, c.P.FirstScanTrueCount);
        Assert.True(c.SeenFirstScan.Value);
        var stats = Assert.Single(c.Statistics.Tasks);
        Assert.Equal("Main", stats.Name);
        Assert.True(stats.Scans >= 10);
        Assert.True(stats.LastScanMs >= 0);
        Assert.True(c.Sys.ScanCount >= 10);
        c.Start();
        Assert.Equal(2, c.PrescanCount.Value);
        c.Stop();
        Assert.Equal(2, c.P.FirstScanTrueCount);
    }

    [Fact]
    public async Task HostApiReadsAndWritesTags()
    {
        await using var c = new TestController();
        Assert.True(c.TryWrite("Scans", "5", out var error), error);
        Assert.True(c.TryRead("Scans", out var v));
        Assert.Equal("5", v!.Text);
        Assert.Contains(c.Tags(), t => t.Path == "Scans");
        using var ms = new MemoryStream();
        c.SaveSnapshot(ms);
        c.Scans.Value = 0;
        ms.Position = 0;
        c.LoadSnapshot(ms);
        Assert.Equal(5, c.Scans.Value);
    }

    [Fact]
    public async Task ServicesAnswerGsvAndTriggerEvents()
    {
        await using var c = new TestController();
        var dest = new WrapSTRING(c.Store.Define("Name", WrapSTRING.Layout, Array.Empty<int>()), 0);
        c.Services.GSV("Controller", null, "Name", dest);
        Assert.Equal("Test", dest.Value);
        var year = new PlcArray<WrapDINT>(c.Store.Define("Clock", EthernetIPSharp.Logix.LogixDataTypes.DINT, new[] { 7 }), 0, 4, new[] { 7 }, static (t, o) => new WrapDINT(t, o));
        c.Services.Call("GSV", "WallClockTime", null, "LocalDateTime", year);
        Assert.Equal(DateTime.Now.Year, year[0].Value);
        c.Services.Call("MAJ", 1, 2); // unsupported services are logged once and skipped unless FaultOnUnsupportedService is set
    }
}
