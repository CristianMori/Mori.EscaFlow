using System.Diagnostics;

namespace Mori.EscaFlow.Runtime;

/// <summary>Monotonic millisecond clock used by timers and the scan engine; replaceable in tests.</summary>
public interface IPlcClock
{
    long NowMs { get; }
}

public static class PlcClock
{
    public static readonly IPlcClock Monotonic = new StopwatchClock();

    private sealed class StopwatchClock : IPlcClock
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public long NowMs => (long)((Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency);
    }
}

/// <summary>A clock advanced by hand, for deterministic tests.</summary>
public sealed class ManualClock : IPlcClock
{
    public long NowMs { get; set; }
    public void Advance(long ms) => NowMs += ms;
}
