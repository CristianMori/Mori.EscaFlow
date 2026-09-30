using System.Xml;
using L5x.Model.Programs;
using L5x.Model.Tags;

namespace L5x.Parsing.Xml;

/// <summary>Reads a <c>&lt;Program&gt;</c> with a streaming cursor.</summary>
public sealed class ProgramReader
{
    private readonly ReaderContext _ctx;
    private readonly TagReader _tags;
    private readonly RoutineReader _routines;

    public ProgramReader(ReaderContext ctx, TagReader tags, RoutineReader routines)
    {
        _ctx = ctx;
        _tags = tags;
        _routines = routines;
    }

    public Program Read(XmlReader reader)
    {
        var line = reader.LineNumber();
        var attrs = new AttributeBag(reader);
        var program = new Program
        {
            Name = attrs.TakeRequired("Name", "?"),
            Type = attrs.Take("Type"),
            Class = attrs.Take("Class"),
            TestEdits = attrs.TakeBool("TestEdits"),
            Disabled = attrs.TakeBool("Disabled"),
            UseAsFolder = attrs.TakeBool("UseAsFolder"),
            MainRoutineName = attrs.Take("MainRoutineName"),
            FaultRoutineName = attrs.Take("FaultRoutineName"),
            SynchronizeRedundancyDataAfterExecution = attrs.TakeBoolNullable("SynchronizeRedundancyDataAfterExecution"),
            SourceLine = line,
        };
        program.UnknownAttributes = attrs.Remaining();

        reader.ForEachChild((r, child) =>
        {
            switch (child)
            {
                case "Description":
                    program.Description = r.ReadNormalizedText();
                    break;
                case "Tags":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Tag")
                        {
                            var t = _tags.Read(rr, TagScope.Program, program.Name);
                            t.Program = program;
                            program.Tags.Add(t);
                            _ctx.Statistics.ProgramTags++;
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "Tags", program);
                        }
                    });
                    break;
                case "Routines":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "Routine") program.Routines.Add(_routines.Read(rr, program));
                        else if (c is "EncodedData" or "EncryptedContent") program.EncodedRoutines.Add(_ctx.ReadGeneric(rr));
                        else _ctx.Unexpected(rr, "Routines", program);
                    });
                    break;
                case "ChildPrograms":
                    r.ForEachChild((rr, c) =>
                    {
                        if (c == "ChildProgram")
                        {
                            var n = rr.GetAttribute("Name");
                            if (n is not null) program.ChildProgramNames.Add(n);
                            rr.Skip();
                        }
                        else
                        {
                            _ctx.Unexpected(rr, "ChildPrograms", program);
                        }
                    });
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    r.Skip();
                    break;
                default:
                    _ctx.Unexpected(r, "Program", program);
                    break;
            }
        });
        _ctx.Statistics.Programs++;
        return program;
    }
}
