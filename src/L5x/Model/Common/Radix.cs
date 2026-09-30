namespace L5x.Model;

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
    UseTypeStyle,
}

public static class RadixExtensions
{
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
