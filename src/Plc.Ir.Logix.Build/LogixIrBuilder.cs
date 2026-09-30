using L5x.Diagnostics;
using L5x.Model;
using L5x.Model.Aoi;
using L5x.Model.DataTypes;
using L5x.Model.Logic;
using L5x.Model.Logic.Fbd;
using L5x.Model.Logic.Sfc;
using L5x.Model.Modules;
using L5x.Model.Programs;
using L5x.Model.Tags;
using L5x.Model.Values;
using L5x.Parsing;
using L5x.Parsing.Linking;
using Plc.Ir.Catalogs;

namespace Plc.Ir.Logix.Build;

public sealed class LogixBuildOptions
{
    public static readonly LogixBuildOptions Default = new();

    /// <summary>Include initial values of variables (the bulk of the file for large projects).</summary>
    public bool IncludeValues { get; init; } = true;

    /// <summary>Keep the original ST lines and rung text next to the trees.</summary>
    public bool IncludeSourceText { get; init; } = true;

    /// <summary>Emit only the types that are actually referenced (plus their member types), not every predefined structure.</summary>
    public bool OnlyReferencedTypes { get; init; } = true;

    /// <summary>Synthesize the prescan (init hook) action of every program and add-on from its logic.</summary>
    public bool SynthesizePrescan { get; init; } = true;
}

/// <summary>Builds the plc-ir 2.0 document from a parsed and linked <see cref="L5xProject"/>.</summary>
public sealed class LogixIrBuilder
{
    private readonly L5xProject _project;
    private readonly Controller _c;
    private readonly LogixBuildOptions _options;
    private readonly ScopeResolver _resolver;
    private readonly Dictionary<Tag, string> _varIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ModuleTagData, string> _moduleVarIds = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _usedTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IrDiagnostic> _diagnostics = new();
    private readonly string _gvlName;

    public LogixIrBuilder(L5xProject project, LogixBuildOptions? options = null)
    {
        _project = project;
        _c = project.Controller;
        _options = options ?? LogixBuildOptions.Default;
        _resolver = new ScopeResolver(_c);
        _gvlName = string.IsNullOrEmpty(_c.Name) ? Logix.ControllerGvl : _c.Name;
        foreach (var tag in _c.AllTags) _varIds[tag] = MakeVarId(tag);
        foreach (var m in _c.Modules)
        {
            var comm = m.Communications;
            if (comm is null) continue;
            var config = comm.ConfigTag ?? comm.ConfigData;
            if (config is not null) _moduleVarIds[config] = ModuleVarIdFor(m, "C");
            foreach (var conn in comm.Connections)
            {
                if (conn.InputTag is not null) _moduleVarIds.TryAdd(conn.InputTag, ModuleVarIdFor(m, "I"));
                if (conn.OutputTag is not null) _moduleVarIds.TryAdd(conn.OutputTag, ModuleVarIdFor(m, "O"));
            }
        }
        Subroutines = new SubroutineIndex(_c);
        Calls = new CallEmitter(this);
    }

    public Controller Controller => _c;
    public SubroutineIndex Subroutines { get; }
    public CallEmitter Calls { get; }

    // ------------------------------------------------------------------ ids

    public string VarId(Tag tag) => _varIds.TryGetValue(tag, out var id) ? id : MakeVarId(tag);

    private string MakeVarId(Tag tag) => tag.Scope switch
    {
        TagScope.Program => $"pou/{tag.Program?.Name}/{tag.Name}",
        TagScope.AoiParameter or TagScope.AoiLocal => $"pou/{tag.Aoi?.Name}/{tag.Name}",
        _ => $"global/{_gvlName}/{tag.Name}",
    };

    public string? ModuleVarId(ModuleTagData data) => _moduleVarIds.GetValueOrDefault(data);

    private static string ModuleVarIdFor(Module m, string suffix) => $"global/{Logix.IoGvl}/{m.Name}:{suffix}";
    private static string ChannelId(Module m, string suffix) => $"io/{m.Name}/{suffix}";
    private static string DeviceId(Module m) => $"device/{m.Name}";

    public bool IsAddOn(string name) => _c.FindAoi(name) is not null;

    // ------------------------------------------------------------------ document

    public IrDocument Build()
    {
        var doc = new IrDocument
        {
            Source = new IrSource
            {
                Vendor = Logix.Id,
                Dialect = "l5x",
                File = _project.SourcePath is null ? null : Path.GetFileName(_project.SourcePath),
                ExportDate = _project.ExportDate,
                SoftwareRevision = _project.SoftwareRevision,
                Generator = "Plc.Ir.Logix.Build " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"),
                GeneratedAt = DateTimeOffset.UtcNow,
            },
            Description = _c.Description,
        };
        doc.SetLogix(new LogixDocumentExt
        {
            TargetName = _project.TargetName,
            TargetType = _project.TargetType,
            MajorRev = _c.MajorRev,
            MinorRev = _c.MinorRev,
            SafetyLevel = _c.SafetyInfo?.SafetyLevel,
        });

        BuildConfiguration(doc);
        BuildGlobals(doc);
        BuildIo(doc);
        foreach (var a in _c.AddOnInstructions) doc.Pous.Add(BuildAoi(a));
        foreach (var p in _c.Programs) doc.Pous.Add(BuildProgram(p));
        BuildTypes(doc);

        foreach (var d in _project.Diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Info && d.Code is "XML001" or "LNK004") continue;
            doc.Diagnostics.Add(new IrDiagnostic { Severity = d.Severity.ToString().ToLowerInvariant(), Code = d.Code, Message = d.Message, Location = d.Location.ToString() });
        }
        doc.Diagnostics.AddRange(_diagnostics);
        return doc;
    }

    private void BuildConfiguration(IrDocument doc)
    {
        var resource = new IrResource { Name = _c.Name, ProcessorType = _c.ProcessorType };
        foreach (var t in _c.Tasks)
        {
            var task = new IrTask
            {
                Name = t.Name,
                Kind = t.Type switch { TaskType.Periodic => "periodic", TaskType.Event => "event", _ => "continuous" },
                IntervalMs = t.Rate,
                Priority = t.Priority,
                WatchdogMs = t.Watchdog,
                Disabled = t.InhibitTask,
                Programs = t.ScheduledPrograms.Select(p => p.Name).ToList(),
                Description = t.Description,
            };
            var trigger = t.EventInfo?["EventTrigger"];
            var eventTag = t.EventInfo?["EventTag"];
            if (trigger is not null || eventTag is not null)
                task.Event = new IrTaskEvent { Kind = trigger ?? "", Source = eventTag is null ? null : (L5xLinker.FindTag(_c, OperandPath.Simple(eventTag), null, null) is { } et ? VarId(et) : eventTag) };
            task.SetLogix(new LogixTaskExt { Type = t.Type.ToString().ToUpperInvariant(), Class = t.Class, EventTrigger = trigger, EventTag = eventTag, InhibitTask = t.InhibitTask, DisableUpdateOutputs = t.DisableUpdateOutputs });
            resource.Tasks.Add(task);
        }
        foreach (var p in _c.Programs)
        {
            if (p.UseAsFolder) continue;
            resource.Programs.Add(new IrProgramInstance { Name = p.Name, Pou = p.Name, Task = p.Task?.Name, Disabled = p.Disabled });
        }
        doc.Configuration = new IrConfiguration { Name = _c.Name, Resources = { resource } };
    }

    // ------------------------------------------------------------------ variables

    private void BuildGlobals(IrDocument doc)
    {
        var gvl = new IrGlobalVariableList { Name = _gvlName, Description = "Controller-scoped tags" };
        foreach (var tag in _c.Tags) gvl.Vars.Add(BuildVariable(tag, "global"));
        doc.Globals.Add(gvl);
    }

    private IrVariable BuildVariable(Tag tag, string section)
    {
        var typeName = tag.DataType?.Name ?? tag.AliasTarget?.DataType?.Name ?? "";
        var v = new IrVariable
        {
            Id = VarId(tag),
            Name = tag.Name,
            Section = section,
            Type = typeName.Length == 0 ? IrTypeRef.Any : LogixTypeMapper.TypeRef(typeName, tag.Dimensions),
            Retain = "retain",
            Constant = tag.Constant,
            Description = tag.Description,
        };
        var ext = new LogixVariableExt
        {
            Path = tag.Scope switch
            {
                TagScope.Program => $"Program:{tag.Program?.Name}.{tag.Name}",
                TagScope.AoiParameter or TagScope.AoiLocal => $"{tag.Aoi?.Name}.{tag.Name}",
                _ => tag.Name,
            },
            Scope = tag.Scope switch
            {
                TagScope.Program => "program",
                TagScope.AoiParameter => "aoiParameter",
                TagScope.AoiLocal => "aoiLocal",
                TagScope.Module => "moduleIo",
                _ => "controller",
            },
            Radix = tag.Radix == Radix.Unknown ? null : tag.Radix.ToString(),
            ExternalAccess = tag.ExternalAccess,
            OpcUaAccess = tag.OpcUaAccess,
            Class = tag.Class,
            Usage = tag.Usage,
            AliasFor = tag.AliasFor,
        };
        if (tag is AoiParameter p)
        {
            ext.Required = p.Required;
            ext.Visible = p.Visible;
            ext.Implicit = p.Name.Equals("EnableIn", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("EnableOut", StringComparison.OrdinalIgnoreCase);
            v.Section = p.ParameterUsage switch { AoiParameterUsage.Output => "output", AoiParameterUsage.InOut => "inOut", _ => "input" };
            v.Retain = "none";
        }
        else if (tag.Scope == TagScope.Program && tag.Usage is { } usage)
        {
            v.Section = usage.ToLowerInvariant() switch { "input" => "input", "output" => "output", "inout" => "inOut", _ => "local" };
        }
        if (tag.TagType == TagType.Alias && tag.AliasFor is not null)
        {
            v.Alias = new IrAlias { Text = tag.AliasFor };
            if (tag.AliasPath is not null)
            {
                var target = new LogixExpressionBuilder(this, _resolver, tag.Program ?? (IRoutineContainer?)tag.Aoi).Ref(tag.AliasPath);
                switch (target)
                {
                    case IrRef r:
                        v.Alias.Target = r;
                        if (r.Type is not null) v.Type = r.Type;
                        break;
                    case IrBitAccess b:
                        // An alias to one bit: the alias is a BOOL; keep the bit host reference and note the bit in the text.
                        v.Alias.Target = b.Of as IrRef;
                        v.Type = IrTypeRef.Named("BOOL");
                        v.Alias.Text = tag.AliasFor;
                        v.Vendor ??= new Dictionary<string, object?>();
                        break;
                }
            }
        }
        if (v.Type.LeafName is { } leaf && !IsAddOn(leaf)) _usedTypes.Add(leaf);
        if (tag.Comments.Count > 0) v.Comments = tag.Comments.GroupBy(c => c.Operand).ToDictionary(g => g.Key, g => g.First().Text);
        var attrs = new Dictionary<string, string>();
        Merge(attrs, "eu", tag.EngineeringUnits);
        Merge(attrs, "min", tag.Mins);
        Merge(attrs, "max", tag.Maxes);
        Merge(attrs, "state0", tag.State0s);
        Merge(attrs, "state1", tag.State1s);
        if (attrs.Count > 0) v.Attributes = attrs;
        if (_options.IncludeValues && tag.Data.Value is { } value) v.Initial = BuildValue(value);
        if (tag.Data.Message is { } m) ext.Message = BuildMessage(m, tag);
        if (tag.ProduceInfo is { } pi) ext.Produced = new LogixProduced { ProduceCount = int.TryParse(pi["ProduceCount"], out var pc) ? pc : null, Attributes = pi.Attributes };
        if (tag.ConsumeInfo is { } ci) ext.Consumed = new LogixConsumed { Producer = ci["Producer"], RemoteTag = ci["RemoteTag"], Rpi = double.TryParse(ci["RPI"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rpi) ? rpi : null, Attributes = ci.Attributes };
        v.SetLogix(ext);
        return v;
    }

    private static void Merge(Dictionary<string, string> attrs, string prefix, Dictionary<string, string>? values)
    {
        if (values is null) return;
        foreach (var (k, val) in values) attrs[prefix + (k.Length == 0 ? "" : ":" + k)] = val;
    }

    private LogixMessage BuildMessage(MessageConfiguration m, Tag tag)
    {
        var ir = new LogixMessage
        {
            MessageType = m.MessageType,
            RequestedLength = m.RequestedLength,
            ConnectedFlag = m.ConnectedFlag,
            ConnectionPath = m.ConnectionPath,
            ConnectionPathSegments = m.ConnectionPathSegments.ToArray(),
            CommTypeCode = m.CommTypeCode,
            ServiceCode = m.ServiceCode,
            ObjectType = m.ObjectType,
            TargetObject = m.TargetObject,
            AttributeNumber = m.AttributeNumber,
            LocalElement = m.LocalElement,
            LocalElementVariable = m.LocalElementTag is null ? null : VarId(m.LocalElementTag),
            RemoteElement = m.RemoteElement,
            DestinationTag = m.DestinationTag,
            DestinationVariable = m.DestinationTagRef is null ? null : VarId(m.DestinationTagRef),
            CacheConnections = m.CacheConnections,
            LargePacketUsage = m.LargePacketUsage,
            SocketService = m.SocketService == SocketService.None ? null : char.ToLowerInvariant(m.SocketService.ToString()[0]) + m.SocketService.ToString()[1..],
            Attributes = m.Attributes,
        };
        if (m.Socket is { } b)
        {
            var x = new LogixExpressionBuilder(this, _resolver, tag.Program ?? (IRoutineContainer?)tag.Aoi);
            var scratch = new DiagnosticBag();
            IrRef? RefOf(string? path)
            {
                if (path is null) return null;
                var op = L5x.Parsing.St.StParser.TryParseOperandPath(path, LogicParseContext.Empty, scratch);
                if (op is null) return new IrRef { Root = IrRefRoot.Unresolved(path), Error = "unparseable path" };
                return x.Ref(op) switch { IrRef r => r, IrBitAccess ba => ba.Of as IrRef, _ => new IrRef { Root = IrRefRoot.Unresolved(path), Error = "not a reference" } };
            }
            var sessionTag = b.Session is null ? null : L5xLinker.FindTag(_c, OperandPath.Simple(b.Session), tag.Program, tag.Aoi);
            ir.Socket = new LogixSocket
            {
                Session = sessionTag is null ? b.Session : VarId(sessionTag),
                Request = RefOf(b.Request),
                Response = RefOf(b.Response),
                Instance = RefOf(b.Instance),
            };
            foreach (var kv in b.Fields)
                if (RefOf(kv.Value) is { } r) ir.Socket.Fields[kv.Key] = r;
        }
        return ir;
    }

    private IrValue BuildValue(TagValue v)
    {
        switch (v)
        {
            case ScalarValue s:
                _usedTypes.Add(s.DataType);
                return new IrScalarValue { Type = s.DataType, Int = s.Kind.IsFloatingPoint() ? null : s.Bits, Real = s.Kind.IsFloatingPoint() ? s.Real : null, Raw = s.RawText };
            case StringValue str:
                _usedTypes.Add(str.DataType);
                return new IrStringValue { Type = str.DataType, Length = str.Length, Text = str.Text };
            case StructValue st:
            {
                if (!IsAddOn(st.DataType)) _usedTypes.Add(st.DataType);
                var ir = new IrStructValue { Type = st.DataType };
                foreach (var kv in st.Members) ir.Members.Add(new IrNamedValue { Name = kv.Key, Value = BuildValue(kv.Value) });
                return ir;
            }
            case ArrayValue a:
            {
                if (!IsAddOn(a.ElementType)) _usedTypes.Add(a.ElementType);
                var ir = new IrArrayValue { Type = a.ElementType, Dims = a.Dimensions };
                foreach (var e in a.Elements) ir.Elements.Add(BuildValue(e));
                return ir;
            }
            default:
                return new IrScalarValue { Type = v.DataType };
        }
    }

    // ------------------------------------------------------------------ I/O

    private void BuildIo(IrDocument doc)
    {
        var gvl = new IrGlobalVariableList { Name = Logix.IoGvl, Description = "Module input, output and configuration images" };
        var byName = new HashSet<string>(_c.Modules.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var m in _c.Modules)
        {
            var parentName = m.Parent?.Name ?? m.ParentModule;
            var device = new IrIoDevice
            {
                Id = DeviceId(m),
                Name = m.Name,
                Catalog = m.CatalogNumber,
                VendorName = m.Vendor,
                Parent = parentName is not null && !parentName.Equals(m.Name, StringComparison.OrdinalIgnoreCase) && byName.Contains(parentName) ? $"device/{parentName}" : null,
                Inhibited = m.Inhibited,
                Description = m.Description,
            };
            var ext = new LogixDeviceExt
            {
                Vendor = m.Vendor,
                ProductType = m.ProductType,
                ProductCode = m.ProductCode,
                Major = m.Major,
                Minor = m.Minor,
                ParentModule = m.ParentModule,
                ParentModPortId = m.ParentModPortId,
                MajorFault = m.MajorFault,
                SafetyEnabled = m.SafetyEnabled,
                SafetyNetwork = m.SafetyNetwork,
                Ports = m.Ports.Select(p => new LogixPort { Id = p.Id, Type = p.Type, Address = p.Address, Upstream = p.Upstream, Width = p.Width }).ToList(),
            };
            if (m.Communications is { } comm)
            {
                ext.CommMethod = comm.CommMethod;
                ext.PrimCxnInputSize = comm.PrimCxnInputSize;
                ext.PrimCxnOutputSize = comm.PrimCxnOutputSize;
                var config = comm.ConfigTag ?? comm.ConfigData;
                if (config is not null) ext.ConfigChannel = AddChannel(doc, gvl, m, "C", "config", config);
                foreach (var conn in comm.Connections)
                {
                    ext.Connections.Add(new LogixConnection
                    {
                        Name = conn.Name,
                        Type = conn.Type,
                        Rpi = conn.Rpi,
                        EventId = conn.EventId,
                        Unicast = conn.Unicast,
                        InputChannel = conn.InputTag is null ? null : AddChannel(doc, gvl, m, "I", "input", conn.InputTag),
                        OutputChannel = conn.OutputTag is null ? null : AddChannel(doc, gvl, m, "O", "output", conn.OutputTag),
                    });
                }
            }
            device.SetLogix(ext);
            doc.Io.Devices.Add(device);
        }
        if (gvl.Vars.Count > 0) doc.Globals.Add(gvl);
    }

    private string AddChannel(IrDocument doc, IrGlobalVariableList gvl, Module m, string suffix, string direction, ModuleTagData data)
    {
        var id = ChannelId(m, suffix);
        if (doc.FindChannel(id) is not null || doc.Io.Channels.Any(c => c.Id == id)) return id;
        var varId = _moduleVarIds.TryGetValue(data, out var existing) ? existing : ModuleVarIdFor(m, suffix);
        var typeName = data.DataTypeName;
        if (typeName is not null) _usedTypes.Add(typeName);
        else _diagnostics.Add(new IrDiagnostic { Severity = "info", Code = "IRB001", Message = $"Module image {m.Name}:{suffix} has no Decorated data in the export; its type is unknown", Location = m.Name });
        var type = typeName is null ? IrTypeRef.Any : IrTypeRef.Named(typeName);
        doc.Io.Channels.Add(new IrIoChannel { Id = id, Device = DeviceId(m), Name = suffix, Direction = direction, Type = type, Variable = varId });
        if (gvl.Vars.All(v => v.Id != varId))
        {
            var v = new IrVariable
            {
                Id = varId,
                Name = $"{m.Name}:{suffix}",
                Section = "global",
                Type = type,
                Retain = "none",
                Io = id,
                Initial = _options.IncludeValues && data.Data.Value is { } value ? BuildValue(value) : null,
                Comments = data.Comments.Count == 0 ? null : data.Comments.GroupBy(c => c.Operand).ToDictionary(g => g.Key, g => g.First().Text),
            };
            v.SetLogix(new LogixVariableExt { Path = $"{m.Name}:{suffix}", Scope = "moduleIo", ExternalAccess = data.ExternalAccess });
            gvl.Vars.Add(v);
        }
        return id;
    }

    // ------------------------------------------------------------------ types

    private void BuildTypes(IrDocument doc)
    {
        var queue = new Queue<string>(_usedTypes);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_options.OnlyReferencedTypes) foreach (var t in _c.Types.All) queue.Enqueue(t.Name);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (AtomicTypes.IsAtomic(name) || IsAddOn(name) || !emitted.Add(name)) continue;
            var def = _c.Types.TryGet(name);
            if (def is null)
            {
                doc.Types.Add(new IrType { Name = name, Kind = "struct", Class = "unresolved", Opaque = true });
                continue;
            }
            if (def is L5x.Model.DataTypes.AtomicType) continue;
            var ir = LogixTypeMapper.Build(def);
            doc.Types.Add(ir);
            foreach (var m in ir.Members ?? new()) if (m.Type.LeafName is { } leaf) queue.Enqueue(leaf);
            if (ir.Logix()?.HiddenMembers is { } hidden) foreach (var h in hidden) queue.Enqueue(h.Type);
        }
        doc.Types.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        doc.InvalidateIndex();
    }

    // ------------------------------------------------------------------ POUs

    private IrPou BuildProgram(Program p)
    {
        var pou = new IrPou { Name = p.Name, Kind = "program", Description = p.Description };
        foreach (var t in p.Tags) pou.Vars.Add(BuildVariable(t, "local"));
        var x = new LogixExpressionBuilder(this, _resolver, p);
        var bodies = new Dictionary<Routine, IrBody>(ReferenceEqualityComparer.Instance);
        foreach (var r in p.Routines)
        {
            var action = BuildAction(r, x, out var body);
            bodies[r] = body;
            pou.Actions.Add(action);
        }
        foreach (var encoded in p.EncodedRoutines) AddEncodedAction(pou, encoded);
        var main = p.MainRoutine?.Name ?? p.MainRoutineName;
        pou.Body = new IrBody { Language = "st", Synthetic = true };
        if (main is not null && pou.FindAction(main) is not null)
            pou.Body.Statements.Add(new IrActionCall { Action = main });
        if (p.FaultRoutineName is { } fault && pou.FindAction(fault) is not null) pou.OnFault = fault;
        if (_options.SynthesizePrescan)
        {
            var init = new IrAction { Name = UniqueActionName(pou, "Prescan"), Hook = "init", Synthetic = true, Body = new IrBody { Language = "st", Synthetic = true } };
            foreach (var r in p.Routines) init.Body.Statements.AddRange(PrescanOf(r, bodies[r], x));
            pou.Actions.Add(init);
        }
        pou.SetLogix(new LogixPouExt
        {
            Kind = "program",
            Class = p.Class,
            MainRoutine = p.MainRoutineName,
            FaultRoutine = p.FaultRoutineName,
            Disabled = p.Disabled,
            Folder = p.UseAsFolder,
            ParentFolder = p.ParentProgram?.Name,
            Children = p.ChildPrograms.Count == 0 ? null : p.ChildPrograms.Select(c => c.Name).ToList(),
            Task = p.Task?.Name,
        });
        return pou;
    }

    private IrPou BuildAoi(AddOnInstructionDefinition a)
    {
        var pou = new IrPou { Name = a.Name, Kind = "functionBlock", Description = a.Description };
        foreach (var param in a.Parameters) pou.Vars.Add(BuildVariable(param, "input"));
        foreach (var t in a.LocalTags) pou.Vars.Add(BuildVariable(t, "local"));
        var x = new LogixExpressionBuilder(this, _resolver, a);
        IrBody? logicBody = null;
        Routine? logic = null;
        foreach (var r in a.Routines)
        {
            var action = BuildAction(r, x, out var body);
            var mode = r.Name.ToUpperInvariant() switch { "LOGIC" => "Logic", "PRESCAN" => "Prescan", "POSTSCAN" => "Postscan", "ENABLEINFALSE" => "EnableInFalse", _ => null };
            action.SetLogix(new LogixActionExt { RoutineType = r.Type.ToString(), ScanMode = mode });
            switch (mode)
            {
                case "Logic":
                    pou.Body = body;
                    logicBody = body;
                    logic = r;
                    continue;
                case "Postscan" when a.ExecutePostscan:
                    action.Hook = "postscan";
                    break;
                case "EnableInFalse" when a.ExecuteEnableInFalse:
                    action.Hook = "enableFalse";
                    break;
            }
            pou.Actions.Add(action);
        }
        foreach (var encoded in a.EncodedRoutines) AddEncodedAction(pou, encoded);
        pou.Body ??= new IrBody { Language = "empty" };
        if (_options.SynthesizePrescan)
        {
            var init = new IrAction { Name = UniqueActionName(pou, "Init"), Hook = "init", Synthetic = true, Body = new IrBody { Language = "st", Synthetic = true } };
            if (logic is not null && logicBody is not null) init.Body.Statements.AddRange(PrescanOf(logic, logicBody, x));
            if (a.ExecutePrescan && pou.FindAction("Prescan") is not null) init.Body.Statements.Add(new IrActionCall { Action = "Prescan" });
            pou.Actions.Add(init);
        }
        pou.SetLogix(new LogixPouExt
        {
            Kind = "addOn",
            Revision = a.Revision,
            RevisionExtension = a.RevisionExtension,
            Vendor = a.Vendor,
            Class = a.Class,
            ExecutePrescan = a.ExecutePrescan,
            ExecutePostscan = a.ExecutePostscan,
            ExecuteEnableInFalse = a.ExecuteEnableInFalse,
            BackingType = a.Name,
        });
        return pou;
    }

    /// <summary>A source-protected routine: present in the export as encoded data only, so it becomes an action with an encrypted body.</summary>
    private void AddEncodedAction(IrPou pou, GenericElement encoded)
    {
        var name = encoded["Name"];
        if (name is null || pou.FindAction(name) is not null) return;
        var action = new IrAction { Name = name, Body = new IrBody { Language = "encrypted" }, Description = encoded["Description"] };
        action.SetLogix(new LogixActionExt { RoutineType = encoded["Type"] });
        pou.Actions.Add(action);
        _diagnostics.Add(new IrDiagnostic { Severity = "info", Code = "IRB002", Message = $"Routine {pou.Name}/{name} is source-protected in the export; its body is not available", Location = pou.Name });
    }

    private static string UniqueActionName(IrPou pou, string name)
    {
        var n = name;
        while (pou.FindAction(n) is not null) n += "_";
        return n;
    }

    private IrAction BuildAction(Routine r, LogixExpressionBuilder x, out IrBody body)
    {
        body = BuildBody(r, x);
        var action = new IrAction
        {
            Name = r.Name,
            Description = r.Description,
            Body = body,
            Calls = r.CalledRoutines.Count == 0 ? null : r.CalledRoutines.Select(c => c.Name).ToList(),
        };
        action.SetLogix(new LogixActionExt { RoutineType = r.Type.ToString() });
        return action;
    }

    private IrBody BuildBody(Routine r, LogixExpressionBuilder x)
    {
        var body = new IrBody();
        var location = $"{(x.Container as Program)?.Name ?? (x.Container as AddOnInstructionDefinition)?.Name}/{r.Name}";
        switch (r.Content)
        {
            case RllContent rll:
            {
                body.Language = "ld";
                var temps = new List<IrTemporary>();
                var lowering = new LadderLowering(x, Calls, temps);
                var ld = new IrLdDiagram();
                foreach (var rung in rll.Rungs)
                {
                    var irRung = new IrRung { Number = rung.Number, Comment = rung.Comment, Text = _options.IncludeSourceText ? rung.Text : null };
                    if (rung.Ast is not null) irRung.Elements = lowering.Lower(rung.Ast, rung.Number, body.Statements);
                    if (rung.Type != "N") irRung.SetLogix(new LogixRungExt { Type = rung.Type });
                    ld.Rungs.Add(irRung);
                }
                body.Temporaries = temps.Count == 0 ? null : temps;
                body.Diagram = ld;
                break;
            }
            case StContent st:
                body.Language = "st";
                body.Statements = st.Ast is null ? new List<IrStatement>() : x.Block(st.Ast);
                body.Comments = st.Comments?.Select(c => new IrComment { Text = c.Text, Line = c.Line }).ToList();
                body.Text = _options.IncludeSourceText ? st.Lines.Select(l => l.Text).ToList() : null;
                break;
            case SfcContent sfc:
                body.Language = "sfc";
                body.Diagram = BuildSfc(sfc, x);
                break;
            case FbdContent fbd:
            {
                body.Language = "fbd";
                var temps = new List<IrTemporary>();
                body.Diagram = new NetLowering(x, Calls, temps, _diagnostics, location).Lower(fbd, body.Statements);
                body.Temporaries = temps.Count == 0 ? null : temps;
                break;
            }
            case null when r.EncryptedContent is not null:
                body.Language = "encrypted";
                break;
            default:
                body.Language = "empty";
                break;
        }
        if (r.Content?.OnlineEditType is { } oet) body.SetLogix(new LogixBodyExt { OnlineEditType = oet });
        return body;
    }

    // ------------------------------------------------------------------ prescan synthesis

    /// <summary>Prescan behaviour of one routine: ladder is re-lowered in prescan mode; ST and FBD bodies invoke their function blocks and always-scan services in prescan mode.</summary>
    private List<IrStatement> PrescanOf(Routine r, IrBody body, LogixExpressionBuilder x)
    {
        var list = new List<IrStatement>();
        switch (r.Content)
        {
            case RllContent rll:
            {
                var lowering = new LadderLowering(x, Calls, new List<IrTemporary>());
                foreach (var rung in rll.Rungs)
                    if (rung.Ast is not null) list.AddRange(lowering.LowerPrescan(rung.Ast, rung.Number));
                break;
            }
            case StContent or FbdContent:
                CollectPrescan(body.Statements, list);
                break;
        }
        return list;
    }

    private void CollectPrescan(IEnumerable<IrStatement> statements, List<IrStatement> list)
    {
        foreach (var s in statements)
        {
            switch (s)
            {
                case IrFbCall c when c.Mode == "normal":
                {
                    var p = new IrFbCall { Instance = c.Instance, Fb = c.Fb, Pou = c.Pou, Profile = c.Profile, Mode = "prescan", Src = c.Src };
                    if (c.Fb is not null)
                    {
                        foreach (var (pin, value) in c.Inputs)
                            if (value.IsBool) p.Inputs[pin] = IrLiteral.False;
                    }
                    else if (c.Pou is not null && _c.FindAoi(c.Pou) is { } aoi)
                    {
                        foreach (var (pin, value) in c.Inputs)
                            if (aoi.FindParameter(pin) is { ParameterUsage: AoiParameterUsage.InOut }) p.Inputs[pin] = value;
                    }
                    list.Add(p);
                    break;
                }
                case IrSysCall sc when sc.Mode == "normal" && LogixServiceCatalog.Instance.RunsEveryScan(sc.Service):
                    list.Add(new IrSysCall { Catalog = sc.Catalog, Service = sc.Service, Args = sc.Args, Mode = "prescan", Src = sc.Src });
                    break;
                case IrIf i:
                    foreach (var b in i.Branches) CollectPrescan(b.Body, list);
                    if (i.Else is not null) CollectPrescan(i.Else, list);
                    break;
                case IrCase c:
                    foreach (var arm in c.Arms) CollectPrescan(arm.Body, list);
                    if (c.Else is not null) CollectPrescan(c.Else, list);
                    break;
                case IrFor f: CollectPrescan(f.Body, list); break;
                case IrWhile w: CollectPrescan(w.Body, list); break;
                case IrRepeat rp: CollectPrescan(rp.Body, list); break;
            }
        }
    }

    // ------------------------------------------------------------------ SFC

    private IrSfcDiagram BuildSfc(SfcContent sfc, LogixExpressionBuilder x)
    {
        var ir = new IrSfcDiagram();
        string? VarIdByName(string? name)
        {
            if (name is null) return null;
            var tag = L5xLinker.FindTag(_c, OperandPath.Simple(name), x.Container as Program, x.Container as AddOnInstructionDefinition);
            return tag is null ? null : VarId(tag);
        }
        foreach (var node in sfc.Nodes)
        {
            switch (node)
            {
                case SfcStep s:
                    ir.Steps.Add(new IrSfcStep
                    {
                        Id = s.Id,
                        Name = s.Operand ?? $"Step_{s.Id}",
                        Variable = VarIdByName(s.Operand),
                        Initial = s.InitialStep,
                        Description = s.Description,
                        Preset = s.Preset?.Expression is null ? null : x.Expr(s.Preset.Expression),
                        LimitHigh = s.LimitHigh?.Expression is null ? null : x.Expr(s.LimitHigh.Expression),
                        LimitLow = s.LimitLow?.Expression is null ? null : x.Expr(s.LimitLow.Expression),
                        X = s.X,
                        Y = s.Y,
                        Actions = s.Actions.Select(a => new IrSfcAction
                        {
                            Id = a.Id,
                            Name = a.Operand ?? $"Action_{a.Id}",
                            Variable = VarIdByName(a.Operand),
                            Qualifier = NormalizeQualifier(a.Qualifier),
                            Boolean = a.IsBoolean,
                            Preset = a.Preset?.Expression is null ? null : x.Expr(a.Preset.Expression),
                            Body = a.Body is null ? null : new IrBody
                            {
                                Language = "st",
                                Statements = a.Body.Ast is null ? new List<IrStatement>() : x.Block(a.Body.Ast),
                                Text = _options.IncludeSourceText ? a.Body.Lines.Select(l => l.Text).ToList() : null,
                                Comments = a.Body.Comments?.Select(c => new IrComment { Text = c.Text, Line = c.Line }).ToList(),
                            },
                            Description = a.Description,
                        }).ToList(),
                    });
                    break;
                case SfcTransition t:
                    ir.Transitions.Add(new IrSfcTransition
                    {
                        Id = t.Id,
                        Name = t.Operand ?? $"Tran_{t.Id}",
                        Variable = VarIdByName(t.Operand),
                        Condition = t.Condition?.Expression is null ? null : x.Expr(t.Condition.Expression),
                        Force = t.Force,
                        Description = t.Description,
                        X = t.X,
                        Y = t.Y,
                    });
                    break;
                case SfcBranch b:
                    ir.Branches.Add(new IrSfcBranch
                    {
                        Id = b.Id,
                        Type = b.BranchType == SfcBranchType.Simultaneous ? "simultaneous" : "selection",
                        Flow = b.BranchFlow == SfcBranchFlow.Converge ? "converge" : "diverge",
                        Priority = b.Priority,
                        Legs = b.Legs.Select(l => l.Id).ToList(),
                    });
                    break;
                case SfcStop st:
                    ir.Stops.Add(new IrSfcStop { Id = st.Id, Name = st.Operand ?? $"Stop_{st.Id}", Variable = VarIdByName(st.Operand), Description = st.Description });
                    break;
            }
        }
        foreach (var l in sfc.Links) ir.Links.Add(new IrSfcLink { From = l.FromId, To = l.ToId });
        return ir;
    }

    private static string NormalizeQualifier(string? q) => q switch
    {
        null => "N",
        "NonStored" => "N",
        "Stored" => "S",
        "Reset" => "R",
        "TimeLimited" => "L",
        "TimeDelayed" => "D",
        "Pulse" => "P",
        "PulseRising" => "P1",
        "PulseRisingEdge" => "P1",
        "PulseFalling" => "P0",
        "PulseFallingEdge" => "P0",
        "StoredTimeLimited" => "SL",
        "StoredTimeDelayed" => "SD",
        "TimeDelayedStored" => "DS",
        _ => q,
    };
}
