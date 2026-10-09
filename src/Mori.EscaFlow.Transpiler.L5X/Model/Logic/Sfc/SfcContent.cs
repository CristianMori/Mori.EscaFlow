using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Logic.Sfc;

/// <summary>Selection: exactly one leg is taken (first true transition, or by priority); Simultaneous: every leg is activated.</summary>
public enum SfcBranchType
{
    Selection,
    Simultaneous,
    Unknown,
}

/// <summary>Diverge: one flow splits into legs; Converge: legs join back into one flow.</summary>
public enum SfcBranchFlow
{
    Diverge,
    Converge,
    Unknown,
}

/// <summary>
/// A sequential function chart: steps (with actions), transitions, branches, stops and the directed links that
/// connect them. The chart engine of the controller activates steps, runs their actions according to the
/// qualifiers and moves on when the following transition is true; this model only describes the chart, the
/// execution semantics belong to the runtime.
/// </summary>
public sealed class SfcContent : RoutineContent
{
    public string? SheetSize { get; init; }
    public string? SheetOrientation { get; init; }
    public string? StepName { get; init; }
    public string? TransitionName { get; init; }
    public string? ActionName { get; init; }
    public string? StopName { get; init; }

    /// <summary>All chart elements (steps, transitions, branches, legs, stops, subroutine markers, text boxes) in file order.</summary>
    public List<SfcNode> Nodes { get; init; } = new();
    public List<SfcDirectedLink> Links { get; init; } = new();
    public List<SfcAttachment> Attachments { get; init; } = new();

    /// <summary>Node lookup by ID (branch legs included), filled by the linker.</summary>
    [JsonIgnore]
    public Dictionary<int, SfcNode> ById { get; } = new();

    [JsonIgnore]
    public IEnumerable<SfcStep> Steps => Nodes.OfType<SfcStep>();

    [JsonIgnore]
    public IEnumerable<SfcTransition> Transitions => Nodes.OfType<SfcTransition>();

    [JsonIgnore]
    public IEnumerable<SfcBranch> Branches => Nodes.OfType<SfcBranch>();

    /// <summary>The step active when the chart starts (or restarts, per the controller's SFC restart option).</summary>
    [JsonIgnore]
    public SfcStep? InitialStep => Steps.FirstOrDefault(s => s.InitialStep);

    /// <summary>Links leaving the given node.</summary>
    public IEnumerable<SfcDirectedLink> LinksFrom(int id) => Links.Where(l => l.FromId == id);

    public IEnumerable<SfcDirectedLink> LinksTo(int id) => Links.Where(l => l.ToId == id);
}

/// <summary>Any chart element with an ID and drawing position.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SfcStep), "step")]
[JsonDerivedType(typeof(SfcAction), "action")]
[JsonDerivedType(typeof(SfcTransition), "transition")]
[JsonDerivedType(typeof(SfcBranch), "branch")]
[JsonDerivedType(typeof(SfcLeg), "leg")]
[JsonDerivedType(typeof(SfcStop), "stop")]
[JsonDerivedType(typeof(SfcSbrRet), "sbrret")]
[JsonDerivedType(typeof(SfcTextBox), "textbox")]
public abstract class SfcNode : L5xElement
{
    public int Id { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public override string ToString() => $"{GetType().Name[3..]} #{Id}";
}

/// <summary>A step. Its SFC_STEP tag records activity (.X), timing (.T, .PRE, .DN) and alarms (.AlarmHigh/.AlarmLow against the limits).</summary>
public sealed class SfcStep : SfcNode
{
    /// <summary>The SFC_STEP tag backing this step; also the step's name.</summary>
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    public int? DescX { get; init; }
    public int? DescY { get; init; }
    public int? DescWidth { get; init; }
    public bool InitialStep { get; init; }

    /// <summary>When true the preset is an expression evaluated on activation; otherwise a constant.</summary>
    public bool PresetUsesExpr { get; init; }
    public bool LimitHighUsesExpr { get; init; }
    public bool LimitLowUsesExpr { get; init; }
    public bool ShowActions { get; init; }
    public string? Description { get; set; }
    public StSnippet? Preset { get; set; }
    public StSnippet? LimitHigh { get; set; }
    public StSnippet? LimitLow { get; set; }
    public List<SfcAction> Actions { get; init; } = new();
}

/// <summary>An action attached to a step. Its qualifier decides when the body runs relative to the step's activity.</summary>
public sealed class SfcAction : SfcNode
{
    /// <summary>The SFC_ACTION tag backing this action.</summary>
    public string? Operand { get; init; }

    /// <summary>N, R, S, L, D, P, P1, P0, SL, SD, DS (or the long forms such as NonStored).</summary>
    public string? Qualifier { get; init; }

    /// <summary>A boolean action has no body: its tag's .Q is simply true while the action is active.</summary>
    public bool IsBoolean { get; init; }
    public bool PresetUsesExpr { get; init; }
    public string? IndicatorTag { get; init; }
    public string? Description { get; set; }

    /// <summary>Time preset for the timed qualifiers (L, D, SL, SD, DS).</summary>
    public StSnippet? Preset { get; set; }
    public StSnippet? Body { get; set; }
}

/// <summary>A transition: the chart advances past it when its condition is true (or when forced while testing).</summary>
public sealed class SfcTransition : SfcNode
{
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    public int? DescX { get; init; }
    public int? DescY { get; init; }
    public int? DescWidth { get; init; }

    /// <summary>Null when not forced; otherwise the forced value.</summary>
    public bool? Force { get; init; }
    public string? Description { get; set; }
    public StSnippet? Condition { get; set; }
}

public sealed class SfcBranch : SfcNode
{
    public SfcBranchType BranchType { get; init; }
    public SfcBranchFlow BranchFlow { get; init; }

    /// <summary>For selection branches: UserDefined when the legs are evaluated in the drawn priority order.</summary>
    public string? Priority { get; init; }
    public List<SfcLeg> Legs { get; init; } = new();
}

/// <summary>One leg of a branch; links attach to legs, not to the branch itself.</summary>
public sealed class SfcLeg : SfcNode
{
    public bool? Force { get; init; }
}

/// <summary>A stop: reaching it halts the chart (its SFC_STOP tag .X goes true) until a reset.</summary>
public sealed class SfcStop : SfcNode
{
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    public int? DescX { get; init; }
    public int? DescY { get; init; }
    public int? DescWidth { get; init; }
    public string? Description { get; set; }
}

/// <summary>Subroutine entry (In) and return (Ret) markers of a chart invoked with JSR.</summary>
public sealed class SfcSbrRet : SfcNode
{
    public string? In { get; init; }
    public string? Ret { get; init; }
}

public sealed class SfcTextBox : SfcNode
{
    public int? Width { get; init; }
    public string? Text { get; set; }
}

/// <summary>A flow link between two node IDs; <see cref="From"/> and <see cref="To"/> are bound by the linker.</summary>
public sealed class SfcDirectedLink : L5xElement
{
    public int FromId { get; init; }
    public int ToId { get; init; }

    /// <summary>False for links drawn as wire connectors rather than lines.</summary>
    public bool Show { get; init; } = true;

    [JsonIgnore]
    public SfcNode? From { get; set; }

    [JsonIgnore]
    public SfcNode? To { get; set; }

    public override string ToString() => $"{FromId} -> {ToId}";
}

/// <summary>A text box attached to a node.</summary>
public sealed class SfcAttachment : L5xElement
{
    public int FromId { get; init; }
    public int ToId { get; init; }

    [JsonIgnore]
    public SfcNode? From { get; set; }

    [JsonIgnore]
    public SfcNode? To { get; set; }
}
