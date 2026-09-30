using L5x.Diagnostics;
using L5x.Model.Logic.St;
using L5x.Parsing.St;
using Xunit;

namespace L5x.Tests.St;

public class StLexerTests
{
    private static List<StToken> Lex(string text, DiagnosticBag? bag = null, List<StComment>? comments = null)
        => new StLexer(text, StLineMap.Single(0), bag ?? new DiagnosticBag(), comments).Tokenize();

    private static string[] Kinds(string text) => Lex(text).Select(t => t.Kind.ToString()).ToArray();

    [Fact]
    public void AssignVersusModuleTagColon()
    {
        var k = Kinds("a:=b;");
        Assert.Equal(new[] { "Identifier", "Assign", "Identifier", "Semicolon", "EndOfFile" }, k);

        var t = Lex("Local:1:I.Data.3");
        Assert.Equal(StTokenKind.Identifier, t[0].Kind);
        Assert.Equal(9, t[0].Length); // "Local:1:I"
        Assert.Equal(new[] { "Identifier", "Dot", "Identifier", "Dot", "Integer", "EndOfFile" }, t.Select(x => x.Kind.ToString()).ToArray());
    }

    [Fact]
    public void SystemTagColon()
    {
        var t = Lex("S:FS");
        Assert.Equal(StTokenKind.Identifier, t[0].Kind);
        Assert.Equal(4, t[0].Length);
    }

    [Fact]
    public void NonRetentiveAssign()
    {
        Assert.Equal(new[] { "Identifier", "AssignNonRetentive", "Integer", "Semicolon", "EndOfFile" }, Kinds("x [:=] 1;"));
        Assert.Equal(new[] { "Identifier", "AssignNonRetentive", "Integer", "Semicolon", "EndOfFile" }, Kinds("x[:=]1;"));
        Assert.Equal(new[] { "Identifier", "LBracket", "Integer", "RBracket", "Assign", "Integer", "Semicolon", "EndOfFile" }, Kinds("x[1]:=1;"));
    }

    [Fact]
    public void BitNumberVersusReal()
    {
        Assert.Equal(new[] { "Identifier", "Dot", "Integer", "EndOfFile" }, Kinds("x.14"));
        Assert.Equal(new[] { "Real", "EndOfFile" }, Kinds("1.5"));
        Assert.Equal(new[] { "Real", "EndOfFile" }, Kinds("1.5e-3"));
        Assert.Equal(new[] { "Integer", "DotDot", "Integer", "EndOfFile" }, Kinds("1..5"));
    }

    [Fact]
    public void RadixLiterals()
    {
        Assert.Equal(new[] { "Integer", "EndOfFile" }, Kinds("16#0F"));
        Assert.Equal(new[] { "Integer", "EndOfFile" }, Kinds("2#0000_1111"));
        Assert.Equal(new[] { "Integer", "EndOfFile" }, Kinds("8#17"));
    }

    [Fact]
    public void CommentsAreCollected()
    {
        var comments = new List<StComment>();
        var t = Lex("IF a THEN(*inline*) b := 1; // trailing\n/* block */ END_IF;", comments: comments);
        Assert.Equal(3, comments.Count);
        Assert.Equal("(*inline*)", comments[0].Text);
        Assert.True(comments[0].IsBlock);
        Assert.Equal("// trailing", comments[1].Text);
        Assert.False(comments[1].IsBlock);
        Assert.Equal("/* block */", comments[2].Text);
        Assert.DoesNotContain(t, x => x.Kind == StTokenKind.Unknown);
        Assert.Equal(StTokenKind.KwThen, t[2].Kind);
        Assert.Equal(StTokenKind.Identifier, t[3].Kind);
    }

    [Fact]
    public void KeywordsAreCaseInsensitive()
    {
        Assert.Equal(new[] { "KwIf", "Identifier", "KwThen", "KwEndIf", "EndOfFile" }, Kinds("if a then end_if"));
    }

    [Fact]
    public void StringsWithEscapes()
    {
        var t = Lex("'it$'s' + 'a$$b'");
        Assert.Equal(StTokenKind.String, t[0].Kind);
        Assert.Equal(7, t[0].Length);
        Assert.Equal(StTokenKind.Plus, t[1].Kind);
        Assert.Equal(StTokenKind.String, t[2].Kind);
    }

    [Fact]
    public void UnterminatedStringReportsDiagnostic()
    {
        var bag = new DiagnosticBag();
        var t = Lex("x := 'abc;", bag);
        Assert.Contains(bag, d => d.Code == "ST000");
        Assert.Contains(t, x => x.Kind == StTokenKind.Unknown);
    }

    [Fact]
    public void Operators()
    {
        Assert.Equal(new[] { "Identifier", "Power", "Integer", "NotEqual", "Identifier", "LessOrEqual", "Identifier", "GreaterOrEqual", "Identifier", "Ampersand", "Identifier", "EndOfFile" },
            Kinds("a ** 2 <> b <= c >= d & e"));
    }

    [Fact]
    public void ProgramScopedBackslash()
    {
        Assert.Equal(new[] { "Backslash", "Identifier", "Dot", "Identifier", "EndOfFile" }, Kinds("\\Prog.Tag"));
    }

    [Fact]
    public void LineMapReportsL5xLineNumbers()
    {
        var lines = new List<L5x.Model.Logic.StLine> { new(10, "a := 1;"), new(11, "  b := 2;") };
        var map = StLineMap.Build(lines, out var text);
        var tokens = new StLexer(text, map, new DiagnosticBag()).Tokenize();
        var b = tokens.First(t => t.Kind == StTokenKind.Identifier && text.Substring(t.Start, t.Length) == "b");
        Assert.Equal(11, b.Line);
        Assert.Equal(3, b.Column);
    }
}
