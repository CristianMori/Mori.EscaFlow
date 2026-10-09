using System.Net;
using EthernetIPSharp.Cip;
using EthernetIPSharp.Logix;
using EthernetIPSharp.Protocol;

namespace Mori.EscaFlow.Runtime;

/// <summary>What the tag server presents to EtherNet/IP clients: the listening endpoint and the CIP Identity object.</summary>
/// <remarks>The defaults describe a ControlLogix-class device (device type 0x0E, revision 33.1) because some clients check the identity before they attempt Logix tag services.</remarks>
public sealed class EipServerOptions
{
    /// <summary>Address to listen on.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>TCP port (44818 is the EtherNet/IP standard).</summary>
    public int Port { get; init; } = EipAdapter.DefaultPort;

    /// <summary>CIP vendor id.</summary>
    public ushort VendorId { get; init; } = 1;

    /// <summary>CIP device type (0x0E = programmable logic controller).</summary>
    public ushort DeviceType { get; init; } = 0x0E;

    /// <summary>CIP product code.</summary>
    public ushort ProductCode { get; init; } = 0x5C;

    /// <summary>Firmware major revision reported.</summary>
    public byte MajorRevision { get; init; } = 33;

    /// <summary>Firmware minor revision reported.</summary>
    public byte MinorRevision { get; init; } = 1;

    /// <summary>Serial number reported.</summary>
    public uint SerialNumber { get; init; } = 0x00C0FFEE;

    /// <summary>Product name reported.</summary>
    public string ProductName { get; init; } = "Mori.EscaFlow.Runtime controller";
}

/// <summary>
/// Hosts a <see cref="TagDatabase"/> over EtherNet/IP: a <see cref="LogixDispatcher"/> (Symbol and Template objects,
/// Message Router with Multiple Service Packet, Connection Manager with Unconnected Send, Identity and Program Name
/// objects) behind an <see cref="EipAdapter"/> listening on TCP. Clients such as Studio 5000, pycomm3 or
/// <c>TagClient</c> browse, read and write the controller's tags exactly as on a 1756.
/// </summary>
/// <remarks>
/// The server reads and writes the same tag buffers the scan works on, so no copying or synchronisation layer is
/// needed; the consistency clients get is that of a real controller (atomic scalars, structures that may tear).
/// Only explicit messaging over TCP is served; Class 1 implicit I/O (UDP) is outside the scope of a tag server.
/// </remarks>
public sealed class EipServer : IAsyncDisposable
{
    private readonly EipAdapter _adapter;

    /// <summary>Prepares the server over <paramref name="database"/>; nothing listens until <see cref="StartAsync"/>.</summary>
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
            // 0x0060: owned, configured, in the "at least one I/O connection established" state clients expect of a controller.
            Status = 0x0060,
        };
        Dispatcher = new LogixDispatcher(database, Identity);
        _adapter = new EipAdapter(Dispatcher, Identity);
    }

    /// <summary>The options the server was built with.</summary>
    public EipServerOptions Options { get; }

    /// <summary>The CIP Identity object served.</summary>
    public IdentityInfo Identity { get; }

    /// <summary>The Logix service dispatcher (tag read/write, template and symbol browsing).</summary>
    public LogixDispatcher Dispatcher { get; }

    /// <summary>True once the listener is up.</summary>
    public bool IsListening { get; private set; }

    /// <summary>Starts listening (idempotent).</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsListening) return;
        await _adapter.ListenAsync(Options.BindAddress, Options.Port, ct);
        IsListening = true;
    }

    /// <summary>Closes the listener and every client session.</summary>
    public async ValueTask DisposeAsync()
    {
        IsListening = false;
        await _adapter.DisposeAsync();
    }
}
