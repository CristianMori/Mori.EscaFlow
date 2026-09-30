using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Logic;
using L5x.Model.Logic.St;

namespace L5x.Parsing.St;

/// <summary>
/// Structured text parser facade. All entry points are thread-safe (no shared mutable state) and never throw
/// on bad input: they emit diagnostics and return error nodes instead.
/// </summary>
public static class StParser
{
    /// <summary>Parses a routine body (or SFC action body). Comments are returned through <paramref name="comments"/> when non-null.</summary>
    public static StBlock ParseBlock(IReadOnlyList<StLine> lines, LogicParseContext context, IDiagnosticSink diagnostics, List<StComment>? comments = null)
    {
        var map = StLineMap.Build(lines, out var text);
        var tokens = new StLexer(text, map, diagnostics, comments).Tokenize();
        var core = new StParserCore(text, tokens, context, diagnostics);
        return core.ParseTopLevel();
    }

    /// <summary>Parses a block from a single string (lines separated by '\n'); line numbers start at <paramref name="firstLine"/>.</summary>
    public static StBlock ParseBlock(string text, LogicParseContext context, IDiagnosticSink diagnostics, List<StComment>? comments = null, int firstLine = 0)
    {
        var lines = new List<StLine>();
        var n = firstLine;
        foreach (var l in text.Split('\n')) lines.Add(new StLine(n++, l.TrimEnd('\r')));
        return ParseBlock(lines, context, diagnostics, comments);
    }

    /// <summary>Parses a single expression, e.g. a ladder CPT/CMP operand or an SFC transition condition. Trailing text yields an error expression.</summary>
    public static StExpression ParseExpression(string text, LogicParseContext context, IDiagnosticSink diagnostics, int line = 0)
    {
        var map = StLineMap.Single(line);
        var tokens = new StLexer(text, map, diagnostics, null).Tokenize();
        var core = new StParserCore(text, tokens, context, diagnostics);
        if (core.IsAtEnd)
        {
            diagnostics.Error("ST001", "Empty expression", new DiagnosticLocation(StLine: line, Column: 1));
            return new StErrorExpression(new TextSpan(0, 0, line, 1), text);
        }
        var expr = core.ParseExpression();
        if (!core.IsAtEnd)
        {
            core.ReportTrailing();
            return new StErrorExpression(new TextSpan(0, text.Length, line, 1), text);
        }
        return expr;
    }

    /// <summary>Parses a single expression from lines (SFC transition conditions and presets).</summary>
    public static StExpression ParseExpression(IReadOnlyList<StLine> lines, LogicParseContext context, IDiagnosticSink diagnostics, List<StComment>? comments = null)
    {
        var map = StLineMap.Build(lines, out var text);
        var tokens = new StLexer(text, map, diagnostics, comments).Tokenize();
        var core = new StParserCore(text, tokens, context, diagnostics);
        if (core.IsAtEnd)
        {
            var first = lines.Count > 0 ? lines[0].Number : 0;
            diagnostics.Error("ST001", "Empty expression", new DiagnosticLocation(StLine: first, Column: 1));
            return new StErrorExpression(new TextSpan(0, 0, first, 1), text);
        }
        var expr = core.ParseExpression();
        // Conditions are sometimes written with a trailing ';'
        if (!core.IsAtEnd && core.CurrentToken.Kind == StTokenKind.Semicolon)
        {
            var t = core.CurrentToken;
            var after = new StParserCore(text, tokens, context, diagnostics);
            _ = after; // no-op; keep API simple
        }
        if (!core.IsAtEnd)
        {
            if (core.CurrentToken.Kind == StTokenKind.Semicolon && IsOnlySemicolonsLeft(tokens, core.CurrentToken))
                return expr;
            core.ReportTrailing();
            return new StErrorExpression(new TextSpan(0, text.Length, lines.Count > 0 ? lines[0].Number : 0, 1), text);
        }
        return expr;
    }

    private static bool IsOnlySemicolonsLeft(List<StToken> tokens, StToken from)
    {
        var started = false;
        foreach (var t in tokens)
        {
            if (!started)
            {
                if (t.Start == from.Start) started = true;
                else continue;
            }
            if (t.Kind is StTokenKind.Semicolon) continue;
            if (t.Kind == StTokenKind.EndOfFile) return true;
            return false;
        }
        return true;
    }

    /// <summary>Parses the contents of a subscript (without the brackets), e.g. <c>i</c> or <c>i,j+1</c>.</summary>
    public static IReadOnlyList<StExpression> ParseIndexList(string text, LogicParseContext context, IDiagnosticSink diagnostics, int line = 0)
    {
        var map = StLineMap.Single(line);
        var tokens = new StLexer(text, map, diagnostics, null).Tokenize();
        var core = new StParserCore(text, tokens, context, diagnostics);
        if (core.IsAtEnd) return Array.Empty<StExpression>();
        var list = core.ParseExpressionList();
        core.ReportTrailing();
        return list;
    }

    /// <summary>
    /// Parses a tag reference such as <c>Local:1:I.Data[3].5</c> or <c>\Prog.Tag</c>. Returns null (with no diagnostic)
    /// when the text is not purely a tag path, so callers can fall back to a full expression parse.
    /// </summary>
    public static OperandPath? TryParseOperandPath(string text, LogicParseContext context, IDiagnosticSink diagnostics)
    {
        var local = new DiagnosticBag();
        var map = StLineMap.Single(0);
        var tokens = new StLexer(text, map, local, null).Tokenize();
        if (local.Count > 0) return null;
        if (tokens.Count < 2) return null;
        var first = tokens[0].Kind;
        if (first != StTokenKind.Identifier && first != StTokenKind.Backslash) return null;
        var core = new StParserCore(text, tokens, context, local);
        var variable = core.ParseVariable();
        if (local.Count > 0 || !core.IsAtEnd) return null;
        return variable.Path;
    }
}
