using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Fbd;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// Reads a materialized <c>&lt;FBDContent&gt;</c> element (spec pp. 223–255). A function block diagram is a set of
/// sheets; each sheet holds elements (input and output references, wire connectors, instruction blocks with their
/// backing tags, tagless functions, add-on instructions, JSR/SBR/RET markers, text boxes) addressed by numeric
/// ID, and wires that connect an output pin of one element to an input pin of another. Feedback wires are marked
/// separately because they carry the previous scan's value and define the execution order.
/// </summary>
public sealed class FbdReader
{
    private readonly ReaderContext _ctx;

    public FbdReader(ReaderContext ctx)
    {
        _ctx = ctx;
    }

    public FbdContent Read(XElement element)
    {
        var attrs = new AttributeBag(element);
        var content = new FbdContent
        {
            OnlineEditType = attrs.Take("OnlineEditType"),
            SheetSize = attrs.Take("SheetSize"),
            SheetOrientation = attrs.Take("SheetOrientation"),
            SourceLine = element.Line(),
        };
        content.UnknownAttributes = attrs.Remaining();
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "Sheet") content.Sheets.Add(ReadSheet(child));
            else (content.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
        }
        return content;
    }

    /// <summary>One sheet: elements and wires. Unknown element kinds are kept generically so a newer instruction family does not break the parse.</summary>
    private FbdSheet ReadSheet(XElement e)
    {
        var a = new AttributeBag(e);
        var sheet = new FbdSheet
        {
            Number = a.TakeInt("Number") ?? 0,
            SourceLine = e.Line(),
        };
        sheet.UnknownAttributes = a.Remaining();
        foreach (var child in e.Elements())
        {
            var b = new AttributeBag(child);
            FbdElement? el = null;
            switch (child.Name.LocalName)
            {
                case "Description":
                    sheet.Description = child.OwnText();
                    continue;
                case "IRef":
                    el = new FbdIRef { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Operand = b.Take("Operand"), HideDesc = b.TakeBool("HideDesc") };
                    break;
                case "ORef":
                    el = new FbdORef { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Operand = b.Take("Operand"), HideDesc = b.TakeBool("HideDesc") };
                    break;
                case "ICon":
                    el = new FbdICon { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Name = b.Take("Name") };
                    break;
                case "OCon":
                    el = new FbdOCon { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Name = b.Take("Name") };
                    break;
                case "Block":
                {
                    // A function block instruction: its pins are members of the backing tag (Operand); VisiblePins lists
                    // the ones drawn, and a few instructions bind array tags through Array child elements.
                    var block = new FbdBlock
                    {
                        Type = b.TakeRequired("Type", "?"),
                        Id = Id(b),
                        X = b.TakeInt("X"),
                        Y = b.TakeInt("Y"),
                        Operand = b.Take("Operand"),
                        HideDesc = b.TakeBool("HideDesc"),
                        AutotuneTag = b.Take("AutotuneTag"),
                    };
                    block.VisiblePins.AddRange(SplitList(b.Take("VisiblePins")));
                    foreach (var arr in child.Elements("Array"))
                    {
                        var n = arr.Attr("Name");
                        var op = arr.Attr("Operand");
                        if (n is not null && op is not null) (block.Arrays ??= new(StringComparer.OrdinalIgnoreCase))[n] = op;
                    }
                    el = block;
                    break;
                }
                case "Function":
                    el = new FbdFunction { Type = b.TakeRequired("Type", "?"), Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y") };
                    break;
                case "AddOnInstruction":
                {
                    // Add-on calls carry their InOut arguments as text on the block rather than as wires.
                    var aoi = new FbdAddOnInstruction
                    {
                        Name = b.TakeRequired("Name", "?"),
                        Id = Id(b),
                        X = b.TakeInt("X"),
                        Y = b.TakeInt("Y"),
                        Operand = b.Take("Operand"),
                        HideDesc = b.TakeBool("HideDesc"),
                    };
                    aoi.VisiblePins.AddRange(SplitList(b.Take("VisiblePins")));
                    foreach (var p in child.Elements("InOutParameter"))
                    {
                        aoi.InOutParameters.Add(new KeyValuePair<string, string>(p.Attr("Name") ?? "", p.Attr("Argument") ?? ""));
                    }
                    el = aoi;
                    break;
                }
                case "JSR":
                {
                    var jsr = new FbdJsr { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Routine = b.Take("Routine") };
                    jsr.In.AddRange(SplitList(b.Take("In")));
                    jsr.Ret.AddRange(SplitList(b.Take("Ret")));
                    el = jsr;
                    break;
                }
                case "SBR":
                {
                    var sbr = new FbdSbr { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Routine = b.Take("Routine") };
                    sbr.In.AddRange(SplitList(b.Take("In")));
                    el = sbr;
                    break;
                }
                case "RET":
                {
                    var ret = new FbdRet { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Routine = b.Take("Routine") };
                    ret.Ret.AddRange(SplitList(b.Take("Ret")));
                    el = ret;
                    break;
                }
                case "TextBox":
                    el = new FbdTextBox { Id = Id(b), X = b.TakeInt("X"), Y = b.TakeInt("Y"), Width = b.TakeInt("Width"), Text = child.OwnText() };
                    break;
                case "Wire":
                case "FeedbackWire":
                    sheet.Wires.Add(new FbdWire
                    {
                        FromId = b.TakeInt("FromID") ?? -1,
                        FromParam = b.Take("FromParam"),
                        ToId = b.TakeInt("ToID") ?? -1,
                        ToParam = b.Take("ToParam"),
                        IsFeedback = child.Name.LocalName == "FeedbackWire",
                        SourceLine = child.Line(),
                        UnknownAttributes = b.Remaining(),
                    });
                    continue;
                case "Attachment":
                    sheet.Attachments.Add(new FbdAttachment
                    {
                        FromId = b.TakeInt("FromID") ?? -1,
                        ToId = b.TakeInt("ToID") ?? -1,
                        SourceLine = child.Line(),
                        UnknownAttributes = b.Remaining(),
                    });
                    continue;
                default:
                    (sheet.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
                    continue;
            }
            el.SourceLine = child.Line();
            el.UnknownAttributes = b.Remaining();
            sheet.Elements.Add(el);
        }
        return sheet;
    }

    private static int Id(AttributeBag b) => b.TakeInt("ID") ?? -1;

    /// <summary>Pin lists are written space separated; commas are accepted for tolerance.</summary>
    private static IEnumerable<string> SplitList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
