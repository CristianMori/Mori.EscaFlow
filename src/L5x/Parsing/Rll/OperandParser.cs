using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Logic.Rll;
using L5x.Model.Logic.St;
using L5x.Parsing.St;
using L5x.Parsing.Values;

namespace L5x.Parsing.Rll;

/// <summary>Classifies the raw text of one instruction argument into an <see cref="Operand"/>.</summary>
public static class OperandParser
{
    /// <summary>
    /// Classifies <paramref name="rawText"/> as operand number <paramref name="index"/> of <paramref name="mnemonic"/>.
    /// Never throws; unparseable text becomes a <see cref="RawOperand"/> with an RLL002 warning.
    /// </summary>
    public static Operand Classify(string mnemonic, int index, string rawText, bool isAddOn, LogicParseContext context, IDiagnosticSink diagnostics)
    {
        var text = rawText.Trim();

        if (text.Length == 0) return EmptyOperand.Instance;
        if (text == "?") return PlaceholderOperand.Single;
        if (text == "??") return PlaceholderOperand.Double;

        if (text[0] == '\'')
        {
            var decoded = LogixStringDecoder.DecodeQuoted(text);
            if (decoded is null)
            {
                diagnostics.Warning("RLL002", $"Unterminated string operand '{text}' in {mnemonic}", DiagnosticLocation.None);
                return new RawOperand(text, "unterminated string");
            }
            return new LiteralOperand(text, new StringLiteral(decoded));
        }

        if (LiteralParser.TryParseNumber(text, out var number))
            return new LiteralOperand(text, number);

        var role = isAddOn ? OperandRole.Tag : InstructionSignatures.RoleAt(mnemonic, index);
        var bare = IsBareIdentifier(text);

        switch (role)
        {
            case OperandRole.Keyword when bare:
                return new KeywordOperand(text);
            case OperandRole.Routine when bare:
                return new RoutineRefOperand(text, text);
            case OperandRole.Label when bare:
                return new LabelOperand(text, text);
            case OperandRole.Expression:
                return ParseExpression(mnemonic, text, context, diagnostics);
        }

        var path = TryPath(text, context, diagnostics);
        if (path is not null) return new TagRefOperand(text, path);

        return ParseExpression(mnemonic, text, context, diagnostics);
    }

    /// <summary>
    /// True for two or more words separated by single spaces, e.g. <c>Forward Only</c> or <c>% of Maximum</c>.
    /// Motion and process instructions use such multi-word enumeration operands. Checked only after the text
    /// failed to parse as an expression, so <c>a - b</c> is never mistaken for a keyword.
    /// </summary>
    public static bool IsWordSequence(ReadOnlySpan<char> text)
    {
        if (text.IndexOf(' ') < 0) return false;
        var start = 0;
        while (start < text.Length)
        {
            var end = text[start..].IndexOf(' ');
            var word = end < 0 ? text[start..] : text.Slice(start, end);
            if (word.IsEmpty) return false;
            for (var i = 0; i < word.Length; i++)
                if (!(char.IsLetterOrDigit(word[i]) || word[i] == '_' || word[i] == '%')) return false;
            if (end < 0) break;
            start += end + 1;
        }
        return true;
    }

    /// <summary>Classifies text that stands alone (e.g. an FBD IRef operand): literal, tag path or expression.</summary>
    public static Operand ClassifyStandalone(string rawText, LogicParseContext context, IDiagnosticSink diagnostics)
        => Classify("", 0, rawText, isAddOn: true, context, diagnostics);

    private static OperandPath? TryPath(string text, LogicParseContext context, IDiagnosticSink diagnostics)
    {
        // The path parser never reports diagnostics for non-paths, but guard against a stray one anyway.
        var scratch = new DiagnosticBag();
        try
        {
            return StParser.TryParseOperandPath(text, context, scratch);
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Operand ParseExpression(string mnemonic, string text, LogicParseContext context, IDiagnosticSink diagnostics)
    {
        var scratch = new DiagnosticBag();
        StExpression expr;
        try
        {
            expr = StParser.ParseExpression(text, context, scratch);
        }
        catch (NotImplementedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Warning("RLL002", $"Cannot parse operand '{text}' of {mnemonic}: {ex.Message}", DiagnosticLocation.None);
            return new RawOperand(text, ex.Message);
        }

        if (expr is StErrorExpression || ContainsError(expr))
        {
            if (IsWordSequence(text)) return new KeywordOperand(text);
            var reason = scratch.Count > 0 ? scratch[0].Message : "syntax error";
            diagnostics.Warning("RLL002", $"Cannot parse operand '{text}' of {mnemonic}: {reason}", DiagnosticLocation.None);
            return new RawOperand(text, reason);
        }

        foreach (var d in scratch) diagnostics.Add(d);
        return new ExpressionOperand(text, expr);
    }

    private static bool ContainsError(StExpression expr) => expr switch
    {
        StErrorExpression => true,
        StUnary u => ContainsError(u.Operand),
        StBinary b => ContainsError(b.Left) || ContainsError(b.Right),
        StCall c => c.Arguments.Any(a => a is not null && ContainsError(a)),
        _ => false,
    };

    /// <summary>True for <c>[A-Za-z_][A-Za-z0-9_]*</c>.</summary>
    public static bool IsBareIdentifier(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return false;
        if (!(char.IsLetter(text[0]) || text[0] == '_')) return false;
        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }
}
