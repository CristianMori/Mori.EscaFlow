using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.St;

/// <summary>Recursive-descent parser over a token list. One instance per text; not reusable.</summary>
internal sealed class StParserCore
{
    private readonly string _text;
    private readonly List<StToken> _tokens;
    private readonly LogicParseContext _context;
    private readonly IDiagnosticSink _diagnostics;
    private int _pos;

    private const int MaxDepth = 200;
    private int _depth;

    public StParserCore(string text, List<StToken> tokens, LogicParseContext context, IDiagnosticSink diagnostics)
    {
        _text = text;
        _tokens = tokens;
        _context = context;
        _diagnostics = diagnostics;
    }

    private StToken Current => _tokens[_pos];

    private StToken PeekToken(int ahead = 1) => _tokens[Math.Min(_pos + ahead, _tokens.Count - 1)];

    private bool AtEnd => Current.Kind == StTokenKind.EndOfFile;

    private string TextOf(in StToken t) => _text.Substring(t.Start, t.Length);

    private StToken Advance()
    {
        var t = Current;
        if (!AtEnd) _pos++;
        return t;
    }

    private bool Accept(StTokenKind kind)
    {
        if (Current.Kind != kind) return false;
        _pos++;
        return true;
    }

    private bool Expect(StTokenKind kind, out StToken token)
    {
        if (Current.Kind == kind)
        {
            token = Advance();
            return true;
        }
        token = Current;
        ReportUnexpected(StKeywords.Describe(kind));
        return false;
    }

    private void ReportUnexpected(string expected)
    {
        var t = Current;
        var found = t.Kind == StTokenKind.EndOfFile ? "end of text" : $"'{TextOf(t)}'";
        _diagnostics.Error("ST001", $"Unexpected {found}, expected {expected}", new DiagnosticLocation(StLine: t.Line, Column: t.Column));
    }

    private void Report(string code, string message, in StToken at, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        => _diagnostics.Add(new ParseDiagnostic(severity, code, message, new DiagnosticLocation(StLine: at.Line, Column: at.Column)));

    private TextSpan SpanFrom(in StToken first) => SpanFrom(first, _pos > 0 ? _tokens[_pos - 1] : first);

    private static TextSpan SpanFrom(in StToken first, in StToken last)
        => new(first.Start, Math.Max(0, last.End - first.Start), first.Line, first.Column);

    // ------------------------------------------------------------------ blocks and statements

    /// <summary>Parses statements until end of text. Stray block terminators are reported and skipped.</summary>
    public StBlock ParseTopLevel()
    {
        var first = Current;
        var statements = new List<StStatement>();
        while (!AtEnd)
        {
            if (Current.IsBlockTerminator)
            {
                var t = Advance();
                Report("ST002", $"Unexpected '{TextOf(t)}' without a matching block", t);
                statements.Add(new StErrorStatement(t.Span, TextOf(t)));
                continue;
            }
            statements.Add(ParseStatement());
        }
        return new StBlock(statements.Count > 0 ? SpanFrom(first) : TextSpan.Empty, statements);
    }

    /// <summary>Parses statements until a block terminator keyword, a CASE label (when requested) or end of text.</summary>
    private StBlock ParseBlock(bool stopAtCaseLabel)
    {
        var first = Current;
        var statements = new List<StStatement>();
        while (!AtEnd && !Current.IsBlockTerminator)
        {
            if (stopAtCaseLabel && IsCaseLabelStart()) break;
            statements.Add(ParseStatement());
        }
        return new StBlock(statements.Count > 0 ? SpanFrom(first) : new TextSpan(first.Start, 0, first.Line, first.Column), statements);
    }

    private bool IsCaseLabelStart()
    {
        if (Current.Kind == StTokenKind.Integer) return true;
        return (Current.Kind == StTokenKind.Minus || Current.Kind == StTokenKind.Plus) && PeekToken().Kind == StTokenKind.Integer;
    }

    private StStatement ParseStatement()
    {
        var start = Current;
        switch (start.Kind)
        {
            case StTokenKind.Semicolon:
                Advance();
                return new StEmptyStatement(start.Span);
            case StTokenKind.KwIf:
                return ParseIf();
            case StTokenKind.KwCase:
                return ParseCase();
            case StTokenKind.KwFor:
                return ParseFor();
            case StTokenKind.KwWhile:
                return ParseWhile();
            case StTokenKind.KwRepeat:
                return ParseRepeat();
            case StTokenKind.KwExit:
                Advance();
                ExpectStatementEnd();
                return new StExit(SpanFrom(start));
            case StTokenKind.KwReturn:
                Advance();
                ExpectStatementEnd();
                return new StReturn(SpanFrom(start));
            case StTokenKind.Identifier when PeekToken().Kind == StTokenKind.LParen:
                return ParseCallStatement();
            case StTokenKind.Identifier:
            case StTokenKind.Backslash:
                return ParseAssignment();
            default:
                ReportUnexpected("a statement");
                return Synchronize(start);
        }
    }

    /// <summary>Skips to the next ';' (consumed) or block terminator / end, producing an error statement.</summary>
    private StErrorStatement Synchronize(in StToken start)
    {
        while (!AtEnd && !Current.IsBlockTerminator)
        {
            var t = Advance();
            if (t.Kind == StTokenKind.Semicolon) break;
        }
        // Guarantee progress when we stopped immediately on a terminator that the caller will not consume.
        if (_pos == IndexOf(start) && !AtEnd) Advance();
        var span = SpanFrom(start);
        return new StErrorStatement(span, _text.Substring(span.Start, span.Length));
    }

    private int IndexOf(in StToken t)
    {
        // tokens are sequential; find by start offset (rare path)
        for (var i = Math.Max(0, _pos - 1); i >= 0; i--)
            if (_tokens[i].Start == t.Start) return i;
        return -1;
    }

    private void ExpectStatementEnd()
    {
        if (!Accept(StTokenKind.Semicolon))
        {
            // Missing ';' is a common editing slip: report but keep going.
            var t = Current;
            Report("ST003", "Missing ';' after statement", t, DiagnosticSeverity.Warning);
        }
    }

    private StStatement ParseAssignment()
    {
        var start = Current;
        var target = ParseVariable();
        bool nonRetentive;
        if (Accept(StTokenKind.Assign)) nonRetentive = false;
        else if (Accept(StTokenKind.AssignNonRetentive)) nonRetentive = true;
        else
        {
            ReportUnexpected("':='");
            return Synchronize(start);
        }
        var value = ParseExpression();
        ExpectStatementEnd();
        return new StAssignment(SpanFrom(start), target, value, nonRetentive);
    }

    private StStatement ParseCallStatement()
    {
        var start = Current;
        var name = TextOf(Advance());
        var args = ParseArgumentList();
        ExpectStatementEnd();
        return new StCallStatement(SpanFrom(start), name, args, _context.IsAddOnInstruction(name));
    }

    /// <summary>Parses '(' [expr] {',' [expr]} ')'. Current must be '('.</summary>
    private List<StExpression?> ParseArgumentList()
    {
        var args = new List<StExpression?>();
        Expect(StTokenKind.LParen, out _);
        if (Accept(StTokenKind.RParen)) return args;
        while (true)
        {
            if (Current.Kind is StTokenKind.Comma or StTokenKind.RParen)
            {
                args.Add(null);
            }
            else
            {
                var first = Current;
                var e = ParseExpression();
                // Multi-word enumeration operands ("Units per sec", "Forward Only") are legal instruction arguments.
                if (e is StVariable { Path: { IsBare: true, ProgramName: null } } v && Current.Kind == StTokenKind.Identifier)
                {
                    var words = new List<string> { v.Path.Root };
                    while (Current.Kind == StTokenKind.Identifier) words.Add(TextOf(Advance()));
                    e = new StKeyword(SpanFrom(first), string.Join(" ", words), StKeywordKind.Keyword);
                }
                args.Add(e);
            }
            if (Accept(StTokenKind.Comma)) continue;
            if (Accept(StTokenKind.RParen)) break;
            ReportUnexpected("',' or ')'");
            // recover: skip to ')' or ';' without consuming ';'
            while (!AtEnd && Current.Kind != StTokenKind.RParen && Current.Kind != StTokenKind.Semicolon && !Current.IsBlockTerminator) Advance();
            Accept(StTokenKind.RParen);
            break;
        }
        return args;
    }

    private StStatement ParseIf()
    {
        var start = Advance(); // IF
        var branches = new List<StIfBranch>();
        var cond = ParseExpression();
        Expect(StTokenKind.KwThen, out _);
        branches.Add(new StIfBranch(cond, ParseBlock(stopAtCaseLabel: false)));
        StBlock? elseBlock = null;
        while (true)
        {
            if (Accept(StTokenKind.KwElsif))
            {
                var c = ParseExpression();
                Expect(StTokenKind.KwThen, out _);
                branches.Add(new StIfBranch(c, ParseBlock(false)));
                continue;
            }
            if (Accept(StTokenKind.KwElse))
            {
                elseBlock = ParseBlock(false);
                // tolerate a second ELSE / ELSIF chain after ELSE by looping (reported below)
                if (Current.Kind is StTokenKind.KwElsif or StTokenKind.KwElse)
                {
                    Report("ST001", "ELSIF/ELSE after ELSE", Current);
                    continue;
                }
            }
            break;
        }
        if (!Accept(StTokenKind.KwEndIf))
        {
            ReportUnexpected("END_IF");
        }
        else
        {
            Accept(StTokenKind.Semicolon);
        }
        return new StIf(SpanFrom(start), branches, elseBlock);
    }

    private StStatement ParseCase()
    {
        var start = Advance(); // CASE
        var selector = ParseExpression();
        Expect(StTokenKind.KwOf, out _);
        var arms = new List<StCaseArm>();
        StBlock? elseBlock = null;
        while (!AtEnd)
        {
            if (Current.Kind == StTokenKind.KwEndCase) break;
            if (Accept(StTokenKind.KwElse))
            {
                elseBlock = ParseBlock(stopAtCaseLabel: false);
                break;
            }
            if (Current.IsBlockTerminator) break;
            if (!IsCaseLabelStart())
            {
                ReportUnexpected("a CASE label");
                var err = Synchronize(Current);
                arms.Add(new StCaseArm(Array.Empty<StCaseLabel>(), new StBlock(err.Span, new StStatement[] { err })));
                continue;
            }
            var labels = new List<StCaseLabel>();
            while (true)
            {
                if (!TryParseCaseLabel(out var label))
                {
                    ReportUnexpected("a CASE label");
                    break;
                }
                labels.Add(label);
                if (!Accept(StTokenKind.Comma)) break;
            }
            Expect(StTokenKind.Colon, out _);
            var body = ParseBlock(stopAtCaseLabel: true);
            arms.Add(new StCaseArm(labels, body));
        }
        if (Accept(StTokenKind.KwEndCase)) Accept(StTokenKind.Semicolon);
        else ReportUnexpected("END_CASE");
        return new StCase(SpanFrom(start), selector, arms, elseBlock);
    }

    private bool TryParseCaseLabel(out StCaseLabel label)
    {
        label = null!;
        if (!TryParseSignedInteger(out var from)) return false;
        long? to = null;
        if (Accept(StTokenKind.DotDot))
        {
            if (!TryParseSignedInteger(out var t)) return false;
            to = t;
        }
        label = new StCaseLabel(from, to);
        return true;
    }

    private bool TryParseSignedInteger(out long value)
    {
        value = 0;
        var negative = false;
        if (Current.Kind == StTokenKind.Minus || Current.Kind == StTokenKind.Plus)
        {
            negative = Current.Kind == StTokenKind.Minus;
            if (PeekToken().Kind != StTokenKind.Integer) return false;
            Advance();
        }
        if (Current.Kind != StTokenKind.Integer) return false;
        var t = Advance();
        if (LiteralParser.TryParseNumber(_text.AsSpan(t.Start, t.Length), out var lit) && lit is IntegerLiteral il)
        {
            value = negative ? -il.Value : il.Value;
            return true;
        }
        Report("ST004", $"Invalid integer literal '{TextOf(t)}'", t);
        return true;
    }

    private StStatement ParseFor()
    {
        var start = Advance(); // FOR
        var variable = ParseVariable();
        Expect(StTokenKind.Assign, out _);
        var from = ParseExpression();
        Expect(StTokenKind.KwTo, out _);
        var to = ParseExpression();
        StExpression? by = null;
        if (Accept(StTokenKind.KwBy)) by = ParseExpression();
        Expect(StTokenKind.KwDo, out _);
        var body = ParseBlock(false);
        if (Accept(StTokenKind.KwEndFor)) Accept(StTokenKind.Semicolon);
        else ReportUnexpected("END_FOR");
        return new StFor(SpanFrom(start), variable, from, to, by, body);
    }

    private StStatement ParseWhile()
    {
        var start = Advance(); // WHILE
        var cond = ParseExpression();
        Expect(StTokenKind.KwDo, out _);
        var body = ParseBlock(false);
        if (Accept(StTokenKind.KwEndWhile)) Accept(StTokenKind.Semicolon);
        else ReportUnexpected("END_WHILE");
        return new StWhile(SpanFrom(start), cond, body);
    }

    private StStatement ParseRepeat()
    {
        var start = Advance(); // REPEAT
        var body = ParseBlock(false);
        StExpression until;
        if (Accept(StTokenKind.KwUntil))
        {
            until = ParseExpression();
        }
        else
        {
            ReportUnexpected("UNTIL");
            until = new StErrorExpression(Current.Span, "");
        }
        if (Accept(StTokenKind.KwEndRepeat)) Accept(StTokenKind.Semicolon);
        else ReportUnexpected("END_REPEAT");
        return new StRepeat(SpanFrom(start), body, until);
    }

    // ------------------------------------------------------------------ expressions

    public StExpression ParseExpression()
    {
        if (++_depth > MaxDepth)
        {
            _depth--;
            Report("ST005", "Expression nesting too deep", Current);
            var t = Advance();
            return new StErrorExpression(t.Span, TextOf(t));
        }
        try
        {
            return ParseOr();
        }
        finally
        {
            _depth--;
        }
    }

    private StExpression ParseOr()
    {
        var left = ParseXor();
        while (Current.Kind == StTokenKind.KwOr)
        {
            Advance();
            var right = ParseXor();
            left = new StBinary(SpanFrom(TokenAt(left)), BinaryOp.Or, left, right);
        }
        return left;
    }

    private StExpression ParseXor()
    {
        var left = ParseAnd();
        while (Current.Kind == StTokenKind.KwXor)
        {
            Advance();
            var right = ParseAnd();
            left = new StBinary(SpanFrom(TokenAt(left)), BinaryOp.Xor, left, right);
        }
        return left;
    }

    private StExpression ParseAnd()
    {
        var left = ParseComparison();
        while (Current.Kind is StTokenKind.KwAnd or StTokenKind.Ampersand)
        {
            Advance();
            var right = ParseComparison();
            left = new StBinary(SpanFrom(TokenAt(left)), BinaryOp.And, left, right);
        }
        return left;
    }

    private StExpression ParseComparison()
    {
        var left = ParseAdditive();
        while (true)
        {
            BinaryOp op;
            switch (Current.Kind)
            {
                case StTokenKind.Equal: op = BinaryOp.Equal; break;
                case StTokenKind.NotEqual: op = BinaryOp.NotEqual; break;
                case StTokenKind.Less: op = BinaryOp.Less; break;
                case StTokenKind.Greater: op = BinaryOp.Greater; break;
                case StTokenKind.LessOrEqual: op = BinaryOp.LessOrEqual; break;
                case StTokenKind.GreaterOrEqual: op = BinaryOp.GreaterOrEqual; break;
                default: return left;
            }
            Advance();
            var right = ParseAdditive();
            left = new StBinary(SpanFrom(TokenAt(left)), op, left, right);
        }
    }

    private StExpression ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is StTokenKind.Plus or StTokenKind.Minus)
        {
            var op = Advance().Kind == StTokenKind.Plus ? BinaryOp.Add : BinaryOp.Subtract;
            var right = ParseMultiplicative();
            left = new StBinary(SpanFrom(TokenAt(left)), op, left, right);
        }
        return left;
    }

    private StExpression ParseMultiplicative()
    {
        var left = ParseUnary();
        while (true)
        {
            BinaryOp op;
            switch (Current.Kind)
            {
                case StTokenKind.Star: op = BinaryOp.Multiply; break;
                case StTokenKind.Slash: op = BinaryOp.Divide; break;
                case StTokenKind.KwMod: op = BinaryOp.Modulo; break;
                default: return left;
            }
            Advance();
            var right = ParseUnary();
            left = new StBinary(SpanFrom(TokenAt(left)), op, left, right);
        }
    }

    private StExpression ParseUnary()
    {
        var start = Current;
        if (Accept(StTokenKind.Minus))
        {
            var operand = ParseUnary();
            return new StUnary(SpanFrom(start), UnaryOp.Negate, operand);
        }
        if (Accept(StTokenKind.Plus))
        {
            return ParseUnary();
        }
        if (Accept(StTokenKind.KwNot))
        {
            var operand = ParseUnary();
            return new StUnary(SpanFrom(start), UnaryOp.Not, operand);
        }
        return ParsePower();
    }

    private StExpression ParsePower()
    {
        var left = ParsePrimary();
        if (Current.Kind == StTokenKind.Power)
        {
            Advance();
            var right = ParseUnary(); // right associative, allows 2**-1
            return new StBinary(SpanFrom(TokenAt(left)), BinaryOp.Power, left, right);
        }
        return left;
    }

    private StExpression ParsePrimary()
    {
        var t = Current;
        switch (t.Kind)
        {
            case StTokenKind.Integer:
            case StTokenKind.Real:
            {
                Advance();
                if (LiteralParser.TryParseNumber(_text.AsSpan(t.Start, t.Length), out var lit))
                    return new StLiteral(t.Span, lit);
                Report("ST004", $"Invalid numeric literal '{TextOf(t)}'", t);
                return new StErrorExpression(t.Span, TextOf(t));
            }
            case StTokenKind.String:
            {
                Advance();
                var decoded = LogixStringDecoder.DecodeQuoted(_text.AsSpan(t.Start, t.Length)) ?? "";
                return new StLiteral(t.Span, new StringLiteral(decoded));
            }
            case StTokenKind.LParen:
            {
                Advance();
                var inner = ParseExpression();
                if (!Accept(StTokenKind.RParen)) ReportUnexpected("')'");
                return inner;
            }
            case StTokenKind.Identifier when PeekToken().Kind == StTokenKind.LParen:
            {
                Advance();
                var args = ParseArgumentList();
                return new StCall(SpanFrom(t), TextOf(t), args);
            }
            case StTokenKind.Identifier:
            case StTokenKind.Backslash:
                return ParseVariable();
            default:
            {
                ReportUnexpected("an expression");
                // Do not swallow tokens that end the enclosing construct.
                if (t.Kind is StTokenKind.Semicolon or StTokenKind.RParen or StTokenKind.RBracket or StTokenKind.Comma or StTokenKind.EndOfFile || t.IsBlockTerminator || t.Kind is StTokenKind.KwThen or StTokenKind.KwDo or StTokenKind.KwOf or StTokenKind.KwTo or StTokenKind.KwBy)
                    return new StErrorExpression(new TextSpan(t.Start, 0, t.Line, t.Column), "");
                Advance();
                return new StErrorExpression(t.Span, TextOf(t));
            }
        }
    }

    /// <summary>variable := ['\' IDENT '.'] IDENT { '.' IDENT | '.' INTEGER | '[' expr {',' expr} ']' }</summary>
    public StVariable ParseVariable()
    {
        var start = Current;
        string? programName = null;
        if (Accept(StTokenKind.Backslash))
        {
            if (Current.Kind == StTokenKind.Identifier)
            {
                programName = TextOf(Advance());
                if (!Accept(StTokenKind.Dot)) ReportUnexpected("'.' after program name");
            }
            else
            {
                ReportUnexpected("program name after '\\'");
            }
        }

        string root;
        if (Current.Kind == StTokenKind.Identifier)
        {
            root = TextOf(Advance());
        }
        else
        {
            ReportUnexpected("identifier");
            root = "";
        }

        List<PathSegment>? segments = null;
        while (true)
        {
            if (Current.Kind == StTokenKind.Dot)
            {
                var next = PeekToken();
                if (next.Kind == StTokenKind.Identifier || next.Kind >= StTokenKind.KwIf)
                {
                    // A keyword directly after '.' can only be a member name (e.g. Tag.Return).
                    Advance();
                    Advance();
                    (segments ??= new()).Add(new MemberSegment(TextOf(next)));
                    continue;
                }
                if (next.Kind == StTokenKind.Integer)
                {
                    Advance();
                    Advance();
                    var digits = _text.AsSpan(next.Start, next.Length);
                    if (int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var bit))
                        (segments ??= new()).Add(new BitSegment(bit));
                    else
                    {
                        Report("ST004", $"Invalid bit number '{TextOf(next)}'", next);
                        (segments ??= new()).Add(new MemberSegment(TextOf(next)));
                    }
                    continue;
                }
                if (next.Kind == StTokenKind.LBracket)
                {
                    // Indirect bit addressing: Tag.[index]
                    Advance();
                    Advance();
                    var bitExpr = ParseExpression();
                    if (!Accept(StTokenKind.RBracket)) ReportUnexpected("']'");
                    (segments ??= new()).Add(new DynamicBitSegment(bitExpr));
                    continue;
                }
                if (next.Kind == StTokenKind.Real)
                {
                    // e.g. Tag.5e3 would lex as a real; treat leading digits as a bit number and report.
                    Advance();
                    Advance();
                    Report("ST004", $"Invalid member or bit '{TextOf(next)}'", next);
                    (segments ??= new()).Add(new MemberSegment(TextOf(next)));
                    continue;
                }
                Advance();
                ReportUnexpected("member name or bit number after '.'");
                break;
            }
            if (Current.Kind == StTokenKind.LBracket)
            {
                Advance();
                var indices = new List<StExpression>();
                if (Current.Kind == StTokenKind.RBracket)
                {
                    ReportUnexpected("index expression");
                }
                else
                {
                    indices.Add(ParseExpression());
                    while (Accept(StTokenKind.Comma)) indices.Add(ParseExpression());
                }
                if (!Accept(StTokenKind.RBracket)) ReportUnexpected("']'");
                (segments ??= new()).Add(new IndexSegment(indices));
                continue;
            }
            break;
        }

        var path = new OperandPath(root, segments is null ? Array.Empty<PathSegment>() : segments, programName);
        return new StVariable(SpanFrom(start), path);
    }

    /// <summary>Reconstructs a token-like anchor from an expression's span for building enclosing spans.</summary>
    private static StToken TokenAt(StExpression e) => new(StTokenKind.Unknown, e.Span.Start, e.Span.Length, e.Span.Line, e.Span.Column);

    // ------------------------------------------------------------------ helpers for the facade

    public bool IsAtEnd => AtEnd;

    public StToken CurrentToken => Current;

    public List<StExpression> ParseExpressionList()
    {
        var list = new List<StExpression> { ParseExpression() };
        while (Accept(StTokenKind.Comma)) list.Add(ParseExpression());
        return list;
    }

    public void ReportTrailing()
    {
        if (!AtEnd) ReportUnexpected("end of expression");
    }
}
