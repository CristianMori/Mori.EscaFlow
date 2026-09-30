using System.Xml.Linq;
using L5x.Model.DataTypes;

namespace L5x.Parsing.Xml;

/// <summary>Reads a materialized <c>&lt;DataType&gt;</c> element.</summary>
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
