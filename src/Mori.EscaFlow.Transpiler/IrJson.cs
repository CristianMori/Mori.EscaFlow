using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// Reads and writes the middle file. The JSON uses camelCase property names, discriminators <c>kind</c> (statements,
/// segments, values, diagrams, ladder elements) and <c>op</c> (expressions), omits nulls, and writes named type
/// references as plain strings. Readers accept any 2.x document; unknown members are ignored unless <c>strict</c>.
/// </summary>
/// <remarks>
/// The options are chosen for interoperability and size: camelCase and omitted nulls keep the file compact and
/// natural for non-.NET readers; named floating-point literals allow NaN and infinity values that Logix exports
/// contain; out-of-order metadata lets hand-edited or re-serialized files put the discriminator anywhere; comments
/// are skipped so documented sample files remain loadable. Strict mode flips unknown members from "ignored" to
/// "rejected" for the validator's schema-level pass.
/// </remarks>
public static class IrJson
{
    /// <summary>The options used for every read and compact write; shared because building options is expensive and they are immutable once used.</summary>
    public static readonly JsonSerializerOptions DefaultOptions = CreateOptions();

    /// <summary>Builds serializer options for the format.</summary>
    /// <param name="indented">Pretty-print (for humans; roughly doubles the size).</param>
    /// <param name="strict">Reject unknown members instead of skipping them.</param>
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

    /// <summary>Writes a document to a stream.</summary>
    public static void Write(IrDocument document, Stream stream, bool indented = false)
        => JsonSerializer.Serialize(stream, document, indented ? CreateOptions(true) : DefaultOptions);

    /// <summary>Writes a document to a file (a 1 MB buffer: documents of large projects reach a hundred megabytes).</summary>
    public static void WriteFile(IrDocument document, string path, bool indented = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        Write(document, stream, indented);
    }

    /// <summary>Serializes a document to a string; indented by default because strings are mostly used for tests and inspection.</summary>
    public static string Serialize(IrDocument document, bool indented = true)
        => JsonSerializer.Serialize(document, indented ? CreateOptions(true) : DefaultOptions);

    /// <summary>Serializes any node (a statement, an expression, a vendor payload) with the format's options.</summary>
    public static string Serialize<T>(T node, bool indented = true)
        => JsonSerializer.Serialize(node, indented ? CreateOptions(true) : DefaultOptions);

    /// <summary>Reads a document from a stream and checks its format id and major version.</summary>
    public static IrDocument Read(Stream stream, bool strict = false)
    {
        var doc = JsonSerializer.Deserialize<IrDocument>(stream, strict ? CreateOptions(false, true) : DefaultOptions) ?? throw new InvalidDataException("Empty IR document");
        Check(doc);
        return doc;
    }

    /// <summary>Reads a document from a file.</summary>
    public static IrDocument ReadFile(string path, bool strict = false)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, strict);
    }

    /// <summary>Reads a document from a string.</summary>
    public static IrDocument Deserialize(string json, bool strict = false)
    {
        var doc = JsonSerializer.Deserialize<IrDocument>(json, strict ? CreateOptions(false, true) : DefaultOptions) ?? throw new InvalidDataException("Empty IR document");
        Check(doc);
        return doc;
    }

    /// <summary>Deserializes any node type with the format's options (no document checks).</summary>
    public static T? DeserializeNode<T>(string json) => JsonSerializer.Deserialize<T>(json, DefaultOptions);

    /// <summary>Rejects documents of another format or another major version; minor versions are compatible by construction (additive, nulls omitted).</summary>
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
