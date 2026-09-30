using L5x.Model;
using L5x.Model.DataTypes;
using Xunit;

namespace L5x.Tests.Values;

public class BuiltInTypesTests
{
    [Fact]
    public void RegistryContainsAtomics()
    {
        var reg = new DataTypeRegistry();
        BuiltInTypes.RegisterInto(reg);
        foreach (var name in new[] { "BOOL", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT", "REAL", "LREAL", "BIT" })
        {
            var t = Assert.IsType<AtomicType>(reg.TryGet(name));
            Assert.Equal(DataTypeClass.Atomic, t.Class);
        }
        Assert.Equal(AtomicKind.Dint, ((AtomicType)reg.TryGet("dint")!).Kind);
    }

    [Fact]
    public void TimerHasFiveMembers()
    {
        var reg = new DataTypeRegistry();
        BuiltInTypes.RegisterInto(reg);
        var timer = Assert.IsType<StructuredType>(reg.TryGet("TIMER"));
        Assert.Equal(DataTypeClass.Predefined, timer.Class);
        Assert.False(timer.IsOpaque);
        Assert.Equal(new[] { "PRE", "ACC", "EN", "TT", "DN" }, timer.VisibleMembers.Select(m => m.Name).ToArray());
        Assert.Equal(6, timer.Members.Count);
        Assert.True(timer.Members[0].Hidden);
        Assert.Equal("DINT", timer.Members[1].DataType.Name);
        var dn = timer.FindMember("DN")!;
        Assert.Equal("BIT", dn.DataType.Name);
        Assert.Equal(timer.Members[0].Name, dn.Target);
        Assert.Equal(29, dn.BitNumber);
    }

    [Fact]
    public void StringIsStringFamilyWithData82()
    {
        var reg = new DataTypeRegistry();
        BuiltInTypes.RegisterInto(reg);
        var s = Assert.IsType<StructuredType>(reg.TryGet("STRING"));
        Assert.Equal(DataTypeFamily.StringFamily, s.Family);
        var data = s.FindMember("DATA")!;
        Assert.Equal(82, data.Dimension);
        Assert.Equal("SINT", data.DataType.Name);
        Assert.Equal(Radix.Ascii, data.Radix);
        Assert.Equal("DINT", s.FindMember("LEN")!.DataType.Name);
    }

    [Fact]
    public void AxisTypesAreOpaque()
    {
        var reg = new DataTypeRegistry();
        BuiltInTypes.RegisterInto(reg);
        var axis = Assert.IsType<StructuredType>(reg.TryGet("AXIS_CIP_DRIVE"));
        Assert.True(axis.IsOpaque);
        Assert.True(((StructuredType)reg.TryGet("MOTION_GROUP")!).IsOpaque);
        Assert.True(((StructuredType)reg.TryGet("COORDINATE_SYSTEM")!).IsOpaque);
    }

    [Fact]
    public void RegisterIntoDoesNotOverrideUserTypes()
    {
        var reg = new DataTypeRegistry();
        var user = new StructuredType { Name = "TIMER", Class = DataTypeClass.User };
        reg.Register(user);
        BuiltInTypes.RegisterInto(reg);
        Assert.Same(user, reg.TryGet("TIMER"));
    }

    [Fact]
    public void AllNamesAreUnique()
    {
        var names = BuiltInTypes.All.Select(t => t.Name.ToUpperInvariant()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains("PID", names);
        Assert.Contains("COUNTER", names);
        Assert.Contains("CONTROL", names);
        Assert.Contains("MESSAGE", names);
        Assert.Contains("SFC_STEP", names);
    }
}
