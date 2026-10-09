namespace Mori.EscaFlow.Transpiler;

/// <summary>A program organisation unit.</summary>
public sealed class IrPou : IrNode
{
    public string Name { get; set; } = "";

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
    public List<IrMethod> Methods { get; set; } = new();
    public List<IrProperty> Properties { get; set; } = new();

    [IrRefKind("pou")]
    public string? Extends { get; set; }

    [IrRefKind("pou")]
    public List<string>? Implements { get; set; }

    /// <summary>Action run when the program faults, when the platform has one.</summary>
    [IrRefKind("action")]
    public string? OnFault { get; set; }

    /// <summary>Set when the body is not in the project (library or protected POU).</summary>
    public IrExternal? External { get; set; }

    public IrAction? FindAction(string name) => Actions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    public IrVariable? FindVar(string name) => Vars.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class IrExternal
{
    public string? Library { get; set; }
    public string? Version { get; set; }
}

public sealed class IrAction : IrNode
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Life-cycle hook when the runtime calls this action itself: init (first scan / prescan), exit (leaving run),
    /// enableFalse (function block called with EN false), postscan (SFC postscan). Null for ordinary actions called from logic.
    /// </summary>
    [IrEnum("init", "exit", "enableFalse", "postscan")]
    public string? Hook { get; set; }

    /// <summary>True when the front-end synthesized the body (e.g. prescan behaviour derived from the ladder).</summary>
    public bool Synthetic { get; set; }
    public IrBody Body { get; set; } = new();

    /// <summary>Actions this one calls (within the same POU), for call-graph consumers.</summary>
    public List<string>? Calls { get; set; }
}

public sealed class IrMethod : IrNode
{
    public string Name { get; set; } = "";
    public IrTypeRef? ReturnType { get; set; }

    [IrEnum("public", "private", "protected", "internal")]
    public string? Access { get; set; }
    public List<IrVariable> Vars { get; set; } = new();
    public IrBody Body { get; set; } = new();
}

public sealed class IrProperty : IrNode
{
    public string Name { get; set; } = "";
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");
    public IrBody? Get { get; set; }
    public IrBody? Set { get; set; }
}
