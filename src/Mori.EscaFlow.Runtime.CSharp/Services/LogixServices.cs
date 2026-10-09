using System.Collections.Concurrent;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// The Logix service catalog at run time. Generated code calls <c>Services.NAME(args)</c> with wrappers and structures
/// passed as objects, keywords as strings and literals as values; <see cref="Call"/> dispatches by name. Override
/// members to plug in real messaging, motion or process control.
/// </summary>
public class LogixServices
{
    private readonly ConcurrentDictionary<string, byte> _logged = new(StringComparer.OrdinalIgnoreCase);

    public LogixServices(PlcController controller) => Controller = controller;

    public PlcController Controller { get; }

    /// <summary>Logs a message once per key (unsupported attribute, ignored write, ...).</summary>
    protected void LogOnce(string key, string message)
    {
        if (_logged.TryAdd(key, 0)) Controller.Log(message);
    }

    /// <summary>Dispatches a service by catalog name.</summary>
    public virtual object? Call(string service, params object?[] args)
    {
        switch (service.ToUpperInvariant())
        {
            case "GSV": GSV(args); return null;
            case "SSV": SSV(args); return null;
            case "MSG": MSG(args); return null;
            case "EVENT": EVENT(args); return null;
            case "IOT": IOT(args); return null;
            case "UID": UID(args); return null;
            case "UIE": UIE(args); return null;
            case "NOP": return null;
            default: Unsupported(service, args); return null;
        }
    }

    /// <summary>GSV(className, instanceName, attributeName, dest).</summary>
    public virtual void GSV(params object?[] args)
    {
        var className = Text(args, 0);
        var instance = args.Length > 1 ? args[1] : null;
        var attribute = Text(args, 2);
        var dest = args.Length > 3 ? args[3] : null;
        if (dest is null) return;
        switch (className.ToUpperInvariant())
        {
            case "WALLCLOCKTIME":
            {
                var now = attribute.Equals("DateTime", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : DateTime.Now;
                if (attribute.Equals("CurrentValue", StringComparison.OrdinalIgnoreCase))
                {
                    Store(dest, (now.ToUniversalTime() - DateTime.UnixEpoch).Ticks / 10);
                    return;
                }
                StoreDateTime(dest, now);
                return;
            }
            case "CONTROLLER":
                switch (attribute.ToUpperInvariant())
                {
                    case "NAME": Store(dest, Controller.Name); return;
                    case "TIMESLICE": Store(dest, 20); return;
                    case "MAJORFAULTRECORD" or "MINORFAULTRECORD": Store(dest, 0); return;
                    default: Unknown(className, attribute, dest); return;
                }
            case "CONTROLLERDEVICE":
                switch (attribute.ToUpperInvariant())
                {
                    case "NAME" or "PRODUCTNAME": Store(dest, Controller.Options.ProductName ?? Controller.Name); return;
                    case "PRODUCTCODE": Store(dest, Controller.Options.ProductCode); return;
                    case "VENDOR": Store(dest, Controller.Options.VendorId); return;
                    case "SERIALNUMBER": Store(dest, 0x00C0FFEE); return;
                    case "MAJORREV" or "MAJORREVISION": Store(dest, 33); return;
                    case "MINORREV" or "MINORREVISION": Store(dest, 1); return;
                    case "STATUS": Store(dest, Controller.State == ControllerState.Running ? 0x0060 : 0x0020); return;
                    default: Unknown(className, attribute, dest); return;
                }
            case "TASK":
            {
                var task = instance is string s && s.Length > 0 && !s.Equals("THIS", StringComparison.OrdinalIgnoreCase) ? Controller.FindTask(s) : Controller.Tasks.FirstOrDefault();
                var stats = task?.Statistics;
                switch (attribute.ToUpperInvariant())
                {
                    case "LASTSCANTIME": Store(dest, (long)((stats?.LastScanMs ?? 0) * 1000)); return;
                    case "MAXSCANTIME": Store(dest, (long)((stats?.MaxScanMs ?? 0) * 1000)); return;
                    case "RATE": Store(dest, (long)((task?.IntervalMs ?? 0) * 1000)); return;
                    case "PRIORITY": Store(dest, task?.Priority ?? 10); return;
                    case "WATCHDOG": Store(dest, (long)((task?.WatchdogMs ?? 500) * 1000)); return;
                    case "OVERLAPCOUNT": Store(dest, stats?.Overruns ?? 0); return;
                    case "STATUS": Store(dest, 0); return;
                    case "INSTANCENUMBER": Store(dest, task is null ? 0 : Controller.Tasks.IndexOf(task) + 1); return;
                    case "ENABLETIMEOUT" or "DISABLEUPDATEOUTPUTS" or "INHIBITTASK": Store(dest, 0); return;
                    default: Unknown(className, attribute, dest); return;
                }
            }
            case "PROGRAM":
            {
                var name = instance as string;
                var task = name is null || name.Equals("THIS", StringComparison.OrdinalIgnoreCase) ? Controller.Tasks.FirstOrDefault() : Controller.Tasks.FirstOrDefault(t => t.Programs.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
                switch (attribute.ToUpperInvariant())
                {
                    case "LASTSCANTIME": Store(dest, (long)((task?.Statistics.LastScanMs ?? 0) * 1000)); return;
                    case "MAXSCANTIME": Store(dest, (long)((task?.Statistics.MaxScanMs ?? 0) * 1000)); return;
                    case "DISABLEFLAG" or "MAJORFAULTRECORD" or "MINORFAULTRECORD" or "SFCRESTART": Store(dest, 0); return;
                    default: Unknown(className, attribute, dest); return;
                }
            }
            case "MODULE":
                switch (attribute.ToUpperInvariant())
                {
                    case "ENTRYSTATUS": Store(dest, 0x4000); return;
                    case "FAULTCODE" or "FAULTINFO" or "MODE" or "FORCESTATUS" or "INSTANCE": Store(dest, 0); return;
                    case "LEDSTATUS": Store(dest, 1); return;
                    case "PATH": Store(dest, instance?.ToString() ?? ""); return;
                    default: Unknown(className, attribute, dest); return;
                }
            case "CST":
                switch (attribute.ToUpperInvariant())
                {
                    case "CURRENTVALUE": Store(dest, (DateTime.UtcNow - DateTime.UnixEpoch).Ticks / 10); return;
                    case "CURRENTSTATUS": Store(dest, 0); return;
                    default: Unknown(className, attribute, dest); return;
                }
            case "FAULTLOG":
                Store(dest, 0);
                return;
            default:
                Unknown(className, attribute, dest);
                return;
        }
    }

    /// <summary>SSV(className, instanceName, attributeName, source): writes to the objects are accepted and logged once.</summary>
    public virtual void SSV(params object?[] args)
    {
        var className = Text(args, 0);
        var attribute = Text(args, 2);
        var source = args.Length > 3 ? args[3] : null;
        switch (className.ToUpperInvariant())
        {
            case "TASK" when attribute.Equals("Rate", StringComparison.OrdinalIgnoreCase):
            case "TASK" when attribute.Equals("Priority", StringComparison.OrdinalIgnoreCase):
            case "PROGRAM" when attribute.Equals("DisableFlag", StringComparison.OrdinalIgnoreCase):
            case "WALLCLOCKTIME":
            case "MODULE":
                LogOnce($"SSV:{className}.{attribute}", $"SSV {className}.{attribute} := {PlcConvert.ToText(source)} accepted (no effect in this runtime)");
                return;
            default:
                LogOnce($"SSV:{className}.{attribute}", $"SSV {className}.{attribute} ignored");
                return;
        }
    }

    /// <summary>MSG(message): the message structure is marked in error (unsupported messaging) until a messaging runtime is plugged in.</summary>
    public virtual void MSG(params object?[] args)
    {
        if (args.Length == 0 || args[0] is not PlcStruct message) return;
        SetBool(message, "EN", true);
        SetBool(message, "DN", false);
        SetBool(message, "EW", false);
        SetBool(message, "ST", false);
        SetBool(message, "ER", true);
        if (message.TryGetMember("ERR", out var err) && err is PlcValue errValue) errValue.SetFrom(0x0001);
        LogOnce("MSG:" + message.Tag.Name + ":" + message.Offset, $"MSG on {message.Tag.Name} is not implemented in this runtime; .ER set");
    }

    /// <summary>EVENT(task): triggers the named event task.</summary>
    public virtual void EVENT(params object?[] args)
    {
        var name = args.Length > 0 ? args[0]?.ToString() : null;
        if (name is null) return;
        var task = Controller.FindTask(name);
        if (task is null)
        {
            LogOnce("EVENT:" + name, $"EVENT: task {name} not found");
            return;
        }
        task.Trigger();
    }

    public virtual void IOT(params object?[] args)
    {
    }

    public virtual void UID(params object?[] args)
    {
    }

    public virtual void UIE(params object?[] args)
    {
    }

    /// <summary>A service the runtime does not implement.</summary>
    public virtual void Unsupported(string service, params object?[] args)
    {
        if (Controller.Options.FaultOnUnsupportedService)
            throw new NotSupportedException($"Logix service {service} is not implemented by this runtime");
        var site = args.Length > 0 && args[0] is IPlcMemory m ? $"{service}:{m.Tag.Name}:{m.Offset}" : service;
        LogOnce("unsupported:" + site, $"service {service} is not implemented by this runtime; call skipped");
    }

    /// <summary>
    /// Prescan of an always-scan service (called once when entering Run for every such instruction). The default
    /// clears the status bits of the instruction's structure (EN, DN, ER, TT, IP, EW, ST) when the first argument is one.
    /// </summary>
    public virtual void Prescan(string service, params object?[] args)
    {
        if (args.Length == 0 || args[0] is not PlcStruct structure) return;
        foreach (var bit in new[] { "EN", "DN", "ER", "TT", "IP", "EW", "ST", "EnableIn", "EnableOut" })
            SetBool(structure, bit, false);
    }

    // ------------------------------------------------------------------ helpers

    private static string Text(object?[] args, int index) => args.Length > index ? PlcConvert.ToText(args[index]) : "";

    private void Unknown(string className, string attribute, object dest)
    {
        LogOnce($"GSV:{className}.{attribute}", $"GSV {className}.{attribute} is not modelled; 0 returned");
        Store(dest, 0);
    }

    /// <summary>Stores a value into a wrapper, string, structure member or array element list.</summary>
    protected static void Store(object dest, object value)
    {
        switch (dest)
        {
            case WrapSTRING s: s.Value = PlcConvert.ToText(value); break;
            case PlcValue v: v.SetFrom(value); break;
            case PlcArrayBase array:
            {
                var first = ((System.Collections.IEnumerable)array).Cast<object>().FirstOrDefault();
                if (first is not null) Store(first, value);
                break;
            }
        }
    }

    private static void StoreDateTime(object dest, DateTime t)
    {
        var parts = new[] { t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, (int)(t.Ticks % TimeSpan.TicksPerSecond / 10) };
        if (dest is PlcArrayBase array)
        {
            var elements = ((System.Collections.IEnumerable)array).Cast<object>().ToList();
            for (var i = 0; i < Math.Min(parts.Length, elements.Count); i++) Store(elements[i], parts[i]);
        }
        else
        {
            Store(dest, parts[0]);
        }
    }

    private static void SetBool(PlcStruct s, string member, bool value)
    {
        if (s.TryGetMember(member, out var m) && m is WrapBOOL b) b.Value = value;
    }
}
