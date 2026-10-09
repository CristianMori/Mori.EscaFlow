using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Diagnostics;
using Mori.EscaFlow.Transpiler.L5X.Model.Logic;
using Mori.EscaFlow.Transpiler.L5X.Model.Programs;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// Reads a <c>&lt;Routine&gt;</c> with a streaming cursor; SFC and FBD bodies are materialized per routine. Ladder
/// and ST bodies are only collected as text here (rung text, ST lines); the logic phase parses them later once the
/// add-on names are known. A routine may carry several content blocks when online edits are pending.
/// </summary>
public sealed class RoutineReader
{
    private readonly ReaderContext _ctx;
    private readonly SfcReader _sfc;
    private readonly FbdReader _fbd;

    public RoutineReader(ReaderContext ctx)
    {
        _ctx = ctx;
        _sfc = new SfcReader(ctx);
        _fbd = new FbdReader(ctx);
    }

    public Routine Read(XmlReader reader, IRoutineContainer owner)
    {
        var line = reader.LineNumber();
        var attrs = new AttributeBag(reader);
        var typeText = attrs.Take("Type");
        var routine = new Routine
        {
            Name = attrs.TakeRequired("Name", "?"),
            Type = typeText switch
            {
                "RLL" => RoutineType.RLL,
                "ST" => RoutineType.ST,
                "SFC" => RoutineType.SFC,
                "FBD" => RoutineType.FBD,
                _ => RoutineType.Unknown,
            },
            SourceLine = line,
            Owner = owner,
        };
        routine.UnknownAttributes = attrs.Remaining();
        if (routine.Type == RoutineType.Unknown && typeText is not null)
            _ctx.Diagnostics.Warning("XML002", $"Unknown routine type '{typeText}'", new DiagnosticLocation(XmlLine: line, Container: owner.Name, Routine: routine.Name));

        reader.ForEachChild((r, child) =>
        {
            switch (child)
            {
                case "Description":
                    routine.Description = r.ReadNormalizedText();
                    break;
                case "RLLContent":
                    AddContent(routine, ReadRll(r, routine));
                    break;
                case "STContent":
                    AddContent(routine, ReadSt(r));
                    break;
                case "SFCContent":
                    AddContent(routine, _sfc.Read(r.ReadElement()));
                    break;
                case "FBDContent":
                    AddContent(routine, _fbd.Read(r.ReadElement()));
                    break;
                case "EncryptionInfo":
                    routine.EncryptionInfo = _ctx.ReadGeneric(r);
                    break;
                case "EncryptedContent":
                case "EncryptedSegments":
                    // Source-protected routine: there is no body to parse, only the encrypted payload.
                    routine.EncryptedContent = _ctx.ReadGeneric(r);
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    r.Skip();
                    break;
                default:
                    _ctx.Unexpected(r, "Routine", routine);
                    break;
            }
        });

        var s = _ctx.Statistics;
        s.Routines++;
        switch (routine.Type)
        {
            case RoutineType.RLL: s.RllRoutines++; break;
            case RoutineType.ST: s.StRoutines++; break;
            case RoutineType.SFC: s.SfcRoutines++; break;
            case RoutineType.FBD: s.FbdRoutines++; break;
        }
        return routine;
    }

    /// <summary>
    /// Picks the routine's effective body. An export taken with online edits pending contains the <c>Original</c>
    /// content plus <c>PendingEdits</c> / <c>TestEdits</c> variants; the original (what the controller runs) is the
    /// content and the edit variants are kept aside, whatever order they appear in.
    /// </summary>
    private static void AddContent(Routine routine, RoutineContent content)
    {
        var isOriginal = content.OnlineEditType is null || content.OnlineEditType == "Original";
        if (routine.Content is null && isOriginal)
        {
            routine.Content = content;
        }
        else if (routine.Content is null)
        {
            // Only edited content exists so far; keep it as content until an original shows up.
            routine.Content = content;
        }
        else if (isOriginal && routine.Content.OnlineEditType is not null && routine.Content.OnlineEditType != "Original")
        {
            routine.OnlineEditContents.Add(routine.Content);
            routine.Content = content;
        }
        else
        {
            routine.OnlineEditContents.Add(content);
        }
    }

    /// <summary>Rungs: number, type (N normal; I/D/R and the lower-case forms mark pending inserts, deletes and replacements), comment and neutral text.</summary>
    private RllContent ReadRll(XmlReader reader, Routine routine)
    {
        var attrs = new AttributeBag(reader);
        var content = new RllContent { OnlineEditType = attrs.Take("OnlineEditType"), SourceLine = reader.LineNumber() };
        content.UnknownAttributes = attrs.Remaining();
        reader.ForEachChild((r, child) =>
        {
            if (child != "Rung")
            {
                _ctx.Unexpected(r, "RLLContent", content);
                return;
            }
            var ra = new AttributeBag(r);
            var number = ra.TakeInt("Number") ?? content.Rungs.Count;
            var type = ra.Take("Type") ?? "N";
            var unknown = ra.Remaining();
            var line = r.LineNumber();
            string? comment = null;
            var text = string.Empty;
            List<Model.GenericElement>? extra = null;
            r.ForEachChild((rr, c) =>
            {
                switch (c)
                {
                    case "Comment": comment = rr.ReadNormalizedText(); break;
                    case "Text": text = rr.ReadNormalizedText(); break;
                    default: (extra ??= new()).Add(_ctx.ReadGeneric(rr)); break;
                }
            });
            content.Rungs.Add(new Rung
            {
                Number = number,
                Type = type,
                Comment = comment,
                Text = text,
                SourceLine = line,
                UnknownAttributes = unknown,
                UnknownChildren = extra,
            });
            _ctx.Statistics.Rungs++;
        });
        return content;
    }

    private StContent ReadSt(XmlReader reader)
    {
        var attrs = new AttributeBag(reader);
        var content = new StContent { OnlineEditType = attrs.Take("OnlineEditType"), SourceLine = reader.LineNumber() };
        content.UnknownAttributes = attrs.Remaining();
        ReadStLines(reader, content.Lines);
        _ctx.Statistics.StLines += content.Lines.Count;
        return content;
    }

    /// <summary>Reads the <c>&lt;Line&gt;</c> children of an STContent element (reader on STContent). One element per source line; statements may span several.</summary>
    public void ReadStLines(XmlReader reader, List<StLine> into)
    {
        reader.ForEachChild((r, child) =>
        {
            if (child == "Line")
            {
                var n = r.GetAttribute("Number");
                var number = int.TryParse(n, out var v) ? v : into.Count;
                into.Add(new StLine(number, r.ReadNormalizedText()));
            }
            else
            {
                r.Skip();
            }
        });
    }
}
