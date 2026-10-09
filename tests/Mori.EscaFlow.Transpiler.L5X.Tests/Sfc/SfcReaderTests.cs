using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Sfc;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Sfc;

public class SfcReaderTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <RSLogix5000Content SchemaRevision="1.0" SoftwareRevision="33.00" TargetName="T" TargetType="Controller" ContainsContext="false">
        <Controller Use="Target" Name="T" ProcessorType="1756-L85E" MajorRev="33" MinorRev="12">
        <DataTypes/>
        <Modules/>
        <AddOnInstructionDefinitions/>
        <Tags/>
        <Programs>
        <Program Name="P" TestEdits="false" MainRoutineName="Main" Disabled="false" UseAsFolder="false">
        <Tags/>
        <Routines>
        <Routine Name="Main" Type="SFC">
        <Description>
        <![CDATA[INIT MANAGER]]>
        </Description>
        <SFCContent SheetSize="Letter - 8.5 x 11 in" SheetOrientation="Landscape">
        <Step ID="0" X="300" Y="80" Operand="Step_001" HideDesc="false" DescX="360" DescY="60" DescWidth="0" InitialStep="true" PresetUsesExpr="true" LimitHighUsesExpr="false"
         LimitLowUsesExpr="false" ShowActions="true">
        <Preset>
        <STContent>
        <Line Number="0">
        <![CDATA[Delay * 2]]>
        </Line>
        </STContent>
        </Preset>
        </Step>
        <Step ID="1" X="300" Y="280" Operand="Step_002" HideDesc="false" DescX="360" DescY="260" DescWidth="0" InitialStep="false" PresetUsesExpr="false" LimitHighUsesExpr="false" LimitLowUsesExpr="false" ShowActions="true">
        <Action ID="2" Operand="Action_000" Qualifier="NonStored" IsBoolean="false" PresetUsesExpr="false">
        <Body>
        <STContent>
        <Line Number="0">
        <![CDATA[PFB.Conv.Cmd.InitRequest [:=] 1;]]>
        </Line>
        <Line Number="1">
        <![CDATA[JSR(Sub);]]>
        </Line>
        </STContent>
        </Body>
        </Action>
        </Step>
        <Transition ID="11" X="300" Y="200" Operand="Tran_000" HideDesc="false" DescX="420" DescY="180" DescWidth="0">
        <Condition>
        <STContent>
        <Line Number="0">
        <![CDATA[OpMode.InitReq AND NOT Fault]]>
        </Line>
        </STContent>
        </Condition>
        </Transition>
        <Branch ID="18" Y="152" BranchType="Selection" BranchFlow="Diverge" Priority="Default">
        <Leg ID="19"/>
        <Leg ID="20"/>
        </Branch>
        <Stop ID="24" X="300" Y="1100" Operand="Stop_000" HideDesc="false" DescX="420" DescY="1080" DescWidth="0"/>
        <DirectedLink FromID="0" ToID="18" Show="true"/>
        <DirectedLink FromID="19" ToID="11" Show="true"/>
        <DirectedLink FromID="11" ToID="1" Show="true"/>
        <DirectedLink FromID="1" ToID="24" Show="true"/>
        <TextBox ID="30" X="10" Y="10" Width="0">
        <![CDATA[note]]>
        </TextBox>
        <Attachment FromID="30" ToID="0"/>
        </SFCContent>
        </Routine>
        <Routine Name="Sub" Type="ST">
        <STContent>
        <Line Number="0">
        <![CDATA[x := 1;]]>
        </Line>
        </STContent>
        </Routine>
        </Routines>
        </Program>
        </Programs>
        <Tasks/>
        </Controller>
        </RSLogix5000Content>
        """;

    [Fact]
    public void ReadsAndLinksTheChart()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics);
        var routine = project.Controller.Programs[0].Routines[0];
        Assert.Equal(RoutineType.SFC, routine.Type);
        Assert.Equal("INIT MANAGER", routine.Description);
        var sfc = Assert.IsType<SfcContent>(routine.Content);

        Assert.Equal(6, sfc.Nodes.Count); // 2 steps, transition, branch, stop, textbox
        Assert.Equal(2, sfc.Steps.Count());
        var initial = sfc.InitialStep;
        Assert.NotNull(initial);
        Assert.Equal("Step_001", initial!.Operand);
        Assert.NotNull(initial.Preset?.Expression);
        Assert.IsType<StBinary>(initial.Preset!.Expression);

        var step2 = sfc.Steps.Last();
        var action = Assert.Single(step2.Actions);
        Assert.Equal("NonStored", action.Qualifier);
        Assert.NotNull(action.Body?.Ast);
        var assign = Assert.IsType<StAssignment>(action.Body!.Ast!.Statements[0]);
        Assert.True(assign.NonRetentive);

        var tr = Assert.Single(sfc.Transitions);
        Assert.NotNull(tr.Condition?.Expression);
        Assert.IsType<StBinary>(tr.Condition!.Expression);

        var branch = Assert.Single(sfc.Branches);
        Assert.Equal(SfcBranchType.Selection, branch.BranchType);
        Assert.Equal(SfcBranchFlow.Diverge, branch.BranchFlow);
        Assert.Equal(2, branch.Legs.Count);

        Assert.Equal(4, sfc.Links.Count);
        Assert.All(sfc.Links, l => Assert.True(l.From is not null && l.To is not null));
        Assert.IsType<SfcLeg>(sfc.Links[1].From);
        Assert.IsType<SfcStop>(sfc.Links[3].To);
        Assert.Single(sfc.Attachments);
        Assert.IsType<SfcTextBox>(sfc.Attachments[0].From);

        // JSR in an action body is linked
        Assert.Contains(routine.CalledRoutines, r => r.Name == "Sub");
    }
}
