using EthernetIPSharp.Logix;
using Xunit;

namespace Mori.EscaFlow.Runtime.Tests;

/// <summary>Atomic wrappers, bit references, arrays and strings over raw tag bytes: layout, rounding and copy semantics.</summary>
public class WrapperTests
{
    private static Tag Bytes(int size) => new(1, "T", LogixDataTypes.SINT, LogixDataTypes.SINT, 1, size);

    [Fact]
    public void BoolBitsShareAHostByte()
    {
        var tag = Bytes(4);
        var a = new WrapBOOL(tag, 1, 0);
        var b = new WrapBOOL(tag, 1, 5);
        a.Value = true;
        b.Value = true;
        Assert.Equal(0b0010_0001, tag.Read<byte>(1));
        a.Value = false;
        Assert.False(a.Value);
        Assert.True(b.Value);
        Assert.Equal(0b0010_0000, tag.Read<byte>(1));
    }

    [Fact]
    public void IntegerAndRealWrappersRoundTrip()
    {
        var tag = Bytes(32);
        var d = new WrapDINT(tag, 4);
        d.Value = -123456;
        Assert.Equal(-123456, tag.Read<int>(4));
        Assert.Equal(-123456, d.Value);
        var r = new WrapREAL(tag, 8);
        r.Value = 1.5f;
        Assert.Equal(1.5f, tag.Read<float>(8));
        var l = new WrapLINT(tag, 16);
        l.Value = long.MinValue + 7;
        Assert.Equal(long.MinValue + 7, l.Value);
        var s = new WrapSINT(tag, 0);
        s.SetFrom(300);
        Assert.Equal(unchecked((sbyte)300), s.Value);
        d.SetFrom(2.5);
        Assert.Equal(2, d.Value); // round half to even
        d.SetFrom(3.5);
        Assert.Equal(4, d.Value);
    }

    [Fact]
    public void IntegerBitsAndBitRef()
    {
        var tag = Bytes(8);
        var w = new WrapDINT(tag, 4);
        w[3] = true;
        w[31] = true;
        Assert.True(w[3]);
        Assert.Equal(unchecked((int)0x8000_0008), w.Value);
        var bit = new PlcBitRef(w, 3);
        Assert.True(bit.Value);
        bit.Value = false;
        Assert.False(w[3]);
        Assert.Equal(unchecked((int)0x8000_0000), w.Value);
        IBoolRef storage = bit;
        Assert.True(PlcLogic.RisingEdge(true, storage));
        Assert.False(PlcLogic.RisingEdge(true, storage));
        Assert.True(PlcLogic.FallingEdge(false, storage));
    }

    [Fact]
    public void StringValueAndLength()
    {
        var tag = Bytes(88);
        var s = new WrapSTRING(tag, 0);
        Assert.Equal(82, s.Capacity);
        Assert.Equal(88, s.Size);
        s.Value = "Hello";
        Assert.Equal(5, s.LEN.Value);
        Assert.Equal((sbyte)'H', s.DATA[0].Value);
        Assert.Equal("Hello", s.Value);
        s.Value = new string('x', 100);
        Assert.Equal(82, s.LEN.Value);
        Assert.Equal(82, s.Value.Length);
        var tag20 = Bytes(24);
        var s20 = new STRING20(tag20, 0);
        Assert.Equal(24, s20.Size);
        s20.Value = "0123456789ABCDEFGHIJKLMNOP";
        Assert.Equal("0123456789ABCDEFGHIJ", s20.Value);
    }

    [Fact]
    public void TwoDimensionalArrayOffsets()
    {
        var tag = Bytes(2 * 3 * 4);
        var a = new PlcArray<WrapDINT>(tag, 0, 4, new[] { 2, 3 }, static (t, o) => new WrapDINT(t, o));
        Assert.Equal(6, a.Length);
        Assert.Equal(24, a.ByteSize);
        a[1, 2].Value = 42;
        Assert.Equal(42, tag.Read<int>((1 * 3 + 2) * 4));
        Assert.Equal(20, a[1, 2].Offset);
        Assert.Throws<IndexOutOfRangeException>(() => a[2, 0]);
        Assert.Same(a[1, 2], a[5]); // flat row-major access is allowed
    }

    [Fact]
    public void BoolArrayIsDwordPacked()
    {
        var tag = Bytes(8);
        var bits = new PlcBoolArray(tag, 0, new[] { 64 });
        bits[5].Value = true;
        bits[33].Value = true;
        Assert.Equal(1u << 5, tag.Read<uint>(0));
        Assert.Equal(1u << 1, tag.Read<uint>(4));
        Assert.True(bits[33].Value);
        Assert.Equal(8, bits.ByteSize);
    }

    [Fact]
    public void StructMembersAndCopy()
    {
        var tag = Bytes(216);
        var m1 = new Motor(tag, 0);
        var m2 = new Motor(tag, 108);
        m1.Speed.Value = 2.5f;
        m1.Fault.Value = true;
        m1.Timer.PRE.Value = 500;
        m1.Name.Value = "pump";
        Assert.Equal(108, m1.Size);
        Assert.True(m1.TryGetMember("timer", out var member));
        Assert.Same(m1.Timer, member);
        PlcMem.Copy(m1, m2, 1);
        Assert.Equal(2.5f, m2.Speed.Value);
        Assert.True(m2.Fault.Value);
        Assert.Equal(500, m2.Timer.PRE.Value);
        Assert.Equal("pump", m2.Name.Value);
        m2.Speed.Value = 0;
        PlcMem.CopyAll(m1, m2);
        Assert.Equal(2.5f, m2.Speed.Value);
    }

    [Fact]
    public void FillWritesEveryElement()
    {
        var tag = Bytes(16);
        var a = new PlcArray<WrapDINT>(tag, 0, 4, new[] { 4 }, static (t, o) => new WrapDINT(t, o));
        PlcMem.Fill(7, a, 3);
        Assert.Equal(new[] { 7, 7, 7, 0 }, a.Select(e => e.Value));
        var src = new WrapDINT(tag, 12);
        src.Value = 9;
        PlcMem.Fill(src, a, 4);
        Assert.All(a, e => Assert.Equal(9, e.Value));
    }

    /// <summary>
    /// A destination/value pairing FLL has no rule for (here: a structure destination filled with a plain number
    /// instead of another structure's view) must not be a silent no-op. A caller that got this far would otherwise
    /// see the destination simply keep its previous value and have no way to tell "the fill ran and did nothing to
    /// change" apart from "the fill was never reached at all" — this can only happen if the converter emitted an
    /// FLL call this method was never meant to receive, so it is reported immediately rather than swallowed.
    /// </summary>
    [Fact]
    public void FillThrowsForAnUnmodelledDestination()
    {
        var tag = Bytes(216);
        var motor = new Motor(tag, 0);
        Assert.Throws<NotSupportedException>(() => PlcMem.Fill(42, motor, 1));

        var array = new PlcArray<Motor>(tag, 0, Motor.Layout.Size, new[] { 2 }, static (t, o) => new Motor(t, o));
        Assert.Throws<NotSupportedException>(() => PlcMem.Fill(42, array, 2));

        Assert.Throws<NotSupportedException>(() => PlcMem.Fill(1, null, 1));
    }

    [Fact]
    public void StandardFunctions()
    {
        Assert.Equal("cde", PlcFn.MID("abcdef", 3, 3));
        Assert.Equal(3, PlcFn.FIND("abcabc", "c", 1));
        Assert.Equal(6, PlcFn.FIND("abcabc", "c", 4));
        Assert.Equal("abXYcd", PlcFn.INSERT("abcd", "XY", 3));
        Assert.Equal("ad", PlcFn.DELETE("abcd", 2, 2));
        Assert.Equal(0x12, PlcMath.TO_BCD(12));
        Assert.Equal(12, PlcMath.FROM_BCD(0x12));
        Assert.Equal(0b1100, PlcFn.BIT_FIELD_DISTRIBUTE(0b11, 0, 0, 2, 2));
        Assert.Equal(0x78563412, PlcFn.SWAP_BYTES(0x12345678));
        Assert.True(PlcFn.IN_RANGE(1, 5, 10));
        Assert.True(PlcFn.IN_RANGE(10, 12, 1)); // wrapped limits
        Assert.Equal(2, PlcMath.RoundEven(2.5));
        Assert.Equal(-2, PlcMath.RoundEven(-2.5));
        Assert.Equal(unchecked((sbyte)200), PlcMath.Wrap<sbyte>(200));
    }
}
