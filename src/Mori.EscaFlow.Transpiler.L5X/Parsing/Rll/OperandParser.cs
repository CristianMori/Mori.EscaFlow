using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;

/// <summary>
/// Classifies the raw text of one instruction argument into an <see cref="Operand"/>.
/// <para>
/// The neutral text does not mark what an operand is: <c>JSR(Main,0)</c> names a routine, <c>GSV(Program,THIS,Name,x)</c>
/// has a class keyword, an instance keyword and an attribute keyword, and <c>CPT(x,a*2)</c> carries an expression.
/// The decision therefore uses, in order: the spelling (empty, placeholder, string, number), the role the
/// instruction signature assigns to the position, and finally whether the text parses as a tag path or as an
/// expression. Add-on instructions have no table entry; every argument of theirs is a tag or literal.
/// </para>
/// <para>
/// A bare identifier in a Tag position is always classified as a tag reference here even when it is really an
/// enumeration word (motion instructions, FAL/FSC modes written in Any positions): the logic phase cannot know
/// which names exist yet. The linker reclassifies those that resolve to no tag as <see cref="KeywordOperand"/>.
/// </para>
/// </summary>
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

        // Strings are quoted with single quotes and use '$' escapes; decode once here so consumers see real characters.
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

        // Numbers in any radix (2#1010, 16#FF, 1.5e3, -3) before anything else: "16#FF" would otherwise look like an expression.
        if (LiteralParser.TryParseNumber(text, out var number))
            return new LiteralOperand(text, number);

        var role = isAddOn ? OperandRole.Tag : InstructionSignatures.RoleAt(mnemonic, index);
        var bare = IsBareIdentifier(text);

        // Role hints only apply to bare words: "JSR(Sub[1],0)" is not a routine name and falls through to the tag path parse.
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

    /// <summary>Tries the text as a pure tag path; a scratch bag keeps the probe silent because failure here is normal.</summary>
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

    /// <summary>
    /// Parses the text with the structured text expression grammar. A multi-word enumeration (<c>Forward Only</c>)
    /// fails that parse and is recognized afterwards; anything else that fails becomes a raw operand with the
    /// first parser message as the reason.
    /// </summary>
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

        // Only a clean parse forwards the probe's diagnostics (informational ones such as unknown functions).
        foreach (var d in scratch) diagnostics.Add(d);
        return new ExpressionOperand(text, expr);
    }

    /// <summary>The expression parser recovers by embedding error nodes; the whole operand is rejected if any is present.</summary>
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
