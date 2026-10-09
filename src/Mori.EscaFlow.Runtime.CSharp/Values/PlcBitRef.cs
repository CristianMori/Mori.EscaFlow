namespace Mori.EscaFlow.Runtime;

/// <summary>A BOOL location: a BOOL wrapper or one bit of an integer wrapper.</summary>
public interface IBoolRef
{
    bool Value { get; set; }
}

/// <summary>One bit of an integer wrapper (<c>Word.3</c>, <c>Word.[i]</c>), read and set atomically.</summary>
public readonly struct PlcBitRef : IBoolRef
{
    public PlcBitRef(PlcValue host, int bit)
    {
        Host = host;
        Bit = bit;
    }

    public PlcValue Host { get; }
    public int Bit { get; }

    public bool Value
    {
        get => ((PlcConvert.ToInt64(Host.GetValue()) >> Bit) & 1) != 0;
        set => Host.Tag.AtomicSetBit(Host.Offset + Bit / 8, Bit % 8, value);
    }

    public static implicit operator bool(PlcBitRef r) => r.Value;
    public override string ToString() => Value ? "1" : "0";
}
