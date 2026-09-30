namespace Plc.Runtime;

/// <summary>Edge detection with a BOOL storage location holding the previous value.</summary>
public static class PlcLogic
{
    /// <summary>True for one evaluation when <paramref name="clk"/> went from false to true; the storage records the last value.</summary>
    public static bool RisingEdge(bool clk, IBoolRef storage)
    {
        var last = storage.Value;
        storage.Value = clk;
        return clk && !last;
    }

    public static bool FallingEdge(bool clk, IBoolRef storage)
    {
        var last = storage.Value;
        storage.Value = clk;
        return !clk && last;
    }

    public static bool RisingEdge(bool clk, WrapBOOL storage) => RisingEdge(clk, (IBoolRef)storage);
    public static bool FallingEdge(bool clk, WrapBOOL storage) => FallingEdge(clk, (IBoolRef)storage);
}
