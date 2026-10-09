using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>State shared by the XML-phase readers (single-threaded).</summary>
public sealed class ReaderContext
{
    public ReaderContext(ParseOptions options, DiagnosticBag diagnostics, ParseStatistics statistics)
    {
        Options = options;
        Diagnostics = diagnostics;
        Statistics = statistics;
    }

    public ParseOptions Options { get; }
    public DiagnosticBag Diagnostics { get; }
    public ParseStatistics Statistics { get; }
    public ValueInterner Interner { get; } = new();

    /// <summary>Names of add-on instructions seen so far (complete once the AOI section has been read).</summary>
    public List<string> AoiNames { get; } = new();

    public bool ParseL5k => Options.ParseValues is ValueParseMode.L5kOnly or ValueParseMode.Both;
    public bool ParseDecorated => Options.ParseValues is ValueParseMode.DecoratedOnly or ValueParseMode.Both;

    public void Unexpected(XmlReader reader, string parent, L5xElement? owner)
    {
        Diagnostics.Info("XML001", $"Unexpected element <{reader.LocalName}> under <{parent}>; kept as generic content",
            new DiagnosticLocation(XmlLine: reader.LineNumber(), ElementPath: parent + "/" + reader.LocalName));
        var g = GenericElementReader.Read(reader, Options.SkipCustomProperties, CountSkipped);
        if (owner is not null) (owner.UnknownChildren ??= new()).Add(g);
    }

    public void CountSkipped() => Statistics.SkippedCustomProperties++;

    public GenericElement ReadGeneric(XmlReader reader) => GenericElementReader.Read(reader, Options.SkipCustomProperties, CountSkipped);

    public DiagnosticLocation Location(XmlReader reader, string? tag = null, string? container = null)
        => new(XmlLine: reader.LineNumber(), Tag: tag, Container: container);
}
