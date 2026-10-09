using System.Diagnostics;
using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Linking;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing;

/// <summary>Entry point: parses an L5X file into an <see cref="L5xProject"/> in three phases (XML, logic, link).</summary>
public static class L5xParser
{
    public static L5xProject ParseFile(string path, ParseOptions? options = null)
    {
        options ??= ParseOptions.Default;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            return Parse(stream, options, path);
        }
        catch (IOException ex)
        {
            throw new L5xParseException($"Cannot read '{path}': {ex.Message}", ex);
        }
    }

    public static L5xProject ParseText(string xml, ParseOptions? options = null)
    {
        using var reader = new StringReader(xml);
        return Parse(XmlReader.Create(reader, Settings), options ?? ParseOptions.Default, null);
    }

    public static L5xProject Parse(Stream stream, ParseOptions? options = null, string? sourcePath = null)
    {
        using var reader = XmlReader.Create(stream, Settings);
        return Parse(reader, options ?? ParseOptions.Default, sourcePath);
    }

    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        CloseInput = false,
        CheckCharacters = false,
    };

    public static L5xProject Parse(XmlReader reader, ParseOptions options, string? sourcePath)
    {
        var total = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var stats = new ParseStatistics();
        var ctx = new ReaderContext(options, diagnostics, stats);
        L5xProject project;
        try
        {
            project = ReadDocument(reader, ctx, sourcePath);
        }
        catch (XmlException ex)
        {
            throw new L5xParseException($"Malformed XML at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}", ex, ex.LineNumber, ex.LinePosition);
        }
        stats.XmlPhase = total.Elapsed;

        if (options.ParseLogic)
        {
            var sw = Stopwatch.StartNew();
            var context = new LogicParseContext(ctx.AoiNames);
            LogicParsePhase.Run(project, context, options, diagnostics);
            stats.LogicPhase = sw.Elapsed;
        }

        if (options.Link)
        {
            var sw = Stopwatch.StartNew();
            L5xLinker.Link(project, options, diagnostics);
            stats.LinkPhase = sw.Elapsed;
        }

        stats.Total = total.Elapsed;
        stats.PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
        return project;
    }

    private static L5xProject ReadDocument(XmlReader reader, ReaderContext ctx, string? sourcePath)
    {
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "RSLogix5000Content")
            throw new L5xParseException($"Expected root element <RSLogix5000Content> but found <{reader.LocalName}>", null, reader.LineNumber());

        var attrs = new AttributeBag(reader);
        var schema = attrs.Take("SchemaRevision");
        var software = attrs.Take("SoftwareRevision");
        var targetName = attrs.Take("TargetName");
        var targetType = attrs.Take("TargetType");
        var targetSubType = attrs.Take("TargetSubType");
        var containsContext = attrs.TakeBool("ContainsContext");
        var exportDate = attrs.Take("ExportDate");
        var exportOptions = attrs.Take("ExportOptions");
        var owner = attrs.Take("Owner");
        var unknown = attrs.Remaining();
        var line = reader.LineNumber();

        Controller? controller = null;
        List<GenericElement>? extra = null;
        var controllerReader = new ControllerReader(ctx);
        reader.ForEachChild((r, child) =>
        {
            if (child == "Controller" && controller is null)
            {
                controller = controllerReader.Read(r);
            }
            else
            {
                (extra ??= new()).Add(ctx.ReadGeneric(r));
            }
        });

        if (controller is null)
        {
            ctx.Diagnostics.Error("XML003", "No <Controller> element found", new DiagnosticLocation(XmlLine: line));
            controller = new Controller { Name = targetName ?? "?" };
        }

        var project = new L5xProject
        {
            SchemaRevision = schema,
            SoftwareRevision = software,
            TargetName = targetName,
            TargetType = targetType,
            TargetSubType = targetSubType,
            ContainsContext = containsContext,
            ExportDate = exportDate,
            ExportOptions = exportOptions,
            Owner = owner,
            Controller = controller,
            SourceLine = line,
            UnknownAttributes = unknown,
            UnknownChildren = extra,
            SourcePath = sourcePath,
            Statistics = ctx.Statistics,
            Diagnostics = ctx.Diagnostics,
        };
        return project;
    }
}
