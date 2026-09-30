using System.Text.Json.Serialization;
using L5x.Model.DataTypes;
using L5x.Model.Logic;
using L5x.Model.Programs;
using L5x.Model.Tags;

namespace L5x.Model.Aoi;

public enum AoiParameterUsage
{
    Input,
    Output,
    InOut,
}

/// <summary>An add-on instruction definition: interface (parameters), local tags and its routines.</summary>
public sealed class AddOnInstructionDefinition : L5xElement, IRoutineContainer
{
    public required string Name { get; init; }
    public string? Revision { get; init; }
    public string? RevisionExtension { get; init; }
    public string? Vendor { get; init; }
    public string? Class { get; init; }
    public bool ExecutePrescan { get; init; }
    public bool ExecutePostscan { get; init; }
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

    public List<AoiParameter> Parameters { get; init; } = new();
    public TagCollection LocalTags { get; init; } = new();
    public List<Routine> Routines { get; init; } = new();

    /// <summary>Source-protected routines exported as <c>&lt;EncodedData&gt;</c> blobs.</summary>
    public List<GenericElement> EncodedRoutines { get; init; } = new();
    IReadOnlyList<GenericElement> IRoutineContainer.EncodedRoutines => EncodedRoutines;

    /// <summary>Structure synthesized by the linker from parameters and local tags, so AOI-typed tags can be shaped.</summary>
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

public sealed class AoiParameter : Tag
{
    public AoiParameterUsage ParameterUsage { get; init; }
    public bool Required { get; init; }
    public bool Visible { get; init; }
}
