using System.Text;
using L5x.Diagnostics;
using L5x.Model;
using L5x.Parsing;
using Plc.Ir.Logix.Build;
using Plc.Ir.Printing;
using Plc.Ir.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Plc.Ir.Logix.Tests;

/// <summary>
/// Conformance suite: every crafted fixture under tests/Conformance/fixtures/logix parses clean, builds a plc-ir
/// document that validates with zero errors, round-trips through JSON byte-identically, and matches the committed
/// golden JSON and ST print. Set PLC_IR_UPDATE_GOLDEN=1 to regenerate the goldens.
/// </summary>
public class ConformanceTests
{
    private readonly ITestOutputHelper _out;

    public ConformanceTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var f in Directory.GetFiles(FixtureDir, "*.L5X").OrderBy(f => f, StringComparer.Ordinal))
            yield return new object[] { Path.GetFileNameWithoutExtension(f) };
    }

    private static string ConformanceDir => Path.Combine(AppContext.BaseDirectory, "Conformance");
    private static string FixtureDir => Path.Combine(ConformanceDir, "fixtures", "logix");
    private static string GoldenDir => Path.Combine(ConformanceDir, "golden", "logix");
    private static bool Update => Environment.GetEnvironmentVariable("PLC_IR_UPDATE_GOLDEN") == "1";

    /// <summary>The repository tests/Conformance folder (walking up from the test assembly), for writing goldens.</summary>
    private static string RepoConformanceDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "tests", "Conformance");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("tests/Conformance not found above " + AppContext.BaseDirectory);
    }

    public sealed record Built(L5xProject Project, IrDocument Ir, string Json, string St);

    private static Built BuildFixture(string name)
    {
        var project = L5xParser.ParseFile(Path.Combine(FixtureDir, name + ".L5X"));
        var problems = project.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info).ToList();
        Assert.True(problems.Count == 0, name + ": " + string.Join("\n", problems));
        var ir = new LogixIrBuilder(project, new LogixBuildOptions { IncludeValues = true }).Build();
        ir.Source.GeneratedAt = DateTimeOffset.UnixEpoch;
        ir.Source.Generator = "conformance";
        var json = IrJson.Serialize(ir, indented: true);
        var printer = new StPrinter(ir);
        var st = new StringBuilder();
        foreach (var pou in ir.Pous) st.Append(printer.PrintPou(pou)).Append('\n');
        return new Built(project, ir, json, st.ToString().Replace("\r\n", "\n"));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureBuildsValidatesAndMatchesGolden(string name)
    {
        var built = BuildFixture(name);
        var ir = built.Ir;

        var validation = new IrValidator().Validate(ir, new ValidationOptions { ServiceCatalogs = { LogixServiceCatalog.Instance } });
        foreach (var issue in validation.Issues.Take(30)) _out.WriteLine("  " + issue);
        Assert.False(validation.HasErrors, string.Join("\n", validation.Errors.Take(20)));

        var unresolved = IrWalker.Expressions(ir).Where(x => x.Expression is IrRef { Root.Kind: "unresolved" } or IrRef { Error: not null }).Select(x => $"{x.Context.Location}: {new StPrinter(ir).Print(x.Expression)}").ToList();
        Assert.True(unresolved.Count == 0, string.Join("\n", unresolved));

        var back = IrJson.Deserialize(built.Json);
        Assert.Equal(built.Json, IrJson.Serialize(back, indented: true));

        var jsonPath = Path.Combine(GoldenDir, name + ".ir.json");
        var stPath = Path.Combine(GoldenDir, name + ".st");
        if (Update)
        {
            var repo = Path.Combine(RepoConformanceDir(), "golden", "logix");
            Directory.CreateDirectory(repo);
            File.WriteAllText(Path.Combine(repo, name + ".ir.json"), built.Json.Replace("\r\n", "\n"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(repo, name + ".st"), built.St, new UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(jsonPath), $"golden {jsonPath} missing; run with PLC_IR_UPDATE_GOLDEN=1");
        Assert.Equal(Normalize(File.ReadAllText(jsonPath)), Normalize(built.Json));
        Assert.Equal(Normalize(File.ReadAllText(stPath)), Normalize(built.St));
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    // ------------------------------------------------------------------ targeted assertions per fixture

    private static List<IrStatement> Rung(IrBody body, int n) => body.Statements.Where(s => s.Src?.Rung == n).ToList();

    [Fact]
    public void Types()
    {
        var ir = BuildFixture("types").Ir;
        var inner = ir.FindType("Inner")!;
        var bit1 = inner.Members!.Single(m => m.Name == "Bit1");
        Assert.True(bit1.Type.Is("BOOL"));
        Assert.Equal("ZZZZZZZZZZInner0", bit1.Logix()!.BitHost);
        Assert.Equal(1, bit1.Logix()!.BitNumber);
        Assert.Equal("second bit", bit1.Description);
        Assert.DoesNotContain(inner.Members!, m => m.Name.StartsWith("ZZZZ"));
        Assert.Single(inner.Logix()!.HiddenMembers!);
        var flags = inner.Members!.Single(m => m.Name == "Flags");
        Assert.True(flags.Type.IsArray);
        Assert.Equal(31, flags.Type.Dims![0].Upper);
        Assert.True(flags.Logix()!.Packed);

        var s20 = ir.FindType("STRING20")!;
        Assert.Equal("string", s20.Kind);
        Assert.Equal(20, s20.Length);
        Assert.Equal("user", s20.Class);
        Assert.Equal(82, ir.FindType("STRING")!.Length);
        Assert.Equal("vendor", ir.FindType("MESSAGE")!.Class);
        Assert.Contains("CTU", ir.FindType("COUNTER")!.StdFb!.Fbs);

        var matrix = ir.FindVariable("global/Types/Matrix")!;
        Assert.Equal(2, matrix.Type.Dims!.Count);
        Assert.Equal(1, matrix.Type.Dims[0].Upper);
        Assert.Equal(2, matrix.Type.Dims[1].Upper);
        var value = Assert.IsType<IrArrayValue>(matrix.Initial);
        Assert.Equal(new[] { 2, 3 }, value.Dims);
        Assert.Equal(6, value.Elements.Count);
        Assert.Equal(42, Assert.IsType<IrScalarValue>(ir.FindVariable("global/Types/Word")!.Initial).Int);
        Assert.True(ir.FindVariable("global/Types/Word")!.Constant);
        Assert.Equal("Hi", Assert.IsType<IrStringValue>(ir.FindVariable("global/Types/Label")!.Initial).Text);

        var o = ir.FindVariable("global/Types/O")!;
        Assert.Equal("number of items", o.Comments![".COUNT"]);
        Assert.Equal("main structure", o.Description);

        Assert.True(ir.FindVariable("global/Types/AlMember")!.Type.Is("REAL"));
        Assert.True(ir.FindVariable("global/Types/AlBit")!.Type.Is("BOOL"));
        Assert.True(ir.FindVariable("global/Types/AlWordBit")!.Type.Is("BOOL"));
        Assert.True(ir.FindVariable("global/Types/AlElem")!.Type.Is("DINT"));
        Assert.True(ir.FindVariable("global/Types/AlFlag")!.Type.Is("BOOL"));
        Assert.True(ir.FindVariable("global/Types/AlItem")!.Type.Is("Inner"));
        Assert.Equal("global/Types/Matrix", ir.FindVariable("global/Types/AlElem")!.Alias!.Target!.Root.Id);

        // Every alias use in logic is expanded and annotated.
        var refs = IrWalker.Expressions(ir).Select(x => x.Expression).OfType<IrRef>().Where(r => r.Alias is not null).ToList();
        Assert.Contains(refs, r => r.Alias == "global/Types/AlItem" && r.Root.Id == "global/Types/O");
        var len = IrWalker.Expressions(ir).Select(x => x.Expression).OfType<IrRef>().Single(r => r.Path.Count > 0 && r.Path[^1] is IrLengthSegment);
        Assert.Equal("Short", Assert.IsType<IrMemberSegment>(len.Path[0]).Name);
    }

    [Fact]
    public void Ladder()
    {
        var ir = BuildFixture("ladder").Ir;
        var main = ir.FindPou("P")!.FindAction("Main")!.Body;
        Assert.Equal("ld", main.Language);
        var ld = Assert.IsType<IrLdDiagram>(main.Diagram);
        Assert.Equal(19, ld.Rungs.Count);

        // Rung 1: ONS edge feeds the OSR / OSF coils.
        Assert.Contains(Rung(main, 1), s => s is IrAssign { Value: IrEdge { Kind: "falling" } });
        // Rung 2: every comparison contact, LIM tree and MEQ mask compare feed one OTE.
        var r2 = Assert.IsType<IrAssign>(Assert.Single(Rung(main, 2)));
        var lim = IrWalker.Expressions(ir).Select(x => x.Expression).OfType<IrLdContact>().ToList();
        var contacts = ld.Rungs[2].Elements.OfType<IrLdContact>().ToList();
        Assert.Equal(9, contacts.Count);
        Assert.Equal("or", Assert.IsType<IrBinary>(contacts.Single(c => c.Name == "LIM").Operand).Operator);
        Assert.Equal("eq", Assert.IsType<IrBinary>(contacts.Single(c => c.Name == "MEQ").Operand).Operator);
        // Rung 3: five standard blocks under the logix profile, RES on TIMER and COUNTER.
        var r3 = Rung(main, 3);
        Assert.Equal(new[] { "TON", "TOF", "TONR", "CTU", "CTD" }, r3.OfType<IrFbCall>().Select(c => c.Fb));
        Assert.All(r3.OfType<IrFbCall>(), c => Assert.Equal(Logix.Id, c.Profile));
        var resCounter = r3.OfType<IrIf>().Last();
        Assert.Equal(6, resCounter.Branches[0].Body.Count);
        // Rung 4: arithmetic to assignments; DIV of DINTs stored into DINT needs no conversion, ABS on REAL is a function.
        var r4 = Rung(main, 4);
        Assert.Contains(r4, s => s is IrIf { Branches: [{ Body: [IrAssign { Value: IrFunction { Name: "SQRT" } }] }] });
        Assert.Contains(r4, s => s is IrIf { Branches: [{ Body: [IrAssign { Value: IrConv { Mode: "roundEven" } }] }] }); // CPT with a REAL term into DINT
        // Rung 5: copies and fill.
        var r5 = Rung(main, 5).OfType<IrIf>().Select(i => i.Branches[0].Body[0]).ToList();
        Assert.IsType<IrCopy>(r5[0]);
        Assert.True(Assert.IsType<IrCopy>(r5[1]).Atomic);
        Assert.IsType<IrFill>(r5[2]);
        // Rung 6: JSR bound to the callee's SBR / RET operands.
        var jsr = Assert.IsType<IrIf>(Assert.Single(Rung(main, 6))).Branches[0].Body;
        Assert.Equal("pou/P/InA", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(jsr[0]).Target).Root.Id);
        Assert.Equal("pou/P/InB", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(jsr[1]).Target).Root.Id);
        Assert.Equal("Sub", Assert.IsType<IrActionCall>(jsr[2]).Action);
        Assert.Equal("global/Ladder/Z", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(jsr[3]).Target).Root.Id);
        // Rung 8: nested branches.
        var nested = Assert.IsType<IrLdBranch>(ld.Rungs[8].Elements[1]);
        Assert.Equal(3, nested.Legs.Count);
        Assert.IsType<IrLdBranch>(nested.Legs[1][1]);
        // Rungs 9-11: MCR zone stored in a temporary, rung 10 conditions ANDed with it.
        Assert.Contains(main.Temporaries!, t => t.Name == "_mcr1");
        var r10 = Rung(main, 10);
        var ote = Assert.IsType<IrAssign>(r10.First(s => s is IrAssign a && a.Target is IrRef { Root.Id: "global/Ladder/Out1" }));
        // OTE and TON share the zone condition, so it is stored once: _rcN := _mcr1 AND B.
        var shared = Assert.IsType<IrRef>(ote.Value);
        Assert.Equal("temp", shared.Root.Kind);
        var sharedAssign = Assert.IsType<IrAssign>(r10.Single(s => s is IrAssign { Target: IrRef t } && t.Root.Kind == "temp" && t.Root.Name == shared.Root.Name));
        Assert.Equal("_mcr1", Assert.IsType<IrRef>(Assert.IsType<IrBinary>(sharedAssign.Value).Left).Root.Name);
        Assert.True(Assert.IsType<IrLdMcr>(ld.Rungs[9].Elements[2]).Begin);
        Assert.False(Assert.IsType<IrLdMcr>(ld.Rungs[11].Elements[0]).Begin);
        // Rung 12: AFI kills the OTE; NOP is passive.
        Assert.True(Assert.IsType<IrLiteral>(Assert.IsType<IrAssign>(Assert.Single(Rung(main, 12))).Value).IsFalse);
        Assert.Equal("none", Assert.IsType<IrLdBlock>(ld.Rungs[12].Elements[2]).Enable);
        // Rung 13: services. GSV/SSV run only when the rung is true; MSG reads the rung condition every scan (it clears .EN when false).
        var gated = Rung(main, 13).OfType<IrIf>().Select(i => Assert.IsType<IrSysCall>(i.Branches[0].Body[0]).Service).ToList();
        Assert.Equal(new[] { "GSV", "SSV" }, gated);
        var msg = Assert.Single(Rung(main, 13).OfType<IrSysCall>());
        Assert.Equal("MSG", msg.Service);
        Assert.NotNull(msg.En);
        // Rung 14: add-on with EN.
        var blink = Assert.IsType<IrFbCall>(Assert.Single(Rung(main, 14)));
        Assert.Equal("Blink", blink.Pou);
        Assert.NotNull(blink.En);
        // Rung 15-16: bit field, masked move, swap bytes, strings as IEC functions.
        var fns = Rung(main, 15).Concat(Rung(main, 16)).OfType<IrIf>().Select(i => Assert.IsType<IrFunction>(Assert.IsType<IrAssign>(i.Branches[0].Body[0]).Value).Name).ToList();
        Assert.Equal(new[] { "BIT_FIELD_DISTRIBUTE", "MASKED_MOVE", "SWAP_BYTES", "CONCAT", "MID", "FIND" }, fns);
        // Rung 17: FOR loop over an action; rung 18: TND.
        var loop = Assert.IsType<IrFor>(Assert.IsType<IrIf>(Assert.Single(Rung(main, 17))).Branches[0].Body[0]);
        Assert.Equal("Loop", Assert.IsType<IrActionCall>(loop.Body[0]).Action);
        Assert.IsType<IrReturn>(Assert.IsType<IrIf>(Assert.Single(Rung(main, 18))).Branches[0].Body[0]);
        // Sub: SBR is passive, RET returns.
        var sub = ir.FindPou("P")!.FindAction("Sub")!.Body;
        Assert.Equal(2, sub.Statements.Count);
        Assert.IsType<IrReturn>(sub.Statements[1]);
        // Prescan hook synthesized.
        var init = ir.FindPou("P")!.Actions.Single(a => a.Hook == "init");
        Assert.Contains(init.Body.Statements, s => s is IrFbCall { Fb: "TON", Mode: "prescan" });
        Assert.Contains(init.Body.Statements, s => s is IrFbCall { Pou: "Blink", Mode: "prescan" });
        Assert.DoesNotContain(init.Body.Statements, s => s is IrSysCall { Service: "GSV" });
    }

    [Fact]
    public void StructuredText()
    {
        var ir = BuildFixture("st").Ir;
        var s = ir.FindPou("P")!.FindAction("Main")!.Body.Statements;
        var d = Assert.IsType<IrAssign>(s[0]);
        Assert.Equal("roundEven", Assert.IsType<IrConv>(d.Value).Mode);
        var sint = Assert.IsType<IrAssign>(s[1]);
        var wrap = Assert.IsType<IrConv>(sint.Value);
        Assert.Equal("wrap", wrap.Mode);
        Assert.True(wrap.To.Is("SINT"));
        Assert.Equal("roundEven", Assert.IsType<IrConv>(Assert.IsType<IrAssign>(s[2]).Value).Mode); // LREAL -> REAL
        Assert.IsNotType<IrConv>(Assert.IsType<IrAssign>(s[3]).Value); // DINT -> LREAL widens
        var ifs = Assert.IsType<IrIf>(s[4]);
        Assert.Equal(2, ifs.Branches.Count);
        Assert.NotNull(ifs.Else);
        var cs = Assert.IsType<IrCase>(s[5]);
        Assert.Equal(2, cs.Arms.Count);
        Assert.Equal(2, cs.Arms[1].Labels.Count);
        Assert.Equal(5, Assert.IsType<IrLiteral>(cs.Arms[1].Labels[0].To).Int);
        Assert.IsType<IrBitAccess>(Assert.IsType<IrAssign>(cs.Else![0]).Value);
        var f = Assert.IsType<IrFor>(s[6]);
        Assert.NotNull(f.By);
        Assert.IsType<IrExit>(Assert.IsType<IrIf>(f.Body[1]).Branches[0].Body[0]);
        Assert.IsType<IrWhile>(s[7]);
        Assert.IsType<IrRepeat>(s[8]);
        var math = Assert.IsType<IrBinary>(Assert.IsType<IrAssign>(s[9]).Value);
        Assert.Equal("SIN", Assert.IsType<IrFunction>(math.Right).Name);
        Assert.Equal("TRUNC", Assert.IsType<IrFunction>(Assert.IsType<IrAssign>(s[10]).Value).Name);
        var dyn = Assert.IsType<IrBitAccess>(Assert.IsType<IrAssign>(s[11]).Target);
        Assert.Equal("global/St/I", Assert.IsType<IrRef>(dyn.Bit).Root.Id);
        Assert.IsType<IrLengthSegment>(Assert.IsType<IrRef>(Assert.IsType<IrAssign>(s[13]).Value).Path[0]);
        Assert.IsType<IrCharSegment>(Assert.IsType<IrRef>(Assert.IsType<IrAssign>(s[14]).Target).Path[0]);
        var jsr = Assert.IsType<IrIf>(s[16]).Branches[0].Body;
        Assert.Equal("pou/P/InA", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(jsr[0]).Target).Root.Id);
        Assert.IsType<IrActionCall>(jsr[1]);
        Assert.Equal("global/St/Word", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(jsr[2]).Target).Root.Id);
        var blink = Assert.IsType<IrFbCall>(s[17]);
        Assert.Equal("Blink", blink.Pou);
        Assert.Equal("global/St/Flag", Assert.IsType<IrRef>(blink.Outputs["Out"]).Root.Id);
        Assert.NotEmpty(ir.FindPou("P")!.FindAction("Main")!.Body.Comments!);
        // TONR inside the add-on is a Logix FBD-style service.
        Assert.Contains(ir.FindPou("Blink")!.Body!.Statements, st => st is IrSysCall { Service: "TONR" });
    }

    [Fact]
    public void Sfc()
    {
        var ir = BuildFixture("sfc").Ir;
        var body = ir.FindPou("P")!.FindAction("Main")!.Body;
        Assert.Equal("sfc", body.Language);
        Assert.Empty(body.Statements);
        var sfc = Assert.IsType<IrSfcDiagram>(body.Diagram);
        Assert.Equal(5, sfc.Steps.Count);
        Assert.Equal(4, sfc.Transitions.Count);
        Assert.Equal(3, sfc.Branches.Count);
        Assert.Single(sfc.Stops);
        Assert.Equal(13, sfc.Links.Count);
        var qualifiers = sfc.Steps.SelectMany(s => s.Actions).Select(a => a.Qualifier).ToList();
        Assert.Equal(new[] { "N", "S", "R", "L", "D", "P", "P1", "P0", "N" }, qualifiers);
        var initial = sfc.Steps.Single(s => s.Initial);
        Assert.Equal("pou/P/Step_000", initial.Variable);
        Assert.Equal("mul", Assert.IsType<IrBinary>(initial.Preset).Operator);
        var nonRetentive = Assert.IsType<IrAssign>(initial.Actions[0].Body!.Statements[0]);
        Assert.True(nonRetentive.NonRetentive);
        Assert.True(initial.Actions[1].Boolean);
        Assert.Null(initial.Actions[1].Body);
        Assert.NotNull(sfc.Steps[1].Actions[1].Preset);
        Assert.Equal("pou/P/Tran_001", sfc.Transitions[1].Variable);
        Assert.True(sfc.Transitions[0].Condition!.IsBool);
        Assert.Equal("simultaneous", sfc.Branches[0].Type);
        Assert.Equal("converge", sfc.Branches[2].Flow);
        Assert.Equal("pou/P/Stop_000", sfc.Stops[0].Variable);
    }

    [Fact]
    public void Fbd()
    {
        var ir = BuildFixture("fbd").Ir;
        var body = ir.FindPou("P")!.FindAction("Main")!.Body;
        Assert.Equal("fbd", body.Language);
        var net = Assert.IsType<IrNetDiagram>(body.Diagram);
        var sheet = Assert.Single(net.Sheets);
        Assert.Equal("Sheet one", sheet.Description);
        Assert.Contains(sheet.Elements, e => e.Kind == "connectorIn" && e.Name == "Sum");
        Assert.Contains(sheet.Elements, e => e.Kind == "block" && e.Type == "Blink" && e.IsPou);
        Assert.Contains(sheet.Wires, w => w.Feedback);

        var s = body.Statements;
        int Index(Func<IrStatement, bool> p) => s.FindIndex(x => p(x));
        var tonrCall = Index(x => x is IrSysCall { Service: "TONR" });
        var tonrEnable = Index(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/TONR_01", Path: [IrMemberSegment { Name: "TimerEnable" }] } });
        var tonrPre = Index(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/TONR_01", Path: [IrMemberSegment { Name: "PRE" }] } });
        var tonrReset = Index(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/TONR_01", Path: [IrMemberSegment { Name: "Reset" }] } });
        var out1 = Index(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/Out1" } });
        var blinkCall = Index(x => x is IrFbCall { Pou: "Blink" });
        var blinkRate = Index(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/Blink_01", Path: [IrMemberSegment { Name: "Rate" }] } });
        Assert.True(tonrEnable >= 0 && tonrPre >= 0 && tonrReset >= 0 && tonrCall >= 0 && out1 >= 0 && blinkCall >= 0 && blinkRate >= 0);
        Assert.True(tonrEnable < tonrCall && tonrPre < tonrCall && tonrReset < tonrCall, "block inputs are assigned before the block executes");
        Assert.True(tonrCall < out1, "the output reference is written after its source block");
        Assert.True(tonrCall < blinkCall, "TONR feeds Blink.Reset, so it executes first; the feedback wire back into TONR.Reset does not reorder");
        Assert.True(blinkRate < blinkCall);
        var blink = (IrFbCall)s[blinkCall];
        Assert.Equal("global/Fbd/Counter", Assert.IsType<IrRef>(blink.Inputs["Data"]).Root.Id);
        // Tagless ADD produces a temporary that the connector pair carries to SumOut.
        var temp = Assert.Single(body.Temporaries!);
        Assert.True(temp.Type.Is("REAL"));
        var sum = Assert.IsType<IrAssign>(s.Single(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/SumOut" } }));
        Assert.Equal("temp", Assert.IsType<IrRef>(sum.Value).Root.Kind);
        Assert.Equal("add", Assert.IsType<IrBinary>(Assert.IsType<IrAssign>(s.Single(x => x is IrAssign { Target: IrRef { Root.Kind: "temp" } })).Value).Operator);
        // JSR through a sheet: input bound to the callee SBR operand, return pin read from the RET operand.
        Assert.Contains(s, x => x is IrActionCall { Action: "Sub" });
        var ret = Assert.IsType<IrAssign>(s.Single(x => x is IrAssign { Target: IrRef { Root.Id: "global/Fbd/RetVal" } }));
        Assert.Equal("pou/P/OutA", Assert.IsType<IrRef>(ret.Value).Root.Id);
    }

    [Fact]
    public void AddOn()
    {
        var ir = BuildFixture("aoi").Ir;
        var motor = ir.FindPou("Motor")!;
        Assert.Equal("functionBlock", motor.Kind);
        Assert.Equal("Motor starter", motor.Description);
        Assert.Equal(new[] { "input", "output", "input", "input", "input", "output", "output", "inOut", "local", "local", "local" }, motor.Vars.Select(v => v.Section));
        Assert.Equal("run command", motor.FindVar("Cmd")!.Description);
        Assert.True(motor.FindVar("Cmd")!.Logix()!.Required);
        Assert.False(motor.FindVar("Delay")!.Logix()!.Required);
        Assert.True(motor.FindVar("Data")!.Type.Is("MotorData"));
        Assert.Equal("ld", motor.Body!.Language);
        Assert.Equal("postscan", motor.FindAction("Postscan")!.Hook);
        Assert.Equal("enableFalse", motor.FindAction("EnableInFalse")!.Hook);
        Assert.Null(motor.FindAction("Prescan")!.Hook);
        var init = motor.Actions.Single(a => a.Hook == "init");
        Assert.Equal("Init", init.Name);
        Assert.Contains(init.Body.Statements, s => s is IrFbCall { Fb: "TON", Mode: "prescan" });
        Assert.Contains(init.Body.Statements, s => s is IrAssign { Target: IrRef { Root.Id: "pou/Motor/Prev" }, Value: IrLiteral { IsTrue: true } });
        Assert.Equal("Prescan", Assert.IsType<IrActionCall>(init.Body.Statements[^1]).Action);
        var ext = motor.Logix()!;
        Assert.Equal("addOn", ext.Kind);
        Assert.Equal("2.1", ext.Revision);
        Assert.True(ext.ExecutePrescan && ext.ExecutePostscan && ext.ExecuteEnableInFalse);

        // Ladder call: instance, En, required inputs / outputs and the InOut argument.
        var main = ir.FindPou("P")!.FindAction("Main")!.Body;
        var call = Assert.IsType<IrFbCall>(Rung(main, 0).Single());
        Assert.Equal("global/Aoi/M1", Assert.IsType<IrRef>(call.Instance).Root.Id);
        Assert.Equal("global/Aoi/Start", Assert.IsType<IrRef>(call.En).Root.Id);
        Assert.Equal("global/Aoi/Start", Assert.IsType<IrRef>(call.Inputs["Cmd"]).Root.Id);
        Assert.Equal("global/Aoi/Spd", Assert.IsType<IrRef>(call.Inputs["Speed"]).Root.Id);
        Assert.Equal("global/Aoi/MD1", Assert.IsType<IrRef>(call.Inputs["Data"]).Root.Id);
        Assert.Equal("global/Aoi/Run1", Assert.IsType<IrRef>(call.Outputs["Running"]).Root.Id);
        Assert.DoesNotContain("Delay", call.Inputs.Keys);
        // ST call: no En; instance members addressed through the POU vars.
        var text = ir.FindPou("P")!.FindAction("Text")!.Body.Statements;
        var stCall = Assert.IsType<IrFbCall>(text[0]);
        Assert.Null(stCall.En);
        Assert.Equal("global/Aoi/MD2", Assert.IsType<IrRef>(stCall.Inputs["Data"]).Root.Id);
        var delay = Assert.IsType<IrRef>(Assert.IsType<IrAssign>(text[1]).Target);
        Assert.Equal("global/Aoi/M2", delay.Root.Id);
        Assert.True(delay.Type!.Is("DINT"));
        // Instance value of the M1 tag keeps the Logix backing layout.
        var m1 = Assert.IsType<IrStructValue>(ir.FindVariable("global/Aoi/M1")!.Initial);
        Assert.Equal("Motor", m1.Type);
        Assert.Contains(m1.Members, m => m.Name == "T1");
        Assert.Null(ir.FindType("Motor"));
    }

    [Fact]
    public void Messages()
    {
        var ir = BuildFixture("messages").Ir;
        var messages = ir.Messages().ToDictionary(m => m.Variable.Name, m => m.Message);
        Assert.Equal(7, messages.Count);
        var read = messages["Read_Remote"];
        Assert.Equal("CIP Data Table Read", read.MessageType);
        Assert.Equal("global/Messages/LocalArr", read.LocalElementVariable);
        Assert.Equal("RemoteArr[0]", read.RemoteElement);
        Assert.Equal(6, read.ConnectionPathSegments.Length);
        Assert.True(read.CacheConnections);
        var identity = messages["Get_Identity"];
        Assert.Equal(0x0e, identity.ServiceCode);
        Assert.Equal(0x01, identity.ObjectType);
        Assert.Equal(7, identity.AttributeNumber);
        Assert.Equal("global/Messages/Reply", identity.DestinationVariable);
        Assert.Null(identity.SocketService);

        var create = messages["SKT_Create"];
        Assert.Equal("createSocket", create.SocketService);
        Assert.Equal("global/Messages/MsgData", create.Socket!.Session);
        Assert.Equal("global/Messages/MsgData", create.Socket.Request!.Root.Id);
        Assert.True(create.Socket.Request.Type!.Is("SKT_Create_Source"));
        Assert.Equal("global/Messages/SKT_Create", create.Socket.Instance!.Root.Id);
        Assert.True(create.Socket.Fields["type"].Type!.Is("DINT"));
        Assert.True(create.Socket.Fields["localPort"].Type!.Is("INT"));
        Assert.Contains("localAddressOctet1", create.Socket.Fields.Keys);
        Assert.Equal("openConnection", messages["SKT_Open"].SocketService);
        Assert.True(messages["SKT_Open"].Socket!.Fields["destination"].Type!.Is("STRING"));
        Assert.Equal("writeSocket", messages["SKT_Write"].SocketService);
        Assert.Contains("buffer", messages["SKT_Write"].Socket!.Fields.Keys);
        Assert.Contains("toPort", messages["SKT_Write"].Socket!.Fields.Keys);
        Assert.Equal("readSocket", messages["SKT_Read"].SocketService);
        Assert.Contains("bufferLength", messages["SKT_Read"].Socket!.Fields.Keys);
        Assert.NotNull(messages["SKT_Read"].Socket!.Response);
        Assert.Equal("deleteSocket", messages["SKT_Delete"].SocketService);
        Assert.Equal("global/Messages/MsgData", messages["SKT_Delete"].Socket!.Session);
        Assert.Null(messages["SKT_Delete"].Socket!.Request);

        var prod = ir.FindVariable("global/Messages/ProdCount")!.Logix()!;
        Assert.Equal(2, prod.Produced!.ProduceCount);
        var cons = ir.FindVariable("global/Messages/ConsCount")!.Logix()!;
        Assert.Equal("OtherPlc", cons.Consumed!.Producer);
        Assert.Equal(20, cons.Consumed.Rpi);

        // MSG instructions become sysCalls whose argument is the message variable; the logic writes .Instance itself.
        var main = ir.FindPou("P")!.FindAction("Main")!.Body.Statements;
        var msgs = main.OfType<IrSysCall>().Where(c => c.Service == "MSG" && c.En is not null).ToList();
        Assert.Equal(7, msgs.Count);
        Assert.Contains(main.OfType<IrIf>().SelectMany(i => i.Branches[0].Body), s => s is IrAssign { Target: IrRef { Root.Id: "global/Messages/SKT_Open", Path: [IrMemberSegment { Name: "Instance" }] } });
    }

    [Fact]
    public void Modules()
    {
        var ir = BuildFixture("modules").Ir;
        Assert.Equal(5, ir.Io.Devices.Count);
        var di = ir.Io.Devices.Single(d => d.Name == "DI");
        Assert.Equal("device/Local", di.Parent);
        Assert.Equal("16 point input", di.Description);
        Assert.Equal("1756-IB16", di.Catalog);
        var diExt = di.Logix()!;
        Assert.Equal("io/DI/C", diExt.ConfigChannel);
        Assert.Equal("io/DI/I", diExt.Connections[0].InputChannel);
        Assert.Equal("io/DI/O", diExt.Connections[0].OutputChannel);
        Assert.Equal(20000, diExt.Connections[0].Rpi);
        Assert.Equal("device/ENet", ir.Io.Devices.Single(d => d.Name == "Point1").Parent);
        var turck = ir.Io.Devices.Single(d => d.Name == "Turck");
        Assert.Equal("21", turck.Logix()!.Vendor);

        var channels = ir.Io.Channels.ToDictionary(c => c.Id);
        Assert.Equal("config", channels["io/DI/C"].Direction);
        Assert.Equal("output", channels["io/DI/O"].Direction);
        Assert.Equal("global/IO/DI:I", channels["io/DI/I"].Variable);
        Assert.True(channels["io/Turck/C"].Type.Is("_0015:935700_001_B2B4CBB0:C:0"));
        Assert.Equal("config", channels["io/Point1/C"].Direction);
        Assert.DoesNotContain("io/Point1/I", channels.Keys); // rack-optimized: no own input image

        var ioType = ir.FindType("_0015:935700_001_90D03083:I:0")!;
        Assert.Equal("io", ioType.Class);
        Assert.True(ioType.Logix()!.ModuleType!.ThirdParty);
        Assert.Equal("input", ioType.Logix()!.ModuleType!.Direction);
        var image = ir.FindVariable("global/IO/DI:I")!;
        Assert.Equal("io/DI/I", image.Io);
        Assert.Equal("moduleIo", image.Logix()!.Scope);
        Assert.IsType<IrStructValue>(image.Initial);

        var s = ir.FindPou("P")!.FindAction("Main")!.Body.Statements;
        // Local:3:I.Data.4 is a bit of the DI input image (slot 3 of the Local chassis).
        var flag = Assert.IsType<IrBinary>(Assert.IsType<IrAssign>(s[0]).Value);
        var slotBit = Assert.IsType<IrBitAccess>(flag.Left);
        var slotRef = Assert.IsType<IrRef>(slotBit.Of);
        Assert.Equal("global/IO/DI:I", slotRef.Root.Id);
        Assert.True(slotRef.Io);
        Assert.Equal(4, Assert.IsType<IrLiteral>(slotBit.Bit).Int);
        // ENet:O.Data[0] is an element of the output image array member.
        var enetOut = Assert.IsType<IrRef>(Assert.IsType<IrAssign>(s[1]).Target);
        Assert.Equal("global/IO/ENet:O", enetOut.Root.Id);
        Assert.IsType<IrIndexSegment>(enetOut.Path[1]);
        Assert.True(enetOut.Type!.Is("DINT"));
        // Third-party config and input members resolve through the learned layouts.
        Assert.Equal("global/IO/Turck:C", Assert.IsType<IrRef>(Assert.IsType<IrAssign>(s[3]).Value).Root.Id);
        Assert.True(Assert.IsType<IrAssign>(s[4]).Value.Type!.Is("SINT"));
        // ENet:1:I (rack-optimized slot) resolves to the parent image; the alias to a module bit expands.
        var or = Assert.IsType<IrBinary>(Assert.IsType<IrAssign>(s[5]).Value);
        var rack = Assert.IsType<IrBitAccess>(Assert.IsType<IrBinary>(or.Left).Right);
        Assert.Equal("global/IO/ENet:I", Assert.IsType<IrRef>(rack.Of).Root.Id);
        var aliased = Assert.IsType<IrBitAccess>(or.Right);
        Assert.Equal("global/IO/DI:I", Assert.IsType<IrRef>(aliased.Of).Root.Id);
        Assert.True(ir.FindVariable("global/Modules/AlInput")!.Type.Is("BOOL"));
        Assert.Equal("global/IO/DI:I", ir.FindVariable("global/Modules/AlInput")!.Alias!.Target!.Root.Id);
        Assert.Equal("global/IO/DI:O", Assert.IsType<IrRef>(Assert.IsType<IrBitAccess>(Assert.IsType<IrAssign>(s[6]).Target).Of).Root.Id);
    }

    [Fact]
    public void Tasks()
    {
        var ir = BuildFixture("tasks").Ir;
        var resource = Assert.Single(ir.Configuration.Resources);
        Assert.Equal(3, resource.Tasks.Count);
        var main = resource.Tasks[0];
        Assert.Equal("continuous", main.Kind);
        Assert.Equal(new[] { "P1", "P2" }, main.Programs);
        Assert.Equal(10, main.Priority);
        Assert.Equal(500, main.WatchdogMs);
        var fast = resource.Tasks[1];
        Assert.Equal("periodic", fast.Kind);
        Assert.Equal(10, fast.IntervalMs);
        Assert.Equal(5, fast.Priority);
        Assert.Equal(100, fast.WatchdogMs);
        Assert.Equal("10 ms task", fast.Description);
        var ev = resource.Tasks[2];
        Assert.Equal("event", ev.Kind);
        Assert.Equal("Consumed Tag", ev.Event!.Kind);
        Assert.Equal("global/Tasks/Cons", ev.Event.Source);
        Assert.True(ev.Disabled);
        Assert.True(ev.Logix()!.DisableUpdateOutputs);

        Assert.Equal(4, resource.Programs.Count); // the folder is not an instance
        var p1 = resource.Programs.Single(p => p.Name == "P1");
        Assert.Equal("Main", p1.Task);
        Assert.True(resource.Programs.Single(p => p.Name == "P2").Disabled);
        Assert.Equal("Fast", resource.Programs.Single(p => p.Name == "P3").Task);
        Assert.Equal("Ev", resource.Programs.Single(p => p.Name == "P4").Task);

        var pou1 = ir.FindPou("P1")!;
        Assert.Equal("Fault", pou1.OnFault);
        Assert.Equal("main program", pou1.Description);
        Assert.Equal("Main", Assert.IsType<IrActionCall>(pou1.Body!.Statements[0]).Action);
        var folder = ir.FindPou("Folder")!;
        Assert.True(folder.Logix()!.Folder);
        Assert.Equal(new[] { "P3", "P4" }, folder.Logix()!.Children!);
        Assert.Equal("Folder", ir.FindPou("P3")!.Logix()!.ParentFolder);
        Assert.Empty(folder.Body!.Statements);
    }
}
