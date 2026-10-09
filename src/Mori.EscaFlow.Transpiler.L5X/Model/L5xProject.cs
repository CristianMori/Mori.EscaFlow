using Mori.EscaFlow.Transpiler.L5X.Diagnostics;

namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>
/// The root of a parsed L5X file: the attributes of <c>RSLogix5000Content</c>, the controller, and the diagnostics
/// and statistics of the parse. An export may target a whole controller or a single component (a program, a data
/// type, an add-on); the <see cref="Controller"/> element wraps the target either way.
/// </summary>
public sealed class L5xProject : L5xElement
{
    public string? SchemaRevision { get; init; }

    /// <summary>Studio 5000 / RSLogix version that produced the export (e.g. "33.00").</summary>
    public string? SoftwareRevision { get; init; }
    public string? TargetName { get; init; }

    /// <summary>Controller, Program, DataType, AddOnInstructionDefinition, Routine, Rung, Tag, Module ...</summary>
    public string? TargetType { get; init; }
    public string? TargetSubType { get; init; }

    /// <summary>True when a partial export also carries the context (types, tags) its target depends on.</summary>
    public bool ContainsContext { get; init; }
    public string? ExportDate { get; init; }
    public string? ExportOptions { get; init; }
    public string? Owner { get; init; }

    public required Controller Controller { get; init; }

    /// <summary>Every diagnostic of all three phases, in file order per phase.</summary>
    public DiagnosticBag Diagnostics { get; init; } = new();

    public ParseStatistics Statistics { get; init; } = new();

    /// <summary>Path the file was read from, when known (null for in-memory parses).</summary>
    public string? SourcePath { get; init; }

    public override string ToString() => $"{TargetName} ({TargetType}, v{SoftwareRevision})";
}

/// <summary>Counters and timings collected while parsing. Integration tests compare the counters against a manual survey of the sample exports.</summary>
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

    /// <summary>Number of value tree nodes read (scalars, strings, structures, arrays), the bulk of a large export.</summary>
    public long ValueNodes { get; set; }
    public int SkippedCustomProperties { get; set; }
    public long PeakWorkingSetBytes { get; set; }

    public int TotalTags => ControllerTags + ProgramTags;
}
