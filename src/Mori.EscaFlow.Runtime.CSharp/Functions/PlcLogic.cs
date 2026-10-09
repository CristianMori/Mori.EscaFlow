namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Edge detection with a BOOL storage location holding the previous value.
/// </summary>
/// <remarks>
/// Logix one-shots (ONS, OSR, OSF) keep their memory in a tag bit the programmer names, not in hidden state, so
/// the previous value survives scans, snapshots and online edits and can be inspected. The converter lowers them
/// to these two functions with the rung condition as the clock and that bit as the storage; the storage is always
/// updated, even when no edge is reported, exactly as the instructions do.
/// </remarks>
public static class PlcLogic
{
    /// <summary>True for one evaluation when <paramref name="clk"/> went from false to true; the storage records the last value.</summary>
    public static bool RisingEdge(bool clk, IBoolRef storage)
    {
        var last = storage.Value;
        storage.Value = clk;
        return clk && !last;
    }

    /// <summary>True for one evaluation when <paramref name="clk"/> went from true to false; the storage records the last value.</summary>
    public static bool FallingEdge(bool clk, IBoolRef storage)
    {
        var last = storage.Value;
        storage.Value = clk;
        return !clk && last;
    }

    /// <summary>Rising edge with a BOOL wrapper as storage (avoids boxing the interface for the common case).</summary>
    public static bool RisingEdge(bool clk, WrapBOOL storage) => RisingEdge(clk, (IBoolRef)storage);

    /// <summary>Falling edge with a BOOL wrapper as storage.</summary>
    public static bool FallingEdge(bool clk, WrapBOOL storage) => FallingEdge(clk, (IBoolRef)storage);
}
