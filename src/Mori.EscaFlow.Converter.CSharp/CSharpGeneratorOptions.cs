namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Settings of the C# converter: where the generated solution finds the runtime, how the host is configured, and how strictly unsupported services are treated.</summary>
public sealed class CSharpGeneratorOptions
{
    /// <summary>Solution and namespace name; defaults to the configuration name.</summary>
    public string? Name { get; init; }

    /// <summary>Path to <c>Mori.EscaFlow.Runtime.CSharp.csproj</c> relative to the generated Logic project (or absolute).</summary>
    /// <remarks>The default assumes the output lives in <c>out/&lt;Name&gt;</c> inside this repository.</remarks>
    public string RuntimeProject { get; init; } = "../../../src/Mori.EscaFlow.Runtime.CSharp/Mori.EscaFlow.Runtime.CSharp.csproj";

    /// <summary>Path to <c>Mori.EscaFlow.Runtime.Host.csproj</c> relative to the generated Host project (or absolute).</summary>
    public string HostProject { get; init; } = "../../../src/Mori.EscaFlow.Runtime.Host/Mori.EscaFlow.Runtime.Host.csproj";

    /// <summary>EtherNet/IP port the generated controller listens on; null disables the server.</summary>
    public int? EipPort { get; init; } = 44818;

    /// <summary>Web console URL for the generated host.</summary>
    public string WebUrl { get; init; } = "http://localhost:5080";

    /// <summary>Treat vendor services the runtime does not implement as generation errors instead of run-time exceptions.</summary>
    public bool StrictServices { get; init; }

    /// <summary>Services implemented by the runtime as methods of <c>LogixServices</c>. Anything else compiles to <c>Services.Unsupported</c> (which logs once or faults, per the controller options) and is listed in the report.</summary>
    /// <remarks>The list must match the methods the runtime's <c>LogixServices</c> class actually defines, because the emitted call is a direct method call.</remarks>
    public HashSet<string> ImplementedServices { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "GSV", "SSV", "MSG", "EVENT", "IOT", "UID", "UIE",
    };

    /// <summary>Emit the original rung text / ST lines as comments above the lowered statements.</summary>
    /// <remarks>The comments make the generated code reviewable against the ladder; turning them off halves the size of large programs.</remarks>
    public bool SourceComments { get; init; } = true;
}
