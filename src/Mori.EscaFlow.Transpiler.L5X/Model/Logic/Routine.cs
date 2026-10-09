using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Logic;

/// <summary>The four Logix routine languages: relay ladder logic, structured text, sequential function chart, function block diagram.</summary>
public enum RoutineType
{
    Unknown,
    RLL,
    ST,
    SFC,
    FBD,
}

/// <summary>
/// A routine of a program or add-on instruction. Routines of a program share its scope (program tags plus
/// controller tags) and run only when called: the main routine from the task, the fault routine on a program
/// fault, the others through JSR; an add-on's Logic routine runs on every call of the instruction.
/// </summary>
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

    /// <summary>Set for source-protected routines, whose body is only present as an encrypted payload.</summary>
    public GenericElement? EncryptedContent { get; set; }

    /// <summary>The program or add-on definition that owns the routine (set by the readers).</summary>
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

/// <summary>A routine body in one of the four languages.</summary>
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

/// <summary>A ladder routine: its rungs in execution order.</summary>
public sealed class RllContent : RoutineContent
{
    public List<Rung> Rungs { get; init; } = new();
}

/// <summary>
/// One rung. The controller evaluates rungs top to bottom; within a rung the condition flows left to right through
/// the instructions of the neutral text, which is the only representation the export carries (no graphics).
/// </summary>
public sealed class Rung : L5xElement
{
    public int Number { get; init; }

    /// <summary>N (normal), I, D, R, IR, rR, rI, rN, e, er, d: the online-edit state (insert, delete, replace and their accepted forms).</summary>
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

/// <summary>A structured text routine: its lines (statements may span lines) and the parsed block.</summary>
public sealed class StContent : RoutineContent
{
    public List<StLine> Lines { get; init; } = new();

    /// <summary>Parsed form; null until the logic phase runs.</summary>
    public StBlock? Ast { get; set; }

    /// <summary>Comments found while parsing, kept for transpilers that preserve documentation.</summary>
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
