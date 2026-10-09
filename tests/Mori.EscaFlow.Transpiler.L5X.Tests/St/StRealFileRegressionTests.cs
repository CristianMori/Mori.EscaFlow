using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;
using Mori.EscaFlow.Transpiler.L5X.Parsing.St;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.St;

/// <summary>Constructs found in the sample exports that the first grammar draft did not cover.</summary>
public class StRealFileRegressionTests
{
    [Fact]
    public void RegionDirectivesAreTrivia()
    {
        var bag = new DiagnosticBag();
        var comments = new List<StComment>();
        var block = StParser.ParseBlock("#region Product Place Conveyor\n  a := 1;\n#endregion\n", LogicParseContext.Empty, bag, comments);
        Assert.Empty(bag);
        Assert.Single(block.Statements);
        Assert.Equal(2, comments.Count);
    }

    [Fact]
    public void RealWithoutLeadingDigit()
    {
        var bag = new DiagnosticBag();
        var block = StParser.ParseBlock("ParamVFD.DecelSpeedMMS := ParamVFD.NormalSpeedMMS *.30;", LogicParseContext.Empty, bag);
        Assert.Empty(bag);
        var a = Assert.IsType<StAssignment>(Assert.Single(block.Statements));
        var mul = Assert.IsType<StBinary>(a.Value);
        var lit = Assert.IsType<StLiteral>(mul.Right);
        Assert.Equal(0.30, ((RealLiteral)lit.Value).Value, 3);
    }

    [Fact]
    public void BitAfterIdentifierIsStillABit()
    {
        var bag = new DiagnosticBag();
        var e = StParser.ParseExpression("x.14 + y[1].3", LogicParseContext.Empty, bag);
        Assert.Empty(bag);
        var bin = Assert.IsType<StBinary>(e);
        Assert.IsType<BitSegment>(((StVariable)bin.Left).Path.Segments[0]);
    }

    [Fact]
    public void IndirectBitAddressing()
    {
        var bag = new DiagnosticBag();
        var block = StParser.ParseBlock("AndResult.[index]:= Conditions.[index] OR InternalBypass.[index + 1];", LogicParseContext.Empty, bag);
        Assert.Empty(bag);
        var a = Assert.IsType<StAssignment>(Assert.Single(block.Statements));
        var seg = Assert.IsType<DynamicBitSegment>(Assert.Single(a.Target.Path.Segments));
        Assert.IsType<StVariable>(seg.Bit);
        Assert.Equal("AndResult.[index]", a.Target.Path.ToLogixText());
    }

    [Fact]
    public void QnanWithSuffix()
    {
        Assert.True(Mori.EscaFlow.Transpiler.L5X.Parsing.Values.LiteralParser.TryParseNumber("1.#QNAN000e+000", out var v));
        Assert.True(double.IsNaN(((RealLiteral)v).Value));
        Assert.True(Mori.EscaFlow.Transpiler.L5X.Parsing.Values.LiteralParser.TryParseNumber("-1.#INF", out var inf));
        Assert.True(double.IsNegativeInfinity(((RealLiteral)inf).Value));
    }

    [Theory]
    [InlineData("Forward Only")]
    [InlineData("% of Maximum")]
    [InlineData("% of Time")]
    public void MultiWordKeywordOperands(string text)
    {
        var bag = new DiagnosticBag();
        var op = OperandParser.Classify("MAPC", 12, text, false, LogicParseContext.Empty, bag);
        Assert.Empty(bag);
        Assert.IsType<Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll.KeywordOperand>(op);
    }

    [Fact]
    public void SubtractionIsNotAKeyword()
    {
        var bag = new DiagnosticBag();
        var op = OperandParser.Classify("CPT", 1, "a - b", false, LogicParseContext.Empty, bag);
        Assert.Empty(bag);
        Assert.IsType<Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll.ExpressionOperand>(op);
    }
}
