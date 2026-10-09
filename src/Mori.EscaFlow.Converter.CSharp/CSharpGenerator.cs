using Mori.EscaFlow.Converter.Layout;
using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Catalogs;
using Mori.EscaFlow.Transpiler.Logix;

namespace Mori.EscaFlow.Converter.CSharp;

/// <summary>Generates a complete C# solution from a plc-ir document. See docs/csharp-generator.md.</summary>
/// <remarks>
/// <para>
/// One call to <see cref="Generate"/> produces every file of the target solution: a logic project with one class per
/// structure type, one class per add-on instruction, one class per program, and the controller class that owns the
/// tags, module images, program instances and tasks; a host project with a `Program.cs` that starts the controller
/// and the web console; a solution file, build properties, a README, and the generation report.
/// </para>
/// <para>
/// Each generated class embeds the comments a reader needs to trust the output without opening the source Logix
/// project: the controller class explains how scan order and prescan are reproduced, the add-on class explains the
/// EnableIn / EnableInFalse contract, and every structure and program class explains how its members are bound to
/// tag memory and where their byte layout comes from.
/// </para>
/// </remarks>
public sealed class CSharpGenerator : IGenerator
{
    private readonly CSharpGeneratorOptions _options;
    private IrDocument _doc = null!;
    private GenerationReport _report = null!;
    private NameMapper _names = null!;
    private LogixLayoutCalculator _layouts = null!;
    private CSharpTypes _types = null!;
    private CSharpExpressions _x = null!;
    private CSharpStatements _s = null!;
    private string _name = "";
    private string _ns = "";
    private string _controllerClass = "";

    public CSharpGenerator(CSharpGeneratorOptions? options = null) => _options = options ?? new CSharpGeneratorOptions();

    public string Target => "csharp";

    /// <summary>Generates every file of the solution and the generation report, in one pass over the document.</summary>
    public GenerationResult Generate(IrDocument document)
    {
        _doc = document;
        var result = new GenerationResult();
        _report = result.Report;
        _report.Target = Target;
        _report.Generator = "Mori.EscaFlow.Converter.CSharp " + (typeof(CSharpGenerator).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        _report.Source = document.Source.File;
        _names = new NameMapper(NameMapper.CSharpKeywords, caseSensitiveTarget: true, _report);
        _layouts = new LogixLayoutCalculator(document);
        _types = new CSharpTypes(document, _names, _layouts);
        _x = new CSharpExpressions(document, _names, _types, _report);
        _s = new CSharpStatements(document, _names, _types, _x, _report, _options, t => _layouts.SizeOf(t).Size);
        _name = Sanitize(_options.Name ?? document.Configuration.Name);
        if (_name.Length == 0) _name = "Plc";
        _ns = _name + ".Logic";
        _controllerClass = _name + "Controller";
        ReserveNames();

        result.Files.Add(new GeneratedFile($"{_name}.slnx", Solution()));
        result.Files.Add(new GeneratedFile("Directory.Build.props", BuildProps()));
        result.Files.Add(new GeneratedFile($"{_ns}/{_ns}.csproj", LogicProject()));
        foreach (var t in _doc.Types.Where(IsGeneratedType))
            result.Files.Add(new GeneratedFile($"{_ns}/Types/{_types.ClassName(t.Name)}.cs", StructureClass(t)));
        foreach (var p in _doc.Pous.Where(p => p.Kind == "functionBlock"))
            result.Files.Add(new GeneratedFile($"{_ns}/AddOns/{_types.ClassName(p.Name)}.cs", FunctionBlockClass(p)));
        foreach (var p in _doc.Pous.Where(p => p.Kind == "program"))
            result.Files.Add(new GeneratedFile($"{_ns}/Programs/{_names.Map(CSharpTypes.TypeScope, p.Name)}.cs", ProgramClass(p)));
        result.Files.Add(new GeneratedFile($"{_ns}/{_controllerClass}.cs", ControllerClass()));
        result.Files.Add(new GeneratedFile($"{_name}.Host/{_name}.Host.csproj", HostProject()));
        result.Files.Add(new GeneratedFile($"{_name}.Host/Program.cs", HostProgram()));
        result.Files.Add(new GeneratedFile("README.md", Readme()));

        foreach (var (service, locations) in _s.Unsupported)
        {
            _report.UnsupportedServices.Add(new UnsupportedService(LogixVendor.Id, service, locations.Count, locations.Distinct().Take(20).ToList()));
            if (_options.StrictServices) _report.Error("GEN002", $"service {service} is not implemented by the runtime ({locations.Count} uses)");
        }
        foreach (var v in _doc.AllVariables().Where(v => v.Alias is not null))
            _report.Aliases.Add(new AliasEntry(v.Id, v.Section, v.Alias!.Text, v.Type.ToString()));
        foreach (var l in _layouts.All().Where(l => l.Approximate)) _report.LayoutNotes.Add($"{l.Name}: layout computed by rule ({l.Size} bytes)");
        result.Files.Add(new GeneratedFile("generation-report.json", _report.ToJson()));
        return result;
    }

    private static string Sanitize(string name) => new(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    /// <summary>Every generated structure and function block layout, dependencies (member structures) before their containers.</summary>
    /// <remarks>
    /// The controller constructor registers every layout with the tag store before defining a single tag, because
    /// the store needs a structure's layout the first time anything of that type is defined; registering a
    /// container before the member types it embeds would leave those members unresolved.
    /// </remarks>
    private List<string> LayoutRegistrationOrder()
    {
        var order = new List<string>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generated = _doc.Types.Where(IsGeneratedType).Select(t => t.Name).Concat(_doc.Pous.Where(p => p.Kind == "functionBlock").Select(p => p.Name)).ToList();
        var known = new HashSet<string>(generated, StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            if (!known.Contains(name) || !done.Add(name)) return;
            var layout = _layouts.Get(name);
            if (layout is not null)
                foreach (var m in layout.Members.Where(m => m.IsStruct)) Visit(m.TypeName);
            order.Add(name);
        }
        foreach (var name in generated) Visit(name);
        return order;
    }

    /// <summary>Structure and string types get a class; the Logix MODULE pseudo-type (module references) does not.</summary>
    private static bool IsGeneratedType(IrType t) => t.Kind is "struct" or "string" && !CSharpTypes.IsModuleType(t.Name);

    /// <summary>
    /// Reserves every identifier the runtime base classes already define, in every scope a generated class draws
    /// names from, so the name mapper never hands out a source tag or routine name that would collide with a base
    /// member (for example a tag literally named <c>Execute</c> or <c>Value</c>).
    /// </summary>
    private void ReserveNames()
    {
        foreach (var n in new[] { "Layout", "GetMember", "Tag", "Offset", "Value", "Size", "Execute", "Prescan", "Postscan", "EnableInFalse", "Controller", "Services", "Sys", "C", "Main", "Name", "Store", "Tasks", "Programs", "State", "Mode", "Start", "Stop", "Statistics", "Log", "PrescanAll", "Fault" })
        {
            _names.Reserve(CSharpTypes.TypeScope, n);
            _names.Reserve(CSharpExpressions.ControllerScope, n);
        }
        foreach (var p in _doc.Pous)
            foreach (var n in new[] { "Layout", "GetMember", "Tag", "Offset", "Value", "Size", "Execute", "Prescan", "Postscan", "EnableInFalse", "Controller", "Services", "Sys", "C", "Main", "Name", "Fault" })
                _names.Reserve(CSharpExpressions.PouScope(p.Name), n);
        // String family members are fixed by the runtime's WrapSTRING (LEN, DATA); map them first so any spelling (Data, data) lands on them.
        _names.Map(CSharpExpressions.MemberScope, "LEN");
        _names.Map(CSharpExpressions.MemberScope, "DATA");
        // Structure members and function block members share one scope with the runtime base class members reserved.
        foreach (var n in new[] { "Layout", "StructSize", "GetMember", "TryGetMember", "Members", "Tag", "Offset", "Value", "Size", "ByteSize", "TypeName", "TypeCode", "Execute", "Logic", "Prescan", "Postscan", "EnableInFalse", "Controller", "Services", "Sys", "Capacity", "Length", "LayoutOf", "Equals", "GetHashCode", "ToString", "GetType" })
            _names.Reserve(CSharpExpressions.MemberScope, n);
        // Program instances share the controller scope with controller tags.
        foreach (var p in _doc.Pous.Where(p => p.Kind == "program")) _x.ProgramInstance(p.Name);
        _names.Reserve(CSharpTypes.TypeScope, _controllerClass);
        _names.Map(CSharpTypes.TypeScope, "STRING");
        // C# forbids a member named like its enclosing type: reserve each class name inside the scopes its members come from.
        foreach (var p in _doc.Pous.Where(p => p.Kind == "program")) _names.Reserve(CSharpExpressions.PouScope(p.Name), _names.Map(CSharpTypes.TypeScope, p.Name));
        foreach (var t in _doc.Types.Where(IsGeneratedType)) _names.Reserve(CSharpExpressions.MemberScope, _types.ClassName(t.Name));
        foreach (var p in _doc.Pous.Where(p => p.Kind == "functionBlock")) _names.Reserve(CSharpExpressions.MemberScope, _types.ClassName(p.Name));
        _names.Reserve(CSharpExpressions.ControllerScope, _controllerClass);
    }

    // ------------------------------------------------------------------ solution files

    private string Solution() => $"""
        <Solution>
          <Project Path="{_ns}/{_ns}.csproj" />
          <Project Path="{_name}.Host/{_name}.Host.csproj" />
        </Solution>

        """;

    private static string BuildProps() => """
        <Project>
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <LangVersion>latest</LangVersion>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <InvariantGlobalization>true</InvariantGlobalization>
            <NoWarn>$(NoWarn);CS0162;CS0164;CS0219;CS8618;CS1998</NoWarn>
          </PropertyGroup>
        </Project>

        """;

    private string LogicProject() => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <RootNamespace>{_ns}</RootNamespace>
            <AssemblyName>{_ns}</AssemblyName>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{_options.RuntimeProject.Replace('\\', '/')}" />
          </ItemGroup>
        </Project>

        """;

    private string HostProject() => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <RootNamespace>{_name}.Host</RootNamespace>
            <AssemblyName>{_name}.Host</AssemblyName>
            <ServerGarbageCollection>true</ServerGarbageCollection>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="../{_ns}/{_ns}.csproj" />
            <ProjectReference Include="{_options.HostProject.Replace('\\', '/')}" />
          </ItemGroup>
        </Project>

        """;

    private string HostProgram() => $$"""
        using Mori.EscaFlow.Runtime;
        using Mori.EscaFlow.Runtime.Host;
        using {{_ns}};

        var controller = new {{_controllerClass}}(new PlcControllerOptions { EipPort = {{(_options.EipPort is { } p ? p.ToString() : "null")}} });
        PlcWebHost.Run(controller, args, new PlcWebHostOptions { Url = "{{_options.WebUrl}}" });

        """;

    private string Readme() => $"""
        # {_name}

        Produced with EscaFlow ({_report.Generator}) from `{_doc.Source.File}` (plc-ir {_doc.IrVersion}, source vendor {_doc.Source.Vendor}). Copyright (c) Cristian Mori.

        - `{_ns}/` — structure classes (`Types/`), add-on instruction classes (`AddOns/`), program classes (`Programs/`)
          and `{_controllerClass}` (tags, module images, program instances, tasks).
        - `{_name}.Host/` — console host with the web console (start/stop, snapshots, tag browser) and the EtherNet/IP tag server.
        - `generation-report.json` — alias and naming tallies, unsupported services, layout notes, instruction inventory.

        Run: `dotnet run --project {_name}.Host` then open {_options.WebUrl}.

        """;

    // ------------------------------------------------------------------ structure classes

    /// <summary>Writes the file header shared by every generated source file: a note that the file is produced output, not hand-written, and the fixed set of usings every generated class needs.</summary>
    private string Header(CodeWriter w)
    {
        w.Line($"// EscaFlow output for {_name}; do not edit, regenerate from the source project.");
        w.Line("#nullable enable");
        w.Line("using System;");
        w.Line("using EthernetIPSharp.Logix;");
        w.Line("using Mori.EscaFlow.Runtime;");
        w.Line();
        w.Line($"namespace {_ns};");
        w.Line();
        return "";
    }

    /// <summary>
    /// Emits one structure or string type as a sealed class over tag memory. A string type wraps the runtime's
    /// <c>WrapSTRING</c> with its fixed capacity; a structure type declares one readonly wrapper field per member,
    /// constructed at the member's byte offset within the structure (from <see cref="LogixLayoutCalculator"/>), and
    /// a <c>GetMember</c> lookup the tag server and the web console use to read an arbitrary member by name. A
    /// predefined vendor type whose members match the runtime's timer, counter or control interfaces also
    /// implements that interface, so the runtime's generic <c>Logix.TON</c> / <c>CTU</c> / ... helpers accept it.
    /// </summary>
    private string StructureClass(IrType t)
    {
        var w = new CodeWriter();
        Header(w);
        var cls = _types.ClassName(t.Name);
        var layout = _layouts.Get(t.Name)!;
        if (t.Description is not null) w.Line("/// <summary>" + Xml(t.Description) + "</summary>");
        if (t.Kind == "string")
        {
            using (w.Block($"public sealed class {cls} : WrapSTRING"))
            {
                w.Line($"public new static readonly PlcLayout Layout = {_types.LayoutLiteral(layout)};");
                w.Line($"public const int StringCapacity = {t.Length ?? 82};");
                w.Line($"public {cls}(Tag tag, int offset) : base(tag, offset, StringCapacity) {{ }}");
            }
            return w.ToString();
        }
        var interfaces = Interfaces(t);
        using (w.Block($"public sealed class {cls} : PlcStruct{interfaces}"))
        {
            w.Line($"public static readonly PlcLayout Layout = {_types.LayoutLiteral(layout)};");
            w.Line($"public const int StructSize = {layout.Size};");
            var members = new List<(string Field, IrMember Member)>();
            foreach (var m in t.Members ?? new())
            {
                var field = _names.Map("members", m.Name);
                members.Add((field, m));
                if (m.Description is not null) w.Line("/// <summary>" + Xml(m.Description) + "</summary>");
                w.Line($"public readonly {_types.FieldType(m.Type)} {field};");
            }
            w.Line();
            // Every member wrapper is bound to tag memory here, at construction, at the offset the layout computed;
            // after this constructor runs the fields never move, so reading a member is just reading a field.
            using (w.Block($"public {cls}(Tag tag, int offset) : base(tag, offset)"))
            {
                foreach (var (field, m) in members)
                {
                    var ml = layout.Find(m.Name);
                    if (ml is null) continue;
                    if (m.Type.Is("BOOL") && !m.Type.IsArray)
                        w.Line($"{field} = new WrapBOOL(tag, offset + {ml.Offset}, {ml.Bit ?? 0});");
                    else
                        w.Line($"{field} = {_types.Construct(m.Type, "tag", $"offset + {ml.Offset}")};");
                }
            }
            w.Line();
            using (w.Block("public override object? GetMember(string name) => name switch", "};"))
            {
                foreach (var (field, m) in members) w.Line($"{CSharpTypes.Quote(m.Name)} => {field},");
                w.Line("_ => null,");
            }
            if (interfaces.Length > 0)
            {
                // Explicit interface implementations: the runtime's timer / counter / control interfaces expose the members as properties.
                w.Line();
                var iface = interfaces[2..];
                foreach (var name in InterfaceMembers(iface))
                {
                    var field = members.First(x => x.Member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Field;
                    var type = name is "PRE" or "ACC" or "LEN" or "POS" ? "WrapDINT" : "WrapBOOL";
                    w.Line($"{type} {iface}.{name} => {field};");
                }
            }
        }
        return w.ToString();
    }

    private static readonly string[] TimerMembers = { "PRE", "ACC", "EN", "TT", "DN" };
    private static readonly string[] CounterMembers = { "PRE", "ACC", "CU", "CD", "DN", "OV", "UN" };
    private static readonly string[] ControlMembers = { "LEN", "POS", "EN", "EU", "DN", "EM", "ER", "UL", "IN", "FD" };

    private static string[] InterfaceMembers(string iface) => iface switch
    {
        "Mori.EscaFlow.Runtime.ITimer" => TimerMembers,
        "Mori.EscaFlow.Runtime.ICounter" => CounterMembers,
        _ => ControlMembers,
    };

    private string Interfaces(IrType t)
    {
        var names = (t.Members ?? new()).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (t.Class == "vendor")
        {
            if (t.Name.Equals("TIMER", StringComparison.OrdinalIgnoreCase) && names.IsSupersetOf(TimerMembers)) return ", Mori.EscaFlow.Runtime.ITimer";
            if (t.Name.Equals("COUNTER", StringComparison.OrdinalIgnoreCase) && names.IsSupersetOf(CounterMembers)) return ", Mori.EscaFlow.Runtime.ICounter";
            if (t.Name.Equals("CONTROL", StringComparison.OrdinalIgnoreCase) && names.IsSupersetOf(ControlMembers)) return ", Mori.EscaFlow.Runtime.IControl";
        }
        return "";
    }

    private static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", " ");

    // ------------------------------------------------------------------ function blocks

    /// <summary>
    /// Emits one add-on instruction definition as a function block class over its backing structure (see
    /// <see cref="LogixLayoutCalculator.BuildBacking"/> for how that structure is laid out). <c>Execute(enableIn)</c>
    /// reproduces the EnableIn / EnableOut contract Logix gives every add-on instance: when <c>enableIn</c> is false
    /// the block's own logic does not run at all — <c>EnableOut</c> is forced false and the author's
    /// <c>EnableInFalse</c> hook (if the definition is set to execute it) runs instead, so the instruction can reset
    /// outputs or quiesce state while disabled; when <c>enableIn</c> is true, <c>EnableOut</c> is set true and the
    /// <c>Logic</c> routine runs as <c>Execute</c>'s private <c>Logic()</c> method. InOut parameters are not stored
    /// fields: they are bound by the caller immediately before each call (see <see cref="CSharpStatements"/> FbCall),
    /// so they always alias the current argument rather than a snapshot.
    /// </summary>
    private string FunctionBlockClass(IrPou p)
    {
        var w = new CodeWriter();
        Header(w);
        var cls = _types.ClassName(p.Name);
        var scope = CSharpExpressions.PouScope(p.Name);
        var layout = _layouts.Get(p.Name)!;
        var ctx = new EmitContext { Kind = EmitContext.Scope.FunctionBlock, Pou = p, ControllerAccess = $"(({_controllerClass})Controller)" };
        if (p.Description is not null) w.Line("/// <summary>" + Xml(p.Description) + "</summary>");
        using (w.Block($"public sealed class {cls} : FunctionBlock"))
        {
            w.Line($"public static readonly PlcLayout Layout = {_types.LayoutLiteral(layout)};");
            w.Line($"public const int StructSize = {layout.Size};");
            var stored = new List<(string Field, IrVariable Var)>();
            foreach (var v in p.Vars)
            {
                var field = _names.Map(CSharpExpressions.MemberScope, v.Name);
                if (v.Description is not null) w.Line("/// <summary>" + Xml(v.Description) + "</summary>");
                if (v.Section == "inOut")
                {
                    w.Line($"public {_types.FieldType(v.Type)} {field} = null!; // InOut parameter: bound by the caller before Execute");
                    continue;
                }
                w.Line($"public readonly {_types.FieldType(v.Type)} {field};");
                stored.Add((field, v));
            }
            w.Line();
            // Every stored parameter and local is bound to its slot of the instance's backing structure here, at
            // the offset LogixLayoutCalculator.BuildBacking computed (EnableIn/EnableOut first, then every other
            // parameter and local in declaration order, BOOLs packed 32 per hidden DINT host).
            using (w.Block($"public {cls}(Tag tag, int offset, PlcController controller) : base(tag, offset, controller)"))
            {
                foreach (var (field, v) in stored)
                {
                    var ml = layout.Find(v.Name);
                    if (ml is null) continue;
                    if (v.Type.Is("BOOL") && !v.Type.IsArray)
                        w.Line($"{field} = new WrapBOOL(tag, offset + {ml.Offset}, {ml.Bit ?? 0});");
                    else
                        w.Line($"{field} = {_types.Construct(v.Type, "tag", $"offset + {ml.Offset}", "controller")};");
                }
            }
            w.Line();
            using (w.Block("public override object? GetMember(string name) => name switch", "};"))
            {
                foreach (var v in p.Vars.Where(v => v.Section != "inOut")) w.Line($"{CSharpTypes.Quote(v.Name)} => {_names.Map(CSharpExpressions.MemberScope, v.Name)},");
                w.Line("_ => null,");
            }
            w.Line();
            var enableIn = p.FindVar("EnableIn") is { } ei ? _names.Map(CSharpExpressions.MemberScope, ei.Name) : null;
            var enableOut = p.FindVar("EnableOut") is { } eo ? _names.Map(CSharpExpressions.MemberScope, eo.Name) : null;
            // EnableIn / EnableOut contract: a false enable skips Logic entirely and runs EnableInFalse instead, exactly
            // as Logix skips an add-on instance's logic when the rung or input condition feeding it is false.
            using (w.Block("public void Execute(bool enableIn)"))
            {
                if (enableIn is not null) w.Line($"{enableIn}.Value = enableIn;");
                using (w.Block("if (!enableIn)"))
                {
                    if (enableOut is not null) w.Line($"{enableOut}.Value = false;");
                    w.Line("EnableInFalse();");
                    w.Line("return;");
                }
                if (enableOut is not null) w.Line($"{enableOut}.Value = true;");
                w.Line("Logic();");
            }
            w.Line();
            using (w.Block("private void Logic()"))
            {
                if (p.Body is not null) _s.Body(w, p.Body, ctx, p.Name + "/Logic");
            }
            foreach (var a in p.Actions)
            {
                w.Line();
                var (modifier, name) = a.Hook switch
                {
                    "init" => ("public override", "Prescan"),
                    "postscan" => ("public override", "Postscan"),
                    "enableFalse" => ("public override", "EnableInFalse"),
                    _ => ("public", _x.ActionMethod(p, a.Name)),
                };
                if (a.Hook == "init" && a.Synthetic) w.Line("/// <summary>Prescan: synthesized from the logic, then the Prescan routine when the definition executes it.</summary>");
                using (w.Block($"{modifier} void {name}()"))
                    _s.Body(w, a.Body, ctx, p.Name + "/" + a.Name);
            }
        }
        return w.ToString();
    }

    // ------------------------------------------------------------------ programs

    /// <summary>
    /// Emits one program as a <c>PlcProgram</c> subclass: one field per program-scoped tag, each bound in the
    /// constructor to a tag the controller's tag store defines under the path <c>Program:&lt;name&gt;.&lt;tag&gt;</c>
    /// (the same addressing Logix itself uses, so an external client reading that path sees the same tag), and a
    /// <c>Main()</c> override that is exactly the program's main routine — the controller's task scheduler calls
    /// <c>Main()</c> once per scan in the scan order the task list defines, which is how program execution order is
    /// reproduced without the runtime needing any Logix-specific scheduling logic of its own. A synthesized
    /// <c>Prescan()</c> override (when prescan synthesis was requested) runs once when the controller enters Run, before any task's first scan.
    /// </summary>
    private string ProgramClass(IrPou p)
    {
        var w = new CodeWriter();
        Header(w);
        var cls = _names.Map(CSharpTypes.TypeScope, p.Name);
        var scope = CSharpExpressions.PouScope(p.Name);
        var ctx = new EmitContext { Kind = EmitContext.Scope.Program, Pou = p, ControllerAccess = "C" };
        if (p.Description is not null) w.Line("/// <summary>" + Xml(p.Description) + "</summary>");
        using (w.Block($"public sealed class {cls} : PlcProgram<{_controllerClass}>"))
        {
            var fields = new List<(string Field, IrVariable Var)>();
            foreach (var v in p.Vars)
            {
                var field = _names.Map(scope, v.Name);
                fields.Add((field, v));
                if (v.Description is not null) w.Line("/// <summary>" + Xml(v.Description) + "</summary>");
                w.Line($"public readonly {_types.FieldType(v.Type)} {field};");
            }
            w.Line();
            using (w.Block($"public {cls}({_controllerClass} controller) : base(controller, {CSharpTypes.Quote(p.Name)})"))
            {
                foreach (var (field, v) in fields) w.Line($"{field} = {DefineAndBind(v, $"Program:{p.Name}.{v.Name}", "controller")};");
            }
            w.Line();
            using (w.Block("public override void Main()"))
            {
                if (p.Body is not null) _s.Body(w, p.Body, ctx, p.Name + "/Main");
            }
            if (p.OnFault is { } fault && p.FindAction(fault) is not null)
            {
                w.Line();
                using (w.Block("public override void Fault()")) w.Line($"{_x.ActionMethod(p, fault)}();");
            }
            foreach (var a in p.Actions)
            {
                w.Line();
                var (modifier, name) = a.Hook == "init" ? ("public override", "Prescan") : ("public", _x.ActionMethod(p, a.Name));
                if (a.Description is not null) w.Line("/// <summary>" + Xml(a.Description) + "</summary>");
                using (w.Block($"{modifier} void {name}()"))
                    _s.Body(w, a.Body, ctx, p.Name + "/" + a.Name);
            }
        }
        return w.ToString();
    }

    /// <summary>Defines the tag for a variable in the store and constructs the wrapper bound to it, at the given Logix-style tag path.</summary>
    private string DefineAndBind(IrVariable v, string path, string controller)
    {
        var store = controller + ".Store";
        var (type, dims, element) = Shape(v.Type);
        var dimsLiteral = dims.Length == 0 ? "System.Array.Empty<int>()" : "new[] { " + string.Join(", ", dims) + " }";
        string define;
        if (CSharpTypes.IsAtomic(element))
            define = $"{store}.Define({CSharpTypes.Quote(path)}, 0x{LogixLayoutCalculator.CipTypeCode(CSharpTypes.CanonicalAtomic(element.Name!)):X2}, {dimsLiteral})";
        else
            define = $"{store}.Define({CSharpTypes.Quote(path)}, {LayoutRef(element)}, {dimsLiteral})";
        return _types.Construct(type, define, "0", controller);
    }

    private string LayoutRef(IrTypeRef element)
    {
        if (element.Kind == "string") return $"{_types.ClassName($"STRING{element.Length}")}.Layout";
        var name = element.Name ?? "DINT";
        if (name.Equals("STRING", StringComparison.OrdinalIgnoreCase)) return "STRING.Layout";
        return $"{_types.ClassName(name)}.Layout";
    }

    private static (IrTypeRef Type, int[] Dims, IrTypeRef Element) Shape(IrTypeRef type)
    {
        if (type.IsArray) return (type, (type.Dims ?? new()).Select(d => (int)d.Count).ToArray(), type.Element ?? IrTypeRef.Named("DINT"));
        if (type.IsAny) return (IrTypeRef.Named("DINT"), Array.Empty<int>(), IrTypeRef.Named("DINT"));
        return (type, Array.Empty<int>(), type);
    }

    // ------------------------------------------------------------------ controller

    /// <summary>
    /// Emits the controller class: the single root of the generated solution's tag memory. Its constructor runs, in
    /// order, (1) layout registration for every generated structure and add-on backing structure, dependencies
    /// first, so the tag store can lay out a nested member the first time it is needed; (2) every controller-scoped
    /// tag and module I/O image, defined in the store and bound to a field at the path Logix itself would use
    /// (plain tag names for controller tags, <c>ModuleName:I</c> / <c>:O</c> / <c>:C</c> for module images), which is
    /// what lets the EtherNet/IP tag server expose the same addressable tag names a real controller would; (3) one
    /// instance per program, added to the scheduler's program list; (4) one <see cref="PlcTask"/> per configured
    /// task, holding the task's scan interval, priority and watchdog and the ordered list of program instances it
    /// runs each scan — program execution order inside one scan, and the relative priority between tasks, both come
    /// from this list and from the runtime's task scheduler, not from anything generated per program.
    /// <c>PrescanAll()</c> calls every program's <c>Prescan()</c> once before the first real scan, reproducing
    /// Logix's prescan pass over the whole project when the controller enters Run.
    /// </summary>
    private string ControllerClass()
    {
        var w = new CodeWriter();
        Header(w);
        var ctx = new EmitContext { Kind = EmitContext.Scope.Controller, ControllerAccess = "this" };
        var resource = _doc.Configuration.Resources.FirstOrDefault() ?? new IrResource { Name = _doc.Configuration.Name };
        using (w.Block($"public sealed class {_controllerClass} : PlcController"))
        {
            var fields = new List<(string Field, IrVariable Var, string Path)>();
            foreach (var g in _doc.Globals)
                foreach (var v in g.Vars)
                {
                    var field = _names.Map(CSharpExpressions.ControllerScope, v.Name);
                    fields.Add((field, v, v.Logix()?.Path ?? v.Name));
                    if (v.Description is not null) w.Line("/// <summary>" + Xml(v.Description) + "</summary>");
                    w.Line($"public readonly {_types.FieldType(v.Type)} {field};");
                }
            w.Line();
            var programs = _doc.Pous.Where(p => p.Kind == "program").ToList();
            foreach (var p in programs)
                w.Line($"public readonly {_names.Map(CSharpTypes.TypeScope, p.Name)} {_x.ProgramInstance(p.Name)};");
            w.Line();
            using (w.Block($"public {_controllerClass}(PlcControllerOptions? options = null) : base({CSharpTypes.Quote(_doc.Configuration.Name)}, options)"))
            {
                w.Line("// Structure layouts (nested structures first), so the tag server presents the same members and offsets as the source controller.");
                foreach (var name in LayoutRegistrationOrder())
                    w.Line($"Store.RegisterLayout({_types.ClassName(name)}.Layout);");
                w.Line();
                w.Line("// Controller tags and module images, defined and bound at the same tag path the source controller uses.");
                foreach (var (field, v, path) in fields) w.Line($"{field} = {DefineAndBind(v, path, "this")};");
                w.Line();
                w.Line("// Program instances.");
                foreach (var p in programs)
                {
                    var inst = _x.ProgramInstance(p.Name);
                    w.Line($"{inst} = new {_names.Map(CSharpTypes.TypeScope, p.Name)}(this);");
                    w.Line($"Programs.Add({inst});");
                }
                w.Line();
                w.Line("// Tasks in scan order: each task runs its program instances in this list's order, at its own interval and priority.");
                foreach (var t in resource.Tasks)
                {
                    var kind = t.Kind switch { "periodic" => "TaskKind.Periodic", "event" => "TaskKind.Event", _ => "TaskKind.Continuous" };
                    var list = string.Join(", ", t.Programs.Where(n => programs.Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).Select(n => _x.ProgramInstance(n)));
                    var interval = t.IntervalMs is { } i ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
                    var watchdog = t.WatchdogMs is { } wd ? wd.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
                    w.Line($"Tasks.Add(new PlcTask({CSharpTypes.Quote(t.Name)}, {kind}, {interval}, {t.Priority ?? 10}, {watchdog}, new PlcProgram[] {{ {list} }}));");
                }
            }
            w.Line();
            using (w.Block("protected override void PrescanAll()"))
            {
                foreach (var p in programs) w.Line($"{_x.ProgramInstance(p.Name)}.Prescan();");
            }
        }
        _ = ctx;
        return w.ToString();
    }
}
