using System.Xml;
using Mori.EscaFlow.Transpiler.L5X.Model.Aoi;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// Reads an <c>&lt;AddOnInstructionDefinition&gt;</c> with a streaming cursor. An add-on instruction is Logix's
/// function block: parameters (input, output, InOut), local tags, a Logic routine, and optional Prescan,
/// Postscan and EnableInFalse routines whose execution is gated by the <c>Execute*</c> flags. Its parameters and
/// locals double as a structure type (the backing type of every instance tag).
/// </summary>
public sealed class AoiReader
{
    private readonly ReaderContext _ctx;
    private readonly TagReader _tags;
    private readonly RoutineReader _routines;

    public AoiReader(ReaderContext ctx, TagReader tags, RoutineReader routines)
    {
        _ctx = ctx;
        _tags = tags;
        _routines = routines;
    }

    public AddOnInstructionDefinition Read(XmlReader reader)
    {
        var line = reader.LineNumber();
        var attrs = new AttributeBag(reader);
        var aoi = new AddOnInstructionDefinition
        {
            Name = attrs.TakeRequired("Name", "?"),
            Revision = attrs.Take("Revision"),
            RevisionExtension = attrs.Take("RevisionExtension"),
            Vendor = attrs.Take("Vendor"),
            Class = attrs.Take("Class"),
            ExecutePrescan = attrs.TakeBool("ExecutePrescan"),
            ExecutePostscan = attrs.TakeBool("ExecutePostscan"),
            ExecuteEnableInFalse = attrs.TakeBool("ExecuteEnableInFalse"),
            CreatedDate = attrs.Take("CreatedDate"),
            CreatedBy = attrs.Take("CreatedBy"),
            EditedDate = attrs.Take("EditedDate"),
            EditedBy = attrs.Take("EditedBy"),
            SoftwareRevision = attrs.Take("SoftwareRevision"),
            IsEncrypted = attrs.TakeBoolNullable("IsEncrypted"),
            SourceLine = line,
        };
        aoi.UnknownAttributes = attrs.Remaining();

        reader.ForEachChild((r, child) =>
        {
            switch (child)
            {
                case "Description":
                    aoi.Description = r.ReadNormalizedText();
                    break;
                case "RevisionNote":
                    aoi.RevisionNote = r.ReadNormalizedText();
                    break;
                case "AdditionalHelpText":
                    aoi.AdditionalHelpText = r.ReadNormalizedText();
                    break;
                case "SignatureHistory":
                    aoi.SignatureHistory = _ctx.ReadGeneric(r);
                    break;
                case "EncodedData":
                    aoi.EncodedData = _ctx.ReadGeneric(r);
                    break;
                case "EncryptionInfo":
                    aoi.EncryptionInfo = _ctx.ReadGeneric(r);
                    break;
                case "Parameters":
                    // Declaration order matters twice: it is the positional order of required operands in a call and the
                    // member order of the backing structure.
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Parameter")
                        {
                            var p = (AoiParameter)_tags.Read(rr, TagScope.AoiParameter, aoi.Name);
                            p.Aoi = aoi;
                            aoi.Parameters.Add(p);
                            _ctx.Statistics.AoiParameters++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "Parameters", aoi);
                        }
                    });
                    break;
                case "LocalTags":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "LocalTag")
                        {
                            var t = _tags.Read(rr, TagScope.AoiLocal, aoi.Name);
                            t.Aoi = aoi;
                            aoi.LocalTags.Add(t);
                            _ctx.Statistics.AoiLocalTags++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "LocalTags", aoi);
                        }
                    });
                    break;
                case "Routines":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Routine") aoi.Routines.Add(_routines.Read(rr, aoi));
                        // Source-protected routines appear only as encoded payloads.
                        else if (c is "EncodedData" or "EncryptedContent") aoi.EncodedRoutines.Add(_ctx.ReadGeneric(rr));
                        else _ctx.Unexpected(rr, "Routines", aoi);
                    });
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    r.Skip();
                    break;
                default:
                    _ctx.Unexpected(r, "AddOnInstructionDefinition", aoi);
                    break;
            }
        });
        return aoi;
    }
}
