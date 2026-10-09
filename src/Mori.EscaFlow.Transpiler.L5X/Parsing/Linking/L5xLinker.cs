using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Fbd;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Rll;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Sfc;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.St;
using Mori.EscaFlow.Transpiler.L5X.Model.Modules;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;
using Mori.EscaFlow.Transpiler.L5X.Parsing.St;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Linking;

/// <summary>
/// Resolves references after parsing: data type registry and references, bit hosts, aliases, routine calls,
/// program/task structure, module parents, SFC/FBD graph links and L5K value shaping.
/// <para>
/// This runs after both the XML phase and the logic phase, and strictly after, because most of what it does
/// needs the whole project to exist first: a tag's data type may be declared anywhere in the file relative to
/// the tag, a routine may call a routine defined later in the same program, a bare word in a rung cannot be
/// told apart from a tag name until every tag name is known. <see cref="Link"/> lists the steps in the order
/// they must run: types before anything that names a type, aliases and routine/module structure before the
/// keyword reclassification that depends on "does this name resolve to something", and value shaping last
/// because it needs the fully resolved type registry.
/// </para>
/// </summary>
public static class L5xLinker
{
    public static void Link(L5xProject project, ParseOptions options, IDiagnosticSink diagnostics)
    {
        var c = project.Controller;
        var registry = BuildRegistry(c, diagnostics);
        c.Types = registry;

        ResolveDataTypeRefs(c, registry, diagnostics);
        ResolveModules(c, diagnostics);
        ResolvePrograms(c, diagnostics);
        ResolveAliases(c, diagnostics);
        ResolveMessages(c, diagnostics);
        ReclassifyKeywordOperands(c);
        ResolveRoutineCalls(c, diagnostics);
        ResolveGraphs(c, diagnostics);
        ParseCommentOperands(c);
        if (options.ParseValues != ValueParseMode.None) ShapeValues(c, registry, options, diagnostics);
        SocketAnalysis.Run(c);
    }

    // ------------------------------------------------------------------ data types

    /// <summary>Seeds the registry with the project's own user types, each add-on's synthesized backing structure, then the built-in atomic and predefined types (which never override a project type of the same, unexpected, name).</summary>
    private static DataTypeRegistry BuildRegistry(Controller c, IDiagnosticSink diagnostics)
    {
        var registry = new DataTypeRegistry();
        foreach (var t in c.DataTypes) registry.Register(t);
        foreach (var aoi in c.AddOnInstructions)
        {
            var backing = BuildAoiBackingType(aoi);
            aoi.BackingType = backing;
            if (registry.Contains(backing.Name))
                diagnostics.Warning("LNK003", $"Add-on instruction '{aoi.Name}' has the same name as a data type", new DiagnosticLocation(XmlLine: aoi.SourceLine, Container: aoi.Name));
            else
                registry.Register(backing);
        }
        BuiltInTypes.RegisterInto(registry);
        return registry;
    }

    /// <summary>Members are parameters then local tags, in declaration order (Decorated data follows this order).</summary>
    private static StructuredType BuildAoiBackingType(AddOnInstructionDefinition aoi)
    {
        var t = new StructuredType { Name = aoi.Name, Class = DataTypeClass.AddOn, Description = aoi.Description, SourceLine = aoi.SourceLine };
        foreach (var p in aoi.Parameters)
        {
            if (p.DataType is null) continue;
            t.Members.Add(new DataTypeMember
            {
                Name = p.Name,
                DataType = DataTypeRef.Of(p.DataType.Name),
                Dimension = p.Dimensions.Length > 0 ? p.Dimensions[0] : 0,
                Radix = p.Radix,
                ExternalAccess = p.ExternalAccess,
                Description = p.Description,
            });
        }
        foreach (var l in aoi.LocalTags)
        {
            if (l.DataType is null) continue;
            t.Members.Add(new DataTypeMember
            {
                Name = l.Name,
                DataType = DataTypeRef.Of(l.DataType.Name),
                Dimension = l.Dimensions.Length > 0 ? l.Dimensions[0] : 0,
                Radix = l.Radix,
                ExternalAccess = l.ExternalAccess,
                Description = l.Description,
                Hidden = false,
            });
        }
        // BOOL parameters/locals are packed into hidden DINTs in the real layout; L5K shaping of AOI-typed tags is therefore approximate.
        t.IsOpaque = true;
        return t;
    }

    /// <summary>
    /// Resolves every <see cref="DataTypeRef"/> in the project (UDT members, add-on backing members, tag types)
    /// against the registry, registering an unresolved placeholder (with one warning per distinct missing name)
    /// for anything that still does not resolve, and a module-defined placeholder for any colon-qualified name.
    /// Also walks tag values to discover module-defined structure layouts from their Decorated data
    /// (see <see cref="LearnModuleTypes"/>), since those types are never declared up front.
    /// </summary>
    private static void ResolveDataTypeRefs(Controller c, DataTypeRegistry registry, IDiagnosticSink diagnostics)
    {
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Resolve(DataTypeRef? r, int line, string? owner)
        {
            if (r is null || r.Definition is not null) return;
            if (registry.Resolve(r)) return;
            if (r.Name.Contains(':'))
            {
                r.Definition = registry.GetOrAddModuleDefined(r.Name);
                return;
            }
            var placeholder = new StructuredType { Name = r.Name, Class = DataTypeClass.Unresolved, IsOpaque = true };
            r.Definition = registry.RegisterIfAbsent(placeholder);
            if (unresolved.Add(r.Name))
                diagnostics.Warning("LNK001", $"Data type '{r.Name}' is not defined", new DiagnosticLocation(XmlLine: line, Tag: owner));
        }

        foreach (var t in c.DataTypes)
        {
            foreach (var m in t.Members)
            {
                Resolve(m.DataType, m.SourceLine, t.Name + "." + m.Name);
                if (m.Target is not null)
                {
                    m.BitHost = t.FindMember(m.Target);
                    if (m.BitHost is null)
                        diagnostics.Warning("LNK001", $"Bit member '{t.Name}.{m.Name}' targets unknown host '{m.Target}'", new DiagnosticLocation(XmlLine: m.SourceLine));
                }
            }
        }
        foreach (var aoi in c.AddOnInstructions)
        {
            if (aoi.BackingType is not null)
                foreach (var m in aoi.BackingType.Members) Resolve(m.DataType, m.SourceLine, aoi.Name + "." + m.Name);
        }
        foreach (var tag in c.AllTags) Resolve(tag.DataType, tag.SourceLine, tag.Name);

        // Module-defined types: learn their shape from Decorated data (module connections first, then any tag
        // whose value embeds a module-defined structure, e.g. a UDT with a PowerFlex output member).
        foreach (var m in c.Modules)
        {
            if (m.Communications is null) continue;
            LearnModuleTypes(m.Communications.ConfigTag?.Data.Decorated, registry);
            LearnModuleTypes(m.Communications.ConfigData?.Data.Decorated, registry);
            foreach (var conn in m.Communications.Connections)
            {
                LearnModuleTypes(conn.InputTag?.Data.Decorated, registry);
                LearnModuleTypes(conn.OutputTag?.Data.Decorated, registry);
            }
        }
        foreach (var tag in c.AllTags)
        {
            if (tag.DataType is { Definition: StructuredType { Class: DataTypeClass.User or DataTypeClass.ModuleDefined } })
                LearnModuleTypes(tag.Data.Decorated, registry);
        }
    }

    /// <summary>Walks a value tree and fills in any module-defined structure (name containing ':') whose members are unknown.</summary>
    private static void LearnModuleTypes(TagValue? value, DataTypeRegistry registry)
    {
        switch (value)
        {
            case StructValue sv:
                if (sv.DataType.Contains(':'))
                {
                    var t = registry.GetOrAddModuleDefined(sv.DataType);
                    if (t.Members.Count == 0)
                    {
                        foreach (var kv in sv.Members)
                        {
                            var (typeName, dim) = kv.Value switch
                            {
                                ArrayValue a => (a.ElementType, a.Dimensions.Length > 0 ? a.Dimensions[0] : 0),
                                _ => (kv.Value.DataType, 0),
                            };
                            t.Members.Add(new DataTypeMember { Name = kv.Key, DataType = DataTypeRef.Of(typeName), Dimension = dim });
                        }
                        t.IsOpaque = false;
                        foreach (var m in t.Members)
                            if (!registry.Resolve(m.DataType) && m.DataType.Name.Contains(':')) m.DataType.Definition = registry.GetOrAddModuleDefined(m.DataType.Name);
                    }
                }
                foreach (var kv in sv.Members)
                    if (kv.Value is StructValue or ArrayValue) LearnModuleTypes(kv.Value, registry);
                break;
            case ArrayValue av:
                // Elements share one type; learning from the first is enough.
                if (av.Elements.Count > 0 && av.Elements[0] is StructValue or ArrayValue) LearnModuleTypes(av.Elements[0], registry);
                break;
        }
    }

    // ------------------------------------------------------------------ modules, programs, tasks

    /// <summary>Builds the module parent/child tree from each module's <see cref="Module.ParentModule"/> name.</summary>
    private static void ResolveModules(Controller c, IDiagnosticSink diagnostics)
    {
        var byName = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in c.Modules) byName[m.Name] = m;
        foreach (var m in c.Modules)
        {
            // The local chassis root module has no real parent and sometimes names itself; either case ends the walk.
            if (string.IsNullOrEmpty(m.ParentModule) || string.Equals(m.ParentModule, m.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (byName.TryGetValue(m.ParentModule, out var parent))
            {
                m.Parent = parent;
                parent.Children.Add(m);
            }
            else
            {
                diagnostics.Info("LNK004", $"Module '{m.Name}' refers to unknown parent '{m.ParentModule}'", new DiagnosticLocation(XmlLine: m.SourceLine));
            }
        }
    }

    /// <summary>Resolves each program's main/fault routine and child-program names, then each task's scheduled program list, in both directions (parent/child, task/program).</summary>
    private static void ResolvePrograms(Controller c, IDiagnosticSink diagnostics)
    {
        foreach (var p in c.Programs)
        {
            if (p.MainRoutineName is not null)
            {
                p.MainRoutine = p.FindRoutine(p.MainRoutineName);
                if (p.MainRoutine is null)
                    diagnostics.Warning("LNK002", $"Program '{p.Name}' main routine '{p.MainRoutineName}' not found", new DiagnosticLocation(XmlLine: p.SourceLine, Container: p.Name));
            }
            if (p.FaultRoutineName is not null)
            {
                p.FaultRoutine = p.FindRoutine(p.FaultRoutineName);
                if (p.FaultRoutine is null)
                    diagnostics.Warning("LNK002", $"Program '{p.Name}' fault routine '{p.FaultRoutineName}' not found", new DiagnosticLocation(XmlLine: p.SourceLine, Container: p.Name));
            }
            foreach (var childName in p.ChildProgramNames)
            {
                var child = c.FindProgram(childName);
                if (child is null)
                {
                    diagnostics.Warning("LNK002", $"Program '{p.Name}' child program '{childName}' not found", new DiagnosticLocation(XmlLine: p.SourceLine, Container: p.Name));
                    continue;
                }
                p.ChildPrograms.Add(child);
                child.ParentProgram = p;
            }
        }
        foreach (var t in c.Tasks)
        {
            foreach (var name in t.ScheduledProgramNames)
            {
                var p = c.FindProgram(name);
                if (p is null)
                {
                    diagnostics.Warning("LNK002", $"Task '{t.Name}' schedules unknown program '{name}'", new DiagnosticLocation(XmlLine: t.SourceLine));
                    continue;
                }
                t.ScheduledPrograms.Add(p);
                p.Task = t;
            }
        }
    }

    // ------------------------------------------------------------------ aliases and comments

    /// <summary>Parses every alias tag's target text and resolves it to the <see cref="Tag"/> it points to, using the alias's own scope (its program, or add-on parameters/locals) before falling back to controller scope.</summary>
    private static void ResolveAliases(Controller c, IDiagnosticSink diagnostics)
    {
        var empty = LogicParseContext.Empty;
        var scratch = new DiagnosticBag();
        foreach (var tag in c.AllTags)
        {
            if (tag.TagType != TagType.Alias || tag.AliasFor is null) continue;
            var path = StParser.TryParseOperandPath(tag.AliasFor, empty, scratch);
            if (path is null)
            {
                diagnostics.Warning("LNK005", $"Alias '{tag.Name}' has an unparseable target '{tag.AliasFor}'", new DiagnosticLocation(XmlLine: tag.SourceLine, Tag: tag.Name));
                continue;
            }
            tag.AliasPath = path;
            tag.AliasTarget = FindTag(c, path, tag.Program, tag.Aoi);
            if (tag.AliasTarget is null && !path.Root.Contains(':'))
                diagnostics.Warning("LNK005", $"Alias '{tag.Name}' target '{tag.AliasFor}' not found", new DiagnosticLocation(XmlLine: tag.SourceLine, Tag: tag.Name));
        }
    }

    /// <summary>
    /// Resolves a tag path's root name to a <see cref="Tag"/>, applying Logix's scope shadowing rule: an
    /// explicit <c>\Program.Tag</c> goes straight to that program; otherwise an add-on's own parameters/locals
    /// (when resolving inside an add-on), then the current program's tags, shadow a same-named controller tag,
    /// which is only reached once nothing more local matches.
    /// </summary>
    public static Tag? FindTag(Controller c, OperandPath path, Program? program, AddOnInstructionDefinition? aoi)
    {
        if (path.ProgramName is not null)
        {
            var p = c.FindProgram(path.ProgramName);
            return p?.Tags[path.Root];
        }
        if (aoi is not null)
        {
            var p = aoi.FindParameter(path.Root);
            if (p is not null) return p;
            var l = aoi.LocalTags[path.Root];
            if (l is not null) return l;
        }
        if (program is not null)
        {
            var t = program.Tags[path.Root];
            if (t is not null) return t;
        }
        return c.Tags[path.Root];
    }

    /// <summary>Parses each tag comment's relative operand specifier (".Member", "[4]", "[7].Input.0") into a path, by prefixing a dummy root so the ordinary path grammar applies.</summary>
    private static void ParseCommentOperands(Controller c)
    {
        var empty = LogicParseContext.Empty;
        var scratch = new DiagnosticBag();
        foreach (var tag in c.AllTags)
        {
            foreach (var comment in tag.Comments)
            {
                // Operand specifiers are relative: ".Member", "[4]", "[7].Input.0"
                comment.Path = StParser.TryParseOperandPath("_" + comment.Operand, empty, scratch);
            }
        }
    }

    // ------------------------------------------------------------------ messages

    /// <summary>Binds the local element and destination tag of every MESSAGE tag to the tags they name.</summary>
    private static void ResolveMessages(Controller c, IDiagnosticSink diagnostics)
    {
        var empty = LogicParseContext.Empty;
        var scratch = new DiagnosticBag();
        foreach (var tag in c.AllTags)
        {
            var msg = tag.Data.Message;
            if (msg is null) continue;
            var loc = new DiagnosticLocation(XmlLine: tag.SourceLine, Tag: tag.Name, Container: tag.Program?.Name ?? tag.Aoi?.Name);
            if (msg.LocalElement is not null)
            {
                msg.LocalElementPath = StParser.TryParseOperandPath(msg.LocalElement, empty, scratch);
                if (msg.LocalElementPath is not null) msg.LocalElementTag = FindTag(c, msg.LocalElementPath, tag.Program, tag.Aoi);
                if (msg.LocalElementTag is null && !msg.LocalElement.Contains(':'))
                    diagnostics.Info("LNK007", $"Message '{tag.Name}' local element '{msg.LocalElement}' not found", loc);
            }
            if (msg.DestinationTag is not null)
            {
                msg.DestinationTagPath = StParser.TryParseOperandPath(msg.DestinationTag, empty, scratch);
                if (msg.DestinationTagPath is not null) msg.DestinationTagRef = FindTag(c, msg.DestinationTagPath, tag.Program, tag.Aoi);
                if (msg.DestinationTagRef is null && !msg.DestinationTag.Contains(':'))
                    diagnostics.Info("LNK007", $"Message '{tag.Name}' destination tag '{msg.DestinationTag}' not found", loc);
            }
        }
    }

    // ------------------------------------------------------------------ keyword reclassification

    /// <summary>
    /// Built-in instructions take enumeration operands (Disabled, All, Programmed, ...) in positions the signature
    /// table lists as tags. Once every tag is known, a bare identifier that names no tag, module, routine or
    /// system bit is turned into a <see cref="KeywordOperand"/> so transpilers never treat it as a variable.
    /// </summary>
    private static void ReclassifyKeywordOperands(Controller c)
    {
        foreach (var container in c.Programs.Cast<IRoutineContainer>().Concat(c.AddOnInstructions))
        {
            var program = container as Program;
            var aoi = container as AddOnInstructionDefinition;

            bool IsUnknownName(string root)
                => !root.Contains(':')
                   && FindTag(c, OperandPath.Simple(root), program, aoi) is null
                   && c.FindModule(root) is null;

            bool IsRoutineName(string root) => container.Routines.Any(r => string.Equals(r.Name, root, StringComparison.OrdinalIgnoreCase));

            foreach (var routine in container.Routines)
            {
                foreach (var content in Contents(routine))
                {
                    switch (content)
                    {
                        case RllContent rll:
                            foreach (var rung in rll.Rungs)
                            {
                                if (rung.Ast is null) continue;
                                rung.Ast = Parsing.Rll.RungRewriter.Rewrite(rung.Ast, instr =>
                                {
                                    if (instr.IsAddOn) return null;
                                    Operand[]? args = null;
                                    for (var i = 0; i < instr.Arguments.Count; i++)
                                    {
                                        switch (instr.Arguments[i])
                                        {
                                            case TagRefOperand { Path: { IsBare: true, ProgramName: null } path }:
                                                if (!IsUnknownName(path.Root) || IsRoutineName(path.Root)) continue;
                                                break;
                                            case ExpressionOperand { Expression: StBinary { Op: BinaryOp.Subtract, Left: StVariable { Path.IsBare: true } l, Right: StVariable { Path.IsBare: true } r } }
                                                when IsUnknownName(l.Path.Root) && IsUnknownName(r.Path.Root):
                                                // Motion profile keywords such as S-Curve parse as a subtraction.
                                                break;
                                            default:
                                                continue;
                                        }
                                        args ??= instr.Arguments.ToArray();
                                        args[i] = new KeywordOperand(instr.Arguments[i].Text);
                                    }
                                    return args is null ? null : instr with { Arguments = args };
                                });
                            }
                            break;
                        case StContent st when st.Ast is not null:
                            st.Ast = ReclassifyStCalls(st.Ast, IsUnknownName, IsRoutineName);
                            break;
                        case SfcContent sfc:
                            foreach (var step in sfc.Steps)
                                foreach (var action in step.Actions)
                                    if (action.Body?.Ast is not null) action.Body.Ast = ReclassifyStCalls(action.Body.Ast, IsUnknownName, IsRoutineName);
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// In structured text every argument parses as an expression, so <c>GSV(WallClockTime, ...)</c>, <c>MAM(..., S-Curve, ...)</c>
    /// and <c>JSR(Sub)</c> produce variables. Once tags are known, arguments of built-in instructions that name no tag
    /// become <see cref="StKeyword"/> nodes (routine names and labels are marked by their signature role).
    /// </summary>
    private static StBlock ReclassifyStCalls(StBlock block, Func<string, bool> isUnknownName, Func<string, bool> isRoutineName)
    {
        return StRewriter.RewriteCalls(block, call =>
        {
            if (call.IsAddOn) return null;
            StExpression?[]? args = null;
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var role = Parsing.Rll.InstructionSignatures.RoleAt(call.Name, i);
                var arg = call.Arguments[i];
                StKeyword? replacement = null;
                switch (arg)
                {
                    case StVariable { Path: { IsBare: true, ProgramName: null } p } v:
                        if (role == Parsing.Rll.OperandRole.Routine && isRoutineName(p.Root))
                            replacement = new StKeyword(v.Span, p.Root, StKeywordKind.Routine);
                        else if (role == Parsing.Rll.OperandRole.Label)
                            replacement = new StKeyword(v.Span, p.Root, StKeywordKind.Label);
                        else if (isUnknownName(p.Root) && !isRoutineName(p.Root))
                            replacement = new StKeyword(v.Span, p.Root, role == Parsing.Rll.OperandRole.Routine ? StKeywordKind.Routine : StKeywordKind.Keyword);
                        break;
                    case StBinary { Op: BinaryOp.Subtract, Left: StVariable { Path.IsBare: true } l, Right: StVariable { Path.IsBare: true } r } b
                        when isUnknownName(l.Path.Root) && isUnknownName(r.Path.Root):
                        // Motion profile keywords such as S-Curve parse as a subtraction.
                        replacement = new StKeyword(b.Span, l.Path.Root + "-" + r.Path.Root, StKeywordKind.Keyword);
                        break;
                }
                if (replacement is null) continue;
                args ??= call.Arguments.ToArray();
                args[i] = replacement;
            }
            return args is null ? null : call with { Arguments = args };
        });
    }

    // ------------------------------------------------------------------ routine calls

    /// <summary>
    /// Finds every JSR/FOR target named in each container's routines (<see cref="CollectCalls"/>) and resolves
    /// it to the actual <see cref="Routine"/>, filling <see cref="Routine.CalledRoutines"/> and
    /// <see cref="Routine.Callers"/> so both directions of the call graph are available without a search.
    /// A target that resolves to nothing is only a warning when the container's encoded (source-protected)
    /// routines are empty; otherwise the call most likely reaches one of those routines, whose name this parser
    /// cannot read, so it is reported as informational instead of as an error.
    /// </summary>
    private static void ResolveRoutineCalls(Controller c, IDiagnosticSink diagnostics)
    {
        foreach (var container in c.Programs.Cast<IRoutineContainer>().Concat(c.AddOnInstructions))
        {
            foreach (var routine in container.Routines)
            {
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectCalls(routine.Content, targets);
                foreach (var extra in routine.OnlineEditContents) CollectCalls(extra, targets);
                foreach (var name in targets)
                {
                    var target = container.Routines.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        var hasEncoded = container.EncodedRoutines.Count > 0;
                        var loc = new DiagnosticLocation(XmlLine: routine.SourceLine, Container: container.Name, Routine: routine.Name);
                        if (hasEncoded)
                            diagnostics.Info("LNK002", $"Routine '{routine.Name}' calls routine '{name}', which is source-protected (encoded) in this export", loc);
                        else
                            diagnostics.Warning("LNK002", $"Routine '{routine.Name}' calls unknown routine '{name}'", loc);
                        continue;
                    }
                    routine.CalledRoutines.Add(target);
                    target.Callers.Add(routine);
                }
            }
        }
    }

    /// <summary>Collects every JSR/FOR routine-name argument reachable from one routine body, across all four languages.</summary>
    private static void CollectCalls(RoutineContent? content, HashSet<string> targets)
    {
        switch (content)
        {
            case RllContent rll:
                foreach (var rung in rll.Rungs)
                {
                    if (rung.Ast is null) continue;
                    foreach (var instr in rung.Ast.AllInstructions())
                    {
                        if (instr.Mnemonic.Equals("JSR", StringComparison.OrdinalIgnoreCase) || instr.Mnemonic.Equals("FOR", StringComparison.OrdinalIgnoreCase))
                        {
                            if (instr.Arguments.Count > 0)
                            {
                                var a = instr.Arguments[0];
                                // The routine name may have classified as any of these, depending on how the
                                // operand parser read it before this linker had a chance to see every routine name.
                                var name = a switch
                                {
                                    RoutineRefOperand r => r.RoutineName,
                                    TagRefOperand t when t.Path.IsBare => t.Path.Root,
                                    KeywordOperand k => k.Text,
                                    _ => null,
                                };
                                if (name is not null) targets.Add(name);
                            }
                        }
                    }
                }
                break;
            case StContent st when st.Ast is not null:
                CollectStCalls(st.Ast, targets);
                break;
            case SfcContent sfc:
                foreach (var step in sfc.Steps)
                    foreach (var action in step.Actions)
                        if (action.Body?.Ast is not null) CollectStCalls(action.Body.Ast, targets);
                break;
            case FbdContent fbd:
                foreach (var sheet in fbd.Sheets)
                    foreach (var el in sheet.Elements)
                        if (el is FbdJsr j && j.Routine is not null) targets.Add(j.Routine);
                break;
        }
    }

    private static void CollectStCalls(StBlock block, HashSet<string> targets)
    {
        foreach (var s in block.Statements) CollectStCalls(s, targets);
    }

    /// <summary>Recurses into every control-flow body (IF/CASE/FOR/WHILE/REPEAT) looking for JSR/FOR call statements.</summary>
    private static void CollectStCalls(StStatement s, HashSet<string> targets)
    {
        switch (s)
        {
            case StCallStatement call:
                if ((call.Name.Equals("JSR", StringComparison.OrdinalIgnoreCase) || call.Name.Equals("FOR", StringComparison.OrdinalIgnoreCase)) && call.Arguments.Count > 0)
                {
                    switch (call.Arguments[0])
                    {
                        case StVariable { Path.IsBare: true } v: targets.Add(v.Path.Root); break;
                        case StKeyword k: targets.Add(k.Text); break;
                    }
                }
                break;
            case StIf i:
                foreach (var b in i.Branches) CollectStCalls(b.Body, targets);
                if (i.Else is not null) CollectStCalls(i.Else, targets);
                break;
            case StCase c:
                foreach (var arm in c.Arms) CollectStCalls(arm.Body, targets);
                if (c.Else is not null) CollectStCalls(c.Else, targets);
                break;
            case StFor f: CollectStCalls(f.Body, targets); break;
            case StWhile w: CollectStCalls(w.Body, targets); break;
            case StRepeat r: CollectStCalls(r.Body, targets); break;
        }
    }

    // ------------------------------------------------------------------ SFC / FBD graphs

    /// <summary>Links every SFC chart's and FBD diagram's numeric-ID references (links, wires, attachments) to the actual nodes/elements they name.</summary>
    private static void ResolveGraphs(Controller c, IDiagnosticSink diagnostics)
    {
        foreach (var routine in c.AllRoutines)
        {
            var loc = new DiagnosticLocation(XmlLine: routine.SourceLine, Container: routine.Owner?.Name, Routine: routine.Name);
            foreach (var content in Contents(routine))
            {
                switch (content)
                {
                    case SfcContent sfc: LinkSfc(sfc, loc, diagnostics); break;
                    case FbdContent fbd: LinkFbd(fbd, loc, diagnostics); break;
                }
            }
        }
    }

    /// <summary>The routine's current body plus any pending/test online-edit bodies, which also need their own graph links resolved.</summary>
    private static IEnumerable<RoutineContent> Contents(Routine r)
    {
        if (r.Content is not null) yield return r.Content;
        foreach (var e in r.OnlineEditContents) yield return e;
    }

    /// <summary>Builds <see cref="SfcContent.ById"/> (steps, actions, transitions, branches, legs, stops, subroutine markers) and resolves every directed link and attachment against it.</summary>
    private static void LinkSfc(SfcContent sfc, DiagnosticLocation loc, IDiagnosticSink diagnostics)
    {
        sfc.ById.Clear();
        foreach (var node in sfc.Nodes)
        {
            Register(node);
            if (node is SfcStep step) foreach (var a in step.Actions) Register(a);
            if (node is SfcBranch branch) foreach (var leg in branch.Legs) Register(leg);
        }

        void Register(SfcNode n)
        {
            if (n.Id < 0) return;
            if (!sfc.ById.TryAdd(n.Id, n))
                diagnostics.Warning("SFC001", $"Duplicate SFC element ID {n.Id}", loc);
        }

        foreach (var link in sfc.Links)
        {
            link.From = sfc.ById.GetValueOrDefault(link.FromId);
            link.To = sfc.ById.GetValueOrDefault(link.ToId);
            if (link.From is null || link.To is null)
                diagnostics.Warning("SFC002", $"DirectedLink {link.FromId}->{link.ToId} refers to an unknown element", loc);
        }
        foreach (var att in sfc.Attachments)
        {
            att.From = sfc.ById.GetValueOrDefault(att.FromId);
            att.To = sfc.ById.GetValueOrDefault(att.ToId);
        }
        var initial = sfc.Steps.Count(s => s.InitialStep);
        if (initial != 1 && sfc.Nodes.Count > 0)
            diagnostics.Info("SFC003", $"SFC has {initial} initial steps", loc);
    }

    /// <summary>Builds each sheet's <see cref="FbdSheet.ById"/> and resolves every wire's endpoints against it; sheets are independent, so IDs only need to be unique within one sheet.</summary>
    private static void LinkFbd(FbdContent fbd, DiagnosticLocation loc, IDiagnosticSink diagnostics)
    {
        foreach (var sheet in fbd.Sheets)
        {
            sheet.ById.Clear();
            foreach (var el in sheet.Elements)
            {
                if (!sheet.ById.TryAdd(el.Id, el))
                    diagnostics.Warning("FBD001", $"Duplicate FBD element ID {el.Id} on sheet {sheet.Number}", loc);
            }
            foreach (var wire in sheet.Wires)
            {
                wire.From = sheet.ById.GetValueOrDefault(wire.FromId);
                wire.To = sheet.ById.GetValueOrDefault(wire.ToId);
                if (wire.From is null || wire.To is null)
                    diagnostics.Warning("FBD002", $"Wire {wire.FromId}->{wire.ToId} refers to an unknown element on sheet {sheet.Number}", loc);
            }
        }
    }

    // ------------------------------------------------------------------ values

    /// <summary>
    /// For every tag with both a data type and an L5K initializer: shapes the L5K tree into a typed value when
    /// Decorated data is absent (this is the only typed value such a tag will have), or, when both are present
    /// and <see cref="ParseOptions.ValidateL5kAgainstDecorated"/> is set, shapes it anyway purely to cross-check
    /// it against the Decorated value and report any structural disagreement — a sign the shaper's assumptions
    /// about that type's layout do not match the real one.
    /// </summary>
    private static void ShapeValues(Controller c, DataTypeRegistry registry, ParseOptions options, IDiagnosticSink diagnostics)
    {
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in c.AllTags)
        {
            var data = tag.Data;
            if (data.L5k is null || tag.DataType is null) continue;
            if (data.Decorated is null)
            {
                var sink = new ScopedDiagnosticSink(diagnostics, new DiagnosticLocation(XmlLine: tag.SourceLine, Tag: tag.Name, Container: tag.Program?.Name ?? tag.Aoi?.Name));
                data.Shaped = L5kShaper.Shape(data.L5k, tag.DataType.Name, tag.Dimensions, registry, sink, reported);
            }
            else if (options.ValidateL5kAgainstDecorated)
            {
                // Only types whose L5K packing is known shape exactly; skip opaque predefined/add-on/module types.
                if (tag.DataType.Definition is StructuredType { IsOpaque: true } or StructuredType { Class: DataTypeClass.ModuleDefined or DataTypeClass.AddOn or DataTypeClass.Unresolved }) continue;
                var scratch = new DiagnosticBag();
                var shaped = L5kShaper.Shape(data.L5k, tag.DataType.Name, tag.Dimensions, registry, scratch, reported);
                if (shaped is not null && !ValueComparer.Equivalent(shaped, data.Decorated, out var why, typeName => registry.TryGet(typeName) is not StructuredType { IsOpaque: false }))
                    diagnostics.Info("LNK006", $"L5K and Decorated values differ for '{tag.Name}': {why}", new DiagnosticLocation(XmlLine: tag.SourceLine, Tag: tag.Name));
            }
        }
    }
}

/// <summary>
/// Structural comparison of two value trees, tolerant of radix differences and of sign-extension spelling
/// (a SINT of -1 and of 255 are the same bit pattern). Used only to validate that <see cref="L5kShaper"/>'s
/// best-effort shaping of L5K data agrees with the authoritative Decorated value; <paramref name="b"/> is always
/// the Decorated (trusted) side, which is why an opaque type is skipped there rather than on <paramref name="a"/>.
/// </summary>
public static class ValueComparer
{
    /// <param name="isOpaque">Returns true for structure type names whose L5K packing is unknown; those subtrees are not compared.</param>
    public static bool Equivalent(TagValue a, TagValue b, out string why, Func<string, bool>? isOpaque = null)
    {
        why = "";
        if (b is StructValue bs && isOpaque is not null && isOpaque(bs.DataType)) return true;
        switch (a, b)
        {
            case (ScalarValue x, ScalarValue y):
                if (x.Kind.IsFloatingPoint() || y.Kind.IsFloatingPoint())
                {
                    if (Math.Abs(x.AsDouble - y.AsDouble) > 1e-6 * Math.Max(1, Math.Abs(y.AsDouble)))
                    {
                        why = $"{x.AsDouble} vs {y.AsDouble}";
                        return false;
                    }
                    return true;
                }
                if (x.Bits != y.Bits && x.RawText is null && y.RawText is null)
                {
                    // Allow sign-extension differences (e.g. SINT -1 vs 255)
                    var mask = y.Kind.BitSize() >= 64 ? -1L : (1L << y.Kind.BitSize()) - 1;
                    if ((x.Bits & mask) == (y.Bits & mask)) return true;
                    why = $"{x.Bits} vs {y.Bits}";
                    return false;
                }
                return true;
            case (StringValue x, StringValue y):
                if (x.Text != y.Text)
                {
                    why = $"'{x.Text}' vs '{y.Text}'";
                    return false;
                }
                return true;
            case (StructValue x, StructValue y):
            {
                foreach (var kv in y.Members)
                {
                    var mine = x[kv.Key];
                    if (mine is null)
                    {
                        why = $"missing member {kv.Key}";
                        return false;
                    }
                    if (!Equivalent(mine, kv.Value, out why, isOpaque))
                    {
                        why = kv.Key + ": " + why;
                        return false;
                    }
                }
                return true;
            }
            case (ArrayValue x, ArrayValue y):
                if (x.Elements.Count != y.Elements.Count)
                {
                    why = $"array length {x.Elements.Count} vs {y.Elements.Count}";
                    return false;
                }
                for (var i = 0; i < x.Elements.Count; i++)
                {
                    if (!Equivalent(x.Elements[i], y.Elements[i], out why, isOpaque))
                    {
                        why = $"[{i}]: " + why;
                        return false;
                    }
                }
                return true;
            case (StructValue, StringValue) or (StringValue, StructValue):
                return true; // string family collapsed on one side only
            default:
                why = $"{a.GetType().Name} vs {b.GetType().Name}";
                return false;
        }
    }
}
