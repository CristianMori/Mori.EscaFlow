using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace L5x.Model;

/// <summary>A constant appearing in logic text or in an operand.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(IntegerLiteral), "int")]
[JsonDerivedType(typeof(RealLiteral), "real")]
[JsonDerivedType(typeof(StringLiteral), "string")]
public abstract record LiteralValue
{
    public abstract string ToLogixText();
    public override string ToString() => ToLogixText();
}

/// <summary>An integer constant. RadixBase is 10, 2, 8 or 16 depending on how it was written.</summary>
public sealed record IntegerLiteral(long Value, int RadixBase = 10) : LiteralValue
{
    public override string ToLogixText() => RadixBase switch
    {
        2 => "2#" + Convert.ToString(Value, 2),
        8 => "8#" + Convert.ToString(Value, 8),
        16 => "16#" + Convert.ToString(Value, 16),
        _ => Value.ToString(CultureInfo.InvariantCulture),
    };
}

public sealed record RealLiteral(double Value) : LiteralValue
{
    public override string ToLogixText()
    {
        if (double.IsNaN(Value)) return "1.#QNAN";
        if (double.IsPositiveInfinity(Value)) return "1.#INF";
        if (double.IsNegativeInfinity(Value)) return "-1.#INF";
        var s = Value.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') || s.Contains('e') ? s : s + ".0";
    }
}

/// <summary>A string constant with Logix '$' escapes already decoded.</summary>
public sealed record StringLiteral(string Value) : LiteralValue
{
    public override string ToLogixText() => "'" + LogixStringEncoder.Encode(Value) + "'";
}

/// <summary>Encodes text using Logix '$' escapes (the inverse of <c>LogixStringDecoder</c>).</summary>
public static class LogixStringEncoder
{
    public static string Encode(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '$': sb.Append("$$"); break;
                case '\'': sb.Append("$'"); break;
                case '"': sb.Append("$Q"); break;
                case '\n': sb.Append("$L"); break;
                case '\r': sb.Append("$R"); break;
                case '\t': sb.Append("$T"); break;
                case '\f': sb.Append("$P"); break;
                default:
                    if (c < 0x20 || c > 0x7E) sb.Append('$').Append(((int)c & 0xFF).ToString("X2"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
