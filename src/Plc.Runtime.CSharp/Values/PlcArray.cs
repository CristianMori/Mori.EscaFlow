using System.Collections;
using EthernetIPSharp.Logix;

namespace Plc.Runtime;

/// <summary>Common shape of arrays living in tag memory.</summary>
public abstract class PlcArrayBase : IPlcMemory
{
    protected PlcArrayBase(Tag tag, int offset, int[] dims)
    {
        Tag = tag;
        Offset = offset;
        Dims = dims;
        var n = 1;
        foreach (var d in dims) n *= d;
        Length = n;
    }

    public Tag Tag { get; }
    public int Offset { get; }
    public int[] Dims { get; }
    public int Rank => Dims.Length;
    public int Length { get; }
    public abstract int ElementSize { get; }
    public abstract int ByteSize { get; }

    /// <summary>Row-major flat index of a multi-dimensional subscript.</summary>
    public int FlatIndex(int i, int j)
    {
        CheckRank(2);
        Check(i, 0); Check(j, 1);
        return i * Dims[1] + j;
    }

    public int FlatIndex(int i, int j, int k)
    {
        CheckRank(3);
        Check(i, 0); Check(j, 1); Check(k, 2);
        return (i * Dims[1] + j) * Dims[2] + k;
    }

    protected void Check(int index, int dim)
    {
        if ((uint)index >= (uint)Dims[dim]) throw new IndexOutOfRangeException($"Index {index} is outside dimension {dim} of size {Dims[dim]}");
    }

    protected void CheckRank(int rank)
    {
        if (Dims.Length != rank) throw new InvalidOperationException($"Array has rank {Dims.Length}, subscript has {rank}");
    }
}

/// <summary>An array of wrappers, structures or nested arrays laid out contiguously (row-major) in tag memory.</summary>
public sealed class PlcArray<T> : PlcArrayBase, IReadOnlyList<T> where T : class, IPlcMemory
{
    private readonly T[] _elements;

    public PlcArray(Tag tag, int offset, int elementSize, int[] dims, Func<Tag, int, T> factory) : base(tag, offset, dims)
    {
        ElementSize = elementSize;
        _elements = new T[Length];
        for (var i = 0; i < Length; i++) _elements[i] = factory(tag, offset + i * elementSize);
    }

    public PlcArray(Tag tag, int offset, int elementSize, int count, Func<Tag, int, T> factory) : this(tag, offset, elementSize, new[] { count }, factory) { }

    public override int ElementSize { get; }
    public override int ByteSize => ElementSize * Length;

    public T this[int i]
    {
        get
        {
            if (Dims.Length == 1) Check(i, 0);
            return _elements[i];
        }
    }

    public T this[int i, int j] => _elements[FlatIndex(i, j)];
    public T this[int i, int j, int k] => _elements[FlatIndex(i, j, k)];

    public IReadOnlyList<T> Elements => _elements;
    public int Count => Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_elements).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _elements.GetEnumerator();
}

/// <summary>A DWORD-packed BOOL array: element i is bit i of DINT i/32.</summary>
public sealed class PlcBoolArray : PlcArrayBase, IReadOnlyList<WrapBOOL>
{
    private readonly WrapBOOL[] _elements;

    public PlcBoolArray(Tag tag, int offset, int[] dims) : base(tag, offset, dims)
    {
        _elements = new WrapBOOL[Length];
        for (var i = 0; i < Length; i++) _elements[i] = new WrapBOOL(tag, offset + i / 8, i % 8);
    }

    public PlcBoolArray(Tag tag, int offset, int count) : this(tag, offset, new[] { count }) { }

    /// <summary>Four bytes per 32 elements.</summary>
    public override int ElementSize => 4;
    public override int ByteSize => (Length + 31) / 32 * 4;

    public WrapBOOL this[int i]
    {
        get
        {
            if (Dims.Length == 1) Check(i, 0);
            return _elements[i];
        }
    }

    public WrapBOOL this[int i, int j] => _elements[FlatIndex(i, j)];
    public WrapBOOL this[int i, int j, int k] => _elements[FlatIndex(i, j, k)];

    public int Count => Length;
    public IEnumerator<WrapBOOL> GetEnumerator() => ((IEnumerable<WrapBOOL>)_elements).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _elements.GetEnumerator();
}
