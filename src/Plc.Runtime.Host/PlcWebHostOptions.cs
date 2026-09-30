namespace Plc.Runtime.Host;

public sealed class PlcWebHostOptions
{
    /// <summary>Listening URL of the web console.</summary>
    public string Url { get; set; } = "http://localhost:5080";

    /// <summary>Put the controller in Run as soon as the host is up.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Folder for server-side snapshots.</summary>
    public string SnapshotDirectory { get; set; } = "./snapshots";

    /// <summary>Largest page of tags one request may return.</summary>
    public int MaxPageSize { get; set; } = 500;
}
