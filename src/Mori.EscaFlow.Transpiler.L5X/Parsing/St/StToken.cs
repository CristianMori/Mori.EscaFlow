using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.St;

public enum StTokenKind
{
    EndOfFile,
    Unknown,
    Identifier,
    Integer,
    Real,
    String,
    Backslash,
    Assign,
    AssignNonRetentive,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Comma,
    Semicolon,
    Colon,
    Dot,
    DotDot,
    Plus,
    Minus,
    Star,
    Slash,
    Power,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessOrEqual,
    GreaterOrEqual,
    Ampersand,
    // keywords
    KwIf,
    KwThen,
    KwElsif,
    KwElse,
    KwEndIf,
    KwCase,
    KwOf,
    KwEndCase,
    KwFor,
    KwTo,
    KwBy,
    KwDo,
    KwEndFor,
    KwWhile,
    KwEndWhile,
    KwRepeat,
    KwUntil,
    KwEndRepeat,
    KwExit,
    KwReturn,
    KwAnd,
    KwOr,
    KwXor,
    KwNot,
    KwMod,
}

/// <summary>A token of structured text. Text is obtained by slicing the source with Start/Length.</summary>
public readonly record struct StToken(StTokenKind Kind, int Start, int Length, int Line, int Column)
{
    public int End => Start + Length;

    public TextSpan Span => new(Start, Length, Line, Column);

    public bool IsBlockTerminator => Kind is StTokenKind.KwElsif or StTokenKind.KwElse or StTokenKind.KwEndIf
        or StTokenKind.KwEndCase or StTokenKind.KwEndFor or StTokenKind.KwEndWhile or StTokenKind.KwUntil or StTokenKind.KwEndRepeat;
}

public static class StKeywords
{
    private static readonly Dictionary<string, StTokenKind> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IF"] = StTokenKind.KwIf,
        ["THEN"] = StTokenKind.KwThen,
        ["ELSIF"] = StTokenKind.KwElsif,
        ["ELSE"] = StTokenKind.KwElse,
        ["END_IF"] = StTokenKind.KwEndIf,
        ["CASE"] = StTokenKind.KwCase,
        ["OF"] = StTokenKind.KwOf,
        ["END_CASE"] = StTokenKind.KwEndCase,
        ["FOR"] = StTokenKind.KwFor,
        ["TO"] = StTokenKind.KwTo,
        ["BY"] = StTokenKind.KwBy,
        ["DO"] = StTokenKind.KwDo,
        ["END_FOR"] = StTokenKind.KwEndFor,
        ["WHILE"] = StTokenKind.KwWhile,
        ["END_WHILE"] = StTokenKind.KwEndWhile,
        ["REPEAT"] = StTokenKind.KwRepeat,
        ["UNTIL"] = StTokenKind.KwUntil,
        ["END_REPEAT"] = StTokenKind.KwEndRepeat,
        ["EXIT"] = StTokenKind.KwExit,
        ["RETURN"] = StTokenKind.KwReturn,
        ["AND"] = StTokenKind.KwAnd,
        ["OR"] = StTokenKind.KwOr,
        ["XOR"] = StTokenKind.KwXor,
        ["NOT"] = StTokenKind.KwNot,
        ["MOD"] = StTokenKind.KwMod,
    };

    public static bool TryGetKeyword(ReadOnlySpan<char> text, out StTokenKind kind)
    {
        // Keywords are short; avoid allocating for obviously-not-keyword identifiers.
        if (text.Length > 10)
        {
            kind = default;
            return false;
        }
        var lookup = Map.GetAlternateLookup<ReadOnlySpan<char>>();
        return lookup.TryGetValue(text, out kind);
    }

    public static string Describe(StTokenKind kind) => kind switch
    {
        StTokenKind.EndOfFile => "end of text",
        StTokenKind.Identifier => "identifier",
        StTokenKind.Integer => "integer",
        StTokenKind.Real => "real",
        StTokenKind.String => "string",
        StTokenKind.Backslash => "\\",
        StTokenKind.Assign => ":=",
        StTokenKind.AssignNonRetentive => "[:=]",
        StTokenKind.LParen => "(",
        StTokenKind.RParen => ")",
        StTokenKind.LBracket => "[",
        StTokenKind.RBracket => "]",
        StTokenKind.Comma => ",",
        StTokenKind.Semicolon => ";",
        StTokenKind.Colon => ":",
        StTokenKind.Dot => ".",
        StTokenKind.DotDot => "..",
        StTokenKind.Plus => "+",
        StTokenKind.Minus => "-",
        StTokenKind.Star => "*",
        StTokenKind.Slash => "/",
        StTokenKind.Power => "**",
        StTokenKind.Equal => "=",
        StTokenKind.NotEqual => "<>",
        StTokenKind.Less => "<",
        StTokenKind.Greater => ">",
        StTokenKind.LessOrEqual => "<=",
        StTokenKind.GreaterOrEqual => ">=",
        StTokenKind.Ampersand => "&",
        _ when kind >= StTokenKind.KwIf => kind.ToString()[2..].ToUpperInvariant().Replace("ENDIF", "END_IF").Replace("ENDCASE", "END_CASE").Replace("ENDFOR", "END_FOR").Replace("ENDWHILE", "END_WHILE").Replace("ENDREPEAT", "END_REPEAT"),
        _ => kind.ToString(),
    };
}

/// <summary>Maps character offsets of a joined text back to L5X line numbers and columns.</summary>
public sealed class StLineMap
{
    private readonly int[] _starts;
    private readonly int[] _numbers;

    private StLineMap(int[] starts, int[] numbers)
    {
        _starts = starts;
        _numbers = numbers;
    }

    public static StLineMap Single(int lineNumber) => new(new[] { 0 }, new[] { lineNumber });

    /// <summary>Joins the lines with '\n' and builds the map.</summary>
    public static StLineMap Build(IReadOnlyList<StLine> lines, out string text)
    {
        if (lines.Count == 0)
        {
            text = string.Empty;
            return Single(0);
        }
        var starts = new int[lines.Count];
        var numbers = new int[lines.Count];
        var total = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            starts[i] = total;
            numbers[i] = lines[i].Number;
            total += lines[i].Text.Length + 1;
        }
        var sb = new System.Text.StringBuilder(total);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i].Text);
        }
        text = sb.ToString();
        return new StLineMap(starts, numbers);
    }

    public (int Line, int Column) Locate(int offset)
    {
        var idx = Array.BinarySearch(_starts, offset);
        if (idx < 0) idx = ~idx - 1;
        if (idx < 0) idx = 0;
        return (_numbers[idx], offset - _starts[idx] + 1);
    }
}
