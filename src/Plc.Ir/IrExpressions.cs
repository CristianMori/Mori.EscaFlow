using System.Globalization;
using System.Text.Json.Serialization;

namespace Plc.Ir;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(IrRef), "ref")]
[JsonDerivedType(typeof(IrLiteral), "lit")]
[JsonDerivedType(typeof(IrUnary), "un")]
[JsonDerivedType(typeof(IrBinary), "bin")]
[JsonDerivedType(typeof(IrFunction), "fn")]
[JsonDerivedType(typeof(IrConv), "conv")]
[JsonDerivedType(typeof(IrBitAccess), "bit")]
[JsonDerivedType(typeof(IrEdge), "edge")]
[JsonDerivedType(typeof(IrSysFn), "sysFn")]
[JsonDerivedType(typeof(IrKeyword), "keyword")]
[JsonDerivedType(typeof(IrPlaceholder), "placeholder")]
[JsonDerivedType(typeof(IrErrorExpression), "error")]
public abstract class IrExpression : IrNode
{
    /// <summary>Result type when known. Conditions are BOOL.</summary>
    public IrTypeRef? Type { get; set; }

    [JsonIgnore]
    public bool IsBool => Type is { } t && t.Is("BOOL");
}

/// <summary>A reference to a variable, a temporary, a system value or an unresolved name, optionally narrowed by a path of members, indices, string length/characters and dereferences.</summary>
public sealed class IrRef : IrExpression
{
    public IrRefRoot Root { get; set; } = new();
    public List<IrPathSegment> Path { get; set; } = new();

    /// <summary>Id of the alias variable this reference was written through, when the source used an alias (the root already names the target).</summary>
    [IrRefKind("variable")]
    public string? Alias { get; set; }

    /// <summary>True when the reference reads or writes an I/O channel image.</summary>
    public bool Io { get; set; }

    /// <summary>True when the reference enters a type whose layout is unknown; <see cref="IrExpression.Type"/> is then null or a guess.</summary>
    public bool Opaque { get; set; }
    public string? Error { get; set; }

    public static IrRef Variable(string id, IrTypeRef? type = null) => new() { Root = IrRefRoot.Variable(id), Type = type };
    public static IrRef Temp(string name, IrTypeRef? type = null) => new() { Root = IrRefRoot.Temp(name), Type = type ?? IrTypeRef.Named("BOOL") };

    [JsonIgnore]
    public bool IsResolved => Root.Kind is "var" or "temp" or "system" or "this" && Error is null;

    public IrRef Member(string name)
    {
        Path.Add(new IrMemberSegment { Name = name });
        return this;
    }

    public IrRef Index(params IrExpression[] indices)
    {
        Path.Add(new IrIndexSegment { Indices = indices.ToList() });
        return this;
    }
}

public sealed class IrRefRoot
{
    /// <summary>var: a declared variable (<see cref="Id"/>); temp: a body temporary (<see cref="Name"/>); system: a runtime value (<see cref="Name"/>, vendor-defined, e.g. Logix S:FS); this: the enclosing function block instance; unresolved: a name the front-end could not bind.</summary>
    [IrEnum("var", "temp", "system", "this", "unresolved")]
    public string Kind { get; set; } = "var";

    [IrRefKind("variable")]
    public string? Id { get; set; }
    public string? Name { get; set; }

    public static IrRefRoot Variable(string id) => new() { Kind = "var", Id = id };
    public static IrRefRoot Temp(string name) => new() { Kind = "temp", Name = name };
    public static IrRefRoot System(string name) => new() { Kind = "system", Name = name };
    public static IrRefRoot This => new() { Kind = "this" };
    public static IrRefRoot Unresolved(string name) => new() { Kind = "unresolved", Name = name };

    public override string ToString() => Kind switch { "var" => Id ?? "?", "this" => "THIS", _ => Name ?? "?" };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrMemberSegment), "member")]
[JsonDerivedType(typeof(IrIndexSegment), "index")]
[JsonDerivedType(typeof(IrLengthSegment), "length")]
[JsonDerivedType(typeof(IrCharSegment), "char")]
[JsonDerivedType(typeof(IrDerefSegment), "deref")]
public abstract class IrPathSegment
{
}

public sealed class IrMemberSegment : IrPathSegment
{
    public string Name { get; set; } = "";
}

public sealed class IrIndexSegment : IrPathSegment
{
    public List<IrExpression> Indices { get; set; } = new();
}

/// <summary>The current length of a string value.</summary>
public sealed class IrLengthSegment : IrPathSegment
{
}

/// <summary>One character of a string value (0-based).</summary>
public sealed class IrCharSegment : IrPathSegment
{
    public IrExpression Index { get; set; } = IrLiteral.OfInt(0);
}

/// <summary>Dereference of a pointer or reference.</summary>
public sealed class IrDerefSegment : IrPathSegment
{
}

public sealed class IrLiteral : IrExpression
{
    [IrEnum("int", "real", "bool", "string", "wstring", "time", "date", "tod", "dt", "enum")]
    public string Kind { get; set; } = "int";
    public long? Int { get; set; }
    public double? Real { get; set; }
    public string? String { get; set; }
    public bool? Bool { get; set; }

    /// <summary>Radix the integer was written in (2, 8, 16) when not decimal.</summary>
    public int? Radix { get; set; }

    /// <summary>Enumeration value name for <c>enum</c>; the value as written for time and date kinds (<c>T#1s500ms</c>) alongside <see cref="Int"/> in nanoseconds / days.</summary>
    public string? Text { get; set; }

    public static IrLiteral True => new() { Kind = "bool", Bool = true, Type = IrTypeRef.Named("BOOL") };
    public static IrLiteral False => new() { Kind = "bool", Bool = false, Type = IrTypeRef.Named("BOOL") };
    public static IrLiteral OfInt(long value, string type = "DINT") => new() { Kind = "int", Int = value, Type = IrTypeRef.Named(type) };
    public static IrLiteral OfReal(double value, string type = "REAL") => new() { Kind = "real", Real = value, Type = IrTypeRef.Named(type) };
    public static IrLiteral Str(string value) => new() { Kind = "string", String = value, Type = IrTypeRef.String(value.Length) };

    [JsonIgnore]
    public bool IsTrue => Kind == "bool" && Bool == true;

    [JsonIgnore]
    public bool IsFalse => Kind == "bool" && Bool == false;

    public override string ToString() => Kind switch
    {
        "bool" => Bool == true ? "TRUE" : "FALSE",
        "int" => Int?.ToString(CultureInfo.InvariantCulture) ?? "0",
        "real" => Real?.ToString("R", CultureInfo.InvariantCulture) ?? "0.0",
        "string" or "wstring" => "'" + String + "'",
        _ => Text ?? "",
    };
}

public sealed class IrUnary : IrExpression
{
    [IrEnum("neg", "not")]
    public string Operator { get; set; } = "not";
    public IrExpression Operand { get; set; } = IrLiteral.False;
}

public sealed class IrBinary : IrExpression
{
    [IrEnum("add", "sub", "mul", "div", "mod", "pow", "eq", "ne", "lt", "gt", "le", "ge", "and", "or", "xor")]
    public string Operator { get; set; } = "and";
    public IrExpression Left { get; set; } = IrLiteral.False;
    public IrExpression Right { get; set; } = IrLiteral.False;

    [JsonIgnore]
    public bool IsComparison => Operator is "eq" or "ne" or "lt" or "gt" or "le" or "ge";

    [JsonIgnore]
    public bool IsLogical => Operator is "and" or "or" or "xor";
}

/// <summary>A standard function (<see cref="Catalogs.StandardFunctions"/>) or a project function (<see cref="Pou"/>) used in an expression.</summary>
public sealed class IrFunction : IrExpression
{
    public string? Name { get; set; }

    [IrRefKind("pou")]
    public string? Pou { get; set; }
    public List<IrExpression?> Args { get; set; } = new();
}

/// <summary>Explicit type conversion. Front-ends insert it wherever the source language converts implicitly with vendor-specific rules.</summary>
public sealed class IrConv : IrExpression
{
    public IrTypeRef To { get; set; } = IrTypeRef.Named("DINT");

    /// <summary>wrap: truncate to the destination width; saturate: clamp; roundEven: round half to even (Logix REAL to integer); roundHalfUp; trunc: toward zero; checked: raise on overflow.</summary>
    [IrEnum("wrap", "saturate", "roundEven", "roundHalfUp", "trunc", "checked")]
    public string Mode { get; set; } = "wrap";
    public IrExpression Operand { get; set; } = IrLiteral.OfInt(0);
}

/// <summary>One bit of an integer or bit-string value. Usable as an assignment target.</summary>
public sealed class IrBitAccess : IrExpression
{
    public IrExpression Of { get; set; } = new IrRef();
    public IrExpression Bit { get; set; } = IrLiteral.OfInt(0);
}

/// <summary>Rising or falling edge of <see cref="Clk"/>, using <see cref="Storage"/> (a BOOL variable) as the previous-scan memory.</summary>
public sealed class IrEdge : IrExpression
{
    [IrEnum("rising", "falling")]
    public string Kind { get; set; } = "rising";
    public IrExpression Clk { get; set; } = IrLiteral.False;
    public IrExpression Storage { get; set; } = new IrRef();
}

/// <summary>A vendor runtime function used inside an expression. <see cref="Catalog"/> names the vendor service catalog (logix, s7, twincat).</summary>
public sealed class IrSysFn : IrExpression
{
    public string Catalog { get; set; } = "";
    public string Service { get; set; } = "";
    public List<IrExpression?> Args { get; set; } = new();
}

/// <summary>An enumeration-like operand (vendor keyword), a jump label or a name (routine, class, attribute) used as an argument.</summary>
public sealed class IrKeyword : IrExpression
{
    public string Text { get; set; } = "";

    [IrEnum("enum", "label", "action", "name")]
    public string Kind { get; set; } = "enum";
}

/// <summary>An unsupplied operand (Logix <c>?</c>) or an elided argument.</summary>
public sealed class IrPlaceholder : IrExpression
{
    public bool Empty { get; set; }
}

public sealed class IrErrorExpression : IrExpression
{
    public string Text { get; set; } = "";
}
