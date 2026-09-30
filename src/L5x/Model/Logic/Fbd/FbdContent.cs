using System.Text.Json.Serialization;

namespace L5x.Model.Logic.Fbd;

public sealed class FbdContent : RoutineContent
{
    public string? SheetSize { get; init; }
    public string? SheetOrientation { get; init; }
    public List<FbdSheet> Sheets { get; init; } = new();
}

public sealed class FbdSheet : L5xElement
{
    public int Number { get; init; }
    public string? Description { get; set; }
    public List<FbdElement> Elements { get; init; } = new();
    public List<FbdWire> Wires { get; init; } = new();
    public List<FbdAttachment> Attachments { get; init; } = new();

    [JsonIgnore]
    public Dictionary<int, FbdElement> ById { get; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FbdIRef), "iref")]
[JsonDerivedType(typeof(FbdORef), "oref")]
[JsonDerivedType(typeof(FbdICon), "icon")]
[JsonDerivedType(typeof(FbdOCon), "ocon")]
[JsonDerivedType(typeof(FbdBlock), "block")]
[JsonDerivedType(typeof(FbdFunction), "function")]
[JsonDerivedType(typeof(FbdAddOnInstruction), "aoi")]
[JsonDerivedType(typeof(FbdJsr), "jsr")]
[JsonDerivedType(typeof(FbdSbr), "sbr")]
[JsonDerivedType(typeof(FbdRet), "ret")]
[JsonDerivedType(typeof(FbdTextBox), "textbox")]
public abstract class FbdElement : L5xElement
{
    public int Id { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public override string ToString() => $"{GetType().Name[3..]} #{Id}";
}

/// <summary>Input reference: a tag or literal feeding the diagram.</summary>
public sealed class FbdIRef : FbdElement
{
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    /// <summary>Parsed operand (tag path or literal) filled by the logic phase.</summary>
    public Rll.Operand? ParsedOperand { get; set; }
}

/// <summary>Output reference: a tag written by the diagram.</summary>
public sealed class FbdORef : FbdElement
{
    public string? Operand { get; init; }
    public bool HideDesc { get; init; }
    public Rll.Operand? ParsedOperand { get; set; }
}

/// <summary>Input wire connector (named jump).</summary>
public sealed class FbdICon : FbdElement
{
    public string? Name { get; init; }
}

public sealed class FbdOCon : FbdElement
{
    public string? Name { get; init; }
}

/// <summary>A function block instruction with a backing tag.</summary>
public sealed class FbdBlock : FbdElement
{
    /// <summary>Instruction mnemonic, e.g. TONR, PIDE.</summary>
    public required string Type { get; init; }
    public string? Operand { get; init; }
    public List<string> VisiblePins { get; init; } = new();
    public bool HideDesc { get; init; }
    public string? AutotuneTag { get; init; }
    /// <summary>Array operands declared as child <c>&lt;Array Name= Operand=/&gt;</c> elements (DEDT, FGEN, MAVE, RMPS).</summary>
    public Dictionary<string, string>? Arrays { get; set; }
}

/// <summary>A tagless function (ADD, SUB, ...), available on 5x80 controllers.</summary>
public sealed class FbdFunction : FbdElement
{
    public required string Type { get; init; }
}

public sealed class FbdAddOnInstruction : FbdElement
{
    public required string Name { get; init; }
    public string? Operand { get; init; }
    public List<string> VisiblePins { get; init; } = new();
    public bool HideDesc { get; init; }
    /// <summary>InOut parameter bindings: parameter name to argument text.</summary>
    public List<KeyValuePair<string, string>> InOutParameters { get; init; } = new();
}

public sealed class FbdJsr : FbdElement
{
    public string? Routine { get; init; }
    public List<string> In { get; init; } = new();
    public List<string> Ret { get; init; } = new();
}

public sealed class FbdSbr : FbdElement
{
    public string? Routine { get; init; }
    public List<string> In { get; init; } = new();
}

public sealed class FbdRet : FbdElement
{
    public string? Routine { get; init; }
    public List<string> Ret { get; init; } = new();
}

public sealed class FbdTextBox : FbdElement
{
    public int? Width { get; init; }
    public string? Text { get; set; }
}

public sealed class FbdWire : L5xElement
{
    public int FromId { get; init; }
    public string? FromParam { get; init; }
    public int ToId { get; init; }
    public string? ToParam { get; init; }
    public bool IsFeedback { get; init; }

    [JsonIgnore]
    public FbdElement? From { get; set; }

    [JsonIgnore]
    public FbdElement? To { get; set; }

    public override string ToString() => $"{FromId}.{FromParam} -> {ToId}.{ToParam}";
}

public sealed class FbdAttachment : L5xElement
{
    public int FromId { get; init; }
    public int ToId { get; init; }
}
