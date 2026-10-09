using System.Globalization;
using Mori.EscaFlow.Transpiler.L5X.Model;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

/// <summary>
/// Parses Logix numeric literal text: decimal integers, radix-prefixed integers (2#, 8#, 16#, with optional
/// underscores), reals with optional exponent, and the Windows NaN/Infinity spellings (1.#QNAN, 1.#INF, -1.#IND).
/// Shared by the ST lexer, the ladder operand classifier, the L5K parser and the Decorated reader.
/// </summary>
public static class LiteralParser
{
    /// <summary>Tries to parse the whole text as a numeric literal.</summary>
    public static bool TryParseNumber(ReadOnlySpan<char> text, out LiteralValue value)
    {
        value = null!;
        text = text.Trim();
        if (text.IsEmpty) return false;

        var negative = false;
        if (text[0] == '-' || text[0] == '+')
        {
            negative = text[0] == '-';
            text = text[1..];
            if (text.IsEmpty) return false;
        }

        // Special float spellings.
        if (text.Length >= 5 && text[1] == '.' && text[2] == '#')
        {
            // 1.#QNAN, 1.#IND, 1.#INF — the exporter may append a formatting suffix such as "000e+000".
            var tail = text[3..];
            var letters = 0;
            while (letters < tail.Length && char.IsLetter(tail[letters])) letters++;
            var word = tail[..letters];
            var suffix = tail[letters..];
            var suffixOk = true;
            foreach (var ch in suffix)
            {
                if (!(char.IsDigit(ch) || ch is 'e' or 'E' or '+' or '-'))
                {
                    suffixOk = false;
                    break;
                }
            }
            if (!suffixOk) return false;
            if (word.Equals("QNAN", StringComparison.OrdinalIgnoreCase) || word.Equals("SNAN", StringComparison.OrdinalIgnoreCase) || word.Equals("IND", StringComparison.OrdinalIgnoreCase) || word.Equals("NAN", StringComparison.OrdinalIgnoreCase))
            {
                value = new RealLiteral(double.NaN);
                return true;
            }
            if (word.Equals("INF", StringComparison.OrdinalIgnoreCase))
            {
                value = new RealLiteral(negative ? double.NegativeInfinity : double.PositiveInfinity);
                return true;
            }
            return false;
        }

        if (!char.IsDigit(text[0]) && text[0] != '.') return false;

        // Radix prefix.
        var hash = text.IndexOf('#');
        if (hash > 0)
        {
            if (!int.TryParse(text[..hash], NumberStyles.None, CultureInfo.InvariantCulture, out var radix)) return false;
            if (radix is not (2 or 8 or 16 or 10)) return false;
            var digits = text[(hash + 1)..];
            if (digits.IsEmpty) return false;
            if (!TryParseRadixDigits(digits, radix, out var magnitude)) return false;
            value = new IntegerLiteral(negative ? unchecked(-magnitude) : magnitude, radix);
            return true;
        }

        var isReal = false;
        foreach (var c in text)
        {
            if (c == '.' || c == 'e' || c == 'E') { isReal = true; break; }
            if (!char.IsDigit(c)) return false;
        }

        if (!isReal)
        {
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var l))
            {
                value = new IntegerLiteral(negative ? -l : l);
                return true;
            }
            if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ul))
            {
                value = new IntegerLiteral(unchecked((long)ul));
                return true;
            }
            return false;
        }

        if (!IsRealSyntax(text)) return false;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            value = new RealLiteral(negative ? -d : d);
            return true;
        }
        return false;
    }

    public static LiteralValue? ParseNumberOrNull(ReadOnlySpan<char> text) => TryParseNumber(text, out var v) ? v : null;

    /// <summary>Digits of a radix-prefixed literal; underscores are ignored. Accepts up to 64 bits (two's complement).</summary>
    public static bool TryParseRadixDigits(ReadOnlySpan<char> digits, int radix, out long value)
    {
        value = 0;
        ulong acc = 0;
        var any = false;
        foreach (var c in digits)
        {
            if (c == '_') continue;
            var d = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (d < 0 || d >= radix) return false;
            var next = acc * (ulong)radix + (ulong)d;
            if (next < acc) return false; // overflow
            acc = next;
            any = true;
        }
        if (!any) return false;
        value = unchecked((long)acc);
        return true;
    }

    /// <summary>Length of the numeric literal starting at the beginning of <paramref name="text"/>, or 0 when there is none. Does not consume a leading sign.</summary>
    public static int ScanNumber(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || !char.IsDigit(text[0])) return 0;
        var i = 0;
        while (i < text.Length && char.IsDigit(text[i])) i++;

        // radix prefix
        if (i < text.Length && text[i] == '#')
        {
            var j = i + 1;
            while (j < text.Length && (char.IsAsciiHexDigit(text[j]) || text[j] == '_')) j++;
            if (j > i + 1)
            {
                // Avoid swallowing an identifier that just happens to follow, e.g. 16#FFg -> stop at g.
                return j;
            }
            return i;
        }

        // 1.#QNAN / 1.#INF (optionally followed by a numeric formatting suffix)
        if (i + 2 < text.Length && text[i] == '.' && text[i + 1] == '#')
        {
            var j = i + 2;
            while (j < text.Length && char.IsLetter(text[j])) j++;
            while (j < text.Length && (char.IsDigit(text[j]) || text[j] is 'e' or 'E' or '+' or '-')) j++;
            return j;
        }

        // fraction: only when '.' is followed by a digit (so tag.3 and 1..5 are not reals)
        if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsDigit(text[i])) i++;
        }

        // exponent
        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            var j = i + 1;
            if (j < text.Length && (text[j] == '+' || text[j] == '-')) j++;
            if (j < text.Length && char.IsDigit(text[j]))
            {
                while (j < text.Length && char.IsDigit(text[j])) j++;
                i = j;
            }
        }
        return i;
    }

    private static bool IsRealSyntax(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length && char.IsDigit(text[i])) i++;
        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsDigit(text[i])) i++;
        }
        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
            var start = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i == start) return false;
        }
        return i == text.Length;
    }
}
