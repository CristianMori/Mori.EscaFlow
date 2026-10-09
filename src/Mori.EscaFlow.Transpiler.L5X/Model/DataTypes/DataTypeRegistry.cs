using System.Collections;

namespace Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;

/// <summary>Case-insensitive collection of the user-defined data types of a controller, in file order.</summary>
public sealed class DataTypeCollection : IReadOnlyList<StructuredType>
{
    private readonly List<StructuredType> _items = new();
    private readonly Dictionary<string, StructuredType> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _items.Count;
    public StructuredType this[int index] => _items[index];
    public StructuredType? this[string name] => _byName.TryGetValue(name, out var t) ? t : null;

    public void Add(StructuredType type)
    {
        _items.Add(type);
        _byName[type.Name] = type;
    }

    public bool Contains(string name) => _byName.ContainsKey(name);

    public IEnumerator<StructuredType> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// All data types known to a project: atomics, predefined structures, user types, add-on backing types and
/// module-defined types. Lookups are case-insensitive.
/// <para>
/// The logic parse phase runs many routines concurrently and some of them (module-defined type discovery)
/// register new types as they go, so every member here is guarded by a single lock. Reads are cheap and the
/// registry is only mutated during parsing and linking, never afterward, so contention is not a concern.
/// </para>
/// </summary>
public sealed class DataTypeRegistry
{
    private readonly Dictionary<string, DataTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IReadOnlyCollection<DataTypeDefinition> All => _types.Values;

    public DataTypeDefinition? TryGet(string name)
    {
        lock (_lock)
        {
            return _types.TryGetValue(name, out var t) ? t : null;
        }
    }

    public bool Contains(string name)
    {
        lock (_lock) return _types.ContainsKey(name);
    }

    /// <summary>Registers a definition, replacing any earlier one with the same name.</summary>
    public void Register(DataTypeDefinition definition)
    {
        lock (_lock) _types[definition.Name] = definition;
    }

    /// <summary>Registers a definition only when the name is still free. Returns the definition now registered under that name.</summary>
    public DataTypeDefinition RegisterIfAbsent(DataTypeDefinition definition)
    {
        lock (_lock)
        {
            if (_types.TryGetValue(definition.Name, out var existing)) return existing;
            _types[definition.Name] = definition;
            return definition;
        }
    }

    /// <summary>
    /// Returns the module-defined type with this name, creating an opaque placeholder when unseen. Module types
    /// are discovered on demand from tag data rather than pre-declared (see <see cref="DataTypeClass.ModuleDefined"/>),
    /// so the first reference to e.g. <c>AB:1756_DI:I:0</c> is what creates its registry entry.
    /// </summary>
    public StructuredType GetOrAddModuleDefined(string name)
    {
        lock (_lock)
        {
            if (_types.TryGetValue(name, out var existing) && existing is StructuredType s) return s;
            var t = new StructuredType { Name = name, Class = DataTypeClass.ModuleDefined, IsOpaque = true, ModuleType = ModuleTypeInfo.TryParse(name) };
            _types[name] = t;
            return t;
        }
    }

    /// <summary>Resolves a reference in place. Returns false when the name is unknown.</summary>
    public bool Resolve(DataTypeRef reference)
    {
        if (reference.Definition is not null) return true;
        var def = TryGet(reference.Name);
        if (def is null) return false;
        reference.Definition = def;
        return true;
    }
}
