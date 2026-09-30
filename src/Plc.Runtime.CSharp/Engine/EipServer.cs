using System.Net;
using EthernetIPSharp.Cip;
using EthernetIPSharp.Logix;
using EthernetIPSharp.Protocol;

namespace Plc.Runtime;

public sealed class EipServerOptions
{
    public IPAddress BindAddress { get; init; } = IPAddress.Any;
    public int Port { get; init; } = EipAdapter.DefaultPort;
    public ushort VendorId { get; init; } = 1;
    public ushort DeviceType { get; init; } = 0x0E;
    public ushort ProductCode { get; init; } = 0x5C;
    public byte MajorRevision { get; init; } = 33;
    public byte MinorRevision { get; init; } = 1;
    public uint SerialNumber { get; init; } = 0x00C0FFEE;
    public string ProductName { get; init; } = "Plc.Runtime controller";
}

/// <summary>
/// Hosts a <see cref="TagDatabase"/> over EtherNet/IP: a <see cref="LogixDispatcher"/> (Symbol and Template objects,
/// Message Router with Multiple Service Packet, Connection Manager with Unconnected Send, Identity and Program Name
/// objects) behind an <see cref="EipAdapter"/> listening on TCP. Clients such as Studio 5000, pycomm3 or
/// <c>TagClient</c> browse, read and write the controller's tags exactly as on a 1756.
/// </summary>
public sealed class EipServer : IAsyncDisposable
{
    private readonly EipAdapter _adapter;

    public EipServer(TagDatabase database, EipServerOptions? options = null)
    {
        Options = options ?? new EipServerOptions();
        Identity = new IdentityInfo
        {
            VendorId = Options.VendorId,
            DeviceType = Options.DeviceType,
            ProductCode = Options.ProductCode,
            MajorRevision = Options.MajorRevision,
            MinorRevision = Options.MinorRevision,
            SerialNumber = Options.SerialNumber,
            ProductName = Options.ProductName,
            Status = 0x0060,
        };
        Dispatcher = new LogixDispatcher(database, Identity);
        _adapter = new EipAdapter(Dispatcher, Identity);
    }

    public EipServerOptions Options { get; }
    public IdentityInfo Identity { get; }
    public LogixDispatcher Dispatcher { get; }
    public bool IsListening { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsListening) return;
        await _adapter.ListenAsync(Options.BindAddress, Options.Port, ct);
        IsListening = true;
    }

    public async ValueTask DisposeAsync()
    {
        IsListening = false;
        await _adapter.DisposeAsync();
    }
}
