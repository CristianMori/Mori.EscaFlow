namespace Plc.Runtime;

/// <summary>One member of a structure layout, as the generator computed it with Logix packing rules.</summary>
public sealed class PlcLayoutMember
{
    public PlcLayoutMember()
    {
    }

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
    public bool Hidden { get; init; }

    /// <summary>A BOOL array stored 32 per DINT.</summary>
    public bool PackedBools { get; init; }

    public bool IsArray => Dims.Length > 0;
    public bool IsStructure => TypeCode == 0;
    public bool IsBool => TypeCode == EthernetIPSharp.Logix.LogixDataTypes.BOOL;

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

/// <summary>The byte layout of a structure type; turned into an EthernetIPSharp template by <see cref="TagStore.RegisterLayout"/>.</summary>
public sealed class PlcLayout
{
    public PlcLayout()
    {
    }

    public PlcLayout(string name, int size, int alignment, PlcLayoutMember[] members)
    {
        Name = name;
        Size = size;
        Alignment = alignment;
        Members = members;
    }

    public string Name { get; init; } = "";
    public int Size { get; init; }
    public int Alignment { get; init; } = 4;
    public PlcLayoutMember[] Members { get; init; } = Array.Empty<PlcLayoutMember>();

    public PlcLayoutMember? FindMember(string name)
    {
        foreach (var m in Members)
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }
}
