namespace Plc.Ir;

/// <summary>The hardware tree and its data channels.</summary>
public sealed class IrIo
{
    public List<IrIoDevice> Devices { get; set; } = new();
    public List<IrIoChannel> Channels { get; set; } = new();
}

public sealed class IrIoDevice : IrNode
{
    /// <summary><c>device/&lt;name&gt;</c></summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Catalog { get; set; }
    public string? VendorName { get; set; }

    [IrRefKind("device")]
    public string? Parent { get; set; }
    public bool Inhibited { get; set; }
}

/// <summary>One data image of a device: an input, output or configuration block, typed like any variable.</summary>
public sealed class IrIoChannel : IrNode
{
    /// <summary><c>io/&lt;device&gt;/&lt;name&gt;</c></summary>
    public string Id { get; set; } = "";

    [IrRefKind("device")]
    public string Device { get; set; } = "";
    public string Name { get; set; } = "";

    [IrEnum("input", "output", "config", "inOut")]
    public string Direction { get; set; } = "input";
    public IrTypeRef Type { get; set; } = IrTypeRef.Any;
    public string? Address { get; set; }

    /// <summary>The variable that is the image of this channel in logic.</summary>
    [IrRefKind("variable")]
    public string? Variable { get; set; }
}
