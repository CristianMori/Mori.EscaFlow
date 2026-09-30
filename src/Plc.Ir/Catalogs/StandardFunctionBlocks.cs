using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plc.Ir.Catalogs;

public sealed class StdFbDefinition
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasPin(string name) => Inputs.ContainsKey(name) || Outputs.ContainsKey(name);
    public string? PinType(string name) => Inputs.TryGetValue(name, out var t) ? t : Outputs.TryGetValue(name, out t) ? t : null;
}

public sealed class StdFbProfile
{
    [JsonIgnore]
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Standard block name to the vendor instance type name.</summary>
    public Dictionary<string, string> InstanceTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per block, per pin: how the pin maps onto the instance structure. Pins absent here keep the IEC name and type.</summary>
    public Dictionary<string, Dictionary<string, StdFbPinMapping>> Pins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public StdFbPinMapping? Pin(string fb, string pin)
        => Pins.TryGetValue(fb, out var pins) && pins.TryGetValue(pin, out var m) ? m : null;

    /// <summary>The pin type under this profile (the mapping's type, else the IEC type).</summary>
    public string? PinType(StdFbDefinition fb, string pin)
        => Pin(fb.Name, pin) is { Type: { } t } ? t : fb.PinType(pin);

    /// <summary>Pins that exist under this profile only (e.g. Logix TT, OV, UN).</summary>
    public IEnumerable<string> ExtraPins(string fb)
        => Pins.TryGetValue(fb, out var pins) ? pins.Where(p => p.Value.Extra).Select(p => p.Key) : Enumerable.Empty<string>();
}

public sealed class StdFbPinMapping
{
    /// <summary>enable: the pin is the block's enable (rung condition in ladder); reset: the pin resets the block; null: a data pin.</summary>
    public string? Role { get; set; }

    /// <summary>The instance member holding this pin's value, or null when the pin has no storage in the vendor structure.</summary>
    public string? Member { get; set; }

    /// <summary>Pin type under the profile when it differs from the IEC type.</summary>
    public string? Type { get; set; }
    public string? Unit { get; set; }

    /// <summary>True for a pin that exists only in this profile.</summary>
    public bool Extra { get; set; }
}

/// <summary>The catalog of standard function blocks and pin profiles, loaded from the embedded <c>std-fbs.json</c>.</summary>
public static class StandardFunctionBlocks
{
    private sealed class Catalog
    {
        public List<StdFbDefinition> Fbs { get; set; } = new();
        public Dictionary<string, StdFbProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Lazy<Catalog> Loaded = new(Load);

    private static Catalog Load()
    {
        using var stream = typeof(StandardFunctionBlocks).Assembly.GetManifestResourceStream("Plc.Ir.Catalogs.std-fbs.json")
            ?? throw new InvalidOperationException("Embedded catalog std-fbs.json is missing");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var catalog = JsonSerializer.Deserialize<Catalog>(stream, options) ?? throw new InvalidOperationException("Empty std-fbs.json");
        foreach (var (name, profile) in catalog.Profiles) profile.Name = name;
        return catalog;
    }

    public static IReadOnlyList<StdFbDefinition> All => Loaded.Value.Fbs;
    public static IReadOnlyDictionary<string, StdFbProfile> Profiles => Loaded.Value.Profiles;

    public static StdFbDefinition? Get(string name) => Loaded.Value.Fbs.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public static bool IsStandard(string name) => Get(name) is not null;
    public static StdFbProfile? GetProfile(string name) => Loaded.Value.Profiles.GetValueOrDefault(name);

    /// <summary>The raw catalog JSON, for documentation and for non-C# consumers.</summary>
    public static string CatalogJson()
    {
        using var stream = typeof(StandardFunctionBlocks).Assembly.GetManifestResourceStream("Plc.Ir.Catalogs.std-fbs.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
