using System.Text.Json.Serialization;

namespace L5x.Model.Values;

/// <summary>The untyped tree produced from a <c>Data Format="L5K"</c> initializer.</summary>
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
