using L5x.Model;
using L5x.Model.DataTypes;
using L5x.Model.Tags;
using Plc.Ir.Catalogs;

namespace Plc.Ir.Logix.Build;

/// <summary>Maps Logix data types onto neutral types: user structures, vendor (predefined) structures, module I/O types and string families.</summary>
public static class LogixTypeMapper
{
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
                    Kind = s.IsStringFamily ? "string" : "struct",
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
                    if (s.Name.Equals("TIMER", StringComparison.OrdinalIgnoreCase)) ir.StdFb = new IrStdFbBinding { Fbs = { "TON", "TOF", "TONR" }, Profile = Logix.Id };
                    else if (s.Name.Equals("COUNTER", StringComparison.OrdinalIgnoreCase)) ir.StdFb = new IrStdFbBinding { Fbs = { "CTU", "CTD" }, Profile = Logix.Id };
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
                        ext.HiddenMembers ??= new List<LogixHiddenMember>();
                        ext.HiddenMembers.Add(new LogixHiddenMember { Name = m.Name, Type = m.DataType.Name, Dims = m.Dimension > 0 ? m.Dimension : null, Position = s.Members.IndexOf(m) });
                        continue;
                    }
                    var member = new IrMember
                    {
                        Name = m.Name,
                        Type = TypeRef(m.IsBitOverlay ? "BOOL" : m.DataType.Name, m.Dimension > 0 ? new[] { m.Dimension } : null),
                        Description = m.Description,
                    };
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
                return new IrType { Name = def.Name, Opaque = true, Class = "unresolved" };
        }
    }

    public static bool IsAtomic(string name) => AtomicTypes.IsAtomic(name);
}
