namespace Mori.EscaFlow.Transpiler.Tests;

/// <summary>A vendor extension object as a front-end would attach it.</summary>
public sealed class LogixTestExt
{
    public string Path { get; set; } = "";
    public string? Radix { get; set; }
    public bool Packed { get; set; }
}

/// <summary>
/// A hand-built document that exercises every node kind of the model: every statement and expression kind, every
/// section of a POU, ladder/FBD/SFC diagrams, aliases, I/O channels and a full configuration/resource/task layout.
/// It is the single fixture shared by the JSON round-trip, schema-coverage, walker and printer tests, so a model
/// change that drops or renames a node kind is caught by whichever of those tests happens to exercise it, without
/// each test needing its own bespoke document.
/// </summary>
/// <remarks>The <c>const string</c> fields below are the plc-ir ids of selected variables, exposed so a test can assert against a specific node (e.g. "the alias target is M1") without re-deriving the id string by hand.</remarks>
public static class SampleDocument
{
    public const string GvlT = "T";
    public const string M1 = "global/T/M1";
    public const string Flags = "global/T/Flags";
    public const string Al = "global/T/Al";
    public const string Run = "global/T/Run";
    public const string Stop = "global/T/Stop";
    public const string Count = "global/T/Count";
    public const string Name = "global/T/Name";
    public const string Ptr = "global/T/Ptr";
    public const string Blk = "global/T/Blk";
    public const string Color = "global/T/Color";
    public const string DiI = "global/IO/DI:I";
    public const string Idx = "pou/P/Idx";
    public const string Step1 = "pou/P/Step1";

    public static IrDocument Build()
    {
        var doc = new IrDocument
        {
            Source = new IrSource
            {
                Vendor = "logix",
                Dialect = "l5x",
                File = "Sample.L5X",
                ExportDate = "Mon Sep 28 10:00:00 2026",
                SoftwareRevision = "33.00",
                Generator = "Mori.EscaFlow.Transpiler.Tests",
                GeneratedAt = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero),
            },
            Description = "sample",
        };
        doc.SetVendor("logix", new { majorRev = 33, minorRev = 12 });

        // ------------------------------------------------------------ types
        doc.Types.Add(new IrType
        {
            Name = "TIMER",
            Kind = "struct",
            Class = "vendor",
            StdFb = new IrStdFbBinding { Fbs = { "TON", "TOF", "TONR" }, Profile = "logix" },
            Members = new()
            {
                new IrMember { Name = "PRE", Type = IrTypeRef.Named("DINT") },
                new IrMember { Name = "ACC", Type = IrTypeRef.Named("DINT") },
                new IrMember { Name = "EN", Type = IrTypeRef.Named("BOOL") },
                new IrMember { Name = "TT", Type = IrTypeRef.Named("BOOL") },
                new IrMember { Name = "DN", Type = IrTypeRef.Named("BOOL") },
            },
        });
        var fault = new IrMember { Name = "Fault", Type = IrTypeRef.Named("BOOL"), Description = "motor fault" };
        fault.SetVendor("logix", new { bitHost = "ZZZZZZZZZZMotor0", bitNumber = 0 });
        doc.Types.Add(new IrType
        {
            Name = "Motor",
            Kind = "struct",
            Class = "user",
            Description = "a motor",
            Members = new()
            {
                new IrMember { Name = "Speed", Type = IrTypeRef.Named("REAL"), Initial = new IrScalarValue { Type = "REAL", Real = 1.5 } },
                fault,
                new IrMember { Name = "Timer", Type = IrTypeRef.Named("TIMER") },
                new IrMember { Name = "History", Type = IrTypeRef.Array(IrTypeRef.Named("REAL"), new IrDim(0, 9)) },
            },
        });
        doc.Types.Add(new IrType { Name = "Color", Kind = "enum", Class = "user", Type = IrTypeRef.Named("INT"), Values = new() { new IrEnumValue { Name = "Red", Value = 0 }, new IrEnumValue { Name = "Green", Value = 1 } } });
        doc.Types.Add(new IrType { Name = "Speed_t", Kind = "alias", Class = "user", Type = IrTypeRef.Named("REAL") });
        doc.Types.Add(new IrType { Name = "Pct", Kind = "subrange", Class = "user", Type = IrTypeRef.Named("INT"), Lower = 0, Upper = 100 });
        doc.Types.Add(new IrType { Name = "Vec3", Kind = "array", Class = "user", Type = IrTypeRef.Named("REAL"), Dims = new() { new IrDim(0, 2) } });
        doc.Types.Add(new IrType { Name = "Overlay", Kind = "union", Class = "library", Members = new() { new IrMember { Name = "AsInt", Type = IrTypeRef.Named("DINT") }, new IrMember { Name = "AsReal", Type = IrTypeRef.Named("REAL") } } });
        doc.Types.Add(new IrType { Name = "STRING", Kind = "string", Class = "vendor", Length = 82 });
        doc.Types.Add(new IrType { Name = "WNAME", Kind = "string", Class = "user", Length = 20, Wide = true });
        doc.Types.Add(new IrType { Name = "AB:1756_DI:I:0", Kind = "struct", Class = "io", Members = new() { new IrMember { Name = "Fault", Type = IrTypeRef.Named("DINT") }, new IrMember { Name = "Data", Type = IrTypeRef.Named("DINT") } } });
        doc.Types.Add(new IrType { Name = "Mystery", Kind = "struct", Class = "unresolved", Opaque = true });

        // ------------------------------------------------------------ globals
        var msgVar = new IrVariable { Id = Name, Name = "Name", Section = "global", Type = IrTypeRef.Named("STRING"), Initial = new IrStringValue { Type = "STRING", Length = 3, Text = "abc" } };
        msgVar.SetVendor("logix", new LogixTestExt { Path = "Name", Radix = "ASCII", Packed = false });
        var gvl = new IrGlobalVariableList
        {
            Name = GvlT,
            Vars =
            {
                new IrVariable
                {
                    Id = M1, Name = "M1", Section = "global", Type = IrTypeRef.Named("Motor"), Retain = "retain",
                    Initial = new IrStructValue
                    {
                        Type = "Motor",
                        Members = new()
                        {
                            new IrNamedValue { Name = "Speed", Value = new IrScalarValue { Type = "REAL", Real = 1.5 } },
                            new IrNamedValue { Name = "Fault", Value = new IrScalarValue { Type = "BOOL", Int = 0 } },
                            new IrNamedValue { Name = "History", Value = new IrArrayValue { Type = "REAL", Dims = new[] { 10 }, Elements = Enumerable.Range(0, 10).Select(i => (IrValue)new IrScalarValue { Type = "REAL", Real = i }).ToList() } },
                        },
                    },
                    Comments = new() { [".Speed"] = "rpm" },
                    Attributes = new() { ["EngineeringUnit"] = "rpm" },
                },
                new IrVariable { Id = Flags, Name = "Flags", Section = "global", Type = IrTypeRef.Array(IrTypeRef.Named("BOOL"), new IrDim(0, 31)) },
                new IrVariable { Id = Run, Name = "Run", Section = "global", Type = IrTypeRef.Named("BOOL"), Retain = "persistent", Initial = new IrScalarValue { Type = "BOOL", Int = 1 } },
                new IrVariable { Id = Stop, Name = "Stop", Section = "global", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = Count, Name = "Count", Section = "global", Type = IrTypeRef.Named("DINT"), Constant = false, Initial = new IrScalarValue { Type = "DINT", Int = 7 } },
                new IrVariable { Id = Al, Name = "Al", Section = "global", Type = IrTypeRef.Named("BOOL"), Alias = new IrAlias { Text = "M1.Fault", Target = IrRef.Variable(M1, IrTypeRef.Named("BOOL")).Member("Fault") } },
                msgVar,
                new IrVariable { Id = Ptr, Name = "Ptr", Section = "global", Type = new IrTypeRef { Kind = "pointer", To = IrTypeRef.Named("Motor") } },
                new IrVariable { Id = Blk, Name = "Blk", Section = "global", Type = IrTypeRef.Named("Blink") },
                new IrVariable { Id = Color, Name = "Color", Section = "global", Type = IrTypeRef.Named("Color"), Initial = new IrScalarValue { Type = "Color", Int = 1, Raw = "Green" } },
                new IrVariable { Id = "global/T/When", Name = "When", Section = "global", Type = IrTypeRef.Named("DT"), Initial = new IrScalarValue { Type = "DT", Raw = "DT#2026-09-29-12:00:00" } },
                new IrVariable { Id = "global/T/Ref", Name = "Ref", Section = "global", Type = new IrTypeRef { Kind = "reference", To = IrTypeRef.Named("DINT") }, Address = "%MD100" },
            },
        };
        doc.Globals.Add(gvl);
        doc.Globals.Add(new IrGlobalVariableList
        {
            Name = "IO",
            Vars = { new IrVariable { Id = DiI, Name = "DI:I", Section = "global", Type = IrTypeRef.Named("AB:1756_DI:I:0"), Io = "io/DI/I" } },
        });

        // ------------------------------------------------------------ io
        doc.Io.Devices.Add(new IrIoDevice { Id = "device/Local", Name = "Local", Catalog = "1756-L85E" });
        doc.Io.Devices.Add(new IrIoDevice { Id = "device/DI", Name = "DI", Catalog = "1756-IB16", VendorName = "Rockwell", Parent = "device/Local", Inhibited = false });
        doc.Io.Channels.Add(new IrIoChannel { Id = "io/DI/I", Device = "device/DI", Name = "I", Direction = "input", Type = IrTypeRef.Named("AB:1756_DI:I:0"), Variable = DiI, Address = "Local:3:I" });

        // ------------------------------------------------------------ configuration
        doc.Configuration = new IrConfiguration
        {
            Name = "Sample",
            Resources =
            {
                new IrResource
                {
                    Name = "Sample",
                    ProcessorType = "1756-L85E",
                    Tasks =
                    {
                        new IrTask { Name = "Main", Kind = "continuous", Priority = 10, WatchdogMs = 500, Programs = { "P" } },
                        new IrTask { Name = "Fast", Kind = "periodic", IntervalMs = 10, Priority = 5 },
                        new IrTask { Name = "Ev", Kind = "event", Event = new IrTaskEvent { Kind = "Consumed Tag", Source = Run }, Disabled = true },
                    },
                    Programs = { new IrProgramInstance { Name = "P", Pou = "P", Task = "Main" } },
                    FaultProgram = "P",
                },
            },
        };

        // ------------------------------------------------------------ pous
        var p = new IrPou
        {
            Name = "P",
            Kind = "program",
            Vars =
            {
                new IrVariable { Id = Idx, Name = "Idx", Section = "local", Type = IrTypeRef.Named("DINT") },
                new IrVariable { Id = "pou/P/Tmp", Name = "Tmp", Section = "temp", Type = IrTypeRef.Named("REAL") },
                new IrVariable { Id = "pou/P/Cnt", Name = "Cnt", Section = "static", Type = IrTypeRef.Named("DINT") },
                new IrVariable { Id = "pou/P/Ext", Name = "Ext", Section = "external", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = Step1, Name = "Step1", Section = "local", Type = IrTypeRef.Named("SFC_STEP") },
            },
            Body = new IrBody { Language = "st", Statements = StStatements(), Text = new List<string> { "IF Run AND NOT Stop THEN Idx := Idx + 1; END_IF;" }, Comments = new List<IrComment> { new() { Text = "main", Line = 0 } } },
            Actions =
            {
                new IrAction { Name = "Main", Body = LadderBody(), Calls = new List<string> { "Sub" } },
                new IrAction { Name = "Net", Body = NetBody() },
                new IrAction { Name = "Seq", Body = SfcBody() },
                new IrAction { Name = "Sub", Body = new IrBody { Language = "st", Statements = { new IrReturn() } } },
                new IrAction { Name = "Prescan", Hook = "init", Synthetic = true, Body = new IrBody { Language = "st", Synthetic = true, Statements = { new IrAssign { Target = IrRef.Variable(Run, IrTypeRef.Named("BOOL")), Value = IrLiteral.False } } } },
            },
            OnFault = "Sub",
            Description = "program P",
        };
        doc.Pous.Add(p);
        doc.Pous.Add(new IrPou
        {
            Name = "Blink",
            Kind = "functionBlock",
            Vars =
            {
                new IrVariable { Id = "pou/Blink/EnableIn", Name = "EnableIn", Section = "input", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = "pou/Blink/Rate", Name = "Rate", Section = "input", Type = IrTypeRef.Named("DINT") },
                new IrVariable { Id = "pou/Blink/Out", Name = "Out", Section = "output", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = "pou/Blink/Buf", Name = "Buf", Section = "inOut", Type = IrTypeRef.Array(IrTypeRef.Named("DINT"), new[] { 4 }) },
                new IrVariable { Id = "pou/Blink/T1", Name = "T1", Section = "local", Type = IrTypeRef.Named("TIMER") },
            },
            Body = new IrBody
            {
                Language = "st",
                Statements =
                {
                    new IrFbCall
                    {
                        Instance = IrRef.Variable("pou/Blink/T1", IrTypeRef.Named("TIMER")),
                        Fb = "TON",
                        Profile = "logix",
                        Inputs = { ["IN"] = new IrRef { Root = IrRefRoot.This, Path = { new IrMemberSegment { Name = "EnableIn" } }, Type = IrTypeRef.Named("BOOL") }, ["PT"] = IrRef.Variable("pou/Blink/Rate", IrTypeRef.Named("DINT")) },
                        Outputs = { ["Q"] = IrRef.Variable("pou/Blink/Out", IrTypeRef.Named("BOOL")) },
                    },
                },
            },
            Actions = { new IrAction { Name = "EnableInFalse", Hook = "enableFalse", Body = new IrBody { Language = "st", Statements = { new IrAssign { Target = IrRef.Variable("pou/Blink/Out", IrTypeRef.Named("BOOL")), Value = IrLiteral.False } } } } },
            Methods =
            {
                new IrMethod
                {
                    Name = "Reset",
                    Access = "public",
                    ReturnType = IrTypeRef.Named("BOOL"),
                    Vars = { new IrVariable { Id = "pou/Blink/Reset/Force", Name = "Force", Section = "input", Type = IrTypeRef.Named("BOOL") } },
                    Body = new IrBody { Language = "st", Statements = { new IrAssign { Target = IrRef.Variable("pou/Blink/T1", IrTypeRef.Named("TIMER")).Member("ACC"), Value = IrLiteral.OfInt(0) } } },
                },
            },
            Properties = { new IrProperty { Name = "Active", Type = IrTypeRef.Named("BOOL"), Get = new IrBody { Language = "st", Statements = { new IrReturn() } } } },
            Implements = new List<string> { "IBlinker" },
        });
        doc.Pous.Add(new IrPou { Name = "IBlinker", Kind = "interface", Methods = { new IrMethod { Name = "Reset", ReturnType = IrTypeRef.Named("BOOL") } } });
        doc.Pous.Add(new IrPou { Name = "Fn", Kind = "function", ReturnType = IrTypeRef.Named("DINT"), Vars = { new IrVariable { Id = "pou/Fn/x", Name = "x", Section = "input", Type = IrTypeRef.Named("DINT") } }, Body = new IrBody { Language = "st", Statements = { new IrAssign { Target = new IrRef { Root = IrRefRoot.Unresolved("Fn") }, Value = IrRef.Variable("pou/Fn/x", IrTypeRef.Named("DINT")) } } } });
        doc.Pous.Add(new IrPou { Name = "LibFb", Kind = "functionBlock", Extends = "Blink", External = new IrExternal { Library = "Tc2_Standard", Version = "3.3" } });
        doc.Pous.Add(new IrPou { Name = "Enc", Kind = "program", Body = new IrBody { Language = "encrypted" } });

        doc.Diagnostics.Add(new IrDiagnostic { Severity = "info", Code = "IR001", Message = "sample diagnostic", Location = "P" });
        return doc;
    }

    private static IrRef V(string id, string type) => IrRef.Variable(id, IrTypeRef.Named(type));

    private static List<IrStatement> StStatements()
    {
        var bool_ = IrTypeRef.Named("BOOL");
        var dint = IrTypeRef.Named("DINT");
        return new List<IrStatement>
        {
            // IF Run AND NOT Stop THEN Idx := Idx + 1; ELSIF Count > 5 THEN Count := 0; ELSE Idx := -Idx; END_IF;
            new IrIf
            {
                Branches =
                {
                    new IrIfBranch
                    {
                        Condition = new IrBinary { Operator = "and", Left = V(Run, "BOOL"), Right = new IrUnary { Operator = "not", Operand = V(Stop, "BOOL"), Type = bool_ }, Type = bool_ },
                        Body = { new IrAssign { Target = V(Idx, "DINT"), Value = new IrBinary { Operator = "add", Left = V(Idx, "DINT"), Right = IrLiteral.OfInt(1), Type = dint }, Src = new IrSrc { Line = 0 } } },
                    },
                    new IrIfBranch
                    {
                        Condition = new IrBinary { Operator = "gt", Left = V(Count, "DINT"), Right = IrLiteral.OfInt(5), Type = bool_ },
                        Body = { new IrAssign { Target = V(Count, "DINT"), Value = IrLiteral.OfInt(0) } },
                    },
                },
                Else = new List<IrStatement> { new IrAssign { Target = V(Idx, "DINT"), Value = new IrUnary { Operator = "neg", Operand = V(Idx, "DINT"), Type = dint } } },
                Src = new IrSrc { Line = 0 },
            },
            // M1.History[Idx] := (Count + 1) * 2.5;
            new IrAssign
            {
                Target = V(M1, "REAL").Member("History").Index(V(Idx, "DINT")),
                Value = new IrBinary { Operator = "mul", Left = new IrBinary { Operator = "add", Left = V(Count, "DINT"), Right = IrLiteral.OfInt(1), Type = dint }, Right = IrLiteral.OfReal(2.5), Type = IrTypeRef.Named("REAL") },
            },
            // Flags[3] [:=] Al  (alias annotated)
            new IrAssign { Target = V(Flags, "BOOL").Index(IrLiteral.OfInt(3)), Value = new IrRef { Root = IrRefRoot.Variable(M1), Path = { new IrMemberSegment { Name = "Fault" } }, Alias = Al, Type = bool_ }, NonRetentive = true },
            // Count.5 := R_EDGE(Run, Flags[0]) XOR Ext; bit access as target, edge expression, system root
            new IrAssign
            {
                Target = new IrBitAccess { Of = V(Count, "DINT"), Bit = IrLiteral.OfInt(5), Type = bool_ },
                Value = new IrBinary { Operator = "xor", Left = new IrEdge { Kind = "rising", Clk = V(Run, "BOOL"), Storage = V(Flags, "BOOL").Index(IrLiteral.OfInt(0)), Type = bool_ }, Right = new IrRef { Root = IrRefRoot.System("S:FS"), Type = bool_ }, Type = bool_ },
            },
            // Name.LEN, Name.DATA[0], Ptr^.Speed, temp, conv, fn, sysFn, keyword, placeholder, error, literals of every kind
            new IrAssign { Target = new IrRef { Root = IrRefRoot.Temp("_rc1"), Type = bool_ }, Value = new IrBinary { Operator = "eq", Left = new IrRef { Root = IrRefRoot.Variable(Name), Path = { new IrLengthSegment() }, Type = dint }, Right = new IrConv { To = dint, Mode = "roundEven", Operand = new IrRef { Root = IrRefRoot.Variable(Ptr), Path = { new IrDerefSegment(), new IrMemberSegment { Name = "Speed" } }, Type = IrTypeRef.Named("REAL") } }, Type = bool_ } },
            new IrAssign { Target = new IrRef { Root = IrRefRoot.Variable(Name), Path = { new IrCharSegment { Index = IrLiteral.OfInt(0) } }, Type = IrTypeRef.Named("SINT") }, Value = new IrFunction { Name = "ABS", Args = { new IrSysFn { Catalog = "logix", Service = "TRN", Args = { IrLiteral.OfReal(3.7) }, Type = dint } }, Type = dint } },
            new IrAssign { Target = V("global/T/Fake", "DINT"), Value = new IrFunction { Pou = "Fn", Args = { new IrLiteral { Kind = "int", Int = 255, Radix = 16, Type = dint }, null, new IrPlaceholder { Empty = true }, new IrKeyword { Text = "Forward Only", Kind = "enum" }, new IrErrorExpression { Text = "??" } } } },
            new IrAssign { Target = V(M1, "Motor"), Value = new IrRef { Root = IrRefRoot.Unresolved("Nowhere"), Error = "unknown tag", Opaque = true, Io = true } },
            new IrAssign { Target = V("global/T/When", "DT"), Value = new IrLiteral { Kind = "dt", Text = "DT#2026-09-29-12:00:00", Type = IrTypeRef.Named("DT") } },
            new IrAssign { Target = V("global/T/Span", "TIME"), Value = new IrLiteral { Kind = "time", Text = "T#1s500ms", Int = 1_500_000_000, Type = IrTypeRef.Named("TIME") } },
            new IrAssign { Target = V("global/T/Day", "DATE"), Value = new IrLiteral { Kind = "date", Text = "D#2026-09-29", Type = IrTypeRef.Named("DATE") } },
            new IrAssign { Target = V("global/T/Tod", "TOD"), Value = new IrLiteral { Kind = "tod", Text = "TOD#12:00:00", Type = IrTypeRef.Named("TOD") } },
            new IrAssign { Target = V(Color, "Color"), Value = new IrLiteral { Kind = "enum", Text = "Green", Int = 1, Type = IrTypeRef.Named("Color") } },
            new IrAssign { Target = V(Name, "STRING"), Value = new IrLiteral { Kind = "string", String = "it's", Type = IrTypeRef.String(4) } },
            new IrAssign { Target = V("global/T/W", "WNAME"), Value = new IrLiteral { Kind = "wstring", String = "wide", Type = IrTypeRef.String(4, true) } },
            new IrAssign { Target = V(Run, "BOOL"), Value = new IrLiteral { Kind = "bool", Bool = true, Type = bool_ } },
            new IrAssign { Target = V(Count, "DINT"), Value = new IrLiteral { Kind = "int", Int = 5, Radix = 2, Type = dint } },
            // calls of every kind
            new IrFbCall { Instance = V(M1, "TIMER").Member("Timer"), Fb = "TON", Profile = "logix", Inputs = { ["IN"] = V(Run, "BOOL"), ["PT"] = IrLiteral.OfInt(500) }, Outputs = { ["Q"] = V(Stop, "BOOL") }, En = V(Run, "BOOL"), Eno = V(Flags, "BOOL").Index(IrLiteral.OfInt(1)), Mode = "prescan" },
            new IrFbCall { Instance = V(Blk, "Blink"), Pou = "Blink", Method = "Reset", Inputs = { ["Rate"] = IrLiteral.OfInt(100) } },
            new IrFnCall { Name = "CONCAT", Args = { V(Name, "STRING"), IrLiteral.Str("x") }, Outputs = new Dictionary<string, IrExpression> { ["OUT"] = V(Name, "STRING") }, En = V(Run, "BOOL") },
            new IrFnCall { Pou = "Fn", Args = { IrLiteral.OfInt(1) } },
            new IrActionCall { Action = "Sub", Inputs = { IrLiteral.OfInt(0) }, Outputs = { V(Count, "DINT") }, En = V(Run, "BOOL") },
            new IrActionCall { Action = "Main", Pou = "P" },
            new IrSysCall { Catalog = "logix", Service = "GSV", Args = { new IrKeyword { Text = "WallClockTime", Kind = "name" }, null, new IrKeyword { Text = "DateTime", Kind = "name" }, V(Count, "DINT") }, En = V(Run, "BOOL") },
            new IrCopy { Source = V(M1, "Motor"), Dest = V(M1, "Motor"), Count = IrLiteral.OfInt(1), Mode = "bytes", Atomic = true, En = V(Run, "BOOL") },
            new IrFill { Value = IrLiteral.OfInt(0), Dest = V(Flags, "BOOL"), Count = IrLiteral.OfInt(32) },
            // CASE with range and enum labels
            new IrCase
            {
                Selector = V(Idx, "DINT"),
                Arms =
                {
                    new IrCaseArm { Labels = { new IrCaseLabel { Value = IrLiteral.OfInt(1) }, new IrCaseLabel { Value = IrLiteral.OfInt(3), To = IrLiteral.OfInt(5) } }, Body = { new IrExit() } },
                    new IrCaseArm { Labels = { new IrCaseLabel { Value = new IrKeyword { Text = "Green", Kind = "enum" } } }, Body = { new IrContinue() } },
                },
                Else = new List<IrStatement> { new IrJump { Label = "Done", En = V(Stop, "BOOL") } },
            },
            new IrFor { Variable = V(Idx, "DINT"), From = IrLiteral.OfInt(0), To = IrLiteral.OfInt(3), By = IrLiteral.OfInt(1), Body = { new IrAssign { Target = V(M1, "REAL").Member("Speed"), Value = new IrBinary { Operator = "pow", Left = new IrBinary { Operator = "sub", Left = V(Count, "DINT"), Right = new IrBinary { Operator = "sub", Left = IrLiteral.OfInt(1), Right = IrLiteral.OfInt(2), Type = dint }, Type = dint }, Right = IrLiteral.OfInt(2), Type = IrTypeRef.Named("REAL") } } } },
            new IrWhile { Condition = new IrBinary { Operator = "lt", Left = V(Idx, "DINT"), Right = IrLiteral.OfInt(10), Type = bool_ }, Body = { new IrAssign { Target = V(Idx, "DINT"), Value = new IrBinary { Operator = "mod", Left = new IrBinary { Operator = "div", Left = V(Idx, "DINT"), Right = IrLiteral.OfInt(2), Type = dint }, Right = IrLiteral.OfInt(3), Type = dint } } } },
            new IrRepeat { Body = { new IrAssign { Target = V(Idx, "DINT"), Value = IrLiteral.OfInt(0) } }, Until = new IrBinary { Operator = "or", Left = new IrBinary { Operator = "ge", Left = V(Idx, "DINT"), Right = IrLiteral.OfInt(1), Type = bool_ }, Right = new IrBinary { Operator = "ne", Left = V(Count, "DINT"), Right = IrLiteral.OfInt(0), Type = bool_ }, Type = bool_ } },
            new IrLabel { Name = "Done" },
            new IrJump { Label = "Done" },
            new IrReturn { En = V(Run, "BOOL") },
            new IrErrorStatement { Text = "unparsed" },
            new IrReturn(),
        };
    }

    private static IrBody LadderBody()
    {
        var bool_ = IrTypeRef.Named("BOOL");
        var rung0 = new IrRung
        {
            Number = 0,
            Comment = "start",
            Text = "XIC(Run)XIO(Stop)[XIC(M1.Fault) ,GRT(Count,10) ]OTE(Flags[0]);",
            Elements =
            {
                new IrLdContact { Name = "XIC", Operand = V(Run, "BOOL") },
                new IrLdContact { Name = "XIO", Operand = V(Stop, "BOOL"), Negated = true },
                new IrLdBranch
                {
                    Legs =
                    {
                        new List<IrLdElement> { new IrLdContact { Name = "XIC", Operand = V(M1, "BOOL").Member("Fault") } },
                        new List<IrLdElement> { new IrLdContact { Name = "GRT", Operand = new IrBinary { Operator = "gt", Left = V(Count, "DINT"), Right = IrLiteral.OfInt(10), Type = bool_ }, Args = new List<IrExpression> { V(Count, "DINT"), IrLiteral.OfInt(10) } } },
                    },
                },
                new IrLdCoil { Name = "OTE", Operand = V(Flags, "BOOL").Index(IrLiteral.OfInt(0)), Mode = "assign" },
            },
        };
        var rung1 = new IrRung
        {
            Number = 1,
            Text = "ONS(Flags[1])TON(M1.Timer,?,?)OSR(Flags[2],Flags[3])OTL(Flags[4])OTU(Flags[5])Blink(Blk,100)JSR(Sub,0)JMP(Done)MCR();",
            Elements =
            {
                new IrLdContact { Name = "ONS", Edge = "rising", Storage = V(Flags, "BOOL").Index(IrLiteral.OfInt(1)) },
                new IrLdBlock { Name = "TON", Args = { V(M1, "TIMER").Member("Timer"), new IrPlaceholder(), new IrPlaceholder() }, Enable = "input" },
                new IrLdCoil { Name = "OSR", Operand = V(Flags, "BOOL").Index(IrLiteral.OfInt(3)), Mode = "risingPulse", Storage = V(Flags, "BOOL").Index(IrLiteral.OfInt(2)) },
                new IrLdCoil { Name = "OTL", Operand = V(Flags, "BOOL").Index(IrLiteral.OfInt(4)), Mode = "set" },
                new IrLdCoil { Name = "OTU", Operand = V(Flags, "BOOL").Index(IrLiteral.OfInt(5)), Mode = "reset" },
                new IrLdCoil { Name = "OTE", Operand = V(Flags, "BOOL").Index(IrLiteral.OfInt(6)), Mode = "negated" },
                new IrLdCoil { Name = "OSF", Operand = V(Flags, "BOOL").Index(IrLiteral.OfInt(7)), Mode = "fallingPulse", Storage = V(Flags, "BOOL").Index(IrLiteral.OfInt(8)) },
                new IrLdBlock { Name = "Blink", Args = { V(Blk, "Blink"), IrLiteral.OfInt(100) }, Enable = "input", IsPou = true, Pins = new Dictionary<string, IrExpression> { ["Rate"] = IrLiteral.OfInt(100) } },
                new IrLdBlock { Name = "JSR", Args = { new IrKeyword { Text = "Sub", Kind = "action" }, IrLiteral.OfInt(0) }, Enable = "gate", Eno = false },
                new IrLdJump { Name = "JMP", Label = "Done" },
                new IrLdMcr { Name = "MCR", Begin = true },
                new IrLdContact { Name = "AFI", Operand = IrLiteral.False },
                new IrLdMcr { Name = "MCR", Begin = false },
                new IrLdLabel { Name = "LBL", Label = "Done" },
                new IrLdReturn { Name = "RET", Outputs = new List<IrExpression> { V(Count, "DINT") } },
            },
        };
        var body = new IrBody
        {
            Language = "ld",
            Diagram = new IrLdDiagram { Rungs = { rung0, rung1 } },
            Temporaries = new List<IrTemporary> { new() { Name = "_rc1", Type = bool_ }, new() { Name = "_mcr1", Type = bool_ } },
            Statements =
            {
                new IrAssign
                {
                    Target = V(Flags, "BOOL").Index(IrLiteral.OfInt(0)),
                    Value = new IrBinary
                    {
                        Operator = "and",
                        Left = new IrBinary { Operator = "and", Left = V(Run, "BOOL"), Right = new IrUnary { Operator = "not", Operand = V(Stop, "BOOL"), Type = bool_ }, Type = bool_ },
                        Right = new IrBinary { Operator = "or", Left = V(M1, "BOOL").Member("Fault"), Right = new IrBinary { Operator = "gt", Left = V(Count, "DINT"), Right = IrLiteral.OfInt(10), Type = bool_ }, Type = bool_ },
                        Type = bool_,
                    },
                    Src = new IrSrc { Rung = 0, Element = 3 },
                },
                new IrAssign { Target = IrRef.Temp("_rc1"), Value = new IrEdge { Kind = "rising", Clk = IrLiteral.True, Storage = V(Flags, "BOOL").Index(IrLiteral.OfInt(1)), Type = bool_ }, Src = new IrSrc { Rung = 1, Element = 0 } },
                new IrFbCall { Instance = V(M1, "TIMER").Member("Timer"), Fb = "TON", Profile = "logix", Inputs = { ["IN"] = IrRef.Temp("_rc1") }, En = IrRef.Temp("_rc1"), Src = new IrSrc { Rung = 1, Element = 1 } },
                new IrIf { Branches = { new IrIfBranch { Condition = IrRef.Temp("_rc1"), Body = { new IrAssign { Target = V(Flags, "BOOL").Index(IrLiteral.OfInt(4)), Value = IrLiteral.True } } } }, Src = new IrSrc { Rung = 1, Element = 3 } },
                new IrIf { Branches = { new IrIfBranch { Condition = IrRef.Temp("_rc1"), Body = { new IrActionCall { Action = "Sub", Inputs = { IrLiteral.OfInt(0) } } } } }, Src = new IrSrc { Rung = 1, Element = 8 } },
                new IrJump { Label = "Done", En = IrRef.Temp("_rc1"), Src = new IrSrc { Rung = 1, Element = 9 } },
                new IrAssign { Target = IrRef.Temp("_mcr1"), Value = IrRef.Temp("_rc1"), Src = new IrSrc { Rung = 1, Element = 10 } },
                new IrLabel { Name = "Done", Src = new IrSrc { Rung = 1, Element = 13 } },
            },
        };
        return body;
    }

    private static IrBody NetBody()
    {
        return new IrBody
        {
            Language = "fbd",
            Diagram = new IrNetDiagram
            {
                Sheets =
                {
                    new IrNetSheet
                    {
                        Number = 1,
                        Description = "sheet 1",
                        Elements =
                        {
                            new IrNetElement { Id = 0, Kind = "input", Operand = V(Run, "BOOL"), X = 10, Y = 10 },
                            new IrNetElement { Id = 1, Kind = "block", Type = "TONR", Operand = V(M1, "TIMER").Member("Timer"), Pins = new List<string> { "TimerEnable", "DN" }, Parameters = new Dictionary<string, IrExpression> { ["PRE"] = IrLiteral.OfInt(100) } },
                            new IrNetElement { Id = 2, Kind = "output", Operand = V(Stop, "BOOL") },
                            new IrNetElement { Id = 3, Kind = "connectorOut", Name = "C1" },
                            new IrNetElement { Id = 4, Kind = "connectorIn", Name = "C1" },
                            new IrNetElement { Id = 5, Kind = "function", Type = "ADD" },
                            new IrNetElement { Id = 6, Kind = "call", Name = "Sub" },
                            new IrNetElement { Id = 7, Kind = "entry", Name = "Sub" },
                            new IrNetElement { Id = 8, Kind = "exit", Name = "Sub" },
                            new IrNetElement { Id = 9, Kind = "text", Text = "note" },
                            new IrNetElement { Id = 10, Kind = "block", Type = "Blink", IsPou = true, Operand = V(Blk, "Blink") },
                        },
                        Wires =
                        {
                            new IrNetWire { From = 0, To = 1, ToPin = "TimerEnable" },
                            new IrNetWire { From = 1, FromPin = "DN", To = 2 },
                            new IrNetWire { From = 1, FromPin = "DN", To = 1, ToPin = "Reset", Feedback = true },
                        },
                    },
                },
            },
            Statements =
            {
                new IrAssign { Target = V(M1, "BOOL").Member("Timer").Member("TimerEnable"), Value = V(Run, "BOOL"), Src = new IrSrc { Sheet = 1, Element = 1 } },
                new IrSysCall { Catalog = "logix", Service = "TONR", Args = { V(M1, "TIMER").Member("Timer") }, Src = new IrSrc { Sheet = 1, Element = 1 } },
                new IrAssign { Target = V(Stop, "BOOL"), Value = V(M1, "BOOL").Member("Timer").Member("DN"), Src = new IrSrc { Sheet = 1, Element = 2 } },
            },
        };
    }

    private static IrBody SfcBody()
    {
        var bool_ = IrTypeRef.Named("BOOL");
        return new IrBody
        {
            Language = "sfc",
            Diagram = new IrSfcDiagram
            {
                Steps =
                {
                    new IrSfcStep
                    {
                        Id = 1, Name = "Step1", Variable = Step1, Initial = true, Preset = IrLiteral.OfInt(100), LimitHigh = IrLiteral.OfInt(1000), LimitLow = IrLiteral.OfInt(0), X = 100, Y = 50, Description = "first",
                        Actions =
                        {
                            new IrSfcAction { Id = 2, Name = "Action1", Qualifier = "N", Body = new IrBody { Language = "st", Statements = { new IrAssign { Target = V(Run, "BOOL"), Value = IrLiteral.True, NonRetentive = true } } } },
                            new IrSfcAction { Id = 3, Name = "Action2", Qualifier = "L", Boolean = true, Preset = IrLiteral.OfInt(50) },
                        },
                    },
                    new IrSfcStep { Id = 4, Name = "Step2" },
                },
                Transitions = { new IrSfcTransition { Id = 5, Name = "Tran1", Condition = new IrBinary { Operator = "eq", Left = V(Count, "DINT"), Right = IrLiteral.OfInt(1), Type = bool_ }, Force = false, X = 100, Y = 100 } },
                Branches = { new IrSfcBranch { Id = 6, Type = "selection", Flow = "diverge", Priority = "UserDefined", Legs = { 7, 8 } }, new IrSfcBranch { Id = 9, Type = "simultaneous", Flow = "converge", Legs = { 7, 8 } } },
                Stops = { new IrSfcStop { Id = 10, Name = "Stop1" } },
                Links = { new IrSfcLink { From = 1, To = 5 }, new IrSfcLink { From = 5, To = 4 } },
            },
        };
    }
}
