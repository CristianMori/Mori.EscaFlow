using System.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Json;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Parsing;
using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Import;
using Mori.EscaFlow.Transpiler.Printing;
using Mori.EscaFlow.Transpiler.Validation;

namespace Mori.EscaFlow.Cli;

public static class Program
{
    private const string Usage = """
        Usage:
          escaflow parse <file.L5X> [options]          parse a Logix export and report on it
          escaflow ir build <file.L5X> <out.ir.json>   build the plc-ir 2.0 middle file
          escaflow ir validate <file.ir.json>          validate a middle file (schema-level read, references, typing)
          escaflow ir print <file.ir.json> [pou[/action]]   print bodies as structured text

        parse options:
          --summary               Print counts and timings (default when no other output is requested)
          --diagnostics           Print every diagnostic
          --json <out.json>       Write the parsed model as JSON
          --json-include <list>   Comma list of: model, logic, values, all (default: all)
          --no-values             Do not parse tag values
          --no-logic              Do not parse rung / ST text
          --validate-values       Cross-check L5K initializers against Decorated data
          --keep-l5k              Keep the raw L5K text on each tag
          --threads <n>           Parallelism for the logic phase
          --strict                Exit code 1 when any Error diagnostic was raised

        ir build options:
          --no-values             Omit initial values from the middle file
          --no-source             Omit rung text and ST lines
          --no-prescan            Do not synthesize prescan (init) actions
          --indent                Indent the JSON
          --validate              Validate the built document and report issues
          --threads <n>           Parallelism for the logic phase

        ir validate options:
          --no-typing             Skip the typing pass
          --warnings              Print warnings as well as errors

          escaflow generate csharp <file.ir.json|file.L5X> <outDir> [options]   generate a C# solution
          --name <Name>           Solution / namespace name (default: controller name)
          --runtime <path>        Path to Mori.EscaFlow.Runtime.CSharp.csproj relative to the generated Logic project
          --host <path>           Path to Mori.EscaFlow.Runtime.Host.csproj relative to the generated Host project
          --eip-port <n|none>     EtherNet/IP port of the generated controller (default 44818)
          --url <url>             Web console URL (default http://localhost:5080)
          --strict-services       Fail when the logic uses a service the runtime does not implement
          --no-source-comments    Do not emit rung text / lines as comments
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 0;
        }
        try
        {
            return args[0] switch
            {
                "parse" when args.Length >= 2 => Parse(args),
                "ir" when args.Length >= 3 && args[1] == "build" && args.Length >= 4 => IrBuild(args),
                "ir" when args.Length >= 3 && args[1] == "validate" => IrValidate(args),
                "ir" when args.Length >= 3 && args[1] == "print" => IrPrint(args),
                "generate" when args.Length >= 4 && args[1] == "csharp" => GenerateCSharp(args),
                _ => UsageError(),
            };
        }
        catch (L5xParseException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int UsageError()
    {
        Console.WriteLine(Usage);
        return 2;
    }

    // ------------------------------------------------------------------ parse

    private static int Parse(string[] args)
    {
        var file = args[1];
        string? jsonPath = null;
        var include = JsonInclude.All;
        var summary = false;
        var diagnostics = false;
        var strict = false;
        var parseValues = ValueParseMode.Both;
        var parseLogic = true;
        var validate = false;
        var keepL5k = false;
        var threads = 0;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--summary": summary = true; break;
                case "--diagnostics": diagnostics = true; break;
                case "--json": jsonPath = Next(args, ref i); break;
                case "--json-include": include = JsonIncludeParser.Parse(Next(args, ref i)); break;
                case "--no-values": parseValues = ValueParseMode.None; break;
                case "--no-logic": parseLogic = false; break;
                case "--validate-values": validate = true; break;
                case "--keep-l5k": keepL5k = true; break;
                case "--threads": threads = int.Parse(Next(args, ref i)); break;
                case "--strict": strict = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'");
                    return UsageError();
            }
        }
        if (jsonPath is null) summary = true;

        var project = L5xParser.ParseFile(file, new ParseOptions
        {
            ParseLogic = parseLogic,
            ParseValues = parseValues,
            ValidateL5kAgainstDecorated = validate,
            KeepRawL5kText = keepL5k,
            Threads = threads,
        });

        if (summary) SummaryPrinter.Print(project, Console.Out);
        if (diagnostics)
            foreach (var d in project.Diagnostics) Console.WriteLine(d);
        if (jsonPath is not null)
        {
            var sw = Stopwatch.StartNew();
            L5xJson.WriteFile(project, jsonPath, include);
            Console.WriteLine($"JSON written to {jsonPath} ({new FileInfo(jsonPath).Length / (1024.0 * 1024.0):F1} MB) in {sw.Elapsed.TotalSeconds:F1} s");
        }
        return strict && project.Diagnostics.HasErrors ? 1 : 0;
    }

    // ------------------------------------------------------------------ ir build

    private static int IrBuild(string[] args)
    {
        var file = args[2];
        var outPath = args[3];
        var values = true;
        var source = true;
        var prescan = true;
        var indent = false;
        var validate = false;
        var threads = 0;
        for (var i = 4; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-values": values = false; break;
                case "--no-source": source = false; break;
                case "--no-prescan": prescan = false; break;
                case "--indent": indent = true; break;
                case "--validate": validate = true; break;
                case "--threads": threads = int.Parse(Next(args, ref i)); break;
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'");
                    return UsageError();
            }
        }

        var sw = Stopwatch.StartNew();
        var project = L5xParser.ParseFile(file, new ParseOptions { Threads = threads });
        Console.WriteLine($"Parsed {Path.GetFileName(file)} in {sw.Elapsed.TotalSeconds:F1} s ({project.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} errors, {project.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} warnings)");

        sw.Restart();
        var ir = new LogixIrBuilder(project, new LogixBuildOptions { IncludeValues = values, IncludeSourceText = source, SynthesizePrescan = prescan }).Build();
        var buildTime = sw.Elapsed;
        sw.Restart();
        if (Path.GetDirectoryName(Path.GetFullPath(outPath)) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        IrJson.WriteFile(ir, outPath, indent);
        var variables = ir.AllVariables().Count();
        var actions = ir.Pous.Sum(p => p.Actions.Count);
        Console.WriteLine($"IR written to {outPath} ({new FileInfo(outPath).Length / (1024.0 * 1024.0):F1} MB): {ir.Types.Count} types, {variables} variables, {ir.Io.Devices.Count} devices, {ir.Pous.Count} pous, {actions} actions; build {buildTime.TotalSeconds:F1} s, write {sw.Elapsed.TotalSeconds:F1} s");

        if (!validate) return 0;
        var result = new IrValidator().Validate(ir, new ValidationOptions { ServiceCatalogs = { LogixServiceCatalog.Instance } });
        Report(result, warnings: false);
        return result.HasErrors ? 1 : 0;
    }

    // ------------------------------------------------------------------ ir validate

    private static int IrValidate(string[] args)
    {
        var file = args[2];
        var typing = true;
        var warnings = false;
        for (var i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-typing": typing = false; break;
                case "--warnings": warnings = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'");
                    return UsageError();
            }
        }
        var sw = Stopwatch.StartNew();
        var result = new IrValidator().ValidateJsonFile(file, new ValidationOptions { CheckTyping = typing, ServiceCatalogs = { LogixServiceCatalog.Instance } });
        Console.WriteLine($"Validated {Path.GetFileName(file)} in {sw.Elapsed.TotalSeconds:F1} s");
        Report(result, warnings);
        return result.HasErrors ? 1 : 0;
    }

    private static void Report(ValidationResult result, bool warnings)
    {
        var errors = result.Errors.ToList();
        var warns = result.Warnings.ToList();
        Console.WriteLine($"{errors.Count} error(s), {warns.Count} warning(s)");
        foreach (var group in errors.GroupBy(e => e.Code))
        {
            Console.WriteLine($"  {group.Key} ({group.Count()}): {ValidationCodes.Describe(group.Key)}");
            foreach (var issue in group.Take(10)) Console.WriteLine($"    {issue.Message} [{issue.Location}]");
            if (group.Count() > 10) Console.WriteLine($"    ... {group.Count() - 10} more");
        }
        if (!warnings) return;
        foreach (var group in warns.GroupBy(e => e.Code))
        {
            Console.WriteLine($"  {group.Key} ({group.Count()}): {ValidationCodes.Describe(group.Key)}");
            foreach (var issue in group.Take(10)) Console.WriteLine($"    {issue.Message} [{issue.Location}]");
            if (group.Count() > 10) Console.WriteLine($"    ... {group.Count() - 10} more");
        }
    }

    // ------------------------------------------------------------------ ir print

    private static int IrPrint(string[] args)
    {
        var file = args[2];
        var selector = args.Length > 3 ? args[3] : null;
        var ir = IrJson.ReadFile(file);
        var printer = new StPrinter(ir);
        var pouName = selector?.Split('/')[0];
        var actionName = selector is not null && selector.Contains('/') ? selector[(selector.IndexOf('/') + 1)..] : null;
        var any = false;
        foreach (var pou in ir.Pous)
        {
            if (pouName is not null && !pou.Name.Equals(pouName, StringComparison.OrdinalIgnoreCase)) continue;
            any = true;
            if (actionName is null)
            {
                Console.WriteLine(printer.PrintPou(pou));
                continue;
            }
            var action = pou.FindAction(actionName);
            if (action is null)
            {
                Console.Error.WriteLine($"error: pou {pou.Name} has no action {actionName}");
                return 2;
            }
            Console.WriteLine($"(* {pou.Name}/{action.Name} [{action.Body.Language}] *)");
            Console.WriteLine(printer.Print(action.Body));
        }
        if (!any)
        {
            Console.Error.WriteLine($"error: no pou named {pouName}");
            return 2;
        }
        return 0;
    }

    // ------------------------------------------------------------------ generate csharp

    private static int GenerateCSharp(string[] args)
    {
        var input = args[2];
        var outDir = args[3];
        string? name = null;
        string? runtime = null;
        string? host = null;
        int? eipPort = 44818;
        string? url = null;
        var strict = false;
        var sourceComments = true;
        for (var i = 4; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": name = Next(args, ref i); break;
                case "--runtime": runtime = Next(args, ref i); break;
                case "--host": host = Next(args, ref i); break;
                case "--eip-port": { var v = Next(args, ref i); eipPort = v.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(v); break; }
                case "--url": url = Next(args, ref i); break;
                case "--strict-services": strict = true; break;
                case "--no-source-comments": sourceComments = false; break;
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'");
                    return UsageError();
            }
        }

        var sw = Stopwatch.StartNew();
        IrDocument ir;
        if (input.EndsWith(".L5X", StringComparison.OrdinalIgnoreCase))
        {
            var project = L5xParser.ParseFile(input);
            ir = new LogixIrBuilder(project).Build();
            Console.WriteLine($"Parsed and built plc-ir from {Path.GetFileName(input)} in {sw.Elapsed.TotalSeconds:F1} s");
        }
        else
        {
            ir = IrJson.ReadFile(input);
            Console.WriteLine($"Read {Path.GetFileName(input)} in {sw.Elapsed.TotalSeconds:F1} s");
        }

        var defaults = new Mori.EscaFlow.Converter.CSharp.CSharpGeneratorOptions();
        var options = new Mori.EscaFlow.Converter.CSharp.CSharpGeneratorOptions
        {
            Name = name,
            RuntimeProject = runtime ?? defaults.RuntimeProject,
            HostProject = host ?? defaults.HostProject,
            EipPort = eipPort,
            WebUrl = url ?? defaults.WebUrl,
            StrictServices = strict,
            SourceComments = sourceComments,
        };
        sw.Restart();
        var result = new Mori.EscaFlow.Converter.CSharp.CSharpGenerator(options).Generate(ir);
        result.WriteTo(outDir);
        var report = result.Report;
        Console.WriteLine($"Generated {result.Files.Count} files into {outDir} in {sw.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"  services used: {report.ServiceInventory.Count} distinct ({report.ServiceInventory.Values.Sum()} calls), unsupported: {report.UnsupportedServices.Count} ({string.Join(", ", report.UnsupportedServices.Select(u => u.Service).Take(15))})");
        Console.WriteLine($"  standard blocks: {string.Join(", ", report.StandardBlockInventory.Select(kv => kv.Key + "x" + kv.Value))}");
        foreach (var m in report.Messages.Where(m => m.Severity != "info").Take(20)) Console.WriteLine($"  {m.Severity} {m.Code}: {m.Message} [{m.Location}]");
        return report.HasErrors ? 1 : 0;
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Option {args[i]} needs a value");
        return args[++i];
    }
}
