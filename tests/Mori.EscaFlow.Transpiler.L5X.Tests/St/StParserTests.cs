using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Parsing.St;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.St;

public class StParserTests
{
    private static StBlock Parse(string text, out DiagnosticBag bag, LogicParseContext? ctx = null)
    {
        bag = new DiagnosticBag();
        return StParser.ParseBlock(text, ctx ?? LogicParseContext.Empty, bag);
    }

    private static StBlock ParseClean(string text)
    {
        var block = Parse(text, out var bag);
        Assert.Empty(bag);
        return block;
    }

    private static StExpression Expr(string text)
    {
        var bag = new DiagnosticBag();
        var e = StParser.ParseExpression(text, LogicParseContext.Empty, bag);
        Assert.Empty(bag);
        return e;
    }

    [Fact]
    public void SimpleAssignment()
    {
        var b = ParseClean("Tag1 := Tag2 + 1;");
        var a = Assert.IsType<StAssignment>(Assert.Single(b.Statements));
        Assert.Equal("Tag1", a.Target.Path.Root);
        Assert.False(a.NonRetentive);
        var bin = Assert.IsType<StBinary>(a.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
    }

    [Fact]
    public void NonRetentiveAssignment()
    {
        var b = ParseClean("PFB.Conv.Cmd.InitRequest [:=] 1;");
        var a = Assert.IsType<StAssignment>(Assert.Single(b.Statements));
        Assert.True(a.NonRetentive);
        Assert.Equal("PFB.Conv.Cmd.InitRequest", a.Target.Path.ToLogixText());
    }

    [Fact]
    public void MultipleStatementsOnOneLineAndEmptyStatements()
    {
        var b = ParseClean("a := 1; b := 2;; c := 3;");
        Assert.Equal(4, b.Statements.Count);
        Assert.IsType<StEmptyStatement>(b.Statements[2]);
    }

    [Fact]
    public void IfElsifElse()
    {
        var b = ParseClean("IF a > 1 THEN x := 1; ELSIF a > 0 THEN x := 2; ELSE x := 3; END_IF;");
        var s = Assert.IsType<StIf>(Assert.Single(b.Statements));
        Assert.Equal(2, s.Branches.Count);
        Assert.NotNull(s.Else);
        Assert.Single(s.Else!.Statements);
    }

    [Fact]
    public void NestedIfWithoutSemicolonAfterEndIf()
    {
        var b = ParseClean("IF a THEN IF b THEN c := 1; END_IF END_IF");
        var outer = Assert.IsType<StIf>(Assert.Single(b.Statements));
        Assert.IsType<StIf>(Assert.Single(outer.Branches[0].Body.Statements));
    }

    [Fact]
    public void CaseWithListsRangesAndNegatives()
    {
        var b = ParseClean("CASE sel OF 0 : x := 0; 1, 2 : x := 1; 3..5 : x := 2; -1 : x := -1; ELSE x := 9; END_CASE;");
        var c = Assert.IsType<StCase>(Assert.Single(b.Statements));
        Assert.Equal(4, c.Arms.Count);
        Assert.Equal(2, c.Arms[1].Labels.Count);
        Assert.True(c.Arms[2].Labels[0].IsRange);
        Assert.Equal(3, c.Arms[2].Labels[0].From);
        Assert.Equal(5, c.Arms[2].Labels[0].To);
        Assert.Equal(-1, c.Arms[3].Labels[0].From);
        Assert.NotNull(c.Else);
    }

    [Fact]
    public void CaseArmWithMultipleStatements()
    {
        var b = ParseClean("CASE i OF\n0 : a := 1;\n    b := 2;\n1 : c := 3;\nEND_CASE;");
        var c = Assert.IsType<StCase>(Assert.Single(b.Statements));
        Assert.Equal(2, c.Arms[0].Body.Statements.Count);
        Assert.Single(c.Arms[1].Body.Statements);
    }

    [Fact]
    public void ForWithAndWithoutBy()
    {
        var b = ParseClean("FOR i := 0 TO 9 DO x[i] := 0; END_FOR; FOR j := 10 TO 0 BY -2 DO EXIT; END_FOR;");
        Assert.Equal(2, b.Statements.Count);
        var f1 = Assert.IsType<StFor>(b.Statements[0]);
        Assert.Null(f1.By);
        var f2 = Assert.IsType<StFor>(b.Statements[1]);
        Assert.NotNull(f2.By);
        Assert.IsType<StExit>(Assert.Single(f2.Body.Statements));
    }

    [Fact]
    public void WhileAndRepeat()
    {
        var b = ParseClean("WHILE a < 10 DO a := a + 1; END_WHILE; REPEAT a := a - 1; UNTIL a = 0 END_REPEAT;");
        Assert.IsType<StWhile>(b.Statements[0]);
        var r = Assert.IsType<StRepeat>(b.Statements[1]);
        Assert.IsType<StBinary>(r.Until);
    }

    [Fact]
    public void CallStatementWithEmptyArgumentsAndAoi()
    {
        var ctx = new LogicParseContext(new[] { "MyAoi" });
        var b = Parse("TONR(t); MyAoi(a,,b); JSR(Sub);", out var bag, ctx);
        Assert.Empty(bag);
        var c1 = Assert.IsType<StCallStatement>(b.Statements[0]);
        Assert.False(c1.IsAddOn);
        var c2 = Assert.IsType<StCallStatement>(b.Statements[1]);
        Assert.True(c2.IsAddOn);
        Assert.Equal(3, c2.Arguments.Count);
        Assert.Null(c2.Arguments[1]);
    }

    [Fact]
    public void ReturnStatement()
    {
        var b = ParseClean("RETURN;");
        Assert.IsType<StReturn>(Assert.Single(b.Statements));
    }

    [Theory]
    [InlineData("-2**2", "-(2 ** 2)")]
    [InlineData("a OR b AND c", "a OR (b AND c)")]
    [InlineData("NOT a = b", "NOT a = b")] // NOT binds tighter than '=' : (NOT a) = b
    [InlineData("a + b * c MOD d", "a + ((b * c) MOD d)")]
    [InlineData("a & b", "a AND b")]
    [InlineData("a < b = c", "(a < b) = c")]
    [InlineData("(a + b) * c", "(a + b) * c")]
    [InlineData("a - b - c", "(a - b) - c")]
    [InlineData("2 ** 3 ** 2", "2 ** (3 ** 2)")]
    [InlineData("a XOR b OR c", "(a XOR b) OR c")]
    public void Precedence(string input, string expectedTree)
    {
        var e = Expr(input);
        Assert.Equal(expectedTree, Dump(e));
    }

    [Fact]
    public void NotVersusComparison()
    {
        var e = Expr("NOT a = b");
        var bin = Assert.IsType<StBinary>(e);
        Assert.Equal(BinaryOp.Equal, bin.Op);
        Assert.IsType<StUnary>(bin.Left);
    }

    [Fact]
    public void FunctionCallInExpression()
    {
        var e = Expr("ABS(x - y) + SQRT(z)");
        var bin = Assert.IsType<StBinary>(e);
        var call = Assert.IsType<StCall>(bin.Left);
        Assert.Equal("ABS", call.Name);
        Assert.IsType<StBinary>(call.Arguments[0]);
    }

    [Fact]
    public void VariablePaths()
    {
        var e = Expr("g_Table.Data[i + 1, 2].Sub.Bits[Idx[0]].5");
        var v = Assert.IsType<StVariable>(e);
        Assert.Equal("g_Table", v.Path.Root);
        Assert.Equal(6, v.Path.Segments.Count);
        Assert.IsType<MemberSegment>(v.Path.Segments[0]);
        var idx = Assert.IsType<IndexSegment>(v.Path.Segments[1]);
        Assert.Equal(2, idx.Indices.Count);
        Assert.IsType<BitSegment>(v.Path.Segments[5]);
        Assert.Equal("g_Table.Data[i + 1,2].Sub.Bits[Idx[0]].5", v.Path.ToLogixText());
    }

    [Fact]
    public void ModuleAndProgramScopedPaths()
    {
        var p = StParser.TryParseOperandPath("Local:1:I.Data.3", LogicParseContext.Empty, new DiagnosticBag());
        Assert.NotNull(p);
        Assert.Equal("Local:1:I", p!.Root);
        Assert.IsType<BitSegment>(p.Segments[1]);

        var q = StParser.TryParseOperandPath("\\Prog.Tag.M", LogicParseContext.Empty, new DiagnosticBag());
        Assert.NotNull(q);
        Assert.Equal("Prog", q!.ProgramName);
        Assert.Equal("Tag", q.Root);

        Assert.Null(StParser.TryParseOperandPath("a + b", LogicParseContext.Empty, new DiagnosticBag()));
        Assert.Null(StParser.TryParseOperandPath("12", LogicParseContext.Empty, new DiagnosticBag()));
        Assert.Null(StParser.TryParseOperandPath("ABS(x)", LogicParseContext.Empty, new DiagnosticBag()));
        Assert.Null(StParser.TryParseOperandPath("", LogicParseContext.Empty, new DiagnosticBag()));
    }

    [Fact]
    public void RealFileSamples()
    {
        var b = ParseClean("""
            //IDV selection logic
            		FOR DimensionVariantIndex := 0 TO DimensionVariantIndexMax - 1 DO
            			CASE DimensionVariantIndex OF
            			0 : IDVTransformationIndex:= IDV0TransformationIndex;
            			1 : IDVTransformationIndex:= IDV1TransformationIndex;
            			END_CASE;
            			IDVHeightError := ABS(g_PalletDataTable.PalletData[PalletDataTableIndex].General.Pallet.ContainedLpns.Lpns[LPNIndex].Sku.TransformationsPerDimensions.TransformationsLists[DimensionVariantIndex].ScoredIdvTransformations[IDVTransformationIndex].ResultingIDV.Dimensions.Height - CIS.Vision.InComm.Data.OutsideRealHeight);
            		END_FOR;
            IF (Axis.IN_modeState AND 16#0F) <> 16#02 THEN(*comment*)
            	Axis.IN_driveState.14 := 1; // trailing
            END_IF;
            """);
        Assert.Equal(2, b.Statements.Count);
        Assert.IsType<StFor>(b.Statements[0]);
        Assert.IsType<StIf>(b.Statements[1]);
    }

    [Fact]
    public void ErrorRecoveryContinuesAfterBadStatement()
    {
        var b = Parse("a := 1; := 2; b := 3;", out var bag);
        Assert.NotEmpty(bag);
        Assert.Equal(3, b.Statements.Count);
        Assert.IsType<StAssignment>(b.Statements[0]);
        Assert.IsType<StErrorStatement>(b.Statements[1]);
        Assert.IsType<StAssignment>(b.Statements[2]);
    }

    [Fact]
    public void MissingEndIfReportsButReturns()
    {
        var b = Parse("IF a THEN b := 1;", out var bag);
        Assert.Contains(bag, d => d.Code == "ST001" && d.Message.Contains("END_IF"));
        Assert.IsType<StIf>(Assert.Single(b.Statements));
    }

    [Fact]
    public void StrayEndIfIsSkipped()
    {
        var b = Parse("a := 1; END_IF; b := 2;", out var bag);
        Assert.Contains(bag, d => d.Code == "ST002");
        Assert.Equal(4, b.Statements.Count); // assignment, error(END_IF), empty(;), assignment
        Assert.IsType<StAssignment>(b.Statements[3]);
    }

    [Fact]
    public void DiagnosticsCarryLineNumbers()
    {
        var lines = new List<Mori.EscaFlow.Transpiler.L5X.Model.Logic.StLine> { new(5, "a := 1;"), new(6, "b := ;"), new(7, "c := 2;") };
        var bag = new DiagnosticBag();
        StParser.ParseBlock(lines, LogicParseContext.Empty, bag);
        var d = Assert.Single(bag);
        Assert.Equal(6, d.Location.StLine);
        Assert.Equal(6, d.Location.Column);
    }

    [Fact]
    public void ExpressionEntryPointRejectsTrailingText()
    {
        var bag = new DiagnosticBag();
        var e = StParser.ParseExpression("a + b c", LogicParseContext.Empty, bag);
        Assert.IsType<StErrorExpression>(e);
        Assert.NotEmpty(bag);
    }

    [Fact]
    public void IndexList()
    {
        var list = StParser.ParseIndexList("i, j+1", LogicParseContext.Empty, new DiagnosticBag());
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void RoundTripText()
    {
        Assert.Equal("(a + b) * c", Expr("(a+b)*c").ToLogixText());
        Assert.Equal("a + b * c", Expr("a+b*c").ToLogixText());
        Assert.Equal("NOT (a AND b)", Expr("NOT (a AND b)").ToLogixText());
        Assert.Equal("x.Y[1].2", Expr("x.Y[1].2").ToLogixText());
        Assert.Equal("16#ff", Expr("16#FF").ToLogixText());
        Assert.Equal("'it$'s'", Expr("'it$'s'").ToLogixText());
    }

    private static string Dump(StExpression e) => e switch
    {
        StBinary b => $"{Wrap(b.Left)} {b.Op.ToLogixText()} {Wrap(b.Right)}",
        StUnary u => (u.Op == UnaryOp.Not ? "NOT " : "-") + Wrap(u.Operand),
        _ => e.ToLogixText(),
    };

    private static string Wrap(StExpression e) => e is StBinary ? "(" + Dump(e) + ")" : Dump(e);
}
