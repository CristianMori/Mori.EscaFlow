using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Logic.Sfc;

public enum SfcBranchType
{
    Selection,
    Simultaneous,
    Unknown,
}

public enum SfcBranchFlow
{
    Diverge,
    Converge,
    Unknown,
}

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

    [JsonIgnore]
    public Dictionary<int, SfcNode> ById { get; } = new();

    [JsonIgnore]
    public IEnumerable<SfcStep> Steps => Nodes.OfType<SfcStep>();

    [JsonIgnore]
    public IEnumerable<SfcTransition> Transitions => Nodes.OfType<SfcTransition>();

    [JsonIgnore]
    public IEnumerable<SfcBranch> Branches => Nodes.OfType<SfcBranch>();

    [JsonIgnore]
    public SfcStep? InitialStep => Steps.FirstOrDefault(s => s.InitialStep);

    /// <summary>Links leaving the given node.</summary>
    public IEnumerable<SfcDirectedLink> LinksFrom(int id) => Links.Where(l => l.FromId == id);

    public IEnumerable<SfcDirectedLink> LinksTo(int id) => Links.Where(l => l.ToId == id);
}

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

public sealed class SfcStep : SfcNode
{
    /// <summary>The SFC_STEP tag backing this step; also the step's name.</summary>
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    public int? DescX { get; init; }
    public int? DescY { get; init; }
    public int? DescWidth { get; init; }
    public bool InitialStep { get; init; }
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

public sealed class SfcAction : SfcNode
{
    /// <summary>The SFC_ACTION tag backing this action.</summary>
    public string? Operand { get; init; }

    /// <summary>N, R, S, L, D, P, P1, P0, SL, SD, DS (or the long forms such as NonStored).</summary>
    public string? Qualifier { get; init; }
    public bool IsBoolean { get; init; }
    public bool PresetUsesExpr { get; init; }
    public string? IndicatorTag { get; init; }
    public string? Description { get; set; }
    public StSnippet? Preset { get; set; }
    public StSnippet? Body { get; set; }
}

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
    public string? Priority { get; init; }
    public List<SfcLeg> Legs { get; init; } = new();
}

public sealed class SfcLeg : SfcNode
{
    public bool? Force { get; init; }
}

public sealed class SfcStop : SfcNode
{
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    public int? DescX { get; init; }
    public int? DescY { get; init; }
    public int? DescWidth { get; init; }
    public string? Description { get; set; }
}

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

public sealed class SfcDirectedLink : L5xElement
{
    public int FromId { get; init; }
    public int ToId { get; init; }
    public bool Show { get; init; } = true;

    [JsonIgnore]
    public SfcNode? From { get; set; }

    [JsonIgnore]
    public SfcNode? To { get; set; }

    public override string ToString() => $"{FromId} -> {ToId}";
}

public sealed class SfcAttachment : L5xElement
{
    public int FromId { get; init; }
    public int ToId { get; init; }

    [JsonIgnore]
    public SfcNode? From { get; set; }

    [JsonIgnore]
    public SfcNode? To { get; set; }
}
