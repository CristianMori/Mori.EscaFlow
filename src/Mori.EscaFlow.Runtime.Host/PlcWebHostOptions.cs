namespace Mori.EscaFlow.Runtime.Host;

/// <summary>Settings of the web console host.</summary>
public sealed class PlcWebHostOptions
{
    /// <summary>Listening URL of the web console.</summary>
    public string Url { get; set; } = "http://localhost:5080";

    /// <summary>Put the controller in Run as soon as the host is up.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Folder for server-side snapshots.</summary>
    /// <remarks>Relative paths resolve against the working directory of the host process; the folder is created on first use.</remarks>
    public string SnapshotDirectory { get; set; } = "./snapshots";

    /// <summary>Largest page of tags one request may return.</summary>
    public int MaxPageSize { get; set; } = 500;
}
