using System.Text.Json.Serialization;
using L5x.Model.Logic.Rll;
using L5x.Model.Logic.St;
using L5x.Model.Programs;

namespace L5x.Model.Logic;

public enum RoutineType
{
    Unknown,
    RLL,
    ST,
    SFC,
    FBD,
}

public sealed class Routine : L5xElement
{
    public required string Name { get; init; }
    public RoutineType Type { get; init; }
    public string? Description { get; set; }

    /// <summary>The routine body. Null when the routine is encrypted or empty.</summary>
    public RoutineContent? Content { get; set; }

    /// <summary>Additional content blocks carrying pending or test online edits.</summary>
    public List<RoutineContent> OnlineEditContents { get; init; } = new();

    public GenericElement? EncryptionInfo { get; set; }
    public GenericElement? EncryptedContent { get; set; }

    [JsonIgnore]
    public IRoutineContainer? Owner { get; set; }

    /// <summary>Routines this routine jumps to (JSR / FOR), resolved by the linker.</summary>
    [JsonIgnore]
    public List<Routine> CalledRoutines { get; } = new();

    [JsonIgnore]
    public List<Routine> Callers { get; } = new();

    [JsonIgnore]
    public RllContent? Rll => Content as RllContent;

    [JsonIgnore]
    public StContent? St => Content as StContent;

    [JsonIgnore]
    public Sfc.SfcContent? Sfc => Content as Sfc.SfcContent;

    [JsonIgnore]
    public Fbd.FbdContent? Fbd => Content as Fbd.FbdContent;

    public override string ToString() => $"{Name} ({Type})";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RllContent), "rll")]
[JsonDerivedType(typeof(StContent), "st")]
[JsonDerivedType(typeof(Sfc.SfcContent), "sfc")]
[JsonDerivedType(typeof(Fbd.FbdContent), "fbd")]
public abstract class RoutineContent : L5xElement
{
    /// <summary>Original / PendingEdits / TestEdits; null when the routine has no online edits.</summary>
    public string? OnlineEditType { get; init; }
}

public sealed class RllContent : RoutineContent
{
    public List<Rung> Rungs { get; init; } = new();
}

public sealed class Rung : L5xElement
{
    public int Number { get; init; }

    /// <summary>N (normal), I, D, R, IR, rR, rI, rN, e, er, d.</summary>
    public string Type { get; init; } = "N";

    public string? Comment { get; set; }

    /// <summary>The neutral text exactly as exported.</summary>
    public required string Text { get; init; }

    /// <summary>Parsed form; null until the logic phase runs.</summary>
    public RungAst? Ast { get; set; }

    public override string ToString() => $"{Number}: {Text}";
}

/// <summary>One <c>&lt;Line&gt;</c> of structured text. Number is the 0-based line number from the file.</summary>
public sealed record StLine(int Number, string Text);

public sealed class StContent : RoutineContent
{
    public List<StLine> Lines { get; init; } = new();

    /// <summary>Parsed form; null until the logic phase runs.</summary>
    public StBlock? Ast { get; set; }

    public List<StComment>? Comments { get; set; }

    /// <summary>All lines joined with newlines, in file order.</summary>
    public string FullText => string.Join("\n", Lines.Select(l => l.Text));
}

/// <summary>Structured text embedded in an SFC (action body, transition condition, step preset) or an FBD.</summary>
public sealed class StSnippet
{
    public List<StLine> Lines { get; init; } = new();

    /// <summary>Set for bodies (statement lists).</summary>
    public StBlock? Ast { get; set; }

    /// <summary>Set for conditions and presets (single expressions).</summary>
    public StExpression? Expression { get; set; }

    public List<StComment>? Comments { get; set; }

    public string FullText => string.Join("\n", Lines.Select(l => l.Text));

    public override string ToString() => FullText;
}
