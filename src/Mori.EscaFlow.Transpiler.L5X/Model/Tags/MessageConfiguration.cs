using System.Globalization;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Tags;

/// <summary>
/// Structural binding of a socket-service message: which request structure it sends and where, inside that
/// structure, the fields the Socket Object interprets are located. Everything here is a path, never a value:
/// the logic may rewrite the socket type, ports, addresses and buffers at any scan, so generated code must read
/// these locations at run time. The parser does not decide the protocol.
/// </summary>
public sealed class SocketBinding
{
    /// <summary>Tag that holds the request structures of this socket (all services of one socket share it), e.g. <c>SNTP_MSG_DATA</c>.</summary>
    public string? Session { get; set; }

    /// <summary>Path of the request structure sent by the message (the MSG source element), e.g. <c>SNTP_MSG_DATA.Create_Source</c>.</summary>
    public string? Request { get; set; }

    /// <summary>Data type of the request structure.</summary>
    public string? RequestType { get; set; }

    /// <summary>Path of the reply structure (the MSG destination element), when one is configured.</summary>
    public string? Response { get; set; }

    /// <summary>Path of the message tag's <c>Instance</c> member: the socket instance the service acts on, written by logic after CreateSocket.</summary>
    public string? Instance { get; set; }

    /// <summary>
    /// Paths of the fields the Socket Object reads from the request structure, keyed by role. Roles by service:
    /// CreateSocket: <c>type</c> (1 = TCP stream, 2 = UDP datagram), <c>timeout</c>, <c>family</c>, <c>localPort</c>,
    /// <c>localAddress</c> (a packed DINT) or <c>localAddressOctet1..4</c>; OpenConnection: <c>timeout</c>, <c>destination</c>
    /// (a string "host?port=n" or "?port=n&amp;address=a.b.c.d"); ReadSocket: <c>timeout</c>, <c>bufferLength</c>;
    /// WriteSocket: <c>timeout</c>, <c>buffer</c>, and for UDP <c>toPort</c>, <c>toAddressOctet1..4</c> or <c>toAddress</c>.
    /// Only fields present in the structure are listed.
    /// </summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Services of the Logix Socket Object (class 16#0342), selected by the CIP service code.</summary>
public enum SocketService
{
    None,
    Unknown,
    CreateSocket,
    OpenConnection,
    ReadSocket,
    WriteSocket,
    DeleteSocket,
    AcceptConnection,
    DeleteAllSockets,
    JoinMulticastAddress,
    DropMulticastAddress,
    ClearLog,
}

public enum MessageCommType
{
    Unknown = -1,
    Cip = 0,
    DhPlus = 1,
    CipWithSourceId = 2,
    BlockTransferRemoteIo = 3,
    BlockTransferControlNet = 4,
}

/// <summary>
/// The configuration of a MESSAGE tag (<c>&lt;Data Format="Message"&gt;&lt;MessageParameters .../&gt;</c>), spec pp. 140–142.
/// Every attribute is also kept verbatim in <see cref="Attributes"/>.
/// </summary>
public sealed class MessageConfiguration
{
    /// <summary>CIP Generic, CIP Data Table Read/Write, Block Transfer Read/Write, PLC2/PLC3/PLC5/SLC Typed ..., SERCOS IDN Read/Write, Module Reconfigure, Unconfigured.</summary>
    public string MessageType { get; init; } = "Unconfigured";

    /// <summary>Number of elements to transfer (0...32,767).</summary>
    public int? RequestedLength { get; init; }

    /// <summary>Raw ConnectedFlag value: 1 = connected, 0 = unconnected (the exporter also writes 2).</summary>
    public int? ConnectedFlag { get; init; }

    /// <summary>Connection path text as exported, e.g. <c>2, 172.17.36.30</c> or <c>1, 3</c>.</summary>
    public string? ConnectionPath { get; init; }

    public int? CommTypeCode { get; init; }

    /// <summary>CIP Generic: service code (hex in the file, decoded here).</summary>
    public int? ServiceCode { get; init; }

    /// <summary>CIP Generic: object class (hex in the file, decoded here). Same as the Class field of the MSG dialog.</summary>
    public int? ObjectType { get; init; }

    /// <summary>CIP Generic: instance (decimal). Same as the Instance field of the MSG dialog.</summary>
    public int? TargetObject { get; init; }

    /// <summary>CIP Generic: attribute number (hex in the file, decoded here).</summary>
    public int? AttributeNumber { get; init; }

    public string? Channel { get; init; }
    public int? SourceLink { get; init; }
    public int? DestinationLink { get; init; }
    public int? DestinationNode { get; init; }
    public int? Rack { get; init; }
    public int? Group { get; init; }
    public int? Slot { get; init; }

    /// <summary>Index into the local element, typically 0.</summary>
    public int? LocalIndex { get; init; }

    /// <summary>Index into the remote element, typically 0.</summary>
    public int? RemoteIndex { get; init; }

    /// <summary>Tag in the local controller: source of a write, destination of a read (the Source/Destination Element of the MSG dialog).</summary>
    public string? LocalElement { get; init; }

    /// <summary>Address or tag name in the remote device: source of a read, destination of a write.</summary>
    public string? RemoteElement { get; init; }

    /// <summary>Destination tag for CIP Generic messages (response data).</summary>
    public string? DestinationTag { get; init; }

    public bool? CacheConnections { get; init; }
    public bool? LargePacketUsage { get; init; }

    /// <summary>Every attribute of the MessageParameters element as written.</summary>
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Attributes the model does not name individually (e.g. newer export additions).</summary>
    public Dictionary<string, string>? UnknownAttributes { get; init; }

    /// <summary>Structural socket binding for socket-service messages (paths, never values); null otherwise.</summary>
    public SocketBinding? Socket { get; set; }

    /// <summary>Resolved <see cref="LocalElement"/> (linker).</summary>
    [JsonIgnore]
    public Tag? LocalElementTag { get; set; }

    public OperandPath? LocalElementPath { get; set; }

    /// <summary>Resolved <see cref="DestinationTag"/> (linker).</summary>
    [JsonIgnore]
    public Tag? DestinationTagRef { get; set; }

    public OperandPath? DestinationTagPath { get; set; }

    [JsonIgnore]
    public MessageCommType CommType => CommTypeCode switch
    {
        0 => MessageCommType.Cip,
        1 => MessageCommType.DhPlus,
        2 => MessageCommType.CipWithSourceId,
        3 => MessageCommType.BlockTransferRemoteIo,
        4 => MessageCommType.BlockTransferControlNet,
        _ => MessageCommType.Unknown,
    };

    [JsonIgnore]
    public bool IsCipGeneric => string.Equals(MessageType, "CIP Generic", StringComparison.OrdinalIgnoreCase);

    /// <summary>CIP class of the Logix Socket Object (socket services over the Ethernet port).</summary>
    public const int SocketObjectClass = 0x342;

    /// <summary>True for CIP Generic messages addressed to the Socket Object (class 16#0342).</summary>
    [JsonIgnore]
    public bool IsSocketService => IsCipGeneric && ObjectType == SocketObjectClass;

    /// <summary>The socket service selected by the service code, when <see cref="IsSocketService"/>.</summary>
    [JsonIgnore]
    public SocketService SocketService => !IsSocketService ? SocketService.None : ServiceCode switch
    {
        0x4B => SocketService.CreateSocket,
        0x4C => SocketService.OpenConnection,
        0x4D => SocketService.ReadSocket,
        0x4E => SocketService.WriteSocket,
        0x4F => SocketService.DeleteSocket,
        0x50 => SocketService.AcceptConnection,
        0x51 => SocketService.DeleteAllSockets,
        0x52 => SocketService.JoinMulticastAddress,
        0x53 => SocketService.DropMulticastAddress,
        0x54 => SocketService.ClearLog,
        _ => SocketService.Unknown,
    };

    /// <summary>
    /// The socket instance for services after CreateSocket. Create uses instance 0; the instance returned by Create
    /// is normally written into the message tag's <c>.Instance</c> member at run time, so the exported value is only the last configured one.
    /// </summary>
    [JsonIgnore]
    public int? SocketInstance => IsSocketService ? TargetObject : null;

    [JsonIgnore]
    public bool IsConfigured => !string.Equals(MessageType, "Unconfigured", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the message is a data-table read (data flows into <see cref="LocalElement"/>).</summary>
    [JsonIgnore]
    public bool IsRead => MessageType.Contains("Read", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsWrite => MessageType.Contains("Write", StringComparison.OrdinalIgnoreCase);

    /// <summary>The connection path split on commas and trimmed, e.g. ["2", "172.17.36.30"] (port, address pairs).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ConnectionPathSegments =>
        string.IsNullOrWhiteSpace(ConnectionPath)
            ? Array.Empty<string>()
            : ConnectionPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static MessageConfiguration FromAttributes(IReadOnlyDictionary<string, string> attrs)
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "MessageType", "RequestedLength", "ConnectedFlag", "ConnectionPath", "CommTypeCode", "ServiceCode", "ObjectType",
            "TargetObject", "AttributeNumber", "Channel", "SourceLink", "DestinationLink", "DestinationNode", "Rack", "Group", "Slot",
            "LocalIndex", "RemoteIndex", "LocalElement", "RemoteElement", "DestinationTag", "CacheConnections", "LargePacketUsage",
        };
        Dictionary<string, string>? unknown = null;
        foreach (var kv in attrs)
            if (!known.Contains(kv.Key)) (unknown ??= new(StringComparer.Ordinal))[kv.Key] = kv.Value;

        string? Get(string n) => attrs.TryGetValue(n, out var v) ? v : null;

        return new MessageConfiguration
        {
            MessageType = Get("MessageType") ?? "Unconfigured",
            RequestedLength = ParseInt(Get("RequestedLength")),
            ConnectedFlag = ParseInt(Get("ConnectedFlag")),
            ConnectionPath = Get("ConnectionPath"),
            CommTypeCode = ParseInt(Get("CommTypeCode")),
            ServiceCode = ParseInt(Get("ServiceCode")),
            ObjectType = ParseInt(Get("ObjectType")),
            TargetObject = ParseInt(Get("TargetObject")),
            AttributeNumber = ParseInt(Get("AttributeNumber")),
            Channel = Get("Channel"),
            SourceLink = ParseInt(Get("SourceLink")),
            DestinationLink = ParseInt(Get("DestinationLink")),
            DestinationNode = ParseInt(Get("DestinationNode")),
            Rack = ParseInt(Get("Rack")),
            Group = ParseInt(Get("Group")),
            Slot = ParseInt(Get("Slot")),
            LocalIndex = ParseInt(Get("LocalIndex")),
            RemoteIndex = ParseInt(Get("RemoteIndex")),
            LocalElement = NullIfEmpty(Get("LocalElement")),
            RemoteElement = NullIfEmpty(Get("RemoteElement")),
            DestinationTag = NullIfEmpty(Get("DestinationTag")),
            CacheConnections = ParseBool(Get("CacheConnections")),
            LargePacketUsage = ParseBool(Get("LargePacketUsage")),
            Attributes = new Dictionary<string, string>(attrs, StringComparer.Ordinal),
            UnknownAttributes = unknown,
        };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static bool? ParseBool(string? s) => s is null ? null : s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Accepts decimal ("52"), Logix hex ("16#0033"), and plain hex with 0x.</summary>
    private static int? ParseInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (LiteralParser.TryParseNumber(s, out var lit) && lit is IntegerLiteral il) return (int)il.Value;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) return h;
        return null;
    }
}
