using System.Collections.Concurrent;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// The Logix service catalog at run time. Generated code calls <c>Services.NAME(args)</c> with wrappers and structures
/// passed as objects, keywords as strings and literals as values; <see cref="Call"/> dispatches by name. Override
/// members to plug in real messaging, motion or process control.
/// </summary>
/// <remarks>
/// Everything the middle format could not express as a neutral statement arrives here as a vendor service with
/// positional arguments, in the order the Logix instruction lists its operands. Arguments that are tags come as
/// their wrapper or structure view (so the service can write results back), enumeration operands as strings, and
/// immediates as boxed numbers. The policy for a service the runtime lacks is deliberate: it is never silent. It is
/// reported by the converter, logged once per call site here, and optionally turned into a major fault.
/// </remarks>
public class LogixServices
{
    /// <summary>Keys already logged, so a message about one call site appears once rather than every scan.</summary>
    private readonly ConcurrentDictionary<string, byte> _logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Binds the services to their controller.</summary>
    public LogixServices(PlcController controller) => Controller = controller;

    /// <summary>The owning controller (tasks, options, log).</summary>
    public PlcController Controller { get; }

    /// <summary>Logs a message once per key (unsupported attribute, ignored write, ...).</summary>
    protected void LogOnce(string key, string message)
    {
        if (_logged.TryAdd(key, 0)) Controller.Log(message);
    }

    /// <summary>Dispatches a service by catalog name.</summary>
    /// <remarks>The generated code calls the typed methods directly; this entry point serves tests, hosts and any dynamic caller.</remarks>
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
    /// <remarks>
    /// Get System Value reads a controller object attribute into a tag. The objects modelled are the ones projects
    /// read in practice: WallClockTime (DateTime as a DINT[7] of year, month, day, hour, minute, second,
    /// microsecond, or CurrentValue as microseconds since the epoch), Controller and ControllerDevice identity,
    /// Task and Program scan times in microseconds (Logix reports them in microseconds, the statistics keep
    /// milliseconds), Module status (always a healthy entry status, since there is no real I/O), CST and FaultLog.
    /// Unknown attributes store 0 and are logged once, so logic that branches on them keeps running.
    /// </remarks>
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
                    // Microseconds since 1970 as a LINT, the form Logix returns.
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
                // "THIS" (or no instance) means the task that is executing the instruction; the first task stands in for it.
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
                    // 0x4000 = "running" entry status: logic that checks module health sees healthy modules.
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
    /// <remarks>Set System Value changes controller objects (task rates, program disable flags, the wall clock). None of those is modelled yet, so the write is acknowledged for the attributes a real controller accepts and ignored for the rest, in both cases with a single log line.</remarks>
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
    /// <remarks>
    /// Logic that sends messages usually waits on <c>.DN</c> or <c>.ER</c> and retries; marking the message in error
    /// with a nonzero <c>.ERR</c> lets such logic complete its state machine instead of hanging on <c>.EN</c>. A
    /// real implementation (CIP data table reads and writes, generic CIP, socket services) replaces this method.
    /// </remarks>
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

    /// <summary>IOT(outputTag): immediate output update. There is no physical output path yet, so nothing to flush.</summary>
    public virtual void IOT(params object?[] args)
    {
    }

    /// <summary>UID: user interrupt disable. Tasks are never interrupted mid-rung here, so it is a no-op.</summary>
    public virtual void UID(params object?[] args)
    {
    }

    /// <summary>UIE: user interrupt enable (see <see cref="UID"/>).</summary>
    public virtual void UIE(params object?[] args)
    {
    }

    /// <summary>A service the runtime does not implement.</summary>
    /// <remarks>Faults the controller when <see cref="PlcControllerOptions.FaultOnUnsupportedService"/> is set; otherwise logs once per call site (service plus the first argument's tag and offset) and skips the call, which keeps the rest of the project scanning.</remarks>
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
    /// <remarks>Logix prescans every instruction with its own rules; for the structure-backed instructions (MSG, PID, alarms, FBD blocks) the common effect is that the status bits start cleared, which this default reproduces without knowing each instruction.</remarks>
    public virtual void Prescan(string service, params object?[] args)
    {
        if (args.Length == 0 || args[0] is not PlcStruct structure) return;
        foreach (var bit in new[] { "EN", "DN", "ER", "TT", "IP", "EW", "ST", "EnableIn", "EnableOut" })
            SetBool(structure, bit, false);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The argument at <paramref name="index"/> as text, or empty when absent.</summary>
    private static string Text(object?[] args, int index) => args.Length > index ? PlcConvert.ToText(args[index]) : "";

    /// <summary>Stores 0 for an attribute that is not modelled and logs it once.</summary>
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
                // A scalar stored into an array lands in its first element, as a GSV into Array[0] would.
                var first = ((System.Collections.IEnumerable)array).Cast<object>().FirstOrDefault();
                if (first is not null) Store(first, value);
                break;
            }
        }
    }

    /// <summary>Stores a date and time the way WallClockTime does: a DINT[7] of year, month, day, hour, minute, second, microsecond (or just the year into a scalar).</summary>
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

    /// <summary>Sets a BOOL member by name when the structure has one.</summary>
    private static void SetBool(PlcStruct s, string member, bool value)
    {
        if (s.TryGetMember(member, out var m) && m is WrapBOOL b) b.Value = value;
    }
}
