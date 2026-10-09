using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime.Tests;

/// <summary>Logix TIMER as the generator would emit it: hidden DINT host (EN 31, TT 30, DN 29), PRE, ACC.</summary>
public sealed class TIMER : PlcStruct, ITimer
{
    public static readonly PlcLayout Layout = new("TIMER", 12, 4, new[]
    {
        new PlcLayoutMember("ZZZZZZZZZZTIMER0", LogixDataTypes.DINT, null, 0, 0, Array.Empty<int>(), 4, hidden: true),
        new PlcLayoutMember("PRE", LogixDataTypes.DINT, null, 4, 0, Array.Empty<int>(), 4),
        new PlcLayoutMember("ACC", LogixDataTypes.DINT, null, 8, 0, Array.Empty<int>(), 4),
        new PlcLayoutMember("EN", LogixDataTypes.BOOL, null, 3, 7, Array.Empty<int>(), 1),
        new PlcLayoutMember("TT", LogixDataTypes.BOOL, null, 3, 6, Array.Empty<int>(), 1),
        new PlcLayoutMember("DN", LogixDataTypes.BOOL, null, 3, 5, Array.Empty<int>(), 1),
    });

    public readonly WrapDINT PRE;
    public readonly WrapDINT ACC;
    public readonly WrapBOOL EN;
    public readonly WrapBOOL TT;
    public readonly WrapBOOL DN;

    public TIMER(Tag tag, int offset) : base(tag, offset)
    {
        PRE = new WrapDINT(tag, offset + 4);
        ACC = new WrapDINT(tag, offset + 8);
        EN = new WrapBOOL(tag, offset + 3, 7);
        TT = new WrapBOOL(tag, offset + 3, 6);
        DN = new WrapBOOL(tag, offset + 3, 5);
    }

    WrapDINT ITimer.PRE => PRE;
    WrapDINT ITimer.ACC => ACC;
    WrapBOOL ITimer.EN => EN;
    WrapBOOL ITimer.TT => TT;
    WrapBOOL ITimer.DN => DN;

    public override object? GetMember(string name) => name.ToUpperInvariant() switch
    {
        "PRE" => PRE, "ACC" => ACC, "EN" => EN, "TT" => TT, "DN" => DN, _ => null,
    };
}

public sealed class COUNTER : PlcStruct, ICounter
{
    public static readonly PlcLayout Layout = new("COUNTER", 12, 4, new[]
    {
        new PlcLayoutMember("ZZZZZZZZZZCOUNTER0", LogixDataTypes.DINT, null, 0, 0, Array.Empty<int>(), 4, hidden: true),
        new PlcLayoutMember("PRE", LogixDataTypes.DINT, null, 4, 0, Array.Empty<int>(), 4),
        new PlcLayoutMember("ACC", LogixDataTypes.DINT, null, 8, 0, Array.Empty<int>(), 4),
        new PlcLayoutMember("CU", LogixDataTypes.BOOL, null, 3, 7, Array.Empty<int>(), 1),
        new PlcLayoutMember("CD", LogixDataTypes.BOOL, null, 3, 6, Array.Empty<int>(), 1),
        new PlcLayoutMember("DN", LogixDataTypes.BOOL, null, 3, 5, Array.Empty<int>(), 1),
        new PlcLayoutMember("OV", LogixDataTypes.BOOL, null, 3, 4, Array.Empty<int>(), 1),
        new PlcLayoutMember("UN", LogixDataTypes.BOOL, null, 3, 3, Array.Empty<int>(), 1),
    });

    public readonly WrapDINT PRE;
    public readonly WrapDINT ACC;
    public readonly WrapBOOL CU;
    public readonly WrapBOOL CD;
    public readonly WrapBOOL DN;
    public readonly WrapBOOL OV;
    public readonly WrapBOOL UN;

    public COUNTER(Tag tag, int offset) : base(tag, offset)
    {
        PRE = new WrapDINT(tag, offset + 4);
        ACC = new WrapDINT(tag, offset + 8);
        CU = new WrapBOOL(tag, offset + 3, 7);
        CD = new WrapBOOL(tag, offset + 3, 6);
        DN = new WrapBOOL(tag, offset + 3, 5);
        OV = new WrapBOOL(tag, offset + 3, 4);
        UN = new WrapBOOL(tag, offset + 3, 3);
    }

    WrapDINT ICounter.PRE => PRE;
    WrapDINT ICounter.ACC => ACC;
    WrapBOOL ICounter.CU => CU;
    WrapBOOL ICounter.CD => CD;
    WrapBOOL ICounter.DN => DN;
    WrapBOOL ICounter.OV => OV;
    WrapBOOL ICounter.UN => UN;
}

/// <summary>A user structure: Speed REAL, hidden SINT host with Fault bit 0, Timer TIMER, Name STRING.</summary>
public sealed class Motor : PlcStruct
{
    public static readonly PlcLayout Layout = new("Motor", 108, 4, new[]
    {
        new PlcLayoutMember("Speed", LogixDataTypes.REAL, null, 0, 0, Array.Empty<int>(), 4),
        new PlcLayoutMember("ZZZZZZZZZZMotor0", LogixDataTypes.SINT, null, 4, 0, Array.Empty<int>(), 1, hidden: true),
        new PlcLayoutMember("Fault", LogixDataTypes.BOOL, null, 4, 0, Array.Empty<int>(), 1),
        new PlcLayoutMember("Timer", 0, "TIMER", 8, 0, Array.Empty<int>(), 12),
        new PlcLayoutMember("Name", 0, "STRING", 20, 0, Array.Empty<int>(), 88),
    });

    public readonly WrapREAL Speed;
    public readonly WrapBOOL Fault;
    public readonly TIMER Timer;
    public readonly WrapSTRING Name;

    public Motor(Tag tag, int offset) : base(tag, offset)
    {
        Speed = new WrapREAL(tag, offset);
        Fault = new WrapBOOL(tag, offset + 4, 0);
        Timer = new TIMER(tag, offset + 8);
        Name = new WrapSTRING(tag, offset + 20);
    }

    public override object? GetMember(string name) => name.ToUpperInvariant() switch
    {
        "SPEED" => Speed, "FAULT" => Fault, "TIMER" => Timer, "NAME" => Name, _ => null,
    };
}

/// <summary>
/// A minimal MESSAGE structure, as a real one lays out the status bits the MSG service reads and writes:
/// EW/ER/DN/ST/EN packed in one byte, plus ERR. Member order and offsets are arbitrary for the test; only the
/// names matter, since <see cref="LogixServices.MSG"/> looks members up by name through <see cref="GetMember"/>.
/// </summary>
public sealed class MESSAGE : PlcStruct
{
    public static readonly PlcLayout Layout = new("MESSAGE", 4, 4, new[]
    {
        new PlcLayoutMember("ERR", LogixDataTypes.INT, null, 0, 0, Array.Empty<int>(), 2),
        new PlcLayoutMember("ZZZZZZZZZZMESSAGE0", LogixDataTypes.SINT, null, 2, 0, Array.Empty<int>(), 1, hidden: true),
        new PlcLayoutMember("EW", LogixDataTypes.BOOL, null, 2, 0, Array.Empty<int>(), 1),
        new PlcLayoutMember("ER", LogixDataTypes.BOOL, null, 2, 1, Array.Empty<int>(), 1),
        new PlcLayoutMember("DN", LogixDataTypes.BOOL, null, 2, 2, Array.Empty<int>(), 1),
        new PlcLayoutMember("ST", LogixDataTypes.BOOL, null, 2, 3, Array.Empty<int>(), 1),
        new PlcLayoutMember("EN", LogixDataTypes.BOOL, null, 2, 4, Array.Empty<int>(), 1),
    });

    public readonly WrapINT ERR;
    public readonly WrapBOOL EW;
    public readonly WrapBOOL ER;
    public readonly WrapBOOL DN;
    public readonly WrapBOOL ST;
    public readonly WrapBOOL EN;

    public MESSAGE(Tag tag, int offset) : base(tag, offset)
    {
        ERR = new WrapINT(tag, offset);
        EW = new WrapBOOL(tag, offset + 2, 0);
        ER = new WrapBOOL(tag, offset + 2, 1);
        DN = new WrapBOOL(tag, offset + 2, 2);
        ST = new WrapBOOL(tag, offset + 2, 3);
        EN = new WrapBOOL(tag, offset + 2, 4);
    }

    public override object? GetMember(string name) => name.ToUpperInvariant() switch
    {
        "ERR" => ERR, "EW" => EW, "ER" => ER, "DN" => DN, "ST" => ST, "EN" => EN, _ => null,
    };
}

public sealed class STRING20 : WrapSTRING
{
    public static new readonly PlcLayout Layout = LayoutFor("STRING20", 20);
    public STRING20(Tag tag, int offset) : base(tag, offset, 20) { }
}

/// <summary>A controller with one continuous task running one program that counts scans.</summary>
public sealed class TestController : PlcController
{
    public readonly WrapDINT Scans;
    public readonly WrapBOOL SeenFirstScan;
    public readonly WrapDINT PrescanCount;
    public readonly TestProgram P;

    public TestController(PlcControllerOptions? options = null) : base("Test", options ?? new PlcControllerOptions { EipPort = null, Log = _ => { } })
    {
        Scans = new WrapDINT(Store.Define("Scans", LogixDataTypes.DINT, Array.Empty<int>()), 0);
        SeenFirstScan = new WrapBOOL(Store.Define("SeenFirstScan", LogixDataTypes.BOOL, Array.Empty<int>()), 0);
        PrescanCount = new WrapDINT(Store.Define("PrescanCount", LogixDataTypes.DINT, Array.Empty<int>()), 0);
        P = new TestProgram(this);
        Programs.Add(P);
        Tasks.Add(new PlcTask("Main", TaskKind.Continuous, null, 10, 5000, new[] { P }));
    }

    protected override void PrescanAll()
    {
        foreach (var p in Programs) p.Prescan();
    }
}

public sealed class TestProgram : PlcProgram<TestController>
{
    public int FirstScanTrueCount;

    public TestProgram(TestController c) : base(c, "P") { }

    public override void Main()
    {
        C.Scans.Value = C.Scans.Value + 1;
        if (Sys.FirstScan)
        {
            FirstScanTrueCount++;
            C.SeenFirstScan.Value = true;
        }
        Thread.Sleep(1);
    }

    public override void Prescan()
    {
        C.PrescanCount.Value = C.PrescanCount.Value + 1;
        C.Scans.Value = 0;
    }
}
