namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Logix COP / CPS / FLL over tag memory.
/// </summary>
/// <remarks>
/// COP is a byte copy, not a typed assignment: Logix copies <c>length</c> elements of the <em>destination</em>
/// type as raw bytes starting at the source address, which is how projects reinterpret data (a DINT[8] copied
/// into a STRING, an I/O image copied into a user structure). Because every value here is a view over tag memory,
/// the runtime can do the same thing literally: take the bytes at the source offset and store them at the
/// destination offset, clipped so neither tag buffer is overrun.
/// </remarks>
public static class PlcMem
{
    /// <summary>
    /// COP: copies <paramref name="count"/> elements of the destination type as raw bytes from the source's memory,
    /// clipped to what the source and destination buffers can provide.
    /// </summary>
    public static void Copy(object? sourceObject, object? destObject, int count)
    {
        if (count <= 0 || sourceObject is not IPlcMemory source || destObject is not IPlcMemory dest) return;
        // Element size of the destination decides how many bytes "count elements" means.
        var destElement = dest is PlcArrayBase da ? da.ElementSize : dest.ByteSize;
        var bytes = (long)destElement * count;
        var destAvailable = dest is PlcArrayBase ? dest.ByteSize : dest.Tag.DataSize - dest.Offset;
        var sourceAvailable = source.Tag.DataSize - source.Offset;
        bytes = Math.Min(bytes, Math.Min(destAvailable, sourceAvailable));
        if (bytes <= 0) return;
        // Through a temporary buffer so a copy within one tag with overlapping ranges behaves like memmove.
        var buffer = source.Tag.GetData(source.Offset, (int)bytes).ToArray();
        dest.Tag.SetDataSilent(buffer, dest.Offset);
    }

    /// <summary>Copies the whole image of a structure, array or value into another of the same size (structure assignment).</summary>
    public static void CopyAll(object? sourceObject, object? destObject)
    {
        if (sourceObject is not IPlcMemory source || destObject is not IPlcMemory dest) return;
        var bytes = Math.Min(source.ByteSize, dest.ByteSize);
        bytes = Math.Min(bytes, Math.Min(source.Tag.DataSize - source.Offset, dest.Tag.DataSize - dest.Offset));
        if (bytes <= 0) return;
        var buffer = source.Tag.GetData(source.Offset, bytes).ToArray();
        dest.Tag.SetDataSilent(buffer, dest.Offset);
    }

    /// <summary>CPS: same as COP; the copy is not interruptible by other tasks in the sense that it is a single buffer copy.</summary>
    /// <remarks>On a real controller CPS additionally blocks I/O updates and other tasks for its duration; here other threads could observe the destination mid-copy, exactly as they could during any structure write.</remarks>
    public static void CopyAtomic(object? source, object? dest, int count) => Copy(source, dest, count);

    /// <summary>FLL: stores <paramref name="value"/> into <paramref name="count"/> elements of the destination.</summary>
    /// <remarks>
    /// <para>
    /// Unlike COP, FLL converts: the value is stored into each element as a MOV would (a REAL into a DINT array
    /// rounds), strings are copied as text, and a structure value is byte-copied into each structure element.
    /// </para>
    /// <para>
    /// Every shape FLL can legitimately be asked to fill in a correctly generated project is handled below. A
    /// destination or value that reaches neither an atomic wrapper, a string, a BOOL array, a value or string
    /// array, nor a structure paired with a structure-shaped value is not a case Logix's FLL covers either, so
    /// rather than doing nothing and leaving the destination holding whatever it held before (which a caller could
    /// easily mistake for "the fill ran and the value happened to already be that"), this throws: such a call can
    /// only come from the converter emitting an argument pairing this method was never meant to receive, which is
    /// a defect worth surfacing immediately rather than one more service to log once and route around.
    /// </para>
    /// </remarks>
    public static void Fill(object? value, object? dest, int count)
    {
        if (count <= 0) return;
        switch (dest)
        {
            case PlcValue v:
                v.SetFrom(value);
                return;
            case WrapSTRING s:
                s.Value = PlcConvert.ToText(value);
                return;
            case PlcBoolArray bits:
            {
                var b = PlcConvert.ToBool(value);
                for (var i = 0; i < Math.Min(count, bits.Length); i++) bits[i].Value = b;
                return;
            }
            case PlcArrayBase array:
            {
                var elements = ((System.Collections.IEnumerable)array).Cast<IPlcMemory>().Take(count).ToList();
                if (elements.Count == 0) return; // nothing asked for (count clipped to an empty array): not an error
                switch (elements[0])
                {
                    case PlcValue:
                        foreach (var e in elements) ((PlcValue)e).SetFrom(value);
                        return;
                    case WrapSTRING:
                    {
                        var text = PlcConvert.ToText(value);
                        foreach (var e in elements) ((WrapSTRING)e).Value = text;
                        return;
                    }
                    case PlcStruct when value is IPlcMemory sourceStruct:
                        foreach (var e in elements) Copy(sourceStruct, e, 1);
                        return;
                    default:
                        throw Unsupported(value, dest);
                }
            }
            case PlcStruct when value is IPlcMemory sourceStruct:
                Copy(sourceStruct, dest, 1);
                return;
            default:
                throw Unsupported(value, dest);
        }
    }

    /// <summary>The exception <see cref="Fill"/> raises for a destination/value pairing it has no rule for.</summary>
    private static NotSupportedException Unsupported(object? value, object? dest)
        => new($"FLL cannot fill a {dest?.GetType().Name ?? "null"} destination with a {value?.GetType().Name ?? "null"} value; this combination is not one the runtime models, which means the converter emitted an FLL call this method was never meant to receive.");
}
