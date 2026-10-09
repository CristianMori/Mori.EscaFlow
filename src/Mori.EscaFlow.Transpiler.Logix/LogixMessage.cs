namespace Mori.EscaFlow.Transpiler.Logix;

/// <summary>Configuration of a MESSAGE tag (the MSG instruction reads it at run time), kept in <c>vendor.logix.message</c> of the variable.</summary>
public sealed class LogixMessage
{
    public string MessageType { get; set; } = "Unconfigured";
    public int? RequestedLength { get; set; }
    public int? ConnectedFlag { get; set; }
    public string? ConnectionPath { get; set; }
    public string[] ConnectionPathSegments { get; set; } = Array.Empty<string>();
    public int? CommTypeCode { get; set; }
    public int? ServiceCode { get; set; }
    public int? ObjectType { get; set; }
    public int? TargetObject { get; set; }
    public int? AttributeNumber { get; set; }
    public string? LocalElement { get; set; }

    /// <summary>Variable id of the local element tag.</summary>
    public string? LocalElementVariable { get; set; }
    public string? RemoteElement { get; set; }
    public string? DestinationTag { get; set; }

    /// <summary>Variable id of the destination tag.</summary>
    public string? DestinationVariable { get; set; }
    public bool? CacheConnections { get; set; }
    public bool? LargePacketUsage { get; set; }

    /// <summary>
    /// For CIP Generic messages to the Socket Object (class 16#0342): createSocket | openConnection | readSocket |
    /// writeSocket | deleteSocket | acceptConnection | deleteAllSockets | joinMulticastAddress | dropMulticastAddress | clearLog | unknown.
    /// The socket instance lives in <see cref="TargetObject"/> and is normally rewritten at run time through the tag's .Instance member.
    /// </summary>
    public string? SocketService { get; set; }

    /// <summary>Structural socket binding for socket services: typed references to the request structure and its fields (paths, never values).</summary>
    public LogixSocket? Socket { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
}

/// <summary>
/// The generated code implements the Socket Object services at run time; this tells it where to read the parameters.
/// All members are references into variables, so their current values are whatever the logic last wrote.
/// </summary>
public sealed class LogixSocket
{
    /// <summary>Variable id of the tag holding the request structures; every service of one socket shares it.</summary>
    public string? Session { get; set; }

    /// <summary>The request structure sent by the message (typed reference).</summary>
    public IrRef? Request { get; set; }

    /// <summary>The reply structure, when configured.</summary>
    public IrRef? Response { get; set; }

    /// <summary>The message tag's Instance member: the socket instance, written by logic after CreateSocket.</summary>
    public IrRef? Instance { get; set; }

    /// <summary>
    /// Field references by role. CreateSocket: type (1 = TCP stream, 2 = UDP datagram), timeout, localFamily, localPort,
    /// localAddress or localAddressOctet1..4. OpenConnection: timeout, destination ("host?port=n" or "?port=n&amp;address=a.b.c.d").
    /// ReadSocket: timeout, bufferLength. WriteSocket: timeout, buffer, toPort, toAddressOctet1..4 / toAddress. Only fields present in the structure appear.
    /// </summary>
    public Dictionary<string, IrRef> Fields { get; set; } = new();
}
