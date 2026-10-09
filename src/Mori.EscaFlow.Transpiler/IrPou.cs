namespace Mori.EscaFlow.Transpiler;

/// <summary>A program organisation unit.</summary>
/// <remarks>
/// <para>
/// IEC 61131-3 has three executable POU kinds plus interfaces: programs (instantiated under tasks), function
/// blocks (instantiated as variables, keep state between calls) and functions (stateless, return a value).
/// Logix maps onto them directly: a program is a program POU, an add-on instruction is a function block, and
/// the controller's routines become actions of their program. Keeping the IEC vocabulary is what lets a
/// converter use one class model for every vendor.
/// </para>
/// <para>
/// A Logix program's <see cref="Body"/> is synthetic (it calls the main routine); its routines are
/// <see cref="Actions"/>. An add-on instruction's Logic routine is the <see cref="Body"/>; its Prescan, Postscan
/// and EnableInFalse routines are actions with the corresponding <see cref="IrAction.Hook"/> when the
/// definition enables them, so the runtime knows which to call itself.
/// </para>
/// </remarks>
public sealed class IrPou : IrNode
{
    /// <summary>POU name (also the instance type name of a function block).</summary>
    public string Name { get; set; } = "";

    /// <summary>program | functionBlock | function | interface</summary>
    [IrEnum("program", "functionBlock", "function", "interface")]
    public string Kind { get; set; } = "program";

    /// <summary>Return type of a function.</summary>
    public IrTypeRef? ReturnType { get; set; }

    /// <summary>Declared variables in declaration order, every section.</summary>
    public List<IrVariable> Vars { get; set; } = new();

    /// <summary>The main body. Null for interfaces and external POUs.</summary>
    public IrBody? Body { get; set; }

    /// <summary>Named actions sharing the POU scope (IEC ACTION; Logix routines and add-on scan-mode routines).</summary>
    public List<IrAction> Actions { get; set; } = new();

    /// <summary>Methods, for object-oriented sources (TwinCAT, Codesys); empty for Logix.</summary>
    public List<IrMethod> Methods { get; set; } = new();

    /// <summary>Properties, for object-oriented sources.</summary>
    public List<IrProperty> Properties { get; set; } = new();

    /// <summary>Base function block, for object-oriented sources.</summary>
    [IrRefKind("pou")]
    public string? Extends { get; set; }

    /// <summary>Implemented interfaces, for object-oriented sources.</summary>
    [IrRefKind("pou")]
    public List<string>? Implements { get; set; }

    /// <summary>Action run when the program faults, when the platform has one.</summary>
    /// <remarks>Logix: the program's fault routine, run by the controller when an instruction in the program faults, before the controller decides whether the fault is cleared.</remarks>
    [IrRefKind("action")]
    public string? OnFault { get; set; }

    /// <summary>Set when the body is not in the project (library or protected POU).</summary>
    public IrExternal? External { get; set; }

    /// <summary>The action named <paramref name="name"/>, case-insensitively, or null.</summary>
    public IrAction? FindAction(string name) => Actions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The variable named <paramref name="name"/>, case-insensitively, or null.</summary>
    public IrVariable? FindVar(string name) => Vars.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Where an external POU's implementation lives.</summary>
public sealed class IrExternal
{
    /// <summary>Library name.</summary>
    public string? Library { get; set; }

    /// <summary>Library version.</summary>
    public string? Version { get; set; }
}

/// <summary>A named body sharing its POU's scope (IEC ACTION).</summary>
/// <remarks>
/// Actions are called from the POU's logic (Logix JSR) or by the runtime as life-cycle hooks. The hooks encode
/// Logix scan semantics in vendor-neutral terms: <c>init</c> is prescan, the pass the controller makes over every
/// routine when entering Run so outputs are cleared and instructions are initialised before the first real scan;
/// <c>enableFalse</c> is the add-on routine executed instead of the logic when the instruction's rung is false;
/// <c>postscan</c> is the pass made over an SFC action when its step is left. A converter runs each hook at the
/// matching moment of its own engine.
/// </remarks>
public sealed class IrAction : IrNode
{
    /// <summary>Action name (the Logix routine name).</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Life-cycle hook when the runtime calls this action itself: init (first scan / prescan), exit (leaving run),
    /// enableFalse (function block called with EN false), postscan (SFC postscan). Null for ordinary actions called from logic.
    /// </summary>
    [IrEnum("init", "exit", "enableFalse", "postscan")]
    public string? Hook { get; set; }

    /// <summary>True when the front-end synthesized the body (e.g. prescan behaviour derived from the ladder).</summary>
    public bool Synthetic { get; set; }

    /// <summary>The action's body.</summary>
    public IrBody Body { get; set; } = new();

    /// <summary>Actions this one calls (within the same POU), for call-graph consumers.</summary>
    public List<string>? Calls { get; set; }
}

/// <summary>A method of a function block, for object-oriented sources.</summary>
public sealed class IrMethod : IrNode
{
    /// <summary>Method name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Return type, null for procedures.</summary>
    public IrTypeRef? ReturnType { get; set; }

    /// <summary>Access modifier as the source declares it.</summary>
    [IrEnum("public", "private", "protected", "internal")]
    public string? Access { get; set; }

    /// <summary>Parameters and locals (ids <c>pou/&lt;pou&gt;/&lt;method&gt;/&lt;name&gt;</c>).</summary>
    public List<IrVariable> Vars { get; set; } = new();

    /// <summary>The method body.</summary>
    public IrBody Body { get; set; } = new();
}

/// <summary>A property of a function block, for object-oriented sources.</summary>
public sealed class IrProperty : IrNode
{
    /// <summary>Property name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Property type.</summary>
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");

    /// <summary>Getter body, when present.</summary>
    public IrBody? Get { get; set; }

    /// <summary>Setter body, when present.</summary>
    public IrBody? Set { get; set; }
}
