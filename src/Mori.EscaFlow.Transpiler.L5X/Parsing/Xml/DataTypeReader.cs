using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// Reads a materialized <c>&lt;DataType&gt;</c> element: a user-defined structure (UDT) or a user string type
/// (<c>Family="StringFamily"</c>, LEN plus DATA members). The export lists members in layout order including the
/// hidden BOOL hosts (<c>ZZZZZZZZZZ...</c>) that BIT members overlay; both are kept because the host order is the
/// byte layout a tag server must reproduce.
/// </summary>
public sealed class DataTypeReader
{
    private readonly ReaderContext _ctx;

    public DataTypeReader(ReaderContext ctx)
    {
        _ctx = ctx;
    }

    public StructuredType Read(XElement element)
    {
        var attrs = new AttributeBag(element);
        var type = new StructuredType
        {
            Name = attrs.TakeRequired("Name", "?"),
            Class = DataTypeClass.User,
            Family = attrs.Take("Family") == "StringFamily" ? DataTypeFamily.StringFamily : DataTypeFamily.NoFamily,
            SourceLine = element.Line(),
        };
        // The Class attribute is always "User" in this section; predefined and module types never appear here.
        _ = attrs.Take("Class");
        type.UnknownAttributes = attrs.Remaining();

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description":
                    type.Description = child.OwnText();
                    break;
                case "EngineeringUnit":
                    type.EngineeringUnit = child.OwnText();
                    break;
                case "Members":
                    foreach (var m in child.Elements("Member")) type.Members.Add(ReadMember(m));
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    break;
                default:
                    (type.UnknownChildren ??= new()).Add(GenericElementReader.Read(child, _ctx.Options.SkipCustomProperties));
                    break;
            }
        }
        return type;
    }

    /// <summary>
    /// One member. A BIT member carries <c>Target</c> (its hidden host member) and <c>BitNumber</c>; an array member
    /// carries <c>Dimension</c> (UDT members are at most one-dimensional); <c>Hidden</c> marks the hosts.
    /// </summary>
    private static DataTypeMember ReadMember(XElement m)
    {
        var attrs = new AttributeBag(m);
        var member = new DataTypeMember
        {
            Name = attrs.TakeRequired("Name", "?"),
            DataType = DataTypeRef.Of(attrs.TakeRequired("DataType", "?")),
            Dimension = attrs.TakeInt("Dimension") ?? 0,
            Radix = attrs.TakeRadix(),
            Hidden = attrs.TakeBool("Hidden"),
            ExternalAccess = attrs.Take("ExternalAccess"),
            Target = attrs.Take("Target"),
            BitNumber = attrs.TakeInt("BitNumber"),
            SourceLine = m.Line(),
        };
        member.Min = attrs.Take("Min");
        member.Max = attrs.Take("Max");
        member.UnknownAttributes = attrs.Remaining();
        foreach (var child in m.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description": member.Description = child.OwnText(); break;
                case "EngineeringUnit": member.EngineeringUnit = child.OwnText(); break;
                case "State0": member.State0 = child.OwnText(); break;
                case "State1": member.State1 = child.OwnText(); break;
                default: (member.UnknownChildren ??= new()).Add(GenericElementReader.Read(child)); break;
            }
        }
        return member;
    }
}
