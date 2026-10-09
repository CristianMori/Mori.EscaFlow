using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// The executable content of a POU, action, method or property accessor. <see cref="Statements"/> is always the
/// executable form (structured statements), whatever the source language; graphical sources additionally keep
/// their drawing in <see cref="Diagram"/>. SFC bodies have no statements: the chart is executed by the runtime.
/// </summary>
/// <remarks>
/// <para>
/// This is the central design decision of the format: a converter targets one executable form, the statement
/// list, for ladder, structured text and FBD alike. The front-end is responsible for lowering ladder rungs and
/// dataflow sheets into statements (the contract is documented in docs/plc-ir-2.0.md). A converter that can
/// render ladder natively (a Siemens LAD target, a documentation tool) uses <see cref="Diagram"/> instead.
/// </para>
/// <para>
/// Lowering sometimes needs scratch variables: a rung condition shared by several outputs, a tagless FBD
/// function's result. They are declared in <see cref="Temporaries"/> and referenced with root kind <c>temp</c>,
/// scoped to this body, so converters emit them as locals rather than as tags.
/// </para>
/// </remarks>
public sealed class IrBody : IrNode
{
    /// <summary>Source language: st, ld, fbd, cfc, il, sfc, graph (Siemens GRAPH), encrypted (source-protected, no body available), empty.</summary>
    [IrEnum("st", "ld", "fbd", "cfc", "il", "sfc", "graph", "encrypted", "empty")]
    public string Language { get; set; } = "st";

    /// <summary>The executable statements, whatever <see cref="Language"/> says (empty for sfc, encrypted and empty bodies).</summary>
    public List<IrStatement> Statements { get; set; } = new();

    /// <summary>Temporaries introduced by lowering (rung conditions shared by several outputs, dataflow intermediates); referenced with root kind <c>temp</c>.</summary>
    public List<IrTemporary>? Temporaries { get; set; }

    /// <summary>The drawing of a graphical body (ladder rungs, FBD sheets, SFC chart), when the source was graphical.</summary>
    public IrDiagram? Diagram { get; set; }

    /// <summary>Original source lines, when kept.</summary>
    public List<string>? Text { get; set; }

    /// <summary>Source comments with their line numbers, so converters can carry documentation into the generated code.</summary>
    public List<IrComment>? Comments { get; set; }

    /// <summary>True when the front-end synthesized this body rather than translating source.</summary>
    /// <remarks>A Logix program's main body (a call of its main routine) and the prescan actions are synthetic: they encode scan behaviour that the source expresses by convention rather than by code.</remarks>
    public bool Synthetic { get; set; }
}

/// <summary>A scratch variable local to one body, created by lowering.</summary>
public sealed class IrTemporary
{
    /// <summary>Name, unique within the body (<c>_rc1</c>, <c>_mcr1</c>, <c>_f1</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Type; BOOL for rung conditions, the function's result type for dataflow intermediates.</summary>
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");
}

/// <summary>A source comment.</summary>
public sealed class IrComment
{
    /// <summary>Comment text without delimiters.</summary>
    public string Text { get; set; } = "";

    /// <summary>Zero-based line in the source body.</summary>
    public int Line { get; set; }
}

/// <summary>A graphical body as drawn.</summary>
/// <remarks>
/// Diagrams are polymorphic on <c>kind</c>: <c>ld</c> (ladder rungs), <c>net</c> (function block networks:
/// Logix FBD, TwinCAT CFC, Siemens FBD) and <c>sfc</c> (sequential function charts). Statements and values use
/// <c>kind</c> as well; expressions use <c>op</c> because an expression's discriminator reads naturally as its
/// operator and keeps the two unions visually distinct in the JSON.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrLdDiagram), "ld")]
[JsonDerivedType(typeof(IrNetDiagram), "net")]
[JsonDerivedType(typeof(IrSfcDiagram), "sfc")]
public abstract class IrDiagram : IrNode
{
}
