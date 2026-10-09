using System.Collections;
using EthernetIPSharp.Logix;

namespace Mori.EscaFlow.Runtime;

/// <summary>
/// Common shape of arrays living in tag memory.
/// </summary>
/// <remarks>
/// Logix arrays are contiguous and row-major with up to three dimensions; element <c>[i,j,k]</c> sits at flat index
/// <c>(i * D1 + j) * D2 + k</c>. The subscript checks throw <see cref="IndexOutOfRangeException"/>, which the task
/// turns into a major fault, mirroring the controller's "array subscript out of range" fault (type 4, code 20).
/// </remarks>
public abstract class PlcArrayBase : IPlcMemory
{
    /// <summary>Binds the array to <paramref name="tag"/> at byte <paramref name="offset"/> with the given dimensions (outermost first).</summary>
    protected PlcArrayBase(Tag tag, int offset, int[] dims)
    {
        Tag = tag;
        Offset = offset;
        Dims = dims;
        var n = 1;
        foreach (var d in dims) n *= d;
        Length = n;
    }

    /// <inheritdoc />
    public Tag Tag { get; }

    /// <inheritdoc />
    public int Offset { get; }

    /// <summary>Element count per dimension, outermost first.</summary>
    public int[] Dims { get; }

    /// <summary>Number of dimensions (1 to 3 in Logix).</summary>
    public int Rank => Dims.Length;

    /// <summary>Total number of elements across all dimensions.</summary>
    public int Length { get; }

    /// <summary>Bytes per element (for packed BOOL arrays, the 4 bytes that hold 32 elements).</summary>
    public abstract int ElementSize { get; }

    /// <inheritdoc />
    public abstract int ByteSize { get; }

    /// <summary>Row-major flat index of a two-dimensional subscript, bounds-checked per dimension.</summary>
    public int FlatIndex(int i, int j)
    {
        CheckRank(2);
        Check(i, 0); Check(j, 1);
        return i * Dims[1] + j;
    }

    /// <summary>Row-major flat index of a three-dimensional subscript, bounds-checked per dimension.</summary>
    public int FlatIndex(int i, int j, int k)
    {
        CheckRank(3);
        Check(i, 0); Check(j, 1); Check(k, 2);
        return (i * Dims[1] + j) * Dims[2] + k;
    }

    /// <summary>Throws when <paramref name="index"/> is outside dimension <paramref name="dim"/>; the unsigned compare rejects negatives in one test.</summary>
    protected void Check(int index, int dim)
    {
        if ((uint)index >= (uint)Dims[dim]) throw new IndexOutOfRangeException($"Index {index} is outside dimension {dim} of size {Dims[dim]}");
    }

    /// <summary>Throws when a subscript has a different number of indices than the array has dimensions.</summary>
    protected void CheckRank(int rank)
    {
        if (Dims.Length != rank) throw new InvalidOperationException($"Array has rank {Dims.Length}, subscript has {rank}");
    }
}

/// <summary>
/// An array of wrappers, structures or nested arrays laid out contiguously (row-major) in tag memory.
/// </summary>
/// <typeparam name="T">The element view type (an atomic wrapper, a generated structure class or another array).</typeparam>
/// <remarks>
/// Element views are created eagerly in the constructor: one object per element, each bound at
/// <c>offset + i * elementSize</c>. Eager construction costs memory for large arrays but keeps element access
/// allocation-free during the scan, which matters more. The one-dimensional indexer deliberately accepts a flat
/// index on multi-dimensional arrays, because Logix file instructions (COP, FLL, FAL) and the host walk arrays
/// flat.
/// </remarks>
public sealed class PlcArray<T> : PlcArrayBase, IReadOnlyList<T> where T : class, IPlcMemory
{
    private readonly T[] _elements;

    /// <summary>Builds the element views with <paramref name="factory"/>, each at its row-major byte offset.</summary>
    /// <param name="elementSize">Bytes per element; nested structures use their padded size so elements stay aligned.</param>
    public PlcArray(Tag tag, int offset, int elementSize, int[] dims, Func<Tag, int, T> factory) : base(tag, offset, dims)
    {
        ElementSize = elementSize;
        _elements = new T[Length];
        for (var i = 0; i < Length; i++) _elements[i] = factory(tag, offset + i * elementSize);
    }

    /// <summary>One-dimensional convenience overload.</summary>
    public PlcArray(Tag tag, int offset, int elementSize, int count, Func<Tag, int, T> factory) : this(tag, offset, elementSize, new[] { count }, factory) { }

    /// <inheritdoc />
    public override int ElementSize { get; }

    /// <inheritdoc />
    public override int ByteSize => ElementSize * Length;

    /// <summary>Element by one-dimensional subscript; on multi-dimensional arrays this is the flat row-major index.</summary>
    public T this[int i]
    {
        get
        {
            if (Dims.Length == 1) Check(i, 0);
            return _elements[i];
        }
    }

    /// <summary>Element by two-dimensional subscript.</summary>
    public T this[int i, int j] => _elements[FlatIndex(i, j)];

    /// <summary>Element by three-dimensional subscript.</summary>
    public T this[int i, int j, int k] => _elements[FlatIndex(i, j, k)];

    /// <summary>All element views in row-major order.</summary>
    public IReadOnlyList<T> Elements => _elements;

    /// <inheritdoc />
    public int Count => Length;

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_elements).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _elements.GetEnumerator();
}

/// <summary>
/// A DWORD-packed BOOL array: element i is bit i of DINT i/32.
/// </summary>
/// <remarks>
/// Logix stores <c>BOOL[n]</c> arrays (n a multiple of 32) as consecutive DINTs with 32 bits each, and clients read
/// <c>Flags[5]</c> as bit 5 of the first DINT. The byte image here is the same: element i is bit <c>i % 8</c> of
/// byte <c>i / 8</c>, which in little-endian order is exactly bit <c>i % 32</c> of DINT <c>i / 32</c>.
/// </remarks>
public sealed class PlcBoolArray : PlcArrayBase, IReadOnlyList<WrapBOOL>
{
    private readonly WrapBOOL[] _elements;

    /// <summary>Builds one BOOL view per element over the packed DINTs.</summary>
    public PlcBoolArray(Tag tag, int offset, int[] dims) : base(tag, offset, dims)
    {
        _elements = new WrapBOOL[Length];
        for (var i = 0; i < Length; i++) _elements[i] = new WrapBOOL(tag, offset + i / 8, i % 8);
    }

    /// <summary>One-dimensional convenience overload.</summary>
    public PlcBoolArray(Tag tag, int offset, int count) : this(tag, offset, new[] { count }) { }

    /// <summary>Four bytes per 32 elements.</summary>
    public override int ElementSize => 4;

    /// <summary>Whole DINTs: the element count rounded up to a multiple of 32, times 4 bytes.</summary>
    public override int ByteSize => (Length + 31) / 32 * 4;

    /// <summary>Element by one-dimensional (or flat) subscript.</summary>
    public WrapBOOL this[int i]
    {
        get
        {
            if (Dims.Length == 1) Check(i, 0);
            return _elements[i];
        }
    }

    /// <summary>Element by two-dimensional subscript.</summary>
    public WrapBOOL this[int i, int j] => _elements[FlatIndex(i, j)];

    /// <summary>Element by three-dimensional subscript.</summary>
    public WrapBOOL this[int i, int j, int k] => _elements[FlatIndex(i, j, k)];

    /// <inheritdoc />
    public int Count => Length;

    /// <inheritdoc />
    public IEnumerator<WrapBOOL> GetEnumerator() => ((IEnumerable<WrapBOOL>)_elements).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _elements.GetEnumerator();
}
