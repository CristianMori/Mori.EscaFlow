using System.Xml;
using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Values;

public class DecoratedDataReaderTests
{
    private static (TagValue? Value, DiagnosticBag Diagnostics) Read(string xml)
    {
        var bag = new DiagnosticBag();
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { IgnoreWhitespace = true });
        reader.MoveToContent();
        var v = DecoratedDataReader.Read(reader, bag);
        return (v, bag);
    }

    [Fact]
    public void ScalarDint()
    {
        var (v, d) = Read("<Data Format=\"Decorated\"><DataValue DataType=\"DINT\" Radix=\"Decimal\" Value=\"-21\"/></Data>");
        Assert.Empty(d);
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal(AtomicKind.Dint, s.Kind);
        Assert.Equal(-21, s.Bits);
        Assert.Equal(Radix.Decimal, s.Radix);
    }

    [Fact]
    public void ScalarBoolWithoutRadix()
    {
        var (v, _) = Read("<DataValue DataType=\"BOOL\" Value=\"1\"/>");
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal(AtomicKind.Bool, s.Kind);
        Assert.True(s.AsBool);
    }

    [Fact]
    public void ScalarRealExponent()
    {
        var (v, d) = Read("<DataValue DataType=\"REAL\" Radix=\"Float\" Value=\"1.5e+2\"/>");
        Assert.Empty(d);
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal(150.0, s.Real);
        Assert.Equal(Radix.Float, s.Radix);
    }

    [Fact]
    public void ScalarHexAndBinary()
    {
        var (h, _) = Read("<DataValue DataType=\"SINT\" Radix=\"Hex\" Value=\"16#00\"/>");
        Assert.Equal(0, Assert.IsType<ScalarValue>(h).Bits);
        var (h2, _) = Read("<DataValue DataType=\"SINT\" Radix=\"Hex\" Value=\"16#ff\"/>");
        Assert.Equal(-1, Assert.IsType<ScalarValue>(h2).Bits); // SINT sign-extends
        var (b, _) = Read("<DataValue DataType=\"SINT\" Radix=\"Binary\" Value=\"2#0000_1111\"/>");
        var bs = Assert.IsType<ScalarValue>(b);
        Assert.Equal(15, bs.Bits);
        Assert.Equal(Radix.Binary, bs.Radix);
    }

    [Fact]
    public void ScalarAscii()
    {
        var (v, d) = Read("<DataValue DataType=\"SINT\" Radix=\"ASCII\" Value=\"'$00'\"/>");
        Assert.Empty(d);
        Assert.Equal(0, Assert.IsType<ScalarValue>(v).Bits);
        var (v2, _) = Read("<DataValue DataType=\"INT\" Radix=\"ASCII\" Value=\"'$00$0F'\"/>");
        Assert.Equal(15, Assert.IsType<ScalarValue>(v2).Bits);
        var (v3, _) = Read("<DataValue DataType=\"SINT\" Radix=\"ASCII\" Value=\"'A'\"/>");
        Assert.Equal(65, Assert.IsType<ScalarValue>(v3).Bits);
    }

    [Fact]
    public void ScalarDateTimeKeptAsRawText()
    {
        var (v, d) = Read("<DataValue DataType=\"LINT\" Radix=\"Date/Time\" Value=\"DT#2023-07-19-15:24:57.123_456\"/>");
        Assert.Empty(d);
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal("DT#2023-07-19-15:24:57.123_456", s.RawText);
    }

    [Fact]
    public void UnparseableScalarReportsDEC002()
    {
        var (v, d) = Read("<DataValue DataType=\"DINT\" Radix=\"Decimal\" Value=\"abc\"/>");
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal("abc", s.RawText);
        Assert.Contains(d, x => x.Code == DecoratedDataReader.CodeBadValue);
    }

    [Fact]
    public void ForceValue()
    {
        var (v, _) = Read("<DataValue DataType=\"DINT\" Radix=\"Decimal\" Value=\"3\" ForceValue=\"7\"/>");
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal(3, s.Bits);
        Assert.Equal("7", s.ForceValue);
    }

    [Fact]
    public void OneDimensionalArray()
    {
        var xml = "<Data Format=\"Decorated\"><Array DataType=\"SINT\" Dimensions=\"4\" Radix=\"Decimal\">" +
                  "<Element Index=\"[0]\" Value=\"2\"/><Element Index=\"[1]\" Value=\"3\"/><Element Index=\"[2]\" Value=\"4\"/><Element Index=\"[3]\" Value=\"-5\"/>" +
                  "</Array></Data>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal("SINT", a.ElementType);
        Assert.Equal(new[] { 4 }, a.Dimensions);
        Assert.Equal(new long[] { 2, 3, 4, -5 }, a.Elements.Select(e => ((ScalarValue)e).Bits).ToArray());
    }

    [Fact]
    public void TwoDimensionalArrayRowMajor()
    {
        var xml = "<Array DataType=\"DINT\" Dimensions=\"2 3\" Radix=\"Decimal\">" +
                  "<Element Index=\"[0,0]\" Value=\"1\"/><Element Index=\"[0,1]\" Value=\"2\"/><Element Index=\"[0,2]\" Value=\"3\"/>" +
                  "<Element Index=\"[1,0]\" Value=\"4\"/><Element Index=\"[1,1]\" Value=\"5\"/><Element Index=\"[1,2]\" Value=\"6\"/>" +
                  "</Array>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal(new[] { 2, 3 }, a.Dimensions);
        Assert.Equal(6, a.TotalLength);
        Assert.Equal(5, ((ScalarValue)a.At(1, 1)!).Bits);
        Assert.Equal(6, ((ScalarValue)a.Elements[5]).Bits);
    }

    [Fact]
    public void MissingElementsFilledAndBadIndexReported()
    {
        var xml = "<Array DataType=\"DINT\" Dimensions=\"3\" Radix=\"Decimal\"><Element Index=\"[1]\" Value=\"9\"/><Element Index=\"[7]\" Value=\"1\"/></Array>";
        var (v, d) = Read(xml);
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal(3, a.Elements.Count);
        Assert.Equal(0, ((ScalarValue)a.Elements[0]).Bits);
        Assert.Equal(9, ((ScalarValue)a.Elements[1]).Bits);
        Assert.Contains(d, x => x.Code == DecoratedDataReader.CodeBadIndex);
    }

    [Fact]
    public void TimerStructure()
    {
        var xml = "<Data Format=\"Decorated\"><Structure DataType=\"TIMER\">" +
                  "<DataValueMember Name=\"PRE\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"1\"/>" +
                  "<DataValueMember Name=\"ACC\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"5\"/>" +
                  "<DataValueMember Name=\"EN\" DataType=\"BOOL\" Value=\"0\"/>" +
                  "<DataValueMember Name=\"TT\" DataType=\"BOOL\" Value=\"0\"/>" +
                  "<DataValueMember Name=\"DN\" DataType=\"BOOL\" Value=\"1\"/>" +
                  "</Structure></Data>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var s = Assert.IsType<StructValue>(v);
        Assert.Equal("TIMER", s.DataType);
        Assert.Equal(new[] { "PRE", "ACC", "EN", "TT", "DN" }, s.Members.Select(m => m.Key).ToArray());
        Assert.Equal(5, ((ScalarValue)s["ACC"]!).Bits);
        Assert.True(((ScalarValue)s["dn"]!).AsBool);
    }

    [Fact]
    public void NestedArrayMemberAndStructureMember()
    {
        var xml = "<Structure DataType=\"Outer\">" +
                  "<DataValueMember Name=\"Id\" DataType=\"INT\" Radix=\"Decimal\" Value=\"3918\"/>" +
                  "<ArrayMember Name=\"Data\" DataType=\"SINT\" Dimensions=\"2\" Radix=\"Hex\"><Element Index=\"[0]\" Value=\"16#01\"/><Element Index=\"[1]\" Value=\"16#02\"/></ArrayMember>" +
                  "<StructureMember Name=\"Timer\" DataType=\"TIMER\">" +
                  "<DataValueMember Name=\"PRE\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"10\"/>" +
                  "<DataValueMember Name=\"ACC\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/>" +
                  "<DataValueMember Name=\"EN\" DataType=\"BOOL\" Value=\"0\"/><DataValueMember Name=\"TT\" DataType=\"BOOL\" Value=\"0\"/><DataValueMember Name=\"DN\" DataType=\"BOOL\" Value=\"0\"/>" +
                  "</StructureMember>" +
                  "<ArrayMember Name=\"Timers\" DataType=\"TIMER\" Dimensions=\"1\"><Element Index=\"[0]\"><Structure DataType=\"TIMER\">" +
                  "<DataValueMember Name=\"PRE\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"7\"/>" +
                  "<DataValueMember Name=\"ACC\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/>" +
                  "<DataValueMember Name=\"EN\" DataType=\"BOOL\" Value=\"0\"/><DataValueMember Name=\"TT\" DataType=\"BOOL\" Value=\"0\"/><DataValueMember Name=\"DN\" DataType=\"BOOL\" Value=\"0\"/>" +
                  "</Structure></Element></ArrayMember>" +
                  "</Structure>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var s = Assert.IsType<StructValue>(v);
        Assert.Equal(4, s.Members.Count);
        var data = Assert.IsType<ArrayValue>(s["Data"]);
        Assert.Equal(Radix.Hex, data.Radix);
        Assert.Equal(2, ((ScalarValue)data.Elements[1]).Bits);
        var timer = Assert.IsType<StructValue>(s["Timer"]);
        Assert.Equal(10, ((ScalarValue)timer["PRE"]!).Bits);
        var timers = Assert.IsType<ArrayValue>(s["Timers"]);
        var t0 = Assert.IsType<StructValue>(timers.Elements[0]);
        Assert.Equal(7, ((ScalarValue)t0["PRE"]!).Bits);
    }

    [Fact]
    public void StringMemberWithCdataCollapsesToStringValue()
    {
        var xml = "<Structure DataType=\"Holder\">" +
                  "<StructureMember Name=\"Name\" DataType=\"STRING\">" +
                  "<DataValueMember Name=\"LEN\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"3\"/>" +
                  "<DataValueMember Name=\"DATA\" DataType=\"STRING\" Radix=\"ASCII\"><![CDATA['abc']]></DataValueMember>" +
                  "</StructureMember>" +
                  "</Structure>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var s = Assert.IsType<StructValue>(v);
        var str = Assert.IsType<StringValue>(s["Name"]);
        Assert.Equal(3, str.Length);
        Assert.Equal("abc", str.Text);
        Assert.Equal("STRING", str.DataType);
    }

    [Fact]
    public void TopLevelStringStructureCollapses()
    {
        var xml = "<Data Format=\"Decorated\"><Structure DataType=\"STRING\">\r\n" +
                  "<DataValueMember Name=\"LEN\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"8\"/>\r\n" +
                  "<DataValueMember Name=\"DATA\" DataType=\"STRING\" Radix=\"ASCII\">\r\n<![CDATA['10201-20$00']]>\r\n</DataValueMember>\r\n" +
                  "</Structure></Data>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var str = Assert.IsType<StringValue>(v);
        Assert.Equal(8, str.Length);
        Assert.Equal("10201-20", str.Text);
    }

    [Fact]
    public void ArrayOfStringsCollapsesEachElement()
    {
        var xml = "<Array DataType=\"STRING\" Dimensions=\"2\">" +
                  "<Element Index=\"[0]\"><Structure DataType=\"STRING\"><DataValueMember Name=\"LEN\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"1\"/><DataValueMember Name=\"DATA\" DataType=\"STRING\" Radix=\"ASCII\"><![CDATA['x']]></DataValueMember></Structure></Element>" +
                  "<Element Index=\"[1]\"><Structure DataType=\"STRING\"><DataValueMember Name=\"LEN\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/><DataValueMember Name=\"DATA\" DataType=\"STRING\" Radix=\"ASCII\"><![CDATA['']]></DataValueMember></Structure></Element>" +
                  "</Array>";
        var (v, d) = Read(xml);
        Assert.Empty(d);
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal("x", Assert.IsType<StringValue>(a.Elements[0]).Text);
        Assert.Equal("", Assert.IsType<StringValue>(a.Elements[1]).Text);
    }

    [Fact]
    public void UnknownElementReportsDEC001AndIsSkipped()
    {
        var xml = "<Structure DataType=\"X\"><Bogus Name=\"q\"><Inner/></Bogus><DataValueMember Name=\"A\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"1\"/></Structure>";
        var (v, d) = Read(xml);
        var s = Assert.IsType<StructValue>(v);
        Assert.Single(s.Members);
        Assert.Contains(d, x => x.Code == DecoratedDataReader.CodeUnknownElement);
    }

    [Fact]
    public void ReaderAdvancesPastElement()
    {
        var xml = "<Tag><Data Format=\"Decorated\"><DataValue DataType=\"DINT\" Radix=\"Decimal\" Value=\"1\"/></Data><Next/></Tag>";
        var bag = new DiagnosticBag();
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { IgnoreWhitespace = true });
        reader.MoveToContent();
        reader.ReadStartElement("Tag");
        var v = DecoratedDataReader.Read(reader, bag);
        Assert.IsType<ScalarValue>(v);
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
        Assert.Equal("Next", reader.LocalName);
    }

    [Fact]
    public void ReadFromElementWorks()
    {
        var el = XElement.Parse("<Data Format=\"Decorated\"><DataValue DataType=\"INT\" Radix=\"Decimal\" Value=\"5\"/></Data>");
        var bag = new DiagnosticBag();
        var v = DecoratedDataReader.ReadFromElement(el, bag);
        Assert.Equal(5, Assert.IsType<ScalarValue>(v).Bits);
    }

    [Fact]
    public void InternerSharesCommonScalars()
    {
        var (a, _) = Read("<DataValue DataType=\"BOOL\" Value=\"0\"/>");
        var (b, _) = Read("<DataValue DataType=\"BOOL\" Value=\"0\"/>");
        Assert.Same(a, b);
        var (c, _) = Read("<DataValue DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/>");
        var (d, _) = Read("<DataValue DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/>");
        Assert.Same(c, d);
    }

    [Fact]
    public void EmptyDataElementReturnsNull()
    {
        var (v, d) = Read("<Data Format=\"Decorated\"/>");
        Assert.Null(v);
        Assert.Empty(d);
    }
}
