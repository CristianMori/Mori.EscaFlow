using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler.L5X.Tests.Integration;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Xml;

/// <summary>
/// Proves that <c>MessageConfiguration.FromAttributes</c> reads CIP Generic, data-table and unconfigured MESSAGE
/// tags correctly, and that <c>IsSocketService</c>/<c>SocketService</c> recognize messages addressed to the
/// Socket Object by class and service code.
/// </summary>
public class MessageConfigurationTests
{
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T">
        <DataTypes/>
        <Tags>
        <Tag Name="AccessEvent" TagType="Base" DataType="DINT" Dimensions="13" Radix="Decimal"/>
        <Tag Name="Reply" TagType="Base" DataType="SINT" Dimensions="100" Radix="Decimal"/>
        <Tag Name="AccessMessage" TagType="Base" DataType="MESSAGE" ExternalAccess="Read/Write">
        <Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="52" ConnectedFlag="2" ConnectionPath="2, 172.17.36.30" CommTypeCode="0" ServiceCode="16#0033" ObjectType="16#0002" TargetObject="1" AttributeNumber="16#0004" LocalIndex="0" LocalElement="AccessEvent" DestinationTag="Reply" LargePacketUsage="false" CacheConnections="true" Future="x"/>
        </Data>
        </Tag>
        <Tag Name="ReadMsg" TagType="Base" DataType="MESSAGE">
        <Data Format="Message">
        <MessageParameters MessageType="CIP Data Table Read" RequestedLength="10" ConnectionPath="1, 3" CommTypeCode="0" LocalIndex="0" LocalElement="AccessEvent" RemoteElement="RemoteArray[0]" RemoteIndex="0"/>
        </Data>
        </Tag>
        <Tag Name="SKT_Create" TagType="Base" DataType="MESSAGE">
        <Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="12" ConnectedFlag="2" ConnectionPath="SymNetwork" CommTypeCode="0" ServiceCode="16#004b" ObjectType="16#0342" TargetObject="0" AttributeNumber="16#0000" LocalIndex="0" LocalElement="AccessEvent" LargePacketUsage="false"/>
        </Data>
        </Tag>
        <Tag Name="SKT_Write" TagType="Base" DataType="MESSAGE">
        <Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="60" ConnectedFlag="2" ConnectionPath="SymNetwork" CommTypeCode="0" ServiceCode="16#004e" ObjectType="16#0342" TargetObject="28570" AttributeNumber="16#0000" LocalIndex="0" LocalElement="Reply" LargePacketUsage="false"/>
        </Data>
        </Tag>
        <Tag Name="Empty" TagType="Base" DataType="MESSAGE">
        <Data Format="Message">
        <MessageParameters MessageType="Unconfigured" RequestedLength="0" LocalIndex="0" CommTypeCode="0"/>
        </Data>
        </Tag>
        </Tags>
        <Programs/>
        <Tasks/>
        </Controller>
        </RSLogix5000Content>
        """;

    [Fact]
    public void ParsesCipGenericParameters()
    {
        var project = L5xParser.ParseText(Xml);
        Assert.Empty(project.Diagnostics.Where(d => d.Severity != Diagnostics.DiagnosticSeverity.Info));
        var c = project.Controller;
        Assert.Equal(5, c.MessageTags.Count());

        var m = c.Tags["AccessMessage"]!.Data.Message!;
        Assert.True(m.IsCipGeneric);
        Assert.True(m.IsConfigured);
        Assert.Equal(52, m.RequestedLength);
        Assert.Equal(0x33, m.ServiceCode);
        Assert.Equal(0x02, m.ObjectType);
        Assert.Equal(1, m.TargetObject);
        Assert.Equal(0x04, m.AttributeNumber);
        Assert.Equal(MessageCommType.Cip, m.CommType);
        Assert.Equal(new[] { "2", "172.17.36.30" }, m.ConnectionPathSegments);
        Assert.True(m.CacheConnections);
        Assert.False(m.LargePacketUsage);
        Assert.Same(c.Tags["AccessEvent"], m.LocalElementTag);
        Assert.Same(c.Tags["Reply"], m.DestinationTagRef);
        Assert.Equal("x", Assert.Single(m.UnknownAttributes!).Value);
        Assert.Equal("16#0033", m.Attributes["ServiceCode"]);
    }

    [Fact]
    public void ParsesDataTableReadAndUnconfigured()
    {
        var project = L5xParser.ParseText(Xml);
        var c = project.Controller;
        var r = c.Tags["ReadMsg"]!.Data.Message!;
        Assert.True(r.IsRead);
        Assert.False(r.IsWrite);
        Assert.False(r.IsCipGeneric);
        Assert.Equal("RemoteArray[0]", r.RemoteElement);
        Assert.Same(c.Tags["AccessEvent"], r.LocalElementTag);
        Assert.Null(r.DestinationTag);

        var e = c.Tags["Empty"]!.Data.Message!;
        Assert.False(e.IsConfigured);
        Assert.Null(e.LocalElement);
    }

    [Fact]
    public void SocketServicesAreRecognized()
    {
        var project = L5xParser.ParseText(Xml);
        var c = project.Controller;
        var create = c.Tags["SKT_Create"]!.Data.Message!;
        Assert.True(create.IsSocketService);
        Assert.Equal(SocketService.CreateSocket, create.SocketService);
        Assert.Equal(0, create.SocketInstance);
        var write = c.Tags["SKT_Write"]!.Data.Message!;
        Assert.Equal(SocketService.WriteSocket, write.SocketService);
        Assert.Equal(28570, write.SocketInstance);
        Assert.Equal(SocketService.None, c.Tags["AccessMessage"]!.Data.Message!.SocketService);

    }
}

/// <summary>Integration check that every MESSAGE tag in a real sample export ends up typed and, where applicable, linked to its local/destination tags.</summary>
[Collection("Samples")]
[Trait("Category", "Integration")]
public class MessageSampleTests
{
    private readonly SampleFileFixture _fx;

    public MessageSampleTests(SampleFileFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void EveryMessageTagIsTypedAndLinked()
    {
        if (!_fx.Has(SampleFileFixture.Aib102)) return;
        var c = _fx.Get(SampleFileFixture.Aib102).Controller;
        var msgs = c.MessageTags.ToList();
        Assert.NotEmpty(msgs);
        Assert.All(msgs, t => Assert.NotNull(t.Data.Message));
        var configured = msgs.Where(t => t.Data.Message!.IsConfigured).ToList();
        Assert.All(configured, t => Assert.True(t.Data.Message!.IsCipGeneric));
        Assert.All(configured.Where(t => t.Data.Message!.LocalElement is not null), t => Assert.NotNull(t.Data.Message!.LocalElementTag));
        Assert.All(configured, t => Assert.NotNull(t.Data.Message!.ServiceCode));
        Assert.All(msgs, t => Assert.Null(t.Data.Message!.UnknownAttributes));

        // The SNTP add-on uses the five standard socket services against the Ethernet module.
        var sockets = msgs.Where(t => t.Data.Message!.IsSocketService).Select(t => t.Data.Message!.SocketService).ToList();
        Assert.Equal(new[] { SocketService.CreateSocket, SocketService.OpenConnection, SocketService.ReadSocket, SocketService.WriteSocket, SocketService.DeleteSocket }, sockets.OrderBy(s => s));
    }
}
