namespace L5x.Parsing;

public enum ValueParseMode
{
    None,
    L5kOnly,
    DecoratedOnly,
    Both,
}

public sealed class ParseOptions
{
    public static readonly ParseOptions Default = new();

    /// <summary>Parse rung text, structured text and SFC snippets into ASTs.</summary>
    public bool ParseLogic { get; init; } = true;

    public ValueParseMode ParseValues { get; init; } = ValueParseMode.Both;

    /// <summary>Keep the raw L5K initializer text on each tag (costs memory on large files).</summary>
    public bool KeepRawL5kText { get; init; }

    /// <summary>Degree of parallelism for the logic phase; 0 or less means processor count.</summary>
    public int Threads { get; init; }

    /// <summary>Compare L5K-shaped values against Decorated values and report mismatches as Info diagnostics.</summary>
    public bool ValidateL5kAgainstDecorated { get; init; }

    /// <summary>Skip CustomProperties subtrees (library round-trip metadata) entirely.</summary>
    public bool SkipCustomProperties { get; init; } = true;

    /// <summary>Run the link phase (reference resolution, type registry, value shaping).</summary>
    public bool Link { get; init; } = true;

    public int EffectiveThreads => Threads > 0 ? Threads : Environment.ProcessorCount;
}
