using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>Base of every statement; polymorphic on <c>kind</c>.</summary>
/// <remarks>
/// <para>
/// The statement set is the IEC 61131-3 structured text statement set (assignment, calls, if, case, for,
/// while, repeat, exit, continue, return) plus what graphical languages and vendor instruction sets need once
/// lowered: jumps and labels (ladder JMP/LBL, which have no structured equivalent), block copies and fills (the
/// Logix COP/CPS/FLL family is a memory operation, not an assignment), calls into vendor service catalogs, and
/// an error placeholder so a document can carry what the front-end could not interpret without losing the rest.
/// </para>
/// <para>
/// Several statements carry an <c>en</c> expression: the enable that gates or feeds the statement. It comes from
/// a ladder rung condition or an EN pin. The convention is uniform: a statement without <c>en</c> runs
/// unconditionally; one with <c>en</c> is either executed every scan with the condition bound to its enable pin
/// (function blocks, every-scan services, which need to see a false condition to reset) or only when the
/// condition is true (gated services). Which of the two applies is decided by the front-end when it lowers
/// ladder: gated statements are wrapped in an <c>if</c> instead, so <c>en</c> on a statement always means
/// "bind, do not gate".
/// </para>
/// </remarks>
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
/// <remarks>
/// Ladder output instructions lower to assignments: OTE becomes <c>x := rc</c> every scan (so a false rung
/// clears the coil, as on the PLC), OTL/OTU become assignments of TRUE/FALSE inside an <c>if</c>, MOV/ADD/CPT
/// become assignments of the computed value. Implicit vendor conversions (Logix REAL to DINT stores round to the
/// nearest even integer) are made explicit by a <c>conv</c> node in the value, so the assignment itself has no
/// hidden semantics.
/// </remarks>
public sealed class IrAssign : IrStatement
{
    /// <summary>The destination: a <c>ref</c> or a <c>bit</c> access.</summary>
    public IrExpression Target { get; set; } = new IrRef();

    /// <summary>The stored value, typed.</summary>
    public IrExpression Value { get; set; } = IrLiteral.False;

    /// <summary>SFC non-retentive assignment (Logix <c>[:=]</c>): reset when the action is deactivated.</summary>
    public bool NonRetentive { get; set; }
}

/// <summary>
/// Invocation of a function block instance: a standard block (<see cref="Fb"/> with <see cref="Profile"/>) or a
/// project POU (<see cref="Pou"/>). Inputs and outputs are keyed by pin name. <see cref="En"/> is the enable input
/// when the source provides one (ladder rung condition, EN pin); when null the call is unconditional.
/// </summary>
/// <remarks>
/// <para>
/// Standard blocks (TON, CTU, R_TRIG, ...) are the plugin point for timing semantics: the block name is IEC, the
/// profile says which vendor's instance structure and pin semantics apply, and the converter dispatches to the
/// runtime implementation of that profile. A Logix TON therefore stays a TON on a TIMER structure with its IN
/// pin bound to the rung condition; a converter without a <c>logix</c> profile implementation must refuse it
/// rather than guess.
/// </para>
/// <para>
/// For a project function block (a Logix add-on instruction) <see cref="En"/> maps onto the block's implicit
/// enable: Logix sets EnableIn from the rung, runs the logic when true and the EnableInFalse routine when
/// false, which is why the call is always emitted (never gated) and the converter branches on <c>en</c>.
/// Inputs are copied in before the call and outputs copied out after it; InOut parameters are passed by
/// reference. A Logix InOut parameter accepts any structure of the declared layout, so a converter binds it as a
/// view over the argument's memory rather than by type identity.
/// </para>
/// <para>
/// <see cref="Mode"/> distinguishes the scan passes: <c>prescan</c> calls are emitted by the synthesized init
/// hooks and run the block's prescan behaviour (clear outputs, initialise state) instead of its logic.
/// </para>
/// </remarks>
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

    /// <summary>Input pin values by pin name (standard pin names under the profile, or parameter names of the POU; InOut parameters appear here).</summary>
    public Dictionary<string, IrExpression> Inputs { get; set; } = new();

    /// <summary>Output pins by name, each with the reference that receives the value after the call.</summary>
    public Dictionary<string, IrExpression> Outputs { get; set; } = new();

    /// <summary>Enable: the rung condition or EN pin; null for an unconditional call.</summary>
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
    /// <summary>Standard function name, when not a project function.</summary>
    public string? Name { get; set; }

    /// <summary>Project function (POU) name.</summary>
    [IrRefKind("pou")]
    public string? Pou { get; set; }

    /// <summary>Positional arguments; null entries are elided positions.</summary>
    public List<IrExpression?> Args { get; set; } = new();

    /// <summary>Output arguments by name (IEC <c>Out =&gt; x</c>).</summary>
    public Dictionary<string, IrExpression>? Outputs { get; set; }

    /// <summary>Enable, when the source has one.</summary>
    public IrExpression? En { get; set; }
}

/// <summary>Call of an action of the current POU (or of <see cref="Pou"/>) with optional input and output values (Logix JSR/SBR/RET).</summary>
/// <remarks>
/// Logix passes JSR parameters to the callee's SBR tags and receives RET values back. The importer binds those
/// to plain assignments around the call whenever it knows the callee's SBR/RET lists, so a converter normally sees
/// an action call with no parameters and emits a plain method call. <see cref="Inputs"/> and
/// <see cref="Outputs"/> stay filled only when the callee's parameter list was unavailable (a source-protected
/// routine), in which case the converter must report that it cannot bind them.
/// </remarks>
public sealed class IrActionCall : IrStatement
{
    /// <summary>The action (routine) name.</summary>
    [IrRefKind("action")]
    public string Action { get; set; } = "";

    /// <summary>The POU owning the action; null for the current POU.</summary>
    [IrRefKind("pou")]
    public string? Pou { get; set; }

    /// <summary>Unbound input values (normally empty, see the remarks).</summary>
    public List<IrExpression> Inputs { get; set; } = new();

    /// <summary>Unbound output destinations (normally empty, see the remarks).</summary>
    public List<IrExpression> Outputs { get; set; } = new();

    /// <summary>Enable, when the source has one.</summary>
    public IrExpression? En { get; set; }
}

/// <summary>A vendor runtime service (Logix GSV/MSG/PID/motion, S7 SFC/SFB, ...) with positional arguments as the vendor catalog lists them. <see cref="Catalog"/> names the service catalog (logix, s7, twincat).</summary>
/// <remarks>
/// Services are the second plugin point. Everything an instruction set has beyond the IEC core (system values,
/// messaging, PID loops, motion, file instructions) is a call into a named catalog; the catalog describes each
/// service's parameters and whether ladder binds the rung condition as an input (every scan) or as a gate. A
/// converter implements a catalog through a runtime interface and must report, never silently drop, services it
/// does not implement. The property is called <c>catalog</c> rather than <c>vendor</c> because <c>vendor</c> is
/// the extension slot every node already has.
/// </remarks>
public sealed class IrSysCall : IrStatement
{
    /// <summary>Catalog id (logix, s7, twincat).</summary>
    public string Catalog { get; set; } = "";

    /// <summary>Service (instruction) name as the catalog lists it.</summary>
    public string Service { get; set; } = "";

    /// <summary>Positional arguments in catalog order; null entries are elided operands.</summary>
    public List<IrExpression?> Args { get; set; } = new();

    /// <summary>Enable, bound for every-scan services; gated services are wrapped in an <c>if</c> instead.</summary>
    public IrExpression? En { get; set; }

    /// <summary>normal | prescan | postscan: scan mode the runtime service is invoked in (see <see cref="IrFbCall.Mode"/>).</summary>
    [IrEnum("normal", "prescan", "postscan")]
    public string Mode { get; set; } = "normal";
}

/// <summary>Block copy of <see cref="Count"/> elements from <see cref="Source"/> to <see cref="Dest"/>.</summary>
/// <remarks>
/// Logix COP copies raw bytes without conversion (a DINT array into a REAL array reinterprets the bits), which
/// is <c>bytes</c> mode; a typed copy converts element by element. CPS is COP with the guarantee that no other
/// task observes a partially copied image, hence <see cref="Atomic"/>.
/// </remarks>
public sealed class IrCopy : IrStatement
{
    /// <summary>Source variable, element or structure.</summary>
    public IrExpression Source { get; set; } = new IrRef();

    /// <summary>Destination variable, element or structure.</summary>
    public IrExpression Dest { get; set; } = new IrRef();

    /// <summary>Number of destination elements to write.</summary>
    public IrExpression Count { get; set; } = IrLiteral.OfInt(1);

    /// <summary>typed: count is in destination elements, types converted; bytes: raw memory image copied untyped (Logix COP/CPS).</summary>
    [IrEnum("typed", "bytes")]
    public string Mode { get; set; } = "typed";

    /// <summary>The copy must not be interrupted by other tasks (Logix CPS).</summary>
    public bool Atomic { get; set; }

    /// <summary>Enable, when the source has one.</summary>
    public IrExpression? En { get; set; }
}

/// <summary>Fill <see cref="Count"/> elements of <see cref="Dest"/> with <see cref="Value"/>.</summary>
public sealed class IrFill : IrStatement
{
    /// <summary>The value written to every element (Logix FLL converts it to the destination type).</summary>
    public IrExpression Value { get; set; } = IrLiteral.OfInt(0);

    /// <summary>Destination variable or element where the fill starts.</summary>
    public IrExpression Dest { get; set; } = new IrRef();

    /// <summary>Number of elements to write.</summary>
    public IrExpression Count { get; set; } = IrLiteral.OfInt(1);

    /// <summary>Enable, when the source has one.</summary>
    public IrExpression? En { get; set; }
}

/// <summary><c>IF ... ELSIF ... ELSE ... END_IF</c>; also the gate for conditionally executed ladder outputs.</summary>
public sealed class IrIf : IrStatement
{
    /// <summary>The IF and ELSIF branches in order.</summary>
    public List<IrIfBranch> Branches { get; set; } = new();

    /// <summary>The ELSE body, when present.</summary>
    public List<IrStatement>? Else { get; set; }
}

/// <summary>One condition and its body.</summary>
public sealed class IrIfBranch
{
    /// <summary>A BOOL expression.</summary>
    public IrExpression Condition { get; set; } = IrLiteral.False;

    /// <summary>Statements executed when the condition holds.</summary>
    public List<IrStatement> Body { get; set; } = new();
}

/// <summary><c>CASE selector OF ... ELSE ... END_CASE</c>.</summary>
public sealed class IrCase : IrStatement
{
    /// <summary>An integer or enumeration expression.</summary>
    public IrExpression Selector { get; set; } = IrLiteral.OfInt(0);

    /// <summary>The arms in order; the first matching arm runs.</summary>
    public List<IrCaseArm> Arms { get; set; } = new();

    /// <summary>The ELSE body, when present.</summary>
    public List<IrStatement>? Else { get; set; }
}

/// <summary>One arm of a case statement.</summary>
public sealed class IrCaseArm
{
    /// <summary>Values or ranges that select this arm.</summary>
    public List<IrCaseLabel> Labels { get; set; } = new();

    /// <summary>Statements executed when a label matches.</summary>
    public List<IrStatement> Body { get; set; } = new();
}

/// <summary>A case label: a literal or enum keyword, or an inclusive range.</summary>
public sealed class IrCaseLabel
{
    /// <summary>The value, or the start of the range.</summary>
    public IrExpression Value { get; set; } = IrLiteral.OfInt(0);

    /// <summary>End of the range (inclusive), null for a single value.</summary>
    public IrExpression? To { get; set; }
}

/// <summary><c>FOR variable := from TO to BY by DO ... END_FOR</c>; the loop variable is a declared variable, as in IEC (and Logix FOR instructions).</summary>
public sealed class IrFor : IrStatement
{
    /// <summary>The loop variable (a <c>ref</c>).</summary>
    public IrExpression Variable { get; set; } = new IrRef();

    /// <summary>Initial value.</summary>
    public IrExpression From { get; set; } = IrLiteral.OfInt(0);

    /// <summary>Final value, inclusive.</summary>
    public IrExpression To { get; set; } = IrLiteral.OfInt(0);

    /// <summary>Step, null for 1. A negative step counts down.</summary>
    public IrExpression? By { get; set; }

    /// <summary>Loop body.</summary>
    public List<IrStatement> Body { get; set; } = new();
}

/// <summary><c>WHILE condition DO ... END_WHILE</c>.</summary>
public sealed class IrWhile : IrStatement
{
    /// <summary>A BOOL expression evaluated before each iteration.</summary>
    public IrExpression Condition { get; set; } = IrLiteral.False;

    /// <summary>Loop body.</summary>
    public List<IrStatement> Body { get; set; } = new();
}

/// <summary><c>REPEAT ... UNTIL condition END_REPEAT</c>; the body runs at least once.</summary>
public sealed class IrRepeat : IrStatement
{
    /// <summary>Loop body.</summary>
    public List<IrStatement> Body { get; set; } = new();

    /// <summary>A BOOL expression evaluated after each iteration; the loop ends when it holds.</summary>
    public IrExpression Until { get; set; } = IrLiteral.True;
}

/// <summary><c>EXIT</c>: leave the innermost loop.</summary>
public sealed class IrExit : IrStatement
{
}

/// <summary><c>CONTINUE</c>: next iteration of the innermost loop.</summary>
public sealed class IrContinue : IrStatement
{
}

/// <summary>Leave the current body (action, method or POU).</summary>
/// <remarks>Ladder RET and TND lower to a return gated by the rung condition; the importer wraps it in an <c>if</c>, so <see cref="En"/> is for sources that express the enable natively.</remarks>
public sealed class IrReturn : IrStatement
{
    /// <summary>Enable, when the source has one.</summary>
    public IrExpression? En { get; set; }
}

/// <summary>Jump to a <see cref="IrLabel"/> in the same body (ladder JMP).</summary>
public sealed class IrJump : IrStatement
{
    /// <summary>Target label name.</summary>
    public string Label { get; set; } = "";

    /// <summary>Enable, when the source has one.</summary>
    public IrExpression? En { get; set; }
}

/// <summary>A jump target (ladder LBL). Labels always sit at the top level of a body, which is what makes a structured <c>goto</c> possible in every target language.</summary>
public sealed class IrLabel : IrStatement
{
    /// <summary>Label name, unique within the body.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Text the front-end could not interpret, kept so the rest of the body is still usable and the gap is visible.</summary>
public sealed class IrErrorStatement : IrStatement
{
    /// <summary>The original text.</summary>
    public string Text { get; set; } = "";
}
