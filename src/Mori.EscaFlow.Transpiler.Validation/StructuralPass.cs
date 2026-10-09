using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.Validation;

/// <summary>
/// Walks the whole object graph reflectively: enum values, empty names, id patterns and uniqueness, and every
/// <see cref="IrRefKindAttribute"/> / <see cref="IrTypeRef"/> occurrence (referential checks that need no logic context).
/// </summary>
internal sealed class StructuralPass
{
    private readonly IrDocument _doc;
    private readonly ValidationResult _result;
    private readonly TypeResolver _types;
    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();
    private static readonly Assembly ModelAssembly = typeof(IrDocument).Assembly;

    private Dictionary<string, IrTask> _tasks = new();
    private Dictionary<string, IrProgramInstance> _instances = new();

    public StructuralPass(IrDocument doc, ValidationResult result, TypeResolver types)
    {
        _doc = doc;
        _result = result;
        _types = types;
    }

    public void Run()
    {
        if (!string.Equals(_doc.Format, IrDocument.FormatId, StringComparison.Ordinal))
            _result.Error(ValidationCodes.FormatVersion, $"format is '{_doc.Format}', expected '{IrDocument.FormatId}'", "document");
        var major = _doc.IrVersion.Split('.')[0];
        if (major != IrDocument.CurrentVersion.Split('.')[0])
            _result.Error(ValidationCodes.FormatVersion, $"irVersion {_doc.IrVersion} is not 2.x", "document");

        CheckIds();
        Walk(_doc, "document", null);
    }

    // ------------------------------------------------------------------ ids

    private void CheckIds()
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in _doc.Globals)
        {
            RequireName(g.Name, "globals", "global list name");
            foreach (var v in g.Vars) CheckVariable(v, $"global/{g.Name}/{v.Name}", variables, $"globals[{g.Name}]");
        }
        var pous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _doc.Pous)
        {
            RequireName(p.Name, "pous", "pou name");
            if (!pous.Add(p.Name)) _result.Error(ValidationCodes.DuplicateId, $"pou '{p.Name}' is declared twice", $"pous[{p.Name}]");
            foreach (var v in p.Vars) CheckVariable(v, $"pou/{p.Name}/{v.Name}", variables, $"pous[{p.Name}]");
            var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Actions)
            {
                RequireName(a.Name, $"pous[{p.Name}].actions", "action name");
                if (!actions.Add(a.Name)) _result.Error(ValidationCodes.DuplicateId, $"action '{a.Name}' of pou '{p.Name}' is declared twice", $"pous[{p.Name}]");
            }
            var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in p.Methods)
            {
                RequireName(m.Name, $"pous[{p.Name}].methods", "method name");
                if (!methods.Add(m.Name)) _result.Error(ValidationCodes.DuplicateId, $"method '{m.Name}' of pou '{p.Name}' is declared twice", $"pous[{p.Name}]");
                foreach (var v in m.Vars) CheckVariable(v, $"pou/{p.Name}/{m.Name}/{v.Name}", variables, $"pous[{p.Name}].{m.Name}");
            }
        }
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _doc.Types)
        {
            RequireName(t.Name, "types", "type name");
            if (!types.Add(t.Name)) _result.Error(ValidationCodes.DuplicateId, $"type '{t.Name}' is declared twice", $"types[{t.Name}]");
            if (t.StdFb is { } binding)
            {
                foreach (var fb in binding.Fbs)
                    if (!StandardFunctionBlocks.IsStandard(fb))
                        _result.Error(ValidationCodes.StdFbBinding, $"type '{t.Name}' binds unknown standard block '{fb}'", $"types[{t.Name}]");
                if (StandardFunctionBlocks.GetProfile(binding.Profile) is null)
                    _result.Error(ValidationCodes.UnknownProfile, $"type '{t.Name}' binds unknown profile '{binding.Profile}'", $"types[{t.Name}]");
            }
        }
        var devices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in _doc.Io.Devices)
        {
            RequireName(d.Id, "io.devices", "device id");
            RequireName(d.Name, "io.devices", "device name");
            if (!devices.Add(d.Id)) _result.Error(ValidationCodes.DuplicateId, $"device id '{d.Id}' is declared twice", d.Id);
            if (d.Id != $"device/{d.Name}") _result.Error(ValidationCodes.MalformedId, $"device id '{d.Id}' should be 'device/{d.Name}'", d.Id);
        }
        var channels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in _doc.Io.Channels)
        {
            RequireName(c.Id, "io.channels", "channel id");
            RequireName(c.Name, "io.channels", "channel name");
            if (!channels.Add(c.Id)) _result.Error(ValidationCodes.DuplicateId, $"channel id '{c.Id}' is declared twice", c.Id);
            var deviceName = c.Device.StartsWith("device/", StringComparison.Ordinal) ? c.Device["device/".Length..] : c.Device;
            if (c.Id != $"io/{deviceName}/{c.Name}") _result.Error(ValidationCodes.MalformedId, $"channel id '{c.Id}' should be 'io/{deviceName}/{c.Name}'", c.Id);
        }
        foreach (var r in _doc.Configuration.Resources)
        {
            _tasks = new Dictionary<string, IrTask>(StringComparer.OrdinalIgnoreCase);
            _instances = new Dictionary<string, IrProgramInstance>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in r.Tasks)
            {
                RequireName(t.Name, $"resources[{r.Name}].tasks", "task name");
                if (!_tasks.TryAdd(t.Name, t)) _result.Error(ValidationCodes.DuplicateId, $"task '{t.Name}' is declared twice", $"resources[{r.Name}]");
            }
            foreach (var p in r.Programs)
            {
                RequireName(p.Name, $"resources[{r.Name}].programs", "program instance name");
                if (!_instances.TryAdd(p.Name, p)) _result.Error(ValidationCodes.DuplicateId, $"program instance '{p.Name}' is declared twice", $"resources[{r.Name}]");
            }
            foreach (var p in r.Programs)
            {
                if (p.Task is null) continue;
                if (!_tasks.TryGetValue(p.Task, out var task))
                    _result.Error(ValidationCodes.UnresolvedTask, $"program instance '{p.Name}' is assigned to unknown task '{p.Task}'", $"resources[{r.Name}].programs[{p.Name}]");
                else if (!task.Programs.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    _result.Error(ValidationCodes.TaskProgramInconsistent, $"program instance '{p.Name}' names task '{p.Task}' but the task does not list it", $"resources[{r.Name}].programs[{p.Name}]");
            }
            foreach (var t in r.Tasks)
                foreach (var name in t.Programs)
                {
                    if (!_instances.TryGetValue(name, out var inst))
                        _result.Error(ValidationCodes.UnresolvedProgramInstance, $"task '{t.Name}' lists unknown program instance '{name}'", $"resources[{r.Name}].tasks[{t.Name}]");
                    else if (!string.Equals(inst.Task, t.Name, StringComparison.OrdinalIgnoreCase))
                        _result.Error(ValidationCodes.TaskProgramInconsistent, $"task '{t.Name}' lists program instance '{name}' whose task is '{inst.Task}'", $"resources[{r.Name}].tasks[{t.Name}]");
                }
            if (r.FaultProgram is { } fp && !_instances.ContainsKey(fp))
                _result.Error(ValidationCodes.UnresolvedProgramInstance, $"fault program '{fp}' is not a program instance", $"resources[{r.Name}]");
        }

        // alias chains
        foreach (var v in _doc.AllVariables())
        {
            if (v.Alias is null) continue;
            var seen = new HashSet<string>(StringComparer.Ordinal) { v.Id };
            var current = v;
            while (current.Alias is { } alias)
            {
                var targetId = alias.Target?.Root is { Kind: "var", Id: { } id } ? id : null;
                if (targetId is null)
                {
                    if (alias.Target is null || alias.Target.Root.Kind == "unresolved")
                        _result.Error(ValidationCodes.AliasTarget, $"alias '{v.Id}' has no resolved target", v.Id);
                    break;
                }
                var target = _doc.FindVariable(targetId);
                if (target is null)
                {
                    _result.Error(ValidationCodes.AliasTarget, $"alias '{v.Id}' targets unknown variable '{targetId}'", v.Id);
                    break;
                }
                if (!seen.Add(targetId))
                {
                    _result.Error(ValidationCodes.AliasTarget, $"alias '{v.Id}' is part of an alias cycle through '{targetId}'", v.Id);
                    break;
                }
                current = target;
            }
        }
    }

    private void CheckVariable(IrVariable v, string expectedId, HashSet<string> variables, string location)
    {
        RequireName(v.Name, location, "variable name");
        RequireName(v.Id, location, "variable id");
        if (v.Id.Length > 0 && v.Id != expectedId)
            _result.Error(ValidationCodes.MalformedId, $"variable id '{v.Id}' should be '{expectedId}'", v.Id);
        if (v.Id.Length > 0 && !variables.Add(v.Id))
            _result.Error(ValidationCodes.DuplicateId, $"variable id '{v.Id}' is declared twice", v.Id);
    }

    private void RequireName(string? value, string location, string what)
    {
        if (string.IsNullOrEmpty(value)) _result.Error(ValidationCodes.EmptyNameOrId, $"{what} is empty", location);
    }

    // ------------------------------------------------------------------ reflective walk

    private void Walk(object node, string path, IrPou? pou)
    {
        if (!_visited.Add(node)) return;
        if (node is IrPou p) pou = p;
        if (node is IrTypeRef typeRef)
        {
            CheckTypeRef(typeRef, path);
            // still descend to check nested Kind enums
        }
        foreach (var prop in Properties(node.GetType()))
        {
            object? value;
            try { value = prop.GetValue(node); }
            catch { continue; }
            if (value is null) continue;
            var propPath = path + "." + Camel(prop.Name);

            if (value is string s)
            {
                if (prop.GetCustomAttribute<IrEnumAttribute>() is { } en && !en.Values.Contains(s, StringComparer.Ordinal))
                    _result.Error(ValidationCodes.InvalidEnumValue, $"'{s}' is not one of {string.Join("|", en.Values)}", propPath);
                if (prop.GetCustomAttribute<IrRefKindAttribute>() is { } rk)
                    CheckRef(rk.Kind, s, node, pou, propPath);
                continue;
            }
            if (value is JsonElement || value is IrDocument && !ReferenceEquals(value, _doc)) continue;
            if (prop.GetCustomAttribute<IrRefKindAttribute>() is { } rkList && value is IEnumerable<string> names)
            {
                foreach (var n in names) CheckRef(rkList.Kind, n, node, pou, propPath);
                continue;
            }
            switch (value)
            {
                case IDictionary dict:
                    foreach (DictionaryEntry entry in dict)
                        if (entry.Value is not null && IsModelObject(entry.Value)) Walk(entry.Value, $"{propPath}[{entry.Key}]", pou);
                    break;
                case IEnumerable list when value is not string:
                {
                    var i = 0;
                    foreach (var item in list)
                    {
                        if (item is not null && IsModelObject(item)) Walk(item, $"{propPath}[{Label(item, i)}]", pou);
                        i++;
                    }
                    break;
                }
                default:
                    if (IsModelObject(value)) Walk(value, propPath, pou);
                    break;
            }
        }
    }

    private static bool IsModelObject(object o)
    {
        var t = o.GetType();
        return t.IsClass && t != typeof(string) && t.Assembly == ModelAssembly;
    }

    private static string Label(object item, int index) => item switch
    {
        IrVariable v => v.Id,
        IrPou p => p.Name,
        IrType t => t.Name,
        IrAction a => a.Name,
        IrMethod m => m.Name,
        IrTask t => t.Name,
        IrProgramInstance p => p.Name,
        IrRung r => "rung " + r.Number,
        IrIoDevice d => d.Id,
        IrIoChannel c => c.Id,
        IrGlobalVariableList g => g.Name,
        _ => index.ToString(),
    };

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static PropertyInfo[] Properties(Type type)
    {
        lock (PropertyCache)
        {
            if (PropertyCache.TryGetValue(type, out var cached)) return cached;
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.Name != nameof(IrNode.Vendor) && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .ToArray();
            PropertyCache[type] = props;
            return props;
        }
    }

    private void CheckTypeRef(IrTypeRef t, string path)
    {
        foreach (var name in TypeResolver.LeafNames(t))
            if (!_types.TypeNameExists(name))
                _result.Error(ValidationCodes.UnresolvedType, $"type '{name}' is not declared", path);
    }

    private void CheckRef(string kind, string value, object owner, IrPou? pou, string path)
    {
        if (string.IsNullOrEmpty(value)) return;
        switch (kind)
        {
            case "variable":
                if (owner is IrRefRoot { Kind: not "var" }) return;
                if (_doc.FindVariable(value) is null)
                    _result.Error(ValidationCodes.UnresolvedVariable, $"variable '{value}' is not declared", path);
                break;
            case "type":
                if (!_types.TypeNameExists(value)) _result.Error(ValidationCodes.UnresolvedType, $"type '{value}' is not declared", path);
                break;
            case "pou":
                if (_doc.FindPou(value) is null) _result.Error(ValidationCodes.UnresolvedPou, $"pou '{value}' is not declared", path);
                break;
            case "action":
            {
                var targetPouName = owner.GetType().GetProperty("Pou")?.GetValue(owner) as string;
                var target = targetPouName is not null ? _doc.FindPou(targetPouName) : pou;
                if (target is null) return; // reported as unresolved pou
                if (target.FindAction(value) is null)
                    _result.Error(ValidationCodes.UnresolvedAction, $"action '{value}' is not declared in pou '{target.Name}'", path);
                break;
            }
            case "channel":
                if (_doc.FindChannel(value) is null) _result.Error(ValidationCodes.UnresolvedChannel, $"channel '{value}' is not declared", path);
                break;
            case "device":
                if (_doc.Io.Devices.All(d => d.Id != value)) _result.Error(ValidationCodes.UnresolvedDevice, $"device '{value}' is not declared", path);
                break;
            case "task":
                // checked per resource in CheckIds
                break;
            case "programInstance":
                // checked per resource in CheckIds
                break;
        }
    }
}
