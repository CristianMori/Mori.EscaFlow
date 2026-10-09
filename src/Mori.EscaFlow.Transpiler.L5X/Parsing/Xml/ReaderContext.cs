using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// State shared by the XML-phase readers (single-threaded): options, the diagnostic sink, the counters, the value
/// interner that deduplicates the millions of identical scalar values of a large export, and the add-on names
/// collected for the logic phase.
/// </summary>
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

    /// <summary>Shares identical value objects across tags; most initial values are zero and would otherwise be allocated per element.</summary>
    public ValueInterner Interner { get; } = new();

    /// <summary>Names of add-on instructions seen so far (complete once the AOI section has been read).</summary>
    public List<string> AoiNames { get; } = new();

    public bool ParseL5k => Options.ParseValues is ValueParseMode.L5kOnly or ValueParseMode.Both;
    public bool ParseDecorated => Options.ParseValues is ValueParseMode.DecoratedOnly or ValueParseMode.Both;

    /// <summary>Records an element the reader did not expect under <paramref name="parent"/> and keeps it verbatim on <paramref name="owner"/>.</summary>
    public void Unexpected(XmlReader reader, string parent, L5xElement? owner)
    {
        Diagnostics.Info("XML001", $"Unexpected element <{reader.LocalName}> under <{parent}>; kept as generic content",
            new DiagnosticLocation(XmlLine: reader.LineNumber(), ElementPath: parent + "/" + reader.LocalName));
        var g = GenericElementReader.Read(reader, Options.SkipCustomProperties, CountSkipped);
        if (owner is not null) (owner.UnknownChildren ??= new()).Add(g);
    }

    public void CountSkipped() => Statistics.SkippedCustomProperties++;

    /// <summary>Copies the current element verbatim, honouring the CustomProperties option.</summary>
    public GenericElement ReadGeneric(XmlReader reader) => GenericElementReader.Read(reader, Options.SkipCustomProperties, CountSkipped);

    public DiagnosticLocation Location(XmlReader reader, string? tag = null, string? container = null)
        => new(XmlLine: reader.LineNumber(), Tag: tag, Container: container);
}
