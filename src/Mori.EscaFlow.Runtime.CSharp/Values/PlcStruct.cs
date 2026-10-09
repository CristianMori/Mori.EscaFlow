using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Base of every generated structure class: a view over a byte range of a tag buffer whose public readonly
/// fields are the member wrappers. Generated classes declare <c>public static readonly PlcLayout Layout</c>;
/// <see cref="Size"/> reads it.
/// </summary>
/// <remarks>
/// A structure owns no memory of its own. Its constructor receives the tag and the byte offset where the
/// structure starts, and builds one wrapper per member at <c>offset + memberOffset</c>, the member offsets
/// having been computed by the converter with the Logix packing rules (members aligned to their size, BIT
/// overlays on hidden hosts, structures padded to 4 or 8 bytes). Nested structures and arrays are built the same
/// way, recursively, so a controller tag of a deep user type becomes one tree of views over one contiguous buffer,
/// and the EtherNet/IP client that asks for the whole tag receives exactly the bytes the logic works on.
/// <see cref="GetMember"/> and <see cref="TryGetMember"/> give services such as MSG name-based access to members
/// of structures they only know by convention (<c>.EN</c>, <c>.DN</c>, <c>.ER</c>).
/// </remarks>
public abstract class PlcStruct : IPlcMemory
{
    /// <summary>Layouts discovered through reflection, cached per concrete class (reflection once, not per instance).</summary>
    private static readonly ConcurrentDictionary<Type, PlcLayout?> Layouts = new();

    /// <summary>Public member fields per concrete class, for the reflective fallback of <see cref="TryGetMember"/>.</summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, FieldInfo>> MemberFields = new();

    /// <summary>Binds the structure to <paramref name="tag"/> starting at byte <paramref name="offset"/>.</summary>
    protected PlcStruct(Tag tag, int offset)
    {
        Tag = tag;
        Offset = offset;
    }

    /// <inheritdoc />
    public Tag Tag { get; }

    /// <inheritdoc />
    public int Offset { get; }

    /// <summary>Byte size of the structure, from the generated static <c>Layout</c> field.</summary>
    public virtual int Size => LayoutOf(GetType())?.Size ?? 0;

    /// <inheritdoc />
    public int ByteSize => Size;

    /// <summary>The layout declared by the concrete class, or null for hand-written structures without one.</summary>
    /// <remarks>Walks the class hierarchy so a derived class (a user string type deriving from <see cref="WrapSTRING"/>) finds its own layout before the base one.</remarks>
    public static PlcLayout? LayoutOf(Type type) => Layouts.GetOrAdd(type, static t =>
    {
        for (var cur = t; cur is not null; cur = cur.BaseType)
        {
            var field = cur.GetField("Layout", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field?.GetValue(null) is PlcLayout layout) return layout;
        }
        return null;
    });

    /// <summary>A member wrapper, structure or array by name; generated classes override this with a switch over their members. Null when unknown.</summary>
    public virtual object? GetMember(string name) => null;

    /// <summary>Finds a member by name (case-insensitive): <see cref="GetMember"/> first, then the public fields of the class.</summary>
    /// <remarks>The reflective path exists for hand-written structures (tests, runtime-internal types) that do not generate a switch; Logix member names are case-insensitive, hence the comparer.</remarks>
    public bool TryGetMember(string name, out object? member)
    {
        member = GetMember(name);
        if (member is not null) return true;
        var fields = MemberFields.GetOrAdd(GetType(), static t =>
        {
            var d = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (typeof(IPlcMemory).IsAssignableFrom(f.FieldType)) d[f.Name] = f;
            return d;
        });
        if (fields.TryGetValue(name, out var field))
        {
            member = field.GetValue(this);
            return member is not null;
        }
        member = null;
        return false;
    }

    /// <summary>Every member wrapper, structure or array of the generated class, in declaration order.</summary>
    public IEnumerable<(string Name, IPlcMemory Member)> Members()
    {
        foreach (var f in GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (typeof(IPlcMemory).IsAssignableFrom(f.FieldType) && f.GetValue(this) is IPlcMemory m) yield return (f.Name, m);
    }

    /// <summary>The structure's type name (diagnostics only; values are read through the members).</summary>
    public override string ToString() => GetType().Name;
}

/// <summary>
/// A Logix string family value: LEN (DINT) followed by DATA (SINT[capacity]). The predefined STRING has capacity 82;
/// generated user string types derive from this class with their own capacity.
/// </summary>
/// <remarks>
/// Logix strings are not null-terminated: <c>LEN</c> says how many of the <c>DATA</c> bytes are meaningful and the
/// rest are whatever was there before. The structure is padded to a multiple of 4 bytes (88 for the 82-character
/// STRING), and <see cref="LayoutFor"/> reproduces that so the tag server presents <c>LEN</c> and <c>DATA</c> at the
/// offsets a real controller uses. Characters are Latin-1: one byte per character, which is what the SINT array
/// holds. Logic reaches the parts through <see cref="LEN"/> and <see cref="DATA"/> (for <c>Str.LEN</c> and
/// <c>Str.DATA[i]</c>) and the whole text through <see cref="Value"/>.
/// </remarks>
public class WrapSTRING : PlcStruct
{
    /// <summary>Capacity of the predefined STRING type.</summary>
    public const int DefaultCapacity = 82;

    /// <summary>Layout of the predefined STRING type (88 bytes).</summary>
    public static readonly PlcLayout Layout = LayoutFor("STRING", DefaultCapacity);

    /// <summary>The current length member (DINT at offset 0).</summary>
    public readonly WrapDINT LEN;

    /// <summary>The character bytes (SINT[capacity] at offset 4).</summary>
    public readonly PlcArray<WrapSINT> DATA;

    /// <summary>A predefined STRING (capacity 82) at <paramref name="offset"/> of <paramref name="tag"/>.</summary>
    public WrapSTRING(Tag tag, int offset) : this(tag, offset, DefaultCapacity) { }

    /// <summary>A string of the given capacity; generated user string types call this from their own constructor.</summary>
    protected WrapSTRING(Tag tag, int offset, int capacity) : base(tag, offset)
    {
        Capacity = capacity;
        LEN = new WrapDINT(tag, offset);
        DATA = new PlcArray<WrapSINT>(tag, offset + 4, 1, new[] { capacity }, static (t, o) => new WrapSINT(t, o));
    }

    /// <summary>Maximum number of characters the DATA array holds.</summary>
    public int Capacity { get; }

    /// <inheritdoc />
    public override int Size => SizeFor(Capacity);

    /// <summary>Byte size of a string of the given capacity: 4 for LEN plus DATA padded to a multiple of 4.</summary>
    public static int SizeFor(int capacity) => 4 + (capacity + 3) / 4 * 4;

    /// <summary>Builds the layout of a string family type with the given capacity.</summary>
    public static PlcLayout LayoutFor(string name, int capacity) => new()
    {
        Name = name,
        Size = SizeFor(capacity),
        Alignment = 4,
        Members = new[]
        {
            new PlcLayoutMember { Name = "LEN", TypeCode = LogixDataTypes.DINT, Offset = 0, ElementSize = 4 },
            new PlcLayoutMember { Name = "DATA", TypeCode = LogixDataTypes.SINT, Offset = 4, Dims = new[] { capacity }, ElementSize = 1 },
        },
    };

    /// <summary>The text: LEN characters of DATA. Setting clamps to the capacity and updates LEN.</summary>
    /// <remarks>Setting also clears the bytes after the new text, so a snapshot or a client reading the whole structure sees no stale characters; LEN is written last so a concurrent reader never sees a length larger than the bytes already in place.</remarks>
    public string Value
    {
        get
        {
            var len = Math.Clamp(LEN.Value, 0, Capacity);
            return Encoding.Latin1.GetString(Tag.GetData(Offset + 4, len));
        }
        set
        {
            var bytes = Encoding.Latin1.GetBytes(value ?? "");
            var len = Math.Min(bytes.Length, Capacity);
            Tag.SetDataSilent(bytes.AsSpan(0, len), Offset + 4);
            if (len < Capacity) Tag.SetDataSilent(new byte[Capacity - len], Offset + 4 + len);
            LEN.Value = len;
        }
    }

    /// <summary>The current length, clamped to the capacity (LEN written by a client may be out of range).</summary>
    public int Length => Math.Clamp(LEN.Value, 0, Capacity);

    /// <summary>Lets string wrappers be passed where the standard string functions expect text.</summary>
    public static implicit operator string(WrapSTRING s) => s.Value;

    /// <inheritdoc />
    public override string ToString() => Value;
}
