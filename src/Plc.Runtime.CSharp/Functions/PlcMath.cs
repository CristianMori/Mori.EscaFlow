using System.Numerics;

namespace Plc.Runtime;

/// <summary>Logix numeric conversions: REAL to integer rounds half to even; narrowing integer stores wrap.</summary>
public static class PlcMath
{
    public static long RoundEvenToInt64(double v)
    {
        if (double.IsNaN(v)) return 0;
        var r = Math.Round(v, MidpointRounding.ToEven);
        if (r >= long.MaxValue) return long.MaxValue;
        if (r <= long.MinValue) return long.MinValue;
        return (long)r;
    }

    /// <summary>Round half to even; the generated code casts the result to the destination integer type.</summary>
    public static long RoundEven(double v) => RoundEvenToInt64(v);
    public static long RoundEven(float v) => RoundEvenToInt64((double)v);
    public static int RoundEvenToInt32(double v) => unchecked((int)RoundEvenToInt64(v));
    public static double Pow(double x, double y) => Math.Pow(x, y);
    public static short RoundEvenToInt16(double v) => unchecked((short)RoundEvenToInt64(v));
    public static sbyte RoundEvenToInt8(double v) => unchecked((sbyte)RoundEvenToInt64(v));
    public static long RoundEvenToInt64(float v) => RoundEvenToInt64((double)v);

    /// <summary>Generic round-half-even conversion of any numeric to an integer type, wrapping on overflow.</summary>
    public static T RoundEven<T>(double v) where T : IBinaryInteger<T> => T.CreateTruncating(RoundEvenToInt64(v));

    /// <summary>Narrowing integer conversion with wrap-around (Logix stores the low bytes).</summary>
    public static T Wrap<T>(long v) where T : IBinaryInteger<T> => T.CreateTruncating(v);

    public static T Wrap<T>(int v) where T : IBinaryInteger<T> => T.CreateTruncating(v);

    /// <summary>Logix TRUNC: toward zero.</summary>
    public static int TRUNC(double v) => double.IsNaN(v) ? 0 : v >= int.MaxValue ? int.MaxValue : v <= int.MinValue ? int.MinValue : (int)Math.Truncate(v);

    public static double EXPT(double x, double y) => Math.Pow(x, y);
    public static double DEG(double rad) => rad * 180.0 / Math.PI;
    public static double RAD(double deg) => deg * Math.PI / 180.0;

    /// <summary>Integer to packed BCD.</summary>
    public static int TO_BCD(int value)
    {
        if (value < 0) value = -value;
        if (value > 99_999_999) value = 99_999_999;
        var result = 0;
        var shift = 0;
        while (value > 0)
        {
            result |= (value % 10) << shift;
            value /= 10;
            shift += 4;
        }
        return result;
    }

    /// <summary>Packed BCD to integer.</summary>
    public static int FROM_BCD(int bcd)
    {
        var result = 0;
        var mul = 1;
        var v = (uint)bcd;
        while (v > 0)
        {
            result += (int)(v & 0xF) * mul;
            mul *= 10;
            v >>= 4;
        }
        return result;
    }

    /// <summary>Logix MOD: result has the sign of the dividend (C# semantics); division by zero yields 0.</summary>
    public static int MOD(int a, int b) => b == 0 ? 0 : a % b;
    public static long MOD(long a, long b) => b == 0 ? 0 : a % b;
    public static double MOD(double a, double b) => b == 0 ? 0 : a % b;

    /// <summary>Logix DIV on integers: truncates toward zero; division by zero sets the result to the largest value.</summary>
    public static int DIV(int a, int b) => b == 0 ? (a < 0 ? int.MinValue : int.MaxValue) : a / b;
    public static long DIV(long a, long b) => b == 0 ? (a < 0 ? long.MinValue : long.MaxValue) : a / b;
    public static double DIV(double a, double b) => b == 0 ? (a < 0 ? double.NegativeInfinity : double.PositiveInfinity) : a / b;
}
