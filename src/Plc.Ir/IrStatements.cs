using System.Text.Json.Serialization;

namespace Plc.Ir;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrAssign), "assign")]
[JsonDerivedType(typeof(IrFbCall), "fbCall")]
[JsonDerivedType(typeof(IrFnCall), "fnCall")]
[JsonDerivedType(typeof(IrActionCall), "actionCall")]
[JsonDerivedType(typeof(IrSysCall), "sysCall")]
[JsonDerivedType(typeof(IrCopy), "copy")]
[JsonDerivedType(typeof(IrFill), "fill")]
[JsonDerivedType(typeof(IrIf), "if")]
[JsonDerivedType(typeof(IrCase), "case")]
[JsonDerivedType(typeof(IrFor), "for")]
[JsonDerivedType(typeof(IrWhile), "while")]
[JsonDerivedType(typeof(IrRepeat), "repeat")]
[JsonDerivedType(typeof(IrExit), "exit")]
[JsonDerivedType(typeof(IrContinue), "continue")]
[JsonDerivedType(typeof(IrReturn), "return")]
[JsonDerivedType(typeof(IrJump), "jump")]
[JsonDerivedType(typeof(IrLabel), "label")]
[JsonDerivedType(typeof(IrErrorStatement), "error")]
public abstract class IrStatement : IrNode
{
}

/// <summary><c>target := value</c>. The target is a <c>ref</c> or a <c>bit</c> expression.</summary>
public sealed class IrAssign : IrStatement
{
    public IrExpression Target { get; set; } = new IrRef();
    public IrExpression Value { get; set; } = IrLiteral.False;

    /// <summary>SFC non-retentive assignment (Logix <c>[:=]</c>): reset when the action is deactivated.</summary>
    public bool NonRetentive { get; set; }
}

/// <summary>
/// Invocation of a function block instance: a standard block (<see cref="Fb"/> with <see cref="Profile"/>) or a
/// project POU (<see cref="Pou"/>). Inputs and outputs are keyed by pin name. <see cref="En"/> is the enable input
/// when the source provides one (ladder rung condition, EN pin); when null the call is unconditional.
/// </summary>
public sealed class IrFbCall : IrStatement
{
    /// <summary>The instance variable (a <c>ref</c>) or null for an instance-less block.</summary>
    public IrExpression? Instance { get; set; }

    /// <summary>Standard function block name (TON, CTU, R_TRIG, ...), see <see cref="Catalogs.StandardFunctionBlocks"/>.</summary>
    public string? Fb { get; set; }

    /// <summary>Project function block (POU) name.</summary>
    [IrRefKind("pou")]
    public string? Pou { get; set; }

    /// <summary>Method of the instance to invoke, for object-oriented targets.</summary>
    public string? Method { get; set; }

    /// <summary>Pin profile for standard blocks: iec | logix | s7 | ... Names the runtime interface a backend implements.</summary>
    public string? Profile { get; set; }
    public Dictionary<string, IrExpression> Inputs { get; set; } = new();
    public Dictionary<string, IrExpression> Outputs { get; set; } = new();
    public IrExpression? En { get; set; }

    /// <summary>Where the ENO output is stored, when wired.</summary>
    public IrExpression? Eno { get; set; }

    /// <summary>normal | prescan (first scan initialisation) | postscan (SFC postscan) | enableFalse (called with EN false).</summary>
    [IrEnum("normal", "prescan", "postscan", "enableFalse")]
    public string Mode { get; set; } = "normal";
}

/// <summary>A function invoked as a statement (result discarded or delivered through output arguments).</summary>
public sealed class IrFnCall : IrStatement
{
    public string? Name { get; set; }

    [IrRefKind("pou")]
    public string? Pou { get; set; }
    public List<IrExpression?> Args { get; set; } = new();
    public Dictionary<string, IrExpression>? Outputs { get; set; }
    public IrExpression? En { get; set; }
}

/// <summary>Call of an action of the current POU (or of <see cref="Pou"/>) with optional input and output values (Logix JSR/SBR/RET).</summary>
public sealed class IrActionCall : IrStatement
{
    [IrRefKind("action")]
    public string Action { get; set; } = "";

    [IrRefKind("pou")]
    public string? Pou { get; set; }
    public List<IrExpression> Inputs { get; set; } = new();
    public List<IrExpression> Outputs { get; set; } = new();
    public IrExpression? En { get; set; }
}

/// <summary>A vendor runtime service (Logix GSV/MSG/PID/motion, S7 SFC/SFB, ...) with positional arguments as the vendor catalog lists them. <see cref="Catalog"/> names the service catalog (logix, s7, twincat).</summary>
public sealed class IrSysCall : IrStatement
{
    public string Catalog { get; set; } = "";
    public string Service { get; set; } = "";
    public List<IrExpression?> Args { get; set; } = new();
    public IrExpression? En { get; set; }

    /// <summary>normal | prescan | postscan: scan mode the runtime service is invoked in (see <see cref="IrFbCall.Mode"/>).</summary>
    [IrEnum("normal", "prescan", "postscan")]
    public string Mode { get; set; } = "normal";
}

/// <summary>Block copy of <see cref="Count"/> elements from <see cref="Source"/> to <see cref="Dest"/>.</summary>
public sealed class IrCopy : IrStatement
{
    public IrExpression Source { get; set; } = new IrRef();
    public IrExpression Dest { get; set; } = new IrRef();
    public IrExpression Count { get; set; } = IrLiteral.OfInt(1);

    /// <summary>typed: count is in destination elements, types converted; bytes: raw memory image copied untyped (Logix COP/CPS).</summary>
    [IrEnum("typed", "bytes")]
    public string Mode { get; set; } = "typed";

    /// <summary>The copy must not be interrupted by other tasks (Logix CPS).</summary>
    public bool Atomic { get; set; }
    public IrExpression? En { get; set; }
}

/// <summary>Fill <see cref="Count"/> elements of <see cref="Dest"/> with <see cref="Value"/>.</summary>
public sealed class IrFill : IrStatement
{
    public IrExpression Value { get; set; } = IrLiteral.OfInt(0);
    public IrExpression Dest { get; set; } = new IrRef();
    public IrExpression Count { get; set; } = IrLiteral.OfInt(1);
    public IrExpression? En { get; set; }
}

public sealed class IrIf : IrStatement
{
    public List<IrIfBranch> Branches { get; set; } = new();
    public List<IrStatement>? Else { get; set; }
}

public sealed class IrIfBranch
{
    public IrExpression Condition { get; set; } = IrLiteral.False;
    public List<IrStatement> Body { get; set; } = new();
}

public sealed class IrCase : IrStatement
{
    public IrExpression Selector { get; set; } = IrLiteral.OfInt(0);
    public List<IrCaseArm> Arms { get; set; } = new();
    public List<IrStatement>? Else { get; set; }
}

public sealed class IrCaseArm
{
    public List<IrCaseLabel> Labels { get; set; } = new();
    public List<IrStatement> Body { get; set; } = new();
}

/// <summary>A case label: a literal or enum keyword, or an inclusive range.</summary>
public sealed class IrCaseLabel
{
    public IrExpression Value { get; set; } = IrLiteral.OfInt(0);
    public IrExpression? To { get; set; }
}

public sealed class IrFor : IrStatement
{
    public IrExpression Variable { get; set; } = new IrRef();
    public IrExpression From { get; set; } = IrLiteral.OfInt(0);
    public IrExpression To { get; set; } = IrLiteral.OfInt(0);
    public IrExpression? By { get; set; }
    public List<IrStatement> Body { get; set; } = new();
}

public sealed class IrWhile : IrStatement
{
    public IrExpression Condition { get; set; } = IrLiteral.False;
    public List<IrStatement> Body { get; set; } = new();
}

public sealed class IrRepeat : IrStatement
{
    public List<IrStatement> Body { get; set; } = new();
    public IrExpression Until { get; set; } = IrLiteral.True;
}

public sealed class IrExit : IrStatement
{
}

public sealed class IrContinue : IrStatement
{
}

/// <summary>Leave the current body (action, method or POU).</summary>
public sealed class IrReturn : IrStatement
{
    public IrExpression? En { get; set; }
}

public sealed class IrJump : IrStatement
{
    public string Label { get; set; } = "";
    public IrExpression? En { get; set; }
}

public sealed class IrLabel : IrStatement
{
    public string Name { get; set; } = "";
}

public sealed class IrErrorStatement : IrStatement
{
    public string Text { get; set; } = "";
}
