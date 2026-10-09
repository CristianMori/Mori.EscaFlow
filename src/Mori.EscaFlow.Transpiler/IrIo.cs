namespace Mori.EscaFlow.Transpiler;

/// <summary>The hardware tree and its data channels.</summary>
/// <remarks>
/// I/O is modelled in two layers so converters can treat it like any other data: devices describe the hardware
/// tree (for documentation and for a future binding to real I/O), channels describe the data images a device
/// exchanges (input, output, configuration) and point at the variable that is that image in logic. Logic never
/// references a channel directly; it references the variable, and the variable's <see cref="IrVariable.Io"/>
/// says which channel it mirrors.
/// </remarks>
public sealed class IrIo
{
    /// <summary>Devices (modules, adapters, drives) of the hardware tree.</summary>
    public List<IrIoDevice> Devices { get; set; } = new();

    /// <summary>Data images exchanged with the devices.</summary>
    public List<IrIoChannel> Channels { get; set; } = new();
}

/// <summary>A node of the hardware tree.</summary>
public sealed class IrIoDevice : IrNode
{
    /// <summary><c>device/&lt;name&gt;</c></summary>
    public string Id { get; set; } = "";

    /// <summary>Device name as the project calls it (the Logix module name).</summary>
    public string Name { get; set; } = "";

    /// <summary>Catalog number (for example <c>1756-IB16</c>).</summary>
    public string? Catalog { get; set; }

    /// <summary>Manufacturer name or id as the vendor records it.</summary>
    public string? VendorName { get; set; }

    /// <summary>The device this one is attached to (chassis, adapter, port owner); null for the root.</summary>
    [IrRefKind("device")]
    public string? Parent { get; set; }

    /// <summary>True when the device is inhibited (configured but not communicating) in the project.</summary>
    public bool Inhibited { get; set; }
}

/// <summary>One data image of a device: an input, output or configuration block, typed like any variable.</summary>
public sealed class IrIoChannel : IrNode
{
    /// <summary><c>io/&lt;device&gt;/&lt;name&gt;</c></summary>
    public string Id { get; set; } = "";

    /// <summary>The device the image belongs to.</summary>
    [IrRefKind("device")]
    public string Device { get; set; } = "";

    /// <summary>Image name within the device (Logix: <c>I</c>, <c>O</c>, <c>C</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Direction of the data as seen from the controller: input is read from the device, output written to it, config sent once, inOut both ways.</summary>
    [IrEnum("input", "output", "config", "inOut")]
    public string Direction { get; set; } = "input";

    /// <summary>Type of the image (a structure of class <c>io</c> for Logix). <c>any</c> when the export did not carry the layout.</summary>
    public IrTypeRef Type { get; set; } = IrTypeRef.Any;

    /// <summary>Direct address of the image when the vendor uses addresses (<c>%IW0</c>) rather than named images.</summary>
    public string? Address { get; set; }

    /// <summary>The variable that is the image of this channel in logic.</summary>
    [IrRefKind("variable")]
    public string? Variable { get; set; }
}
