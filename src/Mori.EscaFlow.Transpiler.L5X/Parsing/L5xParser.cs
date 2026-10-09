using System.Diagnostics;
using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Linking;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing;

/// <summary>
/// Entry point of the L5X parser: turns an export into an <see cref="L5xProject"/> in three phases.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>XML phase</b>: a single forward pass with a streaming <see cref="XmlReader"/>. Production exports reach
/// hundreds of megabytes (most of it tag values), so the document is never loaded as a whole; only bounded
/// elements such as one data type, module or SFC body are materialized as small trees.</item>
/// <item><b>Logic phase</b>: every rung, structured text body and SFC snippet is parsed into an AST, in parallel,
/// once the add-on instruction names are known (they decide whether <c>Name(...)</c> is an add-on call).</item>
/// <item><b>Link phase</b>: the data type registry is built, references (types, aliases, routine calls, tasks,
/// chart links, messages) are resolved and L5K initializers are shaped into typed values.</item>
/// </list>
/// Content problems never throw: they are reported as <see cref="ParseDiagnostic"/> entries so that a project with a
/// few odd constructs still yields a usable model. Only I/O errors and malformed XML raise <see cref="L5xParseException"/>.
/// </remarks>
public static class L5xParser
{
    /// <summary>Parses the file at <paramref name="path"/>. I/O failures become <see cref="L5xParseException"/>.</summary>
    public static L5xProject ParseFile(string path, ParseOptions? options = null)
    {
        options ??= ParseOptions.Default;
        try
        {
            // A large buffer and sequential-scan hint matter for the 200 MB exports: the reader is strictly forward-only.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            return Parse(stream, options, path);
        }
        catch (IOException ex)
        {
            throw new L5xParseException($"Cannot read '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>Parses an in-memory XML string (used by tests and by callers that already hold the export text).</summary>
    public static L5xProject ParseText(string xml, ParseOptions? options = null)
    {
        using var reader = new StringReader(xml);
        return Parse(XmlReader.Create(reader, Settings), options ?? ParseOptions.Default, null);
    }

    /// <summary>Parses from a stream; <paramref name="sourcePath"/> is only recorded on the project for traceability.</summary>
    public static L5xProject Parse(Stream stream, ParseOptions? options = null, string? sourcePath = null)
    {
        using var reader = XmlReader.Create(stream, Settings);
        return Parse(reader, options ?? ParseOptions.Default, sourcePath);
    }

    /// <summary>
    /// Reader settings shared by every entry point. Whitespace and comments are irrelevant to the format; DTDs are
    /// never used by Logix and are refused for safety; <c>CheckCharacters</c> is off because exports occasionally
    /// contain control characters inside CDATA descriptions and refusing the whole file would be worse than keeping them.
    /// </summary>
    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        CloseInput = false,
        CheckCharacters = false,
    };

    /// <summary>Runs the three phases over an already created reader. Timings and counters land in <see cref="ParseStatistics"/>.</summary>
    public static L5xProject Parse(XmlReader reader, ParseOptions options, string? sourcePath)
    {
        var total = Stopwatch.StartNew();
        var diagnostics = new DiagnosticBag();
        var stats = new ParseStatistics();
        // The reader context is shared by every element reader: options, the diagnostic sink and the counters.
        var ctx = new ReaderContext(options, diagnostics, stats);
        L5xProject project;
        try
        {
            project = ReadDocument(reader, ctx, sourcePath);
        }
        catch (XmlException ex)
        {
            // Malformed XML is the one content problem that cannot be reported as a diagnostic: the stream is unusable.
            throw new L5xParseException($"Malformed XML at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}", ex, ex.LineNumber, ex.LinePosition);
        }
        stats.XmlPhase = total.Elapsed;

        if (options.ParseLogic)
        {
            var sw = Stopwatch.StartNew();
            // The logic parsers need the add-on instruction names, which the XML phase collected while reading the definitions.
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

    /// <summary>
    /// Reads the document root (<c>RSLogix5000Content</c>) and dispatches its single <c>Controller</c> child to
    /// <see cref="ControllerReader"/>. Anything else at root level is kept verbatim as a generic element.
    /// </summary>
    private static L5xProject ReadDocument(XmlReader reader, ReaderContext ctx, string? sourcePath)
    {
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "RSLogix5000Content")
            throw new L5xParseException($"Expected root element <RSLogix5000Content> but found <{reader.LocalName}>", null, reader.LineNumber());

        // Attributes are "taken" one by one so that whatever remains can be reported as unknown instead of silently dropped.
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
            // An export of a single component (TargetType other than Controller) still wraps it in a Controller
            // element, so a missing one means the file is not an L5X project at all; report it and continue with a stub.
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
            // The project shares the very instances the readers wrote into, so the later phases keep appending to them.
            Statistics = ctx.Statistics,
            Diagnostics = ctx.Diagnostics,
        };
        return project;
    }
}
