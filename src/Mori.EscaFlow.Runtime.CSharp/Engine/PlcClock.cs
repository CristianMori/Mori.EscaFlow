using System.Diagnostics;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Monotonic millisecond clock used by timers and the scan engine; replaceable in tests.
/// </summary>
/// <remarks>
/// Timers must never see time go backwards (a wall-clock adjustment would make a TON finish early or never), so
/// the production clock is derived from <see cref="Stopwatch"/>, and tests advance a <see cref="ManualClock"/> by
/// hand to check exact ACC values.
/// </remarks>
public interface IPlcClock
{
    /// <summary>Milliseconds since an arbitrary origin; only differences are meaningful.</summary>
    long NowMs { get; }
}

/// <summary>Clock implementations.</summary>
public static class PlcClock
{
    /// <summary>The process-wide monotonic clock.</summary>
    public static readonly IPlcClock Monotonic = new StopwatchClock();

    /// <summary>Milliseconds since the clock was created, from the high-resolution stopwatch counter.</summary>
    private sealed class StopwatchClock : IPlcClock
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public long NowMs => (long)((Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency);
    }
}

/// <summary>A clock advanced by hand, for deterministic tests.</summary>
public sealed class ManualClock : IPlcClock
{
    /// <inheritdoc />
    public long NowMs { get; set; }

    /// <summary>Moves the clock forward by <paramref name="ms"/> milliseconds.</summary>
    public void Advance(long ms) => NowMs += ms;
}
