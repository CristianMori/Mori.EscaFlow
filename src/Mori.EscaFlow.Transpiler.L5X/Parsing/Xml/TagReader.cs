using System.Xml;
using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.L5X.Model.Values;
using Mori.EscaFlow.Transpiler.L5X.Parsing.Values;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>Reads Tag, Parameter and LocalTag elements with a streaming cursor, and Data elements anywhere.</summary>
public sealed class TagReader
{
    private readonly ReaderContext _ctx;

    public TagReader(ReaderContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Reads the element the reader is on (Tag / Parameter / LocalTag) and moves past it.</summary>
    public Tag Read(XmlReader reader, TagScope scope, string? container)
    {
        var line = reader.LineNumber();
        var attrs = new AttributeBag(reader);
        var name = attrs.TakeRequired("Name", "?");
        var tagTypeText = attrs.Take("TagType");
        var tagType = tagTypeText switch
        {
            "Alias" => TagType.Alias,
            "Produced" => TagType.Produced,
            "Consumed" => TagType.Consumed,
            _ => TagType.Base,
        };
        var dataTypeName = attrs.Take("DataType");
        var dimensions = attrs.TakeDimensions("Dimensions");
        var radix = attrs.TakeRadix();
        var constant = attrs.TakeBool("Constant");
        var externalAccess = attrs.Take("ExternalAccess");
        var opcUa = attrs.Take("OpcUaAccess");
        var aliasFor = attrs.Take("AliasFor");
        var usage = attrs.Take("Usage");
        var cls = attrs.Take("Class");
        var sequencing = attrs.Take("Sequencing");

        Tag tag;
        if (scope == TagScope.AoiParameter)
        {
            var required = attrs.TakeBool("Required");
            var visible = attrs.TakeBool("Visible");
            tag = new AoiParameter
            {
                Name = name,
                TagType = tagType,
                DataType = dataTypeName is null ? null : DataTypeRef.Of(dataTypeName),
                Dimensions = dimensions,
                Radix = radix,
                Constant = constant,
                ExternalAccess = externalAccess,
                OpcUaAccess = opcUa,
                AliasFor = aliasFor,
                Usage = usage,
                Class = cls,
                Sequencing = sequencing,
                Scope = scope,
                SourceLine = line,
                ParameterUsage = usage switch
                {
                    "Output" => AoiParameterUsage.Output,
                    "InOut" => AoiParameterUsage.InOut,
                    _ => AoiParameterUsage.Input,
                },
                Required = required,
                Visible = visible,
            };
        }
        else
        {
            tag = new Tag
            {
                Name = name,
                TagType = tagType,
                DataType = dataTypeName is null ? null : DataTypeRef.Of(dataTypeName),
                Dimensions = dimensions,
                Radix = radix,
                Constant = constant,
                ExternalAccess = externalAccess,
                OpcUaAccess = opcUa,
                AliasFor = aliasFor,
                Usage = usage,
                Class = cls,
                Sequencing = sequencing,
                Scope = scope,
                SourceLine = line,
            };
        }
        tag.UnknownAttributes = attrs.Remaining();

        var parentName = reader.LocalName;
        TagData? data = null;
        reader.ForEachChild((r, child) =>
        {
            switch (child)
            {
                case "Description":
                    tag.Description = r.ReadNormalizedText();
                    break;
                case "ConsumeInfo":
                    tag.ConsumeInfo = _ctx.ReadGeneric(r);
                    break;
                case "ProduceInfo":
                    tag.ProduceInfo = _ctx.ReadGeneric(r);
                    break;
                case "ForceData":
                    tag.ForceData = _ctx.ReadGeneric(r);
                    break;
                case "AlarmConditions":
                    tag.AlarmConditions = _ctx.ReadGeneric(r);
                    break;
                case "Comments":
                    ReadComments(r, tag.Comments);
                    break;
                case "EngineeringUnits":
                    tag.EngineeringUnits = ReadOperandMap(r, "EngineeringUnit");
                    break;
                case "Mins":
                    tag.Mins = ReadOperandMap(r, "Min");
                    break;
                case "Maxes":
                    tag.Maxes = ReadOperandMap(r, "Max");
                    break;
                case "State0s":
                    tag.State0s = ReadOperandMap(r, "State0");
                    break;
                case "State1s":
                    tag.State1s = ReadOperandMap(r, "State1");
                    break;
                case "Data":
                case "DefaultData":
                    data ??= new TagData();
                    ReadData(r, data, name, container);
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    r.Skip();
                    break;
                default:
                    _ctx.Unexpected(r, parentName, tag);
                    break;
            }
        });

        if (data is not null)
        {
            FinishData(data, dataTypeName);
            tag.Data = data;
        }
        return tag;
    }

    /// <summary>Reads <c>&lt;Comments&gt;&lt;Comment Operand="..."&gt;</c> children.</summary>
    public void ReadComments(XmlReader reader, List<TagComment> into)
    {
        reader.ForEachChild((r, child) =>
        {
            if (child == "Comment")
            {
                var operand = r.GetAttribute("Operand") ?? "";
                var text = r.ReadNormalizedText();
                into.Add(new TagComment { Operand = operand, Text = text });
            }
            else
            {
                r.Skip();
            }
        });
    }

    private static Dictionary<string, string> ReadOperandMap(XmlReader reader, string childName)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        reader.ForEachChild((r, child) =>
        {
            if (child == childName)
            {
                var operand = r.GetAttribute("Operand") ?? "";
                map[operand] = r.ReadNormalizedText();
            }
            else
            {
                r.Skip();
            }
        });
        return map;
    }

    /// <summary>Reads one Data / DefaultData element (reader on the element) into <paramref name="data"/>, dispatching on Format.</summary>
    public void ReadData(XmlReader reader, TagData data, string? tagName, string? container)
    {
        var format = reader.GetAttribute("Format");
        var line = reader.LineNumber();
        switch (format)
        {
            case null:
            case "":
            {
                var text = reader.ReadNormalizedText();
                data.RawHex = ParseHex(text);
                break;
            }
            case "L5K":
            {
                if (!_ctx.ParseL5k && !_ctx.Options.KeepRawL5kText)
                {
                    reader.Skip();
                    break;
                }
                var text = reader.ReadNormalizedText();
                if (_ctx.Options.KeepRawL5kText) data.L5kText = text;
                if (_ctx.ParseL5k)
                {
                    var sink = new ScopedDiagnosticSink(_ctx.Diagnostics, new DiagnosticLocation(XmlLine: line, Tag: tagName, Container: container));
                    data.L5k = L5kValueParser.Parse(text.AsSpan(), sink);
                }
                break;
            }
            case "Decorated":
            {
                if (!_ctx.ParseDecorated)
                {
                    reader.Skip();
                    break;
                }
                var sink = new ScopedDiagnosticSink(_ctx.Diagnostics, new DiagnosticLocation(XmlLine: line, Tag: tagName, Container: container));
                data.Decorated = DecoratedDataReader.Read(reader, sink, _ctx.Interner);
                if (data.Decorated is not null) _ctx.Statistics.ValueNodes += CountNodes(data.Decorated);
                break;
            }
            case "String":
            {
                var lengthText = reader.GetAttribute("Length");
                var text = reader.ReadNormalizedText();
                data.StringLength = int.TryParse(lengthText, out var len) ? len : null;
                data.StringText = LogixStringDecoder.DecodeQuoted(text.AsSpan()) ?? LogixStringDecoder.Decode(text.AsSpan());
                break;
            }
            case "Message":
            {
                var element = _ctx.ReadGeneric(reader);
                data.MessageElement = element;
                var parameters = element.Child("MessageParameters");
                data.Message = MessageConfiguration.FromAttributes(parameters?.Attributes ?? new Dictionary<string, string>());
                break;
            }
            case "Alarm":
                data.Alarm = _ctx.ReadGeneric(reader);
                break;
            case "Axis":
                data.Axis = _ctx.ReadGeneric(reader);
                break;
            case "MotionGroup":
                data.MotionGroup = _ctx.ReadGeneric(reader);
                break;
            case "CoordinateSystem":
                data.CoordinateSystem = _ctx.ReadGeneric(reader);
                break;
            default:
                (data.Other ??= new()).Add(_ctx.ReadGeneric(reader));
                break;
        }
    }

    /// <summary>Reads Data children of an already materialized element (module config / IO tags).</summary>
    public TagData ReadDataFromElement(XElement owner, string? tagName, string? container)
    {
        var data = new TagData();
        foreach (var d in owner.Elements("Data"))
        {
            using var r = d.CreateReader();
            r.MoveToContent();
            ReadData(r, data, tagName, container);
        }
        return data;
    }

    /// <summary>Builds the Decorated value from a String format when no Decorated block exists.</summary>
    public static void FinishData(TagData data, string? dataTypeName)
    {
        if (data.Decorated is null && data.StringText is not null)
        {
            data.Decorated = new StringValue(data.StringLength ?? data.StringText.Length, data.StringText, dataTypeName ?? "STRING");
        }
    }

    private static byte[]? ParseHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out bytes[i])) return null;
        }
        return bytes;
    }

    public static long CountNodes(TagValue value)
    {
        switch (value)
        {
            case StructValue s:
            {
                long n = 1;
                foreach (var m in s.Members) n += CountNodes(m.Value);
                return n;
            }
            case ArrayValue a:
            {
                long n = 1;
                foreach (var e in a.Elements) n += CountNodes(e);
                return n;
            }
            default:
                return 1;
        }
    }
}
