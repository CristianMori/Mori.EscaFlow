namespace Mori.EscaFlow.Runtime;

/// <summary>Members of the Logix TIMER structure, implemented by the generated <c>TIMER</c> class.</summary>
public interface ITimer
{
    WrapDINT PRE { get; }
    WrapDINT ACC { get; }
    WrapBOOL EN { get; }
    WrapBOOL TT { get; }
    WrapBOOL DN { get; }
}

/// <summary>Members of the Logix COUNTER structure.</summary>
public interface ICounter
{
    WrapDINT PRE { get; }
    WrapDINT ACC { get; }
    WrapBOOL CU { get; }
    WrapBOOL CD { get; }
    WrapBOOL DN { get; }
    WrapBOOL OV { get; }
    WrapBOOL UN { get; }
}

/// <summary>Members of the Logix CONTROL structure.</summary>
public interface IControl
{
    WrapDINT LEN { get; }
    WrapDINT POS { get; }
    WrapBOOL EN { get; }
    WrapBOOL EU { get; }
    WrapBOOL DN { get; }
    WrapBOOL EM { get; }
    WrapBOOL ER { get; }
    WrapBOOL UL { get; }
    WrapBOOL IN { get; }
    WrapBOOL FD { get; }
}
