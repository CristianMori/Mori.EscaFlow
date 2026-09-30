using L5x.Model.Logic;
using L5x.Model.Logic.Rll;
using L5x.Model.Logic.St;
using L5x.Parsing;
using Xunit;

namespace L5x.Tests.Linking;

public class KeywordReclassificationTests
{
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T">
        <DataTypes/>
        <Modules>
        <Module Name="Drive1" CatalogNumber="PowerFlex" ParentModule="Local" ParentModPortId="2">
        <Ports><Port Id="2" Address="10.0.0.5" Type="Ethernet" Upstream="true"/></Ports>
        </Module>
        </Modules>
        <AddOnInstructionDefinitions>
        <AddOnInstructionDefinition Name="MyAoi" Revision="1.0">
        <Parameters>
        <Parameter Name="Mode" TagType="Base" DataType="DINT" Usage="Input" Radix="Decimal" Required="true" Visible="true"/>
        </Parameters>
        <LocalTags/>
        <Routines><Routine Name="Logic" Type="ST"><STContent/></Routine></Routines>
        </AddOnInstructionDefinition>
        </AddOnInstructionDefinitions>
        <Tags>
        <Tag Name="Ax" TagType="Base" DataType="AXIS_VIRTUAL"/>
        <Tag Name="MC" TagType="Base" DataType="MOTION_INSTRUCTION"/>
        <Tag Name="t" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="Disabled" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Aoi1" TagType="Base" DataType="MyAoi"/>
        </Tags>
        <Programs>
        <Program Name="P" MainRoutineName="Main">
        <Tags/>
        <Routines>
        <Routine Name="Main" Type="RLL">
        <RLLContent>
        <Rung Number="0" Type="N">
        <Text>
        <![CDATA[MAJ(Ax,MC,0,10,Units per sec,100,Units per sec2,100,Units per sec2,S-Curve,Disabled,0)GSV(Module,Drive1,EntryStatus,t)JSR(Sub,0)MyAoi(Aoi1,Programmed);]]>
        </Text>
        </Rung>
        </RLLContent>
        </Routine>
        <Routine Name="Sub" Type="ST">
        <STContent>
        <Line Number="0">
        <![CDATA[GSV(WallClockTime,,DateTime,t);]]>
        </Line>
        <Line Number="1">
        <![CDATA[IF t > 0 THEN MAM(Ax,MC,0,10,1,Units per sec,100,Units per sec2,100,Units per sec2,S-Curve,Disabled,0); JSR(Main); END_IF;]]>
        </Line>
        <Line Number="2">
        <![CDATA[MyAoi(Aoi1,Programmed);]]>
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
    public void LadderOperandsAreReclassified()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics.Where(d => d.Severity != Diagnostics.DiagnosticSeverity.Info));
        var rung = ((RllContent)project.Controller.Programs[0].Routines[0].Content!).Rungs[0];
        var instrs = rung.Ast!.AllInstructions().ToList();

        var maj = instrs[0];
        Assert.IsType<TagRefOperand>(maj.Arguments[0]);
        Assert.IsType<KeywordOperand>(maj.Arguments[4]);              // Units per sec
        Assert.Equal("S-Curve", Assert.IsType<KeywordOperand>(maj.Arguments[9]).Text);
        Assert.IsType<TagRefOperand>(maj.Arguments[10]);             // Disabled is a real tag here

        var gsv = instrs[1];
        Assert.IsType<KeywordOperand>(gsv.Arguments[0]);             // Module (class name)
        Assert.IsType<TagRefOperand>(gsv.Arguments[1]);              // Drive1 is a module name: kept as reference
        Assert.IsType<KeywordOperand>(gsv.Arguments[2]);             // EntryStatus

        var jsr = instrs[2];
        Assert.IsType<RoutineRefOperand>(jsr.Arguments[0]);

        var aoi = instrs[3];
        Assert.True(aoi.IsAddOn);
        Assert.IsType<TagRefOperand>(aoi.Arguments[1]);              // AOI arguments are never reclassified
    }

    [Fact]
    public void StructuredTextCallArgumentsAreReclassified()
    {
        var project = L5xParser.ParseText(Xml);
        var st = (StContent)project.Controller.Programs[0].Routines[1].Content!;
        var gsv = Assert.IsType<StCallStatement>(st.Ast!.Statements[0]);
        var cls = Assert.IsType<StKeyword>(gsv.Arguments[0]);
        Assert.Equal("WallClockTime", cls.Text);
        Assert.Equal(StKeywordKind.Keyword, cls.Kind);
        Assert.Null(gsv.Arguments[1]);
        Assert.IsType<StKeyword>(gsv.Arguments[2]);
        Assert.IsType<StVariable>(gsv.Arguments[3]);

        var ifs = Assert.IsType<StIf>(st.Ast.Statements[1]);
        var mam = Assert.IsType<StCallStatement>(ifs.Branches[0].Body.Statements[0]);
        Assert.Equal("S-Curve", Assert.IsType<StKeyword>(mam.Arguments[10]).Text);
        Assert.IsType<StVariable>(mam.Arguments[11]);                // Disabled tag
        var jsr = Assert.IsType<StCallStatement>(ifs.Branches[0].Body.Statements[1]);
        var routine = Assert.IsType<StKeyword>(jsr.Arguments[0]);
        Assert.Equal(StKeywordKind.Routine, routine.Kind);

        var aoiCall = Assert.IsType<StCallStatement>(st.Ast.Statements[2]);
        Assert.IsType<StVariable>(aoiCall.Arguments[1]);

        // Routine call linking still works through the keyword node.
        var sub = project.Controller.Programs[0].Routines[1];
        Assert.Contains(sub.CalledRoutines, r => r.Name == "Main");
    }
}
