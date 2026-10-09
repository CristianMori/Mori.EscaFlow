namespace Mori.EscaFlow.Transpiler;

/// <summary>A global variable list (IEC VAR_GLOBAL block). Variable ids are <c>global/&lt;list&gt;/&lt;name&gt;</c>.</summary>
public sealed class IrGlobalVariableList : IrNode
{
    public string Name { get; set; } = "";
    public List<IrVariable> Vars { get; set; } = new();
}

/// <summary>A variable declaration in a global list, a POU or a method.</summary>
public sealed class IrVariable : IrNode
{
    /// <summary><c>global/&lt;list&gt;/&lt;name&gt;</c>, <c>pou/&lt;pou&gt;/&lt;name&gt;</c> or <c>pou/&lt;pou&gt;/&lt;method&gt;/&lt;name&gt;</c>.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>IEC section: input (VAR_INPUT), output (VAR_OUTPUT), inOut (VAR_IN_OUT), local (VAR), temp (VAR_TEMP), static (VAR_STAT), global (VAR_GLOBAL), external (VAR_EXTERNAL).</summary>
    [IrEnum("input", "output", "inOut", "local", "temp", "static", "global", "external")]
    public string Section { get; set; } = "local";

    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");

    [IrEnum("none", "retain", "persistent")]
    public string Retain { get; set; } = "none";
    public bool Constant { get; set; }

    /// <summary>Initial value tree; omitted when values were not exported.</summary>
    public IrValue? Initial { get; set; }

    /// <summary>Direct address (<c>%IX0.0</c>, <c>%DB1.DBX0.0</c>) when the source declares one.</summary>
    public string? Address { get; set; }

    /// <summary>The I/O channel this variable is the image of, when it is one.</summary>
    [IrRefKind("channel")]
    public string? Io { get; set; }

    /// <summary>For alias variables: the resolved target; every use of the alias in logic is already expanded to the target.</summary>
    public IrAlias? Alias { get; set; }

    /// <summary>Per-member / per-element documentation keyed by operand specifier (".Member", "[3]").</summary>
    public Dictionary<string, string>? Comments { get; set; }

    /// <summary>Free-form source attributes with no neutral meaning but worth keeping visible (engineering units, min/max, state names).</summary>
    public Dictionary<string, string>? Attributes { get; set; }
}

public sealed class IrAlias
{
    /// <summary>The target as written in the source.</summary>
    public string Text { get; set; } = "";
    public IrRef? Target { get; set; }
}
