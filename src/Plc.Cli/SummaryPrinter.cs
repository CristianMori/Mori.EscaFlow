using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Tags;

namespace Plc.Cli;

public static class SummaryPrinter
{
    public static void Print(L5xProject project, TextWriter w)
    {
        var c = project.Controller;
        var s = project.Statistics;
        w.WriteLine($"{project.TargetName} ({project.TargetType}) exported by v{project.SoftwareRevision} on {project.ExportDate}");
        w.WriteLine($"Controller {c.Name} {c.ProcessorType} v{c.MajorRev}.{c.MinorRev}");
        w.WriteLine();
        w.WriteLine($"{"Data types",-22}{s.DataTypes,10}");
        w.WriteLine($"{"Modules",-22}{s.Modules,10}");
        w.WriteLine($"{"Add-on instructions",-22}{s.AddOnInstructions,10}   ({s.AoiParameters} parameters, {s.AoiLocalTags} local tags)");
        w.WriteLine($"{"Controller tags",-22}{s.ControllerTags,10}");
        w.WriteLine($"{"Program tags",-22}{s.ProgramTags,10}");
        w.WriteLine($"{"Programs",-22}{s.Programs,10}");
        w.WriteLine($"{"Routines",-22}{s.Routines,10}   (RLL {s.RllRoutines}, ST {s.StRoutines}, SFC {s.SfcRoutines}, FBD {s.FbdRoutines})");
        w.WriteLine($"{"Rungs",-22}{s.Rungs,10}");
        w.WriteLine($"{"ST lines",-22}{s.StLines,10}");
        w.WriteLine($"{"SFC nodes",-22}{s.SfcNodes,10}");
        w.WriteLine($"{"Tasks",-22}{s.Tasks,10}");
        w.WriteLine($"{"Value nodes",-22}{s.ValueNodes,10}");
        w.WriteLine($"{"CustomProperties skipped",-22}{s.SkippedCustomProperties,8}");
        w.WriteLine();

        var alias = c.AllTags.Count(t => t.TagType == TagType.Alias);
        var produced = c.AllTags.Count(t => t.TagType == TagType.Produced);
        var consumed = c.AllTags.Count(t => t.TagType == TagType.Consumed);
        var withValue = c.AllTags.Count(t => t.Data.Value is not null);
        w.WriteLine($"Tags: {alias} alias, {produced} produced, {consumed} consumed, {withValue} with a parsed value");
        var unresolvedTypes = c.AllTags.Count(t => t.DataType is { Definition: null });
        w.WriteLine($"Registry: {c.Types.All.Count} data types known, {unresolvedTypes} tag type references unresolved");
        w.WriteLine();

        w.WriteLine($"{"XML phase",-14}{s.XmlPhase.TotalSeconds,8:F2} s");
        w.WriteLine($"{"Logic phase",-14}{s.LogicPhase.TotalSeconds,8:F2} s");
        w.WriteLine($"{"Link phase",-14}{s.LinkPhase.TotalSeconds,8:F2} s");
        w.WriteLine($"{"Total",-14}{s.Total.TotalSeconds,8:F2} s");
        w.WriteLine($"{"Peak WS",-14}{s.PeakWorkingSetBytes / (1024.0 * 1024.0),8:F0} MB");
        w.WriteLine($"{"Managed heap",-14}{GC.GetTotalMemory(false) / (1024.0 * 1024.0),8:F0} MB");
        w.WriteLine();

        var diags = project.Diagnostics;
        w.WriteLine($"Diagnostics: {diags.Count} ({diags.CountBySeverity(DiagnosticSeverity.Error)} errors, {diags.CountBySeverity(DiagnosticSeverity.Warning)} warnings, {diags.CountBySeverity(DiagnosticSeverity.Info)} info)");
        foreach (var g in diags.GroupBy(d => d.Code).OrderBy(g => g.Key))
        {
            var first = g.First();
            w.WriteLine($"  {g.Key,-8}{g.Count(),8}  {first.Severity,-8} e.g. {Truncate(first.Message, 90)} [{first.Location}]");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
