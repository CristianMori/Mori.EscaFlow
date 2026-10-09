namespace Mori.EscaFlow.Transpiler.L5X.Model;

/// <summary>
/// The display radix Logix stores per tag (and per L5K/decorated value). It changes how the designer shows a
/// value and, for strings written in L5K, which literal grammar applies; it never changes the stored bits.
/// </summary>
public enum Radix
{
    Unknown = 0,
    NullType,
    General,
    Binary,
    Octal,
    Decimal,
    Hex,
    Exponential,
    Float,
    Ascii,
    DateTime,
    DateTimeNs,
    /// <summary>For an array or structure member: use whatever radix its own data type declares, rather than one fixed here.</summary>
    UseTypeStyle,
}

public static class RadixExtensions
{
    /// <summary>Parses the L5X spelling of a radix, including the two-word forms ("Date/Time", "Date/Time (ns)").</summary>
    public static Radix ParseRadix(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Radix.Unknown;
        return text switch
        {
            "NullType" => Radix.NullType,
            "General" => Radix.General,
            "Binary" => Radix.Binary,
            "Octal" => Radix.Octal,
            "Decimal" => Radix.Decimal,
            "Hex" => Radix.Hex,
            "Exponential" => Radix.Exponential,
            "Float" => Radix.Float,
            "ASCII" or "Ascii" => Radix.Ascii,
            "Date/Time" or "DateTime" => Radix.DateTime,
            "Date/Time (ns)" or "DateTimeNs" => Radix.DateTimeNs,
            "UseTypeStyle" => Radix.UseTypeStyle,
            _ => Radix.Unknown,
        };
    }

    public static string ToL5xString(this Radix radix) => radix switch
    {
        Radix.NullType => "NullType",
        Radix.General => "General",
        Radix.Binary => "Binary",
        Radix.Octal => "Octal",
        Radix.Decimal => "Decimal",
        Radix.Hex => "Hex",
        Radix.Exponential => "Exponential",
        Radix.Float => "Float",
        Radix.Ascii => "ASCII",
        Radix.DateTime => "Date/Time",
        Radix.DateTimeNs => "Date/Time (ns)",
        Radix.UseTypeStyle => "UseTypeStyle",
        _ => "",
    };
}
