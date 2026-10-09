using Mori.EscaFlow.Transpiler.L5X.Model;
using Mori.EscaFlow.Transpiler.L5X.Model.DataTypes;
using Xunit;
using Xunit.Abstractions;

namespace Mori.EscaFlow.Transpiler.L5X.Tests.Values;

/// <summary>
/// Proves the embedded PredefinedTypes.txt resource is internally consistent: every member's declared type
/// resolves to something registered, specific known members have the expected type, and no type name is
/// declared twice (which would silently let the second definition win).
/// </summary>
public class PredefinedTypesResourceTests
{
    private readonly ITestOutputHelper _out;

    public PredefinedTypesResourceTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void EveryMemberTypeIsKnown()
    {
        var registry = new DataTypeRegistry();
        BuiltInTypes.RegisterInto(registry);
        var problems = new List<string>();
        var structs = 0;
        var members = 0;
        foreach (var def in BuiltInTypes.All.OfType<StructuredType>())
        {
            structs++;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in def.Members)
            {
                members++;
                if (!seen.Add(m.Name)) problems.Add($"{def.Name}: duplicate member {m.Name}");
                if (registry.TryGet(m.DataType.Name) is null) problems.Add($"{def.Name}.{m.Name}: unknown type {m.DataType.Name}");
                if (m.Target is not null && def.FindMember(m.Target) is null) problems.Add($"{def.Name}.{m.Name}: bit host {m.Target} missing");
                if (m.Name.Length == 0 || !(char.IsLetter(m.Name[0]) || m.Name[0] == '_')) problems.Add($"{def.Name}: bad member name '{m.Name}'");
            }
        }
        _out.WriteLine($"{structs} predefined structures, {members} members");
        foreach (var p in problems.Take(50)) _out.WriteLine(p);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("AXIS_CIP_DRIVE", "ActualPosition", "REAL")]
    [InlineData("AXIS_CIP_DRIVE", "CIPAxisState", "INT")]
    [InlineData("AXIS_CIP_DRIVE", "MotionFault", "BOOL")]
    [InlineData("AXIS_CIP_DRIVE", "MechanicalBrakeOutputStatus", "BOOL")]
    [InlineData("AXIS_VIRTUAL", "ActualPosition", "REAL")]
    [InlineData("MOTION_INSTRUCTION", "DN", "BOOL")]
    [InlineData("MOTION_INSTRUCTION", "ERR", "INT")]
    [InlineData("MOTION_GROUP", "GroupSynced", "BOOL")]
    [InlineData("COORDINATE_SYSTEM", "ActualPosition", "REAL")]
    [InlineData("CAM", "Master", "REAL")]
    [InlineData("MESSAGE", "DN", "BOOL")]
    [InlineData("MESSAGE", "ERR", "INT")]
    [InlineData("ALARM_ANALOG", "HHInAlarm", "BOOL")]
    [InlineData("ALARM_ANALOG", "HHLimit", "REAL")]
    [InlineData("ALARM_DIGITAL", "InAlarm", "BOOL")]
    [InlineData("PID", "SP", "REAL")]
    [InlineData("SFC_STEP", "X", "BOOL")]
    [InlineData("SFC_ACTION", "Q", "BOOL")]
    [InlineData("CONNECTION_STATUS", "ConnectionFaulted", "BOOL")]
    [InlineData("FBD_TIMER", "PRE", "DINT")]
    [InlineData("PHASE", "Running", "BOOL")]
    public void KnownMembersHaveExpectedTypes(string type, string member, string expectedType)
    {
        var def = Assert.IsType<StructuredType>(BuiltInTypes.Find(type));
        var m = def.FindMember(member);
        Assert.NotNull(m);
        Assert.Equal(expectedType, m!.DataType.Name);
    }

    [Fact]
    public void NoDuplicateTypeNames()
    {
        var dupes = BuiltInTypes.All.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }
}
