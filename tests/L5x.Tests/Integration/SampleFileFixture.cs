using L5x.Model;
using L5x.Parsing;
using Xunit;

namespace L5x.Tests.Integration;

/// <summary>
/// Locates the sample exports (repo root or L5X_SAMPLES_DIR) and parses each at most once per test run.
/// Tests skip themselves when a sample is missing.
/// </summary>
public sealed class SampleFileFixture
{
    public const string Aib102 = "GRC_AIB102_MPLC_V742_20230719.L5X";
    public const string Stlib106 = "GRC_STLIB106_MPLC_V340.L5X";
    public const string Stl107 = "LiftCellGRC_STL107_20230516.L5X";

    private readonly Dictionary<string, Lazy<L5xProject>> _projects = new(StringComparer.OrdinalIgnoreCase);

    public string? SamplesDir { get; }

    public SampleFileFixture()
    {
        SamplesDir = FindSamplesDir();
        foreach (var name in new[] { Aib102, Stlib106, Stl107 })
        {
            var n = name;
            _projects[n] = new Lazy<L5xProject>(() => L5xParser.ParseFile(Path.Combine(SamplesDir!, n), new ParseOptions { ValidateL5kAgainstDecorated = true }), LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    public bool Has(string name) => SamplesDir is not null && File.Exists(Path.Combine(SamplesDir, name));

    public L5xProject Get(string name)
    {
        if (!Has(name)) throw new SkipException($"Sample {name} not found");
        return _projects[name].Value;
    }

    private static string? FindSamplesDir()
    {
        var env = Environment.GetEnvironmentVariable("L5X_SAMPLES_DIR");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, Stlib106)) || File.Exists(Path.Combine(dir, Aib102))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}

/// <summary>Thrown to mark a test as skipped when a sample file is absent.</summary>
public sealed class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}

[CollectionDefinition("Samples")]
public sealed class SamplesCollection : ICollectionFixture<SampleFileFixture>
{
}
