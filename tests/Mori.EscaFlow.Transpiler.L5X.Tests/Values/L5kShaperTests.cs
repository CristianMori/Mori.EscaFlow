using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;
using Xunit;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Values;

public class L5kShaperTests
{
    private static DataTypeRegistry Registry()
    {
        var r = new DataTypeRegistry();
        BuiltInTypes.RegisterInto(r);
        return r;
    }

    private static TagValue? Shape(string l5k, string type, int[]? dims, DataTypeRegistry? registry = null, DiagnosticBag? bag = null)
    {
        bag ??= new DiagnosticBag();
        var value = L5kValueParser.Parse(l5k, bag);
        return L5kShaper.Shape(value, type, dims ?? Array.Empty<int>(), registry ?? Registry(), bag);
    }

    [Fact]
    public void AtomicScalar()
    {
        var v = Shape("21", "DINT", null);
        var s = Assert.IsType<ScalarValue>(v);
        Assert.Equal(AtomicKind.Dint, s.Kind);
        Assert.Equal(21, s.Bits);
    }

    [Fact]
    public void RealFromL5kText()
    {
        var s = Assert.IsType<ScalarValue>(Shape("1.50000000e+002", "REAL", null));
        Assert.Equal(150.0, s.Real);
        Assert.Equal(Radix.Float, s.Radix);
    }

    [Fact]
    public void TimerFromList()
    {
        // L5K writes a TIMER as [status, PRE, ACC]; EN/TT/DN live in bits 31/30/29 of the hidden status word.
        var bag = new DiagnosticBag();
        var v = Shape("[0,5,10]", "TIMER", null, null, bag);
        Assert.Empty(bag);
        var s = Assert.IsType<StructValue>(v);
        Assert.Equal("TIMER", s.DataType);
        Assert.Equal(new[] { "PRE", "ACC", "EN", "TT", "DN" }, s.Members.Select(m => m.Key).ToArray());
        Assert.Equal(5, ((ScalarValue)s["PRE"]!).Bits);
        Assert.Equal(10, ((ScalarValue)s["ACC"]!).Bits);
        Assert.False(((ScalarValue)s["DN"]!).AsBool);

        var running = Assert.IsType<StructValue>(Shape("[1073741824,5,3]", "TIMER", null));
        Assert.False(((ScalarValue)running["EN"]!).AsBool);
        Assert.True(((ScalarValue)running["TT"]!).AsBool);
        Assert.False(((ScalarValue)running["DN"]!).AsBool);
    }

    [Fact]
    public void StringFromLenAndText()
    {
        var v = Shape("[3,'abc$00']", "STRING", null);
        var s = Assert.IsType<StringValue>(v);
        Assert.Equal(3, s.Length);
        Assert.Equal("abc", s.Text);
        Assert.Equal("STRING", s.DataType);
    }

    [Fact]
    public void UserStringTypeWithoutFamilyFlag()
    {
        var reg = Registry();
        var t = new StructuredType { Name = "String32", Class = DataTypeClass.User };
        t.Members.Add(new DataTypeMember { Name = "LEN", DataType = DataTypeRef.Of("DINT") });
        t.Members.Add(new DataTypeMember { Name = "DATA", DataType = DataTypeRef.Of("SINT"), Dimension = 32, Radix = Radix.Ascii });
        reg.Register(t);
        var s = Assert.IsType<StringValue>(Shape("[2,'hi$00$00']", "String32", null, reg));
        Assert.Equal("hi", s.Text);
        Assert.Equal("String32", s.DataType);
    }

    [Fact]
    public void HiddenBitHostProducesBoolMembers()
    {
        var reg = Registry();
        var t = new StructuredType { Name = "Flags", Class = DataTypeClass.User };
        t.Members.Add(new DataTypeMember { Name = "ZZZZZZZZZZFlags0", DataType = DataTypeRef.Of("SINT"), Hidden = true });
        t.Members.Add(new DataTypeMember { Name = "A", DataType = DataTypeRef.Of("BIT"), Target = "ZZZZZZZZZZFlags0", BitNumber = 0 });
        t.Members.Add(new DataTypeMember { Name = "B", DataType = DataTypeRef.Of("BIT"), Target = "ZZZZZZZZZZFlags0", BitNumber = 1 });
        t.Members.Add(new DataTypeMember { Name = "C", DataType = DataTypeRef.Of("BIT"), Target = "ZZZZZZZZZZFlags0", BitNumber = 2 });
        t.Members.Add(new DataTypeMember { Name = "Count", DataType = DataTypeRef.Of("DINT") });
        reg.Register(t);

        var bag = new DiagnosticBag();
        var v = Shape("[5,42]", "Flags", null, reg, bag);
        Assert.Empty(bag);
        var s = Assert.IsType<StructValue>(v);
        Assert.Equal(new[] { "A", "B", "C", "Count" }, s.Members.Select(m => m.Key).ToArray());
        Assert.True(((ScalarValue)s["A"]!).AsBool);
        Assert.False(((ScalarValue)s["B"]!).AsBool);
        Assert.True(((ScalarValue)s["C"]!).AsBool);
        Assert.Equal(42, ((ScalarValue)s["Count"]!).Bits);
    }

    [Fact]
    public void TwoDimensionalArrayFromNestedLists()
    {
        var v = Shape("[[1,2,3],[4,5,6]]", "DINT", new[] { 2, 3 });
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal(new[] { 2, 3 }, a.Dimensions);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, a.Elements.Select(e => ((ScalarValue)e).Bits).ToArray());
        Assert.Equal(5, ((ScalarValue)a.At(1, 1)!).Bits);
    }

    [Fact]
    public void ArrayOfStructures()
    {
        var v = Shape("[[0,1,5],[0,2,7]]", "TIMER", new[] { 2 });
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal(2, a.Elements.Count);
        Assert.Equal(7, ((ScalarValue)((StructValue)a.Elements[1])["ACC"]!).Bits);
    }

    [Fact]
    public void ArrayOfStrings()
    {
        var v = Shape("[[1,'a$00'],[2,'bc$00']]", "STRING", new[] { 2 });
        var a = Assert.IsType<ArrayValue>(v);
        Assert.Equal("bc", Assert.IsType<StringValue>(a.Elements[1]).Text);
    }

    [Fact]
    public void ShapeMismatchReportsL5K004()
    {
        var bag = new DiagnosticBag();
        var v = Shape("[0,5]", "TIMER", null, null, bag);
        var s = Assert.IsType<StructValue>(v);
        Assert.Equal(5, s.Members.Count);
        Assert.Equal(0, ((ScalarValue)s["ACC"]!).Bits);
        Assert.Contains(bag, d => d.Code == L5kShaper.CodeShapeMismatch);
    }

    [Fact]
    public void OpaqueTypeBestEffortWithSingleNotice()
    {
        var bag = new DiagnosticBag();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reg = Registry();
        var value = L5kValueParser.Parse("[1,2.5,'x']", bag);
        var v1 = L5kShaper.Shape(value, "AXIS_CIP_DRIVE", Array.Empty<int>(), reg, bag, reported);
        var v2 = L5kShaper.Shape(value, "AXIS_CIP_DRIVE", Array.Empty<int>(), reg, bag, reported);
        Assert.NotNull(v2);
        var s = Assert.IsType<StructValue>(v1);
        Assert.Equal(3, s.Members.Count);
        Assert.Equal(AtomicKind.Real, Assert.IsType<ScalarValue>(s.Members[1].Value).Kind);
        Assert.Single(bag, d => d.Code == L5kShaper.CodeOpaqueType);
    }

    [Fact]
    public void ShapedEqualsDecoratedForSameTag()
    {
        var reg = Registry();
        var t = new StructuredType { Name = "SymAlarm", Class = DataTypeClass.User };
        t.Members.Add(new DataTypeMember { Name = "ZZZZZZZZZZSymAlarm0", DataType = DataTypeRef.Of("SINT"), Hidden = true });
        t.Members.Add(new DataTypeMember { Name = "EnableIn", DataType = DataTypeRef.Of("BIT"), Target = "ZZZZZZZZZZSymAlarm0", BitNumber = 0 });
        t.Members.Add(new DataTypeMember { Name = "IsWarning", DataType = DataTypeRef.Of("BIT"), Target = "ZZZZZZZZZZSymAlarm0", BitNumber = 1 });
        t.Members.Add(new DataTypeMember { Name = "Delay", DataType = DataTypeRef.Of("DINT") });
        t.Members.Add(new DataTypeMember { Name = "ID", DataType = DataTypeRef.Of("INT") });
        t.Members.Add(new DataTypeMember { Name = "Tmr", DataType = DataTypeRef.Of("TIMER") });
        t.Members.Add(new DataTypeMember { Name = "Text", DataType = DataTypeRef.Of("STRING") });
        reg.Register(t);

        var bag = new DiagnosticBag();
        var shaped = Shape("[3,0,3918,[0,0,0],[2,'ab$00']]", "SymAlarm", null, reg, bag);
        Assert.Empty(bag);

        var xml = "<Data Format=\"Decorated\"><Structure DataType=\"SymAlarm\">" +
                  "<DataValueMember Name=\"EnableIn\" DataType=\"BOOL\" Value=\"1\"/>" +
                  "<DataValueMember Name=\"IsWarning\" DataType=\"BOOL\" Value=\"1\"/>" +
                  "<DataValueMember Name=\"Delay\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/>" +
                  "<DataValueMember Name=\"ID\" DataType=\"INT\" Radix=\"Decimal\" Value=\"3918\"/>" +
                  "<StructureMember Name=\"Tmr\" DataType=\"TIMER\">" +
                  "<DataValueMember Name=\"PRE\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/><DataValueMember Name=\"ACC\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"0\"/>" +
                  "<DataValueMember Name=\"EN\" DataType=\"BOOL\" Value=\"0\"/><DataValueMember Name=\"TT\" DataType=\"BOOL\" Value=\"0\"/><DataValueMember Name=\"DN\" DataType=\"BOOL\" Value=\"0\"/>" +
                  "</StructureMember>" +
                  "<StructureMember Name=\"Text\" DataType=\"STRING\"><DataValueMember Name=\"LEN\" DataType=\"DINT\" Radix=\"Decimal\" Value=\"2\"/><DataValueMember Name=\"DATA\" DataType=\"STRING\" Radix=\"ASCII\"><![CDATA['ab']]></DataValueMember></StructureMember>" +
                  "</Structure></Data>";
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { IgnoreWhitespace = true });
        reader.MoveToContent();
        var decorated = DecoratedDataReader.Read(reader, bag);
        Assert.Empty(bag);

        AssertEquivalent(decorated!, shaped!);
    }

    private static void AssertEquivalent(TagValue expected, TagValue actual)
    {
        switch (expected)
        {
            case ScalarValue es:
            {
                var a = Assert.IsType<ScalarValue>(actual);
                Assert.Equal(es.Kind.IsBoolean(), a.Kind.IsBoolean());
                Assert.Equal(es.AsDouble, a.AsDouble);
                break;
            }
            case StringValue estr:
            {
                var a = Assert.IsType<StringValue>(actual);
                Assert.Equal(estr.Length, a.Length);
                Assert.Equal(estr.Text, a.Text);
                break;
            }
            case StructValue est:
            {
                var a = Assert.IsType<StructValue>(actual);
                Assert.Equal(est.DataType, a.DataType);
                Assert.Equal(est.Members.Select(m => m.Key), a.Members.Select(m => m.Key));
                for (var i = 0; i < est.Members.Count; i++) AssertEquivalent(est.Members[i].Value, a.Members[i].Value);
                break;
            }
            case ArrayValue ea:
            {
                var a = Assert.IsType<ArrayValue>(actual);
                Assert.Equal(ea.Dimensions, a.Dimensions);
                for (var i = 0; i < ea.Elements.Count; i++) AssertEquivalent(ea.Elements[i], a.Elements[i]);
                break;
            }
        }
    }
}
