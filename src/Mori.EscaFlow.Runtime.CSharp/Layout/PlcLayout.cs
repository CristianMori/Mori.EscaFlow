namespace Mori.EscaFlow.Runtime;

/// <summary>
/// One member of a structure layout, as the converter computed it with Logix packing rules.
/// </summary>
/// <remarks>
/// The offsets are pre-resolved by the converter rather than recomputed here because only the converter has the
/// full picture: the hidden BIT hosts and bit numbers exported with user types, the add-on packing of BOOL
/// parameters 32 per DINT, the padding rules for 64-bit members. The runtime just trusts the numbers, binds the
/// member views at them, and hands them to the tag server, so the three views of a structure (generated code,
/// server template, real controller) agree byte for byte.
/// </remarks>
public sealed class PlcLayoutMember
{
    /// <summary>For object-initialiser use.</summary>
    public PlcLayoutMember()
    {
    }

    /// <summary>Positional constructor, the form the generated static layout fields use.</summary>
    public PlcLayoutMember(string name, ushort typeCode, string? templateName, int offset, int bit, int[] dims, int elementSize, bool hidden = false, bool packedBools = false)
    {
        Name = name;
        TypeCode = typeCode;
        TemplateName = templateName;
        Offset = offset;
        Bit = bit;
        Dims = dims;
        ElementSize = elementSize;
        Hidden = hidden;
        PackedBools = packedBools;
    }

    /// <summary>Member name as logic and clients address it.</summary>
    public string Name { get; init; } = "";

    /// <summary>EthernetIPSharp CIP atomic type code (<see cref="EthernetIPSharp.Logix.LogixDataTypes"/>), or 0 for a structure member.</summary>
    public ushort TypeCode { get; init; }

    /// <summary>Layout name of the nested structure for structure members.</summary>
    public string? TemplateName { get; init; }

    /// <summary>Byte offset inside the structure.</summary>
    public int Offset { get; init; }

    /// <summary>Bit inside the host byte for BOOL members.</summary>
    public int Bit { get; init; }

    /// <summary>Array dimensions, empty for scalars.</summary>
    public int[] Dims { get; init; } = Array.Empty<int>();

    /// <summary>Bytes per element (1 for BOOL members; the nested structure size for structure members; 4 per 32 packed BOOLs).</summary>
    public int ElementSize { get; init; }

    /// <summary>A hidden host member (BIT overlay host) that logic never addresses.</summary>
    /// <remarks>Hidden hosts are still part of the byte image and are presented to clients with their <c>ZZZZZZZZZZ</c> names, as a real controller does.</remarks>
    public bool Hidden { get; init; }

    /// <summary>A BOOL array stored 32 per DINT.</summary>
    public bool PackedBools { get; init; }

    /// <summary>True when the member has dimensions.</summary>
    public bool IsArray => Dims.Length > 0;

    /// <summary>True for a nested structure member (no atomic type code).</summary>
    public bool IsStructure => TypeCode == 0;

    /// <summary>True for a BOOL member (a bit of a host byte).</summary>
    public bool IsBool => TypeCode == EthernetIPSharp.Logix.LogixDataTypes.BOOL;

    /// <summary>Total element count across all dimensions (1 for scalars).</summary>
    public int ElementCount
    {
        get
        {
            if (Dims.Length == 0) return 1;
            var n = 1;
            foreach (var d in Dims) n *= d;
            return n;
        }
    }
}

/// <summary>
/// The byte layout of a structure type; turned into an EthernetIPSharp template by <see cref="TagStore.RegisterLayout"/>.
/// </summary>
/// <remarks>
/// Every generated structure class carries one of these in a static <c>Layout</c> field. The size includes the
/// trailing padding (Logix rounds structures up to 4 bytes, or 8 when they contain a 64-bit member) so arrays of
/// the structure place their elements where the controller would.
/// </remarks>
public sealed class PlcLayout
{
    /// <summary>For object-initialiser use.</summary>
    public PlcLayout()
    {
    }

    /// <summary>Positional constructor, the form the generated static layout fields use.</summary>
    public PlcLayout(string name, int size, int alignment, PlcLayoutMember[] members)
    {
        Name = name;
        Size = size;
        Alignment = alignment;
        Members = members;
    }

    /// <summary>The Logix type name, also the template name on the server.</summary>
    public string Name { get; init; } = "";

    /// <summary>Padded byte size.</summary>
    public int Size { get; init; }

    /// <summary>Alignment the structure requires when it is a member or an array element (4, or 8 with 64-bit members).</summary>
    public int Alignment { get; init; } = 4;

    /// <summary>Members in Logix declaration order, hidden hosts included.</summary>
    public PlcLayoutMember[] Members { get; init; } = Array.Empty<PlcLayoutMember>();

    /// <summary>A member by name (case-insensitive, as Logix member names are), or null.</summary>
    public PlcLayoutMember? FindMember(string name)
    {
        foreach (var m in Members)
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }
}
