using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// Base of every node of the middle file. <see cref="Vendor"/> is the extension slot: one entry per vendor id
/// (<c>logix</c>, <c>s7</c>, <c>twincat</c>, ...) holding anything that has no neutral meaning. Values are typed
/// objects when a front-end built the document and <see cref="JsonElement"/> after a read; use
/// <see cref="GetVendor{T}(string, JsonSerializerOptions?)"/> to obtain a typed view either way.
/// </summary>
public abstract class IrNode
{
    [JsonPropertyOrder(900)]
    public string? Description { get; set; }

    /// <summary>Provenance in the source project (line, rung, element).</summary>
    [JsonPropertyOrder(990)]
    public IrSrc? Src { get; set; }

    [JsonPropertyOrder(1000)]
    public Dictionary<string, object?>? Vendor { get; set; }

    public bool HasVendor(string vendor) => Vendor is not null && Vendor.ContainsKey(vendor);

    public void SetVendor(string vendor, object? value)
    {
        if (value is null)
        {
            Vendor?.Remove(vendor);
            if (Vendor is { Count: 0 }) Vendor = null;
            return;
        }
        Vendor ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        Vendor[vendor] = value;
    }

    /// <summary>The vendor extension as <typeparamref name="T"/>, converting from JSON when the document was read from a file.</summary>
    public T? GetVendor<T>(string vendor, JsonSerializerOptions? options = null) where T : class
    {
        if (Vendor is null || !Vendor.TryGetValue(vendor, out var value) || value is null) return null;
        switch (value)
        {
            case T typed:
                return typed;
            case JsonElement element:
            {
                var typed = element.Deserialize<T>(options ?? IrJson.DefaultOptions);
                if (typed is not null) Vendor[vendor] = typed;
                return typed;
            }
            default:
                return null;
        }
    }
}

/// <summary>Where a node comes from in the source: text line/column, ladder rung and element index, or diagram sheet.</summary>
public sealed class IrSrc
{
    public int? Line { get; set; }
    public int? Col { get; set; }
    public int? Rung { get; set; }
    public int? Element { get; set; }
    public int? Sheet { get; set; }
}

/// <summary>Declares the closed set of values a string property may take. Read by the schema generator and the validator.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IrEnumAttribute : Attribute
{
    public IrEnumAttribute(params string[] values) => Values = values;
    public string[] Values { get; }
}

/// <summary>Marks a string property (or list of strings) that names a type, variable, pou, action, channel, device, task or program instance, for the referential validation pass.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IrRefKindAttribute : Attribute
{
    public IrRefKindAttribute(string kind) => Kind = kind;

    /// <summary>type | variable | pou | action | channel | device | task | programInstance</summary>
    public string Kind { get; }
}
