using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

/// <summary>
/// Hands out shared <see cref="ScalarValue"/> instances for the most common values (BOOL 0/1, small integers, REAL 0.0)
/// so that a project with a million decorated data nodes does not allocate a record for each.
/// Thread-safe: the tables are populated eagerly and never mutated afterwards.
/// </summary>
public sealed class ValueInterner
{
    public const int MinSmall = -16;
    public const int MaxSmall = 255;

    private static readonly AtomicKind[] IntegerKinds =
    {
        AtomicKind.Sint, AtomicKind.Int, AtomicKind.Dint, AtomicKind.Lint,
        AtomicKind.Usint, AtomicKind.Uint, AtomicKind.Udint, AtomicKind.Ulint,
    };

    private readonly ScalarValue _boolFalse = new(AtomicKind.Bool, 0, 0, Radix.Decimal);
    private readonly ScalarValue _boolTrue = new(AtomicKind.Bool, 1, 0, Radix.Decimal);
    private readonly ScalarValue _bitFalse = new(AtomicKind.Bit, 0, 0, Radix.Decimal);
    private readonly ScalarValue _bitTrue = new(AtomicKind.Bit, 1, 0, Radix.Decimal);
    private readonly ScalarValue _realZero = new(AtomicKind.Real, 0, 0.0, Radix.Float);
    private readonly ScalarValue _lrealZero = new(AtomicKind.Lreal, 0, 0.0, Radix.Float);
    private readonly ScalarValue[][] _smallDecimal;

    public static readonly ValueInterner Shared = new();

    public ValueInterner()
    {
        _smallDecimal = new ScalarValue[IntegerKinds.Length][];
        for (var k = 0; k < IntegerKinds.Length; k++)
        {
            var arr = new ScalarValue[MaxSmall - MinSmall + 1];
            for (var v = MinSmall; v <= MaxSmall; v++)
            {
                arr[v - MinSmall] = new ScalarValue(IntegerKinds[k], v, 0, Radix.Decimal);
            }
            _smallDecimal[k] = arr;
        }
    }

    public long Hits { get; private set; }

    /// <summary>Returns a shared instance when one exists for the value; otherwise allocates.</summary>
    public ScalarValue Integer(AtomicKind kind, long value, Radix radix)
    {
        if (kind == AtomicKind.Bool)
        {
            return value != 0 ? _boolTrue : _boolFalse;
        }
        if (kind == AtomicKind.Bit)
        {
            return value != 0 ? _bitTrue : _bitFalse;
        }
        if (radix == Radix.Decimal && value >= MinSmall && value <= MaxSmall)
        {
            var k = IndexOfKind(kind);
            if (k >= 0)
            {
                return _smallDecimal[k][value - MinSmall];
            }
        }
        return new ScalarValue(kind, value, 0, radix);
    }

    public ScalarValue Real(AtomicKind kind, double value, Radix radix)
    {
        if (value == 0.0 && !double.IsNegative(value) && radix == Radix.Float)
        {
            return kind == AtomicKind.Lreal ? _lrealZero : _realZero;
        }
        return new ScalarValue(kind, 0, value, radix);
    }

    private static int IndexOfKind(AtomicKind kind) => kind switch
    {
        AtomicKind.Sint => 0,
        AtomicKind.Int => 1,
        AtomicKind.Dint => 2,
        AtomicKind.Lint => 3,
        AtomicKind.Usint => 4,
        AtomicKind.Uint => 5,
        AtomicKind.Udint => 6,
        AtomicKind.Ulint => 7,
        _ => -1,
    };
}
