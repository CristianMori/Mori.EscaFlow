using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.Catalogs;

/// <summary>An IEC 61131-3 standard function block: its name and its input and output pins with their IEC types.</summary>
public sealed class StdFbDefinition
{
    /// <summary>Block name (TON, CTU, R_TRIG, ...).</summary>
    public string Name { get; set; } = "";

    /// <summary>Short description.</summary>
    public string? Description { get; set; }

    /// <summary>Input pins: name to IEC type.</summary>
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output pins: name to IEC type.</summary>
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the block has an input or output pin of that name.</summary>
    public bool HasPin(string name) => Inputs.ContainsKey(name) || Outputs.ContainsKey(name);

    /// <summary>The IEC type of a pin, or null when the block has no such pin.</summary>
    public string? PinType(string name) => Inputs.TryGetValue(name, out var t) ? t : Outputs.TryGetValue(name, out t) ? t : null;
}

/// <summary>A pin profile: how one vendor represents the standard blocks.</summary>
/// <remarks>
/// <para>
/// Profiles are the plugin mechanism for timing semantics. A document never says "IEC TON" or "Logix TON"; it
/// says "TON under profile X". The profile fixes the instance type (an IEC TON instance versus a Logix TIMER
/// structure) and how each standard pin maps onto that instance: Logix stores the preset in <c>PRE</c> as DINT
/// milliseconds where IEC has <c>PT</c> as TIME, exposes extra status pins (TT, OV, UN), and treats the IN pin as
/// the rung condition recorded in <c>EN</c>. A converter implements one runtime per profile and refuses profiles
/// it does not know, instead of silently approximating one vendor's timer with another's.
/// </para>
/// <para>
/// The source structures are kept on purpose: the rest of the program (and every HMI) addresses
/// <c>Timer.ACC</c> and <c>Timer.DN</c> directly, so replacing the structure with an IEC instance would break
/// those references and the tag server's image.
/// </para>
/// </remarks>
public sealed class StdFbProfile
{
    /// <summary>Profile id (iec, logix, s7); set from the catalog key, not serialized.</summary>
    [JsonIgnore]
    public string Name { get; set; } = "";

    /// <summary>Short description.</summary>
    public string? Description { get; set; }

    /// <summary>Standard block name to the vendor instance type name.</summary>
    public Dictionary<string, string> InstanceTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per block, per pin: how the pin maps onto the instance structure. Pins absent here keep the IEC name and type.</summary>
    public Dictionary<string, Dictionary<string, StdFbPinMapping>> Pins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mapping of one pin of one block under this profile, or null when the pin keeps its IEC meaning.</summary>
    public StdFbPinMapping? Pin(string fb, string pin)
        => Pins.TryGetValue(fb, out var pins) && pins.TryGetValue(pin, out var m) ? m : null;

    /// <summary>The pin type under this profile (the mapping's type, else the IEC type).</summary>
    public string? PinType(StdFbDefinition fb, string pin)
        => Pin(fb.Name, pin) is { Type: { } t } ? t : fb.PinType(pin);

    /// <summary>Pins that exist under this profile only (e.g. Logix TT, OV, UN).</summary>
    public IEnumerable<string> ExtraPins(string fb)
        => Pins.TryGetValue(fb, out var pins) ? pins.Where(p => p.Value.Extra).Select(p => p.Key) : Enumerable.Empty<string>();
}

/// <summary>How one standard pin maps onto a vendor's instance structure.</summary>
public sealed class StdFbPinMapping
{
    /// <summary>enable: the pin is the block's enable (rung condition in ladder); reset: the pin resets the block; null: a data pin.</summary>
    public string? Role { get; set; }

    /// <summary>The instance member holding this pin's value, or null when the pin has no storage in the vendor structure.</summary>
    public string? Member { get; set; }

    /// <summary>Pin type under the profile when it differs from the IEC type.</summary>
    public string? Type { get; set; }

    /// <summary>Unit of a time pin under the profile (Logix presets are milliseconds).</summary>
    public string? Unit { get; set; }

    /// <summary>True for a pin that exists only in this profile.</summary>
    public bool Extra { get; set; }
}

/// <summary>The catalog of standard function blocks and pin profiles, loaded from the embedded <c>std-fbs.json</c>.</summary>
/// <remarks>The catalog is a JSON resource rather than C# code so non-.NET consumers of the format can read the same table; <see cref="CatalogJson"/> exposes it verbatim.</remarks>
public static class StandardFunctionBlocks
{
    /// <summary>Shape of the embedded JSON.</summary>
    private sealed class Catalog
    {
        public List<StdFbDefinition> Fbs { get; set; } = new();
        public Dictionary<string, StdFbProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Lazy<Catalog> Loaded = new(Load);

    /// <summary>Reads the embedded resource once; profiles learn their id from their dictionary key.</summary>
    private static Catalog Load()
    {
        using var stream = typeof(StandardFunctionBlocks).Assembly.GetManifestResourceStream("Mori.EscaFlow.Transpiler.Catalogs.std-fbs.json")
            ?? throw new InvalidOperationException("Embedded catalog std-fbs.json is missing");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var catalog = JsonSerializer.Deserialize<Catalog>(stream, options) ?? throw new InvalidOperationException("Empty std-fbs.json");
        foreach (var (name, profile) in catalog.Profiles) profile.Name = name;
        return catalog;
    }

    /// <summary>Every standard block.</summary>
    public static IReadOnlyList<StdFbDefinition> All => Loaded.Value.Fbs;

    /// <summary>Every profile by id.</summary>
    public static IReadOnlyDictionary<string, StdFbProfile> Profiles => Loaded.Value.Profiles;

    /// <summary>The block named <paramref name="name"/> (case-insensitive), or null.</summary>
    public static StdFbDefinition? Get(string name) => Loaded.Value.Fbs.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="name"/> is a standard block.</summary>
    public static bool IsStandard(string name) => Get(name) is not null;

    /// <summary>The profile with the given id, or null.</summary>
    public static StdFbProfile? GetProfile(string name) => Loaded.Value.Profiles.GetValueOrDefault(name);

    /// <summary>The raw catalog JSON, for documentation and for non-C# consumers.</summary>
    public static string CatalogJson()
    {
        using var stream = typeof(StandardFunctionBlocks).Assembly.GetManifestResourceStream("Mori.EscaFlow.Transpiler.Catalogs.std-fbs.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
