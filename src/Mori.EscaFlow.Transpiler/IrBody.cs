using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>
/// The executable content of a POU, action, method or property accessor. <see cref="Statements"/> is always the
/// executable form (structured statements), whatever the source language; graphical sources additionally keep
/// their drawing in <see cref="Diagram"/>. SFC bodies have no statements: the chart is executed by the runtime.
/// </summary>
public sealed class IrBody : IrNode
{
    [IrEnum("st", "ld", "fbd", "cfc", "il", "sfc", "graph", "encrypted", "empty")]
    public string Language { get; set; } = "st";

    public List<IrStatement> Statements { get; set; } = new();

    /// <summary>Temporaries introduced by lowering (rung conditions shared by several outputs, dataflow intermediates); referenced with root kind <c>temp</c>.</summary>
    public List<IrTemporary>? Temporaries { get; set; }

    public IrDiagram? Diagram { get; set; }

    /// <summary>Original source lines, when kept.</summary>
    public List<string>? Text { get; set; }
    public List<IrComment>? Comments { get; set; }

    /// <summary>True when the front-end synthesized this body rather than translating source.</summary>
    public bool Synthetic { get; set; }
}

public sealed class IrTemporary
{
    public string Name { get; set; } = "";
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");
}

public sealed class IrComment
{
    public string Text { get; set; } = "";
    public int Line { get; set; }
}

/// <summary>A graphical body as drawn.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrLdDiagram), "ld")]
[JsonDerivedType(typeof(IrNetDiagram), "net")]
[JsonDerivedType(typeof(IrSfcDiagram), "sfc")]
public abstract class IrDiagram : IrNode
{
}
