using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.Schema;
using Xunit;

namespace Mori.EscaFlow.Transpiler.Tests;

/// <summary>
/// Guards the model/schema contract from <see cref="SchemaGenerator"/>: every model class and property reaches
/// the generated schema, the committed <c>plc-ir-2.0.schema.json</c> and the assembly-embedded copy both stay
/// byte-identical to what the generator produces from the current model, and generation is deterministic.
/// </summary>
public class SchemaTests
{
    /// <summary>Walks up from the test binary's output directory to find the repository root (marked by the solution file), so the committed schema can be located regardless of build configuration or working directory.</summary>
    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Mori.EscaFlow.slnx"))) return Path.Combine(dir, relative);
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static readonly Type[] NotSerialized =
    {
        typeof(IrWalker), typeof(IrWalkContext), typeof(IrTypeRefConverter), typeof(IrEnumAttribute), typeof(IrRefKindAttribute),
    };

    /// <summary>Every public, non-static, non-abstract class of the model namespace, excluding helpers.</summary>
    private static IEnumerable<Type> ModelClasses() => typeof(IrDocument).Assembly.GetTypes()
        .Where(t => t.IsClass && t.IsPublic && t.Namespace == "Mori.EscaFlow.Transpiler" && !(t.IsAbstract && t.IsSealed) && !t.IsAbstract)
        .Where(t => !NotSerialized.Contains(t) && !typeof(Attribute).IsAssignableFrom(t) && !typeof(JsonConverter).IsAssignableFrom(t) && !t.IsNested);

    /// <summary>Every public model class gets a <c>$defs</c> entry with every one of its serialized properties, polymorphic bases are <c>oneOf</c> with a <c>const</c> discriminator, closed-set strings become <c>enum</c>, the vendor slot allows unknown members while every other object forbids them, and non-nullable strings are <c>required</c>.</summary>
    [Fact]
    public void EveryModelClassAndPropertyIsInTheSchema()
    {
        var schema = JsonDocument.Parse(SchemaGenerator.Generate());
        var defs = schema.RootElement.GetProperty("$defs");
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("#/$defs/Document", schema.RootElement.GetProperty("$ref").GetString());

        foreach (var type in ModelClasses())
        {
            var name = type.Name[2..];
            Assert.True(defs.TryGetProperty(name, out var def), $"schema has no definition for {type.Name}");
            if (type == typeof(IrTypeRef))
            {
                Assert.True(def.TryGetProperty("oneOf", out _));
                continue;
            }
            Assert.True(def.TryGetProperty("properties", out var properties), $"{name} has no properties");
            foreach (var prop in SchemaGenerator.SerializedProperties(type))
                Assert.True(properties.TryGetProperty(prop, out _), $"{name}.{prop} is missing from the schema");
            Assert.False(def.GetProperty("additionalProperties").GetBoolean());
        }

        // Polymorphic bases are oneOf over their derived types with the discriminator as a const.
        Assert.Equal(18, defs.GetProperty("Statement").GetProperty("oneOf").GetArrayLength());
        Assert.Equal(12, defs.GetProperty("Expression").GetProperty("oneOf").GetArrayLength());
        Assert.Equal("ref", defs.GetProperty("Ref").GetProperty("properties").GetProperty("op").GetProperty("const").GetString());
        Assert.Contains("op", defs.GetProperty("Ref").GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("contact", defs.GetProperty("LdContact").GetProperty("properties").GetProperty("kind").GetProperty("const").GetString());
        // Enums and vendor slot.
        Assert.Equal(8, defs.GetProperty("Variable").GetProperty("properties").GetProperty("section").GetProperty("enum").GetArrayLength());
        Assert.True(defs.GetProperty("Variable").GetProperty("properties").GetProperty("vendor").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("variable", defs.GetProperty("IoChannel").GetProperty("properties").GetProperty("variable").GetProperty("x-refKind").GetString());
        Assert.Equal("programInstance", defs.GetProperty("Task").GetProperty("properties").GetProperty("programs").GetProperty("items").GetProperty("x-refKind").GetString());
        // Type references use the string-or-object form.
        Assert.Equal("#/$defs/TypeRef", defs.GetProperty("Member").GetProperty("properties").GetProperty("type").GetProperty("$ref").GetString());
        // Required strings.
        Assert.Contains("id", defs.GetProperty("Variable").GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    /// <summary>The committed schema file is the generator's output, kept in sync by hand via <c>PLC_IR_UPDATE_SCHEMA=1</c> rather than regenerated on every build, so a model change that is not yet reflected in the committed schema fails this test instead of silently shipping a stale schema.</summary>
    [Fact]
    public void CommittedSchemaMatchesGeneratedAndEmbedded()
    {
        var generated = SchemaGenerator.Generate();
        var path = RepoFile(Path.Combine("src", "Mori.EscaFlow.Transpiler", "Schema", "plc-ir-2.0.schema.json"));
        if (Environment.GetEnvironmentVariable("PLC_IR_UPDATE_SCHEMA") == "1" || !File.Exists(path))
            File.WriteAllText(path, generated);
        var committed = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(generated, committed);
        var embedded = SchemaGenerator.Embedded();
        Assert.NotNull(embedded);
        Assert.Equal(generated, embedded!.Replace("\r\n", "\n"));
    }

    /// <summary>Generating the schema twice from the same model yields identical text, so a diff against it is meaningful rather than noisy from run to run.</summary>
    [Fact]
    public void GenerationIsDeterministic()
    {
        Assert.Equal(SchemaGenerator.Generate(), SchemaGenerator.Generate());
    }
}
