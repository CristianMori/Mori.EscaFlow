using System.Xml;
using L5x.Model;
using L5x.Model.Tags;

namespace L5x.Parsing.Xml;

/// <summary>Reads the <c>&lt;Controller&gt;</c> element and its sections in file order.</summary>
public sealed class ControllerReader
{
    private readonly ReaderContext _ctx;
    private readonly TagReader _tags;
    private readonly RoutineReader _routines;
    private readonly DataTypeReader _dataTypes;
    private readonly ModuleReader _modules;
    private readonly AoiReader _aois;
    private readonly ProgramReader _programs;

    public ControllerReader(ReaderContext ctx)
    {
        _ctx = ctx;
        _tags = new TagReader(ctx);
        _routines = new RoutineReader(ctx);
        _dataTypes = new DataTypeReader(ctx);
        _modules = new ModuleReader(ctx, _tags);
        _aois = new AoiReader(ctx, _tags, _routines);
        _programs = new ProgramReader(ctx, _tags, _routines);
    }

    /// <summary>Raised after the AddOnInstructionDefinitions section closes, when all AOI names are known.</summary>
    public event Action<Controller>? AddOnInstructionsComplete;

    public Controller Read(XmlReader reader)
    {
        var line = reader.LineNumber();
        var attrs = new AttributeBag(reader);
        var controller = new Controller
        {
            Name = attrs.TakeRequired("Name", "?"),
            Use = attrs.Take("Use"),
            ProcessorType = attrs.Take("ProcessorType"),
            MajorRev = attrs.TakeInt("MajorRev"),
            MinorRev = attrs.TakeInt("MinorRev"),
            ProjectCreationDate = attrs.Take("ProjectCreationDate"),
            LastModifiedDate = attrs.Take("LastModifiedDate"),
            SfcExecutionControl = attrs.Take("SFCExecutionControl"),
            SfcRestartPosition = attrs.Take("SFCRestartPosition"),
            SfcLastScan = attrs.Take("SFCLastScan"),
            ProjectSN = attrs.Take("ProjectSN"),
            SourceLine = line,
        };
        foreach (var kv in attrs.Remaining() ?? new Dictionary<string, string>()) controller.Attributes[kv.Key] = kv.Value;

        var aoisDone = false;
        reader.ForEachChild((r, child) =>
        {
            switch (child)
            {
                case "Description":
                    controller.Description = r.ReadNormalizedText();
                    break;
                case "RedundancyInfo":
                    controller.RedundancyInfo = _ctx.ReadGeneric(r);
                    break;
                case "Security":
                    controller.Security = _ctx.ReadGeneric(r);
                    break;
                case "SafetyInfo":
                    controller.SafetyInfo = ReadSafetyInfo(r);
                    break;
                case "DataTypes":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "DataType")
                        {
                            controller.DataTypes.Add(_dataTypes.Read(rr.ReadElement()));
                            _ctx.Statistics.DataTypes++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "DataTypes", controller);
                        }
                    });
                    break;
                case "Modules":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Module")
                        {
                            controller.Modules.Add(_modules.Read(rr.ReadElement()));
                            _ctx.Statistics.Modules++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "Modules", controller);
                        }
                    });
                    break;
                case "AddOnInstructionDefinitions":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "AddOnInstructionDefinition")
                        {
                            var aoi = _aois.Read(rr);
                            controller.AddOnInstructions.Add(aoi);
                            _ctx.AoiNames.Add(aoi.Name);
                            _ctx.Statistics.AddOnInstructions++;
                        }
                        else if (c == "EncodedData" || c == "EncryptedAOIContent")
                        {
                            controller.Other.Add(_ctx.ReadGeneric(rr));
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "AddOnInstructionDefinitions", controller);
                        }
                    });
                    aoisDone = true;
                    AddOnInstructionsComplete?.Invoke(controller);
                    break;
                case "Tags":
                    EnsureAoisDone(ref aoisDone, controller);
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Tag")
                        {
                            controller.Tags.Add(_tags.Read(rr, TagScope.Controller, null));
                            _ctx.Statistics.ControllerTags++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "Tags", controller);
                        }
                    });
                    break;
                case "Programs":
                    EnsureAoisDone(ref aoisDone, controller);
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Program") controller.Programs.Add(_programs.Read(rr));
                        else _ctx.Unexpected(rr, "Programs", controller);
                    });
                    break;
                case "Tasks":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Task")
                        {
                            controller.Tasks.Add(TaskReader.Read(rr.ReadElement()));
                            _ctx.Statistics.Tasks++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "Tasks", controller);
                        }
                    });
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    r.Skip();
                    break;
                default:
                    // CST, WallClockTime, Trends, DataLogs, TimeSynchronize, EthernetPorts, ParameterConnections, ...
                    controller.Other.Add(_ctx.ReadGeneric(r));
                    break;
            }
        });
        EnsureAoisDone(ref aoisDone, controller);
        return controller;
    }

    private void EnsureAoisDone(ref bool done, Controller controller)
    {
        if (done) return;
        done = true;
        AddOnInstructionsComplete?.Invoke(controller);
    }

    private SafetyInfo ReadSafetyInfo(XmlReader reader)
    {
        var attrs = new AttributeBag(reader);
        var info = new SafetyInfo
        {
            SafetySignature = attrs.Take("SafetySignature"),
            SafetyLocked = attrs.TakeBoolNullable("SafetyLocked"),
            SignatureRunModeProtect = attrs.TakeBoolNullable("SignatureRunModeProtect"),
            ConfigureSafetyIOAlways = attrs.TakeBoolNullable("ConfigureSafetyIOAlways"),
            SafetyLevel = attrs.Take("SafetyLevel"),
            SourceLine = reader.LineNumber(),
        };
        foreach (var kv in attrs.Remaining() ?? new Dictionary<string, string>()) info.Attributes[kv.Key] = kv.Value;
        reader.ForEachChild((r, child) =>
        {
            if (child == "SafetyTagMap")
            {
                var text = r.ReadNormalizedText();
                foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0) info.SafetyTagMap.Add(new KeyValuePair<string, string>(pair[..eq].Trim(), pair[(eq + 1)..].Trim()));
                }
            }
            else
            {
                (info.UnknownChildren ??= new()).Add(_ctx.ReadGeneric(r));
            }
        });
        return info;
    }
}
