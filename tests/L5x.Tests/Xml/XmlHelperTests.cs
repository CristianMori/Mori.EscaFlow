using System.Xml;
using L5x.Model.Tags;
using L5x.Model.Values;
using L5x.Parsing;
using L5x.Parsing.Xml;
using Xunit;

namespace L5x.Tests.Xml;

public class XmlHelperTests
{
    [Theory]
    [InlineData("\r\npayload\r\n", "payload")]
    [InlineData("\npayload\n", "payload")]
    [InlineData("\n\t\tindented\n", "\t\tindented")]
    [InlineData("\nline1\nline2\n", "line1\nline2")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CdataNormalizeStripsExactlyOneNewlineEachSide(string? input, string expected)
    {
        Assert.Equal(expected, CdataText.Normalize(input));
    }

    [Fact]
    public void AttributeBagTracksLeftovers()
    {
        using var reader = XmlReader.Create(new StringReader("<Tag Name=\"A\" DataType=\"DINT\" Dimensions=\"5 10\" Constant=\"true\" Weird=\"x\"/>"));
        reader.MoveToContent();
        var bag = new AttributeBag(reader);
        Assert.Equal("A", bag.Take("Name"));
        Assert.Equal("DINT", bag.Take("DataType"));
        Assert.Equal(new[] { 5, 10 }, bag.TakeDimensions("Dimensions"));
        Assert.True(bag.TakeBool("Constant"));
        Assert.Null(bag.Take("Missing"));
        var rest = bag.Remaining();
        Assert.NotNull(rest);
        Assert.Equal("x", Assert.Single(rest!).Value);
    }

    private const string TagXml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller">
        <Controller Name="C">
        <DataTypes>
        <DataType Name="U" Family="NoFamily" Class="User">
        <Description>
        <![CDATA[A type]]>
        </Description>
        <Members>
        <Member Name="A" DataType="DINT" Dimension="0" Radix="Decimal" Hidden="false" ExternalAccess="Read/Write"/>
        <Member Name="ZZZZZZZZZZU0" DataType="SINT" Dimension="0" Radix="Decimal" Hidden="true" ExternalAccess="Read/Write"/>
        <Member Name="B" DataType="BIT" Dimension="0" Radix="Decimal" Hidden="false" Target="ZZZZZZZZZZU0" BitNumber="0" ExternalAccess="Read/Write"/>
        </Members>
        <CustomProperties><Provider ID="ACM"><Data><Val><![CDATA[<x/>]]></Val></Data></Provider></CustomProperties>
        </DataType>
        </DataTypes>
        <Tags>
        <Tag Name="T1" TagType="Base" DataType="U" Radix="Decimal" Constant="false" ExternalAccess="Read/Write">
        <Description>
        <![CDATA[desc]]>
        </Description>
        <Comments>
        <Comment Operand=".B">
        <![CDATA[bit b]]>
        </Comment>
        </Comments>
        <CustomProperties><Provider ID="ACM"><Data><Val><![CDATA[junk]]></Val></Data></Provider></CustomProperties>
        <Data Format="L5K">
        <![CDATA[[7,1]]]>
        </Data>
        <Data Format="Decorated">
        <Structure DataType="U">
        <DataValueMember Name="A" DataType="DINT" Radix="Decimal" Value="7"/>
        <DataValueMember Name="B" DataType="BOOL" Value="1"/>
        </Structure>
        </Data>
        </Tag>
        <Tag Name="S" TagType="Base" DataType="STRING" Constant="false" ExternalAccess="Read/Write">
        <Data Format="L5K">
        <![CDATA[[3,'abc$00$00']]]>
        </Data>
        <Data Format="String" Length="3">
        <![CDATA['abc']]>
        </Data>
        </Tag>
        <Tag Name="Al" TagType="Alias" AliasFor="T1.B" ExternalAccess="Read/Write"/>
        <Tag Name="M" TagType="Base" DataType="MESSAGE" ExternalAccess="Read/Write">
        <Data Format="Message">
        <MessageParameters MessageType="CIP Generic" RequestedLength="52"/>
        </Data>
        </Tag>
        </Tags>
        <Programs/>
        <Tasks/>
        </Controller>
        </RSLogix5000Content>
        """;

    [Fact]
    public void TagsDataTypesAndCustomPropertiesAreHandled()
    {
        var project = L5xParser.ParseText(TagXml, new ParseOptions { ValidateL5kAgainstDecorated = true });
        Assert.Empty(project.Diagnostics);
        Assert.Equal(2, project.Statistics.SkippedCustomProperties);

        var c = project.Controller;
        var u = c.DataTypes["U"]!;
        Assert.Equal("A type", u.Description);
        Assert.Equal(3, u.Members.Count);
        Assert.Same(u.Members[1], u.Members[2].BitHost);

        var t1 = c.Tags["T1"]!;
        Assert.Equal("desc", t1.Description);
        Assert.Equal("bit b", Assert.Single(t1.Comments).Text);
        Assert.NotNull(t1.Comments[0].Path);
        var sv = Assert.IsType<StructValue>(t1.Data.Decorated);
        Assert.Equal(7, ((ScalarValue)sv["A"]!).Bits);
        Assert.NotNull(t1.Data.L5k);
        Assert.Same(u, t1.DataType!.Definition);

        var s = c.Tags["S"]!;
        var str = Assert.IsType<StringValue>(s.Data.Value);
        Assert.Equal("abc", str.Text);
        Assert.Equal(3, str.Length);

        var al = c.Tags["Al"]!;
        Assert.Same(t1, al.AliasTarget);
        Assert.Equal("T1.B", al.AliasPath!.ToLogixText());

        var m = c.Tags["M"]!;
        Assert.NotNull(m.Data.Message);
        Assert.Equal("CIP Generic", m.Data.Message!.MessageType);
        Assert.Equal(52, m.Data.Message.RequestedLength);
        Assert.NotNull(m.Data.MessageElement);
    }

    [Fact]
    public void ValuesCanBeSkipped()
    {
        var project = L5xParser.ParseText(TagXml, new ParseOptions { ParseValues = ValueParseMode.None });
        var t1 = project.Controller.Tags["T1"]!;
        Assert.Null(t1.Data.Decorated);
        Assert.Null(t1.Data.L5k);
    }

    [Fact]
    public void MalformedXmlThrowsParseException()
    {
        var ex = Assert.Throws<L5x.Diagnostics.L5xParseException>(() => L5xParser.ParseText("<RSLogix5000Content><Controller Name=\"x\"><Tags><Tag Name=\"a\"></Tags></Controller></RSLogix5000Content>"));
        Assert.NotNull(ex.LineNumber);
    }

    [Fact]
    public void GenericElementReaderKeepsEverything()
    {
        using var reader = XmlReader.Create(new StringReader("<A x=\"1\"><B y=\"2\">text</B><CustomProperties><P/></CustomProperties><C/></A>"));
        reader.MoveToContent();
        var g = GenericElementReader.Read(reader);
        Assert.Equal("A", g.Name);
        Assert.Equal("1", g["x"]);
        Assert.Equal(2, g.Children.Count);
        Assert.Equal("text", g.Child("B")!.Text);
        Assert.Null(g.Child("CustomProperties"));
    }
}
