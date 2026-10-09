using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Aoi;

/// <summary>Input: read by the instruction; Output: written by the instruction, read by the caller; InOut: a reference parameter, the instruction operates directly on the caller's tag.</summary>
public enum AoiParameterUsage
{
    Input,
    Output,
    InOut,
}

/// <summary>
/// An add-on instruction definition: interface (parameters), local tags and its routines. Every instance of the
/// instruction in logic is backed by a tag shaped like <see cref="BackingType"/> (parameters plus local tags,
/// in declaration order), so calling the instruction is really operating on one structure instance through its
/// members; this mirrors how Logix itself implements add-on instructions.
/// <para>
/// Up to four routines give the instruction its behaviour: <see cref="Logic"/> runs on every call,
/// <see cref="Prescan"/> and <see cref="Postscan"/> run once around a program's prescan/postscan rather than per
/// call, and <see cref="EnableInFalse"/> runs instead of <see cref="Logic"/> when the instruction executes with
/// a false rung condition and <see cref="ExecuteEnableInFalse"/> is set (otherwise the instruction's outputs and
/// internal state simply hold, matching how a false ladder rung leaves outputs unaffected).
/// </para>
/// </summary>
public sealed class AddOnInstructionDefinition : L5xElement, IRoutineContainer
{
    public required string Name { get; init; }
    public string? Revision { get; init; }
    public string? RevisionExtension { get; init; }
    public string? Vendor { get; init; }
    public string? Class { get; init; }

    /// <summary>True when the <see cref="Prescan"/> routine should run during the owning program's prescan (controller mode transition to Run), in addition to its normal Logic calls.</summary>
    public bool ExecutePrescan { get; init; }

    /// <summary>True when the <see cref="Postscan"/> routine should run during the owning program's postscan (transition out of Run).</summary>
    public bool ExecutePostscan { get; init; }

    /// <summary>True when a call with a false rung condition runs <see cref="EnableInFalse"/> instead of leaving the instruction's state untouched.</summary>
    public bool ExecuteEnableInFalse { get; init; }
    public string? CreatedDate { get; init; }
    public string? CreatedBy { get; init; }
    public string? EditedDate { get; init; }
    public string? EditedBy { get; init; }
    public string? SoftwareRevision { get; init; }
    public bool? IsEncrypted { get; init; }

    public string? Description { get; set; }
    public string? RevisionNote { get; set; }
    public string? AdditionalHelpText { get; set; }
    public GenericElement? SignatureHistory { get; set; }
    public GenericElement? EncodedData { get; set; }
    public GenericElement? EncryptionInfo { get; set; }

    /// <summary>Parameters in declaration order; this order is also the member order of <see cref="BackingType"/>.</summary>
    public List<AoiParameter> Parameters { get; init; } = new();
    public TagCollection LocalTags { get; init; } = new();
    public List<Routine> Routines { get; init; } = new();

    /// <summary>Source-protected routines exported as <c>&lt;EncodedData&gt;</c> blobs.</summary>
    public List<GenericElement> EncodedRoutines { get; init; } = new();
    IReadOnlyList<GenericElement> IRoutineContainer.EncodedRoutines => EncodedRoutines;

    /// <summary>Structure synthesized by the linker from parameters and local tags, so AOI-typed tags can be shaped like any other structured tag.</summary>
    [JsonIgnore]
    public StructuredType? BackingType { get; set; }

    string IRoutineContainer.Name => Name;
    IReadOnlyList<Routine> IRoutineContainer.Routines => Routines;
    TagCollection IRoutineContainer.Tags => LocalTags;

    public Routine? Logic => FindRoutine("Logic");
    public Routine? Prescan => FindRoutine("Prescan");
    public Routine? Postscan => FindRoutine("Postscan");
    public Routine? EnableInFalse => FindRoutine("EnableInFalse");

    public Routine? FindRoutine(string name) => Routines.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    public AoiParameter? FindParameter(string name) => Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Name} v{Revision}";
}

/// <summary>A parameter of an add-on instruction: a <see cref="Tag"/> with its calling convention (<see cref="ParameterUsage"/>) and its visibility in the instruction's face.</summary>
public sealed class AoiParameter : Tag
{
    public AoiParameterUsage ParameterUsage { get; init; }

    /// <summary>True when a caller must supply this parameter explicitly (no default); applies to Input/Output parameters.</summary>
    public bool Required { get; init; }

    /// <summary>True when the parameter appears on the instruction's visible face rather than being hidden (back-end-only) state.</summary>
    public bool Visible { get; init; }
}
