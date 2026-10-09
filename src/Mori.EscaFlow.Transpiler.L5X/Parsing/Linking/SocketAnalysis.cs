using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Linking;

/// <summary>
/// Binds socket-service messages to the request structures they send and locates, by data type, the fields the
/// Socket Object interprets (type, ports, addresses, destination, buffers). Produces paths only: the values are
/// run-time data that the generated code must read when the service executes.
/// </summary>
public static class SocketAnalysis
{
    public static void Run(Controller controller)
    {
        var resolver = new ScopeResolver(controller);
        var messages = new List<(Tag Tag, MessageConfiguration Msg)>();
        foreach (var tag in controller.AllTags)
        {
            var m = tag.Data.Message;
            if (m is null || !m.IsSocketService) continue;
            messages.Add((tag, m));
            var binding = new SocketBinding
            {
                Session = SessionOf(m),
                Request = m.LocalElement,
                Response = m.DestinationTag,
                Instance = tag.Name + ".Instance",
            };
            if (m.LocalElementPath is not null)
            {
                var r = resolver.Resolve(m.LocalElementPath, tag.Program ?? (Model.Programs.IRoutineContainer?)tag.Aoi);
                binding.RequestType = r.LeafType?.Name;
                if (r.LeafType is StructuredType st) LocateFields(m.SocketService, st, m.LocalElementPath.ToLogixText(), binding.Fields);
            }
            m.Socket = binding;
        }

        // A message without a request structure (DeleteSocket sends only the instance) belongs to the only socket
        // session declared in the same program or add-on definition, when there is exactly one.
        foreach (var group in messages.GroupBy(x => (object?)x.Tag.Program ?? x.Tag.Aoi))
        {
            var known = group.Select(x => x.Msg.Socket!.Session).Where(s => s is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (known.Count != 1 || known[0] is not { } session) continue;
            foreach (var (_, m) in group)
                if (m.Socket!.Session is null) m.Socket.Session = session;
        }
    }

    /// <summary>The request tag without its last member (<c>SNTP_MSG_DATA.Create_Source</c> → <c>SNTP_MSG_DATA</c>), or the tag itself.</summary>
    private static string? SessionOf(MessageConfiguration m)
    {
        var path = m.LocalElementPath;
        if (path is null) return m.LocalElement;
        if (path.Segments.Count == 0) return path.Root;
        var parent = new OperandPath(path.Root, path.Segments.Take(path.Segments.Count - 1).ToList(), path.ProgramName);
        return parent.ToLogixText();
    }

    /// <summary>Finds the conventional field names of the socket request structures (Rockwell ENET-AT002 layouts) by type, not by value.</summary>
    private static void LocateFields(SocketService service, StructuredType st, string basePath, Dictionary<string, string> fields)
    {
        void Add(string role, string? memberPath)
        {
            if (memberPath is not null) fields[role] = basePath + "." + memberPath;
        }

        Add("timeout", MemberNamed(st, "Timeout"));
        switch (service)
        {
            case SocketService.CreateSocket:
                Add("type", MemberNamed(st, "Type"));
                AddAddress(st, MemberContaining(st, "Addr"), "local");
                break;
            case SocketService.OpenConnection:
                Add("destination", MemberContaining(st, "Dest") ?? MemberContaining(st, "Addr"));
                break;
            case SocketService.ReadSocket:
                Add("bufferLength", MemberContaining(st, "BufLen") ?? MemberContaining(st, "Len"));
                break;
            case SocketService.WriteSocket:
                Add("buffer", MemberContaining(st, "Buf") ?? MemberContaining(st, "Data"));
                AddAddress(st, MemberNamed(st, "ToAddr") ?? MemberContaining(st, "Addr"), "to");
                break;
            case SocketService.AcceptConnection:
                Add("bufferLength", MemberContaining(st, "Len"));
                break;
        }

        void AddAddress(StructuredType owner, string? addrMember, string prefix)
        {
            if (addrMember is null) return;
            var member = owner.FindMember(addrMember);
            if (member?.DataType.Definition is StructuredType addr)
            {
                Add(prefix + "Family", Sub(addrMember, MemberNamed(addr, "Family")));
                Add(prefix + "Port", Sub(addrMember, MemberNamed(addr, "Port")));
                for (var i = 1; i <= 4; i++)
                {
                    var suffix = i switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => "4th" };
                    Add(prefix + "AddressOctet" + i, Sub(addrMember, addr.Members.FirstOrDefault(x => x.Name.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) && x.Name.Contains(suffix, StringComparison.OrdinalIgnoreCase))?.Name));
                }
                Add(prefix + "Address", Sub(addrMember, MemberNamed(addr, "Addr")));
            }
            else
            {
                // Flat layout: Family/Port/Addr directly on the request structure.
                Add(prefix + "Family", MemberNamed(owner, "Family"));
                Add(prefix + "Port", MemberNamed(owner, "Port"));
                Add(prefix + "Address", addrMember);
            }
        }
    }

    private static string? Sub(string parent, string? child) => child is null ? null : parent + "." + child;

    private static string? MemberNamed(StructuredType st, string name) => st.FindMember(name)?.Name;

    private static string? MemberContaining(StructuredType st, string part)
        => st.Members.FirstOrDefault(m => !m.Hidden && m.Name.Contains(part, StringComparison.OrdinalIgnoreCase))?.Name;
}
