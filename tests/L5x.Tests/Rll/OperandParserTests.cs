using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Logic.Rll;
using L5x.Parsing;
using L5x.Parsing.Rll;
using Xunit;

namespace L5x.Tests.Rll;

public class OperandParserTests
{
    private static Operand Classify(string text, string mnemonic = "MOV", int index = 0, LogicParseContext? ctx = null, DiagnosticBag? diag = null)
        => OperandParser.Classify(mnemonic, index, text, isAddOn: false, ctx ?? LogicParseContext.Empty, diag ?? new DiagnosticBag());

    // ---- literals, placeholders, keywords: independent of the ST parser ----

    [Theory]
    [InlineData("-1", -1L, 10)]
    [InlineData("42", 42L, 10)]
    [InlineData("+7", 7L, 10)]
    [InlineData("16#FF_FF", 65535L, 16)]
    [InlineData("2#1010", 10L, 2)]
    [InlineData("8#17", 15L, 8)]
    public void IntegerLiterals(string text, long value, int radix)
    {
        var lit = Assert.IsType<LiteralOperand>(Classify(text));
        var i = Assert.IsType<IntegerLiteral>(lit.Value);
        Assert.Equal(value, i.Value);
        Assert.Equal(radix, i.RadixBase);
        Assert.Equal(text, lit.Text);
    }

    [Theory]
    [InlineData("1.5e-3", 0.0015)]
    [InlineData("2.0", 2.0)]
    [InlineData("-0.5", -0.5)]
    [InlineData("0.00000000e+000", 0.0)]
    public void RealLiterals(string text, double value)
    {
        var lit = Assert.IsType<LiteralOperand>(Classify(text));
        Assert.Equal(value, Assert.IsType<RealLiteral>(lit.Value).Value, 12);
    }

    [Fact]
    public void NanAndInfinity()
    {
        Assert.True(double.IsNaN(Assert.IsType<RealLiteral>(Assert.IsType<LiteralOperand>(Classify("1.#QNAN")).Value).Value));
        Assert.True(double.IsPositiveInfinity(Assert.IsType<RealLiteral>(Assert.IsType<LiteralOperand>(Classify("1.#INF")).Value).Value));
        Assert.True(double.IsNegativeInfinity(Assert.IsType<RealLiteral>(Assert.IsType<LiteralOperand>(Classify("-1.#INF")).Value).Value));
    }

    [Fact]
    public void StringLiteral_DecodesEscapes()
    {
        var lit = Assert.IsType<LiteralOperand>(Classify("'a$'b$$c$0D'"));
        Assert.Equal("a'b$c\r", Assert.IsType<StringLiteral>(lit.Value).Value);
    }

    [Fact]
    public void UnterminatedString_IsRaw()
    {
        var diag = new DiagnosticBag();
        Assert.IsType<RawOperand>(Classify("'abc", diag: diag));
        Assert.Contains(diag, d => d.Code == "RLL002");
    }

    [Fact]
    public void PlaceholdersAndEmpty()
    {
        Assert.Same(PlaceholderOperand.Single, Classify("?"));
        Assert.Same(PlaceholderOperand.Double, Classify("??"));
        Assert.Same(EmptyOperand.Instance, Classify(""));
        Assert.Same(EmptyOperand.Instance, Classify("   "));
    }

    [Theory]
    [InlineData("GSV", 0, "WallClockTime")]
    [InlineData("GSV", 2, "DateTime")]
    [InlineData("SSV", 0, "Program")]
    [InlineData("SWPB", 1, "REVERSE")]
    [InlineData("FSC", 3, "ALL")]
    [InlineData("FAL", 3, "INC")]
    [InlineData("SFP", 1, "Paused")]
    public void KeywordRoles(string mnemonic, int index, string text)
    {
        Assert.IsType<KeywordOperand>(Classify(text, mnemonic, index));
    }

    [Theory]
    [InlineData("JSR", 0)]
    [InlineData("FOR", 0)]
    public void RoutineRoles(string mnemonic, int index)
    {
        var op = Assert.IsType<RoutineRefOperand>(Classify("MySub", mnemonic, index));
        Assert.Equal("MySub", op.RoutineName);
    }

    [Theory]
    [InlineData("JMP")]
    [InlineData("LBL")]
    public void LabelRoles(string mnemonic)
    {
        Assert.Equal("L1", Assert.IsType<LabelOperand>(Classify("L1", mnemonic, 0)).Label);
    }

    [Fact]
    public void ModeOperand_NumberBeatsKeyword()
    {
        Assert.IsType<LiteralOperand>(Classify("2", "FSC", 3));
    }

    [Fact]
    public void Signatures_TableIsLoaded()
    {
        Assert.True(InstructionSignatures.IsKnown("XIC"));
        Assert.True(InstructionSignatures.IsKnown("xic"));
        Assert.True(InstructionSignatures.IsKnown("MAM"));
        Assert.True(InstructionSignatures.IsKnown("DCSRT"));
        Assert.False(InstructionSignatures.IsKnown("NOT_AN_INSTRUCTION"));
        Assert.Equal(OperandRole.Expression, InstructionSignatures.RoleAt("CPT", 1));
        Assert.Equal(OperandRole.Expression, InstructionSignatures.RoleAt("CMP", 0));
        Assert.Equal(OperandRole.Tag, InstructionSignatures.RoleAt("JSR", 5));   // "..." repeat
        Assert.Equal(OperandRole.Tag, InstructionSignatures.RoleAt("XIC", 7));   // beyond table
        Assert.Equal(OperandRole.Tag, InstructionSignatures.RoleAt("NOPE", 0));  // unknown
        Assert.Equal(0, InstructionSignatures.OperandCount("AFI"));
        Assert.True(InstructionSignatures.Mnemonics.Count > 200);
    }

    // ---- tag paths: depend on the ST parser ----

    [Fact]
    public void ModuleTagPath()
    {
        var op = Assert.IsType<TagRefOperand>(Classify("Local:1:I.Data.3"));
        Assert.Equal("Local:1:I", op.Path.Root);
        Assert.Equal(2, op.Path.Segments.Count);
        Assert.Equal("Data", Assert.IsType<MemberSegment>(op.Path.Segments[0]).Name);
        Assert.Equal(3, Assert.IsType<BitSegment>(op.Path.Segments[1]).Bit);
    }

    [Fact]
    public void SystemTagPath()
    {
        var op = Assert.IsType<TagRefOperand>(Classify("S:FS", "XIC"));
        Assert.Equal("S:FS", op.Path.Root);
        Assert.True(op.Path.IsBare);
    }

    [Fact]
    public void ProgramScopedPath()
    {
        var op = Assert.IsType<TagRefOperand>(Classify(@"\Prog.Tag"));
        Assert.Equal("Prog", op.Path.ProgramName);
        Assert.Equal("Tag", op.Path.Root);
        Assert.Equal(@"\Prog.Tag", op.Path.ToLogixText());
    }

    [Fact]
    public void MultiDimIndexMemberBit()
    {
        var op = Assert.IsType<TagRefOperand>(Classify("T[1,2].M.5"));
        Assert.Equal("T", op.Path.Root);
        Assert.Equal(3, op.Path.Segments.Count);
        var idx = Assert.IsType<IndexSegment>(op.Path.Segments[0]);
        Assert.Equal(new[] { 1, 2 }, idx.ConstantIndices());
        Assert.Equal("M", Assert.IsType<MemberSegment>(op.Path.Segments[1]).Name);
        Assert.Equal(5, Assert.IsType<BitSegment>(op.Path.Segments[2]).Bit);
        Assert.Equal("T[1,2].M.5", op.Path.ToLogixText());
    }

    [Fact]
    public void IndirectIndex_IsExpression()
    {
        var op = Assert.IsType<TagRefOperand>(Classify("Arr[Idx[2],3]"));
        var idx = Assert.IsType<IndexSegment>(Assert.Single(op.Path.Segments));
        Assert.False(idx.IsConstant);
        Assert.Equal(2, idx.Indices.Count);
    }

    [Fact]
    public void ExpressionRole_ParsesExpression()
    {
        var op = Assert.IsType<ExpressionOperand>(Classify("(a+b)*2", "CPT", 1));
        Assert.Equal("(a+b)*2", op.Text);
    }

    [Fact]
    public void NonPathTagRole_FallsBackToExpression()
    {
        // A comparison in a tag position (e.g. an odd export) still parses as an expression.
        Assert.IsType<ExpressionOperand>(Classify("a+1", "MOV", 0));
    }

    [Fact]
    public void Unparseable_IsRawWithWarning()
    {
        var diag = new DiagnosticBag();
        Assert.IsType<RawOperand>(Classify("a +* b", "MOV", 0, diag: diag));
        Assert.Contains(diag, d => d.Code == "RLL002");
    }

    [Fact]
    public void KeywordRole_WithNonIdentifier_FallsBackToPath()
    {
        Assert.IsType<TagRefOperand>(Classify("Local:1:I", "GSV", 0));
    }
}
