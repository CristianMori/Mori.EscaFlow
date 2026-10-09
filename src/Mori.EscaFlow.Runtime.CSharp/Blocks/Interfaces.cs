namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Members of the Logix TIMER structure, implemented by the generated <c>TIMER</c> class.
/// </summary>
/// <remarks>
/// The runtime's timer instructions work on this interface rather than on a runtime-owned class because the
/// TIMER type, like every structure, is generated from the project so that its byte layout (hidden DINT host with
/// EN / TT / DN at bits 31 / 30 / 29, then PRE, then ACC; 12 bytes) is the one the project and its clients expect.
/// The interface is the contract between the two.
/// </remarks>
public interface ITimer
{
    /// <summary>Preset in milliseconds.</summary>
    WrapDINT PRE { get; }

    /// <summary>Accumulated milliseconds.</summary>
    WrapDINT ACC { get; }

    /// <summary>Enable: the rung condition the instruction last saw.</summary>
    WrapBOOL EN { get; }

    /// <summary>Timer timing: true while ACC is increasing.</summary>
    WrapBOOL TT { get; }

    /// <summary>Done: ACC reached PRE (for TOF: the delay has not elapsed yet).</summary>
    WrapBOOL DN { get; }
}

/// <summary>Members of the Logix COUNTER structure (12 bytes: host DINT with CU / CD / DN / OV / UN, PRE, ACC).</summary>
public interface ICounter
{
    /// <summary>Preset count.</summary>
    WrapDINT PRE { get; }

    /// <summary>Accumulated count (wraps at the DINT range, see OV / UN).</summary>
    WrapDINT ACC { get; }

    /// <summary>Count-up enable: the rung condition CTU last saw, used to detect its rising edge.</summary>
    WrapBOOL CU { get; }

    /// <summary>Count-down enable: the rung condition CTD last saw.</summary>
    WrapBOOL CD { get; }

    /// <summary>Done: ACC is at or above PRE.</summary>
    WrapBOOL DN { get; }

    /// <summary>Overflow: ACC wrapped from the maximum DINT to the minimum.</summary>
    WrapBOOL OV { get; }

    /// <summary>Underflow: ACC wrapped from the minimum DINT to the maximum.</summary>
    WrapBOOL UN { get; }
}

/// <summary>Members of the Logix CONTROL structure used by file, shift and sequencer instructions.</summary>
public interface IControl
{
    /// <summary>Number of elements the instruction works on.</summary>
    WrapDINT LEN { get; }

    /// <summary>Current position.</summary>
    WrapDINT POS { get; }

    /// <summary>Enable.</summary>
    WrapBOOL EN { get; }

    /// <summary>Enable unload (FIFO / LIFO).</summary>
    WrapBOOL EU { get; }

    /// <summary>Done.</summary>
    WrapBOOL DN { get; }

    /// <summary>Empty.</summary>
    WrapBOOL EM { get; }

    /// <summary>Error.</summary>
    WrapBOOL ER { get; }

    /// <summary>Unload.</summary>
    WrapBOOL UL { get; }

    /// <summary>Inhibit.</summary>
    WrapBOOL IN { get; }

    /// <summary>Found.</summary>
    WrapBOOL FD { get; }
}
