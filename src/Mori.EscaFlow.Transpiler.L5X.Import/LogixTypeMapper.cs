using Mori.EscaFlow.Transpiler;
using Mori.EscaFlow.Transpiler.Logix;
using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Mori.EscaFlow.Transpiler.L5X.Model.Tags;
using Mori.EscaFlow.Transpiler.Catalogs;

namespace Mori.EscaFlow.Transpiler.L5X.Import;

/// <summary>Maps Logix data types onto neutral types: user structures, vendor (predefined) structures, module I/O types and string families.</summary>
/// <remarks>
/// The neutral type lists only what logic can address: BIT overlays appear as plain BOOL members and the hidden
/// integer hosts that store them move to the <c>vendor.logix</c> slot, where a tag server that must reproduce the
/// Logix byte image finds them again. Add-on backing structures are not emitted at all; an add-on instance is typed by
/// its POU, whose variables carry the same information.
/// </remarks>
public static class LogixTypeMapper
{
    /// <summary>A named type reference, wrapped in an array type when the source declares dimensions (Logix arrays always start at 0).</summary>
    public static IrTypeRef TypeRef(string name, IReadOnlyList<int>? dims = null)
    {
        var leaf = IrTypeRef.Named(name);
        return dims is { Count: > 0 } ? IrTypeRef.Array(leaf, dims) : leaf;
    }

    /// <summary>Builds the neutral type for a Logix definition; add-on backing types are not types in the IR (their instances are typed by the POU).</summary>
    public static IrType Build(DataTypeDefinition def)
    {
        switch (def)
        {
            case StructuredType s:
            {
                var ir = new IrType
                {
                    Name = s.Name,
                    // STRING and user string types are a LEN + DATA[n] pair in Logix; the neutral kind "string" carries the capacity.
                    Kind = s.IsStringFamily ? "string" : "struct",
                    // Neutral classes: firmware-supplied structures are "vendor", I/O images are "io"; add-on backing types are
                    // classified "user" for completeness although they are never emitted (see the class remarks).
                    Class = s.Class switch
                    {
                        DataTypeClass.Predefined => "vendor",
                        DataTypeClass.User => "user",
                        DataTypeClass.AddOn => "user",
                        DataTypeClass.ModuleDefined => "io",
                        DataTypeClass.Unresolved => "unresolved",
                        _ => "vendor",
                    },
                    Opaque = s.IsOpaque,
                    Description = s.Description,
                    Length = s.IsStringFamily ? s.FindMember("DATA")?.Dimension : null,
                    Members = new List<IrMember>(),
                };
                if (s.Class == DataTypeClass.Predefined)
                {
                    // TIMER and COUNTER are the instance structures of the standard blocks under the logix pin profile:
                    // TON/TOF/RTO share TIMER (PRE, ACC, EN, TT, DN), CTU/CTD share COUNTER. The binding lets a
                    // converter pick its timer runtime from the type alone.
                    if (s.Name.Equals("TIMER", StringComparison.OrdinalIgnoreCase)) ir.StdFb = new IrStdFbBinding { Fbs = { "TON", "TOF", "TONR" }, Profile = LogixVendor.Id };
                    else if (s.Name.Equals("COUNTER", StringComparison.OrdinalIgnoreCase)) ir.StdFb = new IrStdFbBinding { Fbs = { "CTU", "CTD" }, Profile = LogixVendor.Id };
                }
                var ext = new LogixTypeExt
                {
                    Class = s.Class switch
                    {
                        DataTypeClass.Predefined => "predefined",
                        DataTypeClass.User => "user",
                        DataTypeClass.AddOn => "addon",
                        DataTypeClass.ModuleDefined => "module",
                        DataTypeClass.Unresolved => "unresolved",
                        _ => "predefined",
                    },
                    Family = s.Family == DataTypeFamily.NoFamily ? null : s.Family.ToString(),
                    // Module-defined types encode vendor, product, data direction and index in their name
                    // (AB:1756_DI:I:0); the parsed descriptor is kept so a converter can tell inputs from outputs.
                    ModuleType = s.ModuleType is null ? null : new LogixModuleType
                    {
                        Vendor = s.ModuleType.Vendor,
                        Product = s.ModuleType.Product,
                        Direction = s.ModuleType.Kind switch
                        {
                            IoDataKind.Input => "input",
                            IoDataKind.Output => "output",
                            IoDataKind.Config => "config",
                            IoDataKind.SafetyInput => "safetyInput",
                            IoDataKind.SafetyOutput => "safetyOutput",
                            _ => "unknown",
                        },
                        Index = s.ModuleType.Index,
                        ThirdParty = s.ModuleType.IsThirdParty,
                    },
                    BackingFor = s.Class == DataTypeClass.AddOn ? s.Name : null,
                };
                ir.SetLogix(ext);
                foreach (var m in s.Members)
                {
                    if (m.Hidden)
                    {
                        // Hidden members are the SINT/DINT hosts Logix allocates for BIT members (ZZZZZZZZZZ...). Logic
                        // never names them, so they leave the neutral member list; their position is recorded so the
                        // layout calculator can rebuild the exact member order of the byte image.
                        ext.HiddenMembers ??= new List<LogixHiddenMember>();
                        ext.HiddenMembers.Add(new LogixHiddenMember { Name = m.Name, Type = m.DataType.Name, Dims = m.Dimension > 0 ? m.Dimension : null, Position = s.Members.IndexOf(m) });
                        continue;
                    }
                    var member = new IrMember
                    {
                        Name = m.Name,
                        // A BIT overlay member is a BOOL to the logic; the host and bit number go to the vendor slot below.
                        Type = TypeRef(m.IsBitOverlay ? "BOOL" : m.DataType.Name, m.Dimension > 0 ? new[] { m.Dimension } : null),
                        Description = m.Description,
                    };
                    // BOOL arrays inside structures are stored 32 bits per DINT in Logix; the flag tells layout
                    // calculators and tag servers not to allocate a byte per element.
                    var packed = m.Dimension > 0 && m.DataType.Name.Equals("BOOL", StringComparison.OrdinalIgnoreCase);
                    if (m.IsBitOverlay || m.Radix != Radix.Unknown || m.ExternalAccess is not null || packed)
                    {
                        member.SetLogix(new LogixMemberExt
                        {
                            BitHost = m.Target,
                            BitNumber = m.BitNumber,
                            Radix = m.Radix == Radix.Unknown ? null : m.Radix.ToString(),
                            ExternalAccess = m.ExternalAccess,
                            Packed = packed,
                        });
                    }
                    ir.Members.Add(member);
                }
                return ir;
            }
            default:
                // Only structured types reach the IR (atomics are implicit); anything else is a name the project
                // references without defining.
                return new IrType { Name = def.Name, Opaque = true, Class = "unresolved" };
        }
    }

    /// <summary>True for the IEC elementary types, which are never listed in the document.</summary>
    public static bool IsAtomic(string name) => AtomicTypes.IsAtomic(name);
}
