namespace Mori.EscaFlow.Transpiler.Catalogs;

/// <summary>One positional parameter of a vendor service.</summary>
public sealed class ServiceParam
{
    public string Name { get; set; } = "";

    /// <summary>ref: a variable reference (read); out: a variable reference written by the service; inOut; keyword: an enumeration keyword; action: an action name; label: a jump label; expression: any value; literal; any: unchecked.</summary>
    public string Role { get; set; } = "any";

    /// <summary>Expected type name when fixed (e.g. MESSAGE, TIMER, DINT); null when any.</summary>
    public string? Type { get; set; }
    public bool Optional { get; set; }
}

/// <summary>A vendor runtime service callable through <c>sysCall</c> / <c>sysFn</c>.</summary>
public sealed class ServiceDefinition
{
    public string Name { get; set; } = "";

    /// <summary>Catalog group: system, messaging, control, file, string, ascii, phase, motion, safety, timer, ...</summary>
    public string Group { get; set; } = "";
    public string? Description { get; set; }
    public List<ServiceParam> Params { get; set; } = new();

    /// <summary>True when the last parameter repeats.</summary>
    public bool Variadic { get; set; }

    /// <summary>How ladder binds the rung condition: input (the service runs every scan and reads the condition) or gate (runs only when true).</summary>
    public string Enable { get; set; } = "gate";

    /// <summary>True when the service yields a value usable in expressions (<c>sysFn</c>).</summary>
    public bool IsFunction { get; set; }

    /// <summary>Result type for functions.</summary>
    public string? ResultType { get; set; }
}

/// <summary>A vendor's service catalog. Registered with the validator so <c>sysCall</c> nodes can be checked; implemented by the vendor packages (Mori.EscaFlow.Transpiler.Logix, ...).</summary>
public interface IServiceCatalog
{
    /// <summary>Catalog id as used in <c>sysCall.catalog</c> (logix, s7, twincat).</summary>
    string Id { get; }
    IReadOnlyList<ServiceDefinition> Services { get; }
    ServiceDefinition? Get(string name);
}
