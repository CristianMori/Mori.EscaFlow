using System.Runtime.CompilerServices;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// The Logix profile of the standard function blocks: timers on TIMER structures (DINT milliseconds, EN / TT / DN
/// status) and counters on COUNTER structures, with the rung condition as the enable pin. Timing uses
/// <see cref="Clock"/> (monotonic by default; tests inject a manual clock).
/// </summary>
/// <remarks>
/// <para>
/// The converter emits one call per scan for every timer or counter instruction, with the rung condition as the
/// second argument, exactly as the controller evaluates the instruction every scan it is reached. All persistent
/// state lives in the structure members (so it is visible to clients, survives snapshots, and can be forced from
/// the web console); the only runtime-private state is the timestamp of the previous evaluation, needed to turn
/// wall-clock time into ACC milliseconds. That timestamp is kept in a weak table keyed by the structure view, so
/// timers need no registration and are collected with their controller.
/// </para>
/// <para>
/// Logix timers count in milliseconds of elapsed time, not scans: ACC advances by the real time between two
/// evaluations, which is why a monotonic clock is used and why a timer that is not evaluated for a while (its rung
/// is inside a skipped branch, or an MCR zone) does not accumulate during that gap.
/// </para>
/// </remarks>
public static class Logix
{
    /// <summary>Runtime-private timer state: when the timer was last evaluated while timing.</summary>
    private sealed class TimerState
    {
        /// <summary>Clock reading at the previous evaluation.</summary>
        public long LastMs;

        /// <summary>True once <see cref="LastMs"/> is valid; false restarts the elapsed measurement on the next call.</summary>
        public bool Timing;
    }

    /// <summary>Per-instance state, weakly keyed so instances are never kept alive by the runtime.</summary>
    private static readonly ConditionalWeakTable<ITimer, TimerState> States = new();

    /// <summary>The clock timers read; tests replace it with a <see cref="ManualClock"/> for deterministic timing.</summary>
    public static IPlcClock Clock { get; set; } = PlcClock.Monotonic;

    private static TimerState StateOf(ITimer t) => States.GetValue(t, static _ => new TimerState());

    /// <summary>Elapsed milliseconds since the last call for this timer, capped at int range; starts timing on the first call.</summary>
    private static int Elapsed(TimerState s)
    {
        var now = Clock.NowMs;
        if (!s.Timing)
        {
            // First evaluation after (re)start: nothing elapsed yet, just remember the reference point.
            s.Timing = true;
            s.LastMs = now;
            return 0;
        }
        var e = now - s.LastMs;
        s.LastMs = now;
        return e > int.MaxValue ? int.MaxValue : (int)e;
    }

    /// <summary>TON: times while the rung is true; DN when ACC reaches PRE; everything resets when the rung is false.</summary>
    /// <remarks>
    /// Logix semantics reproduced: a false rung clears EN, TT, DN and ACC immediately (the on-delay restarts from
    /// zero each time the rung goes true); the rising edge of the rung sets EN and starts timing; while timing TT
    /// is true; when ACC reaches PRE, ACC is clamped to PRE, DN is set and TT cleared, and the timer stays done
    /// without accumulating further until the rung drops.
    /// </remarks>
    public static void TON(ITimer t, bool enable)
    {
        var s = StateOf(t);
        if (!enable)
        {
            t.EN.Value = false;
            t.TT.Value = false;
            t.DN.Value = false;
            t.ACC.Value = 0;
            s.Timing = false;
            return;
        }
        if (!t.EN.Value)
        {
            // Rising edge of the rung: start the elapsed measurement from now.
            t.EN.Value = true;
            s.Timing = false;
        }
        if (t.DN.Value)
        {
            t.TT.Value = false;
            return;
        }
        var acc = Accumulate(t.ACC.Value, Elapsed(s));
        if (acc >= t.PRE.Value)
        {
            t.ACC.Value = t.PRE.Value;
            t.DN.Value = true;
            t.TT.Value = false;
        }
        else
        {
            t.ACC.Value = acc;
            t.TT.Value = true;
        }
    }

    /// <summary>TOF: DN follows the rung immediately when true; when the rung goes false DN stays until PRE elapsed.</summary>
    /// <remarks>
    /// The off-delay timer is the mirror of TON: a true rung sets EN and DN and holds ACC at zero; the falling
    /// edge clears EN and starts timing; DN stays true while ACC climbs to PRE and clears when it gets there,
    /// which is what makes outputs driven by DN stay on for PRE milliseconds after their condition drops.
    /// </remarks>
    public static void TOF(ITimer t, bool enable)
    {
        var s = StateOf(t);
        if (enable)
        {
            t.EN.Value = true;
            t.DN.Value = true;
            t.TT.Value = false;
            t.ACC.Value = 0;
            s.Timing = false;
            return;
        }
        if (t.EN.Value)
        {
            // Falling edge of the rung: start the off-delay from now.
            t.EN.Value = false;
            s.Timing = false;
        }
        if (!t.DN.Value)
        {
            t.TT.Value = false;
            return;
        }
        var acc = Accumulate(t.ACC.Value, Elapsed(s));
        if (acc >= t.PRE.Value)
        {
            t.ACC.Value = t.PRE.Value;
            t.DN.Value = false;
            t.TT.Value = false;
        }
        else
        {
            t.ACC.Value = acc;
            t.TT.Value = true;
        }
    }

    /// <summary>TONR (Logix RTO): accumulates while the rung is true and holds when false; cleared only by RES (member assignments).</summary>
    /// <remarks>
    /// Unlike TON, a false rung only stops timing (EN and TT clear) and leaves ACC and DN as they are, so the
    /// accumulated time survives rung drops, scans and even power cycles (ACC is a retentive tag member). The
    /// converter lowers RES on the timer to the member assignments <see cref="ResetTimer"/> performs.
    /// </remarks>
    public static void TONR(ITimer t, bool enable)
    {
        var s = StateOf(t);
        if (!enable)
        {
            t.EN.Value = false;
            t.TT.Value = false;
            s.Timing = false;
            return;
        }
        if (!t.EN.Value)
        {
            t.EN.Value = true;
            s.Timing = false;
        }
        if (t.DN.Value || t.ACC.Value >= t.PRE.Value)
        {
            // Already done (possibly because a client or RES changed PRE / ACC): keep DN, stop timing.
            t.DN.Value = true;
            t.TT.Value = false;
            return;
        }
        var acc = Accumulate(t.ACC.Value, Elapsed(s));
        if (acc >= t.PRE.Value)
        {
            t.ACC.Value = t.PRE.Value;
            t.DN.Value = true;
            t.TT.Value = false;
        }
        else
        {
            t.ACC.Value = acc;
            t.TT.Value = true;
        }
    }

    /// <summary>Adds elapsed time to ACC, saturating at the DINT maximum instead of wrapping negative.</summary>
    private static int Accumulate(int acc, int elapsed)
    {
        var sum = (long)acc + elapsed;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>CTU: counts rising edges of the rung (tracked in CU); DN when ACC reaches PRE; OV when ACC wraps.</summary>
    /// <remarks>
    /// The CU member is the previous rung state, so the rising edge is detected between scans without any
    /// runtime-private storage; this is also why prescan sets CU true (<see cref="PrescanCTU"/>): a rung that is
    /// already true on the first scan must not count. DN is re-evaluated every scan from ACC and PRE, as Logix does,
    /// so a client changing PRE changes DN at the next evaluation.
    /// </remarks>
    public static void CTU(ICounter c, bool cu)
    {
        if (cu && !c.CU.Value)
        {
            var acc = c.ACC.Value;
            if (acc == int.MaxValue)
            {
                // Logix wraps the accumulator and flags the overflow instead of faulting.
                c.ACC.Value = int.MinValue;
                c.OV.Value = true;
            }
            else
            {
                c.ACC.Value = acc + 1;
            }
        }
        c.CU.Value = cu;
        c.DN.Value = c.ACC.Value >= c.PRE.Value;
    }

    /// <summary>CTD: counts down on rising edges (tracked in CD); DN when ACC reaches PRE; UN when ACC wraps.</summary>
    public static void CTD(ICounter c, bool cd)
    {
        if (cd && !c.CD.Value)
        {
            var acc = c.ACC.Value;
            if (acc == int.MinValue)
            {
                c.ACC.Value = int.MaxValue;
                c.UN.Value = true;
            }
            else
            {
                c.ACC.Value = acc - 1;
            }
        }
        c.CD.Value = cd;
        c.DN.Value = c.ACC.Value >= c.PRE.Value;
    }

    /// <summary>RES on a timer: ACC, EN, TT and DN cleared.</summary>
    public static void ResetTimer(ITimer t)
    {
        t.ACC.Value = 0;
        t.EN.Value = false;
        t.TT.Value = false;
        t.DN.Value = false;
        StateOf(t).Timing = false;
    }

    /// <summary>RES on a counter: ACC, CU, CD, DN, OV and UN cleared.</summary>
    public static void ResetCounter(ICounter c)
    {
        c.ACC.Value = 0;
        c.CU.Value = false;
        c.CD.Value = false;
        c.DN.Value = false;
        c.OV.Value = false;
        c.UN.Value = false;
    }

    /// <summary>Prescan of TON: the controller clears EN, TT, DN and ACC so the first scan starts from a known state.</summary>
    public static void PrescanTON(ITimer t)
    {
        t.EN.Value = false;
        t.TT.Value = false;
        t.DN.Value = false;
        t.ACC.Value = 0;
        StateOf(t).Timing = false;
    }

    /// <summary>Prescan of TOF: same clearing as TON.</summary>
    public static void PrescanTOF(ITimer t) => PrescanTON(t);

    /// <summary>RTO keeps its accumulated value through prescan.</summary>
    public static void PrescanTONR(ITimer t)
    {
        t.EN.Value = false;
        t.TT.Value = false;
        t.DN.Value = false;
        StateOf(t).Timing = false;
    }

    /// <summary>CU is set so the first scan does not count.</summary>
    public static void PrescanCTU(ICounter c) => c.CU.Value = true;

    /// <summary>CD is set so the first scan does not count down.</summary>
    public static void PrescanCTD(ICounter c) => c.CD.Value = true;
}
