using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Anything that lives in tag memory: a value wrapper, a structure or an array.
/// </summary>
/// <remarks>
/// The runtime keeps no copies of tag data. Every value the generated logic touches is a view over a byte range
/// of an EthernetIPSharp <see cref="Tag"/> buffer, which is the same buffer the EtherNet/IP tag server hands to
/// clients. This is what makes the generated program behave like a controller rather than like a program with a
/// tag export bolted on: an HMI that writes <c>Motor.Speed</c> changes the value the logic reads on its next scan,
/// and the logic writing it is immediately visible to the HMI, with no synchronisation layer, no change events
/// and no serialisation step in between. <see cref="Tag"/>, <see cref="Offset"/> and <see cref="ByteSize"/> are
/// all a consumer needs to copy, overlay (<see cref="PlcView"/>) or snapshot the value.
/// </remarks>
public interface IPlcMemory
{
    /// <summary>The tag whose buffer holds this value.</summary>
    Tag Tag { get; }

    /// <summary>Byte offset of the value inside the tag buffer.</summary>
    int Offset { get; }

    /// <summary>Number of bytes the value occupies from <see cref="Offset"/>.</summary>
    int ByteSize { get; }
}

/// <summary>
/// Base of the atomic value wrappers: a typed view over a byte range of a tag buffer.
/// </summary>
/// <remarks>
/// Generated code holds one wrapper per atomic leaf (every <c>DINT</c>, every <c>BOOL</c> bit, every <c>REAL</c>)
/// and reads and writes through <c>.Value</c>. Writes use the tag's silent path on purpose: a scan performs tens or
/// hundreds of thousands of writes, and raising a change event per write would make the scan unusable. Clients
/// that want to observe values read the memory asynchronously, as they would read a real 1756 controller.
/// Scalars up to 8 bytes are read and written with single unaligned loads and stores, so a scalar is never seen
/// half-written by another thread; structures and arrays, on the other hand, may tear when a client reads them
/// while a scan is in progress, which is exactly the consistency a physical Logix controller offers.
/// </remarks>
public abstract class PlcValue : IPlcMemory
{
    /// <summary>Binds the wrapper to <paramref name="tag"/> at byte <paramref name="offset"/>.</summary>
    protected PlcValue(Tag tag, int offset)
    {
        Tag = tag;
        Offset = offset;
    }

    /// <inheritdoc />
    public Tag Tag { get; }

    /// <inheritdoc />
    public int Offset { get; }

    /// <inheritdoc />
    public abstract int ByteSize { get; }

    /// <summary>The Logix type name (BOOL, SINT, DINT, REAL, ...) as hosts and diagnostics show it.</summary>
    public abstract string TypeName { get; }

    /// <summary>The CIP atomic type code the tag server reports for this value (unsigned kinds share the signed codes, as Logix does).</summary>
    public abstract ushort TypeCode { get; }

    /// <summary>The current value boxed (bool, sbyte, short, int, long, byte, ushort, uint, ulong, float, double).</summary>
    public abstract object GetValue();

    /// <summary>Stores a value given as any numeric, bool or string, converting as a Logix MOV would (reals round half to even, integers wrap).</summary>
    public abstract void SetFrom(object? value);

    /// <summary>The value as invariant text (what the web console and the path reader show).</summary>
    public override string ToString() => PlcConvert.Format(GetValue());
}

/// <summary>
/// A BOOL: one bit of a byte in tag memory.
/// </summary>
/// <remarks>
/// Logix never stores a BOOL on its own: controller-scoped BOOL tags occupy a DINT, BOOL members of a user type
/// are bits of a hidden SINT host, and BOOL arrays are packed 32 per DINT. The wrapper therefore addresses a
/// (byte offset, bit) pair. Bits are numbered little-endian inside their host, so bit 31 of a DINT is bit 7 of
/// byte 3; this matches how the EtherNet/IP client sees <c>Timer.EN</c> at bit 31 of the first DINT of a TIMER.
/// Writes go through <see cref="Tag.AtomicSetBit"/> because two tasks may own different bits of the same byte.
/// </remarks>
public sealed class WrapBOOL : PlcValue, IBoolRef
{
    /// <summary>Binds the wrapper to bit <paramref name="bit"/> of the byte at <paramref name="byteOffset"/>.</summary>
    public WrapBOOL(Tag tag, int byteOffset, int bit = 0) : base(tag, byteOffset) => Bit = bit;

    /// <summary>The bit (0..7) inside the host byte.</summary>
    public int Bit { get; }

    /// <summary>A BOOL view spans the one byte that holds its bit.</summary>
    public override int ByteSize => 1;

    /// <inheritdoc />
    public override string TypeName => "BOOL";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.BOOL;

    /// <summary>The bit's value. Reading tests the bit in the host byte; writing sets or clears it atomically.</summary>
    public bool Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Tag.Read<byte>(Offset) & (1 << Bit)) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Tag.AtomicSetBit(Offset, Bit, value);
    }

    /// <inheritdoc />
    public override object GetValue() => Value;

    /// <inheritdoc />
    public override void SetFrom(object? value) => Value = PlcConvert.ToBool(value);

    /// <summary>Lets a BOOL wrapper be used directly as a condition.</summary>
    public static implicit operator bool(WrapBOOL w) => w.Value;
}

/// <summary>
/// Shared implementation of the integer wrappers (SINT, INT, DINT, LINT and unsigned kinds).
/// </summary>
/// <typeparam name="T">The .NET integer the Logix type maps to (sbyte for SINT, int for DINT, ...).</typeparam>
/// <remarks>
/// Besides the whole value, Logix lets logic address any bit of an integer (<c>Word.3</c>, <c>Word.[i]</c>), both
/// as a condition and as an output coil, so the wrapper exposes an indexer over its bits. Bit writes are atomic
/// for the same reason as <see cref="WrapBOOL"/>: other bits of the same word may be written by another task.
/// </remarks>
public abstract class WrapInteger<T> : PlcValue where T : unmanaged, IBinaryInteger<T>
{
    /// <summary>Binds the wrapper to <paramref name="tag"/> at byte <paramref name="offset"/>.</summary>
    protected WrapInteger(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override int ByteSize => Unsafe.SizeOf<T>();

    /// <summary>The whole integer, read and written with one unaligned access (little-endian, as in the controller).</summary>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Tag.Read<T>(Offset);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Tag.WriteSilent(Offset, value);
    }

    /// <summary>Bit access (<c>Word.3</c>, <c>Word.[i]</c>); bit 0 is the least significant bit of the value.</summary>
    public bool this[int bit]
    {
        get => GetBit(bit);
        set => SetBit(bit, value);
    }

    /// <summary>Tests one bit of the value.</summary>
    public bool GetBit(int bit) => ((Value >> bit) & T.One) != T.Zero;

    /// <summary>Sets or clears one bit atomically; the bit index is translated to (byte, bit) in little-endian order.</summary>
    public void SetBit(int bit, bool value) => Tag.AtomicSetBit(Offset + bit / 8, bit % 8, value);

    /// <inheritdoc />
    public override object GetValue() => Value;

    /// <summary>Stores any value: reals are rounded half to even first, then the result is truncated to the wrapper's width as a Logix MOV does.</summary>
    public override void SetFrom(object? value) => Value = T.CreateTruncating(PlcConvert.ToInt64(value));
}

/// <summary>SINT: 8-bit signed integer.</summary>
public sealed class WrapSINT : WrapInteger<sbyte>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapSINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "SINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.SINT;
}

/// <summary>INT: 16-bit signed integer.</summary>
public sealed class WrapINT : WrapInteger<short>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "INT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.INT;
}

/// <summary>DINT: 32-bit signed integer, the native integer of Logix arithmetic.</summary>
public sealed class WrapDINT : WrapInteger<int>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapDINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "DINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.DINT;
}

/// <summary>LINT: 64-bit signed integer.</summary>
public sealed class WrapLINT : WrapInteger<long>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapLINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "LINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.LINT;
}

/// <summary>USINT: 8-bit unsigned integer. Served with the SINT type code because CIP has no separate code the Logix client stack understands.</summary>
public sealed class WrapUSINT : WrapInteger<byte>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapUSINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "USINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.SINT;
}

/// <summary>UINT: 16-bit unsigned integer (served as INT).</summary>
public sealed class WrapUINT : WrapInteger<ushort>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapUINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "UINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.INT;
}

/// <summary>UDINT: 32-bit unsigned integer (served as DINT).</summary>
public sealed class WrapUDINT : WrapInteger<uint>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapUDINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "UDINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.DINT;
}

/// <summary>ULINT: 64-bit unsigned integer (served as LINT).</summary>
public sealed class WrapULINT : WrapInteger<ulong>
{
    /// <inheritdoc cref="WrapInteger{T}(Tag, int)" />
    public WrapULINT(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "ULINT";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.LINT;
}

/// <summary>
/// Shared implementation of REAL and LREAL.
/// </summary>
/// <typeparam name="T">float for REAL, double for LREAL.</typeparam>
public abstract class WrapFloat<T> : PlcValue where T : unmanaged, IFloatingPoint<T>
{
    /// <summary>Binds the wrapper to <paramref name="tag"/> at byte <paramref name="offset"/>.</summary>
    protected WrapFloat(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override int ByteSize => Unsafe.SizeOf<T>();

    /// <summary>The IEEE value, read and written with one unaligned access.</summary>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Tag.Read<T>(Offset);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Tag.WriteSilent(Offset, value);
    }

    /// <inheritdoc />
    public override object GetValue() => Value;

    /// <summary>Stores any value, saturating at the float range rather than producing infinity from a finite source.</summary>
    public override void SetFrom(object? value) => Value = T.CreateSaturating(PlcConvert.ToDouble(value));
}

/// <summary>REAL: IEEE 754 single precision.</summary>
public sealed class WrapREAL : WrapFloat<float>
{
    /// <inheritdoc cref="WrapFloat{T}(Tag, int)" />
    public WrapREAL(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "REAL";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.REAL;
}

/// <summary>LREAL: IEEE 754 double precision.</summary>
public sealed class WrapLREAL : WrapFloat<double>
{
    /// <inheritdoc cref="WrapFloat{T}(Tag, int)" />
    public WrapLREAL(Tag tag, int offset) : base(tag, offset) { }

    /// <inheritdoc />
    public override string TypeName => "LREAL";

    /// <inheritdoc />
    public override ushort TypeCode => LogixDataTypes.LREAL;
}

/// <summary>
/// Conversions between boxed values, wrappers and text, used wherever a value arrives untyped: service
/// arguments, host writes, snapshot text, <see cref="PlcValue.SetFrom"/>.
/// </summary>
/// <remarks>
/// The rules mirror what a Logix MOV between mismatched types does: a REAL stored into an integer is rounded
/// half to even (not truncated), an integer stored into a narrower integer keeps its low bits, and a BOOL is
/// 0 or 1. Text follows the Logix literal syntax: <c>16#</c>, <c>8#</c> and <c>2#</c> radix prefixes with
/// optional underscores, plus the usual decimal and floating forms, all culture-invariant.
/// </remarks>
public static class PlcConvert
{
    /// <summary>Interprets a value as a condition: numbers are true when nonzero; text accepts 1 / TRUE / ON / YES.</summary>
    public static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        WrapBOOL w => w.Value,
        PlcValue v => ToBool(v.GetValue()),
        string s => s.Trim().ToUpperInvariant() is "1" or "TRUE" or "ON" or "YES",
        _ => ToDouble(value) != 0,
    };

    /// <summary>Interprets a value as a 64-bit integer, rounding reals half to even and parsing Logix radix literals.</summary>
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

    /// <summary>Interprets a value as a double (integers convert exactly up to 2^53, text is parsed invariant).</summary>
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

    /// <summary>The text of a value: string wrappers give their characters, everything else its formatted number.</summary>
    public static string ToText(object? value) => value switch
    {
        null => "",
        string s => s,
        WrapSTRING w => w.Value,
        PlcValue v => Format(v.GetValue()),
        _ => Format(value),
    };

    /// <summary>Formats a boxed value invariantly; BOOL as 0 / 1 (the Logix display form), reals round-trippable.</summary>
    public static string Format(object? value) => value switch
    {
        null => "",
        bool b => b ? "1" : "0",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? "",
    };

    /// <summary>Parses an integer written in any Logix radix (<c>16#FF</c>, <c>2#1010_1010</c>, <c>8#17</c>), C-style hex, decimal, or a real that is then rounded.</summary>
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
