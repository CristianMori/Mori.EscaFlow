using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Fbd;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Fbd;

/// <summary>Proves every FBD element kind (IRef/ORef, connectors, blocks, add-on calls, JSR/SBR/RET) is read with its attributes intact, and that wires (including feedback wires) resolve to the right elements by ID.</summary>
public class FbdReaderTests
{
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" SoftwareRevision="33.00" TargetName="T" TargetType="Controller" ContainsContext="false">
        <Controller Use="Target" Name="T">
        <DataTypes/>
        <AddOnInstructionDefinitions>
        <AddOnInstructionDefinition Name="MyAoi" Revision="1.0">
        <Parameters/>
        <LocalTags/>
        <Routines>
        <Routine Name="Logic" Type="ST"><STContent/></Routine>
        </Routines>
        </AddOnInstructionDefinition>
        </AddOnInstructionDefinitions>
        <Tags/>
        <Programs>
        <Program Name="P" MainRoutineName="Main">
        <Tags/>
        <Routines>
        <Routine Name="Main" Type="FBD">
        <FBDContent SheetSize="Letter (8.5x11in)" SheetOrientation="Landscape">
        <Sheet Number="1">
        <Description>
        <![CDATA[Sheet one]]>
        </Description>
        <IRef ID="0" X="20" Y="40" Operand="Input_1" HideDesc="false"/>
        <IRef ID="1" X="20" Y="80" Operand="2.5" HideDesc="false"/>
        <ORef ID="2" X="400" Y="40" Operand="Output_1" HideDesc="false"/>
        <ICon ID="3" X="20" Y="120" Name="Conn"/>
        <OCon ID="4" X="400" Y="120" Name="Conn"/>
        <Block ID="5" X="200" Y="40" Type="MUL" Operand="MUL_01" VisiblePins="SourceA SourceB Dest" HideDesc="false"/>
        <Block ID="6" X="200" Y="200" Type="DEDT" Operand="DEDT_01" VisiblePins="In Out">
        <Array Name="Storage" Operand="DelayStorage"/>
        </Block>
        <AddOnInstruction ID="7" X="200" Y="300" Name="MyAoi" Operand="MyAoi_01" VisiblePins="EnableIn EnableOut">
        <InOutParameter Name="Data" Argument="SomeTag"/>
        </AddOnInstruction>
        <JSR ID="8" X="200" Y="400" Routine="Sub" In="a b" Ret="c"/>
        <Function ID="9" X="300" Y="400" Type="ADD"/>
        <Wire FromID="0" FromParam="In" ToID="5" ToParam="SourceA"/>
        <Wire FromID="1" FromParam="In" ToID="5" ToParam="SourceB"/>
        <Wire FromID="5" FromParam="Dest" ToID="2" ToParam="Out"/>
        <FeedbackWire FromID="5" FromParam="Dest" ToID="6" ToParam="In"/>
        <TextBox ID="10" X="10" Y="10" Width="0">
        <![CDATA[hello]]>
        </TextBox>
        <Attachment FromID="10" ToID="5"/>
        </Sheet>
        </FBDContent>
        </Routine>
        <Routine Name="Sub" Type="RLL"><RLLContent/></Routine>
        </Routines>
        </Program>
        </Programs>
        <Tasks/>
        </Controller>
        </RSLogix5000Content>
        """;

    [Fact]
    public void MapsAllElementsAndResolvesWires()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics);
        var routine = project.Controller.Programs[0].Routines[0];
        Assert.Equal(RoutineType.FBD, routine.Type);
        var fbd = Assert.IsType<FbdContent>(routine.Content);
        Assert.Equal("Landscape", fbd.SheetOrientation);
        var sheet = Assert.Single(fbd.Sheets);
        Assert.Equal(1, sheet.Number);
        Assert.Equal("Sheet one", sheet.Description);
        Assert.Equal(11, sheet.Elements.Count);

        var iref = Assert.IsType<FbdIRef>(sheet.ById[0]);
        Assert.IsType<TagRefOperand>(iref.ParsedOperand);
        var lit = Assert.IsType<FbdIRef>(sheet.ById[1]);
        Assert.IsType<LiteralOperand>(lit.ParsedOperand);
        Assert.IsType<FbdORef>(sheet.ById[2]);
        Assert.Equal("Conn", Assert.IsType<FbdICon>(sheet.ById[3]).Name);
        Assert.Equal("Conn", Assert.IsType<FbdOCon>(sheet.ById[4]).Name);

        var mul = Assert.IsType<FbdBlock>(sheet.ById[5]);
        Assert.Equal("MUL", mul.Type);
        Assert.Equal(new[] { "SourceA", "SourceB", "Dest" }, mul.VisiblePins);

        var dedt = Assert.IsType<FbdBlock>(sheet.ById[6]);
        Assert.Equal("DelayStorage", dedt.Arrays!["Storage"]);

        var aoi = Assert.IsType<FbdAddOnInstruction>(sheet.ById[7]);
        Assert.Equal("MyAoi", aoi.Name);
        Assert.Equal("SomeTag", aoi.InOutParameters[0].Value);

        var jsr = Assert.IsType<FbdJsr>(sheet.ById[8]);
        Assert.Equal("Sub", jsr.Routine);
        Assert.Equal(new[] { "a", "b" }, jsr.In);
        Assert.Equal(new[] { "c" }, jsr.Ret);
        Assert.Contains(routine.CalledRoutines, r => r.Name == "Sub");

        Assert.Equal("ADD", Assert.IsType<FbdFunction>(sheet.ById[9]).Type);
        Assert.Equal("hello", Assert.IsType<FbdTextBox>(sheet.ById[10]).Text);

        Assert.Equal(4, sheet.Wires.Count);
        Assert.All(sheet.Wires, w => Assert.True(w.From is not null && w.To is not null));
        Assert.True(sheet.Wires[3].IsFeedback);
        Assert.Equal("SourceA", sheet.Wires[0].ToParam);
        Assert.Single(sheet.Attachments);
    }
}
