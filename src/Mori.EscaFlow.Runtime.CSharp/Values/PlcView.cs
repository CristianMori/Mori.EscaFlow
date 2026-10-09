using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>Typed views over another object's tag memory, used to bind InOut parameters: the declared parameter type is overlaid on the argument's bytes.</summary>
public static class PlcView
{
    /// <summary>Returns <paramref name="current"/> when it already views <paramref name="source"/>'s memory, else a new view built by <paramref name="factory"/>.</summary>
    public static T Of<T>(T? current, object? source, Func<Tag, int, T> factory) where T : class, IPlcMemory
    {
        if (source is not IPlcMemory m) throw new ArgumentException("InOut argument is not tag memory", nameof(source));
        if (current is not null && ReferenceEquals(current.Tag, m.Tag) && current.Offset == m.Offset) return current;
        return factory(m.Tag, m.Offset);
    }

    /// <summary>
    /// Array view over the argument's memory. Logix declares InOut array parameters with dimension 1 and applies the
    /// argument's real size, so the view takes its dimensions from the argument (an array of the same element size
    /// keeps its dimensions; anything else is seen as one dimension of <c>bytes / elementSize</c> elements).
    /// </summary>
    public static PlcArray<T> OfArray<T>(PlcArray<T>? current, object? source, int elementSize, Func<Tag, int, T> elementFactory) where T : class, IPlcMemory
    {
        if (source is not IPlcMemory m) throw new ArgumentException("InOut argument is not tag memory", nameof(source));
        if (current is not null && ReferenceEquals(current.Tag, m.Tag) && current.Offset == m.Offset && current.ByteSize == m.ByteSize) return current;
        var dims = source is PlcArrayBase a && a.ElementSize == elementSize ? a.Dims.ToArray() : new[] { Math.Max(1, m.ByteSize / Math.Max(1, elementSize)) };
        return new PlcArray<T>(m.Tag, m.Offset, elementSize, dims, elementFactory);
    }

    /// <summary>BOOL array view over the argument's memory (32 bits per DINT).</summary>
    public static PlcBoolArray OfBoolArray(PlcBoolArray? current, object? source)
    {
        if (source is not IPlcMemory m) throw new ArgumentException("InOut argument is not tag memory", nameof(source));
        if (current is not null && ReferenceEquals(current.Tag, m.Tag) && current.Offset == m.Offset && current.ByteSize == m.ByteSize) return current;
        var dims = source is PlcBoolArray b ? b.Dims.ToArray() : new[] { Math.Max(1, m.ByteSize) * 8 };
        return new PlcBoolArray(m.Tag, m.Offset, dims);
    }
}
