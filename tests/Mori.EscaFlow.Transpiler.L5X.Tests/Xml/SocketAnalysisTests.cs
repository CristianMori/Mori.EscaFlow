using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Tests.Integration;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Xml;

/// <summary>
/// Proves that <c>SocketAnalysis</c> binds a socket-service MESSAGE tag to its request structure purely by that
/// structure's data type, and locates the conventional field paths (type, address, buffer, ...) by member name
/// rather than by reading any run-time value.
/// </summary>
public class SocketAnalysisTests
{
    /// <summary>The ENET-AT002 sample layout: one UDT holding every request structure of a socket. No initial values on purpose: bindings come from the types.</summary>
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T">
        <DataTypes>
        <DataType Name="SKT_Address" Family="NoFamily" Class="User"><Members>
        <Member Name="Family" DataType="INT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="Port" DataType="INT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="IP_4th_Octect" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="IP_3rd_Octect" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="IP_2nd_Octect" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="IP_1st_Octect" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        </Members></DataType>
        <DataType Name="SKT_Create_Source" Family="NoFamily" Class="User"><Members>
        <Member Name="Timeout" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="Type" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="Addr" DataType="SKT_Address" Dimension="0" Radix="NullType" Hidden="false"/>
        </Members></DataType>
        <DataType Name="SKT_Open_Connection" Family="NoFamily" Class="User"><Members>
        <Member Name="Timeout" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="Dest_Addr" DataType="STRING" Dimension="0" Radix="NullType" Hidden="false"/>
        </Members></DataType>
        <DataType Name="SKT_Write_Source" Family="NoFamily" Class="User"><Members>
        <Member Name="Timeout" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="ToAddr" DataType="SKT_Address" Dimension="0" Radix="NullType" Hidden="false"/>
        <Member Name="Buffer" DataType="STRING" Dimension="0" Radix="NullType" Hidden="false"/>
        </Members></DataType>
        <DataType Name="SKT_Data" Family="NoFamily" Class="User"><Members>
        <Member Name="Create_Source" DataType="SKT_Create_Source" Dimension="0" Radix="NullType" Hidden="false"/>
        <Member Name="Instance" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false"/>
        <Member Name="Open_Connection" DataType="SKT_Open_Connection" Dimension="0" Radix="NullType" Hidden="false"/>
        <Member Name="Write_Source" DataType="SKT_Write_Source" Dimension="0" Radix="NullType" Hidden="false"/>
        </Members></DataType>
        </DataTypes>
        <Tags>
        <Tag Name="MsgData" TagType="Base" DataType="SKT_Data" Radix="NullType"/>
        <Tag Name="SKT_Create" TagType="Base" DataType="MESSAGE"><Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="14" ConnectedFlag="2" ConnectionPath="Enet" CommTypeCode="0" ServiceCode="16#004b" ObjectType="16#0342" TargetObject="0" AttributeNumber="16#0000" LocalIndex="0" LocalElement="MsgData.Create_Source"/>
        </Data></Tag>
        <Tag Name="SKT_Open" TagType="Base" DataType="MESSAGE"><Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="30" ConnectedFlag="2" ConnectionPath="Enet" CommTypeCode="0" ServiceCode="16#004c" ObjectType="16#0342" TargetObject="0" AttributeNumber="16#0000" LocalIndex="0" LocalElement="MsgData.Open_Connection"/>
        </Data></Tag>
        <Tag Name="SKT_Write" TagType="Base" DataType="MESSAGE"><Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="40" ConnectedFlag="2" ConnectionPath="Enet" CommTypeCode="0" ServiceCode="16#004e" ObjectType="16#0342" TargetObject="0" AttributeNumber="16#0000" LocalIndex="0" LocalElement="MsgData.Write_Source"/>
        </Data></Tag>
        <Tag Name="SKT_Delete" TagType="Base" DataType="MESSAGE"><Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="0" ConnectedFlag="2" ConnectionPath="Enet" CommTypeCode="0" ServiceCode="16#004f" ObjectType="16#0342" TargetObject="0" AttributeNumber="16#0000" LocalIndex="0"/>
        </Data></Tag>
        </Tags>
        <Programs/>
        <Tasks/>
        </Controller>
        </RSLogix5000Content>
        """;

    [Fact]
    public void BindsRequestStructuresAndFieldsByType()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics.Where(d => d.Severity != Diagnostics.DiagnosticSeverity.Info));
        var c = project.Controller;

        var create = c.Tags["SKT_Create"]!.Data.Message!.Socket!;
        Assert.Equal("MsgData", create.Session);
        Assert.Equal("MsgData.Create_Source", create.Request);
        Assert.Equal("SKT_Create_Source", create.RequestType);
        Assert.Equal("SKT_Create.Instance", create.Instance);
        Assert.Equal("MsgData.Create_Source.Type", create.Fields["type"]);
        Assert.Equal("MsgData.Create_Source.Timeout", create.Fields["timeout"]);
        Assert.Equal("MsgData.Create_Source.Addr.Port", create.Fields["localPort"]);
        Assert.Equal("MsgData.Create_Source.Addr.IP_1st_Octect", create.Fields["localAddressOctet1"]);
        Assert.Equal("MsgData.Create_Source.Addr.IP_4th_Octect", create.Fields["localAddressOctet4"]);

        var open = c.Tags["SKT_Open"]!.Data.Message!.Socket!;
        Assert.Equal("MsgData", open.Session);
        Assert.Equal("MsgData.Open_Connection.Dest_Addr", open.Fields["destination"]);

        var write = c.Tags["SKT_Write"]!.Data.Message!.Socket!;
        Assert.Equal("MsgData.Write_Source.Buffer", write.Fields["buffer"]);
        Assert.Equal("MsgData.Write_Source.ToAddr.Port", write.Fields["toPort"]);
        Assert.Equal("MsgData.Write_Source.ToAddr.IP_2nd_Octect", write.Fields["toAddressOctet2"]);

        var delete = c.Tags["SKT_Delete"]!.Data.Message!.Socket!;
        Assert.Null(delete.Request);
        Assert.Equal("MsgData", delete.Session); // joined the only session of the scope
        Assert.Equal("SKT_Delete.Instance", delete.Instance);

    }
}

/// <summary>Integration check that real sample exports with SNTP socket messages bind correctly end to end.</summary>
[Collection("Samples")]
[Trait("Category", "Integration")]
public class SocketSampleTests
{
    private readonly SampleFileFixture _fx;

    public SocketSampleTests(SampleFileFixture fx)
    {
        _fx = fx;
    }

    [Theory]
    [InlineData(SampleFileFixture.Stlib106)]
    [InlineData(SampleFileFixture.Aib102)]
    [InlineData(SampleFileFixture.Stl107)]
    public void SntpSocketMessagesAreBound(string file)
    {
        if (!_fx.Has(file)) return;
        var c = _fx.Get(file).Controller;
        var sockets = c.MessageTags.Where(t => t.Data.Message!.IsSocketService).Select(t => t.Data.Message!).ToList();
        Assert.Equal(5, sockets.Count);
        Assert.All(sockets, m => Assert.NotNull(m.Socket));
        Assert.Single(sockets.Select(m => m.Socket!.Session).Distinct());
        var create = sockets.Single(m => m.SocketService == SocketService.CreateSocket).Socket!;
        Assert.Contains("type", create.Fields.Keys);
        Assert.Contains("localPort", create.Fields.Keys);
        var open = sockets.Single(m => m.SocketService == SocketService.OpenConnection).Socket!;
        Assert.Contains("destination", open.Fields.Keys);
        var read = sockets.Single(m => m.SocketService == SocketService.ReadSocket).Socket!;
        Assert.Contains("bufferLength", read.Fields.Keys);
        var write = sockets.Single(m => m.SocketService == SocketService.WriteSocket).Socket!;
        Assert.Contains("buffer", write.Fields.Keys);
        Assert.Contains("toPort", write.Fields.Keys);
        Assert.All(sockets, m => Assert.NotNull(m.Socket!.Instance));
    }
}
