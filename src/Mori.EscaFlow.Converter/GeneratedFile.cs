using System.Text;

namespace Mori.EscaFlow.Converter;

/// <summary>One file of a generated solution: a path relative to the output root and its text.</summary>
/// <remarks>Files are kept in memory until <see cref="WriteTo"/> so tests can inspect a generation without touching the disk.</remarks>
public sealed record GeneratedFile(string Path, string Content)
{
    /// <summary>Writes the file under <paramref name="root"/>, creating directories as needed; UTF-8 without a byte order mark so build tools and diff viewers treat it as plain text.</summary>
    public void WriteTo(string root)
    {
        var full = System.IO.Path.Combine(root, Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, Content, new UTF8Encoding(false));
    }
}

/// <summary>What a generator produces: files plus the report.</summary>
public sealed class GenerationResult
{
    /// <summary>Every file of the output, in generation order.</summary>
    public List<GeneratedFile> Files { get; } = new();

    /// <summary>Everything a reader needs that is not visible in the files (tallies, inventories, unsupported services, messages).</summary>
    public GenerationReport Report { get; } = new();

    /// <summary>Writes every file under <paramref name="root"/>.</summary>
    public void WriteTo(string root)
    {
        foreach (var f in Files) f.WriteTo(root);
    }
}

/// <summary>A target-language generator over a plc-ir document.</summary>
/// <remarks>
/// Each <c>Mori.EscaFlow.Converter.&lt;Language&gt;</c> package implements this once. Generators are expected to be
/// deterministic (the same document yields the same files) so generated solutions can be diffed across versions.
/// </remarks>
public interface IGenerator
{
    /// <summary>Short target id (<c>csharp</c>), used in the report and by the command line tool.</summary>
    string Target { get; }

    /// <summary>Produces the whole output for one document.</summary>
    GenerationResult Generate(Mori.EscaFlow.Transpiler.IrDocument document);
}
