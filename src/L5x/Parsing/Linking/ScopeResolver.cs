using L5x.Model;
using L5x.Model.Aoi;
using L5x.Model.DataTypes;
using L5x.Model.Modules;
using L5x.Model.Programs;
using L5x.Model.Tags;

namespace L5x.Parsing.Linking;

/// <summary>Result of resolving an <see cref="OperandPath"/> against a scope.</summary>
public sealed record ResolvedReference
{
    /// <summary>The declaring tag; null for module I/O tags (<c>Local:1:I</c>) and unresolved roots.</summary>
    public Tag? Tag { get; init; }

    /// <summary>The scope the tag was found in.</summary>
    public TagScope? Scope { get; init; }

    /// <summary>For module tags: the module that owns the data.</summary>
    public Module? Module { get; init; }

    /// <summary>For module tags: the connection data (input/output/config) the path starts at.</summary>
    public ModuleTagData? ModuleData { get; init; }

    /// <summary>Data type of the referenced element; BOOL for bit references. Null when unknown.</summary>
    public DataTypeDefinition? LeafType { get; init; }

    /// <summary>Dimensions still unindexed at the end of the path (e.g. referencing a whole array). Empty for scalars.</summary>
    public int[] LeafDimensions { get; init; } = Array.Empty<int>();

    /// <summary>The structure member the path ends on, when it ends on a member.</summary>
    public DataTypeMember? LeafMember { get; init; }

    /// <summary>True when the path selects a single bit of an integer (<c>.n</c>, <c>.[i]</c> or a BIT overlay member).</summary>
    public bool IsBit { get; init; }

    /// <summary>Constant bit number when known.</summary>
    public int? BitNumber { get; init; }

    /// <summary>The integer host type when <see cref="IsBit"/> (the type the bit is taken from).</summary>
    public DataTypeDefinition? BitHostType { get; init; }

    /// <summary>The path after alias expansion (the tag's real path).</summary>
    public required OperandPath ExpandedPath { get; init; }

    /// <summary>Why resolution stopped early, or null when fully resolved.</summary>
    public string? Error { get; init; }

    /// <summary>True when the path walked to a leaf with a known type.</summary>
    public bool IsResolved => Error is null && LeafType is not null;

    /// <summary>True when the path names a module itself (GSV/SSV instance operand) rather than tag data.</summary>
    public bool IsModuleReference => Module is not null && Tag is null && ModuleData is null;

    /// <summary>True when the root was found but the type walk hit an opaque (member-less) type.</summary>
    public bool IsOpaque { get; init; }

    /// <summary>
    /// True when the reference is I/O data: a module tag (<c>Local:1:I</c>) or a tag / member whose type is a
    /// module-defined structure such as <c>AB:1756_DI:I:0</c> or a third-party <c>_0015:...:C:0</c>.
    /// </summary>
    public bool IsIoData { get; init; }

    /// <summary>The module-defined type the path passed through, when <see cref="IsIoData"/>.</summary>
    public StructuredType? IoType { get; init; }

    public bool IsArrayReference => LeafDimensions.Length > 0;
}

/// <summary>
/// Resolves tag paths to declarations and leaf types, following aliases, program/AOI scoping and the
/// data type registry. Intended for transpilers that need the type of every operand.
/// </summary>
public sealed class ScopeResolver
{
    private readonly Controller _controller;
    private readonly DataTypeRegistry _registry;
    private readonly Dictionary<string, (Module Module, ModuleTagData Data, bool Opaque)> _moduleTags = new(StringComparer.OrdinalIgnoreCase);

    public ScopeResolver(Controller controller)
    {
        _controller = controller;
        _registry = controller.Types;
        IndexModuleTags();
    }

    /// <summary>Resolves in program scope (program tags first, then controller tags).</summary>
    public ResolvedReference Resolve(OperandPath path, Program? program) => Resolve(path, program, null);

    /// <summary>Resolves in add-on instruction scope (parameters and local tags, then controller tags).</summary>
    public ResolvedReference Resolve(OperandPath path, AddOnInstructionDefinition aoi) => Resolve(path, null, aoi);

    /// <summary>Resolves in the scope of a routine container (program or add-on definition).</summary>
    public ResolvedReference Resolve(OperandPath path, IRoutineContainer? container) => container switch
    {
        Program p => Resolve(path, p, null),
        AddOnInstructionDefinition a => Resolve(path, null, a),
        _ => Resolve(path, null, null),
    };

    public ResolvedReference Resolve(OperandPath path, Program? program, AddOnInstructionDefinition? aoi)
    {
        // System bits such as S:FS (first scan)
        if (IsSystemTag(path.Root))
            return new ResolvedReference { ExpandedPath = path, LeafType = _registry.TryGet("BOOL"), IsBit = true, Error = null };

        // Module I/O tags such as Local:1:I or Rack2:3:O.Data
        if (path.Root.Contains(':') && path.ProgramName is null)
            return ResolveModuleTag(path);

        var tag = L5xLinker.FindTag(_controller, path, program, aoi);
        if (tag is null)
        {
            // GSV/SSV/MSG operands can name a module directly (class Module, instance = module name).
            var module = _controller.FindModule(path.Root);
            if (module is not null && path.IsBare)
                return new ResolvedReference { ExpandedPath = path, Module = module, Scope = TagScope.Module, Error = null };
            return new ResolvedReference { ExpandedPath = path, Error = $"Tag '{path.Root}' not found" };
        }

        // Follow aliases: the alias path is the real tag path, our segments continue from there.
        var expanded = path;
        var guard = 0;
        while (tag.TagType == TagType.Alias)
        {
            if (tag.AliasPath is null || tag.AliasTarget is null)
                return new ResolvedReference { Tag = tag, Scope = tag.Scope, ExpandedPath = expanded, Error = $"Alias '{tag.Name}' target '{tag.AliasFor}' is not resolved" };
            var merged = new List<PathSegment>(tag.AliasPath.Segments);
            merged.AddRange(expanded.Segments);
            expanded = new OperandPath(tag.AliasPath.Root, merged, tag.AliasPath.ProgramName);
            tag = tag.AliasTarget;
            if (++guard > 16)
                return new ResolvedReference { Tag = tag, Scope = tag.Scope, ExpandedPath = expanded, Error = "Alias chain too deep" };
        }

        if (tag.DataType is null)
            return new ResolvedReference { Tag = tag, Scope = tag.Scope, ExpandedPath = expanded, Error = $"Tag '{tag.Name}' has no data type" };

        var type = tag.DataType.Definition ?? _registry.TryGet(tag.DataType.Name);
        return Walk(expanded, expanded.Segments, type, tag.Dimensions, tag, tag.Scope, null, null);
    }

    private ResolvedReference ResolveModuleTag(OperandPath path)
    {
        if (!_moduleTags.TryGetValue(path.Root, out var entry))
            return new ResolvedReference { ExpandedPath = path, Error = $"Module tag '{path.Root}' not found" };
        if (entry.Opaque)
        {
            // Rack-optimized slot: the data lives inside the parent's connection. Point I/O adapters expose it as a
            // byte/word array named Data indexed by slot, so the slot tag has that element type.
            var parentTypeName = entry.Data.DataTypeName;
            var parentType = parentTypeName is null ? null : _registry.TryGet(parentTypeName) as StructuredType;
            var dataMember = parentType?.FindMember("Data");
            if (dataMember is { IsArray: true } && (dataMember.DataType.Definition ?? _registry.TryGet(dataMember.DataType.Name)) is AtomicType slotType && !slotType.Kind.IsBoolean())
            {
                return Walk(path, path.Segments, slotType, Array.Empty<int>(), null, TagScope.Module, entry.Module, entry.Data);
            }
            var bit = path.Segments.Count > 0 && path.Segments[^1] is BitSegment or DynamicBitSegment;
            return new ResolvedReference
            {
                ExpandedPath = path,
                Module = entry.Module,
                ModuleData = entry.Data,
                Scope = TagScope.Module,
                IsIoData = true,
                IoType = parentType,
                IsOpaque = true,
                IsBit = bit,
                BitNumber = (path.Segments.Count > 0 ? path.Segments[^1] as BitSegment : null)?.Bit,
                LeafType = bit ? _registry.TryGet("BOOL") : null,
            };
        }
        var typeName = entry.Data.DataTypeName;
        var type = typeName is null ? null : _registry.TryGet(typeName);
        return Walk(path, path.Segments, type, Array.Empty<int>(), null, TagScope.Module, entry.Module, entry.Data);
    }

    private ResolvedReference Walk(OperandPath expanded, IReadOnlyList<PathSegment> segments, DataTypeDefinition? type, int[] dims, Tag? tag, TagScope? scope, Module? module, ModuleTagData? moduleData)
    {
        DataTypeMember? leafMember = null;
        var isBit = false;
        int? bitNumber = null;
        DataTypeDefinition? bitHost = null;
        var opaque = false;
        StructuredType? ioType = type as StructuredType is { IsModuleDefined: true } io0 ? io0 : null;

        ResolvedReference Done(string? error) => new()
        {
            Tag = tag,
            Scope = scope,
            Module = module,
            ModuleData = moduleData,
            IsIoData = ioType is not null || moduleData is not null,
            IoType = ioType,
            LeafType = isBit ? _registry.TryGet("BOOL") : type,
            LeafDimensions = dims,
            LeafMember = leafMember,
            IsBit = isBit,
            BitNumber = bitNumber,
            BitHostType = bitHost,
            ExpandedPath = expanded,
            Error = error,
            IsOpaque = opaque,
        };

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (isBit) return Done($"Cannot apply '{seg}' to a bit reference");
            switch (seg)
            {
                case IndexSegment idx:
                {
                    if (dims.Length == 0)
                    {
                        // Indexing a BOOL array member declared as e.g. BOOL[32] on a DINT host is represented with Dimension; anything else is invalid.
                        if (type is AtomicType at && !at.Kind.IsFloatingPoint() && !at.Kind.IsBoolean())
                        {
                            // Some exports index an integer to reach a bit ("Word[3]" is not legal, but tolerate as bit).
                            isBit = true;
                            bitHost = type;
                            bitNumber = idx.ConstantIndices() is { Length: 1 } ci ? ci[0] : null;
                            continue;
                        }
                        return Done($"'{seg}' indexes a non-array of type {type?.Name ?? "?"}");
                    }
                    if (idx.Indices.Count != dims.Length)
                        return Done($"'{seg}' has {idx.Indices.Count} indices but the array has rank {dims.Length}");
                    dims = Array.Empty<int>();
                    continue;
                }
                case MemberSegment m:
                {
                    if (dims.Length > 0) return Done($"Member '{m.Name}' accessed on an unindexed array");
                    if (type is null) return Done($"Member '{m.Name}' on unknown type");
                    if (type is not StructuredType st)
                        return Done($"Member '{m.Name}' on atomic type {type.Name}");
                    var member = st.FindMember(m.Name);
                    if (member is null)
                    {
                        if (st.IsOpaque || st.Members.Count == 0)
                        {
                            opaque = true;
                            type = null;
                            leafMember = null;
                            // keep walking blind: remaining members are unknown but not an error
                            for (var j = i + 1; j < segments.Count; j++)
                            {
                                if (segments[j] is BitSegment or DynamicBitSegment)
                                {
                                    isBit = true;
                                    bitNumber = (segments[j] as BitSegment)?.Bit;
                                }
                            }
                            return Done(null);
                        }
                        return Done($"Type {st.Name} has no member '{m.Name}'");
                    }
                    leafMember = member;
                    if (member.IsBitOverlay)
                    {
                        isBit = true;
                        bitNumber = member.BitNumber;
                        bitHost = member.BitHost?.DataType.Definition ?? (member.BitHost is null ? null : _registry.TryGet(member.BitHost.DataType.Name));
                        type = _registry.TryGet("BOOL");
                        dims = Array.Empty<int>();
                        continue;
                    }
                    type = member.DataType.Definition ?? _registry.TryGet(member.DataType.Name);
                    if (type is null && member.DataType.Name.Contains(':')) type = _registry.TryGet(member.DataType.Name);
                    if (type is StructuredType { IsModuleDefined: true } io) ioType ??= io;
                    dims = member.Dimension > 0 ? new[] { member.Dimension } : Array.Empty<int>();
                    continue;
                }
                case BitSegment b:
                {
                    if (dims.Length > 0) return Done($"Bit '.{b.Bit}' on an unindexed array");
                    if (type is AtomicType at && !at.Kind.IsFloatingPoint())
                    {
                        isBit = true;
                        bitHost = type;
                        bitNumber = b.Bit;
                        continue;
                    }
                    if (type is null)
                    {
                        isBit = true;
                        bitNumber = b.Bit;
                        continue;
                    }
                    return Done($"Bit '.{b.Bit}' on non-integer type {type.Name}");
                }
                case DynamicBitSegment:
                {
                    if (dims.Length > 0) return Done("Indirect bit on an unindexed array");
                    if (type is AtomicType at && !at.Kind.IsFloatingPoint())
                    {
                        isBit = true;
                        bitHost = type;
                        continue;
                    }
                    if (type is null)
                    {
                        isBit = true;
                        continue;
                    }
                    return Done($"Indirect bit on non-integer type {type.Name}");
                }
            }
        }
        if (type is StructuredType { IsOpaque: true }) opaque = true;
        return Done(null);
    }

    private void IndexModuleTags()
    {
        // Module tag roots: "<Module>:<I|O|C>", "<Module>:<I|O><n>" for numbered data-driven connections,
        // "<Parent>:<Slot>:<I|O|C>" for chassis/point-bus modules, and "<Module>:<ConnName>:<I|O>" for named ones.
        var byName = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _controller.Modules) byName[m.Name] = m;

        foreach (var m in _controller.Modules)
        {
            var comm = m.Communications;
            var prefixes = ModuleTagPrefixes(m).ToList();

            void Add(string suffix, ModuleTagData? data, bool opaque = false)
            {
                if (data is null) return;
                foreach (var p in prefixes) _moduleTags.TryAdd(p + ":" + suffix, (m, data, opaque));
            }

            var hasConnections = comm is not null && comm.Connections.Count > 0;
            if (comm is not null)
            {
                Add("C", comm.ConfigTag ?? comm.ConfigData);
                var n = 0;
                foreach (var conn in comm.Connections)
                {
                    n++;
                    Add("I", conn.InputTag);
                    Add("O", conn.OutputTag);
                    Add("I" + n, conn.InputTag);
                    Add("O" + n, conn.OutputTag);
                    if (conn.Type is not null && conn.Type.StartsWith("Safety", StringComparison.OrdinalIgnoreCase))
                    {
                        // Safety connections are addressed as Module:SI / Module:SO.
                        Add("SI", conn.InputTag);
                        Add("SO", conn.OutputTag);
                    }
                    if (!string.IsNullOrEmpty(conn.Name) && !conn.Name.StartsWith('_'))
                    {
                        Add(conn.Name + ":I", conn.InputTag);
                        Add(conn.Name + ":O", conn.OutputTag);
                    }
                }
            }

            // Rack-optimized slot: no own connection, data is carried by the parent's connection.
            if (!hasConnections && !string.IsNullOrEmpty(m.ParentModule) && byName.TryGetValue(m.ParentModule, out var parent) && parent.Communications is { } pc)
            {
                var input = pc.Connections.Select(c => c.InputTag).FirstOrDefault(t => t is not null);
                var output = pc.Connections.Select(c => c.OutputTag).FirstOrDefault(t => t is not null);
                if (input is not null) foreach (var p in prefixes) _moduleTags.TryAdd(p + ":I", (parent, input, true));
                if (output is not null) foreach (var p in prefixes) _moduleTags.TryAdd(p + ":O", (parent, output, true));
                var config = comm?.ConfigTag ?? comm?.ConfigData ?? pc.ConfigTag ?? pc.ConfigData;
                if (config is not null) foreach (var p in prefixes) _moduleTags.TryAdd(p + ":C", (parent, config, comm?.ConfigTag is null && comm?.ConfigData is null));
            }
        }
    }

    private static IEnumerable<string> ModuleTagPrefixes(Module m)
    {
        yield return m.Name;
        // Chassis-based modules are addressed through their parent: Local:3:I means slot 3 in the Local chassis.
        if (!string.IsNullOrEmpty(m.ParentModule) && !string.Equals(m.ParentModule, m.Name, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var port in m.Ports)
            {
                if (port.Upstream && port.Address is not null && port.Address.All(char.IsDigit))
                    yield return m.ParentModule + ":" + port.Address;
            }
        }
    }

    private static bool IsSystemTag(string root)
        => root.Length > 2 && (root[0] == 'S' || root[0] == 's') && root[1] == ':';
}
