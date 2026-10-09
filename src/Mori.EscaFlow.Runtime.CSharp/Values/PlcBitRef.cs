namespace Mori.EscaFlow.Runtime;

/// <summary>
/// A BOOL location: a BOOL wrapper or one bit of an integer wrapper.
/// </summary>
/// <remarks>
/// Logix instructions that keep a storage bit (ONS, OSR, OSF) accept either a BOOL tag or a bit of an integer
/// (<c>Word.3</c>). The edge helpers take this interface so the converter can pass either form without knowing
/// which one the source used.
/// </remarks>
public interface IBoolRef
{
    /// <summary>The bit's value.</summary>
    bool Value { get; set; }
}

/// <summary>
/// One bit of an integer wrapper (<c>Word.3</c>, <c>Word.[i]</c>), read and set atomically.
/// </summary>
/// <remarks>
/// A struct rather than a class so that the converter can create one per use site (<c>new PlcBitRef(Word, 3)</c>)
/// inside a scan without allocating. The bit index is relative to the whole integer, least significant bit first,
/// and is translated to the host byte and bit for the atomic write.
/// </remarks>
public readonly struct PlcBitRef : IBoolRef
{
    /// <summary>References bit <paramref name="bit"/> of <paramref name="host"/>.</summary>
    public PlcBitRef(PlcValue host, int bit)
    {
        Host = host;
        Bit = bit;
    }

    /// <summary>The integer wrapper that owns the bit.</summary>
    public PlcValue Host { get; }

    /// <summary>Bit index inside the host value (0 = least significant).</summary>
    public int Bit { get; }

    /// <summary>The bit: read from the host's current value, written atomically into the host byte that holds it.</summary>
    public bool Value
    {
        get => ((PlcConvert.ToInt64(Host.GetValue()) >> Bit) & 1) != 0;
        set => Host.Tag.AtomicSetBit(Host.Offset + Bit / 8, Bit % 8, value);
    }

    /// <summary>Lets a bit reference be used directly as a condition.</summary>
    public static implicit operator bool(PlcBitRef r) => r.Value;

    /// <summary>The bit as 0 / 1, the Logix display form.</summary>
    public override string ToString() => Value ? "1" : "0";
}
