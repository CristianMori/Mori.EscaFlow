using EthernetIPSharp.Logix;
using Xunit;

namespace Mori.EscaFlow.Runtime.Tests;

/// <summary>
/// A packed BOOL[64] array member, exactly as the converter lays one out inside a structure: presented to the
/// tag server as two DWORDs (<see cref="TagStore.RegisterLayout"/> turns a <c>PackedBools</c> member into a
/// DWORD host, since CIP has no bit-array member type), with the wrapper's own bit-level view for logic.
/// </summary>
public sealed class PackedFlags : PlcStruct
{
    public static readonly PlcLayout Layout = new("PackedFlags", 8, 4, new[]
    {
        new PlcLayoutMember("Flags", LogixDataTypes.BOOL, null, 0, 0, new[] { 64 }, 1, packedBools: true),
    });

    public readonly PlcBoolArray Flags;

    public PackedFlags(Tag tag, int offset) : base(tag, offset) => Flags = new PlcBoolArray(tag, offset, new[] { 64 });

    public override object? GetMember(string name) => name.ToUpperInvariant() == "FLAGS" ? Flags : null;
}

/// <summary>A structure one level above <see cref="PackedFlags"/>, so a path to <c>Flags</c> crosses a member step (and, as an array tag, an element index) before it ever reaches the packed member.</summary>
public sealed class Nested : PlcStruct
{
    public static readonly PlcLayout Layout = new("Nested", 8, 4, new[]
    {
        new PlcLayoutMember("Inner", 0, "PackedFlags", 0, 0, Array.Empty<int>(), 8),
    });

    public readonly PackedFlags Inner;

    public Nested(Tag tag, int offset) : base(tag, offset) => Inner = new PackedFlags(tag, offset);

    public override object? GetMember(string name) => name.ToUpperInvariant() == "INNER" ? Inner : null;
}

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

    /// <summary>
    /// A whole-array (unindexed) read of a packed BOOL array falls back to <c>TryResolveArray</c>, since the
    /// library's path walker only resolves an indexed element of such an array, not the array as a whole. That
    /// fallback walks the structure's <em>CIP</em> template, where a packed BOOL array member is presented as a
    /// DWORD host (<see cref="TagStore.RegisterLayout"/>); getting the whole-array read right for such a member —
    /// reporting it as <c>BOOL[n]</c> and reading individual bits, not as a smaller array of raw DWORDs — needs the
    /// member's own <c>PackedBools</c> flag to be picked up, not just the root tag's. This covers both the single
    /// level case and a path that also crosses a structure array index before reaching the packed member, which is
    /// the shape a generated controller actually produces for an add-on or module image nested in an array of tags.
    /// </summary>
    [Fact]
    public void WholeArrayReadOfNestedPackedBoolMemberReadsBitsNotHostWords()
    {
        var store = NewStore();
        store.RegisterLayout(PackedFlags.Layout);
        store.RegisterLayout(Nested.Layout);

        // One level deep, no index: Root.Inner.Flags.
        var root = new Nested(store.Define("Root", Nested.Layout, Array.Empty<int>()), 0);
        root.Inner.Flags[3].Value = true;
        root.Inner.Flags[40].Value = true;
        Assert.True(store.TryRead("Root.Inner.Flags", out var v));
        Assert.Equal("BOOL[64]", v!.Type);
        Assert.Equal("1", Element(v.Text, 3));
        Assert.Equal("1", Element(v.Text, 40));
        Assert.Equal("0", Element(v.Text, 0));

        // Through a structure array index and then a nested member: Elements[2].Inner.Flags.
        var tag = store.Define("Elements", Nested.Layout, new[] { 5 });
        var elements = new PlcArray<Nested>(tag, 0, Nested.Layout.Size, new[] { 5 }, static (t, o) => new Nested(t, o));
        elements[2].Inner.Flags[10].Value = true;
        Assert.True(store.TryRead("Elements[2].Inner.Flags", out v));
        Assert.Equal("BOOL[64]", v!.Type);
        Assert.Equal("1", Element(v.Text, 10));
        Assert.Equal("0", Element(v.Text, 9));
        // A different array element must read its own (all-clear) copy of the member, not element 2's.
        Assert.True(store.TryRead("Elements[0].Inner.Flags", out v));
        Assert.Equal("0", Element(v!.Text, 10));
    }

    /// <summary>The i-th element of a <c>[a, b, c, ...]</c> text as <see cref="TagStore.FormatArray"/> (private) renders it.</summary>
    private static string Element(string arrayText, int index) => arrayText.Trim('[', ']').Split(", ")[index];

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
