using System.Globalization;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler;

/// <summary>Base of every expression; polymorphic on <c>op</c>.</summary>
/// <remarks>
/// Every expression carries its result type when the front-end could determine it, so a converter never has to
/// re-run type inference: comparisons are BOOL, arithmetic takes the wider operand type, references carry the
/// leaf type of the addressed variable or member. A null type marks an expression that reaches into an opaque
/// type or that the front-end could not interpret; converters treat those as unsupported rather than guess.
/// </remarks>
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

    /// <summary>True when the result type is BOOL.</summary>
    [JsonIgnore]
    public bool IsBool => Type is { } t && t.Is("BOOL");
}

/// <summary>A reference to a variable, a temporary, a system value or an unresolved name, optionally narrowed by a path of members, indices, string length/characters and dereferences.</summary>
/// <remarks>
/// <para>
/// A reference is a root plus a path, never a text to be parsed: the root identifies the declared variable by
/// id (scope already resolved, aliases already expanded), the path walks into it with typed segments. A
/// converter emits the root as its own access to the variable and each segment as a member access, an index
/// or a string operation; it needs no name lookup and no knowledge of the source syntax.
/// </para>
/// <para>
/// Bit access (<c>Word.3</c>, <c>Word.[i]</c>) is not a path segment but a separate <c>bit</c> expression
/// wrapping the reference, because a bit is not a sub-object of the integer: it has no address of its own, it
/// must be read with a mask and written with a read-modify-write, and most target languages represent it with
/// a different construct than member access. Keeping it out of the path keeps the path walk purely structural.
/// </para>
/// </remarks>
public sealed class IrRef : IrExpression
{
    /// <summary>What the reference starts from.</summary>
    public IrRefRoot Root { get; set; } = new();

    /// <summary>Segments narrowing the root, in order.</summary>
    public List<IrPathSegment> Path { get; set; } = new();

    /// <summary>Id of the alias variable this reference was written through, when the source used an alias (the root already names the target).</summary>
    [IrRefKind("variable")]
    public string? Alias { get; set; }

    /// <summary>True when the reference reads or writes an I/O channel image.</summary>
    public bool Io { get; set; }

    /// <summary>True when the reference enters a type whose layout is unknown; <see cref="IrExpression.Type"/> is then null or a guess.</summary>
    public bool Opaque { get; set; }

    /// <summary>Why the front-end could not resolve the reference, when it could not.</summary>
    public string? Error { get; set; }

    /// <summary>A reference to a declared variable.</summary>
    public static IrRef Variable(string id, IrTypeRef? type = null) => new() { Root = IrRefRoot.Variable(id), Type = type };

    /// <summary>A reference to a body temporary (BOOL unless stated).</summary>
    public static IrRef Temp(string name, IrTypeRef? type = null) => new() { Root = IrRefRoot.Temp(name), Type = type ?? IrTypeRef.Named("BOOL") };

    /// <summary>True when the root is bound and no error was recorded.</summary>
    [JsonIgnore]
    public bool IsResolved => Root.Kind is "var" or "temp" or "system" or "this" && Error is null;

    /// <summary>Appends a member segment (builder helper).</summary>
    public IrRef Member(string name)
    {
        Path.Add(new IrMemberSegment { Name = name });
        return this;
    }

    /// <summary>Appends an index segment (builder helper).</summary>
    public IrRef Index(params IrExpression[] indices)
    {
        Path.Add(new IrIndexSegment { Indices = indices.ToList() });
        return this;
    }
}

/// <summary>The starting point of a reference.</summary>
public sealed class IrRefRoot
{
    /// <summary>var: a declared variable (<see cref="Id"/>); temp: a body temporary (<see cref="Name"/>); system: a runtime value (<see cref="Name"/>, vendor-defined, e.g. Logix S:FS); this: the enclosing function block instance; unresolved: a name the front-end could not bind.</summary>
    [IrEnum("var", "temp", "system", "this", "unresolved")]
    public string Kind { get; set; } = "var";

    /// <summary>Variable id for kind <c>var</c>.</summary>
    [IrRefKind("variable")]
    public string? Id { get; set; }

    /// <summary>Temporary name, system value name or unresolved name for the other kinds.</summary>
    public string? Name { get; set; }

    /// <summary>Root at a declared variable.</summary>
    public static IrRefRoot Variable(string id) => new() { Kind = "var", Id = id };

    /// <summary>Root at a body temporary.</summary>
    public static IrRefRoot Temp(string name) => new() { Kind = "temp", Name = name };

    /// <summary>Root at a vendor system value (Logix <c>S:FS</c>, the first-scan bit).</summary>
    public static IrRefRoot System(string name) => new() { Kind = "system", Name = name };

    /// <summary>Root at the enclosing function block instance.</summary>
    public static IrRefRoot This => new() { Kind = "this" };

    /// <summary>Root at a name that could not be bound; the document stays loadable and the validator reports it.</summary>
    public static IrRefRoot Unresolved(string name) => new() { Kind = "unresolved", Name = name };

    /// <summary>Id or name, for messages.</summary>
    public override string ToString() => Kind switch { "var" => Id ?? "?", "this" => "THIS", _ => Name ?? "?" };
}

/// <summary>One step of a reference path; polymorphic on <c>kind</c>.</summary>
/// <remarks>Member and index are the structural steps; length and char give string-family values a vendor-neutral face (Logix <c>Str.LEN</c> and <c>Str.DATA[i]</c>, Siemens S7 string headers) so converters can map them onto their own string type; deref serves pointer types of object-oriented sources.</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IrMemberSegment), "member")]
[JsonDerivedType(typeof(IrIndexSegment), "index")]
[JsonDerivedType(typeof(IrLengthSegment), "length")]
[JsonDerivedType(typeof(IrCharSegment), "char")]
[JsonDerivedType(typeof(IrDerefSegment), "deref")]
public abstract class IrPathSegment
{
}

/// <summary>A structure member by name.</summary>
public sealed class IrMemberSegment : IrPathSegment
{
    /// <summary>Member name as declared.</summary>
    public string Name { get; set; } = "";
}

/// <summary>An array element; one index expression per dimension.</summary>
public sealed class IrIndexSegment : IrPathSegment
{
    /// <summary>Index expressions, outermost dimension first.</summary>
    public List<IrExpression> Indices { get; set; } = new();
}

/// <summary>The current length of a string value.</summary>
public sealed class IrLengthSegment : IrPathSegment
{
}

/// <summary>One character of a string value (0-based).</summary>
public sealed class IrCharSegment : IrPathSegment
{
    /// <summary>Character position.</summary>
    public IrExpression Index { get; set; } = IrLiteral.OfInt(0);
}

/// <summary>Dereference of a pointer or reference.</summary>
public sealed class IrDerefSegment : IrPathSegment
{
}

/// <summary>A constant.</summary>
/// <remarks>The radix an integer was written in is kept so converters can reproduce <c>16#00FF</c> style literals and so documentation shows what the author wrote; it has no semantic effect.</remarks>
public sealed class IrLiteral : IrExpression
{
    /// <summary>Which of the value members applies.</summary>
    [IrEnum("int", "real", "bool", "string", "wstring", "time", "date", "tod", "dt", "enum")]
    public string Kind { get; set; } = "int";

    /// <summary>Integer value; for time kinds the magnitude in the unit described by <see cref="Text"/>.</summary>
    public long? Int { get; set; }

    /// <summary>Floating-point value.</summary>
    public double? Real { get; set; }

    /// <summary>String value (unescaped).</summary>
    public string? String { get; set; }

    /// <summary>Boolean value.</summary>
    public bool? Bool { get; set; }

    /// <summary>Radix the integer was written in (2, 8, 16) when not decimal.</summary>
    public int? Radix { get; set; }

    /// <summary>Enumeration value name for <c>enum</c>; the value as written for time and date kinds (<c>T#1s500ms</c>) alongside <see cref="Int"/> in nanoseconds / days.</summary>
    public string? Text { get; set; }

    /// <summary>The BOOL literal TRUE (a fresh instance each time, since nodes are mutable).</summary>
    public static IrLiteral True => new() { Kind = "bool", Bool = true, Type = IrTypeRef.Named("BOOL") };

    /// <summary>The BOOL literal FALSE.</summary>
    public static IrLiteral False => new() { Kind = "bool", Bool = false, Type = IrTypeRef.Named("BOOL") };

    /// <summary>An integer literal typed DINT unless stated.</summary>
    public static IrLiteral OfInt(long value, string type = "DINT") => new() { Kind = "int", Int = value, Type = IrTypeRef.Named(type) };

    /// <summary>A floating-point literal typed REAL unless stated.</summary>
    public static IrLiteral OfReal(double value, string type = "REAL") => new() { Kind = "real", Real = value, Type = IrTypeRef.Named(type) };

    /// <summary>A string literal typed as an anonymous string of its own length.</summary>
    public static IrLiteral Str(string value) => new() { Kind = "string", String = value, Type = IrTypeRef.String(value.Length) };

    /// <summary>True for the literal TRUE.</summary>
    [JsonIgnore]
    public bool IsTrue => Kind == "bool" && Bool == true;

    /// <summary>True for the literal FALSE.</summary>
    [JsonIgnore]
    public bool IsFalse => Kind == "bool" && Bool == false;

    /// <summary>IEC spelling, for messages.</summary>
    public override string ToString() => Kind switch
    {
        "bool" => Bool == true ? "TRUE" : "FALSE",
        "int" => Int?.ToString(CultureInfo.InvariantCulture) ?? "0",
        "real" => Real?.ToString("R", CultureInfo.InvariantCulture) ?? "0.0",
        "string" or "wstring" => "'" + String + "'",
        _ => Text ?? "",
    };
}

/// <summary>Unary operator.</summary>
public sealed class IrUnary : IrExpression
{
    /// <summary>neg: arithmetic negation; not: logical NOT on BOOL, bitwise complement on integers (IEC NOT is both).</summary>
    [IrEnum("neg", "not")]
    public string Operator { get; set; } = "not";

    /// <summary>The operand.</summary>
    public IrExpression Operand { get; set; } = IrLiteral.False;
}

/// <summary>Binary operator.</summary>
/// <remarks>
/// <c>and</c>, <c>or</c> and <c>xor</c> are logical on BOOL operands and bitwise on integers, as in IEC and
/// Logix (the AND instruction on DINTs is bitwise); the result type says which. <c>div</c> and <c>mod</c> on
/// integers are integer operations, and a converter must apply the vendor's zero-divisor rule (Logix produces a
/// saturated result and a minor fault rather than stopping).
/// </remarks>
public sealed class IrBinary : IrExpression
{
    /// <summary>add sub mul div mod pow eq ne lt gt le ge and or xor</summary>
    [IrEnum("add", "sub", "mul", "div", "mod", "pow", "eq", "ne", "lt", "gt", "le", "ge", "and", "or", "xor")]
    public string Operator { get; set; } = "and";

    /// <summary>Left operand.</summary>
    public IrExpression Left { get; set; } = IrLiteral.False;

    /// <summary>Right operand.</summary>
    public IrExpression Right { get; set; } = IrLiteral.False;

    /// <summary>True for eq ne lt gt le ge.</summary>
    [JsonIgnore]
    public bool IsComparison => Operator is "eq" or "ne" or "lt" or "gt" or "le" or "ge";

    /// <summary>True for and or xor.</summary>
    [JsonIgnore]
    public bool IsLogical => Operator is "and" or "or" or "xor";
}

/// <summary>A standard function (<see cref="Catalogs.StandardFunctions"/>) or a project function (<see cref="Pou"/>) used in an expression.</summary>
/// <remarks>Vendor spellings are normalised by the front-end (Logix SQR becomes SQRT, ASN becomes ASIN, XPY becomes EXPT), so converters only implement the IEC names.</remarks>
public sealed class IrFunction : IrExpression
{
    /// <summary>Standard function name.</summary>
    public string? Name { get; set; }

    /// <summary>Project function (POU) name.</summary>
    [IrRefKind("pou")]
    public string? Pou { get; set; }

    /// <summary>Positional arguments; null entries are elided positions.</summary>
    public List<IrExpression?> Args { get; set; } = new();
}

/// <summary>Explicit type conversion. Front-ends insert it wherever the source language converts implicitly with vendor-specific rules.</summary>
/// <remarks>
/// Implicit conversions differ between vendors (Logix rounds REAL to DINT half-to-even and wraps on narrowing,
/// other platforms truncate or saturate), so leaving them implicit would make the middle file mean different
/// things to different converters. Making every lossy conversion a node with a mode fixes the semantics in the
/// document. Widening conversions need no node.
/// </remarks>
public sealed class IrConv : IrExpression
{
    /// <summary>Destination type.</summary>
    public IrTypeRef To { get; set; } = IrTypeRef.Named("DINT");

    /// <summary>wrap: truncate to the destination width; saturate: clamp; roundEven: round half to even (Logix REAL to integer); roundHalfUp; trunc: toward zero; checked: raise on overflow.</summary>
    [IrEnum("wrap", "saturate", "roundEven", "roundHalfUp", "trunc", "checked")]
    public string Mode { get; set; } = "wrap";

    /// <summary>The converted value.</summary>
    public IrExpression Operand { get; set; } = IrLiteral.OfInt(0);
}

/// <summary>One bit of an integer or bit-string value. Usable as an assignment target.</summary>
/// <remarks>See <see cref="IrRef"/> for why bit access is an expression rather than a path segment. <see cref="Of"/> is the integer (a reference), <see cref="Bit"/> the bit number (a literal for <c>Word.3</c>, any expression for Logix indirect bits <c>Word.[i]</c>).</remarks>
public sealed class IrBitAccess : IrExpression
{
    /// <summary>The integer or bit string whose bit is addressed.</summary>
    public IrExpression Of { get; set; } = new IrRef();

    /// <summary>Bit number, 0 = least significant.</summary>
    public IrExpression Bit { get; set; } = IrLiteral.OfInt(0);
}

/// <summary>Rising or falling edge of <see cref="Clk"/>, using <see cref="Storage"/> (a BOOL variable) as the previous-scan memory.</summary>
/// <remarks>
/// Logix one-shots (ONS, OSR, OSF) keep their memory in a user-visible storage bit instead of inside an
/// R_TRIG instance, so the expression names the storage explicitly: the result is <c>clk AND NOT storage</c>
/// (rising) and the storage is updated to <c>clk</c> as a side effect. Converters must preserve that side
/// effect and the prescan rule that storage bits are set true, so the first scan never produces a spurious pulse.
/// </remarks>
public sealed class IrEdge : IrExpression
{
    /// <summary>rising | falling</summary>
    [IrEnum("rising", "falling")]
    public string Kind { get; set; } = "rising";

    /// <summary>The signal (a rung condition for ladder one-shots).</summary>
    public IrExpression Clk { get; set; } = IrLiteral.False;

    /// <summary>The BOOL variable (or bit) remembering the previous value of <see cref="Clk"/>.</summary>
    public IrExpression Storage { get; set; } = new IrRef();
}

/// <summary>A vendor runtime function used inside an expression. <see cref="Catalog"/> names the vendor service catalog (logix, s7, twincat).</summary>
/// <remarks>The expression form of <see cref="IrSysCall"/>: a service that yields a value (Logix DTR, used as a rung condition with a side effect on its reference operand).</remarks>
public sealed class IrSysFn : IrExpression
{
    /// <summary>Catalog id.</summary>
    public string Catalog { get; set; } = "";

    /// <summary>Service name.</summary>
    public string Service { get; set; } = "";

    /// <summary>Positional arguments; null entries are elided operands.</summary>
    public List<IrExpression?> Args { get; set; } = new();
}

/// <summary>An enumeration-like operand (vendor keyword), a jump label or a name (routine, class, attribute) used as an argument.</summary>
/// <remarks>Logix instructions take bare words that are neither tags nor literals (GSV class and attribute names, motion profile names such as <c>S-Curve</c>, routine names in JSR); the front-end classifies them so converters can pass them as strings or map them onto their own enumerations.</remarks>
public sealed class IrKeyword : IrExpression
{
    /// <summary>The word as written.</summary>
    public string Text { get; set; } = "";

    /// <summary>enum: an enumeration-like operand; label: a jump label; action: an action (routine) name; name: an object name (module, task, class).</summary>
    [IrEnum("enum", "label", "action", "name")]
    public string Kind { get; set; } = "enum";
}

/// <summary>An unsupplied operand (Logix <c>?</c>) or an elided argument.</summary>
/// <remarks>Logix ladder shows display-only operands as <c>?</c> (the preset and accumulator of a TON are read from the structure, not from the operands); the placeholder keeps the argument position without inventing a value.</remarks>
public sealed class IrPlaceholder : IrExpression
{
    /// <summary>True for an elided argument (two consecutive commas), false for an explicit <c>?</c>.</summary>
    public bool Empty { get; set; }
}

/// <summary>Text the front-end could not interpret as an expression.</summary>
public sealed class IrErrorExpression : IrExpression
{
    /// <summary>The original text.</summary>
    public string Text { get; set; } = "";
}
