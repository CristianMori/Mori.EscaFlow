namespace Mori.EscaFlow.Transpiler.Catalogs;

/// <summary>One positional parameter of a vendor service.</summary>
public sealed class ServiceParam
{
    /// <summary>Parameter name as the vendor manual calls it (<c>argN</c> when unknown).</summary>
    public string Name { get; set; } = "";

    /// <summary>ref: a variable reference (read); out: a variable reference written by the service; inOut; keyword: an enumeration keyword; action: an action name; label: a jump label; expression: any value; literal; any: unchecked.</summary>
    public string Role { get; set; } = "any";

    /// <summary>Expected type name when fixed (e.g. MESSAGE, TIMER, DINT); null when any.</summary>
    public string? Type { get; set; }

    /// <summary>True when the argument may be elided.</summary>
    public bool Optional { get; set; }
}

/// <summary>A vendor runtime service callable through <c>sysCall</c> / <c>sysFn</c>.</summary>
/// <remarks>
/// A service catalog is how the format stays neutral without losing the vendor's instruction set: the document
/// only names the service and lists arguments in catalog order; the catalog carries what the validator needs
/// (arity, roles) and what a converter needs to bind ladder (the enable rule). Runtime behaviour lives in the
/// converter's implementation of the catalog, one runtime interface per catalog id.
/// </remarks>
public sealed class ServiceDefinition
{
    /// <summary>Service name (the Logix mnemonic).</summary>
    public string Name { get; set; } = "";

    /// <summary>Catalog group: system, messaging, control, file, string, ascii, phase, motion, safety, timer, ...</summary>
    public string Group { get; set; } = "";

    /// <summary>Short description, when the catalog has one.</summary>
    public string? Description { get; set; }

    /// <summary>Positional parameters.</summary>
    public List<ServiceParam> Params { get; set; } = new();

    /// <summary>True when the last parameter repeats.</summary>
    public bool Variadic { get; set; }

    /// <summary>How ladder binds the rung condition: input (the service runs every scan and reads the condition) or gate (runs only when true).</summary>
    /// <remarks>Every-scan services (timers, PID, alarms, MSG) keep state and must observe a false condition to reset it; gated services (MOV, GSV) simply do nothing when the rung is false.</remarks>
    public string Enable { get; set; } = "gate";

    /// <summary>True when the service yields a value usable in expressions (<c>sysFn</c>).</summary>
    public bool IsFunction { get; set; }

    /// <summary>Result type for functions.</summary>
    public string? ResultType { get; set; }
}

/// <summary>A vendor's service catalog. Registered with the validator so <c>sysCall</c> nodes can be checked; implemented by the vendor packages (Mori.EscaFlow.Transpiler.Logix, ...).</summary>
/// <remarks>Defined in the core rather than in a vendor package so the validator can check any catalog without depending on every vendor package.</remarks>
public interface IServiceCatalog
{
    /// <summary>Catalog id as used in <c>sysCall.catalog</c> (logix, s7, twincat).</summary>
    string Id { get; }

    /// <summary>Every service of the catalog.</summary>
    IReadOnlyList<ServiceDefinition> Services { get; }

    /// <summary>The service named <paramref name="name"/> (case-insensitive), or null.</summary>
    ServiceDefinition? Get(string name);
}
