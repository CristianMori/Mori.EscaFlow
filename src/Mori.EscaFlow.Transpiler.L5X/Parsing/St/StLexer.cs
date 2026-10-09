using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.St;

/// <summary>
/// Tokenizes structured text. Comments are collected separately and never delivered as tokens, so the parser
/// grammar never has to account for them.
/// <para>
/// Logix accepts three comment forms: <c>//</c> to end of line, <c>(* ... *)</c> and <c>/* ... */</c> (the
/// latter two can span lines), plus the editor's own <c>#region</c> / <c>#endregion</c> folding markers, which
/// are only directives when they start a line and are otherwise treated like ordinary text so a <c>#</c> inside
/// an expression is never silently swallowed.
/// </para>
/// </summary>
public sealed class StLexer
{
    private readonly string _text;
    private readonly StLineMap _lines;
    private readonly IDiagnosticSink _diagnostics;
    private readonly List<StComment>? _comments;
    private int _pos;

    public StLexer(string text, StLineMap lines, IDiagnosticSink diagnostics, List<StComment>? comments = null)
    {
        _text = text;
        _lines = lines;
        _diagnostics = diagnostics;
        _comments = comments;
    }

    // Tracks the previous token's kind so the '.' scanner can tell a real literal like ".30" from a member/bit
    // access following an identifier, ']' or ')' (see the '.' case in Next()).
    private StTokenKind _lastKind = StTokenKind.EndOfFile;

    public List<StToken> Tokenize()
    {
        var tokens = new List<StToken>(Math.Max(16, _text.Length / 4));
        while (true)
        {
            var t = Next();
            _lastKind = t.Kind;
            tokens.Add(t);
            if (t.Kind == StTokenKind.EndOfFile) break;
        }
        return tokens;
    }

    private StToken Make(StTokenKind kind, int start, int length)
    {
        var (line, col) = _lines.Locate(start);
        return new StToken(kind, start, length, line, col);
    }

    private StToken Next()
    {
        SkipTrivia();
        if (_pos >= _text.Length) return Make(StTokenKind.EndOfFile, _text.Length, 0);

        var start = _pos;
        var c = _text[_pos];

        if (char.IsLetter(c) || c == '_')
        {
            ScanIdentifier();
            var span = _text.AsSpan(start, _pos - start);
            // A ':' inside the span (e.g. "Local:1:I") means this is a module/alias-style name, never a keyword.
            if (span.IndexOf(':') < 0 && StKeywords.TryGetKeyword(span, out var kw))
                return Make(kw, start, _pos - start);
            return Make(StTokenKind.Identifier, start, _pos - start);
        }

        if (char.IsDigit(c))
        {
            var len = LiteralParser.ScanNumber(_text.AsSpan(_pos));
            if (len == 0) len = 1;
            _pos += len;
            var span = _text.AsSpan(start, len);
            var isReal = span.IndexOf('#') < 0 && (span.IndexOfAny('.', 'e', 'E') >= 0);
            if (span.IndexOf('#') >= 0 && span.Length > 2 && span[1] == '.' && span[2] == '#') isReal = true; // 1.#QNAN
            return Make(isReal ? StTokenKind.Real : StTokenKind.Integer, start, len);
        }

        switch (c)
        {
            case '\'':
            {
                var end = LogixStringDecoder.ScanQuoted(_text.AsSpan(), _pos);
                if (end < 0)
                {
                    // unterminated: consume to end of line
                    var eol = _text.IndexOf('\n', _pos);
                    end = eol < 0 ? _text.Length : eol;
                    _pos = end;
                    Report("ST000", "Unterminated string literal", start);
                    return Make(StTokenKind.Unknown, start, end - start);
                }
                _pos = end;
                return Make(StTokenKind.String, start, end - start);
            }
            case '\\':
                _pos++;
                return Make(StTokenKind.Backslash, start, 1);
            case ':':
                if (Peek(1) == '=')
                {
                    _pos += 2;
                    return Make(StTokenKind.Assign, start, 2);
                }
                _pos++;
                return Make(StTokenKind.Colon, start, 1);
            case '[':
            {
                // [:=] possibly with inner spaces: the non-retentive assignment operator, written as a bracketed
                // ":=" so it reads distinctly from a plain assignment in the exported text.
                var i = _pos + 1;
                while (i < _text.Length && _text[i] == ' ') i++;
                if (i + 1 < _text.Length && _text[i] == ':' && _text[i + 1] == '=')
                {
                    var j = i + 2;
                    while (j < _text.Length && _text[j] == ' ') j++;
                    if (j < _text.Length && _text[j] == ']')
                    {
                        _pos = j + 1;
                        return Make(StTokenKind.AssignNonRetentive, start, _pos - start);
                    }
                }
                _pos++;
                return Make(StTokenKind.LBracket, start, 1);
            }
            case ']': _pos++; return Make(StTokenKind.RBracket, start, 1);
            case '(': _pos++; return Make(StTokenKind.LParen, start, 1);
            case ')': _pos++; return Make(StTokenKind.RParen, start, 1);
            case ',': _pos++; return Make(StTokenKind.Comma, start, 1);
            case ';': _pos++; return Make(StTokenKind.Semicolon, start, 1);
            case '.':
                if (Peek(1) == '.')
                {
                    _pos += 2;
                    return Make(StTokenKind.DotDot, start, 2);
                }
                // A real without a leading digit (".30") is legal wherever a number can start; after an identifier,
                // ']' or ')' a '.' followed by digits is a member/bit access instead.
                if (char.IsDigit(Peek(1)) && _lastKind is not (StTokenKind.Identifier or StTokenKind.RBracket or StTokenKind.RParen or StTokenKind.Integer or StTokenKind.Real))
                {
                    var j = _pos + 1;
                    while (j < _text.Length && char.IsDigit(_text[j])) j++;
                    if (j < _text.Length && (_text[j] == 'e' || _text[j] == 'E'))
                    {
                        var k = j + 1;
                        if (k < _text.Length && (_text[k] == '+' || _text[k] == '-')) k++;
                        if (k < _text.Length && char.IsDigit(_text[k]))
                        {
                            while (k < _text.Length && char.IsDigit(_text[k])) k++;
                            j = k;
                        }
                    }
                    _pos = j;
                    return Make(StTokenKind.Real, start, j - start);
                }
                _pos++;
                return Make(StTokenKind.Dot, start, 1);
            case '+': _pos++; return Make(StTokenKind.Plus, start, 1);
            case '-': _pos++; return Make(StTokenKind.Minus, start, 1);
            case '*':
                if (Peek(1) == '*')
                {
                    _pos += 2;
                    return Make(StTokenKind.Power, start, 2);
                }
                _pos++;
                return Make(StTokenKind.Star, start, 1);
            case '/': _pos++; return Make(StTokenKind.Slash, start, 1);
            case '=': _pos++; return Make(StTokenKind.Equal, start, 1);
            case '<':
                if (Peek(1) == '>')
                {
                    _pos += 2;
                    return Make(StTokenKind.NotEqual, start, 2);
                }
                if (Peek(1) == '=')
                {
                    _pos += 2;
                    return Make(StTokenKind.LessOrEqual, start, 2);
                }
                _pos++;
                return Make(StTokenKind.Less, start, 1);
            case '>':
                if (Peek(1) == '=')
                {
                    _pos += 2;
                    return Make(StTokenKind.GreaterOrEqual, start, 2);
                }
                _pos++;
                return Make(StTokenKind.Greater, start, 1);
            case '&': _pos++; return Make(StTokenKind.Ampersand, start, 1); // accepted synonym for AND
            default:
                _pos++;
                Report("ST000", $"Unexpected character '{c}'", start);
                return Make(StTokenKind.Unknown, start, 1);
        }
    }

    private char Peek(int ahead) => _pos + ahead < _text.Length ? _text[_pos + ahead] : '\0';

    /// <summary>
    /// Identifier: [A-Za-z_][A-Za-z0-9_]* followed by any number of ":segment" parts, where a ':' is only
    /// absorbed when it is not the start of ":=" and is followed by an identifier character. This lets names
    /// like <c>Local:3:I</c> (module-defined tag addressing) and <c>Program:Fault</c> lex as one identifier.
    /// </summary>
    private void ScanIdentifier()
    {
        while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
        while (_pos + 1 < _text.Length && _text[_pos] == ':' && _text[_pos + 1] != '=' && (char.IsLetterOrDigit(_text[_pos + 1]) || _text[_pos + 1] == '_'))
        {
            _pos++;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
        }
    }

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v')
            {
                _pos++;
                continue;
            }
            if (c == '/' && Peek(1) == '/')
            {
                var start = _pos;
                var eol = _text.IndexOf('\n', _pos);
                var end = eol < 0 ? _text.Length : eol;
                AddComment(start, end, isBlock: false);
                _pos = end;
                continue;
            }
            if (c == '#' && IsAtLineStart(_pos))
            {
                // #region / #endregion editor directives: treated like a line comment.
                var start = _pos;
                var eol = _text.IndexOf('\n', _pos);
                var end = eol < 0 ? _text.Length : eol;
                AddComment(start, end, isBlock: false);
                _pos = end;
                continue;
            }
            if (c == '(' && Peek(1) == '*')
            {
                SkipBlockComment("*)");
                continue;
            }
            if (c == '/' && Peek(1) == '*')
            {
                SkipBlockComment("*/");
                continue;
            }
            break;
        }
    }

    /// <summary>True when only whitespace precedes <paramref name="pos"/> on its line (so a leading '#' is a directive, not an operator misuse).</summary>
    private bool IsAtLineStart(int pos)
    {
        for (var i = pos - 1; i >= 0; i--)
        {
            var ch = _text[i];
            if (ch == '\n') return true;
            if (ch != ' ' && ch != '\t' && ch != '\r') return false;
        }
        return true;
    }

    private void SkipBlockComment(string terminator)
    {
        var start = _pos;
        var end = _text.IndexOf(terminator, _pos + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            Report("ST000", "Unterminated block comment", start);
            AddComment(start, _text.Length, isBlock: true);
            _pos = _text.Length;
            return;
        }
        AddComment(start, end + 2, isBlock: true);
        _pos = end + 2;
    }

    private void AddComment(int start, int end, bool isBlock)
    {
        if (_comments is null) return;
        var (line, col) = _lines.Locate(start);
        _comments.Add(new StComment(_text.Substring(start, end - start), line, col, isBlock));
    }

    private void Report(string code, string message, int offset)
    {
        var (line, col) = _lines.Locate(offset);
        _diagnostics.Error(code, message, new DiagnosticLocation(StLine: line, Column: col));
    }
}
