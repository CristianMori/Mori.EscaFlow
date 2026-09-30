using System.Text.Json;
using L5x.Json;
using L5x.Parsing;
using L5x.Tests.Integration;
using Xunit;

namespace L5x.Tests.Json;

public class JsonTests
{
    private const string Xml = """
        <RSLogix5000Content SchemaRevision="1.0" TargetType="Controller" TargetName="T">
        <Controller Name="T">
        <DataTypes/>
        <Tags>
        <Tag Name="A" TagType="Base" DataType="DINT" Radix="Decimal">
        <Data Format="Decorated">
        <DataValue DataType="DINT" Radix="Decimal" Value="5"/>
        </Data>
        </Tag>
        </Tags>
        <Programs>
        <Program Name="P" MainRoutineName="Main">
        <Tags/>
        <Routines>
        <Routine Name="Main" Type="RLL">
        <RLLContent>
        <Rung Number="0" Type="N">
        <Text>
        <![CDATA[XIC(A.0)[XIC(B) ,MOV(1,C) ]OTE(D);]]>
        </Text>
        </Rung>
        </RLLContent>
        </Routine>
        <Routine Name="S" Type="ST">
        <STContent>
        <Line Number="0">
        <![CDATA[IF A > 1 THEN C := A * 2; END_IF;]]>
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
    public void SerializesPolymorphicNodesWithDiscriminators()
    {
        var project = L5xParser.ParseText(Xml);
        var json = L5xJson.Serialize(project);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("T", root.GetProperty("TargetName").GetString());
        var rung = root.GetProperty("Controller").GetProperty("Programs")[0].GetProperty("Routines")[0].GetProperty("Content").GetProperty("Rungs")[0];
        var elements = rung.GetProperty("Ast").GetProperty("Elements");
        Assert.Equal("instruction", elements[0].GetProperty("$type").GetString());
        Assert.Equal("branch", elements[1].GetProperty("$type").GetString());
        Assert.Equal("tag", elements[0].GetProperty("Arguments")[0].GetProperty("$type").GetString());
        var st = root.GetProperty("Controller").GetProperty("Programs")[0].GetProperty("Routines")[1].GetProperty("Content").GetProperty("Ast").GetProperty("Statements")[0];
        Assert.Equal("if", st.GetProperty("$type").GetString());
        var tag = root.GetProperty("Controller").GetProperty("Tags")[0];
        Assert.Equal("DINT", tag.GetProperty("DataType").GetString());
        Assert.Equal("scalar", tag.GetProperty("Data").GetProperty("Decorated").GetProperty("$type").GetString());
    }

    [Fact]
    public void IncludeFlagsDropLogicAndValues()
    {
        var project = L5xParser.ParseText(Xml);
        var json = L5xJson.Serialize(project, JsonInclude.Model);
        using var doc = JsonDocument.Parse(json);
        var rung = doc.RootElement.GetProperty("Controller").GetProperty("Programs")[0].GetProperty("Routines")[0].GetProperty("Content").GetProperty("Rungs")[0];
        Assert.False(rung.TryGetProperty("Ast", out _));
        Assert.True(rung.TryGetProperty("Text", out _));
        var tag = doc.RootElement.GetProperty("Controller").GetProperty("Tags")[0];
        Assert.False(tag.TryGetProperty("Data", out _));
    }
}

[Collection("Samples")]
[Trait("Category", "Integration")]
public class JsonSampleTests
{
    private readonly SampleFileFixture _fx;

    public JsonSampleTests(SampleFileFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void Stlib106DumpIsValidJson()
    {
        if (!_fx.Has(SampleFileFixture.Stlib106)) return;
        var project = _fx.Get(SampleFileFixture.Stlib106);
        var path = Path.Combine(Path.GetTempPath(), "l5x-test-stlib106.json");
        try
        {
            L5xJson.WriteFile(project, path, JsonInclude.Model | JsonInclude.Logic);
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var programs = doc.RootElement.GetProperty("Controller").GetProperty("Programs");
            Assert.Equal(18, programs.GetArrayLength());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
