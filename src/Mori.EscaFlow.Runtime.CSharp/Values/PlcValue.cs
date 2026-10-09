using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>Anything that lives in tag memory: a value wrapper, a structure or an array.</summary>
public interface IPlcMemory
{
    Tag Tag { get; }
    int Offset { get; }
    int ByteSize { get; }
}

/// <summary>Base of the atomic value wrappers: a typed view over a byte range of a tag buffer.</summary>
public abstract class PlcValue : IPlcMemory
{
    protected PlcValue(Tag tag, int offset)
    {
        Tag = tag;
        Offset = offset;
    }

    public Tag Tag { get; }
    public int Offset { get; }
    public abstract int ByteSize { get; }
    public abstract string TypeName { get; }
    public abstract ushort TypeCode { get; }

    /// <summary>The current value boxed (bool, sbyte, short, int, long, byte, ushort, uint, ulong, float, double).</summary>
    public abstract object GetValue();

    /// <summary>Stores a value given as any numeric, bool or string, converting as a Logix MOV would.</summary>
    public abstract void SetFrom(object? value);

    public override string ToString() => PlcConvert.Format(GetValue());
}

public sealed class WrapBOOL : PlcValue, IBoolRef
{
    public WrapBOOL(Tag tag, int byteOffset, int bit = 0) : base(tag, byteOffset) => Bit = bit;

    public int Bit { get; }
    public override int ByteSize => 1;
    public override string TypeName => "BOOL";
    public override ushort TypeCode => LogixDataTypes.BOOL;

    public bool Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Tag.Read<byte>(Offset) & (1 << Bit)) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Tag.AtomicSetBit(Offset, Bit, value);
    }

    public override object GetValue() => Value;
    public override void SetFrom(object? value) => Value = PlcConvert.ToBool(value);
    public static implicit operator bool(WrapBOOL w) => w.Value;
}

/// <summary>Shared implementation of the integer wrappers (SINT, INT, DINT, LINT and unsigned kinds).</summary>
public abstract class WrapInteger<T> : PlcValue where T : unmanaged, IBinaryInteger<T>
{
    protected WrapInteger(Tag tag, int offset) : base(tag, offset) { }

    public override int ByteSize => Unsafe.SizeOf<T>();

    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Tag.Read<T>(Offset);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Tag.WriteSilent(Offset, value);
    }

    /// <summary>Bit access (<c>Word.3</c>, <c>Word.[i]</c>).</summary>
    public bool this[int bit]
    {
        get => GetBit(bit);
        set => SetBit(bit, value);
    }

    public bool GetBit(int bit) => ((Value >> bit) & T.One) != T.Zero;
    public void SetBit(int bit, bool value) => Tag.AtomicSetBit(Offset + bit / 8, bit % 8, value);

    public override object GetValue() => Value;
    public override void SetFrom(object? value) => Value = T.CreateTruncating(PlcConvert.ToInt64(value));
}

public sealed class WrapSINT : WrapInteger<sbyte>
{
    public WrapSINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "SINT";
    public override ushort TypeCode => LogixDataTypes.SINT;
}

public sealed class WrapINT : WrapInteger<short>
{
    public WrapINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "INT";
    public override ushort TypeCode => LogixDataTypes.INT;
}

public sealed class WrapDINT : WrapInteger<int>
{
    public WrapDINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "DINT";
    public override ushort TypeCode => LogixDataTypes.DINT;
}

public sealed class WrapLINT : WrapInteger<long>
{
    public WrapLINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "LINT";
    public override ushort TypeCode => LogixDataTypes.LINT;
}

public sealed class WrapUSINT : WrapInteger<byte>
{
    public WrapUSINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "USINT";
    public override ushort TypeCode => LogixDataTypes.SINT;
}

public sealed class WrapUINT : WrapInteger<ushort>
{
    public WrapUINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "UINT";
    public override ushort TypeCode => LogixDataTypes.INT;
}

public sealed class WrapUDINT : WrapInteger<uint>
{
    public WrapUDINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "UDINT";
    public override ushort TypeCode => LogixDataTypes.DINT;
}

public sealed class WrapULINT : WrapInteger<ulong>
{
    public WrapULINT(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "ULINT";
    public override ushort TypeCode => LogixDataTypes.LINT;
}

/// <summary>Shared implementation of REAL and LREAL.</summary>
public abstract class WrapFloat<T> : PlcValue where T : unmanaged, IFloatingPoint<T>
{
    protected WrapFloat(Tag tag, int offset) : base(tag, offset) { }

    public override int ByteSize => Unsafe.SizeOf<T>();

    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Tag.Read<T>(Offset);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Tag.WriteSilent(Offset, value);
    }

    public override object GetValue() => Value;
    public override void SetFrom(object? value) => Value = T.CreateSaturating(PlcConvert.ToDouble(value));
}

public sealed class WrapREAL : WrapFloat<float>
{
    public WrapREAL(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "REAL";
    public override ushort TypeCode => LogixDataTypes.REAL;
}

public sealed class WrapLREAL : WrapFloat<double>
{
    public WrapLREAL(Tag tag, int offset) : base(tag, offset) { }
    public override string TypeName => "LREAL";
    public override ushort TypeCode => LogixDataTypes.LREAL;
}

/// <summary>Conversions between boxed values, wrappers and text.</summary>
public static class PlcConvert
{
    public static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        WrapBOOL w => w.Value,
        PlcValue v => ToBool(v.GetValue()),
        string s => s.Trim().ToUpperInvariant() is "1" or "TRUE" or "ON" or "YES",
        _ => ToDouble(value) != 0,
    };

    public static long ToInt64(object? value) => value switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        sbyte v => v,
        byte v => v,
        short v => v,
        ushort v => v,
        int v => v,
        uint v => v,
        long v => v,
        ulong v => unchecked((long)v),
        float v => PlcMath.RoundEvenToInt64(v),
        double v => PlcMath.RoundEvenToInt64(v),
        decimal v => (long)Math.Round(v, MidpointRounding.ToEven),
        PlcValue w => ToInt64(w.GetValue()),
        string s => ParseInt64(s),
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };

    public static double ToDouble(object? value) => value switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        sbyte v => v,
        byte v => v,
        short v => v,
        ushort v => v,
        int v => v,
        uint v => v,
        long v => v,
        ulong v => v,
        float v => v,
        double v => v,
        decimal v => (double)v,
        PlcValue w => ToDouble(w.GetValue()),
        string s => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    };

    public static string ToText(object? value) => value switch
    {
        null => "",
        string s => s,
        WrapSTRING w => w.Value,
        PlcValue v => Format(v.GetValue()),
        _ => Format(value),
    };

    public static string Format(object? value) => value switch
    {
        null => "",
        bool b => b ? "1" : "0",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? "",
    };

    private static long ParseInt64(string s)
    {
        s = s.Trim();
        if (s.StartsWith("16#", StringComparison.Ordinal)) return Convert.ToInt64(s[3..].Replace("_", ""), 16);
        if (s.StartsWith("2#", StringComparison.Ordinal)) return Convert.ToInt64(s[2..].Replace("_", ""), 2);
        if (s.StartsWith("8#", StringComparison.Ordinal)) return Convert.ToInt64(s[2..].Replace("_", ""), 8);
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(s[2..], 16);
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        return PlcMath.RoundEvenToInt64(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
    }
}
