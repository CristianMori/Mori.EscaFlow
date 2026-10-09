using Mori.EscaFlow.Transpiler;

namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Which class a body is being emitted into, and how variable ids are reached from there.</summary>
/// <remarks>
/// The same variable is spelled differently depending on where the code runs: a controller tag is a field of the
/// controller (<c>Run</c>) inside the controller class, <c>C.Run</c> inside a program, and reached through a cast of
/// the base <c>Controller</c> inside an add-on. The context carries that access path plus the per-body state the
/// statement emitter needs (declared temporaries, labels, loop depth).
/// </remarks>
public sealed class EmitContext
{
    /// <summary>The kind of class being emitted.</summary>
    public enum Scope { Controller, Program, FunctionBlock }

    public Scope Kind { get; init; }

    /// <summary>The POU being emitted (program or function block); null in the controller.</summary>
    public IrPou? Pou { get; init; }

    /// <summary>C# expression that reaches the controller from here (<c>this</c>, <c>C</c>, or a cast of <c>Controller</c>).</summary>
    public string ControllerAccess { get; init; } = "this";

    /// <summary>Expression that reaches the services object.</summary>
    /// <remarks>Programs reach it through their typed controller <c>C</c>; add-ons and the controller expose it directly through the runtime base class.</remarks>
    public string Services => Kind == Scope.Controller ? "Services" : Kind == Scope.Program ? "C.Services" : "Services";

    /// <summary>Expression that reaches the system status (first scan, status bits).</summary>
    public string Sys => Kind == Scope.Controller ? "Sys" : Kind == Scope.Program ? "C.Sys" : "Sys";

    /// <summary>Temporaries declared for the current body.</summary>
    public HashSet<string> Temporaries { get; } = new(StringComparer.Ordinal);

    /// <summary>Labels present in the current body (for goto emission).</summary>
    /// <remarks>A jump to a label that is not in the same body cannot be a C# <c>goto</c>; the emitter leaves a comment instead.</remarks>
    public HashSet<string> Labels { get; } = new(StringComparer.Ordinal);

    /// <summary>Loop depth, to decide whether EXIT maps to break.</summary>
    public int LoopDepth { get; set; }
}
