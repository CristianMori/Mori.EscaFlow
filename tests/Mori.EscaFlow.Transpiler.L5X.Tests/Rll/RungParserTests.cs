using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Rll;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Rll;

public class RungParserTests
{
    private static readonly LogicParseContext NoAoi = LogicParseContext.Empty;

    private static (RungAst Ast, DiagnosticBag Diag) Parse(string text, LogicParseContext? ctx = null)
    {
        var diag = new DiagnosticBag();
        var ast = new RungParser(ctx ?? NoAoi).Parse(text, diag);
        return (ast, diag);
    }

    private static InstructionElement Instr(RungElement e) => Assert.IsType<InstructionElement>(e);
    private static BranchElement Branch(RungElement e) => Assert.IsType<BranchElement>(e);

    private static void AssertRoundTrip(string text, LogicParseContext? ctx = null)
    {
        var (ast, _) = Parse(text, ctx);
        var written = RungTextWriter.Write(ast);
        Assert.Equal(RungTextWriter.Normalize(text.TrimEnd(';') + ";"), RungTextWriter.Normalize(written));
    }

    private static int RllDiagnostics(DiagnosticBag diag) => diag.Count(d => d.Code.StartsWith("RLL", StringComparison.Ordinal));

    // ---- structure only: no tag operands, so these do not depend on the ST parser ----

    [Fact]
    public void EmptyText_YieldsEmptyRung()
    {
        var (ast, diag) = Parse("   ");
        Assert.Empty(ast.Elements);
        Assert.Empty(diag);
    }

    [Fact]
    public void ZeroArgumentInstructions()
    {
        var (ast, diag) = Parse("AFI()NOP()BRK();");
        Assert.Equal(3, ast.Elements.Count);
        Assert.Equal(new[] { "AFI", "NOP", "BRK" }, ast.Elements.Select(e => Instr(e).Mnemonic));
        Assert.All(ast.Elements, e => Assert.Empty(Instr(e).Arguments));
        Assert.Empty(diag);
        AssertRoundTrip("AFI()NOP()BRK();");
    }

    [Fact]
    public void Offsets_AreRecorded()
    {
        var (ast, _) = Parse("AFI() [NOP() ,BRK() ]NOP();");
        Assert.Equal(0, ast.Elements[0].Offset);
        Assert.Equal(6, ast.Elements[1].Offset);
        var branch = Branch(ast.Elements[1]);
        Assert.Equal(7, branch.Legs[0][0].Offset);
        Assert.Equal(14, branch.Legs[1][0].Offset);
        Assert.Equal(21, ast.Elements[2].Offset);
    }

    [Fact]
    public void LiteralOperands_AreClassifiedWithoutStParser()
    {
        var (ast, diag) = Parse("MOV(1,?)MOV(-2.5,??)MOV(16#FF,'abc');");
        var mov1 = Instr(ast.Elements[0]);
        Assert.Equal(new IntegerLiteral(1), Assert.IsType<LiteralOperand>(mov1.Arguments[0]).Value);
        Assert.Same(PlaceholderOperand.Single, mov1.Arguments[1]);
        var mov2 = Instr(ast.Elements[1]);
        Assert.Equal(new RealLiteral(-2.5), Assert.IsType<LiteralOperand>(mov2.Arguments[0]).Value);
        Assert.Same(PlaceholderOperand.Double, mov2.Arguments[1]);
        var mov3 = Instr(ast.Elements[2]);
        Assert.Equal(new IntegerLiteral(255, 16), Assert.IsType<LiteralOperand>(mov3.Arguments[0]).Value);
        Assert.Equal(new StringLiteral("abc"), Assert.IsType<LiteralOperand>(mov3.Arguments[1]).Value);
        Assert.Empty(diag);
    }

    [Fact]
    public void EmptyArgument_BetweenCommas()
    {
        var (ast, diag) = Parse("MOV(1,,2);");
        var mov = Instr(ast.Elements[0]);
        Assert.Equal(3, mov.Arguments.Count);
        Assert.Same(EmptyOperand.Instance, mov.Arguments[1]);
        Assert.Empty(diag);
        AssertRoundTrip("MOV(1,,2);");
    }

    [Fact]
    public void TrailingEmptyArgument_IsKept()
    {
        var (ast, _) = Parse("MOV(1,);");
        Assert.Equal(2, Instr(ast.Elements[0]).Arguments.Count);
        Assert.Same(EmptyOperand.Instance, Instr(ast.Elements[0]).Arguments[1]);
    }

    [Fact]
    public void StringOperand_WithEscapedQuoteAndComma()
    {
        var (ast, diag) = Parse("MOV('it$'s, ok',1);");
        var mov = Instr(ast.Elements[0]);
        Assert.Equal(2, mov.Arguments.Count);
        Assert.Equal(new StringLiteral("it's, ok"), Assert.IsType<LiteralOperand>(mov.Arguments[0]).Value);
        Assert.Empty(diag);
        AssertRoundTrip("MOV('it$'s, ok',1);");
    }

    [Fact]
    public void Branch_WithEmptyLegs()
    {
        var (ast, diag) = Parse("[,NOP()]NOP();");
        var b = Branch(ast.Elements[0]);
        Assert.Equal(2, b.Legs.Count);
        Assert.Empty(b.Legs[0]);
        Assert.Single(b.Legs[1]);
        Assert.Empty(diag);

        (ast, diag) = Parse("[NOP(),]");
        b = Branch(ast.Elements[0]);
        Assert.Equal(2, b.Legs.Count);
        Assert.Single(b.Legs[0]);
        Assert.Empty(b.Legs[1]);
        Assert.Empty(diag);

        (ast, diag) = Parse("[,]");
        b = Branch(ast.Elements[0]);
        Assert.Equal(2, b.Legs.Count);
        Assert.All(b.Legs, l => Assert.Empty(l));
        Assert.Empty(diag);

        AssertRoundTrip("[,NOP()]NOP();");
        AssertRoundTrip("[NOP(),]");
        AssertRoundTrip("[,]");
    }

    [Fact]
    public void SequentialBranchGroups()
    {
        var (ast, diag) = Parse("[MOV(1,?),MOV(2,?)][MOV(3,?),MOV(4,?)];");
        Assert.Equal(2, ast.Elements.Count);
        Assert.Equal(2, Branch(ast.Elements[0]).Legs.Count);
        Assert.Equal(2, Branch(ast.Elements[1]).Legs.Count);
        Assert.Empty(diag);
        AssertRoundTrip("[MOV(1,?),MOV(2,?)][MOV(3,?),MOV(4,?)];");
    }

    [Fact]
    public void NestedBranches_StructureOnly()
    {
        var (ast, diag) = Parse("[[MOV(1,?) ,MOV(2,?) ] MOV(3,?) ,MOV(4,?) ];");
        var outer = Branch(ast.Elements[0]);
        Assert.Equal(2, outer.Legs.Count);
        Assert.Equal(2, outer.Legs[0].Count);
        var inner = Branch(outer.Legs[0][0]);
        Assert.Equal(2, inner.Legs.Count);
        Assert.Equal("MOV", Instr(outer.Legs[0][1]).Mnemonic);
        Assert.Equal("MOV", Instr(outer.Legs[1][0]).Mnemonic);
        Assert.Empty(diag);
        AssertRoundTrip("[[MOV(1,?) ,MOV(2,?) ] MOV(3,?) ,MOV(4,?) ];");
    }

    [Fact]
    public void SubscriptBrackets_InsideArguments_AreNotBranches()
    {
        // Structure check only; operands are examined in the ST-dependent tests below.
        var (ast, _) = Parse("MOV(Arr[i],Arr[Idx[2],3]);");
        Assert.Single(ast.Elements);
        var mov = Instr(ast.Elements[0]);
        Assert.Equal(2, mov.Arguments.Count);
        Assert.Equal("Arr[i]", mov.Arguments[0].Text);
        Assert.Equal("Arr[Idx[2],3]", mov.Arguments[1].Text);
    }

    [Fact]
    public void ExpressionCommas_InsideParens_DoNotSplitArguments()
    {
        var (ast, _) = Parse("CPT(x,(a+b)*ABS(c,d));");
        var cpt = Instr(ast.Elements[0]);
        Assert.Equal(2, cpt.Arguments.Count);
        Assert.Equal("(a+b)*ABS(c,d)", cpt.Arguments[1].Text);
    }

    [Fact]
    public void GarbageRecovery_KeepsBothInstructions()
    {
        var (ast, diag) = Parse("MOV(1,?) ## MOV(2,?);");
        Assert.Equal(2, ast.Elements.Count);
        Assert.Contains(diag, d => d.Code == "RLL003");
    }

    [Fact]
    public void MnemonicWithoutParen_IsReported()
    {
        var (ast, diag) = Parse("XIC MOV(1,?);");
        Assert.Single(ast.Elements);
        Assert.Contains(diag, d => d.Code == "RLL003");
    }

    [Fact]
    public void UnterminatedInstruction_IsReported()
    {
        var (ast, diag) = Parse("MOV(1,?");
        Assert.Single(ast.Elements);
        Assert.Equal(2, Instr(ast.Elements[0]).Arguments.Count);
        Assert.Contains(diag, d => d.Code == "RLL004");
    }

    [Fact]
    public void UnterminatedBranch_IsReported()
    {
        var (ast, diag) = Parse("[MOV(1,?),MOV(2,?)");
        Assert.Single(ast.Elements);
        Assert.Equal(2, Branch(ast.Elements[0]).Legs.Count);
        Assert.Contains(diag, d => d.Code == "RLL004");
    }

    [Fact]
    public void UnterminatedString_IsReported()
    {
        var (ast, diag) = Parse("MOV('abc,1);");
        Assert.Single(ast.Elements);
        Assert.IsType<RawOperand>(Instr(ast.Elements[0]).Arguments[0]);
        Assert.Contains(diag, d => d.Code == "RLL004" || d.Code == "RLL002");
    }

    [Fact]
    public void TextAfterTerminator_IsReported()
    {
        var (ast, diag) = Parse("NOP();NOP();");
        Assert.Equal(2, ast.Elements.Count);
        Assert.Contains(diag, d => d.Code == "RLL003");
    }

    [Fact]
    public void UnknownMnemonic_IsInfoOnly()
    {
        var (ast, diag) = Parse("FOOBAR(1,2)FOOBAR(3,4);");
        Assert.Equal(2, ast.Elements.Count);
        var unknown = diag.Where(d => d.Code == "RLL001").ToList();
        Assert.Single(unknown);
        Assert.Equal(DiagnosticSeverity.Info, unknown[0].Severity);
    }

    [Fact]
    public void AddOnInstruction_IsRecognized()
    {
        var ctx = new LogicParseContext(new[] { "ADD_ON_TOGGLE" });
        var (ast, diag) = Parse("ADD_ON_TOGGLE(1,2);", ctx);
        var instr = Instr(ast.Elements[0]);
        Assert.True(instr.IsAddOn);
        Assert.DoesNotContain(diag, d => d.Code == "RLL001");

        (ast, _) = Parse("ADD_ON_TOGGLE(1,2);");
        Assert.False(Instr(ast.Elements[0]).IsAddOn);
    }

    [Fact]
    public void KeywordOperands_FromSignature()
    {
        var (ast, diag) = Parse("SWPB(1,REVERSE,?)FSC(?,?,?,ALL,1=1)GSV(WallClockTime,,DateTime,?);");
        var swpb = Instr(ast.Elements[0]);
        Assert.IsType<KeywordOperand>(swpb.Arguments[1]);
        var fsc = Instr(ast.Elements[1]);
        Assert.IsType<KeywordOperand>(fsc.Arguments[3]);
        var gsv = Instr(ast.Elements[2]);
        Assert.IsType<KeywordOperand>(gsv.Arguments[0]);
        Assert.Same(EmptyOperand.Instance, gsv.Arguments[1]);
        Assert.IsType<KeywordOperand>(gsv.Arguments[2]);
        Assert.Equal(0, RllDiagnostics(diag));
    }

    [Fact]
    public void RoutineAndLabelOperands()
    {
        var (ast, diag) = Parse("JSR(Sub,0)JMP(L1)LBL(L1)FOR(Loop,?,0,10,1);");
        var jsr = Instr(ast.Elements[0]);
        Assert.Equal("Sub", Assert.IsType<RoutineRefOperand>(jsr.Arguments[0]).RoutineName);
        Assert.IsType<LiteralOperand>(jsr.Arguments[1]);
        Assert.Equal("L1", Assert.IsType<LabelOperand>(Instr(ast.Elements[1]).Arguments[0]).Label);
        Assert.Equal("L1", Assert.IsType<LabelOperand>(Instr(ast.Elements[2]).Arguments[0]).Label);
        Assert.IsType<RoutineRefOperand>(Instr(ast.Elements[3]).Arguments[0]);
        Assert.Equal(0, RllDiagnostics(diag));
        AssertRoundTrip("JSR(Sub,0)JMP(L1)LBL(L1)FOR(Loop,?,0,10,1);");
    }

    [Fact]
    public void Writer_ProducesExporterLayout()
    {
        var (ast, _) = Parse("NOP()[NOP(),[NOP(),NOP()]NOP()]NOP();");
        Assert.Equal("NOP()[NOP() ,[NOP() ,NOP() ] NOP() ]NOP();", RungTextWriter.Write(ast));
    }

    [Fact]
    public void Normalize_KeepsStringWhitespace()
    {
        Assert.Equal("MOV('a b',x);", RungTextWriter.Normalize(" MOV( 'a b' , x ) ; "));
        Assert.Equal("MOV('a$' b',x);", RungTextWriter.Normalize("MOV('a$' b', x);"));
    }

    // ---- these depend on the ST parser for tag paths and expressions ----

    [Fact]
    public void SingleInstruction_TagOperand()
    {
        var (ast, diag) = Parse("XIC(Start);");
        var xic = Instr(ast.Elements[0]);
        Assert.Equal("XIC", xic.Mnemonic);
        var tag = Assert.IsType<TagRefOperand>(xic.Arguments[0]);
        Assert.Equal("Start", tag.Path.Root);
        Assert.True(tag.Path.IsBare);
        Assert.Empty(diag);
        AssertRoundTrip("XIC(Start);");
    }

    [Fact]
    public void Concatenation_NoSeparators()
    {
        var (ast, diag) = Parse("XIC(input1)XIC(input2)OTE(output1)OTE(output2);");
        Assert.Equal(new[] { "XIC", "XIC", "OTE", "OTE" }, ast.Elements.Select(e => Instr(e).Mnemonic));
        Assert.Empty(diag);
        AssertRoundTrip("XIC(input1)XIC(input2)OTE(output1)OTE(output2);");
    }

    [Fact]
    public void SimpleBranch()
    {
        var (ast, diag) = Parse("XIC(a)[XIC(b) ,XIC(c) ]OTE(d);");
        Assert.Equal(3, ast.Elements.Count);
        var b = Branch(ast.Elements[1]);
        Assert.Equal(2, b.Legs.Count);
        Assert.Equal("b", Assert.IsType<TagRefOperand>(Instr(b.Legs[0][0]).Arguments[0]).Path.Root);
        Assert.Equal("c", Assert.IsType<TagRefOperand>(Instr(b.Legs[1][0]).Arguments[0]).Path.Root);
        Assert.Empty(diag);
        AssertRoundTrip("XIC(a)[XIC(b) ,XIC(c) ]OTE(d);");
    }

    [Fact]
    public void NestedBranches()
    {
        var text = "[[XIC(a) ,XIC(b) ] MOV(1,x) ,XIC(c) ];";
        var (ast, diag) = Parse(text);
        var outer = Branch(ast.Elements[0]);
        var inner = Branch(outer.Legs[0][0]);
        Assert.Equal("a", Assert.IsType<TagRefOperand>(Instr(inner.Legs[0][0]).Arguments[0]).Path.Root);
        Assert.Equal("x", Assert.IsType<TagRefOperand>(Instr(outer.Legs[0][1]).Arguments[1]).Path.Root);
        Assert.Empty(diag);
        AssertRoundTrip(text);
    }

    [Fact]
    public void EmptyLegs_WithTags()
    {
        AssertRoundTrip("[,XIC(a)]");
        AssertRoundTrip("[XIC(a),]");
        var (ast, diag) = Parse("XIC(conveyor_a)[,XIC(input_1) XIO(input_2) ]OTE(light_1);");
        var b = Branch(ast.Elements[1]);
        Assert.Empty(b.Legs[0]);
        Assert.Equal(2, b.Legs[1].Count);
        Assert.Empty(diag);
        AssertRoundTrip("XIC(conveyor_a)[,XIC(input_1) XIO(input_2) ]OTE(light_1);");
    }

    [Fact]
    public void Subscripts_ParseAsIndexSegments()
    {
        var (ast, diag) = Parse("MOV(Arr[i],Arr[Idx[2],3]);");
        var mov = Instr(ast.Elements[0]);
        var a0 = Assert.IsType<TagRefOperand>(mov.Arguments[0]);
        Assert.Equal("Arr", a0.Path.Root);
        var idx0 = Assert.IsType<IndexSegment>(Assert.Single(a0.Path.Segments));
        Assert.Single(idx0.Indices);
        var a1 = Assert.IsType<TagRefOperand>(mov.Arguments[1]);
        var idx1 = Assert.IsType<IndexSegment>(Assert.Single(a1.Path.Segments));
        Assert.Equal(2, idx1.Indices.Count);
        Assert.Empty(diag);
        AssertRoundTrip("MOV(Arr[i],Arr[Idx[2],3]);");
    }

    [Fact]
    public void ExpressionOperands()
    {
        var (ast, diag) = Parse("CPT(x,(a+b)*ABS(c))FSC(ctl,?,?,ALL,Data[ctl.POS]=0)CMP(a<>b);");
        Assert.IsType<ExpressionOperand>(Instr(ast.Elements[0]).Arguments[1]);
        var fsc = Instr(ast.Elements[1]);
        Assert.IsType<TagRefOperand>(fsc.Arguments[0]);
        Assert.IsType<KeywordOperand>(fsc.Arguments[3]);
        Assert.IsType<ExpressionOperand>(fsc.Arguments[4]);
        Assert.IsType<ExpressionOperand>(Instr(ast.Elements[2]).Arguments[0]);
        Assert.Empty(diag);
        AssertRoundTrip("CPT(x,(a+b)*ABS(c))FSC(ctl,?,?,ALL,Data[ctl.POS]=0)CMP(a<>b);");
    }

    [Fact]
    public void Gsv_EmptyArgumentAndDestinationPath()
    {
        var (ast, diag) = Parse("GSV(WallClockTime,,DateTime,Time_Logix.Year);");
        var gsv = Instr(ast.Elements[0]);
        Assert.Equal(4, gsv.Arguments.Count);
        var dst = Assert.IsType<TagRefOperand>(gsv.Arguments[3]);
        Assert.Equal("Year", Assert.IsType<MemberSegment>(Assert.Single(dst.Path.Segments)).Name);
        Assert.Empty(diag);
        AssertRoundTrip("GSV(WallClockTime,,DateTime,Time_Logix.Year);");
    }

    [Fact]
    public void AddOnCall_ArgumentsAreTags()
    {
        var ctx = new LogicParseContext(new[] { "AOI_TIME_ADD" });
        var text = "XIC(InActivateNotify)ONS(ONSBitwise.0)[AOI_TIME_ADD(AOMadeUpID,Transit.CaseId,InCurrentDateTime,1,CurrentPickfaceID) ,CPS(CurrentPickfaceID,InOut.PickfaceID,1) ,OTE(DataCopied) ];";
        var (ast, diag) = Parse(text, ctx);
        var branch = Branch(ast.Elements[2]);
        var aoi = Instr(branch.Legs[0][0]);
        Assert.True(aoi.IsAddOn);
        Assert.Equal(5, aoi.Arguments.Count);
        Assert.IsType<TagRefOperand>(aoi.Arguments[0]);
        Assert.IsType<LiteralOperand>(aoi.Arguments[3]);
        var ons = Instr(ast.Elements[1]);
        Assert.Equal(0, Assert.IsType<BitSegment>(Assert.Single(Assert.IsType<TagRefOperand>(ons.Arguments[0]).Path.Segments)).Bit);
        Assert.Empty(diag);
        AssertRoundTrip(text, ctx);
    }

    [Theory]
    [InlineData("EQU(0,State)GRT(Query_Interval_in_Min,0)[[XIC(DN) ,EQU(Query_Error_Retry_in_Min,0) ] MUL(60000,Query_Interval_in_Min,Interval_Timer.PRE) ,XIC(ER) NEQ(Query_Error_Retry_in_Min,0) MUL(60000,Query_Error_Retry_in_Min,Interval_Timer.PRE) ,TON(Interval_Timer,?,?) ,CPT(Next_Query_in_Min,(Interval_Timer.PRE-Interval_Timer.ACC)/60000) ];")]
    [InlineData("[XIO(EnableAlarm) ,XIC(Inhibith) ,XIC(Common.InhibitAlarms) ,XIC(Common.InhibitAlarmGroups.Bit[intGroupNo]) ]OTE(InhibitActive);")]
    [InlineData("[XIO(InhibitActive) [XIO(AlarmOnTrue) XIO(Trigger) ,XIC(AlarmOnTrue) XIC(Trigger) ] ,XIC(AlarmActive) XIO(intReset) ][MOV(Delay,alarmTimer.PRE) TON(alarmTimer,?,?) ,XIC(alarmTimer.DN) OTE(AlarmActive) ];")]
    [InlineData("XIC(AOInStartBlinker)[XIO(TimerBlinkerOff.DN) TON(TimerBlinkerOn,?,?) ,XIC(TimerBlinkerOn.DN) TON(TimerBlinkerOff,?,?) ];")]
    [InlineData("[LIM(0,AOParamDelayOnMsec,10000) MOV(AOParamDelayOnMsec,TimerBlinkerOn.PRE) ,[LES(AOParamDelayOnMsec,0) ,GRT(AOParamDelayOnMsec,10000) ] MOV(0,TimerBlinkerOn.PRE) ];")]
    [InlineData("XIC(a)XIC(b)[XIC(c) [XIC(d) FSC(searcher,?,?,ALL,ISDUWrite.Data[searcher.POS]=0) MOV(searcher.POS,idx) ,XIC(e) OTU(f) ] ,XIC(g) OTU(h) ]OTU(i);")]
    [InlineData("XIC(s:fs)MOV(0,Local:1:I.Data[3].5)SWPB(x,REVERSE,y)MSG(SKT_Create)JSR(A00_PARAMETERS,0);")]
    public void RealRungSamples_ParseCleanAndRoundTrip(string text)
    {
        var (ast, diag) = Parse(text);
        Assert.NotEmpty(ast.Elements);
        Assert.Empty(diag);
        Assert.All(ast.AllInstructions(), i => Assert.DoesNotContain(i.Arguments, a => a is RawOperand));
        AssertRoundTrip(text);
    }
}
