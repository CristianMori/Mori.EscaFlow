using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

// ------------------------------------------------------------------ ladder

/// <summary>A ladder body as drawn. The executable form is the enclosing body's statements (see the lowering contract in docs/plc-ir-2.0.md).</summary>
public sealed class IrLdDiagram : IrDiagram
{
    public List<IrRung> Rungs { get; set; } = new();
}

public sealed class IrRung : IrNode
{
    public int Number { get; set; }
    public string? Comment { get; set; }

    /// <summary>The rung source text, when kept.</summary>
    public string? Text { get; set; }
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
    public string? Name { get; set; }
}

/// <summary>Conditions the rung: a BOOL operand or a comparison expression, possibly negated, possibly edge-detected.</summary>
public sealed class IrLdContact : IrLdElement
{
    /// <summary>The tested value. Null for an edge contact that tests the incoming rung condition itself (Logix ONS).</summary>
    public IrExpression? Operand { get; set; }
    public bool Negated { get; set; }

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
    public IrExpression Operand { get; set; } = new IrRef();

    /// <summary>assign: operand := rc every scan; negated: operand := NOT rc; set / reset: latch when rc; risingPulse / fallingPulse: one-shot of rc with <see cref="Storage"/>.</summary>
    [IrEnum("assign", "negated", "set", "reset", "risingPulse", "fallingPulse")]
    public string Mode { get; set; } = "assign";
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
    [IrEnum("input", "gate", "none")]
    public string Enable { get; set; } = "gate";

    /// <summary>True when the block passes the rung condition on (ENO); false when it terminates the rung.</summary>
    public bool Eno { get; set; } = true;

    /// <summary>True when the block names a project function block (POU) rather than a vendor instruction.</summary>
    public bool IsPou { get; set; }
}

public sealed class IrLdBranch : IrLdElement
{
    public List<List<IrLdElement>> Legs { get; set; } = new();
}

public sealed class IrLdJump : IrLdElement
{
    public string Label { get; set; } = "";
}

public sealed class IrLdLabel : IrLdElement
{
    public string Label { get; set; } = "";
}

/// <summary>Master control relay zone boundary: begin stores the rung condition, end restores it.</summary>
public sealed class IrLdMcr : IrLdElement
{
    public bool Begin { get; set; }
}

public sealed class IrLdReturn : IrLdElement
{
    public List<IrExpression>? Outputs { get; set; }
}

// ------------------------------------------------------------------ function block network (FBD / CFC)

/// <summary>A dataflow network as drawn (Logix FBD sheets, TwinCAT CFC, S7 FBD networks).</summary>
public sealed class IrNetDiagram : IrDiagram
{
    public List<IrNetSheet> Sheets { get; set; } = new();
}

public sealed class IrNetSheet : IrNode
{
    public int Number { get; set; }
    public List<IrNetElement> Elements { get; set; } = new();
    public List<IrNetWire> Wires { get; set; } = new();
}

public sealed class IrNetElement : IrNode
{
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
    public string? Text { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
}

public sealed class IrNetWire
{
    public int From { get; set; }
    public string? FromPin { get; set; }
    public int To { get; set; }
    public string? ToPin { get; set; }

    /// <summary>True for a feedback wire: the destination reads the value the source produced in the previous scan.</summary>
    public bool Feedback { get; set; }
}

// ------------------------------------------------------------------ sequential function chart

public sealed class IrSfcDiagram : IrDiagram
{
    public List<IrSfcStep> Steps { get; set; } = new();
    public List<IrSfcTransition> Transitions { get; set; } = new();
    public List<IrSfcBranch> Branches { get; set; } = new();
    public List<IrSfcStop> Stops { get; set; } = new();
    public List<IrSfcLink> Links { get; set; } = new();
}

public sealed class IrSfcStep : IrNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The step's state variable, when the platform backs steps with data.</summary>
    [IrRefKind("variable")]
    public string? Variable { get; set; }
    public bool Initial { get; set; }
    public IrExpression? Preset { get; set; }
    public IrExpression? LimitHigh { get; set; }
    public IrExpression? LimitLow { get; set; }
    public List<IrSfcAction> Actions { get; set; } = new();
    public int? X { get; set; }
    public int? Y { get; set; }
}

public sealed class IrSfcAction : IrNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [IrRefKind("variable")]
    public string? Variable { get; set; }

    /// <summary>IEC action qualifier.</summary>
    [IrEnum("N", "S", "R", "L", "D", "P", "P1", "P0", "SL", "SD", "DS")]
    public string Qualifier { get; set; } = "N";

    /// <summary>True for a boolean action: the action variable is set while active and no body runs.</summary>
    public bool Boolean { get; set; }
    public IrExpression? Preset { get; set; }
    public IrBody? Body { get; set; }
}

public sealed class IrSfcTransition : IrNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [IrRefKind("variable")]
    public string? Variable { get; set; }
    public IrExpression? Condition { get; set; }
    public bool? Force { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
}

public sealed class IrSfcBranch : IrNode
{
    public int Id { get; set; }

    [IrEnum("selection", "simultaneous")]
    public string Type { get; set; } = "selection";

    [IrEnum("diverge", "converge")]
    public string Flow { get; set; } = "diverge";
    public string? Priority { get; set; }

    /// <summary>Leg ids in drawing order.</summary>
    public List<int> Legs { get; set; } = new();
}

public sealed class IrSfcStop : IrNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [IrRefKind("variable")]
    public string? Variable { get; set; }
}

public sealed class IrSfcLink
{
    public int From { get; set; }
    public int To { get; set; }
}
