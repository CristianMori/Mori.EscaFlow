using Plc.Ir.Catalogs;
using Xunit;

namespace Plc.Ir.Tests;

public class CatalogTests
{
    [Fact]
    public void StandardFunctionBlocksLoad()
    {
        Assert.Equal(11, StandardFunctionBlocks.All.Count);
        var ton = StandardFunctionBlocks.Get("ton")!;
        Assert.Equal("TIME", ton.PinType("PT"));
        Assert.Equal("BOOL", ton.PinType("Q"));
        Assert.True(ton.HasPin("ET"));
        Assert.False(ton.HasPin("PRE"));
        Assert.True(StandardFunctionBlocks.IsStandard("CTUD"));
        Assert.False(StandardFunctionBlocks.IsStandard("PIDE"));
        Assert.Contains("iec", StandardFunctionBlocks.Profiles.Keys);
        Assert.Contains("\"TON\"", StandardFunctionBlocks.CatalogJson());
    }

    [Fact]
    public void LogixProfileMapsPinsOntoTimerStructure()
    {
        var logix = StandardFunctionBlocks.GetProfile("logix")!;
        Assert.Equal("logix", logix.Name);
        Assert.Equal("TIMER", logix.InstanceTypes["TON"]);
        Assert.Equal("COUNTER", logix.InstanceTypes["CTU"]);
        var pt = logix.Pin("TON", "PT")!;
        Assert.Equal("PRE", pt.Member);
        Assert.Equal("DINT", pt.Type);
        Assert.Equal("ms", pt.Unit);
        Assert.Equal("enable", logix.Pin("TON", "IN")!.Role);
        Assert.Equal("DN", logix.Pin("CTU", "Q")!.Member);
        Assert.Equal("DINT", logix.PinType(StandardFunctionBlocks.Get("TON")!, "ET"));
        Assert.Equal("BOOL", logix.PinType(StandardFunctionBlocks.Get("TON")!, "Q"));
        Assert.Contains("TT", logix.ExtraPins("TON"));
        Assert.Empty(StandardFunctionBlocks.GetProfile("iec")!.ExtraPins("TON"));
    }

    [Fact]
    public void AtomicTypesResolveAliasesAndPromote()
    {
        Assert.Equal("TOD", AtomicTypes.Get("TIME_OF_DAY")!.Name);
        Assert.Equal("DT", AtomicTypes.Get("date_and_time")!.Name);
        Assert.Equal(25, AtomicTypes.All.Count);
        Assert.True(AtomicTypes.Get("DINT")!.SupportsBits);
        Assert.True(AtomicTypes.Get("WORD")!.SupportsBits);
        Assert.False(AtomicTypes.Get("REAL")!.SupportsBits);
        Assert.False(AtomicTypes.IsAtomic("Motor"));
        Assert.True(AtomicTypes.IsAtomic(IrTypeRef.Named("lreal")));
        Assert.Equal("REAL", AtomicTypes.Wider("DINT", "REAL"));
        Assert.Equal("DINT", AtomicTypes.Wider("SINT", "DINT"));
        Assert.Equal("LINT", AtomicTypes.IntegerTypeFor(1L << 40));
    }

    [Fact]
    public void StandardFunctionsTypeConversionsAndResults()
    {
        var conv = StandardFunctions.Get("DINT_TO_REAL")!;
        Assert.Equal("conversion", conv.Group);
        Assert.Equal("REAL", conv.ResultType(new[] { "DINT" }));
        Assert.Equal("STRING", StandardFunctions.Get("CONCAT")!.ResultType(Array.Empty<string?>()));
        Assert.Equal("LREAL", StandardFunctions.Get("SQRT")!.ResultType(new[] { "LREAL" }));
        Assert.Equal("REAL", StandardFunctions.Get("ABS")!.ResultType(new[] { "REAL" }));
        Assert.Equal("BOOL", StandardFunctions.Get("GT")!.ResultType(new[] { "DINT", "DINT" }));
        Assert.Equal("DINT", StandardFunctions.Get("MAX")!.ResultType(new[] { "INT", "DINT" }));
        Assert.Null(StandardFunctions.Get("Motor_TO_Foo"));
        Assert.True(StandardFunctions.IsStandard("string_to_int"));
    }
}
