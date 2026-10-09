using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Json;

/// <summary>
/// Which parts of the parsed model a JSON dump should carry. The three sections can be large (parsed ASTs and
/// shaped values especially) and a consumer inspecting project structure often wants none of them, hence the
/// ability to drop each independently rather than always serializing everything.
/// </summary>
[Flags]
public enum JsonInclude
{
    /// <summary>The structural model itself (types, tags, modules, programs, routines): always kept, regardless of what else is requested.</summary>
    Model = 1,
    /// <summary>Parsed rung/ST/SFC/FBD ASTs.</summary>
    Logic = 2,
    /// <summary>Shaped/decorated tag values.</summary>
    Values = 4,
    All = Model | Logic | Values,
}

/// <summary>Parses the CLI's comma-separated <c>--json-include</c> option text into a <see cref="JsonInclude"/> flag set.</summary>
public static class JsonIncludeParser
{
    public static JsonInclude Parse(string text)
    {
        JsonInclude result = 0;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "model" => JsonInclude.Model,
                "logic" => JsonInclude.Logic,
                "values" => JsonInclude.Values,
                "all" => JsonInclude.All,
                _ => throw new ArgumentException($"Unknown --json-include value '{part}'"),
            };
        }
        // Model is never optional; an input that named none of the recognized sections defaults to everything.
        return result == 0 ? JsonInclude.All : result | JsonInclude.Model;
    }
}

/// <summary>JSON serialization of the model with System.Text.Json.</summary>
public static class L5xJson
{
    /// <summary>Builds serializer options that trim the requested sections and know how to write the model's few hand-rolled value types.</summary>
    public static JsonSerializerOptions CreateOptions(JsonInclude include = JsonInclude.All, bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            IncludeFields = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ti => Trim(ti, include) } },
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new DataTypeRefConverter());
        options.Converters.Add(new TextSpanConverter());
        return options;
    }

    /// <summary>
    /// Removes the properties <paramref name="include"/> excludes. This runs as a type-info modifier (applied
    /// once per type, not per instance) rather than per-property <c>[JsonIgnore]</c> attributes, because whether
    /// a property is included depends on a runtime option, not a fixed decision the type itself can make; a few
    /// properties (<see cref="TagData.Value"/>, <see cref="TagData.IsEmpty"/>) are always dropped regardless of
    /// <paramref name="include"/> since they only duplicate or summarize other properties already present.
    /// </summary>
    private static void Trim(JsonTypeInfo ti, JsonInclude include)
    {
        if (ti.Kind != JsonTypeInfoKind.Object) return;
        var dropLogic = (include & JsonInclude.Logic) == 0;
        var dropValues = (include & JsonInclude.Values) == 0;
        for (var i = ti.Properties.Count - 1; i >= 0; i--)
        {
            var p = ti.Properties[i];
            var drop = false;
            if (dropLogic)
            {
                if (ti.Type == typeof(Rung) && p.Name == nameof(Rung.Ast)) drop = true;
                if (ti.Type == typeof(StContent) && p.Name is nameof(StContent.Ast) or nameof(StContent.Comments)) drop = true;
                if (ti.Type == typeof(StSnippet) && p.Name is nameof(StSnippet.Ast) or nameof(StSnippet.Expression) or nameof(StSnippet.Comments)) drop = true;
            }
            if (dropValues)
            {
                if (ti.Type == typeof(Tag) || ti.Type.IsSubclassOf(typeof(Tag)))
                {
                    if (p.Name == nameof(Tag.Data)) drop = true;
                }
                if (ti.Type == typeof(Model.Modules.ModuleTagData) && p.Name == nameof(Model.Modules.ModuleTagData.Data)) drop = true;
            }
            // Never serialize the raw L5K text-derived duplicates when the typed value exists
            if (ti.Type == typeof(TagData) && p.Name == nameof(TagData.Value)) drop = true;
            if (ti.Type == typeof(TagData) && p.Name == nameof(TagData.IsEmpty)) drop = true;
            if (drop) ti.Properties.RemoveAt(i);
        }
    }

    /// <summary>Serializes a project directly to a file with a large buffered stream, avoiding building the whole JSON text in memory first for large projects.</summary>
    public static void WriteFile(L5xProject project, string path, JsonInclude include = JsonInclude.All, bool indented = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        JsonSerializer.Serialize(stream, project, CreateOptions(include, indented));
    }

    public static string Serialize(object value, JsonInclude include = JsonInclude.All, bool indented = true)
        => JsonSerializer.Serialize(value, value.GetType(), CreateOptions(include, indented));

    /// <summary>Writes a <see cref="DataTypeRef"/> as just its name string rather than an object, since <see cref="DataTypeRef.Definition"/> is a resolution cache, not independent data.</summary>
    private sealed class DataTypeRefConverter : JsonConverter<DataTypeRef>
    {
        public override DataTypeRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String ? DataTypeRef.Of(reader.GetString()!) : null;

        public override void Write(Utf8JsonWriter writer, DataTypeRef value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Name);
    }

    /// <summary>Writes a <see cref="TextSpan"/> as a compact "start:length:line:column" string instead of a nested object, since every AST node carries one.</summary>
    private sealed class TextSpanConverter : JsonConverter<TextSpan>
    {
        public override TextSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString() ?? "";
            var parts = s.Split(':');
            return parts.Length == 4 ? new TextSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])) : TextSpan.Empty;
        }

        public override void Write(Utf8JsonWriter writer, TextSpan value, JsonSerializerOptions options)
            => writer.WriteStringValue($"{value.Start}:{value.Length}:{value.Line}:{value.Column}");
    }
}
