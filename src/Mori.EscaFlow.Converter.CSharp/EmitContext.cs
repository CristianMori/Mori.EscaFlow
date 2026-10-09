using Mori.EscaFlow.Transpiler;

namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Which class a body is being emitted into, and how variable ids are reached from there.</summary>
public sealed class EmitContext
{
    public enum Scope { Controller, Program, FunctionBlock }

    public Scope Kind { get; init; }

    /// <summary>The POU being emitted (program or function block); null in the controller.</summary>
    public IrPou? Pou { get; init; }

    /// <summary>C# expression that reaches the controller from here (<c>this</c>, <c>C</c>, or a cast of <c>Controller</c>).</summary>
    public string ControllerAccess { get; init; } = "this";

    /// <summary>Expression that reaches the services object.</summary>
    public string Services => Kind == Scope.Controller ? "Services" : Kind == Scope.Program ? "C.Services" : "Services";
    public string Sys => Kind == Scope.Controller ? "Sys" : Kind == Scope.Program ? "C.Sys" : "Sys";

    /// <summary>Temporaries declared for the current body.</summary>
    public HashSet<string> Temporaries { get; } = new(StringComparer.Ordinal);

    /// <summary>Labels present in the current body (for goto emission).</summary>
    public HashSet<string> Labels { get; } = new(StringComparer.Ordinal);

    /// <summary>Loop depth, to decide whether EXIT maps to break.</summary>
    public int LoopDepth { get; set; }
}
