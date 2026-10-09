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
public abstract class PlcStruct : IPlcMemory
{
    private static readonly ConcurrentDictionary<Type, PlcLayout?> Layouts = new();
    private static readonly ConcurrentDictionary<Type, Dictionary<string, FieldInfo>> MemberFields = new();

    protected PlcStruct(Tag tag, int offset)
    {
        Tag = tag;
        Offset = offset;
    }

    public Tag Tag { get; }
    public int Offset { get; }

    /// <summary>Byte size of the structure, from the generated static <c>Layout</c> field.</summary>
    public virtual int Size => LayoutOf(GetType())?.Size ?? 0;

    public int ByteSize => Size;

    /// <summary>The layout declared by the concrete class, or null for hand-written structures without one.</summary>
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

    public override string ToString() => GetType().Name;
}

/// <summary>
/// A Logix string family value: LEN (DINT) followed by DATA (SINT[capacity]). The predefined STRING has capacity 82;
/// generated user string types derive from this class with their own capacity.
/// </summary>
public class WrapSTRING : PlcStruct
{
    public const int DefaultCapacity = 82;

    /// <summary>Layout of the predefined STRING type (88 bytes).</summary>
    public static readonly PlcLayout Layout = LayoutFor("STRING", DefaultCapacity);

    public readonly WrapDINT LEN;
    public readonly PlcArray<WrapSINT> DATA;

    public WrapSTRING(Tag tag, int offset) : this(tag, offset, DefaultCapacity) { }

    protected WrapSTRING(Tag tag, int offset, int capacity) : base(tag, offset)
    {
        Capacity = capacity;
        LEN = new WrapDINT(tag, offset);
        DATA = new PlcArray<WrapSINT>(tag, offset + 4, 1, new[] { capacity }, static (t, o) => new WrapSINT(t, o));
    }

    public int Capacity { get; }
    public override int Size => SizeFor(Capacity);

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

    public int Length => Math.Clamp(LEN.Value, 0, Capacity);

    public static implicit operator string(WrapSTRING s) => s.Value;
    public override string ToString() => Value;
}
