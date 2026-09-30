namespace Plc.Generator.CSharp;

public sealed class CSharpGeneratorOptions
{
    /// <summary>Solution and namespace name; defaults to the configuration name.</summary>
    public string? Name { get; init; }

    /// <summary>Path to <c>Plc.Runtime.CSharp.csproj</c> relative to the generated Logic project (or absolute).</summary>
    public string RuntimeProject { get; init; } = "../../../src/Plc.Runtime.CSharp/Plc.Runtime.CSharp.csproj";

    /// <summary>Path to <c>Plc.Runtime.Host.csproj</c> relative to the generated Host project (or absolute).</summary>
    public string HostProject { get; init; } = "../../../src/Plc.Runtime.Host/Plc.Runtime.Host.csproj";

    /// <summary>EtherNet/IP port the generated controller listens on; null disables the server.</summary>
    public int? EipPort { get; init; } = 44818;

    /// <summary>Web console URL for the generated host.</summary>
    public string WebUrl { get; init; } = "http://localhost:5080";

    /// <summary>Treat vendor services the runtime does not implement as generation errors instead of run-time exceptions.</summary>
    public bool StrictServices { get; init; }

    /// <summary>Services implemented by the runtime as methods of <c>LogixServices</c>. Anything else compiles to <c>Services.Unsupported</c> (which logs once or faults, per the controller options) and is listed in the report.</summary>
    public HashSet<string> ImplementedServices { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "GSV", "SSV", "MSG", "EVENT", "IOT", "UID", "UIE",
    };

    /// <summary>Emit the original rung text / ST lines as comments above the lowered statements.</summary>
    public bool SourceComments { get; init; } = true;
}
