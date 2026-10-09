using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// Base of every node of the middle file (plc-ir 2.0).
/// </summary>
/// <remarks>
/// <para>
/// The middle file is deliberately two-layered. The neutral core is IEC 61131-3: configurations, tasks, POUs,
/// variable sections, types, statements and expressions that every PLC vendor can be mapped onto. Everything a
/// vendor has that the core cannot express (a Rockwell tag path, a radix, a MESSAGE configuration, a Siemens
/// data block number) travels in the <see cref="Vendor"/> slot of the node it belongs to, keyed by vendor id
/// (<c>logix</c>, <c>s7</c>, <c>twincat</c>, ...). A converter that targets the neutral core never looks at the
/// slot; a tag server or a same-vendor round trip reads it through the vendor package's typed views. Keeping
/// the slot on every node, rather than in a side table, means nothing is lost when a document is read, edited
/// and written again by code that does not know the vendor.
/// </para>
/// <para>
/// Values in the slot are typed objects while a front-end is building the document (the vendor package puts its
/// own classes there) and become <see cref="JsonElement"/> after a read, because the core cannot know the vendor
/// classes. <see cref="GetVendor{T}(string, JsonSerializerOptions?)"/> hides the difference and caches the
/// converted object, so repeated reads do not deserialize twice.
/// </para>
/// </remarks>
public abstract class IrNode
{
    /// <summary>Free-text documentation carried over from the source (tag descriptions, routine comments, type descriptions).</summary>
    /// <remarks>Ordered late in the JSON so the structural members of a node come first when reading the file by eye.</remarks>
    [JsonPropertyOrder(900)]
    public string? Description { get; set; }

    /// <summary>Provenance in the source project (line, rung, element), for diagnostics and for converters that emit source comments.</summary>
    [JsonPropertyOrder(990)]
    public IrSrc? Src { get; set; }

    /// <summary>The vendor extension slot: vendor id to an opaque payload. Null when no vendor attached anything to this node.</summary>
    /// <remarks>Serialized last. The dictionary is created lazily by <see cref="SetVendor"/> so the vast majority of nodes carry no allocation for it.</remarks>
    [JsonPropertyOrder(1000)]
    public Dictionary<string, object?>? Vendor { get; set; }

    /// <summary>True when a payload for <paramref name="vendor"/> is attached.</summary>
    public bool HasVendor(string vendor) => Vendor is not null && Vendor.ContainsKey(vendor);

    /// <summary>Attaches (or, with a null value, removes) the payload for <paramref name="vendor"/>.</summary>
    /// <remarks>Removing the last payload drops the dictionary altogether, so a node that no longer has vendor data serializes without the slot.</remarks>
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

    /// <summary>The vendor payload as <typeparamref name="T"/>, converting from JSON when the document was read from a file.</summary>
    /// <param name="vendor">Vendor id (<c>logix</c>, ...).</param>
    /// <param name="options">Serializer options for the conversion; defaults to the middle file's own options.</param>
    /// <returns>The typed payload, or null when the slot is absent or holds something else.</returns>
    /// <remarks>After a successful conversion the typed object replaces the <see cref="JsonElement"/> in the slot, so later calls are free and so that a subsequent write serializes the typed object (same JSON shape).</remarks>
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
/// <remarks>
/// Only the coordinates that apply are set. Lowered ladder statements carry <see cref="Rung"/> and
/// <see cref="Element"/> (the depth-first index of the drawn element that produced them), structured text
/// statements carry <see cref="Line"/> and <see cref="Col"/>, dataflow statements carry <see cref="Sheet"/> and
/// <see cref="Element"/>. Converters use it to group generated code per rung and to point diagnostics back at
/// the original project.
/// </remarks>
public sealed class IrSrc
{
    /// <summary>Zero-based source line of a structured text statement.</summary>
    public int? Line { get; set; }

    /// <summary>Column within <see cref="Line"/>.</summary>
    public int? Col { get; set; }

    /// <summary>Rung number of a ladder statement.</summary>
    public int? Rung { get; set; }

    /// <summary>Index of the drawn element (ladder, depth first) or of the network element (FBD) that produced the statement.</summary>
    public int? Element { get; set; }

    /// <summary>Sheet number of a dataflow (FBD) statement.</summary>
    public int? Sheet { get; set; }
}

/// <summary>Declares the closed set of values a string property may take.</summary>
/// <remarks>
/// The model uses strings rather than C# enums for discriminated values so the JSON stays readable and so other
/// languages can consume it without a generated enum table. This attribute keeps the set machine-readable: the
/// schema generator turns it into a JSON Schema <c>enum</c> and the validator's structural pass checks every
/// annotated property of every node against it.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IrEnumAttribute : Attribute
{
    /// <param name="values">The allowed values, in documentation order.</param>
    public IrEnumAttribute(params string[] values) => Values = values;

    /// <summary>The allowed values.</summary>
    public string[] Values { get; }
}

/// <summary>Marks a string property (or list of strings) that names a type, variable, pou, action, channel, device, task or program instance.</summary>
/// <remarks>
/// References between nodes are by id or name rather than by object reference, so the file is a plain tree.
/// The referential validation pass walks every property carrying this attribute and checks that the named
/// thing exists, which is what lets a converter assume every lookup succeeds.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IrRefKindAttribute : Attribute
{
    /// <param name="kind">What the property names: type | variable | pou | action | channel | device | task | programInstance.</param>
    public IrRefKindAttribute(string kind) => Kind = kind;

    /// <summary>type | variable | pou | action | channel | device | task | programInstance</summary>
    public string Kind { get; }
}
