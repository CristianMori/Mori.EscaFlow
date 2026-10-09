namespace Mori.EscaFlow.Transpiler.L5X.Model.Values;

/// <summary>Everything found in the <c>&lt;Data&gt;</c> / <c>&lt;DefaultData&gt;</c> children of a tag, keyed by format.</summary>
public sealed class TagData
{
    public static readonly TagData Empty = new();

    /// <summary>The raw L5K initializer text (only kept when <c>ParseOptions.KeepRawL5kText</c> is set).</summary>
    public string? L5kText { get; set; }

    /// <summary>The untyped tree parsed from <c>Format="L5K"</c>.</summary>
    public L5kValue? L5k { get; set; }

    /// <summary>The typed tree parsed from <c>Format="Decorated"</c> (or built from <c>Format="String"</c>).</summary>
    public TagValue? Decorated { get; set; }

    /// <summary>Text from <c>Format="String"</c> with '$' escapes decoded.</summary>
    public string? StringText { get; set; }

    public int? StringLength { get; set; }

    /// <summary>Bytes from a raw <c>&lt;Data&gt;</c> element without a Format attribute.</summary>
    public byte[]? RawHex { get; set; }

    /// <summary>Typed MESSAGE tag configuration (from <c>Format="Message"</c>).</summary>
    public Tags.MessageConfiguration? Message { get; set; }

    /// <summary>The raw <c>Format="Message"</c> element, kept for anything the typed configuration does not cover.</summary>
    public GenericElement? MessageElement { get; set; }

    public GenericElement? Alarm { get; set; }
    public GenericElement? Axis { get; set; }
    public GenericElement? MotionGroup { get; set; }
    public GenericElement? CoordinateSystem { get; set; }

    /// <summary>Any other <c>Data</c> element with an unrecognized Format.</summary>
    public List<GenericElement>? Other { get; set; }

    /// <summary>Value shaped from the L5K tree by the linker when Decorated data is missing.</summary>
    public TagValue? Shaped { get; set; }

    /// <summary>The best available typed value.</summary>
    public TagValue? Value => Decorated ?? Shaped;

    public bool IsEmpty => L5k is null && Decorated is null && StringText is null && RawHex is null
        && Message is null && Alarm is null && Axis is null && MotionGroup is null && CoordinateSystem is null && Other is null;
}
