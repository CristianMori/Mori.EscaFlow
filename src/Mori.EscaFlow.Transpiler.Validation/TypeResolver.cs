using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.Validation;

/// <summary>Type queries over a document: name resolution and atomic classification used by the referential and typing passes.</summary>
internal sealed class TypeResolver
{
    private readonly IrDocument _doc;

    public TypeResolver(IrDocument doc) => _doc = doc;

    /// <summary>True when the name is an atomic, a listed type, a POU usable as an instance type, a standard function block or a bare string type.</summary>
    public bool TypeNameExists(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (AtomicTypes.IsAtomic(name)) return true;
        if (IsBareString(name)) return true;
        if (_doc.FindType(name) is not null) return true;
        if (_doc.FindPou(name) is { Kind: "functionBlock" or "program" }) return true;
        return StandardFunctionBlocks.IsStandard(name);
    }

    public static bool IsBareString(string name) => name.Equals("STRING", StringComparison.OrdinalIgnoreCase) || name.Equals("WSTRING", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every named leaf of a type reference (arrays, pointers), for existence checks.</summary>
    public static IEnumerable<string> LeafNames(IrTypeRef? t)
    {
        while (t is not null)
        {
            switch (t.Kind)
            {
                case "named":
                    if (t.Name is not null) yield return t.Name;
                    yield break;
                case "array": t = t.Element; break;
                case "pointer" or "reference": t = t.To; break;
                default: yield break;
            }
        }
    }

    // ------------------------------------------------------------------ classification

    public enum Cat
    {
        Unknown,
        Bool,
        Integer,
        BitString,
        Real,
        String,
        Time,
        Date,
        Char,
        Enum,
        Struct,
        Array,
        Other,
    }

    public Cat Classify(IrTypeRef? t)
    {
        if (t is null) return Cat.Unknown;
        switch (t.Kind)
        {
            case "any": return Cat.Unknown;
            case "array": return Cat.Array;
            case "string": return Cat.String;
            case "pointer" or "reference": return Cat.Other;
        }
        var name = t.Name;
        if (string.IsNullOrEmpty(name)) return Cat.Unknown;
        if (AtomicTypes.Get(name) is { } a)
            return a.Category switch
            {
                AtomicCategory.Bool => Cat.Bool,
                AtomicCategory.SignedInt or AtomicCategory.UnsignedInt => Cat.Integer,
                AtomicCategory.BitString => Cat.BitString,
                AtomicCategory.Real => Cat.Real,
                AtomicCategory.Time => Cat.Time,
                AtomicCategory.Date => Cat.Date,
                AtomicCategory.Char => Cat.Char,
                _ => Cat.Other,
            };
        if (IsBareString(name)) return Cat.String;
        var def = _doc.FindType(name);
        if (def is null) return Cat.Unknown;
        return def.Kind switch
        {
            "string" => Cat.String,
            "enum" => Cat.Enum,
            "alias" => Classify(def.Type),
            "subrange" => Classify(def.Type),
            "array" => Cat.Array,
            "struct" or "union" => def.Opaque ? Cat.Unknown : Cat.Struct,
            _ => Cat.Unknown,
        };
    }

    public static bool IsNumericCat(Cat c) => c is Cat.Integer or Cat.Real or Cat.Bool or Cat.BitString or Cat.Enum;
    public static bool IsIntegerLike(Cat c) => c is Cat.Integer or Cat.BitString or Cat.Bool or Cat.Enum;

    /// <summary>Assignment / pin compatibility: unknown on either side passes; equal named types pass; atomic scalars convert among bool/int/bitstring/real; strings to strings.</summary>
    public bool Compatible(IrTypeRef? target, IrTypeRef? value)
    {
        if (target is null || value is null) return true;
        if (target.Equals(value)) return true;
        var tc = Classify(target);
        var vc = Classify(value);
        if (tc == Cat.Unknown || vc == Cat.Unknown) return true;
        if (IsNumericCat(tc) && IsNumericCat(vc)) return true;
        if (tc == Cat.String && vc == Cat.String) return true;
        if (tc == Cat.Time && vc is Cat.Time or Cat.Integer) return true;
        if (tc == Cat.Char && vc is Cat.Char or Cat.String or Cat.Integer) return true;
        if (tc == Cat.Date && vc is Cat.Date) return true;
        if (tc == Cat.Array && vc == Cat.Array) return true;
        // alias / subrange resolve to their base
        if (tc == Cat.Struct && vc == Cat.Struct)
            return string.Equals(BaseName(target), BaseName(value), StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private string? BaseName(IrTypeRef t)
    {
        var name = t.LeafName;
        for (var i = 0; i < 8 && name is not null; i++)
        {
            var def = _doc.FindType(name);
            if (def is { Kind: "alias" or "subrange", Type: { } b }) name = b.LeafName;
            else break;
        }
        return name;
    }

    /// <summary>Members of a named type for path walking: project struct/union members, POU vars (instances), standard block pins. Null when unknown or opaque.</summary>
    public Dictionary<string, IrTypeRef>? MembersOf(string name)
    {
        var def = _doc.FindType(name);
        if (def is not null)
        {
            if (def.Kind is "alias" or "subrange") return def.Type?.LeafName is { } b && def.Type.Kind == "named" ? MembersOf(b) : null;
            if (def.Opaque || def.Members is null) return null;
            var d = new Dictionary<string, IrTypeRef>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in def.Members) d.TryAdd(m.Name, m.Type);
            return d;
        }
        if (_doc.FindPou(name) is { } pou)
        {
            var d = new Dictionary<string, IrTypeRef>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in pou.Vars) d.TryAdd(v.Name, v.Type);
            return d;
        }
        if (StandardFunctionBlocks.Get(name) is { } fb)
        {
            var d = new Dictionary<string, IrTypeRef>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pin, type) in fb.Inputs) d.TryAdd(pin, IrTypeRef.Named(type));
            foreach (var (pin, type) in fb.Outputs) d.TryAdd(pin, IrTypeRef.Named(type));
            return d;
        }
        return null;
    }

    /// <summary>Array shape of a type reference: dims and element type, following named array types.</summary>
    public (List<IrDim> Dims, IrTypeRef Element)? ArrayShape(IrTypeRef t)
    {
        if (t.Kind == "array" && t.Element is not null) return (t.Dims ?? new List<IrDim>(), t.Element);
        if (t.Kind == "named" && t.Name is not null && _doc.FindType(t.Name) is { Kind: "array", Type: { } element } def)
            return (def.Dims ?? new List<IrDim>(), element);
        return null;
    }

    public bool IsStringType(IrTypeRef t) => Classify(t) == Cat.String;
}
