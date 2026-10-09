using Mori.EscaFlow.Transpiler.L5X.Diagnostics;

namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>The root of a parsed L5X file.</summary>
public sealed class L5xProject : L5xElement
{
    public string? SchemaRevision { get; init; }
    public string? SoftwareRevision { get; init; }
    public string? TargetName { get; init; }
    public string? TargetType { get; init; }
    public string? TargetSubType { get; init; }
    public bool ContainsContext { get; init; }
    public string? ExportDate { get; init; }
    public string? ExportOptions { get; init; }
    public string? Owner { get; init; }

    public required Controller Controller { get; init; }

    public DiagnosticBag Diagnostics { get; init; } = new();

    public ParseStatistics Statistics { get; init; } = new();

    public string? SourcePath { get; init; }

    public override string ToString() => $"{TargetName} ({TargetType}, v{SoftwareRevision})";
}

/// <summary>Counters and timings collected while parsing.</summary>
public sealed class ParseStatistics
{
    public TimeSpan XmlPhase { get; set; }
    public TimeSpan LogicPhase { get; set; }
    public TimeSpan LinkPhase { get; set; }
    public TimeSpan Total { get; set; }

    public int DataTypes { get; set; }
    public int Modules { get; set; }
    public int AddOnInstructions { get; set; }
    public int ControllerTags { get; set; }
    public int ProgramTags { get; set; }
    public int AoiParameters { get; set; }
    public int AoiLocalTags { get; set; }
    public int Programs { get; set; }
    public int Routines { get; set; }
    public int RllRoutines { get; set; }
    public int StRoutines { get; set; }
    public int SfcRoutines { get; set; }
    public int FbdRoutines { get; set; }
    public int Rungs { get; set; }
    public int StLines { get; set; }
    public int SfcNodes { get; set; }
    public int Tasks { get; set; }
    public long ValueNodes { get; set; }
    public int SkippedCustomProperties { get; set; }
    public long PeakWorkingSetBytes { get; set; }

    public int TotalTags => ControllerTags + ProgramTags;
}
