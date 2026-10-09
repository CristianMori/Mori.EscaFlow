using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Model.Modules;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>
/// Reads a materialized <c>&lt;Module&gt;</c> element: identity (catalog number, vendor, product type and code,
/// revision), the port it hangs off (<c>ParentModule</c> / <c>ParentModPortId</c>), its own ports, and the
/// communications block with the configuration image and the connections whose input and output images become
/// the <c>Module:I</c> / <c>Module:O</c> / <c>Module:C</c> tags of the project.
/// </summary>
public sealed class ModuleReader
{
    private readonly ReaderContext _ctx;
    private readonly TagReader _tags;

    public ModuleReader(ReaderContext ctx, TagReader tags)
    {
        _ctx = ctx;
        _tags = tags;
    }

    public Module Read(XElement element)
    {
        var attrs = new AttributeBag(element);
        var module = new Module
        {
            Name = attrs.TakeRequired("Name", "?"),
            CatalogNumber = attrs.Take("CatalogNumber"),
            Vendor = attrs.Take("Vendor"),
            ProductType = attrs.Take("ProductType"),
            ProductCode = attrs.Take("ProductCode"),
            Major = attrs.TakeInt("Major"),
            Minor = attrs.TakeInt("Minor"),
            ParentModule = attrs.Take("ParentModule"),
            ParentModPortId = attrs.TakeInt("ParentModPortId"),
            Inhibited = attrs.TakeBool("Inhibited"),
            MajorFault = attrs.TakeBool("MajorFault"),
            SafetyEnabled = attrs.TakeBoolNullable("SafetyEnabled"),
            SafetyNetwork = attrs.Take("SafetyNetwork"),
            SourceLine = element.Line(),
        };
        module.UnknownAttributes = attrs.Remaining();

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Description":
                    module.Description = child.OwnText();
                    break;
                case "EKey":
                    // Electronic keying (ExactMatch / CompatibleModule / Disabled): kept for round trips, no transpiler meaning.
                    module.EKey = GenericElementReader.Read(child);
                    break;
                case "Ports":
                    foreach (var p in child.Elements("Port")) module.Ports.Add(ReadPort(p));
                    break;
                case "Communications":
                    module.Communications = ReadCommunications(child, module.Name);
                    break;
                case "ExtendedProperties":
                    module.ExtendedProperties = GenericElementReader.Read(child);
                    break;
                case "CustomProperties":
                    _ctx.CountSkipped();
                    break;
                default:
                    (module.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
                    break;
            }
        }
        return module;
    }

    /// <summary>A port: the upstream one is the slot/address the module sits at in its parent; downstream ports carry a bus that children attach to.</summary>
    private static ModulePort ReadPort(XElement p)
    {
        var attrs = new AttributeBag(p);
        var port = new ModulePort
        {
            Id = attrs.TakeInt("Id") ?? 0,
            Address = attrs.Take("Address"),
            Type = attrs.Take("Type"),
            Upstream = attrs.TakeBool("Upstream"),
            Width = attrs.TakeInt("Width"),
            SourceLine = p.Line(),
        };
        port.UnknownAttributes = attrs.Remaining();
        var bus = p.Element("Bus");
        if (bus is not null) port.Bus = GenericElementReader.Read(bus);
        return port;
    }

    /// <summary>Configuration image (ConfigTag or the older ConfigData), connections and scripts.</summary>
    private ModuleCommunications ReadCommunications(XElement c, string moduleName)
    {
        var attrs = new AttributeBag(c);
        var comm = new ModuleCommunications
        {
            CommMethod = attrs.Take("CommMethod"),
            PrimCxnInputSize = attrs.TakeInt("PrimCxnInputSize"),
            PrimCxnOutputSize = attrs.TakeInt("PrimCxnOutputSize"),
            SourceLine = c.Line(),
        };
        comm.UnknownAttributes = attrs.Remaining();
        foreach (var child in c.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "ConfigTag":
                    comm.ConfigTag = ReadTagData(child, moduleName + ":C");
                    break;
                case "ConfigData":
                    comm.ConfigData = ReadTagData(child, moduleName + ":C");
                    break;
                case "ConfigScript":
                    comm.ConfigScript = GenericElementReader.Read(child);
                    break;
                case "Connections":
                    foreach (var conn in child.Elements())
                    {
                        if (conn.Name.LocalName == "Connection") comm.Connections.Add(ReadConnection(conn, moduleName));
                        // A rack-optimized chassis connection: the children's images live inside the parent's connection.
                        else if (conn.Name.LocalName == "RackConnection") comm.RackConnection = GenericElementReader.Read(conn);
                        else (comm.UnknownChildren ??= new()).Add(GenericElementReader.Read(conn));
                    }
                    break;
                case "SafetyScript":
                    comm.SafetyScript = GenericElementReader.Read(child);
                    break;
                default:
                    (comm.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
                    break;
            }
        }
        return comm;
    }

    /// <summary>One connection with its RPI and its input / output images; each image is typed by the Decorated data it carries.</summary>
    private ModuleConnection ReadConnection(XElement c, string moduleName)
    {
        var attrs = new AttributeBag(c);
        var conn = new ModuleConnection
        {
            Name = attrs.TakeRequired("Name", "?"),
            Rpi = attrs.TakeInt("RPI"),
            Type = attrs.Take("Type"),
            EventId = attrs.TakeInt("EventID"),
            Unicast = attrs.TakeBoolNullable("Unicast"),
            ProgrammaticallySendEventTrigger = attrs.TakeBoolNullable("ProgrammaticallySendEventTrigger"),
            SourceLine = c.Line(),
        };
        conn.UnknownAttributes = attrs.Remaining();
        foreach (var child in c.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "InputTag": conn.InputTag = ReadTagData(child, moduleName + ":I"); break;
                case "OutputTag": conn.OutputTag = ReadTagData(child, moduleName + ":O"); break;
                default: (conn.UnknownChildren ??= new()).Add(GenericElementReader.Read(child)); break;
            }
        }
        return conn;
    }

    /// <summary>
    /// A module image: the Data children (whose Decorated structure names the module-defined type, e.g.
    /// <c>AB:1756_DI:I:0</c>) plus per-member comments. The type is learned from this data later, never predefined.
    /// </summary>
    private ModuleTagData ReadTagData(XElement e, string tagName)
    {
        var attrs = new AttributeBag(e);
        var t = new ModuleTagData
        {
            ExternalAccess = attrs.Take("ExternalAccess"),
            ConfigSize = attrs.TakeInt("ConfigSize"),
            SourceLine = e.Line(),
        };
        t.UnknownAttributes = attrs.Remaining();
        var data = _tags.ReadDataFromElement(e, tagName, null);
        TagReader.FinishData(data, null);
        t.Data = data;
        var comments = e.Element("Comments");
        if (comments is not null)
        {
            foreach (var c in comments.Elements("Comment"))
            {
                t.Comments.Add(new Model.Tags.TagComment { Operand = c.Attr("Operand") ?? "", Text = c.OwnText() });
            }
        }
        foreach (var child in e.Elements())
        {
            var n = child.Name.LocalName;
            if (n is "Data" or "Comments") continue;
            if (n == "CustomProperties")
            {
                _ctx.CountSkipped();
                continue;
            }
            (t.UnknownChildren ??= new()).Add(GenericElementReader.Read(child));
        }
        return t;
    }
}
