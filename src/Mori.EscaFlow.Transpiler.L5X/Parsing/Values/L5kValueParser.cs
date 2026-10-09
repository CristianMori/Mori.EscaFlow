using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

/// <summary>
/// Parses the text of a <c>&lt;Data Format="L5K"&gt;</c> initializer into an untyped <see cref="L5kValue"/> tree.
/// Grammar: value := list | string | number; list := '[' [value {',' value}] ']'.
/// Whitespace (space, tab, CR, LF) may appear anywhere between tokens. Never throws.
/// </summary>
public static class L5kValueParser
{
    public const string CodeBadToken = "L5K001";
    public const string CodeUnterminated = "L5K002";

    public static L5kValue Parse(ReadOnlySpan<char> text, IDiagnosticSink diagnostics)
    {
        var pos = 0;
        SkipWhitespace(text, ref pos);
        if (pos >= text.Length)
        {
            return L5kList.Empty;
        }
        var value = ParseValue(text, ref pos, diagnostics);
        SkipWhitespace(text, ref pos);
        if (pos < text.Length)
        {
            // Trailing garbage: report once.
            var end = ScanRawToken(text, pos);
            diagnostics.Warning(CodeBadToken, $"Unexpected text after value: '{text.Slice(pos, end - pos).ToString()}'", new DiagnosticLocation(Column: pos));
        }
        return value;
    }

    public static L5kValue Parse(string text, IDiagnosticSink diagnostics) => Parse(text.AsSpan(), diagnostics);

    private static L5kValue ParseValue(ReadOnlySpan<char> text, ref int pos, IDiagnosticSink diagnostics)
    {
        SkipWhitespace(text, ref pos);
        if (pos >= text.Length)
        {
            diagnostics.Warning(CodeUnterminated, "Unexpected end of L5K data", new DiagnosticLocation(Column: pos));
            return new L5kRaw("");
        }

        var c = text[pos];
        if (c == '[')
        {
            return ParseList(text, ref pos, diagnostics);
        }
        if (c == '\'')
        {
            var end = LogixStringDecoder.ScanQuoted(text, pos);
            if (end < 0)
            {
                diagnostics.Warning(CodeUnterminated, "Unterminated string in L5K data", new DiagnosticLocation(Column: pos));
                var raw = text[pos..].ToString();
                pos = text.Length;
                return new L5kRaw(raw);
            }
            var decoded = LogixStringDecoder.Decode(text.Slice(pos + 1, end - pos - 2));
            pos = end;
            return new L5kString(decoded);
        }

        var tokenEnd = ScanRawToken(text, pos);
        if (tokenEnd == pos)
        {
            // A stray delimiter such as ',' or ']' where a value was expected.
            diagnostics.Warning(CodeBadToken, $"Unexpected character '{c}' in L5K data", new DiagnosticLocation(Column: pos));
            return new L5kRaw("");
        }
        var token = text.Slice(pos, tokenEnd - pos);
        pos = tokenEnd;

        if (LiteralParser.TryParseNumber(token, out var literal))
        {
            switch (literal)
            {
                case IntegerLiteral i:
                    return new L5kInteger(i.Value, i.RadixBase == 10 ? null : RadixPrefix(token));
                case RealLiteral r:
                    return new L5kReal(r.Value);
            }
        }

        if (IsIdentifier(token))
        {
            return new L5kRaw(token.ToString());
        }

        diagnostics.Warning(CodeBadToken, $"Unrecognized token '{token.ToString()}' in L5K data", new DiagnosticLocation(Column: tokenEnd - token.Length));
        return new L5kRaw(token.ToString());
    }

    private static L5kValue ParseList(ReadOnlySpan<char> text, ref int pos, IDiagnosticSink diagnostics)
    {
        var start = pos;
        pos++; // '['
        var items = new List<L5kValue>();
        SkipWhitespace(text, ref pos);
        if (pos < text.Length && text[pos] == ']')
        {
            pos++;
            return L5kList.Empty;
        }

        while (true)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length)
            {
                diagnostics.Warning(CodeUnterminated, "Unterminated list in L5K data", new DiagnosticLocation(Column: start));
                return new L5kList(items);
            }
            var ch = text[pos];
            if (ch == ',' || ch == ']')
            {
                // Empty slot (e.g. "[1,,2]"): record an empty raw so positions stay aligned.
                items.Add(new L5kRaw(""));
            }
            else
            {
                items.Add(ParseValue(text, ref pos, diagnostics));
            }

            SkipWhitespace(text, ref pos);
            if (pos >= text.Length)
            {
                diagnostics.Warning(CodeUnterminated, "Unterminated list in L5K data", new DiagnosticLocation(Column: start));
                return new L5kList(items);
            }
            ch = text[pos];
            if (ch == ',')
            {
                pos++;
                continue;
            }
            if (ch == ']')
            {
                pos++;
                return new L5kList(items);
            }
            // Something unexpected between items: report it, consume it, and keep going.
            var end = ScanRawToken(text, pos);
            if (end == pos) end = pos + 1;
            diagnostics.Warning(CodeBadToken, $"Unexpected text '{text.Slice(pos, end - pos).ToString()}' in L5K list", new DiagnosticLocation(Column: pos));
            pos = end;
        }
    }

    private static void SkipWhitespace(ReadOnlySpan<char> text, ref int pos)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') pos++;
            else break;
        }
    }

    /// <summary>Scans a token up to the next delimiter (',', ']', '[', quote or whitespace).</summary>
    private static int ScanRawToken(ReadOnlySpan<char> text, int pos)
    {
        var i = pos;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == ',' || c == ']' || c == '[' || c == '\'' || c == ' ' || c == '\t' || c == '\r' || c == '\n') break;
            i++;
        }
        return i;
    }

    private static string? RadixPrefix(ReadOnlySpan<char> token)
    {
        var hash = token.IndexOf('#');
        if (hash < 0) return null;
        var start = 0;
        if (token[0] == '-' || token[0] == '+') start = 1;
        return token.Slice(start, hash - start + 1).ToString();
    }

    private static bool IsIdentifier(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty) return false;
        if (!(char.IsLetter(token[0]) || token[0] == '_')) return false;
        foreach (var c in token)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '.')) return false;
        }
        return true;
    }
}
