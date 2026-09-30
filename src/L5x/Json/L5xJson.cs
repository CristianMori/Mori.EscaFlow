using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using L5x.Model;
using L5x.Model.DataTypes;
using L5x.Model.Logic;
using L5x.Model.Logic.Rll;
using L5x.Model.Logic.St;
using L5x.Model.Tags;
using L5x.Model.Values;

namespace L5x.Json;

[Flags]
public enum JsonInclude
{
    Model = 1,
    Logic = 2,
    Values = 4,
    All = Model | Logic | Values,
}

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
        return result == 0 ? JsonInclude.All : result | JsonInclude.Model;
    }
}

/// <summary>JSON serialization of the model with System.Text.Json.</summary>
public static class L5xJson
{
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

    public static void WriteFile(L5xProject project, string path, JsonInclude include = JsonInclude.All, bool indented = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        JsonSerializer.Serialize(stream, project, CreateOptions(include, indented));
    }

    public static string Serialize(object value, JsonInclude include = JsonInclude.All, bool indented = true)
        => JsonSerializer.Serialize(value, value.GetType(), CreateOptions(include, indented));

    private sealed class DataTypeRefConverter : JsonConverter<DataTypeRef>
    {
        public override DataTypeRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String ? DataTypeRef.Of(reader.GetString()!) : null;

        public override void Write(Utf8JsonWriter writer, DataTypeRef value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Name);
    }

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
