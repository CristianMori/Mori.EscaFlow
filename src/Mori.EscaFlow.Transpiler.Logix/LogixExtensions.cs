using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.Logix;

/// <summary>Constants of the Logix vendor extension.</summary>
public static class LogixVendor
{
    /// <summary>Vendor id used in <c>source.vendor</c>, the <c>vendor.logix</c> slot, <c>sysCall.catalog</c> and the <c>logix</c> pin profile.</summary>
    /// <remarks>Named <see cref="LogixVendor"/> rather than <c>Logix</c> because the runtime converter has its own <c>Logix</c> class (the static helper exposing TON/TOF/TONR/CTU/CTD to generated code); keeping the two apart avoids a name clash between "the vendor id holder" and "the standard-block runtime helper" even though both live under a Logix-flavoured namespace.</remarks>
    public const string Id = "logix";

    /// <summary>Name of the global variable list holding controller-scoped tags (the controller name is used at build time; this is the fallback).</summary>
    public const string ControllerGvl = "Controller";

    /// <summary>Name of the synthetic global variable list holding module I/O images.</summary>
    public const string IoGvl = "IO";

    /// <summary>Program body action name synthesized for the main routine call.</summary>
    public const string MainAction = "Main";
}

/// <summary>
/// Typed access to the <c>vendor.logix</c> slot of each node kind. The vendor slot (<see cref="IrNode.Vendor"/>) is
/// a <c>Dictionary&lt;string, object?&gt;</c> keyed by vendor id so the core model stays neutral; these extension
/// methods are the only place that casts the slot's payload back to a concrete Logix type, so nothing outside this
/// file needs to know the slot's storage convention.
/// </summary>
public static class LogixExtensions
{
    public static LogixDocumentExt? Logix(this IrDocument node) => node.GetVendor<LogixDocumentExt>(LogixVendor.Id);
    public static void SetLogix(this IrDocument node, LogixDocumentExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixVariableExt? Logix(this IrVariable node) => node.GetVendor<LogixVariableExt>(LogixVendor.Id);
    public static void SetLogix(this IrVariable node, LogixVariableExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixTypeExt? Logix(this IrType node) => node.GetVendor<LogixTypeExt>(LogixVendor.Id);
    public static void SetLogix(this IrType node, LogixTypeExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixMemberExt? Logix(this IrMember node) => node.GetVendor<LogixMemberExt>(LogixVendor.Id);
    public static void SetLogix(this IrMember node, LogixMemberExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixPouExt? Logix(this IrPou node) => node.GetVendor<LogixPouExt>(LogixVendor.Id);
    public static void SetLogix(this IrPou node, LogixPouExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixActionExt? Logix(this IrAction node) => node.GetVendor<LogixActionExt>(LogixVendor.Id);
    public static void SetLogix(this IrAction node, LogixActionExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixDeviceExt? Logix(this IrIoDevice node) => node.GetVendor<LogixDeviceExt>(LogixVendor.Id);
    public static void SetLogix(this IrIoDevice node, LogixDeviceExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixTaskExt? Logix(this IrTask node) => node.GetVendor<LogixTaskExt>(LogixVendor.Id);
    public static void SetLogix(this IrTask node, LogixTaskExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixRungExt? Logix(this IrRung node) => node.GetVendor<LogixRungExt>(LogixVendor.Id);
    public static void SetLogix(this IrRung node, LogixRungExt ext) => node.SetVendor(LogixVendor.Id, ext);

    public static LogixBodyExt? Logix(this IrBody node) => node.GetVendor<LogixBodyExt>(LogixVendor.Id);
    public static void SetLogix(this IrBody node, LogixBodyExt ext) => node.SetVendor(LogixVendor.Id, ext);

    /// <summary>The Logix tag path of a variable (<c>Tag</c>, <c>Program:P.Tag</c>, <c>Module:I</c>) when recorded, else the id.</summary>
    /// <remarks>Falling back to the plc-ir id rather than returning null keeps every call site simple; the id is always present and, for most variables, is already derived from the same path, so the fallback is rarely visibly wrong.</remarks>
    public static string LogixPath(this IrVariable v) => v.Logix()?.Path ?? v.Id;

    /// <summary>Every MESSAGE-typed variable of the document with its configuration, for a converter to generate MSG runtime bindings from without re-walking the whole variable tree itself.</summary>
    public static IEnumerable<(IrVariable Variable, LogixMessage Message)> Messages(this IrDocument doc)
    {
        foreach (var v in doc.AllVariables())
            if (v.Logix()?.Message is { } m) yield return (v, m);
    }
}

// ------------------------------------------------------------------ extension payloads

/// <summary>Controller-level facts from the L5X project header that have no place in the neutral <see cref="IrDocument"/> itself: product identity and revision, used for diagnostics and for choosing which Logix quirks apply.</summary>
public sealed class LogixDocumentExt
{
    public string? TargetName { get; set; }
    public string? TargetType { get; set; }
    public int? MajorRev { get; set; }
    public int? MinorRev { get; set; }
    public string? SafetyLevel { get; set; }
}

/// <summary>Rockwell-only facts about a variable: everything the L5X tag element carries beyond name, type and initial value.</summary>
public sealed class LogixVariableExt
{
    /// <summary>The path a client addresses the tag by: <c>Tag</c>, <c>Program:Prog.Tag</c>, <c>Module:I</c>. Add-on parameters and locals use <c>Def.Name</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>controller | program | aoiParameter | aoiLocal | moduleIo</summary>
    public string Scope { get; set; } = "controller";
    public string? Radix { get; set; }
    public string? ExternalAccess { get; set; }
    public string? OpcUaAccess { get; set; }
    public string? Class { get; set; }

    /// <summary>Program parameter usage as written (Input / Output / InOut / Public).</summary>
    public string? Usage { get; set; }
    public bool? Required { get; set; }
    public bool? Visible { get; set; }

    /// <summary>The alias text as written in the export, for alias tags. Kept alongside the resolved <see cref="IrVariable.Alias"/> target because the raw text is sometimes needed for diagnostics when resolution fails.</summary>
    public string? AliasFor { get; set; }
    public LogixMessage? Message { get; set; }
    public LogixProduced? Produced { get; set; }
    public LogixConsumed? Consumed { get; set; }

    /// <summary>For the implicit EnableIn / EnableOut parameters of an add-on instruction: Logix synthesizes these on every AOI and they must be recognised as such rather than treated as ordinary user parameters.</summary>
    public bool Implicit { get; set; }
}

/// <summary>Produced-tag configuration (this tag is the source of a controller-to-controller data exchange).</summary>
public sealed class LogixProduced
{
    public int? ProduceCount { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
}

/// <summary>Consumed-tag configuration (this tag mirrors a tag produced by another controller).</summary>
public sealed class LogixConsumed
{
    public string? Producer { get; set; }
    public string? RemoteTag { get; set; }
    public double? Rpi { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
}

/// <summary>Rockwell-only facts about a type: which Logix type family it came from and, for a BOOL-overlay structure, the hidden integer hosts that back its bit members.</summary>
public sealed class LogixTypeExt
{
    /// <summary>atomic | predefined | user | addon | module | unresolved</summary>
    public string Class { get; set; } = "user";
    public string? Family { get; set; }
    public LogixModuleType? ModuleType { get; set; }

    /// <summary>For the backing structure of an add-on instruction: the add-on (POU) name.</summary>
    public string? BackingFor { get; set; }

    /// <summary>
    /// Hidden host members of BIT overlays (the ZZZZZZZZZZ... SINT/DINT hosts), not addressable by logic but part
    /// of the Logix layout. Logix stores a run of BOOL members as bits of an invisible integer; a converter must
    /// still allocate that integer in the generated structure's memory layout so the overall size and member offsets
    /// match the controller, even though no logic ever names the host member directly.
    /// </summary>
    public List<LogixHiddenMember>? HiddenMembers { get; set; }
}

/// <summary>One hidden integer host backing a run of BOOL members, with its position in the full (visible + hidden) Logix member order.</summary>
public sealed class LogixHiddenMember
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int? Dims { get; set; }

    /// <summary>Index in the Logix member list (visible and hidden members together).</summary>
    public int Position { get; set; }
}

/// <summary>Identity and connection role of an I/O module type, for a converter deciding how to simulate its image in the virtual controller.</summary>
public sealed class LogixModuleType
{
    public string Vendor { get; set; } = "";
    public string Product { get; set; } = "";

    /// <summary>input | output | config | safetyInput | safetyOutput | unknown</summary>
    public string Direction { get; set; } = "unknown";
    public int Index { get; set; }
    public bool ThirdParty { get; set; }
}

/// <summary>Rockwell-only facts about a structure member: whether it is a hidden bit host, the bit overlay it backs, and radix/access hints used for a faithful round-trip through generated code.</summary>
public sealed class LogixMemberExt
{
    public bool Hidden { get; set; }

    /// <summary>For BOOL members overlaid on an integer host: the hidden host member and the bit.</summary>
    public string? BitHost { get; set; }
    public int? BitNumber { get; set; }
    public string? Radix { get; set; }
    public string? ExternalAccess { get; set; }

    /// <summary>True for BOOL arrays stored 32 per DINT.</summary>
    public bool Packed { get; set; }
}

/// <summary>Rockwell-only facts about a POU: whether it is a program or an add-on instruction, its scan-mode routine names, and its place in the Logix program/folder tree.</summary>
public sealed class LogixPouExt
{
    /// <summary>program | addOn</summary>
    public string Kind { get; set; } = "program";
    public string? Revision { get; set; }
    public string? RevisionExtension { get; set; }
    public string? Vendor { get; set; }
    public string? Class { get; set; }
    public bool ExecutePrescan { get; set; }
    public bool ExecutePostscan { get; set; }
    public bool ExecuteEnableInFalse { get; set; }

    /// <summary>The structure type Logix uses for instances of this add-on (same name as the add-on).</summary>
    public string? BackingType { get; set; }
    public string? MainRoutine { get; set; }
    public string? FaultRoutine { get; set; }
    public bool Disabled { get; set; }
    public bool Folder { get; set; }
    public string? ParentFolder { get; set; }
    public List<string>? Children { get; set; }
    public string? Task { get; set; }
}

/// <summary>Rockwell-only facts about an action: the routine type it was authored in (ladder/ST/SFC/FBD) and, for an add-on, which scan-mode hook it implements.</summary>
public sealed class LogixActionExt
{
    /// <summary>RLL | ST | SFC | FBD</summary>
    public string? RoutineType { get; set; }

    /// <summary>Logic | Prescan | Postscan | EnableInFalse for add-on scan-mode routines.</summary>
    public string? ScanMode { get; set; }
}

/// <summary>Rockwell-only facts about a body: the online-edit and sheet layout metadata Logix keeps for its own ladder/FBD editor, carried through only so a re-exported project looks the same in that editor.</summary>
public sealed class LogixBodyExt
{
    public string? OnlineEditType { get; set; }
    public string? SheetSize { get; set; }
    public string? SheetOrientation { get; set; }
}

/// <summary>Rockwell-only fact about a ladder rung: its comment/edit type code.</summary>
public sealed class LogixRungExt
{
    /// <summary>N, I, D, R, IR, rR, rI, rN, e, er, d.</summary>
    public string Type { get; set; } = "N";
}

/// <summary>Rockwell-only facts about an I/O module: product identity, backplane topology and the connections it opens, kept so the virtual controller can present the same module tree a real one would.</summary>
public sealed class LogixDeviceExt
{
    public string? Vendor { get; set; }
    public string? ProductType { get; set; }
    public string? ProductCode { get; set; }
    public int? Major { get; set; }
    public int? Minor { get; set; }
    public string? ParentModule { get; set; }
    public int? ParentModPortId { get; set; }
    public bool MajorFault { get; set; }
    public bool? SafetyEnabled { get; set; }
    public string? SafetyNetwork { get; set; }
    public List<LogixPort> Ports { get; set; } = new();
    public List<LogixConnection> Connections { get; set; } = new();

    /// <summary>Channel id of the configuration image.</summary>
    public string? ConfigChannel { get; set; }
    public string? CommMethod { get; set; }
    public int? PrimCxnInputSize { get; set; }
    public int? PrimCxnOutputSize { get; set; }
}

/// <summary>One physical/backplane port of a module (its address on a network or chassis, and whether it faces upstream toward the controller).</summary>
public sealed class LogixPort
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public string? Address { get; set; }
    public bool Upstream { get; set; }
    public int? Width { get; set; }
}

/// <summary>One CIP connection a module opens (its RPI, event id for event tasks, and the I/O channels its input/output images map to).</summary>
public sealed class LogixConnection
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public int? Rpi { get; set; }
    public int? EventId { get; set; }
    public bool? Unicast { get; set; }

    /// <summary>Channel ids of the input and output images.</summary>
    public string? InputChannel { get; set; }
    public string? OutputChannel { get; set; }
}

/// <summary>Rockwell-only facts about a task: its scheduling class and, for an event task, the trigger it waits on.</summary>
public sealed class LogixTaskExt
{
    /// <summary>CONTINUOUS | PERIODIC | EVENT as written.</summary>
    public string? Type { get; set; }
    public string? Class { get; set; }
    public string? EventTrigger { get; set; }
    public string? EventTag { get; set; }
    public bool InhibitTask { get; set; }
    public bool DisableUpdateOutputs { get; set; }
}

/// <summary>Reads the Logix pin profile of the standard function block catalog, and maps Logix timer/counter mnemonics onto the standard blocks they implement.</summary>
public static class LogixProfiles
{
    /// <summary>The <c>logix</c> profile from <c>std-fbs.json</c>. Throws rather than returning null because its absence means the embedded catalog and this package have drifted apart — a build-time inconsistency, not a runtime condition callers should need to handle.</summary>
    public static StdFbProfile Profile => StandardFunctionBlocks.GetProfile(LogixVendor.Id) ?? throw new InvalidOperationException("logix profile missing from std-fbs.json");

    /// <summary>The standard block a Logix timer / counter instruction maps to (RTO is the retentive-on-delay variant of TON, modelled as the standard TONR block).</summary>
    public static string? StandardBlockFor(string mnemonic) => mnemonic.ToUpperInvariant() switch
    {
        "TON" => "TON",
        "TOF" => "TOF",
        "RTO" => "TONR",
        "CTU" => "CTU",
        "CTD" => "CTD",
        _ => null,
    };
}
