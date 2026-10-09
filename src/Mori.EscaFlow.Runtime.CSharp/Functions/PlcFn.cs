using System.Globalization;
using System.Numerics;

namespace Mori.EscaFlow.Runtime;

/// <summary>The IEC standard functions the generated code calls by name.</summary>
public static class PlcFn
{
    // ------------------------------------------------------------------ numeric

    public static int ABS(int x) => x == int.MinValue ? int.MaxValue : Math.Abs(x);
    public static long ABS(long x) => x == long.MinValue ? long.MaxValue : Math.Abs(x);
    public static float ABS(float x) => MathF.Abs(x);
    public static double ABS(double x) => Math.Abs(x);
    public static double SQRT(double x) => Math.Sqrt(x);
    public static double LN(double x) => Math.Log(x);
    public static double LOG(double x) => Math.Log10(x);
    public static double EXP(double x) => Math.Exp(x);
    public static double SIN(double x) => Math.Sin(x);
    public static double COS(double x) => Math.Cos(x);
    public static double TAN(double x) => Math.Tan(x);
    public static double ASIN(double x) => Math.Asin(x);
    public static double ACOS(double x) => Math.Acos(x);
    public static double ATAN(double x) => Math.Atan(x);
    public static int TRUNC(double x) => PlcMath.TRUNC(x);
    public static int ROUND(double x) => PlcMath.RoundEvenToInt32(x);
    public static double EXPT(double x, double y) => PlcMath.EXPT(x, y);
    public static double DEG(double x) => PlcMath.DEG(x);
    public static double RAD(double x) => PlcMath.RAD(x);

    public static T MIN<T>(T a, T b) where T : INumber<T> => T.Min(a, b);
    public static T MIN<T>(T a, T b, T c) where T : INumber<T> => T.Min(a, T.Min(b, c));
    public static T MAX<T>(T a, T b) where T : INumber<T> => T.Max(a, b);
    public static T MAX<T>(T a, T b, T c) where T : INumber<T> => T.Max(a, T.Max(b, c));
    public static T LIMIT<T>(T min, T value, T max) where T : INumber<T> => T.Min(T.Max(value, min), max);
    public static T SEL<T>(bool g, T in0, T in1) => g ? in1 : in0;
    public static T MUX<T>(int k, params T[] inputs) => inputs[Math.Clamp(k, 0, inputs.Length - 1)];

    /// <summary>Logix LIM: low &lt;= high tests low &lt;= test &lt;= high; low &gt; high tests test &gt;= low OR test &lt;= high.</summary>
    public static bool IN_RANGE<T>(T low, T test, T high) where T : INumber<T>
        => low <= high ? low <= test && test <= high : test >= low || test <= high;

    // ------------------------------------------------------------------ bit strings

    public static T SHL<T>(T value, int n) where T : IBinaryInteger<T> => value << n;
    public static T SHR<T>(T value, int n) where T : IBinaryInteger<T> => value >>> n;
    public static T ROL<T>(T value, int n) where T : IBinaryInteger<T> => T.RotateLeft(value, n);
    public static T ROR<T>(T value, int n) where T : IBinaryInteger<T> => T.RotateRight(value, n);

    /// <summary>MVM: bits of source selected by mask replace the same bits of dest.</summary>
    public static T MASKED_MOVE<T>(T source, T mask, T dest) where T : IBinaryInteger<T> => (source & mask) | (dest & ~mask);

    /// <summary>MEQ: source and compare agree on the masked bits.</summary>
    public static bool MASKED_EQUAL<T>(T source, T mask, T compare) where T : IBinaryInteger<T> => (source & mask) == (compare & mask);

    /// <summary>BTD: copies <paramref name="length"/> bits from source starting at sourceBit into dest starting at destBit.</summary>
    public static int BIT_FIELD_DISTRIBUTE(int source, int sourceBit, int dest, int destBit, int length)
    {
        if (length <= 0) return dest;
        var result = (uint)dest;
        for (var i = 0; i < length; i++)
        {
            var sb = sourceBit + i;
            var db = destBit + i;
            if (sb > 31 || db > 31) break;
            var bit = ((uint)source >> sb) & 1u;
            result = bit != 0 ? result | (1u << db) : result & ~(1u << db);
        }
        return (int)result;
    }

    /// <summary>SWPB: REVERSE swaps all bytes, WORD swaps bytes within 16-bit words, HIGH/LOW swaps the words.</summary>
    public static int SWAP_BYTES(int value, string mode = "REVERSE")
    {
        var v = (uint)value;
        return mode.ToUpperInvariant() switch
        {
            "WORD" => (int)(((v & 0x00FF00FFu) << 8) | ((v & 0xFF00FF00u) >> 8)),
            "HIGH/LOW" or "HIGHLOW" => (int)((v << 16) | (v >> 16)),
            _ => (int)BinaryPrimitivesReverse(v),
        };
    }

    private static uint BinaryPrimitivesReverse(uint v) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);

    public static short SWAP_BYTES(short value) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    // ------------------------------------------------------------------ strings

    public static int LEN(string s) => s.Length;
    public static int LEN(WrapSTRING s) => s.Length;
    public static string CONCAT(string a, string b) => a + b;
    public static string CONCAT(string a, string b, string c) => a + b + c;
    public static string LEFT(string s, int n) => n <= 0 ? "" : s.Length <= n ? s : s[..n];
    public static string RIGHT(string s, int n) => n <= 0 ? "" : s.Length <= n ? s : s[^n..];

    /// <summary>MID(source, quantity, start): 1-based start as in Logix / IEC.</summary>
    public static string MID(string s, int quantity, int start)
    {
        if (start < 1) start = 1;
        if (start > s.Length || quantity <= 0) return "";
        return s.Substring(start - 1, Math.Min(quantity, s.Length - start + 1));
    }

    /// <summary>FIND(source, search, start): 1-based position of search in source at or after start, 0 when absent.</summary>
    public static int FIND(string source, string search, int start = 1)
    {
        if (start < 1) start = 1;
        if (start > source.Length) return 0;
        var i = source.IndexOf(search, start - 1, StringComparison.Ordinal);
        return i < 0 ? 0 : i + 1;
    }

    /// <summary>INSERT(sourceA, sourceB, start): sourceB inserted into sourceA at 1-based start.</summary>
    public static string INSERT(string a, string b, int start)
    {
        start = Math.Clamp(start, 1, a.Length + 1);
        return a.Insert(start - 1, b);
    }

    /// <summary>DELETE(source, quantity, start): removes quantity characters from 1-based start.</summary>
    public static string DELETE(string s, int quantity, int start)
    {
        if (start < 1 || start > s.Length || quantity <= 0) return s;
        return s.Remove(start - 1, Math.Min(quantity, s.Length - start + 1));
    }

    public static string REPLACE(string s, string replacement, int quantity, int start) => INSERT(DELETE(s, quantity, start), replacement, start);
    public static string UPPER(string s) => s.ToUpperInvariant();
    public static string LOWER(string s) => s.ToLowerInvariant();

    public static int STRING_TO_INT(string s)
    {
        s = s.Trim();
        var end = 0;
        if (end < s.Length && (s[end] == '-' || s[end] == '+')) end++;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return int.TryParse(s[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public static float STRING_TO_REAL(string s)
    {
        s = s.Trim();
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] is '-' or '+' or '.' or 'e' or 'E')) end++;
        return float.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public static string INT_TO_STRING(long v) => v.ToString(CultureInfo.InvariantCulture);
    public static string REAL_TO_STRING(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static int SIZE(PlcArrayBase array, int dimension = 0) => dimension < array.Dims.Length ? array.Dims[dimension] : 0;

    public static int TO_BCD(int v) => PlcMath.TO_BCD(v);
    public static int FROM_BCD(int v) => PlcMath.FROM_BCD(v);
}
