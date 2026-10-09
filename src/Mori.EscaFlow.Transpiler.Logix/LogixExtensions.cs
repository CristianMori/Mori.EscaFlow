using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.Logix;

/// <summary>Constants of the Logix vendor extension.</summary>
public static class LogixVendor
{
    /// <summary>Vendor id used in <c>source.vendor</c>, the <c>vendor.logix</c> slot, <c>sysCall.catalog</c> and the <c>logix</c> pin profile.</summary>
    public const string Id = "logix";

    /// <summary>Name of the global variable list holding controller-scoped tags (the controller name is used at build time; this is the fallback).</summary>
    public const string ControllerGvl = "Controller";

    /// <summary>Name of the synthetic global variable list holding module I/O images.</summary>
    public const string IoGvl = "IO";

    /// <summary>Program body action name synthesized for the main routine call.</summary>
    public const string MainAction = "Main";
}

/// <summary>Typed access to the <c>vendor.logix</c> slot of each node kind.</summary>
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
    public static string LogixPath(this IrVariable v) => v.Logix()?.Path ?? v.Id;

    /// <summary>Every MESSAGE-typed variable of the document with its configuration.</summary>
    public static IEnumerable<(IrVariable Variable, LogixMessage Message)> Messages(this IrDocument doc)
    {
        foreach (var v in doc.AllVariables())
            if (v.Logix()?.Message is { } m) yield return (v, m);
    }
}

// ------------------------------------------------------------------ extension payloads

public sealed class LogixDocumentExt
{
    public string? TargetName { get; set; }
    public string? TargetType { get; set; }
    public int? MajorRev { get; set; }
    public int? MinorRev { get; set; }
    public string? SafetyLevel { get; set; }
}

/// <summary>Rockwell-only facts about a variable.</summary>
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

    /// <summary>The alias text as written in the export, for alias tags.</summary>
    public string? AliasFor { get; set; }
    public LogixMessage? Message { get; set; }
    public LogixProduced? Produced { get; set; }
    public LogixConsumed? Consumed { get; set; }

    /// <summary>For the implicit EnableIn / EnableOut parameters of an add-on instruction.</summary>
    public bool Implicit { get; set; }
}

public sealed class LogixProduced
{
    public int? ProduceCount { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
}

public sealed class LogixConsumed
{
    public string? Producer { get; set; }
    public string? RemoteTag { get; set; }
    public double? Rpi { get; set; }
    public Dictionary<string, string>? Attributes { get; set; }
}

public sealed class LogixTypeExt
{
    /// <summary>atomic | predefined | user | addon | module | unresolved</summary>
    public string Class { get; set; } = "user";
    public string? Family { get; set; }
    public LogixModuleType? ModuleType { get; set; }

    /// <summary>For the backing structure of an add-on instruction: the add-on (POU) name.</summary>
    public string? BackingFor { get; set; }

    /// <summary>Hidden host members of BIT overlays (the ZZZZZZZZZZ... SINT/DINT hosts), not addressable by logic but part of the Logix layout.</summary>
    public List<LogixHiddenMember>? HiddenMembers { get; set; }
}

public sealed class LogixHiddenMember
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int? Dims { get; set; }

    /// <summary>Index in the Logix member list (visible and hidden members together).</summary>
    public int Position { get; set; }
}

public sealed class LogixModuleType
{
    public string Vendor { get; set; } = "";
    public string Product { get; set; } = "";

    /// <summary>input | output | config | safetyInput | safetyOutput | unknown</summary>
    public string Direction { get; set; } = "unknown";
    public int Index { get; set; }
    public bool ThirdParty { get; set; }
}

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

public sealed class LogixActionExt
{
    /// <summary>RLL | ST | SFC | FBD</summary>
    public string? RoutineType { get; set; }

    /// <summary>Logic | Prescan | Postscan | EnableInFalse for add-on scan-mode routines.</summary>
    public string? ScanMode { get; set; }
}

public sealed class LogixBodyExt
{
    public string? OnlineEditType { get; set; }
    public string? SheetSize { get; set; }
    public string? SheetOrientation { get; set; }
}

public sealed class LogixRungExt
{
    /// <summary>N, I, D, R, IR, rR, rI, rN, e, er, d.</summary>
    public string Type { get; set; } = "N";
}

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

public sealed class LogixPort
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public string? Address { get; set; }
    public bool Upstream { get; set; }
    public int? Width { get; set; }
}

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

/// <summary>Reads the Logix pin profile of the standard function block catalog.</summary>
public static class LogixProfiles
{
    public static StdFbProfile Profile => StandardFunctionBlocks.GetProfile(LogixVendor.Id) ?? throw new InvalidOperationException("logix profile missing from std-fbs.json");

    /// <summary>The standard block a Logix timer / counter instruction maps to.</summary>
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
