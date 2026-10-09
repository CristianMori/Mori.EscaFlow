using System.Text;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

/// <summary>
/// Decodes the Logix '$' escape convention used in string literals, descriptions and L5K data:
/// $$ $' $Q $L $N $P $R $T and $xx (two hex digits).
/// </summary>
public static class LogixStringDecoder
{
    /// <summary>Decodes escapes in <paramref name="text"/>. Unknown escapes are kept verbatim.</summary>
    public static string Decode(ReadOnlySpan<char> text)
    {
        if (text.IndexOf('$') < 0) return text.ToString();
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '$' || i + 1 >= text.Length)
            {
                sb.Append(c);
                continue;
            }
            var n = text[i + 1];
            switch (n)
            {
                case '$': sb.Append('$'); i++; break;
                case '\'': sb.Append('\''); i++; break;
                case 'Q': case 'q': sb.Append('"'); i++; break;
                case 'L': case 'l': sb.Append('\n'); i++; break;
                case 'N': case 'n': sb.Append("\r\n"); i++; break;
                case 'P': case 'p': sb.Append('\f'); i++; break;
                case 'R': case 'r': sb.Append('\r'); i++; break;
                case 'T': case 't': sb.Append('\t'); i++; break;
                default:
                    if (i + 2 < text.Length && char.IsAsciiHexDigit(n) && char.IsAsciiHexDigit(text[i + 2]))
                    {
                        sb.Append((char)Convert.ToInt32(text.Slice(i + 1, 2).ToString(), 16));
                        i += 2;
                    }
                    else
                    {
                        sb.Append('$');
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Scans a single-quoted string starting at <paramref name="start"/> (which must be the opening quote).
    /// Returns the index just past the closing quote, or -1 when the string is unterminated.
    /// Escaped quotes ($') and doubled dollars ($$) do not terminate the string.
    /// </summary>
    public static int ScanQuoted(ReadOnlySpan<char> text, int start)
    {
        if (start >= text.Length || text[start] != '\'') return -1;
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '$')
            {
                i += 2;
                continue;
            }
            if (c == '\'') return i + 1;
            i++;
        }
        return -1;
    }

    /// <summary>Decodes the contents of a quoted literal (including its quotes). Returns null when malformed.</summary>
    public static string? DecodeQuoted(ReadOnlySpan<char> quoted)
    {
        quoted = quoted.Trim();
        if (quoted.Length < 2 || quoted[0] != '\'' || quoted[^1] != '\'') return null;
        return Decode(quoted[1..^1]);
    }
}
