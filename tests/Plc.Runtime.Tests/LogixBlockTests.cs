using EthernetIPSharp.Logix;
using Xunit;

namespace Plc.Runtime.Tests;

[Collection("Clock")]
public class LogixBlockTests
{
    private static Tag Bytes(int size) => new(1, "T", LogixDataTypes.SINT, LogixDataTypes.SINT, 1, size);

    [Fact]
    public void TonTimesAndResets()
    {
        var clock = new ManualClock();
        Logix.Clock = clock;
        var t = new TIMER(Bytes(12), 0);
        t.PRE.Value = 100;

        Logix.TON(t, false);
        Assert.False(t.EN.Value);
        Logix.TON(t, true);
        Assert.True(t.EN.Value);
        Assert.True(t.TT.Value);
        Assert.False(t.DN.Value);
        Assert.Equal(0, t.ACC.Value);
        clock.Advance(40);
        Logix.TON(t, true);
        Assert.Equal(40, t.ACC.Value);
        Assert.True(t.TT.Value);
        clock.Advance(70);
        Logix.TON(t, true);
        Assert.Equal(100, t.ACC.Value);
        Assert.True(t.DN.Value);
        Assert.False(t.TT.Value);
        clock.Advance(50);
        Logix.TON(t, true);
        Assert.Equal(100, t.ACC.Value);
        Logix.TON(t, false);
        Assert.False(t.DN.Value);
        Assert.False(t.EN.Value);
        Assert.Equal(0, t.ACC.Value);
    }

    [Fact]
    public void TofHoldsDnAfterRungDrops()
    {
        var clock = new ManualClock();
        Logix.Clock = clock;
        var t = new TIMER(Bytes(12), 0);
        t.PRE.Value = 50;
        Logix.TOF(t, true);
        Assert.True(t.DN.Value);
        Assert.True(t.EN.Value);
        Logix.TOF(t, false);
        Assert.True(t.DN.Value);
        Assert.False(t.EN.Value);
        clock.Advance(30);
        Logix.TOF(t, false);
        Assert.True(t.DN.Value);
        Assert.True(t.TT.Value);
        Assert.Equal(30, t.ACC.Value);
        clock.Advance(30);
        Logix.TOF(t, false);
        Assert.False(t.DN.Value);
        Assert.False(t.TT.Value);
        Assert.Equal(50, t.ACC.Value);
    }

    [Fact]
    public void TonrRetainsAndResets()
    {
        var clock = new ManualClock();
        Logix.Clock = clock;
        var t = new TIMER(Bytes(12), 0);
        t.PRE.Value = 100;
        Logix.TONR(t, true);
        clock.Advance(30);
        Logix.TONR(t, true);
        Assert.Equal(30, t.ACC.Value);
        Logix.TONR(t, false);
        clock.Advance(500);
        Logix.TONR(t, false);
        Assert.Equal(30, t.ACC.Value);
        Logix.TONR(t, true);
        clock.Advance(80);
        Logix.TONR(t, true);
        Assert.True(t.DN.Value);
        Assert.Equal(100, t.ACC.Value);
        Logix.ResetTimer(t);
        Assert.Equal(0, t.ACC.Value);
        Assert.False(t.DN.Value);
    }

    [Fact]
    public void CountersCountEdges()
    {
        var c = new COUNTER(Bytes(12), 0);
        c.PRE.Value = 2;
        Logix.CTU(c, true);
        Logix.CTU(c, true);
        Assert.Equal(1, c.ACC.Value);
        Assert.False(c.DN.Value);
        Logix.CTU(c, false);
        Logix.CTU(c, true);
        Assert.Equal(2, c.ACC.Value);
        Assert.True(c.DN.Value);
        Logix.CTD(c, false);
        Logix.CTD(c, true);
        Assert.Equal(1, c.ACC.Value);
        Assert.False(c.DN.Value);
        c.ACC.Value = int.MaxValue;
        Logix.CTU(c, false);
        Logix.CTU(c, true);
        Assert.True(c.OV.Value);
        Assert.Equal(int.MinValue, c.ACC.Value);
        Logix.PrescanCTU(c);
        Assert.True(c.CU.Value);
    }
}

[CollectionDefinition("Clock")]
public sealed class ClockCollection : ICollectionFixture<object>
{
}
