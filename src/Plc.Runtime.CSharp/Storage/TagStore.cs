using System.Globalization;
using System.Text;
using EthernetIPSharp.Cip;
using EthernetIPSharp.Logix;

namespace Plc.Runtime;

/// <summary>A tag as the store defined it.</summary>
public sealed record TagEntry(string Path, Tag Tag, string TypeName, int[] Dims, string? Program, PlcLayout? Layout);

/// <summary>
/// The controller's tag memory: an EthernetIPSharp <see cref="TagDatabase"/> with layouts registered as templates,
/// tags defined by Logix path, and text-level access by path for hosts.
/// </summary>
public sealed class TagStore
{
    private const string ProgramPrefix = "Program:";

    private readonly Dictionary<string, TagEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TemplateDefinition> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ushort, PlcLayout> _layoutsById = new();
    private readonly Dictionary<string, PlcLayout> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private ushort _nextTemplateId = 0x100;

    public TagStore(TagDatabase database) => Database = database;

    public TagDatabase Database { get; }

    /// <summary>Notes about definitions that could not be represented exactly (flattened arrays and the like).</summary>
    public List<string> Warnings { get; } = new();

    public IEnumerable<TagEntry> Entries => _entries.Values;

    // ------------------------------------------------------------------ layouts

    /// <summary>Registers a structure layout as a template with the exact offsets; nested layouts must be registered first. Idempotent by name.</summary>
    public TemplateDefinition RegisterLayout(PlcLayout layout)
    {
        if (_templates.TryGetValue(layout.Name, out var existing)) return existing;
        var members = new List<TemplateMemberInfo>(layout.Members.Length);
        foreach (var m in layout.Members)
        {
            if (m.IsStructure)
            {
                if (m.TemplateName is null || !_templates.TryGetValue(m.TemplateName, out var nested))
                    throw new InvalidOperationException($"Layout {layout.Name}: member {m.Name} references layout '{m.TemplateName}' which is not registered yet");
                members.Add(new TemplateMemberInfo(m.Name, (ushort)(0x8000 | nested.InstanceId), m.Offset, m.IsArray ? m.ElementCount : 0, (int)nested.StructureSize));
            }
            else if (m.IsBool && !m.IsArray)
            {
                members.Add(new TemplateMemberInfo(m.Name, LogixDataTypes.BOOL, m.Offset, m.Bit, 0));
            }
            else if (m.IsBool || m.PackedBools)
            {
                // Packed BOOL arrays are presented as the DWORDs that hold them.
                members.Add(new TemplateMemberInfo(m.Name, LogixDataTypes.DWORD, m.Offset, (m.ElementCount + 31) / 32, 4));
            }
            else
            {
                var size = m.ElementSize > 0 ? m.ElementSize : Math.Max(1, LogixDataTypes.GetElementSize(m.TypeCode));
                members.Add(new TemplateMemberInfo(m.Name, m.TypeCode, m.Offset, m.IsArray ? m.ElementCount : 0, size));
            }
        }
        var id = _nextTemplateId++;
        var template = new TemplateDefinition(id, layout.Name, (ushort)(0x8000 | id), (uint)layout.Size, members.ToArray());
        Database.AddTemplate(template);
        _templates[layout.Name] = template;
        _layouts[layout.Name] = layout;
        _layoutsById[id] = layout;
        return template;
    }

    public TemplateDefinition? FindTemplate(string layoutName) => _templates.GetValueOrDefault(layoutName);
    public PlcLayout? FindLayout(string name) => _layouts.GetValueOrDefault(name);

    // ------------------------------------------------------------------ definitions

    /// <summary>Defines (or returns) an atomic tag: <c>Tag</c>, <c>Program:P.Tag</c> or a module image such as <c>DI:I</c>.</summary>
    public Tag Define(string logixPath, ushort atomicType, int[] dims)
    {
        if (_entries.TryGetValue(logixPath, out var existing)) return existing.Tag;
        var (program, name) = Split(logixPath);
        var scope = program is null ? null : Database.RegisterProgram(program);
        Tag tag;
        if (dims.Length == 0)
        {
            tag = scope is null ? Database.AddTag(name, atomicType) : scope.AddTag(name, atomicType);
        }
        else if (dims.Length == 1)
        {
            var count = dims[0];
            if (atomicType == LogixDataTypes.BOOL) count = (count + 31) / 32 * 32;
            tag = scope is null ? Database.AddTag(name, atomicType, count) : scope.AddTag(name, atomicType, count);
        }
        else if (scope is null)
        {
            tag = Database.AddTag(name, atomicType, dims.Select(d => (uint)d).ToArray());
        }
        else
        {
            var count = 1;
            foreach (var d in dims) count *= d;
            tag = scope.AddTag(name, atomicType, count);
            Warnings.Add($"{logixPath}: program-scoped multi-dimensional array flattened to [{count}] for the tag server");
        }
        _entries[logixPath] = new TagEntry(logixPath, tag, AtomicName(atomicType), dims, program, null);
        return tag;
    }

    /// <summary>Defines (or returns) a structured tag backed by a layout.</summary>
    public Tag Define(string logixPath, PlcLayout layout, int[] dims)
    {
        if (_entries.TryGetValue(logixPath, out var existing)) return existing.Tag;
        var template = RegisterLayout(layout);
        var (program, name) = Split(logixPath);
        var scope = program is null ? null : Database.RegisterProgram(program);
        Tag tag;
        if (dims.Length == 0)
        {
            tag = scope is null ? Database.AddTag(name, template) : scope.AddTag(name, template);
        }
        else if (dims.Length == 1)
        {
            tag = scope is null ? Database.AddTag(name, template, dims[0]) : scope.AddTag(name, template, dims[0]);
        }
        else if (scope is null)
        {
            tag = Database.AddTag(name, template, dims.Select(d => (uint)d).ToArray());
        }
        else
        {
            var count = 1;
            foreach (var d in dims) count *= d;
            tag = scope.AddTag(name, template, count);
            Warnings.Add($"{logixPath}: program-scoped multi-dimensional array flattened to [{count}] for the tag server");
        }
        _entries[logixPath] = new TagEntry(logixPath, tag, layout.Name, dims, program, layout);
        return tag;
    }

    public TagEntry? Find(string logixPath) => _entries.GetValueOrDefault(logixPath);

    private static (string? Program, string Name) Split(string logixPath)
    {
        if (logixPath.StartsWith(ProgramPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var dot = logixPath.IndexOf('.');
            if (dot > ProgramPrefix.Length) return (logixPath[ProgramPrefix.Length..dot], logixPath[(dot + 1)..]);
        }
        return (null, logixPath);
    }

    // ------------------------------------------------------------------ enumeration

    public IEnumerable<TagInfo> Tags(string? filter = null)
    {
        foreach (var e in _entries.Values.OrderBy(e => e.Program ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (filter is { Length: > 0 } && !e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new TagInfo(e.Path, e.TypeName, e.Dims, e.Program, null);
        }
    }

    // ------------------------------------------------------------------ text access

    /// <summary>Reads a value by Logix path (members, elements, bits, whole arrays and structures as text).</summary>
    public bool TryRead(string path, out TagValueText? value)
    {
        value = null;
        if (!TryResolve(path, out var r, out _)) return false;
        var text = r.Dims is not null ? FormatArray(r) : FormatValue(r.Tag, r.Offset, r.Type, r.ElementSize, r.Bit, r.Template, 0);
        value = new TagValueText(path, TypeNameOf(r), text);
        return true;
    }

    /// <summary>Writes a value given as text.</summary>
    public bool TryWrite(string path, string text, out string? error)
    {
        if (!TryResolve(path, out var r, out error)) return false;
        try
        {
            if (r.Dims is not null)
            {
                error = "whole-array writes are not supported; write an element";
                return false;
            }
            if (r.Bit is { } bit)
            {
                r.Tag.AtomicSetBit(r.Offset, bit, PlcConvert.ToBool(text));
                return true;
            }
            if (r.Template is not null)
            {
                if (IsStringTemplate(r.Template))
                {
                    WriteString(r.Tag, r.Offset, r.Template, text);
                    return true;
                }
                error = "structure writes are not supported; write a member";
                return false;
            }
            switch (r.Type & 0x00FF)
            {
                case 0xC1: r.Tag.Write<byte>(r.Offset, PlcConvert.ToBool(text) ? (byte)1 : (byte)0); break;
                case 0xC2: r.Tag.Write(r.Offset, unchecked((sbyte)PlcConvert.ToInt64(text))); break;
                case 0xC3: r.Tag.Write(r.Offset, unchecked((short)PlcConvert.ToInt64(text))); break;
                case 0xC4: r.Tag.Write(r.Offset, unchecked((int)PlcConvert.ToInt64(text))); break;
                case 0xC5: r.Tag.Write(r.Offset, PlcConvert.ToInt64(text)); break;
                case 0xCA: r.Tag.Write(r.Offset, (float)PlcConvert.ToDouble(text)); break;
                case 0xCB: r.Tag.Write(r.Offset, PlcConvert.ToDouble(text)); break;
                case 0xD3: r.Tag.Write(r.Offset, unchecked((uint)PlcConvert.ToInt64(text))); break;
                default:
                    error = $"cannot write type 0x{r.Type:X4}";
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    private readonly record struct Resolved(Tag Tag, int Offset, ushort Type, int ElementSize, int? Bit, TemplateDefinition? Template, int[]? Dims, bool BoolArray);

    private bool TryResolve(string path, out Resolved result, out string? error)
    {
        result = default;
        error = null;
        var (program, rest) = Split(path);
        var scope = program is null ? null : Database.FindProgram(program);
        if (program is not null && scope is null)
        {
            error = $"program '{program}' not found";
            return false;
        }
        var end = rest.IndexOfAny(new[] { '.', '[' });
        var rootName = end < 0 ? rest : rest[..end];
        var root = scope is null ? Database.FindByName(rootName) : scope.FindByName(rootName);
        if (root is null)
        {
            error = $"tag '{rootName}' not found";
            return false;
        }
        var segments = new List<CipPathSegment>();
        int? bit = null;
        var i = end < 0 ? rest.Length : end;
        while (i < rest.Length)
        {
            if (rest[i] == '.')
            {
                var j = i + 1;
                while (j < rest.Length && rest[j] != '.' && rest[j] != '[') j++;
                var name = rest[(i + 1)..j];
                if (name.Length > 0 && name.All(char.IsDigit) && j == rest.Length)
                {
                    bit = int.Parse(name, CultureInfo.InvariantCulture);
                }
                else
                {
                    segments.Add(new SymbolicPathSegment(name));
                }
                i = j;
            }
            else if (rest[i] == '[')
            {
                var close = rest.IndexOf(']', i);
                if (close < 0)
                {
                    error = "unterminated index";
                    return false;
                }
                foreach (var part in rest[(i + 1)..close].Split(','))
                {
                    if (!uint.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    {
                        error = $"bad index '{part}'";
                        return false;
                    }
                    segments.Add(new ElementPathSegment(index));
                }
                i = close + 1;
            }
            else
            {
                error = $"unexpected character '{rest[i]}'";
                return false;
            }
        }

        if (TagPathWalker.TryWalk(root, segments, id => Database.FindTemplate(id), out var walk, out var walkError))
        {
            var template = walk.Template;
            if (template is null && LogixDataTypes.IsStruct(walk.TypeCode)) template = Database.FindTemplate(LogixDataTypes.GetTemplateId(walk.TypeCode));
            if (bit is { } b)
            {
                if (walk.BitPos.HasValue || template is not null || walk.ElementSize <= 0)
                {
                    error = "bit access on a non-integer";
                    return false;
                }
                if (b < 0 || b >= walk.ElementSize * 8)
                {
                    error = $"bit {b} outside a {walk.ElementSize * 8}-bit value";
                    return false;
                }
                result = new Resolved(root, walk.Offset + b / 8, LogixDataTypes.BOOL, 1, b % 8, null, null, false);
                return true;
            }
            result = new Resolved(root, walk.Offset, walk.TypeCode, walk.ElementSize, walk.BitPos, template, null, false);
            return true;
        }

        // The walker refuses an unindexed array; resolve its shape ourselves for whole-array reads.
        if (bit is null && TryResolveArray(root, segments, out result)) return true;
        error = walkError;
        return false;
    }

    private bool TryResolveArray(Tag root, List<CipPathSegment> segments, out Resolved result)
    {
        result = default;
        var offset = 0;
        var type = root.TagType;
        var elementSize = root.ElementSize;
        var template = LogixDataTypes.IsStruct(root.SymbolType) ? Database.FindTemplate(LogixDataTypes.GetTemplateId(root.SymbolType)) : null;
        int[]? dims = root.Dims.Count > 0 ? root.Dims.Select(d => (int)d).ToArray() : null;
        var pending = 0;
        long running = 0;
        var boolArray = root.TagType == LogixDataTypes.BOOL && root.Dims.Count > 0;
        foreach (var seg in segments)
        {
            switch (seg)
            {
                case ElementPathSegment el:
                    if (dims is null) return false;
                    running = running * dims[pending] + el.Index;
                    pending++;
                    if (pending == dims.Length)
                    {
                        offset += boolArray ? (int)(running / 8) : (int)(running * elementSize);
                        dims = null;
                        pending = 0;
                        running = 0;
                    }
                    break;
                case SymbolicPathSegment sym:
                    if (dims is not null || template is null) return false;
                    var member = template.Members.FirstOrDefault(m => m.Name.Equals(sym.Name, StringComparison.OrdinalIgnoreCase));
                    if (member.Name is null) return false;
                    offset += member.Offset;
                    type = member.DataType;
                    boolArray = false;
                    if (LogixDataTypes.IsStruct(type))
                    {
                        template = Database.FindTemplate(LogixDataTypes.GetTemplateId(type));
                        if (template is null) return false;
                        elementSize = (int)template.StructureSize;
                    }
                    else
                    {
                        template = null;
                        elementSize = member.ElementSize > 0 ? member.ElementSize : LogixDataTypes.GetElementSize(type);
                    }
                    dims = member.ArraySize > 0 ? new[] { member.ArraySize } : null;
                    break;
            }
        }
        if (dims is null) return false;
        result = new Resolved(root, offset, type, elementSize, null, template, dims, boolArray);
        return true;
    }

    private string TypeNameOf(Resolved r)
    {
        var name = r.Bit is not null ? "BOOL" : r.Template?.Name ?? AtomicName(r.Type);
        return r.Dims is null ? name : $"{name}[{string.Join(",", r.Dims)}]";
    }

    private string FormatArray(Resolved r)
    {
        var dims = r.Dims!;
        var count = 1;
        foreach (var d in dims) count *= d;
        var sb = new StringBuilder("[");
        var shown = Math.Min(count, 64);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) sb.Append(", ");
            if (r.BoolArray)
                sb.Append((r.Tag.Read<byte>(r.Offset + i / 8) & (1 << (i % 8))) != 0 ? '1' : '0');
            else
                sb.Append(FormatValue(r.Tag, r.Offset + i * r.ElementSize, r.Type, r.ElementSize, null, r.Template, 1));
        }
        if (shown < count) sb.Append(", ...");
        return sb.Append(']').ToString();
    }

    private string FormatValue(Tag tag, int offset, ushort type, int elementSize, int? bit, TemplateDefinition? template, int depth)
    {
        if (bit is { } b) return (tag.Read<byte>(offset) & (1 << b)) != 0 ? "1" : "0";
        if (template is not null)
        {
            if (IsStringTemplate(template)) return ReadString(tag, offset, template);
            if (depth > 3) return "{...}";
            var sb = new StringBuilder("{");
            var first = true;
            foreach (var m in template.Members)
            {
                if (m.Name.StartsWith("ZZZZZZZZZZ", StringComparison.Ordinal)) continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(m.Name).Append(": ");
                var nested = LogixDataTypes.IsStruct(m.DataType) ? Database.FindTemplate(LogixDataTypes.GetTemplateId(m.DataType)) : null;
                if (m.ArraySize > 0 && !(m.DataType == LogixDataTypes.BOOL && m.ElementSize == 0))
                {
                    var n = Math.Min(m.ArraySize, 16);
                    sb.Append('[');
                    for (var i = 0; i < n; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(FormatValue(tag, offset + m.Offset + i * m.ElementSize, m.DataType, m.ElementSize, null, nested, depth + 1));
                    }
                    if (n < m.ArraySize) sb.Append(", ...");
                    sb.Append(']');
                }
                else if (m.DataType == LogixDataTypes.BOOL && m.ElementSize == 0)
                {
                    sb.Append(FormatValue(tag, offset + m.Offset, m.DataType, 1, m.ArraySize, null, depth + 1));
                }
                else
                {
                    sb.Append(FormatValue(tag, offset + m.Offset, m.DataType, m.ElementSize, null, nested, depth + 1));
                }
            }
            return sb.Append('}').ToString();
        }
        return (type & 0x00FF) switch
        {
            0xC1 => tag.Read<byte>(offset) != 0 ? "1" : "0",
            0xC2 => tag.Read<sbyte>(offset).ToString(CultureInfo.InvariantCulture),
            0xC3 => tag.Read<short>(offset).ToString(CultureInfo.InvariantCulture),
            0xC4 => tag.Read<int>(offset).ToString(CultureInfo.InvariantCulture),
            0xC5 => tag.Read<long>(offset).ToString(CultureInfo.InvariantCulture),
            0xCA => tag.Read<float>(offset).ToString("R", CultureInfo.InvariantCulture),
            0xCB => tag.Read<double>(offset).ToString("R", CultureInfo.InvariantCulture),
            0xD3 => "16#" + tag.Read<uint>(offset).ToString("X8", CultureInfo.InvariantCulture),
            _ => "?",
        };
    }

    private static bool IsStringTemplate(TemplateDefinition t)
        => t.Members.Count >= 2
           && t.Members[0].Name.Equals("LEN", StringComparison.OrdinalIgnoreCase) && t.Members[0].DataType == LogixDataTypes.DINT
           && t.Members[1].Name.Equals("DATA", StringComparison.OrdinalIgnoreCase) && t.Members[1].DataType == LogixDataTypes.SINT && t.Members[1].ArraySize > 0;

    private static string ReadString(Tag tag, int offset, TemplateDefinition t)
    {
        var capacity = t.Members[1].ArraySize;
        var len = Math.Clamp(tag.Read<int>(offset), 0, capacity);
        return Encoding.Latin1.GetString(tag.GetData(offset + 4, len));
    }

    private static void WriteString(Tag tag, int offset, TemplateDefinition t, string text)
    {
        if (text.Length >= 2 && ((text[0] == '\'' && text[^1] == '\'') || (text[0] == '"' && text[^1] == '"'))) text = text[1..^1];
        var capacity = t.Members[1].ArraySize;
        var bytes = Encoding.Latin1.GetBytes(text);
        var len = Math.Min(bytes.Length, capacity);
        var buffer = new byte[4 + capacity];
        BitConverter.TryWriteBytes(buffer, len);
        bytes.AsSpan(0, len).CopyTo(buffer.AsSpan(4));
        tag.SetData(buffer, offset);
    }

    public static string AtomicName(ushort type) => (type & 0x00FF) switch
    {
        0xC1 => "BOOL",
        0xC2 => "SINT",
        0xC3 => "INT",
        0xC4 => "DINT",
        0xC5 => "LINT",
        0xCA => "REAL",
        0xCB => "LREAL",
        0xD3 => "DWORD",
        _ => $"0x{type:X4}",
    };

    // ------------------------------------------------------------------ snapshots

    public TagDatabasePersistence.LoadResult? LastLoad { get; private set; }

    public void SaveSnapshot(Stream stream) => TagDatabasePersistence.Save(Database, stream);

    public void LoadSnapshot(Stream stream) => LastLoad = TagDatabasePersistence.Load(Database, stream);
}
