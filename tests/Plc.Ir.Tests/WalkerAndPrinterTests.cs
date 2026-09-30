using Plc.Ir.Printing;
using Xunit;
using Xunit.Abstractions;

namespace Plc.Ir.Tests;

public class WalkerAndPrinterTests
{
    private readonly ITestOutputHelper _out;

    public WalkerAndPrinterTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void WalkerEnumeratesEverything()
    {
        var doc = SampleDocument.Build();
        var expressions = IrWalker.Expressions(doc).ToList();
        var statements = IrWalker.Statements(doc).ToList();
        Assert.True(expressions.Count > 100, expressions.Count.ToString());
        Assert.True(statements.Count > 40, statements.Count.ToString());

        // Every expression op and statement kind is visited at least once.
        var ops = expressions.Select(e => e.Expression.GetType()).Distinct().ToHashSet();
        foreach (var t in new[] { typeof(IrRef), typeof(IrLiteral), typeof(IrUnary), typeof(IrBinary), typeof(IrFunction), typeof(IrConv), typeof(IrBitAccess), typeof(IrEdge), typeof(IrSysFn), typeof(IrKeyword), typeof(IrPlaceholder), typeof(IrErrorExpression) })
            Assert.Contains(t, ops);
        var kinds = statements.Select(s => s.Statement.GetType()).Distinct().ToHashSet();
        foreach (var t in new[] { typeof(IrAssign), typeof(IrFbCall), typeof(IrFnCall), typeof(IrActionCall), typeof(IrSysCall), typeof(IrCopy), typeof(IrFill), typeof(IrIf), typeof(IrCase), typeof(IrFor), typeof(IrWhile), typeof(IrRepeat), typeof(IrExit), typeof(IrContinue), typeof(IrReturn), typeof(IrJump), typeof(IrLabel), typeof(IrErrorStatement) })
            Assert.Contains(t, kinds);

        // Alias targets are visited with the variable as context.
        Assert.Contains(expressions, e => e.Context.Variable?.Id == SampleDocument.Al && e.Expression is IrRef { Root.Id: SampleDocument.M1 });
        // SFC action bodies and transition conditions are visited.
        Assert.Contains(statements, s => s.Context.Action?.Name == "Seq" && s.Statement is IrAssign { NonRetentive: true });
        Assert.Contains(expressions, e => e.Context.Action?.Name == "Seq" && e.Expression is IrBinary { Operator: "eq" });
        // Ladder element operands and nested index expressions are visited.
        Assert.Contains(expressions, e => e.Context.Action?.Name == "Main" && e.Expression is IrLiteral { Int: 10 });
        Assert.Contains(expressions, e => e.Expression is IrRef { Root.Id: SampleDocument.Idx } && e.Context.Pou?.Name == "P" && e.Context.Action is null);
        // Nested expressions inside statements of methods and properties.
        Assert.Contains(statements, s => s.Context.Method?.Name == "Reset");
        Assert.Equal("P/Main", statements.First(s => s.Context.Action?.Name == "Main").Context.Location);
    }

    [Fact]
    public void PrinterRendersStructuredText()
    {
        var doc = SampleDocument.Build();
        var printer = new StPrinter(doc);
        var text = printer.Print(doc.Pous[0].Body!);
        _out.WriteLine(text);
        Assert.Contains("IF Run AND NOT Stop THEN\n    Idx := Idx + 1;\nELSIF Count > 5 THEN\n    Count := 0;\nELSE\n    Idx := -Idx;\nEND_IF;\n", text);
        Assert.Contains("M1.History[Idx] := (Count + 1) * 2.5;", text);
        Assert.Contains("Flags[3] [:=] M1.Fault;", text);
        Assert.Contains("Count.5 := R_EDGE(Run, Flags[0]) XOR S:FS;", text);
        Assert.Contains("_rc1 := Name.LEN = TO_DINT#roundEven(Ptr^.Speed);", text);
        Assert.Contains("Name.DATA[0] := ABS(TRN(3.7));", text);
        Assert.Contains("Fake := Fn(16#FF, , ?, Forward Only, (* error: ?? *));", text);
        Assert.Contains("Name := 'it$'s';", text);
        Assert.Contains("W := \"wide\";", text);
        Assert.Contains("Run := TRUE;", text);
        Assert.Contains("Count := 2#101;", text);
        Assert.Contains("M1.Timer(EN := Run, IN := Run, PT := 500, Q => Stop, ENO => Flags[1]); (* TON/logix, mode: prescan *)", text);
        Assert.Contains("Blk.Reset(Rate := 100); (* Blink *)", text);
        Assert.Contains("(* EN := Run *) CONCAT(Name, 'x', OUT => Name);", text);
        Assert.Contains("IF Run THEN Sub(0; Count); END_IF;", text);
        Assert.Contains("P.Main();", text);
        Assert.Contains("(* EN := Run *) GSV(WallClockTime, , DateTime, Count); (* logix *)", text);
        Assert.Contains("(* EN := Run *) COPY_ATOMIC(M1, M1, 1); (* bytes *)", text);
        Assert.Contains("FILL(0, Flags, 32);", text);
        Assert.Contains("CASE Idx OF\n    1, 3..5:\n        EXIT;\n    Green:\n        CONTINUE;\n    ELSE\n        IF Stop THEN JMP Done; END_IF;\nEND_CASE;\n", text);
        Assert.Contains("FOR Idx := 0 TO 3 BY 1 DO\n    M1.Speed := (Count - (1 - 2)) ** 2;\nEND_FOR;\n", text);
        Assert.Contains("WHILE Idx < 10 DO\n    Idx := Idx / 2 MOD 3;\nEND_WHILE;\n", text);
        Assert.Contains("REPEAT\n    Idx := 0;\nUNTIL Idx >= 1 OR Count <> 0\nEND_REPEAT;\n", text);
        Assert.Contains("Done:\nJMP Done;\nIF Run THEN RETURN; END_IF;\n(* error: unparsed *)\nRETURN;\n", text);
        Assert.Contains("When := DT#2026-09-29-12:00:00;", text);
        Assert.Contains("Color := Green;", text);
    }

    [Fact]
    public void PrinterRendersLoweredLadderWithTemporariesAndPou()
    {
        var doc = SampleDocument.Build();
        var printer = new StPrinter(doc);
        var main = printer.Print(doc.Pous[0].FindAction("Main")!.Body);
        _out.WriteLine(main);
        Assert.StartsWith("VAR_TEMP\n    _rc1 : BOOL;\n    _mcr1 : BOOL;\nEND_VAR\n", main);
        Assert.Contains("Flags[0] := Run AND NOT Stop AND (M1.Fault OR Count > 10);", main);
        Assert.Contains("_rc1 := R_EDGE(TRUE, Flags[1]);", main);
        Assert.Contains("M1.Timer(EN := _rc1, IN := _rc1); (* TON/logix *)", main);
        Assert.Contains("IF _rc1 THEN JMP Done; END_IF;", main);

        var pou = printer.PrintPou(doc.FindPou("Blink")!);
        _out.WriteLine(pou);
        Assert.StartsWith("FUNCTION_BLOCK Blink IMPLEMENTS IBlinker\nVAR_INPUT\n    EnableIn : BOOL;\n    Rate : DINT;\nEND_VAR\nVAR_OUTPUT\n    Out : BOOL;\nEND_VAR\nVAR_IN_OUT\n    Buf : ARRAY[0..3] OF DINT;\nEND_VAR\nVAR\n    T1 : TIMER;\nEND_VAR\n", pou);
        Assert.Contains("T1(IN := THIS^.EnableIn, PT := Rate, Q => Out); (* TON/logix *)", pou);
        Assert.Contains("ACTION EnableInFalse (* hook: enableFalse *)\n    Out := FALSE;\nEND_ACTION\nEND_FUNCTION_BLOCK\n", pou);

        var gvlPou = printer.PrintPou(doc.Pous[0]);
        Assert.Contains("PROGRAM P\n", gvlPou);
        Assert.Contains("VAR_TEMP\n    Tmp : REAL;\nEND_VAR\n", gvlPou);

        // Without a document, ids fall back to their last segment.
        Assert.Equal("M1.Speed", new StPrinter().Print(IrRef.Variable(SampleDocument.M1).Member("Speed")));
        // Precedence: parentheses only where needed.
        Assert.Equal("a OR b AND NOT (c OR d)", new StPrinter().Print(new IrBinary
        {
            Operator = "or",
            Left = new IrRef { Root = IrRefRoot.Unresolved("a") },
            Right = new IrBinary { Operator = "and", Left = new IrRef { Root = IrRefRoot.Unresolved("b") }, Right = new IrUnary { Operator = "not", Operand = new IrBinary { Operator = "or", Left = new IrRef { Root = IrRefRoot.Unresolved("c") }, Right = new IrRef { Root = IrRefRoot.Unresolved("d") } } } },
        }));
    }
}
