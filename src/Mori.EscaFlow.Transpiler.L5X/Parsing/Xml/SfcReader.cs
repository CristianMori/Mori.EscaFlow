using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic.Sfc;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>Reads a materialized <c>&lt;SFCContent&gt;</c> element.</summary>
public sealed class SfcReader
{
    private readonly ReaderContext _ctx;

    public SfcReader(ReaderContext ctx)
    {
        _ctx = ctx;
    }

    public SfcContent Read(XElement element)
    {
        var attrs = new AttributeBag(element);
        var content = new SfcContent
        {
            OnlineEditType = attrs.Take("OnlineEditType"),
            SheetSize = attrs.Take("SheetSize"),
            SheetOrientation = attrs.Take("SheetOrientation"),
            StepName = attrs.Take("StepName"),
            TransitionName = attrs.Take("TransitionName"),
            ActionName = attrs.Take("ActionName"),
            StopName = attrs.Take("StopName"),
            SourceLine = element.Line(),
        };
        content.UnknownAttributes = attrs.Remaining();

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Step": content.Nodes.Add(ReadStep(child)); break;
                case "Transition": content.Nodes.Add(ReadTransition(child)); break;
                case "Branch": content.Nodes.Add(ReadBranch(child)); break;
                case "Stop": content.Nodes.Add(ReadStop(child)); break;
                case "SbrRet": content.Nodes.Add(ReadSbrRet(child)); break;
                case "TextBox": content.Nodes.Add(ReadTextBox(child)); break;
                case "DirectedLink":
                {
                    var a = new AttributeBag(child);
                    content.Links.Add(new SfcDirectedLink
                    {
                        FromId = a.TakeInt("FromID") ?? -1,
                        ToId = a.TakeInt("ToID") ?? -1,
                        Show = a.TakeBool("Show", true),
                        SourceLine = child.Line(),
                        UnknownAttributes = a.Remaining(),
                    });
                    break;
                }
                case "Attachment":
                {
                    var a = new AttributeBag(child);
                    content.Attachments.Add(new SfcAttachment
                    {
                        FromId = a.TakeInt("FromID") ?? -1,
                        ToId = a.TakeInt("ToID") ?? -1,
                        SourceLine = child.Line(),
                        UnknownAttributes = a.Remaining(),
                    });
                    break;
                }
                default:
                    (content.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
                    break;
            }
        }
        _ctx.Statistics.SfcNodes += content.Nodes.Count;
        return content;
    }

    private SfcStep ReadStep(XElement e)
    {
        var a = new AttributeBag(e);
        var step = new SfcStep
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            Operand = a.Take("Operand"),
            HideDesc = a.TakeBool("HideDesc"),
            DescX = a.TakeInt("DescX"),
            DescY = a.TakeInt("DescY"),
            DescWidth = a.TakeInt("DescWidth"),
            InitialStep = a.TakeBool("InitialStep"),
            PresetUsesExpr = a.TakeBool("PresetUsesExpr"),
            LimitHighUsesExpr = a.TakeBool("LimitHighUsesExpr"),
            LimitLowUsesExpr = a.TakeBool("LimitLowUsesExpr"),
            ShowActions = a.TakeBool("ShowActions"),
            SourceLine = e.Line(),
        };
        step.UnknownAttributes = a.Remaining();
        foreach (var child in e.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description": step.Description = child.OwnText(); break;
                case "Preset": step.Preset = ReadSnippet(child); break;
                case "LimitHigh": step.LimitHigh = ReadSnippet(child); break;
                case "LimitLow": step.LimitLow = ReadSnippet(child); break;
                case "Action": step.Actions.Add(ReadAction(child)); break;
                default: (step.UnknownChildren ??= new()).Add(GenericElementReader.Read(child)); break;
            }
        }
        return step;
    }

    private SfcAction ReadAction(XElement e)
    {
        var a = new AttributeBag(e);
        var action = new SfcAction
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            Operand = a.Take("Operand"),
            Qualifier = a.Take("Qualifier"),
            IsBoolean = a.TakeBool("IsBoolean"),
            PresetUsesExpr = a.TakeBool("PresetUsesExpr"),
            IndicatorTag = a.Take("IndicatorTag"),
            SourceLine = e.Line(),
        };
        action.UnknownAttributes = a.Remaining();
        foreach (var child in e.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description": action.Description = child.OwnText(); break;
                case "Preset": action.Preset = ReadSnippet(child); break;
                case "Body": action.Body = ReadSnippet(child); break;
                default: (action.UnknownChildren ??= new()).Add(GenericElementReader.Read(child)); break;
            }
        }
        return action;
    }

    private SfcTransition ReadTransition(XElement e)
    {
        var a = new AttributeBag(e);
        var t = new SfcTransition
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            Operand = a.Take("Operand"),
            HideDesc = a.TakeBool("HideDesc"),
            DescX = a.TakeInt("DescX"),
            DescY = a.TakeInt("DescY"),
            DescWidth = a.TakeInt("DescWidth"),
            Force = a.TakeBoolNullable("Force"),
            SourceLine = e.Line(),
        };
        t.UnknownAttributes = a.Remaining();
        foreach (var child in e.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description": t.Description = child.OwnText(); break;
                case "Condition": t.Condition = ReadSnippet(child); break;
                default: (t.UnknownChildren ??= new()).Add(GenericElementReader.Read(child)); break;
            }
        }
        return t;
    }

    private static SfcBranch ReadBranch(XElement e)
    {
        var a = new AttributeBag(e);
        var b = new SfcBranch
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            BranchType = a.Take("BranchType") switch
            {
                "Selection" => SfcBranchType.Selection,
                "Simultaneous" => SfcBranchType.Simultaneous,
                _ => SfcBranchType.Unknown,
            },
            BranchFlow = a.Take("BranchFlow") switch
            {
                "Diverge" => SfcBranchFlow.Diverge,
                "Converge" => SfcBranchFlow.Converge,
                _ => SfcBranchFlow.Unknown,
            },
            Priority = a.Take("Priority"),
            SourceLine = e.Line(),
        };
        b.UnknownAttributes = a.Remaining();
        foreach (var leg in e.Elements("Leg"))
        {
            var la = new AttributeBag(leg);
            b.Legs.Add(new SfcLeg
            {
                Id = la.TakeInt("ID") ?? -1,
                Force = la.TakeBoolNullable("Force"),
                SourceLine = leg.Line(),
                UnknownAttributes = la.Remaining(),
            });
        }
        return b;
    }

    private static SfcStop ReadStop(XElement e)
    {
        var a = new AttributeBag(e);
        var s = new SfcStop
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            Operand = a.Take("Operand"),
            HideDesc = a.TakeBool("HideDesc"),
            DescX = a.TakeInt("DescX"),
            DescY = a.TakeInt("DescY"),
            DescWidth = a.TakeInt("DescWidth"),
            SourceLine = e.Line(),
        };
        s.UnknownAttributes = a.Remaining();
        s.Description = e.ChildText("Description");
        return s;
    }

    private static SfcSbrRet ReadSbrRet(XElement e)
    {
        var a = new AttributeBag(e);
        var s = new SfcSbrRet
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            In = a.Take("In"),
            Ret = a.Take("Ret") ?? a.Take("Out"),
            SourceLine = e.Line(),
        };
        s.UnknownAttributes = a.Remaining();
        return s;
    }

    private static SfcTextBox ReadTextBox(XElement e)
    {
        var a = new AttributeBag(e);
        var t = new SfcTextBox
        {
            Id = a.TakeInt("ID") ?? -1,
            X = a.TakeInt("X"),
            Y = a.TakeInt("Y"),
            Width = a.TakeInt("Width"),
            SourceLine = e.Line(),
        };
        t.UnknownAttributes = a.Remaining();
        t.Text = e.OwnText();
        return t;
    }

    /// <summary>Reads Preset / LimitHigh / LimitLow / Condition / Body wrappers containing an STContent.</summary>
    private StSnippet ReadSnippet(XElement wrapper)
    {
        var snippet = new StSnippet();
        foreach (var st in wrapper.Elements("STContent"))
        {
            foreach (var line in st.Elements("Line"))
            {
                var n = line.Attr("Number");
                var number = int.TryParse(n, out var v) ? v : snippet.Lines.Count;
                snippet.Lines.Add(new StLine(number, line.OwnText()));
            }
        }
        _ctx.Statistics.StLines += snippet.Lines.Count;
        return snippet;
    }
}
