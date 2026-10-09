using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.L5X.Model.Values;

/// <summary>
/// The untyped tree produced from a <c>Data Format="L5K"</c> initializer: Logix's older, type-agnostic value
/// syntax (nested brace lists of numbers and strings), still used for some tag defaults and module config
/// alongside the newer typed "Decorated" format. Because L5K carries no type information of its own, this tree
/// is shaped against the tag's actual data type afterward by <c>L5kShaper</c> into a typed <see cref="TagValue"/>;
/// kept separately rather than shaped during parsing since the data type may not be resolved yet.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(L5kInteger), "int")]
[JsonDerivedType(typeof(L5kReal), "real")]
[JsonDerivedType(typeof(L5kString), "string")]
[JsonDerivedType(typeof(L5kList), "list")]
[JsonDerivedType(typeof(L5kRaw), "raw")]
public abstract record L5kValue;

/// <summary>An integer. RadixText keeps the original prefix ("16#", "2#", "8#") when one was used, otherwise null.</summary>
public sealed record L5kInteger(long Value, string? RadixText = null) : L5kValue;

public sealed record L5kReal(double Value) : L5kValue;

/// <summary>A string with '$' escapes decoded.</summary>
public sealed record L5kString(string Value) : L5kValue;

public sealed record L5kList(IReadOnlyList<L5kValue> Items) : L5kValue
{
    public static readonly L5kList Empty = new(Array.Empty<L5kValue>());
}

/// <summary>A token the parser could not interpret.</summary>
public sealed record L5kRaw(string Text) : L5kValue;
