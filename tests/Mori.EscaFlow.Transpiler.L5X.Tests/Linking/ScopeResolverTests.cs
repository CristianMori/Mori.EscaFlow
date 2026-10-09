using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Linking;
using Mori.EscaFlow.Transpiler.L5X.Parsing.St;
using Mori.EscaFlow.Transpiler.L5X.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Linking;

/// <summary>
/// Proves <c>ScopeResolver</c> walks a tag path to the right leaf type in every case: members, array indices
/// and bits combined and nested, predefined-type members and the STRING collapse, program/controller scope
/// shadowing and alias following, module and system tag addressing, and that hitting an opaque type partway
/// through a path stops cleanly (no error, no further type information) rather than guessing.
/// </summary>
public class ScopeResolverTests
{
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T">
        <DataTypes>
        <DataType Name="Inner" Family="NoFamily" Class="User">
        <Members>
        <Member Name="Val" DataType="REAL" Dimension="0" Radix="Float" Hidden="false"/>
        <Member Name="ZZZZZZZZZZInner0" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="true"/>
        <Member Name="Flag" DataType="BIT" Dimension="0" Radix="Decimal" Hidden="false" Target="ZZZZZZZZZZInner0" BitNumber="3"/>
        </Members>
        </DataType>
        <DataType Name="Outer" Family="NoFamily" Class="User">
        <Members>
        <Member Name="Items" DataType="Inner" Dimension="4" Radix="NullType" Hidden="false"/>
        <Member Name="Count" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="Timer" DataType="TIMER" Dimension="0" Radix="NullType" Hidden="false"/>
        <Member Name="Name" DataType="STRING" Dimension="0" Radix="NullType" Hidden="false"/>
        </Members>
        </DataType>
        </DataTypes>
        <Modules>
        <Module Name="Local" CatalogNumber="1756-L85E" ParentModule="Local" ParentModPortId="1">
        <Ports><Port Id="1" Address="0" Type="ICP" Upstream="false"><Bus Size="10"/></Port></Ports>
        </Module>
        <Module Name="DI_Card" CatalogNumber="1756-IB16" ParentModule="Local" ParentModPortId="1">
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
        <AddOnInstructionDefinition Name="MyAoi" Revision="1.0">
        <Parameters>
        <Parameter Name="EnableIn" TagType="Base" DataType="BOOL" Usage="Input" Radix="Decimal" Required="false" Visible="false"/>
        <Parameter Name="In" TagType="Base" DataType="DINT" Usage="Input" Radix="Decimal" Required="true" Visible="true"/>
        </Parameters>
        <LocalTags>
        <LocalTag Name="Work" DataType="Outer" Radix="NullType"/>
        </LocalTags>
        <Routines><Routine Name="Logic" Type="ST"><STContent/></Routine></Routines>
        </AddOnInstructionDefinition>
        </AddOnInstructionDefinitions>
        <Tags>
        <Tag Name="G" TagType="Base" DataType="Outer" Radix="NullType"/>
        <Tag Name="Arr" TagType="Base" DataType="Outer" Dimensions="2 3" Radix="NullType"/>
        <Tag Name="Word" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="Shadow" TagType="Base" DataType="DINT" Radix="Decimal"/>
        <Tag Name="Al" TagType="Alias" AliasFor="G.Items[1]"/>
        <Tag Name="AlBit" TagType="Alias" AliasFor="Word.7"/>
        <Tag Name="Axis" TagType="Base" DataType="AXIS_CIP_DRIVE"/>
        </Tags>
        <Programs>
        <Program Name="P" MainRoutineName="Main">
        <Tags>
        <Tag Name="Shadow" TagType="Base" DataType="REAL" Radix="Float"/>
        <Tag Name="Local1" TagType="Base" DataType="TIMER" Radix="NullType"/>
        </Tags>
        <Routines><Routine Name="Main" Type="RLL"><RLLContent/></Routine></Routines>
        </Program>
        <Program Name="Q" MainRoutineName="Main">
        <Tags><Tag Name="QTag" TagType="Base" DataType="INT" Radix="Decimal"/></Tags>
        <Routines><Routine Name="Main" Type="RLL"><RLLContent/></Routine></Routines>
        </Program>
        </Programs>
        <Tasks/>
        </Controller>
        </RSLogix5000Content>
        """;

    private static (ScopeResolver Resolver, Controller Controller) Setup()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info));
        return (new ScopeResolver(project.Controller), project.Controller);
    }

    private static OperandPath Path(string text) => StParser.TryParseOperandPath(text, LogicParseContext.Empty, new DiagnosticBag())!;

    [Fact]
    public void WalksMembersArraysAndBits()
    {
        var (r, c) = Setup();
        var p = c.FindProgram("P");

        var a = r.Resolve(Path("G.Items[2].Val"), p);
        Assert.True(a.IsResolved, a.Error);
        Assert.Equal("REAL", a.LeafType!.Name);
        Assert.Same(c.Tags["G"], a.Tag);
        Assert.Equal("Val", a.LeafMember!.Name);

        var b = r.Resolve(Path("G.Items[i].Flag"), p);
        Assert.True(b.IsResolved, b.Error);
        Assert.True(b.IsBit);
        Assert.Equal(3, b.BitNumber);
        Assert.Equal("SINT", b.BitHostType!.Name);
        Assert.Equal("BOOL", b.LeafType!.Name);

        var w = r.Resolve(Path("Word.5"), p);
        Assert.True(w.IsBit);
        Assert.Equal(5, w.BitNumber);
        Assert.Equal("DINT", w.BitHostType!.Name);

        var dyn = r.Resolve(Path("Word.[k]"), p);
        Assert.True(dyn.IsBit);
        Assert.Null(dyn.BitNumber);

        var whole = r.Resolve(Path("G.Items"), p);
        Assert.True(whole.IsArrayReference);
        Assert.Equal(new[] { 4 }, whole.LeafDimensions);
        Assert.Equal("Inner", whole.LeafType!.Name);

        var multi = r.Resolve(Path("Arr[1,2].Count"), p);
        Assert.True(multi.IsResolved, multi.Error);
        Assert.Equal("DINT", multi.LeafType!.Name);

        var badRank = r.Resolve(Path("Arr[1].Count"), p);
        Assert.False(badRank.IsResolved);
        Assert.Contains("rank", badRank.Error);
    }

    [Fact]
    public void PredefinedMembersAndStrings()
    {
        var (r, c) = Setup();
        var p = c.FindProgram("P");
        var dn = r.Resolve(Path("G.Timer.DN"), p);
        Assert.True(dn.IsResolved, dn.Error);
        Assert.True(dn.IsBit);
        Assert.Equal("BOOL", dn.LeafType!.Name);

        var pre = r.Resolve(Path("Local1.PRE"), p);
        Assert.True(pre.IsResolved, pre.Error);
        Assert.Equal("DINT", pre.LeafType!.Name);
        Assert.Equal(TagScope.Program, pre.Scope);

        var data = r.Resolve(Path("G.Name.DATA[3]"), p);
        Assert.True(data.IsResolved, data.Error);
        Assert.Equal("SINT", data.LeafType!.Name);

        var len = r.Resolve(Path("G.Name.LEN"), p);
        Assert.Equal("DINT", len.LeafType!.Name);
    }

    [Fact]
    public void ScopingAndAliases()
    {
        var (r, c) = Setup();
        var p = c.FindProgram("P");
        var q = c.FindProgram("Q");

        Assert.Equal("REAL", r.Resolve(Path("Shadow"), p).LeafType!.Name);
        Assert.Equal("DINT", r.Resolve(Path("Shadow"), q).LeafType!.Name);
        Assert.Equal("DINT", r.Resolve(Path("Shadow"), (Model.Programs.Program?)null).LeafType!.Name);

        var cross = r.Resolve(Path("\\Q.QTag"), p);
        Assert.Equal("INT", cross.LeafType!.Name);

        var al = r.Resolve(Path("Al.Val"), p);
        Assert.True(al.IsResolved, al.Error);
        Assert.Equal("REAL", al.LeafType!.Name);
        Assert.Same(c.Tags["G"], al.Tag);
        Assert.Equal("G.Items[1].Val", al.ExpandedPath.ToLogixText());

        var alBit = r.Resolve(Path("AlBit"), p);
        Assert.True(alBit.IsBit);
        Assert.Equal(7, alBit.BitNumber);

        var missing = r.Resolve(Path("Nope.X"), p);
        Assert.False(missing.IsResolved);
        Assert.Null(missing.Tag);

        var aoi = c.FindAoi("MyAoi")!;
        var local = r.Resolve(Path("Work.Count"), aoi);
        Assert.Equal("DINT", local.LeafType!.Name);
        Assert.Equal(TagScope.AoiLocal, local.Scope);
        var param = r.Resolve(Path("In"), aoi);
        Assert.Equal(TagScope.AoiParameter, param.Scope);
    }

    [Fact]
    public void ModuleAndSystemTags()
    {
        var (r, c) = Setup();
        var p = c.FindProgram("P");

        var io = r.Resolve(Path("Local:3:I.Data.4"), p);
        Assert.True(io.IsResolved, io.Error);
        Assert.Equal("DI_Card", io.Module!.Name);
        Assert.True(io.IsBit);
        Assert.Equal("DINT", io.BitHostType!.Name);
        Assert.Equal(TagScope.Module, io.Scope);

        var byName = r.Resolve(Path("DI_Card:I.Fault"), p);
        Assert.True(byName.IsResolved, byName.Error);
        Assert.True(byName.IsIoData);
        var ioType = Assert.IsType<StructuredType>(c.Types.TryGet("AB:1756_DI:I:0"));
        Assert.Equal(DataTypeClass.ModuleDefined, ioType.Class);
        Assert.NotNull(ioType.ModuleType);
        Assert.Equal("AB", ioType.ModuleType!.Vendor);
        Assert.Equal("1756_DI", ioType.ModuleType.Product);
        Assert.Equal(IoDataKind.Input, ioType.ModuleType.Kind);
        Assert.Same(ioType, byName.IoType);
        Assert.False(r.Resolve(Path("G.Count"), p).IsIoData);

        var thirdParty = ModuleTypeInfo.TryParse("_0015:935700_001_B2B4CBB0:C:0");
        Assert.NotNull(thirdParty);
        Assert.True(thirdParty!.IsThirdParty);
        Assert.Equal(IoDataKind.Config, thirdParty.Kind);
        Assert.Equal("935700_001_B2B4CBB0", thirdParty.Product);
        Assert.Equal(IoDataKind.SafetyInput, ModuleTypeInfo.TryParse("AB:1734_IB8S_Safety2:SI:0")!.Kind);
        Assert.Null(ModuleTypeInfo.TryParse("TIMER"));

        var fs = r.Resolve(Path("S:FS"), p);
        Assert.True(fs.IsBit);
        Assert.Null(fs.Error);

        // AXIS_CIP_DRIVE has a documented member list now; unknown members on it stay opaque instead of failing.
        var axis = r.Resolve(Path("Axis.ActualPosition"), p);
        Assert.True(axis.IsResolved, axis.Error);
        Assert.Equal("REAL", axis.LeafType!.Name);
        var unknown = r.Resolve(Path("Axis.NoSuchMember"), p);
        Assert.Null(unknown.Error);
        Assert.True(unknown.IsOpaque);
        Assert.Null(unknown.LeafType);
    }
}

/// <summary>Integration check that every rung operand in a real sample project resolves through <c>ScopeResolver</c> without error.</summary>
[Collection("Samples")]
[Trait("Category", "Integration")]
public class ScopeResolverSampleTests
{
    private readonly SampleFileFixture _fx;
    private readonly ITestOutputHelper _out;

    public ScopeResolverSampleTests(SampleFileFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Theory]
    [InlineData(SampleFileFixture.Aib102)]
    [InlineData(SampleFileFixture.Stlib106)]
    [InlineData(SampleFileFixture.Stl107)]
    public void EveryRungOperandResolves(string file)
    {
        if (!_fx.Has(file)) return;
        var project = _fx.Get(file);
        var resolver = new ScopeResolver(project.Controller);
        int total = 0, resolved = 0, opaque = 0, failed = 0;
        var failures = new Dictionary<string, int>();
        foreach (var routine in project.Controller.AllRoutines)
        {
            var paths = new List<OperandPath>();
            switch (routine.Content)
            {
                case RllContent rll:
                    foreach (var rung in rll.Rungs)
                        foreach (var instr in rung.Ast!.AllInstructions())
                            foreach (var arg in instr.Arguments)
                            {
                                if (arg is TagRefOperand t) paths.Add(t.Path);
                                else if (arg is ExpressionOperand e) CollectVariables(e.Expression, paths);
                            }
                    break;
                case StContent st when st.Ast is not null:
                    CollectVariables(st.Ast, paths);
                    break;
                case Model.Logic.Sfc.SfcContent sfc:
                    foreach (var step in sfc.Steps)
                        foreach (var a in step.Actions)
                            if (a.Body?.Ast is not null) CollectVariables(a.Body.Ast, paths);
                    foreach (var tr in sfc.Transitions)
                        if (tr.Condition?.Expression is not null) CollectVariables(tr.Condition.Expression, paths);
                    break;
            }
            foreach (var path in paths)
            {
                {
                    {
                        var t = new { Path = path };
                        total++;
                        var r = resolver.Resolve(t.Path, routine.Owner);
                        if (r.IsResolved || r.IsModuleReference) resolved++;
                        else if (r.Error is null && r.IsOpaque)
                        {
                            opaque++;
                            var key = "opaque: " + (r.Tag?.DataType?.Name ?? r.ModuleData?.DataTypeName ?? "?") + " " + t.Path.ToLogixText();
                            failures[key] = failures.GetValueOrDefault(key) + 1;
                        }
                        else
                        {
                            failed++;
                            var key = r.Error ?? "unknown";
                            failures[key] = failures.GetValueOrDefault(key) + 1;
                        }
                    }
                }
            }
        }
        _out.WriteLine($"{file}: {total} tag operands, {resolved} resolved, {opaque} opaque, {failed} failed");
        foreach (var kv in failures.OrderByDescending(k => k.Value).Take(40)) _out.WriteLine($"  {kv.Value,6}  {kv.Key}");
        Assert.True(failed == 0, $"{failed} of {total} tag operands failed to resolve");
    }

    private static void CollectVariables(Model.Logic.St.StBlock block, List<OperandPath> into)
    {
        foreach (var s in block.Statements) CollectVariables(s, into);
    }

    private static void CollectVariables(Model.Logic.St.StStatement s, List<OperandPath> into)
    {
        switch (s)
        {
            case Model.Logic.St.StAssignment a:
                CollectVariables(a.Target, into);
                CollectVariables(a.Value, into);
                break;
            case Model.Logic.St.StCallStatement c:
                foreach (var arg in c.Arguments) if (arg is not null) CollectVariables(arg, into);
                break;
            case Model.Logic.St.StIf i:
                foreach (var b in i.Branches) { CollectVariables(b.Condition, into); CollectVariables(b.Body, into); }
                if (i.Else is not null) CollectVariables(i.Else, into);
                break;
            case Model.Logic.St.StCase c:
                CollectVariables(c.Selector, into);
                foreach (var arm in c.Arms) CollectVariables(arm.Body, into);
                if (c.Else is not null) CollectVariables(c.Else, into);
                break;
            case Model.Logic.St.StFor f:
                CollectVariables(f.Variable, into);
                CollectVariables(f.From, into);
                CollectVariables(f.To, into);
                if (f.By is not null) CollectVariables(f.By, into);
                CollectVariables(f.Body, into);
                break;
            case Model.Logic.St.StWhile w:
                CollectVariables(w.Condition, into);
                CollectVariables(w.Body, into);
                break;
            case Model.Logic.St.StRepeat r:
                CollectVariables(r.Body, into);
                CollectVariables(r.Until, into);
                break;
        }
    }

    private static void CollectVariables(Model.Logic.St.StExpression e, List<OperandPath> into)
    {
        switch (e)
        {
            case Model.Logic.St.StVariable v:
                into.Add(v.Path);
                foreach (var seg in v.Path.Segments)
                {
                    if (seg is IndexSegment idx) foreach (var i in idx.Indices) CollectVariables(i, into);
                    if (seg is DynamicBitSegment db) CollectVariables(db.Bit, into);
                }
                break;
            case Model.Logic.St.StUnary u:
                CollectVariables(u.Operand, into);
                break;
            case Model.Logic.St.StBinary b:
                CollectVariables(b.Left, into);
                CollectVariables(b.Right, into);
                break;
            case Model.Logic.St.StCall c:
                foreach (var arg in c.Arguments) if (arg is not null) CollectVariables(arg, into);
                break;
        }
    }
}
