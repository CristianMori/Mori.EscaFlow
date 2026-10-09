namespace Mori.EscaFlow.Runtime;

/// <summary>Logix COP / CPS / FLL over tag memory.</summary>
public static class PlcMem
{
    /// <summary>
    /// COP: copies <paramref name="count"/> elements of the destination type as raw bytes from the source's memory,
    /// clipped to what the source and destination buffers can provide.
    /// </summary>
    public static void Copy(object? sourceObject, object? destObject, int count)
    {
        if (count <= 0 || sourceObject is not IPlcMemory source || destObject is not IPlcMemory dest) return;
        var destElement = dest is PlcArrayBase da ? da.ElementSize : dest.ByteSize;
        var bytes = (long)destElement * count;
        var destAvailable = dest is PlcArrayBase ? dest.ByteSize : dest.Tag.DataSize - dest.Offset;
        var sourceAvailable = source.Tag.DataSize - source.Offset;
        bytes = Math.Min(bytes, Math.Min(destAvailable, sourceAvailable));
        if (bytes <= 0) return;
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
    public static void CopyAtomic(object? source, object? dest, int count) => Copy(source, dest, count);

    /// <summary>FLL: stores <paramref name="value"/> into <paramref name="count"/> elements of the destination.</summary>
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
                if (elements.Count == 0) return;
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
                        return;
                }
            }
            case PlcStruct when value is IPlcMemory sourceStruct:
                Copy(sourceStruct, dest, 1);
                return;
        }
    }
}
