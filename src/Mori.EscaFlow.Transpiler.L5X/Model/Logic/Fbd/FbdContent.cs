using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Logic.Fbd;

/// <summary>
/// A function block diagram routine: sheets of elements connected by wires. Execution follows the data flow:
/// a block runs once its inputs are available, output references are written last, and feedback wires deliver
/// the value produced in the previous scan.
/// </summary>
public sealed class FbdContent : RoutineContent
{
    public string? SheetSize { get; init; }
    public string? SheetOrientation { get; init; }
    public List<FbdSheet> Sheets { get; init; } = new();
}

/// <summary>One sheet of the diagram; element IDs are unique within the sheet.</summary>
public sealed class FbdSheet : L5xElement
{
    public int Number { get; init; }
    public string? Description { get; set; }
    public List<FbdElement> Elements { get; init; } = new();
    public List<FbdWire> Wires { get; init; } = new();
    public List<FbdAttachment> Attachments { get; init; } = new();

    /// <summary>Element lookup by ID, filled by the linker.</summary>
    [JsonIgnore]
    public Dictionary<int, FbdElement> ById { get; } = new();
}

/// <summary>Any element placed on a sheet.</summary>
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

/// <summary>Input wire connector (named jump): provides the value that the output connector of the same name receives, possibly on another sheet.</summary>
public sealed class FbdICon : FbdElement
{
    public string? Name { get; init; }
}

/// <summary>Output wire connector: receives a value under a name that input connectors read.</summary>
public sealed class FbdOCon : FbdElement
{
    public string? Name { get; init; }
}

/// <summary>A function block instruction with a backing tag; its pins are members of that tag (the structure type of the instruction).</summary>
public sealed class FbdBlock : FbdElement
{
    /// <summary>Instruction mnemonic, e.g. TONR, PIDE.</summary>
    public required string Type { get; init; }

    /// <summary>The backing tag.</summary>
    public string? Operand { get; init; }
    public List<string> VisiblePins { get; init; } = new();
    public bool HideDesc { get; init; }
    public string? AutotuneTag { get; init; }

    /// <summary>Array operands declared as child <c>&lt;Array Name= Operand=/&gt;</c> elements (DEDT, FGEN, MAVE, RMPS).</summary>
    public Dictionary<string, string>? Arrays { get; set; }
}

/// <summary>A tagless function (ADD, SUB, ...), available on 5x80 controllers; its result lives on its output wire only.</summary>
public sealed class FbdFunction : FbdElement
{
    public required string Type { get; init; }
}

/// <summary>An add-on instruction call; wired pins are parameters, InOut parameters are bound by text.</summary>
public sealed class FbdAddOnInstruction : FbdElement
{
    public required string Name { get; init; }

    /// <summary>The instance (backing) tag.</summary>
    public string? Operand { get; init; }
    public List<string> VisiblePins { get; init; } = new();
    public bool HideDesc { get; init; }

    /// <summary>InOut parameter bindings: parameter name to argument text.</summary>
    public List<KeyValuePair<string, string>> InOutParameters { get; init; } = new();
}

/// <summary>Jump to subroutine with input and return pins.</summary>
public sealed class FbdJsr : FbdElement
{
    public string? Routine { get; init; }
    public List<string> In { get; init; } = new();
    public List<string> Ret { get; init; } = new();
}

/// <summary>Subroutine entry marker declaring the input parameters.</summary>
public sealed class FbdSbr : FbdElement
{
    public string? Routine { get; init; }
    public List<string> In { get; init; } = new();
}

/// <summary>Subroutine return marker declaring the returned values.</summary>
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

/// <summary>A wire from an output pin to an input pin. Feedback wires close loops and carry the previous scan's value.</summary>
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

/// <summary>A text box attached to an element.</summary>
public sealed class FbdAttachment : L5xElement
{
    public int FromId { get; init; }
    public int ToId { get; init; }
}
