using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// Reads and writes the middle file. The JSON uses camelCase property names, discriminators <c>kind</c> (statements,
/// segments, values, diagrams, ladder elements) and <c>op</c> (expressions), omits nulls, and writes named type
/// references as plain strings. Readers accept any 2.x document; unknown members are ignored unless <c>strict</c>.
/// </summary>
public static class IrJson
{
    public static readonly JsonSerializerOptions DefaultOptions = CreateOptions();

    public static JsonSerializerOptions CreateOptions(bool indented = false, bool strict = false)
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            AllowOutOfOrderMetadataProperties = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = strict ? JsonUnmappedMemberHandling.Disallow : JsonUnmappedMemberHandling.Skip,
        };
        return o;
    }

    public static void Write(IrDocument document, Stream stream, bool indented = false)
        => JsonSerializer.Serialize(stream, document, indented ? CreateOptions(true) : DefaultOptions);

    public static void WriteFile(IrDocument document, string path, bool indented = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        Write(document, stream, indented);
    }

    public static string Serialize(IrDocument document, bool indented = true)
        => JsonSerializer.Serialize(document, indented ? CreateOptions(true) : DefaultOptions);

    public static string Serialize<T>(T node, bool indented = true)
        => JsonSerializer.Serialize(node, indented ? CreateOptions(true) : DefaultOptions);

    public static IrDocument Read(Stream stream, bool strict = false)
    {
        var doc = JsonSerializer.Deserialize<IrDocument>(stream, strict ? CreateOptions(false, true) : DefaultOptions) ?? throw new InvalidDataException("Empty IR document");
        Check(doc);
        return doc;
    }

    public static IrDocument ReadFile(string path, bool strict = false)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, strict);
    }

    public static IrDocument Deserialize(string json, bool strict = false)
    {
        var doc = JsonSerializer.Deserialize<IrDocument>(json, strict ? CreateOptions(false, true) : DefaultOptions) ?? throw new InvalidDataException("Empty IR document");
        Check(doc);
        return doc;
    }

    public static T? DeserializeNode<T>(string json) => JsonSerializer.Deserialize<T>(json, DefaultOptions);

    private static void Check(IrDocument doc)
    {
        if (!string.Equals(doc.Format, IrDocument.FormatId, StringComparison.Ordinal))
            throw new InvalidDataException($"Not a {IrDocument.FormatId} document (format = '{doc.Format}')");
        var major = doc.IrVersion.Split('.')[0];
        var supported = IrDocument.CurrentVersion.Split('.')[0];
        if (major != supported)
            throw new InvalidDataException($"IR version {doc.IrVersion} is not supported by this reader (supports {supported}.x)");
    }
}
