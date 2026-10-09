namespace Mori.EscaFlow.Transpiler;

/// <summary>A global variable list (IEC VAR_GLOBAL block). Variable ids are <c>global/&lt;list&gt;/&lt;name&gt;</c>.</summary>
/// <remarks>Logix has no global lists; the importer creates one per controller (controller-scoped tags) and a synthetic <c>IO</c> list for module images, so controller tags and device images are ordinary globals to a converter.</remarks>
public sealed class IrGlobalVariableList : IrNode
{
    /// <summary>List name (part of the ids of its variables).</summary>
    public string Name { get; set; } = "";

    /// <summary>Variables in declaration order.</summary>
    public List<IrVariable> Vars { get; set; } = new();
}

/// <summary>A variable declaration in a global list, a POU or a method.</summary>
/// <remarks>
/// <para>
/// The id is the handle every reference uses; it encodes the scope so a converter can place the variable
/// (global list, program, function block, method) without consulting the declaring container. Names are kept
/// exactly as written; converters apply their own identifier rules and record changes in a naming tally.
/// </para>
/// <para>
/// Alias variables (a Logix alias tag) keep their declaration here, but every use in logic is already rewritten
/// to the target by the front-end: the reference carries the target's id and notes the alias id. A converter
/// therefore emits no alias storage, only the tally, which is what the backend rules ask for.
/// </para>
/// </remarks>
public sealed class IrVariable : IrNode
{
    /// <summary><c>global/&lt;list&gt;/&lt;name&gt;</c>, <c>pou/&lt;pou&gt;/&lt;name&gt;</c> or <c>pou/&lt;pou&gt;/&lt;method&gt;/&lt;name&gt;</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Name as written in the source.</summary>
    public string Name { get; set; } = "";

    /// <summary>IEC section: input (VAR_INPUT), output (VAR_OUTPUT), inOut (VAR_IN_OUT), local (VAR), temp (VAR_TEMP), static (VAR_STAT), global (VAR_GLOBAL), external (VAR_EXTERNAL).</summary>
    /// <remarks>For a Logix add-on instruction, parameters are input / output / inOut (InOut parameters are passed by reference and are not part of the instance's backing data) and local tags are local.</remarks>
    [IrEnum("input", "output", "inOut", "local", "temp", "static", "global", "external")]
    public string Section { get; set; } = "local";

    /// <summary>Declared type (atomic, structure, function block instance type, array, string).</summary>
    public IrTypeRef Type { get; set; } = IrTypeRef.Named("BOOL");

    /// <summary>none: initialised at every start; retain: survives a power cycle (every Logix tag); persistent: survives a download as well.</summary>
    [IrEnum("none", "retain", "persistent")]
    public string Retain { get; set; } = "none";

    /// <summary>True for a constant (read-only in logic).</summary>
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

/// <summary>The target of an alias variable.</summary>
public sealed class IrAlias
{
    /// <summary>The target as written in the source (for example <c>M1.Fault</c>).</summary>
    public string Text { get; set; } = "";

    /// <summary>The resolved target reference (variable id plus path); its type is the alias's type.</summary>
    public IrRef? Target { get; set; }
}
