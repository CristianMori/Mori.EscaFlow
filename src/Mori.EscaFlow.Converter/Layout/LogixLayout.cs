using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Catalogs;
using Mori.EscaFlow.Transpiler.Logix;

namespace Mori.EscaFlow.Converter.Layout;

/// <summary>Byte layout of one structure as the target runtime and the tag server must present it.</summary>
/// <remarks>
/// A generated structure class binds each member to tag memory at these offsets, and the EtherNet/IP server publishes
/// the same offsets as a template, so a client that reads the whole structure gets the bytes a real controller
/// would send.
/// </remarks>
public sealed class StructLayout
{
    /// <summary>The structure (or add-on) name the layout describes.</summary>
    public string Name { get; init; } = "";

    /// <summary>Total size in bytes, rounded up to <see cref="Alignment"/>.</summary>
    public int Size { get; set; }

    /// <summary>4, or 8 when a 64-bit member forces it; nested members of this type are placed at multiples of it.</summary>
    public int Alignment { get; set; } = 4;

    /// <summary>Members in byte order, hidden hosts included.</summary>
    public List<MemberLayout> Members { get; } = new();

    /// <summary>True when the layout was computed by rule for a vendor type whose exact packing is not known.</summary>
    public bool Approximate { get; set; }

    /// <summary>The member named <paramref name="name"/> (Logix names are case-insensitive), or null.</summary>
    public MemberLayout? Find(string name) => Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One member of a <see cref="StructLayout"/>: where it starts, how it is typed and shaped.</summary>
public sealed class MemberLayout
{
    public string Name { get; init; } = "";

    /// <summary>Atomic type name, or the structure name.</summary>
    public string TypeName { get; init; } = "";

    /// <summary>True when <see cref="TypeName"/> is a structure (nested template) rather than an atomic.</summary>
    public bool IsStruct { get; init; }

    /// <summary>Byte offset from the start of the structure.</summary>
    public int Offset { get; set; }

    /// <summary>Bit inside the byte at <see cref="Offset"/> for BOOL members (BIT overlays and packed BOOLs).</summary>
    public int? Bit { get; set; }

    /// <summary>Array dimensions, empty for a scalar member.</summary>
    public int[] Dims { get; init; } = Array.Empty<int>();

    /// <summary>Size of one element in bytes (1 for BOOL members, which address a bit).</summary>
    public int ElementSize { get; set; }

    /// <summary>Hidden host member (not addressable by logic; present for the byte image only).</summary>
    public bool Hidden { get; init; }

    /// <summary>True for BOOL arrays packed 32 per DINT.</summary>
    public bool PackedBools { get; init; }

    /// <summary>Number of elements (1 for a scalar).</summary>
    public int Count => Dims.Length == 0 ? 1 : Dims.Aggregate(1, (a, b) => a * b);
}

/// <summary>
/// Computes Logix structure layouts from plc-ir types and function blocks: atomic members aligned to their size,
/// BIT overlays on their hosts (from <c>vendor.logix</c>), BOOL members without a host packed 8 per synthesized SINT
/// host, BOOL arrays packed 32 per DINT, structures aligned to 8 when they contain a 64-bit member else 4, sizes
/// rounded up to the alignment; add-on backing structures pack EnableIn/EnableOut and every BOOL parameter or
/// local 32 per hidden DINT host.
/// </summary>
/// <remarks>
/// <para>
/// Logix stores a user-defined type (UDT) the way a C compiler would store a struct with natural alignment: SINT on
/// any byte, INT on even addresses, DINT/REAL on multiples of four, LINT/LREAL on multiples of eight. BOOL members
/// are not bytes: Studio 5000 allocates hidden SINT hosts named <c>ZZZZZZZZZZ&lt;Type&gt;n</c> and maps up to eight
/// BOOLs onto their bits (the export records host and bit number per member, which the importer keeps in the vendor
/// slot). A BOOL array is different again: it is packed 32 bits per DINT. The total size is rounded up to the
/// structure's alignment so arrays of the structure stay aligned.
/// </para>
/// <para>
/// Add-on instruction instances use a backing structure Logix builds from the parameters and local tags: EnableIn
/// and EnableOut come first, every BOOL parameter or local is packed 32 per hidden DINT host in declaration order,
/// other members follow the UDT rules. InOut parameters are references and take no space.
/// </para>
/// <para>
/// Layouts are cached per type name; a type that (indirectly) contains itself is treated as opaque to stop the recursion.
/// </para>
/// </remarks>
public sealed class LogixLayoutCalculator
{
    private readonly IrDocument _doc;
    private readonly Dictionary<string, StructLayout> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names whose layout is being computed right now, to detect recursive member types.</summary>
    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);

    public LogixLayoutCalculator(IrDocument document) => _doc = document;

    /// <summary>Byte size of an atomic (1 for BOOL and the 8-bit types, up to 8 for the 64-bit types; 4 for unknown names).</summary>
    public static int AtomicSize(string name) => AtomicTypes.Get(name) is { } t ? Math.Max(1, t.Bits / 8) : 4;

    /// <summary>The CIP elementary data type code the EtherNet/IP server reports for an atomic (0 for structures).</summary>
    public static ushort CipTypeCode(string atomic) => atomic.ToUpperInvariant() switch
    {
        "BOOL" => 0xC1, "SINT" => 0xC2, "INT" => 0xC3, "DINT" => 0xC4, "LINT" => 0xC5,
        "USINT" => 0xC6, "UINT" => 0xC7, "UDINT" => 0xC8, "ULINT" => 0xC9, "REAL" => 0xCA, "LREAL" => 0xCB,
        "BYTE" => 0xD1, "WORD" => 0xD2, "DWORD" => 0xD3, "LWORD" => 0xD4, "TIME" => 0xCC, "DATE" => 0xCD, "TOD" => 0xCE, "DT" => 0xCF,
        _ => 0,
    };

    /// <summary>Size and alignment of a type reference (atomic, structure, function block instance or array).</summary>
    public (int Size, int Alignment) SizeOf(IrTypeRef type)
    {
        switch (type.Kind)
        {
            case "named":
            {
                var name = type.Name ?? "DINT";
                if (AtomicTypes.IsAtomic(name))
                {
                    // A standalone BOOL tag occupies a byte; only BOOL members of structures are packed into hosts.
                    if (name.Equals("BOOL", StringComparison.OrdinalIgnoreCase)) return (1, 1);
                    var s = AtomicSize(name);
                    return (s, s);
                }
                var layout = Get(name);
                return layout is null ? (4, 4) : (layout.Size, layout.Alignment);
            }
            case "array":
            {
                var element = type.Element ?? IrTypeRef.Named("DINT");
                var count = (type.Dims ?? new()).Aggregate(1L, (a, d) => a * d.Count);
                // BOOL arrays: 32 bits per DINT, DINT aligned.
                if (element.Is("BOOL")) return ((int)((count + 31) / 32 * 4), 4);
                var (es, ea) = SizeOf(element);
                return ((int)(es * count), ea);
            }
            case "string":
                // LEN (DINT) followed by DATA (SINT[capacity]), padded to a DINT boundary.
                return (Align(4 + (type.Length ?? 82), 4), 4);
            default:
                return (4, 4);
        }
    }

    /// <summary>Layout of a named structure, function block backing structure or string type; null for atomics and unknown names.</summary>
    public StructLayout? Get(string name)
    {
        if (AtomicTypes.IsAtomic(name)) return null;
        if (_cache.TryGetValue(name, out var cached)) return cached;
        if (!_inProgress.Add(name)) return null; // recursive type: treated as opaque
        try
        {
            StructLayout? layout = null;
            if (_doc.FindType(name) is { } type) layout = Build(type);
            else if (_doc.FindPou(name) is { Kind: "functionBlock" } pou) layout = BuildBacking(pou);
            if (layout is not null) _cache[name] = layout;
            return layout;
        }
        finally
        {
            _inProgress.Remove(name);
        }
    }

    /// <summary>Every layout of the document: structure and string types first, then add-on backing structures.</summary>
    public IEnumerable<StructLayout> All()
    {
        foreach (var t in _doc.Types) if (t.Kind is "struct" or "string" && Get(t.Name) is { } l) yield return l;
        foreach (var p in _doc.Pous) if (p.Kind == "functionBlock" && Get(p.Name) is { } l) yield return l;
    }

    // ------------------------------------------------------------------ structures

    /// <summary>Lays out a structure or string type following the UDT rules described on the class.</summary>
    private StructLayout Build(IrType type)
    {
        var layout = new StructLayout { Name = type.Name };
        if (type.Kind == "string")
        {
            // Every Logix string family type is LEN + DATA[capacity]; the capacity is the only variable part.
            var capacity = type.Length ?? 82;
            layout.Members.Add(new MemberLayout { Name = "LEN", TypeName = "DINT", Offset = 0, ElementSize = 4 });
            layout.Members.Add(new MemberLayout { Name = "DATA", TypeName = "SINT", Offset = 4, Dims = new[] { capacity }, ElementSize = 1 });
            layout.Size = Align(4 + capacity, 4);
            layout.Alignment = 4;
            return layout;
        }

        var ext = type.Logix();
        // A predefined type whose BOOL members have no recorded hosts (the manuals document members, not packing)
        // is laid out by rule and reported as approximate.
        layout.Approximate = type.Class == "vendor" && ext?.Class == "predefined" && ext.HiddenMembers is null && (type.Members ?? new()).Any(m => m.Type.Is("BOOL"));

        // Reconstruct the full member order: visible members interleaved with hidden hosts at their recorded positions.
        // The importer removed the hidden hosts from the neutral member list but kept their original index, so the
        // byte order of the export can be rebuilt exactly.
        var ordered = new List<(string Name, IrTypeRef Type, bool Hidden, LogixMemberExt? Ext)>();
        var visible = (type.Members ?? new()).Select(m => (m.Name, m.Type, false, m.Logix())).ToList();
        var hidden = ext?.HiddenMembers ?? new List<LogixHiddenMember>();
        var total = visible.Count + hidden.Count;
        var vi = 0;
        for (var pos = 0; pos < total; pos++)
        {
            var h = hidden.FirstOrDefault(x => x.Position == pos);
            if (h is not null) ordered.Add((h.Name, h.Dims is { } d ? IrTypeRef.Array(IrTypeRef.Named(h.Type), new[] { d }) : IrTypeRef.Named(h.Type), true, null));
            else if (vi < visible.Count) ordered.Add(visible[vi++]);
        }
        while (vi < visible.Count) ordered.Add(visible[vi++]);

        var offset = 0;
        var alignment = 4;
        // Offsets of the hidden hosts placed so far, so BIT overlays that follow can address them.
        var hostOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var synthesizedHost = -1;
        var synthesizedBits = 8;
        foreach (var (name, mtype, isHidden, mext) in ordered)
        {
            if (mtype.Is("BOOL"))
            {
                if (mext?.BitHost is { } host && hostOffsets.TryGetValue(host, out var hostOffset))
                {
                    // BIT overlay recorded by the export: bit n of the host, which may be wider than a byte.
                    var bit = mext.BitNumber ?? 0;
                    layout.Members.Add(new MemberLayout { Name = name, TypeName = "BOOL", Offset = hostOffset + bit / 8, Bit = bit % 8, ElementSize = 1 });
                    continue;
                }
                // No host in the source: synthesize SINT hosts, 8 bits each, in member order.
                if (synthesizedBits == 8)
                {
                    synthesizedHost = offset;
                    layout.Members.Add(new MemberLayout { Name = $"ZZZZZZZZZZ{type.Name}_{layout.Members.Count}", TypeName = "SINT", Offset = offset, ElementSize = 1, Hidden = true });
                    offset += 1;
                    synthesizedBits = 0;
                    layout.Approximate = true;
                }
                layout.Members.Add(new MemberLayout { Name = name, TypeName = "BOOL", Offset = synthesizedHost, Bit = synthesizedBits++, ElementSize = 1 });
                continue;
            }
            var member = Place(name, mtype, ref offset, ref alignment, isHidden);
            layout.Members.Add(member);
            if (isHidden) hostOffsets[name] = member.Offset;
        }
        layout.Alignment = alignment;
        layout.Size = Align(Math.Max(offset, 1), alignment);
        return layout;
    }

    /// <summary>Add-on instruction backing structure: EnableIn, EnableOut, input and output parameters, then locals; BOOLs packed 32 per hidden DINT host.</summary>
    /// <remarks>
    /// The export does not spell out the backing layout, so it is reconstructed from the published rule (Logix 5000
    /// Import/Export reference, add-on data type section) and flagged approximate: a tag server presenting an add-on
    /// instance byte-for-byte may differ from the real controller when Logix reorders small members into alignment gaps.
    /// </remarks>
    private StructLayout BuildBacking(IrPou pou)
    {
        var layout = new StructLayout { Name = pou.Name, Approximate = true };
        // InOut parameters are references to the caller's tags and occupy no bytes of the instance.
        var vars = pou.Vars.Where(v => v.Section is "input" or "output" or "local").ToList();
        // EnableIn / EnableOut first, in that order, then the rest as declared.
        var ordered = vars.Where(v => v.Name.Equals("EnableIn", StringComparison.OrdinalIgnoreCase))
            .Concat(vars.Where(v => v.Name.Equals("EnableOut", StringComparison.OrdinalIgnoreCase)))
            .Concat(vars.Where(v => !v.Name.Equals("EnableIn", StringComparison.OrdinalIgnoreCase) && !v.Name.Equals("EnableOut", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var offset = 0;
        var alignment = 4;
        var hostOffset = -1;
        var hostBits = 32;
        foreach (var v in ordered)
        {
            if (v.Type.Is("BOOL"))
            {
                // A new DINT host every 32 BOOLs (the 1st, 33rd, 65th ... BOOL opens one), in declaration order.
                if (hostBits == 32)
                {
                    offset = Align(offset, 4);
                    hostOffset = offset;
                    layout.Members.Add(new MemberLayout { Name = $"ZZZZZZZZZZ{pou.Name}_{layout.Members.Count}", TypeName = "DINT", Offset = offset, ElementSize = 4, Hidden = true });
                    offset += 4;
                    hostBits = 0;
                }
                layout.Members.Add(new MemberLayout { Name = v.Name, TypeName = "BOOL", Offset = hostOffset + hostBits / 8, Bit = hostBits % 8, ElementSize = 1 });
                hostBits++;
                continue;
            }
            layout.Members.Add(Place(v.Name, v.Type, ref offset, ref alignment, false));
        }
        layout.Alignment = alignment;
        layout.Size = Align(Math.Max(offset, 4), alignment);
        return layout;
    }

    /// <summary>Places a non-BOOL member (or a BOOL array) at the next aligned offset and advances the cursor and the structure alignment.</summary>
    private MemberLayout Place(string name, IrTypeRef type, ref int offset, ref int alignment, bool hidden)
    {
        int[] dims = Array.Empty<int>();
        var element = type;
        if (type.IsArray)
        {
            dims = (type.Dims ?? new()).Select(d => (int)d.Count).ToArray();
            element = type.Element ?? IrTypeRef.Named("DINT");
        }
        if (element.Is("BOOL") && dims.Length > 0)
        {
            // BOOL[n] member: n bits packed into ceil(n / 32) DINTs.
            var count = dims.Aggregate(1, (a, b) => a * b);
            offset = Align(offset, 4);
            var m = new MemberLayout { Name = name, TypeName = "BOOL", Offset = offset, Dims = dims, ElementSize = 1, PackedBools = true, Hidden = hidden };
            offset += (count + 31) / 32 * 4;
            alignment = Math.Max(alignment, 4);
            return m;
        }
        var (size, align) = SizeOf(element);
        // Nested structures and strings always start on a DINT boundary, whatever their own alignment says.
        if (element.Kind == "string" || (element.IsNamed && !AtomicTypes.IsAtomic(element.Name!))) align = Math.Max(align, 4);
        offset = Align(offset, align);
        var isStruct = element.Kind == "string" || (element.IsNamed && !AtomicTypes.IsAtomic(element.Name!));
        var member = new MemberLayout
        {
            Name = name,
            TypeName = element.Kind == "string" ? $"STRING{element.Length}" : element.Name ?? "DINT",
            IsStruct = isStruct,
            Offset = offset,
            Dims = dims,
            ElementSize = size,
            Hidden = hidden,
        };
        var count2 = dims.Length == 0 ? 1 : dims.Aggregate(1, (a, b) => a * b);
        offset += size * count2;
        // A 64-bit member raises the structure alignment to 8; nothing raises it beyond that.
        alignment = Math.Max(alignment, Math.Min(align, 8));
        return member;
    }

    /// <summary>Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.</summary>
    private static int Align(int value, int alignment) => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
}
