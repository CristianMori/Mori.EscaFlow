using L5x.Parsing;
using Plc.Ir.Logix.Build;
using Plc.Ir.Validation;
using Xunit;

namespace Plc.Ir.Logix.Tests;

public class LogixIrBuilderTests
{
    public const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T" ProcessorType="1756-L85E" MajorRev="33" MinorRev="12">
        <DataTypes>
        <DataType Name="Motor" Family="NoFamily" Class="User">
        <Members>
        <Member Name="Speed" DataType="REAL" Dimension="0" Radix="Float" Hidden="false"/>
        <Member Name="ZZZZZZZZZZMotor0" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="true"/>
        <Member Name="Fault" DataType="BIT" Dimension="0" Radix="Decimal" Hidden="false" Target="ZZZZZZZZZZMotor0" BitNumber="0"/>
        <Member Name="Timer" DataType="TIMER" Dimension="0" Radix="NullType" Hidden="false"/>
        <Member Name="Name" DataType="STRING" Dimension="0" Radix="NullType" Hidden="false"/>
        </Members>
        </DataType>
        </DataTypes>
        <Modules>
        <Module Name="Local" CatalogNumber="1756-L85E" ParentModule="Local" ParentModPortId="1">
        <Ports><Port Id="1" Address="0" Type="ICP" Upstream="false"/></Ports>
        </Module>
        <Module Name="DI" CatalogNumber="1756-IB16" ParentModule="Local" ParentModPortId="1">
        <Ports><Port Id="1" Address="3" Type="ICP" Upstream="true"/></Ports>
        <Communications>
        <Connections>
        <Connection Name="Input" RPI="20000" Type="Input">
        <InputTag ExternalAccess="Read/Write">
        <Data Format="Decorated">
        <Structure DataType="AB:1756_DI:I:0">
        <DataValueMember Name="Fault" DataType="DINT" Radix="Binary" Value="2#0000_0000_0000_0000_0000_0000_0000_0000"/>
        <DataValueMember Name="Data" DataType="DINT" Radix="Binary" Value="2#0000_0000_0000_0000_0000_0000_0000_0000"/>
        </Structure>
        </Data>
        </InputTag>
        </Connection>
        </Connections>
        </Communications>
        </Module>
        </Modules>
        <AddOnInstructionDefinitions>
        <AddOnInstructionDefinition Name="Blink" Revision="1.0" ExecutePrescan="true" ExecuteEnableInFalse="true">
        <Parameters>
        <Parameter Name="EnableIn" TagType="Base" DataType="BOOL" Usage="Input" Radix="Decimal" Required="false" Visible="false"/>
        <Parameter Name="Rate" TagType="Base" DataType="DINT" Usage="Input" Radix="Decimal" Required="true" Visible="true"/>
        <Parameter Name="Out" TagType="Base" DataType="BOOL" Usage="Output" Radix="Decimal" Required="true" Visible="true"/>
        </Parameters>
        <LocalTags><LocalTag Name="T1" DataType="TIMER" Radix="NullType"/></LocalTags>
        <Routines>
        <Routine Name="Logic" Type="RLL"><RLLContent><Rung Number="0" Type="N"><Text><![CDATA[XIO(T1.DN)TON(T1,?,?)XIC(T1.DN)OTE(Out);]]></Text></Rung></RLLContent></Routine>
        <Routine Name="Prescan" Type="ST"><STContent><Line Number="0"><![CDATA[Out := 0;]]></Line></STContent></Routine>
        <Routine Name="EnableInFalse" Type="ST"><STContent><Line Number="0"><![CDATA[Out := 0;]]></Line></STContent></Routine>
        </Routines>
        </AddOnInstructionDefinition>
        </AddOnInstructionDefinitions>
        <Tags>
        <Tag Name="M1" TagType="Base" DataType="Motor" Radix="NullType">
        <Data Format="Decorated">
        <Structure DataType="Motor">
        <DataValueMember Name="Speed" DataType="REAL" Radix="Float" Value="1.5"/>
        <DataValueMember Name="Fault" DataType="BOOL" Value="0"/>
        <StructureMember Name="Timer" DataType="TIMER">
        <DataValueMember Name="PRE" DataType="DINT" Radix="Decimal" Value="500"/>
        <DataValueMember Name="ACC" DataType="DINT" Radix="Decimal" Value="0"/>
        <DataValueMember Name="EN" DataType="BOOL" Value="0"/>
        <DataValueMember Name="TT" DataType="BOOL" Value="0"/>
        <DataValueMember Name="DN" DataType="BOOL" Value="0"/>
        </StructureMember>
        <StructureMember Name="Name" DataType="STRING">
        <DataValueMember Name="LEN" DataType="DINT" Radix="Decimal" Value="0"/>
        <ArrayMember Name="DATA" DataType="SINT" Dimensions="82" Radix="ASCII"/>
        </StructureMember>
        </Structure>
        </Data>
        </Tag>
        <Tag Name="Motors" TagType="Base" DataType="Motor" Dimensions="4" Radix="NullType"/>
        <Tag Name="Run" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Stop" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Lamp" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Pulse" TagType="Base" DataType="BOOL" Radix="Decimal"/>
        <Tag Name="Count" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="Word" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="Fast" TagType="Base" DataType="REAL" Radix="Float"/>
        <Tag Name="Blk" TagType="Base" DataType="Blink"/>
        <Tag Name="Al" TagType="Alias" AliasFor="M1.Fault"/>
        </Tags>
        <Programs>
        <Program Name="P" MainRoutineName="Main">
        <Tags>
        <Tag Name="Idx" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="InA" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="OutA" TagType="Base" DataType="DINT" Radix="Decimal"/>
        </Tags>
        <Routines>
        <Routine Name="Main" Type="RLL">
        <RLLContent>
        <Rung Number="0" Type="N">
        <Comment><![CDATA[start]]></Comment>
        <Text><![CDATA[XIC(Run)XIO(Stop)[XIC(Motors[Idx].Fault) ,GRT(Count,10) ]OTE(Lamp);]]></Text>
        </Rung>
        <Rung Number="1" Type="N">
        <Text><![CDATA[XIC(Run)TON(M1.Timer,?,?)XIC(M1.Timer.DN)MOV(0,Count)ADD(Count,1,Count)JSR(Sub,1,Count,Word);]]></Text>
        </Rung>
        <Rung Number="2" Type="N">
        <Text><![CDATA[XIC(Al)Blink(Blk,100,Pulse)OTL(Word.3)AFI()OTU(Word.3);]]></Text>
        </Rung>
        <Rung Number="3" Type="N">
        <Text><![CDATA[XIC(DI:I.Data.4)CPT(M1.Speed,(Count+1)*2.5)MOV(Fast,Count);]]></Text>
        </Rung>
        <Rung Number="4" Type="N">
        <Text><![CDATA[XIC(Run)ONS(Word.0)OSR(Word.1,Pulse)MCR();]]></Text>
        </Rung>
        <Rung Number="5" Type="N">
        <Text><![CDATA[XIC(Stop)OTE(Lamp)JMP(Skip);]]></Text>
        </Rung>
        <Rung Number="6" Type="N">
        <Text><![CDATA[MCR();]]></Text>
        </Rung>
        <Rung Number="7" Type="N">
        <Text><![CDATA[LBL(Skip)LIM(0,Count,10)RES(M1.Timer)COP(M1,Motors[0],1)GSV(WallClockTime,,DateTime,Word);]]></Text>
        </Rung>
        </RLLContent>
        </Routine>
        <Routine Name="Sub" Type="ST">
        <STContent>
        <Line Number="0"><![CDATA[SBR(InA);]]></Line>
        <Line Number="1"><![CDATA[IF Run AND NOT Stop THEN Count := Count + 1; ELSIF Count > 5 THEN Count := 0; END_IF;]]></Line>
        <Line Number="2"><![CDATA[FOR Idx := 0 TO 3 DO Motors[Idx].Speed := M1.Speed * 2.0; END_FOR;]]></Line>
        <Line Number="3"><![CDATA[GSV(WallClockTime,,DateTime,Word);]]></Line>
        <Line Number="4"><![CDATA[OutA := InA + M1.Name.LEN;]]></Line>
        <Line Number="5"><![CDATA[Blink(Blk, 50, Pulse);]]></Line>
        <Line Number="6"><![CDATA[RET(OutA);]]></Line>
        </STContent>
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

    private static IrDocument Build(bool values = true)
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics.Where(d => d.Severity != L5x.Diagnostics.DiagnosticSeverity.Info));
        return new LogixIrBuilder(project, new LogixBuildOptions { IncludeValues = values }).Build();
    }

    private static IrPou Program(IrDocument ir) => ir.FindPou("P")!;

    [Fact]
    public void DocumentConfigurationAndGlobals()
    {
        var ir = Build();
        Assert.Equal(IrDocument.FormatId, ir.Format);
        Assert.Equal(IrDocument.CurrentVersion, ir.IrVersion);
        Assert.Equal(Logix.Id, ir.Source.Vendor);
        Assert.Equal("T", ir.Configuration.Name);
        var resource = Assert.Single(ir.Configuration.Resources);
        Assert.Equal("1756-L85E", resource.ProcessorType);
        var task = Assert.Single(resource.Tasks);
        Assert.Equal("continuous", task.Kind);
        Assert.Equal(new[] { "P" }, task.Programs);
        var instance = Assert.Single(resource.Programs);
        Assert.Equal("P", instance.Pou);
        Assert.Equal("Main", instance.Task);
        Assert.Equal(33, ir.Logix()!.MajorRev);

        var gvl = ir.Globals.Single(g => g.Name == "T");
        var m1 = ir.FindVariable("global/T/M1")!;
        Assert.Equal("global", m1.Section);
        Assert.Equal("retain", m1.Retain);
        Assert.True(m1.Type.Is("Motor"));
        Assert.Equal("M1", m1.Logix()!.Path);
        var value = Assert.IsType<IrStructValue>(m1.Initial);
        Assert.Equal(1.5, Assert.IsType<IrScalarValue>(value.Members[0].Value).Real);
        var motors = ir.FindVariable("global/T/Motors")!;
        Assert.True(motors.Type.IsArray);
        Assert.Equal(3, motors.Type.Dims![0].Upper);
        Assert.Equal("Motor", motors.Type.LeafName);

        var al = ir.FindVariable("global/T/Al")!;
        Assert.True(al.Type.Is("BOOL"));
        Assert.Equal("M1.Fault", al.Alias!.Text);
        Assert.Equal("global/T/M1", al.Alias.Target!.Root.Id);
        Assert.Equal("Fault", Assert.IsType<IrMemberSegment>(al.Alias.Target.Path[0]).Name);
        Assert.Contains(gvl.Vars, v => v.Name == "Blk" && v.Type.Is("Blink"));
    }

    [Fact]
    public void TypesAreNeutralWithLogixExtension()
    {
        var ir = Build();
        var motor = ir.FindType("Motor")!;
        Assert.Equal("user", motor.Class);
        Assert.Equal("struct", motor.Kind);
        Assert.Equal(new[] { "Speed", "Fault", "Timer", "Name" }, motor.Members!.Select(m => m.Name));
        var fault = motor.Members!.Single(m => m.Name == "Fault");
        Assert.True(fault.Type.Is("BOOL"));
        Assert.Equal("ZZZZZZZZZZMotor0", fault.Logix()!.BitHost);
        Assert.Equal(0, fault.Logix()!.BitNumber);
        var hidden = Assert.Single(motor.Logix()!.HiddenMembers!);
        Assert.Equal("ZZZZZZZZZZMotor0", hidden.Name);

        var timer = ir.FindType("TIMER")!;
        Assert.Equal("vendor", timer.Class);
        Assert.Equal(Logix.Id, timer.StdFb!.Profile);
        Assert.Contains("TON", timer.StdFb.Fbs);
        var str = ir.FindType("STRING")!;
        Assert.Equal("string", str.Kind);
        Assert.Equal(82, str.Length);
        Assert.Null(ir.FindType("DINT"));
        Assert.Null(ir.FindType("Blink")); // add-on instances are typed by the POU

        var io = ir.FindType("AB:1756_DI:I:0")!;
        Assert.Equal("io", io.Class);
        Assert.Equal("input", io.Logix()!.ModuleType!.Direction);
        Assert.Equal(2, io.Members!.Count);
    }

    [Fact]
    public void IoDevicesAndChannels()
    {
        var ir = Build();
        var di = ir.Io.Devices.Single(d => d.Name == "DI");
        Assert.Equal("device/DI", di.Id);
        Assert.Equal("device/Local", di.Parent);
        Assert.Null(ir.Io.Devices.Single(d => d.Name == "Local").Parent);
        var channel = Assert.Single(ir.Io.Channels);
        Assert.Equal("io/DI/I", channel.Id);
        Assert.Equal("input", channel.Direction);
        Assert.Equal("global/IO/DI:I", channel.Variable);
        var image = ir.FindVariable("global/IO/DI:I")!;
        Assert.Equal("io/DI/I", image.Io);
        Assert.True(image.Type.Is("AB:1756_DI:I:0"));
        Assert.Equal("io/DI/I", di.Logix()!.Connections[0].InputChannel);
    }

    [Fact]
    public void ProgramAndAddOnPous()
    {
        var ir = Build();
        var p = Program(ir);
        Assert.Equal("program", p.Kind);
        Assert.Equal("local", ir.FindVariable("pou/P/Idx")!.Section);
        var main = Assert.IsType<IrActionCall>(Assert.Single(p.Body!.Statements));
        Assert.Equal("Main", main.Action);
        Assert.True(p.Body.Synthetic);
        Assert.Contains(p.Actions, a => a.Name == "Main" && a.Body.Language == "ld");
        Assert.Contains(p.Actions, a => a.Name == "Sub" && a.Body.Language == "st");
        Assert.Contains("Sub", p.FindAction("Main")!.Calls!);

        var blink = ir.FindPou("Blink")!;
        Assert.Equal("functionBlock", blink.Kind);
        Assert.Equal("input", ir.FindVariable("pou/Blink/Rate")!.Section);
        Assert.Equal("output", ir.FindVariable("pou/Blink/Out")!.Section);
        Assert.True(ir.FindVariable("pou/Blink/EnableIn")!.Logix()!.Implicit);
        Assert.Equal("local", ir.FindVariable("pou/Blink/T1")!.Section);
        Assert.Equal("ld", blink.Body!.Language);
        Assert.Equal("enableFalse", blink.FindAction("EnableInFalse")!.Hook);
        Assert.Null(blink.FindAction("Prescan")!.Hook);
        var init = blink.Actions.Single(a => a.Hook == "init");
        Assert.True(init.Synthetic);
        var ton = Assert.IsType<IrFbCall>(init.Body.Statements[0]);
        Assert.Equal("prescan", ton.Mode);
        Assert.Equal("TON", ton.Fb);
        var out0 = Assert.IsType<IrAssign>(init.Body.Statements[1]);
        Assert.True(Assert.IsType<IrLiteral>(out0.Value).IsFalse);
        Assert.Equal("Prescan", Assert.IsType<IrActionCall>(init.Body.Statements[2]).Action);
        Assert.Equal("addOn", blink.Logix()!.Kind);
    }

    [Fact]
    public void LadderDiagramIsNeutral()
    {
        var ir = Build();
        var ld = Assert.IsType<IrLdDiagram>(Program(ir).FindAction("Main")!.Body.Diagram);
        var rung0 = ld.Rungs[0];
        Assert.Equal("start", rung0.Comment);
        Assert.Equal(4, rung0.Elements.Count);
        var xic = Assert.IsType<IrLdContact>(rung0.Elements[0]);
        Assert.Equal("XIC", xic.Name);
        var run = Assert.IsType<IrRef>(xic.Operand);
        Assert.Equal("global/T/Run", run.Root.Id);
        Assert.True(run.Type!.Is("BOOL"));
        Assert.True(Assert.IsType<IrLdContact>(rung0.Elements[1]).Negated);
        var branch = Assert.IsType<IrLdBranch>(rung0.Elements[2]);
        var indirect = Assert.IsType<IrRef>(Assert.IsType<IrLdContact>(branch.Legs[0][0]).Operand);
        Assert.Equal("global/T/Motors", indirect.Root.Id);
        var index = Assert.IsType<IrIndexSegment>(indirect.Path[0]);
        Assert.Equal("pou/P/Idx", Assert.IsType<IrRef>(index.Indices[0]).Root.Id);
        Assert.Equal("Fault", Assert.IsType<IrMemberSegment>(indirect.Path[1]).Name);
        Assert.True(indirect.Type!.Is("BOOL"));
        var grt = Assert.IsType<IrLdContact>(branch.Legs[1][0]);
        Assert.Equal("gt", Assert.IsType<IrBinary>(grt.Operand).Operator);
        Assert.Equal(2, grt.Args!.Count);
        var ote = Assert.IsType<IrLdCoil>(rung0.Elements[3]);
        Assert.Equal("assign", ote.Mode);

        var rung1 = ld.Rungs[1];
        var ton = Assert.IsType<IrLdBlock>(rung1.Elements[1]);
        Assert.Equal("input", ton.Enable);
        var jsr = Assert.IsType<IrLdBlock>(rung1.Elements[5]);
        Assert.Equal("gate", jsr.Enable);
        var blink = Assert.IsType<IrLdBlock>(ld.Rungs[2].Elements[1]);
        Assert.True(blink.IsPou);
        var ons = Assert.IsType<IrLdContact>(ld.Rungs[4].Elements[1]);
        Assert.Equal("rising", ons.Edge);
        Assert.IsType<IrBitAccess>(ons.Storage);
        var osr = Assert.IsType<IrLdCoil>(ld.Rungs[4].Elements[2]);
        Assert.Equal("risingPulse", osr.Mode);
        Assert.True(Assert.IsType<IrLdMcr>(ld.Rungs[4].Elements[3]).Begin);
        Assert.False(Assert.IsType<IrLdMcr>(ld.Rungs[6].Elements[0]).Begin);
        Assert.Equal("Skip", Assert.IsType<IrLdJump>(ld.Rungs[5].Elements[2]).Label);
        Assert.Equal("Skip", Assert.IsType<IrLdLabel>(ld.Rungs[7].Elements[0]).Label);
    }

    [Fact]
    public void LadderIsLoweredToStatements()
    {
        var ir = Build();
        var body = Program(ir).FindAction("Main")!.Body;
        var s = body.Statements;
        List<IrStatement> Rung(int n) => s.Where(st => st.Src?.Rung == n).ToList();

        // Rung 0: Lamp := Run AND NOT Stop AND (Motors[Idx].Fault OR Count > 10)
        var lamp = Assert.IsType<IrAssign>(Assert.Single(Rung(0)));
        Assert.Equal("global/T/Lamp", Assert.IsType<IrRef>(lamp.Target).Root.Id);
        var cond = Assert.IsType<IrBinary>(lamp.Value);
        Assert.Equal("and", cond.Operator);
        Assert.Equal("or", Assert.IsType<IrBinary>(cond.Right).Operator);
        Assert.True(cond.IsBool);

        // Rung 1: TON is a standard block under the logix profile with IN bound to the rung condition; the compound
        // condition shared by MOV / ADD / JSR is stored once in a body temporary; JSR is bound to the callee's SBR / RET.
        var r1 = Rung(1);
        var ton = Assert.IsType<IrFbCall>(r1[0]);
        Assert.Equal("TON", ton.Fb);
        Assert.Equal(Logix.Id, ton.Profile);
        Assert.Equal("global/T/M1", Assert.IsType<IrRef>(ton.Instance).Root.Id);
        Assert.Equal("global/T/Run", Assert.IsType<IrRef>(ton.Inputs["IN"]).Root.Id);
        var temp = Assert.IsType<IrAssign>(r1[1]);
        Assert.Equal("temp", Assert.IsType<IrRef>(temp.Target).Root.Kind);
        Assert.Equal("_rc1", Assert.IsType<IrRef>(temp.Target).Root.Name);
        Assert.Single(body.Temporaries!, t => t.Name == "_rc1");
        var mov = Assert.IsType<IrIf>(r1[2]);
        Assert.Equal("temp", Assert.IsType<IrRef>(mov.Branches[0].Condition).Root.Kind);
        Assert.Equal("global/T/Count", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(mov.Branches[0].Body[0]).Target).Root.Id);
        var add = Assert.IsType<IrAssign>(Assert.IsType<IrIf>(r1[3]).Branches[0].Body[0]);
        Assert.Equal("add", Assert.IsType<IrBinary>(add.Value).Operator);
        var jsrIf = Assert.IsType<IrIf>(r1[4]);
        var bindIn = Assert.IsType<IrAssign>(jsrIf.Branches[0].Body[0]);
        Assert.Equal("pou/P/InA", Assert.IsType<IrRef>(bindIn.Target).Root.Id);
        Assert.Equal("global/T/Count", Assert.IsType<IrRef>(bindIn.Value).Root.Id);
        var call = Assert.IsType<IrActionCall>(jsrIf.Branches[0].Body[1]);
        Assert.Equal("Sub", call.Action);
        Assert.Empty(call.Inputs);
        var bindOut = Assert.IsType<IrAssign>(jsrIf.Branches[0].Body[2]);
        Assert.Equal("global/T/Word", Assert.IsType<IrRef>(bindOut.Target).Root.Id);
        Assert.Equal("pou/P/OutA", Assert.IsType<IrRef>(bindOut.Value).Root.Id);

        // Rung 2: alias expanded (with the alias noted), add-on as fbCall with En, OTL under IF, AFI kills the rest.
        var r2 = Rung(2);
        var blink = Assert.IsType<IrFbCall>(r2[0]);
        Assert.Equal("Blink", blink.Pou);
        var en = Assert.IsType<IrRef>(blink.En);
        Assert.Equal("global/T/M1", en.Root.Id);
        Assert.Equal("global/T/Al", en.Alias);
        Assert.Equal(100, Assert.IsType<IrLiteral>(blink.Inputs["Rate"]).Int);
        Assert.Equal("global/T/Pulse", Assert.IsType<IrRef>(blink.Outputs["Out"]).Root.Id);
        var otl = Assert.IsType<IrIf>(r2[1]);
        var otlAssign = Assert.IsType<IrAssign>(otl.Branches[0].Body[0]);
        var bit = Assert.IsType<IrBitAccess>(otlAssign.Target);
        Assert.Equal(3, Assert.IsType<IrLiteral>(bit.Bit).Int);
        Assert.Equal("global/T/Word", Assert.IsType<IrRef>(bit.Of).Root.Id);
        Assert.True(bit.Of.Type!.Is("DINT"));
        var otu = Assert.IsType<IrIf>(r2[2]);
        Assert.True(Assert.IsType<IrLiteral>(otu.Branches[0].Condition).IsFalse);

        // Rung 3: module I/O bit, CPT expression, REAL to DINT store rounds.
        var r3 = Rung(3);
        var cpt = Assert.IsType<IrIf>(r3[0]);
        var ioBit = Assert.IsType<IrBitAccess>(cpt.Branches[0].Condition);
        var ioRef = Assert.IsType<IrRef>(ioBit.Of);
        Assert.True(ioRef.Io);
        Assert.Equal("global/IO/DI:I", ioRef.Root.Id);
        Assert.Equal(4, Assert.IsType<IrLiteral>(ioBit.Bit).Int);
        var cptAssign = Assert.IsType<IrAssign>(cpt.Branches[0].Body[0]);
        Assert.True(cptAssign.Target.Type!.Is("REAL"));
        Assert.Equal("mul", Assert.IsType<IrBinary>(cptAssign.Value).Operator);
        var movReal = Assert.IsType<IrAssign>(Assert.IsType<IrIf>(r3[1]).Branches[0].Body[0]);
        var conv = Assert.IsType<IrConv>(movReal.Value);
        Assert.Equal("roundEven", conv.Mode);
        Assert.True(conv.To.Is("DINT"));

        // Rung 4: ONS edge on the rung condition, OSR pulse, MCR zone begins.
        var r4 = Rung(4);
        // The ONS edge of the rung condition is stored in a temporary (the OSR and the MCR both use it), then the
        // OSR output is the rising edge of that temporary with its own storage bit.
        var onsTemp = Assert.IsType<IrAssign>(r4[0]);
        Assert.Equal("temp", Assert.IsType<IrRef>(onsTemp.Target).Root.Kind);
        var ons = Assert.IsType<IrEdge>(onsTemp.Value);
        Assert.Equal("global/T/Run", Assert.IsType<IrRef>(ons.Clk).Root.Id);
        Assert.Equal(0, Assert.IsType<IrLiteral>(Assert.IsType<IrBitAccess>(ons.Storage).Bit).Int);
        var osr = Assert.IsType<IrAssign>(r4[1]);
        Assert.Equal("global/T/Pulse", Assert.IsType<IrRef>(osr.Target).Root.Id);
        var edge = Assert.IsType<IrEdge>(osr.Value);
        Assert.Equal("rising", edge.Kind);
        Assert.Equal("temp", Assert.IsType<IrRef>(edge.Clk).Root.Kind);
        var mcr = Assert.IsType<IrAssign>(r4[2]);
        Assert.Equal("_mcr1", Assert.IsType<IrRef>(mcr.Target).Root.Name);

        // Rung 5 inside the zone: OTE takes the zone condition; JMP under IF.
        var r5 = Rung(5);
        var zoned = Assert.IsType<IrBinary>(Assert.IsType<IrAssign>(r5[0]).Value);
        Assert.Equal("_mcr1", Assert.IsType<IrRef>(zoned.Left).Root.Name);
        Assert.Equal("global/T/Lamp", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(r5[1]).Target).Root.Id);
        Assert.IsType<IrJump>(Assert.IsType<IrIf>(r5[2]).Branches[0].Body[0]);

        // Rung 7 after the zone: label, LIM tree, RES member assigns, COP, GSV service.
        var r7 = Rung(7);
        Assert.Equal("Skip", Assert.IsType<IrLabel>(r7[0]).Name);
        var limTemp = Assert.IsType<IrAssign>(r7[1]);
        Assert.Equal("or", Assert.IsType<IrBinary>(limTemp.Value).Operator);
        var res = Assert.IsType<IrIf>(r7[2]);
        Assert.Equal(4, res.Branches[0].Body.Count);
        Assert.Equal("ACC", Assert.IsType<IrMemberSegment>(Assert.IsType<IrRef>(Assert.IsType<IrAssign>(res.Branches[0].Body[0]).Target).Path[^1]).Name);
        var cop = Assert.IsType<IrCopy>(Assert.IsType<IrIf>(r7[3]).Branches[0].Body[0]);
        Assert.Equal("bytes", cop.Mode);
        var gsv = Assert.IsType<IrSysCall>(Assert.IsType<IrIf>(r7[4]).Branches[0].Body[0]);
        Assert.Equal(Logix.Id, gsv.Catalog);
        Assert.Equal("GSV", gsv.Service);
        Assert.Equal("WallClockTime", Assert.IsType<IrKeyword>(gsv.Args[0]).Text);
        Assert.Null(gsv.Args[1]);
    }

    [Fact]
    public void StructuredTextStatements()
    {
        var ir = Build();
        var st = Program(ir).FindAction("Sub")!.Body;
        Assert.Equal("st", st.Language);
        var stmts = st.Statements;
        Assert.Equal(6, stmts.Count); // SBR contributes nothing
        var ifs = Assert.IsType<IrIf>(stmts[0]);
        Assert.Equal(2, ifs.Branches.Count);
        Assert.True(ifs.Branches[0].Condition.IsBool);
        Assert.Equal(1, ifs.Src!.Line);
        var f = Assert.IsType<IrFor>(stmts[1]);
        Assert.Equal("pou/P/Idx", Assert.IsType<IrRef>(f.Variable).Root.Id);
        var assign = Assert.IsType<IrAssign>(f.Body[0]);
        Assert.True(assign.Target.Type!.Is("REAL"));
        var gsv = Assert.IsType<IrSysCall>(stmts[2]);
        Assert.Equal("WallClockTime", Assert.IsType<IrKeyword>(gsv.Args[0]).Text);
        Assert.Null(gsv.Args[1]);
        var len = Assert.IsType<IrAssign>(stmts[3]);
        var lenRef = Assert.IsType<IrRef>(Assert.IsType<IrBinary>(len.Value).Right);
        Assert.IsType<IrLengthSegment>(lenRef.Path[^1]);
        var blink = Assert.IsType<IrFbCall>(stmts[4]);
        Assert.Equal("Blink", blink.Pou);
        Assert.Null(blink.En);
        Assert.IsType<IrReturn>(stmts[5]);
        Assert.Equal(7, st.Text!.Count);
    }

    [Fact]
    public void PrescanOfProgramIsSynthesized()
    {
        var ir = Build();
        var init = Program(ir).Actions.Single(a => a.Hook == "init");
        Assert.Equal("Prescan", init.Name);
        var s = init.Body.Statements;
        // OTE Lamp cleared (rung 0), TON prescan (rung 1), Blink prescan (rung 2), ONS storage set + OSR output cleared + storage set (rung 4), OTE Lamp (rung 5), Blink in Sub (ST)
        Assert.Contains(s, x => x is IrAssign { Target: IrRef { Root.Id: "global/T/Lamp" }, Value: IrLiteral { IsFalse: true } });
        Assert.Contains(s, x => x is IrFbCall { Fb: "TON", Mode: "prescan" } c && c.Inputs["IN"] is IrLiteral { IsFalse: true });
        Assert.Contains(s, x => x is IrFbCall { Pou: "Blink", Mode: "prescan" });
        Assert.Contains(s, x => x is IrAssign { Target: IrBitAccess, Value: IrLiteral { IsTrue: true } });
        Assert.Equal(2, s.Count(x => x is IrFbCall { Pou: "Blink", Mode: "prescan" }));
        Assert.DoesNotContain(s, x => x is IrSysCall { Service: "GSV" });
    }

    [Fact]
    public void ValidatesCleanAndRoundTrips()
    {
        var ir = Build();
        var result = new IrValidator().Validate(ir, new ValidationOptions { ServiceCatalogs = { LogixServiceCatalog.Instance } });
        Assert.True(!result.HasErrors, result.ToString());
        var json = IrJson.Serialize(ir);
        var back = IrJson.Deserialize(json);
        Assert.Equal(json, IrJson.Serialize(back));
        Assert.Equal(ir.Pous.Count, back.Pous.Count);
        Assert.Equal("M1", back.FindVariable("global/T/M1")!.Logix()!.Path);
        Assert.Equal(33, back.Logix()!.MajorRev);
        var again = new IrValidator().Validate(back, new ValidationOptions { ServiceCatalogs = { LogixServiceCatalog.Instance } });
        Assert.False(again.HasErrors, again.ToString());
    }

    [Fact]
    public void ValuesCanBeOmitted()
    {
        var ir = Build(values: false);
        Assert.All(ir.AllVariables(), v => Assert.Null(v.Initial));
    }
}
