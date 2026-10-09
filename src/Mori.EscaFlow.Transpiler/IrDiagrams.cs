using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

// ------------------------------------------------------------------ ladder

/// <summary>A ladder body as drawn. The executable form is the enclosing body's statements (see the lowering contract in docs/plc-ir-2.0.md).</summary>
/// <remarks>
/// <para>
/// The drawing is kept in vendor-neutral element kinds (contact, coil, block, branch, jump, label, mcr,
/// return) that every ladder dialect shares, with the instruction name and operands as drawn, so a converter
/// with native ladder output (a Siemens LAD or TwinCAT LD target, a documentation generator) can redraw the
/// rung, and so the original rung text can be regenerated for traceability.
/// </para>
/// <para>
/// The lowering contract the importer applies when producing the statements: the rung condition starts true
/// at the left rail and flows left to right; a contact ANDs its operand (negated contacts AND the complement,
/// edge contacts AND an <c>edge</c> expression); a branch ORs the conditions at the end of its legs, and outputs
/// inside a leg see the incoming condition AND the leg's own; an assign coil stores the condition every scan,
/// set/reset coils store TRUE/FALSE only when the condition holds, pulse coils store an edge of the condition;
/// a block with enable <c>input</c> always runs with the condition bound to its enable pin, a block with enable
/// <c>gate</c> runs only when the condition is true; jump and return are gated; an MCR zone stores the
/// condition at its start and every following rung starts from it until the zone ends; after an output the
/// condition continues unchanged, so several outputs on one rung share it; compound conditions used by several
/// outputs are stored once in a body temporary.
/// </para>
/// </remarks>
public sealed class IrLdDiagram : IrDiagram
{
    /// <summary>Rungs in execution order.</summary>
    public List<IrRung> Rungs { get; set; } = new();
}

/// <summary>One rung: its number, comment, original text and the elements as drawn.</summary>
public sealed class IrRung : IrNode
{
    /// <summary>Rung number within the routine (the number lowered statements carry in <c>src.rung</c>).</summary>
    public int Number { get; set; }

    /// <summary>Rung comment, when the source has one.</summary>
    public string? Comment { get; set; }

    /// <summary>The rung source text, when kept.</summary>
    public string? Text { get; set; }

    /// <summary>Elements left to right; branches nest their legs.</summary>
    public List<IrLdElement> Elements { get; set; } = new();
}

/// <summary>
/// A ladder element in neutral terms. <see cref="Name"/> is the instruction or box name as drawn; the operands as
/// drawn are on the element kind (contact / coil operand, block args). The lowered statement produced for an
/// element carries <c>src.element</c> = the element's index in its rung (depth first).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrLdContact), "contact")]
[JsonDerivedType(typeof(IrLdCoil), "coil")]
[JsonDerivedType(typeof(IrLdBlock), "block")]
[JsonDerivedType(typeof(IrLdBranch), "branch")]
[JsonDerivedType(typeof(IrLdJump), "jump")]
[JsonDerivedType(typeof(IrLdLabel), "label")]
[JsonDerivedType(typeof(IrLdMcr), "mcr")]
[JsonDerivedType(typeof(IrLdReturn), "return")]
public abstract class IrLdElement : IrNode
{
    /// <summary>Instruction or box name as drawn (Logix mnemonic such as <c>XIC</c>, <c>TON</c>, or an add-on name).</summary>
    public string? Name { get; set; }
}

/// <summary>Conditions the rung: a BOOL operand or a comparison expression, possibly negated, possibly edge-detected.</summary>
/// <remarks>Logix XIC/XIO, the comparison instructions (EQU, GRT, LIM, MEQ, CMP), ONS and AFI are all contacts: they only shape the rung condition and never act.</remarks>
public sealed class IrLdContact : IrLdElement
{
    /// <summary>The tested value. Null for an edge contact that tests the incoming rung condition itself (Logix ONS).</summary>
    public IrExpression? Operand { get; set; }

    /// <summary>True for a normally-closed contact (the complement of the operand conditions the rung).</summary>
    public bool Negated { get; set; }

    /// <summary>rising | falling for an edge contact.</summary>
    [IrEnum("rising", "falling")]
    public string? Edge { get; set; }

    /// <summary>Edge memory variable.</summary>
    public IrExpression? Storage { get; set; }

    /// <summary>Operands as drawn beyond <see cref="Operand"/> (comparison instructions list both sides here).</summary>
    public List<IrExpression>? Args { get; set; }
}

/// <summary>Writes the rung condition to a BOOL operand.</summary>
public sealed class IrLdCoil : IrLdElement
{
    /// <summary>The BOOL (or bit) written.</summary>
    public IrExpression Operand { get; set; } = new IrRef();

    /// <summary>assign: operand := rc every scan; negated: operand := NOT rc; set / reset: latch when rc; risingPulse / fallingPulse: one-shot of rc with <see cref="Storage"/>.</summary>
    [IrEnum("assign", "negated", "set", "reset", "risingPulse", "fallingPulse")]
    public string Mode { get; set; } = "assign";

    /// <summary>Edge memory for the pulse modes (Logix OSR/OSF storage bit).</summary>
    public IrExpression? Storage { get; set; }
}

/// <summary>Any other instruction or box: executes a statement, gated or fed by the rung condition.</summary>
public sealed class IrLdBlock : IrLdElement
{
    /// <summary>Operands as drawn, positional.</summary>
    public List<IrExpression> Args { get; set; } = new();

    /// <summary>Named pins as drawn, for sources that wire boxes by pin name.</summary>
    public Dictionary<string, IrExpression>? Pins { get; set; }

    /// <summary>input: the statement runs every scan with the rung condition bound to its enable pin; gate: the statement runs only when the rung condition is true; none: unconditional.</summary>
    /// <remarks>Timers, counters, add-on instructions and Logix always-scan instructions (PID, alarms, MSG) are <c>input</c>: they must see a false condition to reset or clear. MOV, COP, JSR and the like are <c>gate</c>.</remarks>
    [IrEnum("input", "gate", "none")]
    public string Enable { get; set; } = "gate";

    /// <summary>True when the block passes the rung condition on (ENO); false when it terminates the rung.</summary>
    public bool Eno { get; set; } = true;

    /// <summary>True when the block names a project function block (POU) rather than a vendor instruction.</summary>
    public bool IsPou { get; set; }
}

/// <summary>A parallel branch; the rung condition after it is the OR of its legs.</summary>
public sealed class IrLdBranch : IrLdElement
{
    /// <summary>The legs, each a series of elements; an empty leg is a straight wire.</summary>
    public List<List<IrLdElement>> Legs { get; set; } = new();
}

/// <summary>Jump to a label in the same routine when the rung condition is true (Logix JMP).</summary>
public sealed class IrLdJump : IrLdElement
{
    /// <summary>Target label.</summary>
    public string Label { get; set; } = "";
}

/// <summary>A jump target (Logix LBL).</summary>
public sealed class IrLdLabel : IrLdElement
{
    /// <summary>Label name.</summary>
    public string Label { get; set; } = "";
}

/// <summary>Master control relay zone boundary: begin stores the rung condition, end restores it.</summary>
/// <remarks>Inside a Logix MCR zone every rung behaves as if its condition were ANDed with the zone condition: coils clear, timers reset. Lowering stores the zone condition in a temporary and starts every zoned rung from it.</remarks>
public sealed class IrLdMcr : IrLdElement
{
    /// <summary>True for the MCR that opens a zone, false for the one that closes it.</summary>
    public bool Begin { get; set; }
}

/// <summary>Leaves the routine when the rung condition is true (Logix RET with its return values, TND).</summary>
public sealed class IrLdReturn : IrLdElement
{
    /// <summary>Return values as drawn (Logix RET operands).</summary>
    public List<IrExpression>? Outputs { get; set; }
}

// ------------------------------------------------------------------ function block network (FBD / CFC)

/// <summary>A dataflow network as drawn (Logix FBD sheets, TwinCAT CFC, S7 FBD networks).</summary>
/// <remarks>
/// Lowering contract: executable elements are ordered topologically along the non-feedback wires (ties broken
/// by drawing position); each wire into a block pin becomes an assignment of the source value into the block's
/// instance member; then the block is invoked; output references are assigned in their own position. A
/// feedback wire is excluded from the ordering, so its destination reads the value the source produced in the
/// previous scan, which is exactly the "assume data available" semantics of Logix FBD.
/// </remarks>
public sealed class IrNetDiagram : IrDiagram
{
    /// <summary>Sheets in execution order.</summary>
    public List<IrNetSheet> Sheets { get; set; } = new();
}

/// <summary>One sheet of a network diagram.</summary>
public sealed class IrNetSheet : IrNode
{
    /// <summary>Sheet number.</summary>
    public int Number { get; set; }

    /// <summary>Elements on the sheet.</summary>
    public List<IrNetElement> Elements { get; set; } = new();

    /// <summary>Wires between element pins.</summary>
    public List<IrNetWire> Wires { get; set; } = new();
}

/// <summary>An element of a network sheet.</summary>
public sealed class IrNetElement : IrNode
{
    /// <summary>Element id, unique within the sheet (what wires refer to).</summary>
    public int Id { get; set; }

    /// <summary>input: a value entering the sheet (<see cref="Operand"/>); output: a variable written by the sheet; connectorIn / connectorOut: named sheet connectors; block: an instance of <see cref="Type"/> (vendor instruction or POU); function: an instance-less function; call / entry / exit: action call, entry and return markers; text: a text box.</summary>
    [IrEnum("input", "output", "connectorIn", "connectorOut", "block", "function", "call", "entry", "exit", "text")]
    public string Kind { get; set; } = "block";

    /// <summary>Instruction, function or POU name for block / function / call.</summary>
    public string? Type { get; set; }

    /// <summary>True when <see cref="Type"/> names a project POU.</summary>
    public bool IsPou { get; set; }

    /// <summary>Connector name for connectorIn / connectorOut; action name for call / entry / exit.</summary>
    public string? Name { get; set; }

    /// <summary>The value (input), target (output) or instance variable (block).</summary>
    public IrExpression? Operand { get; set; }

    /// <summary>Pins shown on the block, as drawn.</summary>
    public List<string>? Pins { get; set; }

    /// <summary>Pin-to-operand bindings written on the block rather than wired (Logix InOut parameters, array operands).</summary>
    public Dictionary<string, IrExpression>? Parameters { get; set; }

    /// <summary>Text of a text box.</summary>
    public string? Text { get; set; }

    /// <summary>Drawing position, kept for redrawing and for ordering ties.</summary>
    public int? X { get; set; }

    /// <summary>Drawing position.</summary>
    public int? Y { get; set; }
}

/// <summary>A wire from an output pin to an input pin.</summary>
public sealed class IrNetWire
{
    /// <summary>Source element id.</summary>
    public int From { get; set; }

    /// <summary>Source pin name (null for elements with one output, such as inputs and connectors).</summary>
    public string? FromPin { get; set; }

    /// <summary>Destination element id.</summary>
    public int To { get; set; }

    /// <summary>Destination pin name.</summary>
    public string? ToPin { get; set; }

    /// <summary>True for a feedback wire: the destination reads the value the source produced in the previous scan.</summary>
    public bool Feedback { get; set; }
}

// ------------------------------------------------------------------ sequential function chart

/// <summary>A sequential function chart as drawn. SFC bodies have no lowered statements: the step, transition and action semantics are executed by the target runtime's chart engine.</summary>
public sealed class IrSfcDiagram : IrDiagram
{
    /// <summary>Steps, with their actions.</summary>
    public List<IrSfcStep> Steps { get; set; } = new();

    /// <summary>Transitions between steps.</summary>
    public List<IrSfcTransition> Transitions { get; set; } = new();

    /// <summary>Selection and simultaneous branches.</summary>
    public List<IrSfcBranch> Branches { get; set; } = new();

    /// <summary>Stop elements (Logix SFC_STOP).</summary>
    public List<IrSfcStop> Stops { get; set; } = new();

    /// <summary>Directed links between element ids (steps, transitions, branch legs, stops).</summary>
    public List<IrSfcLink> Links { get; set; } = new();
}

/// <summary>A step of the chart.</summary>
public sealed class IrSfcStep : IrNode
{
    /// <summary>Element id within the chart.</summary>
    public int Id { get; set; }

    /// <summary>Step name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The step's state variable, when the platform backs steps with data.</summary>
    /// <remarks>Logix backs every step with an SFC_STEP structure (timers, counts, alarm limits) that logic can read.</remarks>
    [IrRefKind("variable")]
    public string? Variable { get; set; }

    /// <summary>True for the initial step.</summary>
    public bool Initial { get; set; }

    /// <summary>Step timer preset expression, when set.</summary>
    public IrExpression? Preset { get; set; }

    /// <summary>High alarm limit on the step time, when set.</summary>
    public IrExpression? LimitHigh { get; set; }

    /// <summary>Low alarm limit on the step time, when set.</summary>
    public IrExpression? LimitLow { get; set; }

    /// <summary>Actions attached to the step.</summary>
    public List<IrSfcAction> Actions { get; set; } = new();

    /// <summary>Drawing position.</summary>
    public int? X { get; set; }

    /// <summary>Drawing position.</summary>
    public int? Y { get; set; }
}

/// <summary>An action attached to a step.</summary>
public sealed class IrSfcAction : IrNode
{
    /// <summary>Element id within the chart.</summary>
    public int Id { get; set; }

    /// <summary>Action name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The action's state variable (Logix SFC_ACTION), when backed by data.</summary>
    [IrRefKind("variable")]
    public string? Variable { get; set; }

    /// <summary>IEC action qualifier.</summary>
    /// <remarks>N non-stored, S set, R reset, L time-limited, D time-delayed, P pulse, P1 pulse on activation, P0 pulse on deactivation, SL stored and time-limited, SD stored and time-delayed, DS delayed and stored.</remarks>
    [IrEnum("N", "S", "R", "L", "D", "P", "P1", "P0", "SL", "SD", "DS")]
    public string Qualifier { get; set; } = "N";

    /// <summary>True for a boolean action: the action variable is set while active and no body runs.</summary>
    public bool Boolean { get; set; }

    /// <summary>Timer preset for the timed qualifiers, when set.</summary>
    public IrExpression? Preset { get; set; }

    /// <summary>The action's statements (structured text in Logix).</summary>
    public IrBody? Body { get; set; }
}

/// <summary>A transition: the chart advances when its condition holds.</summary>
public sealed class IrSfcTransition : IrNode
{
    /// <summary>Element id within the chart.</summary>
    public int Id { get; set; }

    /// <summary>Transition name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The transition's BOOL variable, when backed by data.</summary>
    [IrRefKind("variable")]
    public string? Variable { get; set; }

    /// <summary>The BOOL condition expression.</summary>
    public IrExpression? Condition { get; set; }

    /// <summary>Forced value, when the transition is forced in the source project.</summary>
    public bool? Force { get; set; }

    /// <summary>Drawing position.</summary>
    public int? X { get; set; }

    /// <summary>Drawing position.</summary>
    public int? Y { get; set; }
}

/// <summary>A divergence or convergence.</summary>
public sealed class IrSfcBranch : IrNode
{
    /// <summary>Element id within the chart.</summary>
    public int Id { get; set; }

    /// <summary>selection: one leg is taken (the first whose transition holds); simultaneous: every leg runs in parallel.</summary>
    [IrEnum("selection", "simultaneous")]
    public string Type { get; set; } = "selection";

    /// <summary>diverge: opens the legs; converge: joins them.</summary>
    [IrEnum("diverge", "converge")]
    public string Flow { get; set; } = "diverge";

    /// <summary>Leg priority rule of a selection divergence, as the vendor names it.</summary>
    public string? Priority { get; set; }

    /// <summary>Leg ids in drawing order.</summary>
    public List<int> Legs { get; set; } = new();
}

/// <summary>A stop element: the chart stops when reached.</summary>
public sealed class IrSfcStop : IrNode
{
    /// <summary>Element id within the chart.</summary>
    public int Id { get; set; }

    /// <summary>Stop name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The stop's state variable, when backed by data.</summary>
    [IrRefKind("variable")]
    public string? Variable { get; set; }
}

/// <summary>A directed link between two chart element ids.</summary>
public sealed class IrSfcLink
{
    /// <summary>Source element id.</summary>
    public int From { get; set; }

    /// <summary>Destination element id.</summary>
    public int To { get; set; }
}
