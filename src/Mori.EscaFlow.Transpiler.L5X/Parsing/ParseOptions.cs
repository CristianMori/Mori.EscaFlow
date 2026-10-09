namespace Mori.EscaFlow.Transpiler.L5X.Parsing;

/// <summary>
/// Which tag value representations to read. An export carries each value twice: as an L5K initializer string
/// (compact, untyped) and as Decorated XML (typed, one element per member). Reading both allows a cross-check;
/// reading none makes the XML phase several times faster on value-heavy exports.
/// </summary>
public enum ValueParseMode
{
    None,
    L5kOnly,
    DecoratedOnly,
    Both,
}

/// <summary>Knobs of the parser. The defaults produce the complete model.</summary>
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

    /// <summary>
    /// Skip <c>CustomProperties</c> subtrees entirely. They hold library round-trip metadata (Studio 5000 library
    /// object ids and the like), can nest inside descriptions and rung comments, and carry nothing a transpiler needs.
    /// </summary>
    public bool SkipCustomProperties { get; init; } = true;

    /// <summary>Run the link phase (reference resolution, type registry, value shaping).</summary>
    public bool Link { get; init; } = true;

    /// <summary>The thread count actually used by the logic phase.</summary>
    public int EffectiveThreads => Threads > 0 ? Threads : Environment.ProcessorCount;
}
