using EthernetIPSharp.Logix;
using Xunit;

namespace Mori.EscaFlow.Runtime.Tests;

/// <summary>Layout-to-template registration, path resolution and text read/write, and snapshot round-trips, against a real <see cref="TagDatabase"/>.</summary>
public class TagStoreTests
{
    private static TagStore NewStore() => new(new TagDatabase());

    [Fact]
    public void LayoutBecomesTemplateWithExactOffsets()
    {
        var store = NewStore();
        store.RegisterLayout(TIMER.Layout);
        store.RegisterLayout(WrapSTRING.Layout);
        var motor = store.RegisterLayout(Motor.Layout);
        Assert.Equal(108u, motor.StructureSize);
        var timer = motor.Members.Single(m => m.Name == "Timer");
        Assert.Equal(8, timer.Offset);
        Assert.True(LogixDataTypes.IsStruct(timer.DataType));
        Assert.Equal(12, timer.ElementSize);
        var fault = motor.Members.Single(m => m.Name == "Fault");
        Assert.Equal(LogixDataTypes.BOOL, fault.DataType);
        Assert.Equal(4, fault.Offset);
        Assert.Equal(0, fault.ElementSize); // scalar BOOL: bit in ArraySize
        Assert.Same(motor, store.RegisterLayout(Motor.Layout));
        var ex = Assert.Throws<InvalidOperationException>(() => NewStore().RegisterLayout(Motor.Layout));
        Assert.Contains("TIMER", ex.Message);
    }

    [Fact]
    public void DefineReadAndWriteByPath()
    {
        var store = NewStore();
        store.RegisterLayout(TIMER.Layout);
        store.RegisterLayout(WrapSTRING.Layout);
        var word = new WrapDINT(store.Define("Word", LogixDataTypes.DINT, Array.Empty<int>()), 0);
        var m1 = new Motor(store.Define("M1", Motor.Layout, Array.Empty<int>()), 0);
        var matrix = new PlcArray<WrapDINT>(store.Define("A", LogixDataTypes.DINT, new[] { 2, 3 }), 0, 4, new[] { 2, 3 }, static (t, o) => new WrapDINT(t, o));
        var idx = new WrapDINT(store.Define("Program:P.Idx", LogixDataTypes.DINT, Array.Empty<int>()), 0);
        var flags = new PlcBoolArray(store.Define("Flags", LogixDataTypes.BOOL, new[] { 32 }), 0, new[] { 32 });
        var image = new WrapDINT(store.Define("DI:I", LogixDataTypes.DINT, Array.Empty<int>()), 0);

        word.Value = 0b1000;
        m1.Speed.Value = 1.25f;
        m1.Timer.PRE.Value = 500;
        m1.Fault.Value = true;
        m1.Name.Value = "pump";
        matrix[1, 2].Value = 42;
        idx.Value = 7;
        flags[5].Value = true;
        image.Value = 99;

        Assert.True(store.TryRead("Word.3", out var v));
        Assert.Equal("1", v!.Text);
        Assert.Equal("BOOL", v.Type);
        Assert.True(store.TryRead("M1.Speed", out v));
        Assert.Equal("1.25", v!.Text);
        Assert.True(store.TryRead("M1.Timer.PRE", out v));
        Assert.Equal("500", v!.Text);
        Assert.True(store.TryRead("M1.Fault", out v));
        Assert.Equal("1", v!.Text);
        Assert.True(store.TryRead("M1.Name", out v));
        Assert.Equal("pump", v!.Text);
        Assert.True(store.TryRead("A[1,2]", out v));
        Assert.Equal("42", v!.Text);
        Assert.True(store.TryRead("A", out v));
        Assert.Equal("[0, 0, 0, 0, 0, 42]", v!.Text);
        Assert.Equal("DINT[2,3]", v.Type);
        Assert.True(store.TryRead("Program:P.Idx", out v));
        Assert.Equal("7", v!.Text);
        Assert.True(store.TryRead("Flags[5]", out v));
        Assert.Equal("1", v!.Text);
        Assert.True(store.TryRead("DI:I", out v));
        Assert.Equal("99", v!.Text);
        Assert.True(store.TryRead("M1", out v));
        Assert.Contains("Speed: 1.25", v!.Text);
        Assert.Contains("Timer: {", v.Text);
        Assert.False(store.TryRead("Nope", out _));
        Assert.False(store.TryRead("M1.Bogus", out _));

        Assert.True(store.TryWrite("Word.3", "0", out var error), error);
        Assert.Equal(0, word.Value);
        Assert.True(store.TryWrite("M1.Speed", "2.5", out error), error);
        Assert.Equal(2.5f, m1.Speed.Value);
        Assert.True(store.TryWrite("A[0,1]", "-1", out error), error);
        Assert.Equal(-1, matrix[0, 1].Value);
        Assert.True(store.TryWrite("Program:P.Idx", "16#10", out error), error);
        Assert.Equal(16, idx.Value);
        Assert.True(store.TryWrite("M1.Name", "'valve'", out error), error);
        Assert.Equal("valve", m1.Name.Value);
        Assert.True(store.TryWrite("M1.Fault", "false", out error), error);
        Assert.False(m1.Fault.Value);
        Assert.True(store.TryWrite("Flags[6]", "1", out error), error);
        Assert.True(flags[6].Value);
        Assert.False(store.TryWrite("M1", "x", out error));
        Assert.NotNull(error);
        Assert.False(store.TryWrite("M1.Speed", "abc", out error));

        var tags = store.Tags().ToList();
        Assert.Contains(tags, t => t.Path == "Program:P.Idx" && t.Program == "P" && t.Type == "DINT");
        Assert.Contains(tags, t => t.Path == "M1" && t.Type == "Motor");
        Assert.Single(store.Tags("flag"));
    }

    [Fact]
    public void SnapshotRoundTrips()
    {
        var store = NewStore();
        store.RegisterLayout(TIMER.Layout);
        var t = new TIMER(store.Define("T1", TIMER.Layout, Array.Empty<int>()), 0);
        var idx = new WrapDINT(store.Define("Program:P.Idx", LogixDataTypes.DINT, Array.Empty<int>()), 0);
        t.PRE.Value = 1234;
        t.DN.Value = true;
        idx.Value = 55;
        using var ms = new MemoryStream();
        store.SaveSnapshot(ms);

        t.PRE.Value = 0;
        t.DN.Value = false;
        idx.Value = 0;
        ms.Position = 0;
        store.LoadSnapshot(ms);
        Assert.Equal(1234, t.PRE.Value);
        Assert.True(t.DN.Value);
        Assert.Equal(55, idx.Value);
        Assert.Equal(2, store.LastLoad!.TagsRestored);
    }
}
