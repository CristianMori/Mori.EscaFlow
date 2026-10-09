using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// Reads a materialized <c>&lt;Task&gt;</c> element. Logix tasks are continuous (one per controller, runs whenever
/// nothing else does), periodic (rate in milliseconds, preempting by priority) or event (triggered by a module
/// input change, a consumed tag, an EVENT instruction, an axis or a motion group). The scheduled programs run
/// in the listed order on every scan of the task.
/// </summary>
public static class TaskReader
{
    public static L5xTask Read(XElement element)
    {
        var attrs = new AttributeBag(element);
        var typeText = attrs.Take("Type");
        var task = new L5xTask
        {
            Name = attrs.TakeRequired("Name", "?"),
            Type = typeText?.ToUpperInvariant() switch
            {
                "CONTINUOUS" => TaskType.Continuous,
                "PERIODIC" => TaskType.Periodic,
                "EVENT" => TaskType.Event,
                _ => TaskType.Unknown,
            },
            Class = attrs.Take("Class"),
            Rate = attrs.TakeDouble("Rate"),
            Priority = attrs.TakeInt("Priority"),
            Watchdog = attrs.TakeDouble("Watchdog"),
            DisableUpdateOutputs = attrs.TakeBool("DisableUpdateOutputs"),
            InhibitTask = attrs.TakeBool("InhibitTask"),
            SourceLine = element.Line(),
        };
        task.UnknownAttributes = attrs.Remaining();
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description":
                    task.Description = child.OwnText();
                    break;
                case "EventInfo":
                    // EventTrigger / EventTag / EnableTimeout: kept generic, the importer reads the attributes it needs.
                    task.EventInfo = GenericElementReader.Read(child);
                    break;
                case "ScheduledPrograms":
                    foreach (var p in child.Elements("ScheduledProgram"))
                    {
                        var n = p.Attr("Name");
                        if (n is not null) task.ScheduledProgramNames.Add(n);
                    }
                    break;
                default:
                    (task.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
                    break;
            }
        }
        return task;
    }
}
