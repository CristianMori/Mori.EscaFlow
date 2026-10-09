using System.Text;

namespace Mori.EscaFlow.Converter;

/// <summary>One file of a generated solution: a path relative to the output root and its text.</summary>
public sealed record GeneratedFile(string Path, string Content)
{
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
    public List<GeneratedFile> Files { get; } = new();
    public GenerationReport Report { get; } = new();

    public void WriteTo(string root)
    {
        foreach (var f in Files) f.WriteTo(root);
    }
}

/// <summary>A target-language generator over a plc-ir document.</summary>
public interface IGenerator
{
    string Target { get; }
    GenerationResult Generate(Mori.EscaFlow.Transpiler.IrDocument document);
}
