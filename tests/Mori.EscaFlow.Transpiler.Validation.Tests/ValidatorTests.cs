using Mori.EscaFlow.Transpiler.Catalogs;
using Xunit;

namespace Mori.EscaFlow.Transpiler.Validation.Tests;

/// <summary>
/// One mutation, one issue code: each test starts from the single known-valid document built by <see cref="Valid"/>,
/// applies one targeted change that should make exactly one rule fire, and asserts that code is present (and, where
/// it matters, that nothing more severe than a warning was raised). Mutating a shared valid baseline rather than
/// building a fresh broken document per test keeps every test isolated to the one thing it is actually checking.
/// </summary>
public class ValidatorTests
{
    /// <summary>A small, complete and valid document: a controller with one program calling a TON (logix profile), a function block, an alias, a temp, a label and a sysCall.</summary>
    private static IrDocument Valid()
    {
        var doc = new IrDocument
        {
            Source = new IrSource { Vendor = "logix", Dialect = "l5x", Generator = "test" },
            Configuration = new IrConfiguration
            {
                Name = "T",
                Resources =
                {
                    new IrResource
                    {
                        Name = "T",
                        Tasks = { new IrTask { Name = "Main", Kind = "continuous", Programs = { "P" } } },
                        Programs = { new IrProgramInstance { Name = "P", Pou = "P", Task = "Main" } },
                    },
                },
            },
        };
        doc.Types.Add(new IrType
        {
            Name = "TIMER",
            Kind = "struct",
            Class = "vendor",
            StdFb = new IrStdFbBinding { Fbs = { "TON", "TOF", "TONR" }, Profile = "logix" },
            Members = new List<IrMember>
            {
                new() { Name = "PRE", Type = IrTypeRef.Named("DINT") },
                new() { Name = "ACC", Type = IrTypeRef.Named("DINT") },
                new() { Name = "EN", Type = IrTypeRef.Named("BOOL") },
                new() { Name = "TT", Type = IrTypeRef.Named("BOOL") },
                new() { Name = "DN", Type = IrTypeRef.Named("BOOL") },
            },
        });
        doc.Types.Add(new IrType
        {
            Name = "COUNTER",
            Kind = "struct",
            Class = "vendor",
            Members = new List<IrMember>
            {
                new() { Name = "PRE", Type = IrTypeRef.Named("DINT") },
                new() { Name = "ACC", Type = IrTypeRef.Named("DINT") },
                new() { Name = "DN", Type = IrTypeRef.Named("BOOL") },
            },
        });
        doc.Types.Add(new IrType
        {
            Name = "Motor",
            Kind = "struct",
            Class = "user",
            Members = new List<IrMember>
            {
                new() { Name = "Speed", Type = IrTypeRef.Named("REAL") },
                new() { Name = "Fault", Type = IrTypeRef.Named("BOOL") },
                new() { Name = "Timer", Type = IrTypeRef.Named("TIMER") },
            },
        });
        doc.Globals.Add(new IrGlobalVariableList
        {
            Name = "T",
            Vars =
            {
                new IrVariable { Id = "global/T/Run", Name = "Run", Section = "global", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = "global/T/Lamp", Name = "Lamp", Section = "global", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = "global/T/Count", Name = "Count", Section = "global", Type = IrTypeRef.Named("DINT") },
                new IrVariable { Id = "global/T/Word", Name = "Word", Section = "global", Type = IrTypeRef.Named("DINT") },
                new IrVariable { Id = "global/T/M1", Name = "M1", Section = "global", Type = IrTypeRef.Named("Motor") },
                new IrVariable { Id = "global/T/Motors", Name = "Motors", Section = "global", Type = IrTypeRef.Array(IrTypeRef.Named("Motor"), new[] { 4 }) },
                new IrVariable { Id = "global/T/C1", Name = "C1", Section = "global", Type = IrTypeRef.Named("COUNTER") },
                new IrVariable { Id = "global/T/Blk", Name = "Blk", Section = "global", Type = IrTypeRef.Named("Blink") },
                new IrVariable
                {
                    Id = "global/T/Al", Name = "Al", Section = "global", Type = IrTypeRef.Named("BOOL"),
                    Alias = new IrAlias { Text = "M1.Fault", Target = IrRef.Variable("global/T/M1", IrTypeRef.Named("BOOL")).Member("Fault") },
                },
            },
        });
        doc.Pous.Add(new IrPou
        {
            Name = "Blink",
            Kind = "functionBlock",
            Vars =
            {
                new IrVariable { Id = "pou/Blink/EnableIn", Name = "EnableIn", Section = "input", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = "pou/Blink/Rate", Name = "Rate", Section = "input", Type = IrTypeRef.Named("DINT") },
                new IrVariable { Id = "pou/Blink/Out", Name = "Out", Section = "output", Type = IrTypeRef.Named("BOOL") },
                new IrVariable { Id = "pou/Blink/T1", Name = "T1", Section = "local", Type = IrTypeRef.Named("TIMER") },
            },
            Body = new IrBody
            {
                Language = "st",
                Statements =
                {
                    new IrAssign
                    {
                        Target = IrRef.Variable("pou/Blink/Out", IrTypeRef.Named("BOOL")),
                        Value = IrRef.Variable("pou/Blink/T1", IrTypeRef.Named("BOOL")).Member("DN"),
                    },
                },
            },
        });
        var p = new IrPou
        {
            Name = "P",
            Kind = "program",
            Vars = { new IrVariable { Id = "pou/P/Idx", Name = "Idx", Section = "local", Type = IrTypeRef.Named("DINT") } },
            Body = new IrBody { Language = "st", Statements = { new IrActionCall { Action = "Main" } }, Synthetic = true },
        };
        var main = new IrAction
        {
            Name = "Main",
            Body = new IrBody
            {
                Language = "ld",
                Temporaries = new List<IrTemporary> { new() { Name = "_rc1", Type = IrTypeRef.Named("BOOL") } },
                Statements =
                {
                    new IrAssign
                    {
                        Target = IrRef.Temp("_rc1"),
                        Value = new IrBinary
                        {
                            Operator = "and",
                            Left = IrRef.Variable("global/T/Run", IrTypeRef.Named("BOOL")),
                            Right = new IrBinary { Operator = "gt", Left = IrRef.Variable("global/T/Count", IrTypeRef.Named("DINT")), Right = IrLiteral.OfInt(10), Type = IrTypeRef.Named("BOOL") },
                            Type = IrTypeRef.Named("BOOL"),
                        },
                        Src = new IrSrc { Rung = 0 },
                    },
                    new IrAssign { Target = IrRef.Variable("global/T/Lamp", IrTypeRef.Named("BOOL")), Value = IrRef.Temp("_rc1") },
                    new IrFbCall
                    {
                        Instance = IrRef.Variable("global/T/M1", IrTypeRef.Named("TIMER")).Member("Timer"),
                        Fb = "TON",
                        Profile = "logix",
                        Inputs = { ["IN"] = IrRef.Temp("_rc1"), ["PT"] = IrLiteral.OfInt(500) },
                        Outputs = { ["Q"] = IrRef.Variable("global/T/Lamp", IrTypeRef.Named("BOOL")) },
                    },
                    new IrFbCall
                    {
                        Instance = IrRef.Variable("global/T/Blk", IrTypeRef.Named("Blink")),
                        Pou = "Blink",
                        Inputs = { ["EnableIn"] = IrRef.Variable("global/T/Run", IrTypeRef.Named("BOOL")), ["Rate"] = IrLiteral.OfInt(100) },
                        Outputs = { ["Out"] = IrRef.Variable("global/T/Lamp", IrTypeRef.Named("BOOL")) },
                    },
                    new IrIf
                    {
                        Branches =
                        {
                            new IrIfBranch
                            {
                                Condition = IrRef.Variable("global/T/Run", IrTypeRef.Named("BOOL")),
                                Body =
                                {
                                    new IrAssign
                                    {
                                        Target = new IrBitAccess { Of = IrRef.Variable("global/T/Word", IrTypeRef.Named("DINT")), Bit = IrLiteral.OfInt(3), Type = IrTypeRef.Named("BOOL") },
                                        Value = IrLiteral.True,
                                    },
                                    new IrAssign
                                    {
                                        Target = IrRef.Variable("global/T/Motors", IrTypeRef.Named("REAL")).Index(IrRef.Variable("pou/P/Idx", IrTypeRef.Named("DINT"))).Member("Speed"),
                                        Value = new IrBinary { Operator = "mul", Left = IrRef.Variable("global/T/M1", IrTypeRef.Named("REAL")).Member("Speed"), Right = IrLiteral.OfReal(2.0), Type = IrTypeRef.Named("REAL") },
                                    },
                                    new IrJump { Label = "END" },
                                },
                            },
                        },
                    },
                    new IrSysCall { Catalog = "logix", Service = "GSV", Args = { new IrKeyword { Text = "WallClockTime", Kind = "name" }, null, new IrKeyword { Text = "DateTime", Kind = "name" }, IrRef.Variable("global/T/Word", IrTypeRef.Named("DINT")) } },
                    new IrActionCall { Action = "Sub" },
                    new IrLabel { Name = "END" },
                },
            },
        };
        p.Actions.Add(main);
        p.Actions.Add(new IrAction { Name = "Sub", Body = new IrBody { Language = "st" } });
        doc.Pous.Add(p);
        return doc;
    }

    /// <summary>A minimal service catalog registering only GSV, so <c>sysCall</c> checks (unknown service, arg count) have something real to check against without pulling in the full Logix catalog.</summary>
    private sealed class FakeCatalog : IServiceCatalog
    {
        public string Id => "logix";
        public IReadOnlyList<ServiceDefinition> Services { get; } = new[]
        {
            new ServiceDefinition
            {
                Name = "GSV", Group = "system",
                Params = { new ServiceParam { Name = "class", Role = "keyword" }, new ServiceParam { Name = "instance", Role = "any", Optional = true }, new ServiceParam { Name = "attribute", Role = "keyword" }, new ServiceParam { Name = "dest", Role = "out" } },
            },
        };
        public ServiceDefinition? Get(string name) => Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Validates with <see cref="FakeCatalog"/> registered by default; <paramref name="withCatalog"/> = false validates with no catalogs at all, for the "catalog not registered" warning test.</summary>
    private static ValidationResult Run(IrDocument doc, bool withCatalog = true)
        => new IrValidator().Validate(doc, new ValidationOptions { ServiceCatalogs = withCatalog ? new List<IServiceCatalog> { new FakeCatalog() } : new List<IServiceCatalog>() });

    /// <summary>Asserts the expected code is among the issues, including the code's own description in the failure message so a mismatch is diagnosable without cross-referencing <see cref="ValidationCodes"/> by hand.</summary>
    private static void AssertCode(ValidationResult r, string code)
        => Assert.True(r.Issues.Any(i => i.Code == code), $"expected {code} ({ValidationCodes.Describe(code)}), got:\n{string.Join("\n", r.Issues)}");

    [Fact]
    public void ValidDocumentHasNoIssues()
    {
        var r = Run(Valid());
        Assert.True(r.Issues.Count == 0, string.Join("\n", r.Issues));
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void ValidDocumentSurvivesJsonRoundTripAndStrictRead()
    {
        var json = IrJson.Serialize(Valid());
        var r = new IrValidator(new ValidationOptions { ServiceCatalogs = { new FakeCatalog() } }).ValidateJson(json);
        Assert.True(r.Issues.Count == 0, string.Join("\n", r.Issues));
    }

    [Fact]
    public void UnknownCatalogIsAWarningOnly()
    {
        var r = Run(Valid(), withCatalog: false);
        Assert.False(r.HasErrors);
        AssertCode(r, ValidationCodes.UnknownServiceCatalog);
    }

    [Fact]
    public void UnknownVariableInRef()
    {
        var doc = Valid();
        ((IrAssign)doc.Pous[1].Actions[0].Body.Statements[1]).Target = IrRef.Variable("global/T/Nope", IrTypeRef.Named("BOOL"));
        AssertCode(Run(doc), ValidationCodes.UnresolvedVariable);
    }

    [Fact]
    public void UnresolvedRootIsReported()
    {
        var doc = Valid();
        ((IrAssign)doc.Pous[1].Actions[0].Body.Statements[1]).Value = new IrRef { Root = IrRefRoot.Unresolved("Ghost") };
        AssertCode(Run(doc), ValidationCodes.UnresolvedReference);
    }

    [Fact]
    public void WrongPinOnTon()
    {
        var doc = Valid();
        ((IrFbCall)doc.Pous[1].Actions[0].Body.Statements[2]).Inputs["PV"] = IrLiteral.OfInt(1);
        AssertCode(Run(doc), ValidationCodes.UnknownPin);
    }

    [Fact]
    public void ExtraProfilePinIsAccepted()
    {
        var doc = Valid();
        ((IrFbCall)doc.Pous[1].Actions[0].Body.Statements[2]).Outputs["TT"] = IrRef.Variable("global/T/Lamp", IrTypeRef.Named("BOOL"));
        Assert.Empty(Run(doc).Issues);
    }

    [Fact]
    public void InstanceTypeMismatch()
    {
        var doc = Valid();
        ((IrFbCall)doc.Pous[1].Actions[0].Body.Statements[2]).Instance = IrRef.Variable("global/T/C1", IrTypeRef.Named("COUNTER"));
        AssertCode(Run(doc), ValidationCodes.InstanceTypeMismatch);
    }

    [Fact]
    public void FbCallNeedsExactlyOneTarget()
    {
        var doc = Valid();
        ((IrFbCall)doc.Pous[1].Actions[0].Body.Statements[2]).Pou = "Blink";
        AssertCode(Run(doc), ValidationCodes.FbCallTarget);
    }

    [Fact]
    public void PouPinDirection()
    {
        var doc = Valid();
        ((IrFbCall)doc.Pous[1].Actions[0].Body.Statements[3]).Inputs["Out"] = IrLiteral.True;
        AssertCode(Run(doc), ValidationCodes.PouPinMismatch);
    }

    [Fact]
    public void BitOnReal()
    {
        var doc = Valid();
        var ifs = (IrIf)doc.Pous[1].Actions[0].Body.Statements[4];
        ((IrBitAccess)((IrAssign)ifs.Branches[0].Body[0]).Target).Of = IrRef.Variable("global/T/M1", IrTypeRef.Named("REAL")).Member("Speed");
        AssertCode(Run(doc), ValidationCodes.BitOfNotInteger);
    }

    [Fact]
    public void IndexCountMismatch()
    {
        var doc = Valid();
        var ifs = (IrIf)doc.Pous[1].Actions[0].Body.Statements[4];
        ((IrAssign)ifs.Branches[0].Body[1]).Target = IrRef.Variable("global/T/Motors", IrTypeRef.Named("REAL")).Index(IrLiteral.OfInt(0), IrLiteral.OfInt(1)).Member("Speed");
        AssertCode(Run(doc), ValidationCodes.PathIndexCount);
    }

    [Fact]
    public void MemberNotFound()
    {
        var doc = Valid();
        var ifs = (IrIf)doc.Pous[1].Actions[0].Body.Statements[4];
        ((IrAssign)ifs.Branches[0].Body[1]).Value = IrRef.Variable("global/T/M1", IrTypeRef.Named("REAL")).Member("Torque");
        AssertCode(Run(doc), ValidationCodes.PathMemberNotFound);
    }

    [Fact]
    public void LeafTypeMismatchIsAWarning()
    {
        var doc = Valid();
        var ifs = (IrIf)doc.Pous[1].Actions[0].Body.Statements[4];
        ((IrAssign)ifs.Branches[0].Body[1]).Value = IrRef.Variable("global/T/M1", IrTypeRef.Named("REAL")).Member("Timer").Member("PRE");
        var r = Run(doc);
        Assert.False(r.HasErrors);
        AssertCode(r, ValidationCodes.RefLeafTypeMismatch);
    }

    [Fact]
    public void UnknownServiceOnRegisteredCatalog()
    {
        var doc = Valid();
        ((IrSysCall)doc.Pous[1].Actions[0].Body.Statements[5]).Service = "XYZ";
        AssertCode(Run(doc), ValidationCodes.UnknownService);
    }

    [Fact]
    public void ServiceArgCount()
    {
        var doc = Valid();
        ((IrSysCall)doc.Pous[1].Actions[0].Body.Statements[5]).Args.Add(IrLiteral.OfInt(1));
        AssertCode(Run(doc), ValidationCodes.ServiceArgCount);
    }

    [Fact]
    public void InvalidEnumValue()
    {
        var doc = Valid();
        doc.Pous[1].Vars[0].Section = "bogus";
        AssertCode(Run(doc), ValidationCodes.InvalidEnumValue);
    }

    [Fact]
    public void MissingTypeName()
    {
        var doc = Valid();
        doc.Globals[0].Vars[2].Type = IrTypeRef.Named("Nowhere");
        AssertCode(Run(doc), ValidationCodes.UnresolvedType);
    }

    [Fact]
    public void JumpToUndefinedLabel()
    {
        var doc = Valid();
        ((IrLabel)doc.Pous[1].Actions[0].Body.Statements[7]).Name = "ELSEWHERE";
        AssertCode(Run(doc), ValidationCodes.UndefinedLabel);
    }

    [Fact]
    public void AliasCycle()
    {
        var doc = Valid();
        var al = doc.Globals[0].Vars.Single(v => v.Name == "Al");
        al.Alias!.Target = IrRef.Variable("global/T/Al2", IrTypeRef.Named("BOOL"));
        doc.Globals[0].Vars.Add(new IrVariable
        {
            Id = "global/T/Al2", Name = "Al2", Section = "global", Type = IrTypeRef.Named("BOOL"),
            Alias = new IrAlias { Text = "Al", Target = IrRef.Variable("global/T/Al", IrTypeRef.Named("BOOL")) },
        });
        AssertCode(Run(doc), ValidationCodes.AliasTarget);
    }

    [Fact]
    public void TaskProgramInconsistency()
    {
        var doc = Valid();
        doc.Configuration.Resources[0].Tasks[0].Programs.Clear();
        AssertCode(Run(doc), ValidationCodes.TaskProgramInconsistent);
    }

    [Fact]
    public void TemporaryNotDeclared()
    {
        var doc = Valid();
        doc.Pous[1].Actions[0].Body.Temporaries = null;
        AssertCode(Run(doc), ValidationCodes.UndeclaredTemporary);
    }

    [Fact]
    public void UnresolvedActionCall()
    {
        var doc = Valid();
        ((IrActionCall)doc.Pous[1].Actions[0].Body.Statements[6]).Action = "Missing";
        AssertCode(Run(doc), ValidationCodes.UnresolvedAction);
    }

    [Fact]
    public void MalformedVariableId()
    {
        var doc = Valid();
        doc.Pous[1].Vars[0].Id = "pou/Q/Idx";
        AssertCode(Run(doc), ValidationCodes.MalformedId);
    }

    [Fact]
    public void DuplicateIds()
    {
        var doc = Valid();
        doc.Globals[0].Vars.Add(new IrVariable { Id = "global/T/Run", Name = "Run", Section = "global", Type = IrTypeRef.Named("BOOL") });
        AssertCode(Run(doc), ValidationCodes.DuplicateId);
    }

    [Fact]
    public void AssignTypeMismatch()
    {
        var doc = Valid();
        ((IrAssign)doc.Pous[1].Actions[0].Body.Statements[1]).Value = IrLiteral.Str("abc");
        AssertCode(Run(doc), ValidationCodes.AssignTypeMismatch);
    }

    [Fact]
    public void ConditionNotBool()
    {
        var doc = Valid();
        ((IrIf)doc.Pous[1].Actions[0].Body.Statements[4]).Branches[0].Condition = IrRef.Variable("global/T/Count", IrTypeRef.Named("DINT"));
        AssertCode(Run(doc), ValidationCodes.ConditionNotBool);
    }

    [Fact]
    public void EdgePinsMustBeBool()
    {
        var doc = Valid();
        ((IrAssign)doc.Pous[1].Actions[0].Body.Statements[1]).Value = new IrEdge { Kind = "rising", Clk = IrRef.Variable("global/T/Run", IrTypeRef.Named("BOOL")), Storage = IrRef.Variable("global/T/Count", IrTypeRef.Named("DINT")), Type = IrTypeRef.Named("BOOL") };
        AssertCode(Run(doc), ValidationCodes.EdgePinNotBool);
    }

    [Fact]
    public void StrictJsonReadReportsUnknownMember()
    {
        var json = IrJson.Serialize(Valid()).Replace("\"format\": \"plc-ir\"", "\"format\": \"plc-ir\", \"bogus\": 1");
        var r = new IrValidator().ValidateJson(json);
        AssertCode(r, ValidationCodes.JsonRead);
    }

    [Fact]
    public void WrongMajorVersionIsReported()
    {
        var r = new IrValidator().ValidateJson("{\"format\":\"plc-ir\",\"irVersion\":\"9.0\"}");
        AssertCode(r, ValidationCodes.FormatVersion);
    }

    [Fact]
    public void ResultConvertsToDiagnostics()
    {
        var doc = Valid();
        doc.Pous[1].Vars[0].Section = "bogus";
        var d = Run(doc).ToDiagnostics().Single();
        Assert.Equal("error", d.Severity);
        Assert.Equal(ValidationCodes.InvalidEnumValue, d.Code);
    }
}
