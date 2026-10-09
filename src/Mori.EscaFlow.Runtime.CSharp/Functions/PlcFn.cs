using System.Globalization;
using System.Numerics;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// The IEC standard functions the generated code calls by name.
/// </summary>
/// <remarks>
/// The converter lowers Logix compute, trigonometric, bit and string instructions to assignments of these
/// functions (ABS → ABS, SQR → SQRT, ASN → ASIN, XPY → EXPT, MVM → MASKED_MOVE, MID/FIND/INSERT/DELETE with their
/// Logix operand order). Numeric arguments arrive as values, strings as .NET strings (a <see cref="WrapSTRING"/>
/// converts implicitly), so the functions are plain and testable. Overflow behaviour follows Logix where it
/// differs from .NET: ABS of the most negative integer saturates instead of throwing.
/// </remarks>
public static class PlcFn
{
    // ------------------------------------------------------------------ numeric

    /// <summary>ABS on DINT; the most negative value saturates to the maximum rather than overflowing.</summary>
    public static int ABS(int x) => x == int.MinValue ? int.MaxValue : Math.Abs(x);

    /// <summary>ABS on LINT, saturating.</summary>
    public static long ABS(long x) => x == long.MinValue ? long.MaxValue : Math.Abs(x);

    /// <summary>ABS on REAL.</summary>
    public static float ABS(float x) => MathF.Abs(x);

    /// <summary>ABS on LREAL.</summary>
    public static double ABS(double x) => Math.Abs(x);

    /// <summary>Square root (Logix SQR).</summary>
    public static double SQRT(double x) => Math.Sqrt(x);

    /// <summary>Natural logarithm.</summary>
    public static double LN(double x) => Math.Log(x);

    /// <summary>Base-10 logarithm.</summary>
    public static double LOG(double x) => Math.Log10(x);

    /// <summary>e raised to x.</summary>
    public static double EXP(double x) => Math.Exp(x);

    /// <summary>Sine of radians.</summary>
    public static double SIN(double x) => Math.Sin(x);

    /// <summary>Cosine of radians.</summary>
    public static double COS(double x) => Math.Cos(x);

    /// <summary>Tangent of radians.</summary>
    public static double TAN(double x) => Math.Tan(x);

    /// <summary>Arc sine in radians (Logix ASN).</summary>
    public static double ASIN(double x) => Math.Asin(x);

    /// <summary>Arc cosine in radians (Logix ACS).</summary>
    public static double ACOS(double x) => Math.Acos(x);

    /// <summary>Arc tangent in radians (Logix ATN).</summary>
    public static double ATAN(double x) => Math.Atan(x);

    /// <summary>Truncation toward zero (Logix TRN).</summary>
    public static int TRUNC(double x) => PlcMath.TRUNC(x);

    /// <summary>Round half to even, the rounding Logix applies when a REAL is stored into an integer.</summary>
    public static int ROUND(double x) => PlcMath.RoundEvenToInt32(x);

    /// <summary>x raised to y (Logix XPY).</summary>
    public static double EXPT(double x, double y) => PlcMath.EXPT(x, y);

    /// <summary>Radians to degrees.</summary>
    public static double DEG(double x) => PlcMath.DEG(x);

    /// <summary>Degrees to radians.</summary>
    public static double RAD(double x) => PlcMath.RAD(x);

    /// <summary>Smaller of two values.</summary>
    public static T MIN<T>(T a, T b) where T : INumber<T> => T.Min(a, b);

    /// <summary>Smallest of three values.</summary>
    public static T MIN<T>(T a, T b, T c) where T : INumber<T> => T.Min(a, T.Min(b, c));

    /// <summary>Larger of two values.</summary>
    public static T MAX<T>(T a, T b) where T : INumber<T> => T.Max(a, b);

    /// <summary>Largest of three values.</summary>
    public static T MAX<T>(T a, T b, T c) where T : INumber<T> => T.Max(a, T.Max(b, c));

    /// <summary>Clamps a value between min and max.</summary>
    public static T LIMIT<T>(T min, T value, T max) where T : INumber<T> => T.Min(T.Max(value, min), max);

    /// <summary>Binary selection: in1 when g, else in0.</summary>
    public static T SEL<T>(bool g, T in0, T in1) => g ? in1 : in0;

    /// <summary>Multiplexer: the k-th input, clamped to the available inputs.</summary>
    public static T MUX<T>(int k, params T[] inputs) => inputs[Math.Clamp(k, 0, inputs.Length - 1)];

    /// <summary>Logix LIM: low &lt;= high tests low &lt;= test &lt;= high; low &gt; high tests test &gt;= low OR test &lt;= high.</summary>
    /// <remarks>The second form is the "wrap-around" limit Logix defines when the limits are reversed, used for ranges that cross a rollover point.</remarks>
    public static bool IN_RANGE<T>(T low, T test, T high) where T : INumber<T>
        => low <= high ? low <= test && test <= high : test >= low || test <= high;

    // ------------------------------------------------------------------ bit strings

    /// <summary>Shift left by n bits, zero fill.</summary>
    public static T SHL<T>(T value, int n) where T : IBinaryInteger<T> => value << n;

    /// <summary>Logical shift right by n bits, zero fill (no sign extension, as a bit string shift).</summary>
    public static T SHR<T>(T value, int n) where T : IBinaryInteger<T> => value >>> n;

    /// <summary>Rotate left by n bits.</summary>
    public static T ROL<T>(T value, int n) where T : IBinaryInteger<T> => T.RotateLeft(value, n);

    /// <summary>Rotate right by n bits.</summary>
    public static T ROR<T>(T value, int n) where T : IBinaryInteger<T> => T.RotateRight(value, n);

    /// <summary>MVM: bits of source selected by mask replace the same bits of dest.</summary>
    public static T MASKED_MOVE<T>(T source, T mask, T dest) where T : IBinaryInteger<T> => (source & mask) | (dest & ~mask);

    /// <summary>MEQ: source and compare agree on the masked bits.</summary>
    public static bool MASKED_EQUAL<T>(T source, T mask, T compare) where T : IBinaryInteger<T> => (source & mask) == (compare & mask);

    /// <summary>BTD: copies <paramref name="length"/> bits from source starting at sourceBit into dest starting at destBit.</summary>
    /// <remarks>Bits that would fall outside the 32-bit destination are dropped, as the instruction does.</remarks>
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

    /// <summary>SWPB on an INT: the two bytes exchanged.</summary>
    public static short SWAP_BYTES(short value) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    // ------------------------------------------------------------------ strings

    /// <summary>Length of a .NET string.</summary>
    public static int LEN(string s) => s.Length;

    /// <summary>Current LEN of a Logix string (clamped to its capacity).</summary>
    public static int LEN(WrapSTRING s) => s.Length;

    /// <summary>Concatenation of two strings; the destination clamps to its capacity when stored.</summary>
    public static string CONCAT(string a, string b) => a + b;

    /// <summary>Concatenation of three strings.</summary>
    public static string CONCAT(string a, string b, string c) => a + b + c;

    /// <summary>The leftmost n characters.</summary>
    public static string LEFT(string s, int n) => n <= 0 ? "" : s.Length <= n ? s : s[..n];

    /// <summary>The rightmost n characters.</summary>
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

    /// <summary>REPLACE: quantity characters at 1-based start replaced by the replacement text.</summary>
    public static string REPLACE(string s, string replacement, int quantity, int start) => INSERT(DELETE(s, quantity, start), replacement, start);

    /// <summary>Upper case (Logix UPPER).</summary>
    public static string UPPER(string s) => s.ToUpperInvariant();

    /// <summary>Lower case (Logix LOWER).</summary>
    public static string LOWER(string s) => s.ToLowerInvariant();

    /// <summary>STOD: the leading integer of the text (optional sign, digits), 0 when there is none, as the Logix instruction parses it.</summary>
    public static int STRING_TO_INT(string s)
    {
        s = s.Trim();
        var end = 0;
        if (end < s.Length && (s[end] == '-' || s[end] == '+')) end++;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return int.TryParse(s[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>STOR: the leading real of the text, 0 when there is none.</summary>
    public static float STRING_TO_REAL(string s)
    {
        s = s.Trim();
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] is '-' or '+' or '.' or 'e' or 'E')) end++;
        return float.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>DTOS: decimal text of an integer.</summary>
    public static string INT_TO_STRING(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>RTOS: round-trippable text of a real.</summary>
    public static string REAL_TO_STRING(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>SIZE: element count of one dimension of an array (0 for a dimension it does not have).</summary>
    public static int SIZE(PlcArrayBase array, int dimension = 0) => dimension < array.Dims.Length ? array.Dims[dimension] : 0;

    /// <summary>TOD: integer to packed BCD.</summary>
    public static int TO_BCD(int v) => PlcMath.TO_BCD(v);

    /// <summary>FRD: packed BCD to integer.</summary>
    public static int FROM_BCD(int v) => PlcMath.FROM_BCD(v);
}
