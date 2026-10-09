using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Import;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Import.Tests;

/// <summary>
/// Targeted regression tests for <see cref="LadderLowering"/>'s rung-condition bookkeeping: once a one-shot (ONS)
/// has turned the running condition into an absolute expression (one that already embeds the rung's incoming
/// condition, rather than one still relative to it), every later element in the same series — including a
/// subsequent branch — must keep combining correctly. A self-contained, minimal project is used rather than the
/// shared fixtures elsewhere in this test suite, so the rung text and the expected tree are easy to read side by
/// side and unaffected by unrelated fixture changes.
/// </summary>
public class LadderLoweringTests
{
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T" ProcessorType="1756-L85E" MajorRev="33" MinorRev="12">
        <Tags>
        <Tag Name="X" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="A" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="B" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Out" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Word" TagType="Base" DataType="DINT" Radix="Decimal"/>
        </Tags>
        <Programs>
        <Program Name="P" MainRoutineName="Main">
        <Routines>
        <Routine Name="Main" Type="RLL">
        <RLLContent>
        <Rung Number="0" Type="N">
        <Text><![CDATA[XIC(X)ONS(Word.0)[XIC(A),XIC(B)]OTE(Out);]]></Text>
        </Rung>
        </RLLContent>
        </Routine>
        </Routines>
        </Program>
        </Programs>
        <Tasks>
        <Task Name="Main" Type="CONTINUOUS" Priority="10" Watchdog="500">
        <ScheduledPrograms><ScheduledProgram Name="P"/></ScheduledPrograms>
        </Task>
        </Tasks>
        </Controller>
        </RSLogix5000Content>
        """;

    /// <summary>
    /// The rung is <c>XIC(X) ONS(Word.0) [XIC(A), XIC(B)] OTE(Out)</c>: a one-shot turns the condition into an
    /// edge expression (which already embeds <c>X</c>, so it is "absolute" rather than relative to the rung's
    /// incoming condition — see <see cref="LadderLowering"/>'s private <c>relIsAbsolute</c> bookkeeping), and a
    /// branch follows it before the output. The correct result, regardless of that bookkeeping, is a single
    /// assignment <c>Out := edge(X) AND (A OR B)</c>: the branch's OR combines with the edge by a plain AND, the
    /// same way it would combine with any other relative condition. This is the shape that exposed a branch whose
    /// two code paths for "the running condition is already absolute" versus "still relative to the rung" had
    /// become textually identical; this test pins the externally observable behaviour so a future edit that makes
    /// them diverge again is caught here rather than only in a generated project.
    /// </summary>
    [Fact]
    public void EdgeThenBranchCombinesWithPlainAnd()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics.Where(d => d.Severity != Mori.EscaFlow.Transpiler.L5X.Diagnostics.DiagnosticSeverity.Info));
        var ir = new LogixIrBuilder(project).Build();

        var main = ir.FindPou("P")!.FindAction("Main")!.Body;
        var assign = Assert.IsType<IrAssign>(Assert.Single(main.Statements));
        Assert.Equal("global/T/Out", Assert.IsType<IrRef>(assign.Target).Root.Id);

        var and = Assert.IsType<IrBinary>(assign.Value);
        Assert.Equal("and", and.Operator);

        // Left operand: the one-shot's edge, clocked by X (the condition at the point ONS appears).
        var edge = Assert.IsType<IrEdge>(and.Left);
        Assert.Equal("rising", edge.Kind);
        Assert.Equal("global/T/X", Assert.IsType<IrRef>(edge.Clk).Root.Id);

        // Right operand: the branch's OR of its two legs, ANDed in exactly like any other relative condition.
        var or = Assert.IsType<IrBinary>(and.Right);
        Assert.Equal("or", or.Operator);
        Assert.Equal("global/T/A", Assert.IsType<IrRef>(or.Left).Root.Id);
        Assert.Equal("global/T/B", Assert.IsType<IrRef>(or.Right).Root.Id);
    }
}
