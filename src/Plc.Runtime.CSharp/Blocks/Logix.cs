using System.Runtime.CompilerServices;

namespace Plc.Runtime;

/// <summary>
/// The Logix profile of the standard function blocks: timers on TIMER structures (DINT milliseconds, EN / TT / DN
/// status) and counters on COUNTER structures, with the rung condition as the enable pin. Timing uses
/// <see cref="Clock"/> (monotonic by default; tests inject a manual clock).
/// </summary>
public static class Logix
{
    private sealed class TimerState
    {
        public long LastMs;
        public bool Timing;
    }

    private static readonly ConditionalWeakTable<ITimer, TimerState> States = new();

    public static IPlcClock Clock { get; set; } = PlcClock.Monotonic;

    private static TimerState StateOf(ITimer t) => States.GetValue(t, static _ => new TimerState());

    /// <summary>Elapsed milliseconds since the last call for this timer, capped at int range; starts timing on the first call.</summary>
    private static int Elapsed(TimerState s)
    {
        var now = Clock.NowMs;
        if (!s.Timing)
        {
            s.Timing = true;
            s.LastMs = now;
            return 0;
        }
        var e = now - s.LastMs;
        s.LastMs = now;
        return e > int.MaxValue ? int.MaxValue : (int)e;
    }

    /// <summary>TON: times while the rung is true; DN when ACC reaches PRE; everything resets when the rung is false.</summary>
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

    private static int Accumulate(int acc, int elapsed)
    {
        var sum = (long)acc + elapsed;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>CTU: counts rising edges of the rung (tracked in CU); DN when ACC reaches PRE; OV when ACC wraps.</summary>
    public static void CTU(ICounter c, bool cu)
    {
        if (cu && !c.CU.Value)
        {
            var acc = c.ACC.Value;
            if (acc == int.MaxValue)
            {
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

    public static void PrescanTON(ITimer t)
    {
        t.EN.Value = false;
        t.TT.Value = false;
        t.DN.Value = false;
        t.ACC.Value = 0;
        StateOf(t).Timing = false;
    }

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

    public static void PrescanCTD(ICounter c) => c.CD.Value = true;
}
