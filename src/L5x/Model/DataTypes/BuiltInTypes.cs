namespace L5x.Model.DataTypes;

/// <summary>
/// The atomic and predefined (controller-supplied) data types. Member lists are described in a compact DSL:
/// <c>NAME: member:TYPE, member:TYPE[n], ...</c>. Types whose layout is not reliably known are marked opaque.
/// </summary>
public static class BuiltInTypes
{
    private static readonly Lazy<IReadOnlyList<DataTypeDefinition>> _all = new(Build);

    public static IReadOnlyList<DataTypeDefinition> All => _all.Value;

    public static void RegisterInto(DataTypeRegistry registry)
    {
        foreach (var t in All) registry.RegisterIfAbsent(t);
    }

    public static DataTypeDefinition? Find(string name)
    {
        foreach (var t in All)
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    // Structures with known member lists.
    // Format: "[?]NAME: m:TYPE, m:TYPE[dim], m:TYPE@Radix, *hidden:DINT, bit:BIT(host.n)"
    //   ?      the member list is known but the L5K packing is not: the type is marked opaque so the
    //          L5K shaper falls back to best effort instead of guessing.
    //   *name  a hidden member (a bit host that Decorated data does not show).
    //   BIT(host.n) a BOOL overlaid on bit n of the hidden host.
    private static readonly string[] StructureDsl =
    {
        "STRING: LEN:DINT, DATA:SINT[82]@Ascii",
        "TIMER: *ZZZZZZZZZZTIMER0:DINT, PRE:DINT, ACC:DINT, EN:BIT(ZZZZZZZZZZTIMER0.31), TT:BIT(ZZZZZZZZZZTIMER0.30), DN:BIT(ZZZZZZZZZZTIMER0.29)",
        "COUNTER: *ZZZZZZZZZZCOUNTER0:DINT, PRE:DINT, ACC:DINT, CU:BIT(ZZZZZZZZZZCOUNTER0.31), CD:BIT(ZZZZZZZZZZCOUNTER0.30), DN:BIT(ZZZZZZZZZZCOUNTER0.29), OV:BIT(ZZZZZZZZZZCOUNTER0.28), UN:BIT(ZZZZZZZZZZCOUNTER0.27)",
        "CONTROL: *ZZZZZZZZZZCONTROL0:DINT, LEN:DINT, POS:DINT, EN:BIT(ZZZZZZZZZZCONTROL0.31), EU:BIT(ZZZZZZZZZZCONTROL0.30), DN:BIT(ZZZZZZZZZZCONTROL0.29), EM:BIT(ZZZZZZZZZZCONTROL0.28), ER:BIT(ZZZZZZZZZZCONTROL0.27), UL:BIT(ZZZZZZZZZZCONTROL0.26), IN:BIT(ZZZZZZZZZZCONTROL0.25), FD:BIT(ZZZZZZZZZZCONTROL0.24)",
    };

    // Structures whose exact layout is not known here; kept opaque so that Decorated data drives the shape.
    private static readonly string[] OpaqueStructures =
    {
        "ALARM", "ALARM_ANALOG", "ALARM_DIGITAL",
        "MOTION_INSTRUCTION", "MOTION_GROUP",
        "AXIS_SERVO", "AXIS_SERVO_DRIVE", "AXIS_CIP_DRIVE", "AXIS_VIRTUAL", "AXIS_GENERIC", "AXIS_GENERIC_DRIVE", "AXIS_CONSUMED",
        "COORDINATE_SYSTEM", "OUTPUT_CAM", "OUTPUT_COMPENSATION", "CAM", "CAM_PROFILE", "MOTION_INSTRUCTION_EXT",
        "PHASE", "PHASE_INSTRUCTION", "SEQUENCE", "STEP", "SEQUENCE_STEP", "SEQUENCE_TRANSITION", "SEQ_STEP", "SEQ_TRANSITION",
        "HMIBC", "PID_ENHANCED", "PIDE_AUTOTUNE", "SERIAL_PORT_CONTROL", "REDUNDANCY_STATUS", "SAFETY", "MODULE",
        "FILTER_HIGH_PASS", "FILTER_LOW_PASS", "FILTER_NOTCH", "FILTER_DERIVATIVE", "FILTER_LEAD_LAG_SEC_ORDER",
        "DEADTIME", "DERIVATIVE", "DISCRETE_2STATE", "DISCRETE_3STATE", "FLIP_FLOP_D", "FLIP_FLOP_JKD", "FUNCTION_GENERATOR",
        "INTEGRATOR", "LEAD_LAG", "MAXIMUM_CAPTURE", "MINIMUM_CAPTURE", "MOVING_AVERAGE", "MOVING_STD_DEV", "MULTIPLEXER",
        "PROP_INT", "PULSE_MULTIPLIER", "RAMP_SOAK", "RATE_LIMITER", "RESET_COUNTER", "SCALE", "SEL_ENHANCED",
        "SELECT", "SELECTABLE_NEGATE", "SELECTED_SUMMER", "SPLIT_RANGE", "TOTALIZER", "UP_DOWN_ACCUM",
        "S_CURVE", "SELECT_ENHANCED", "INTERNAL_MODEL_CONTROL", "MODULAR_MULTIVARIABLE_CONTROL", "COORDINATED_CONTROL",
        "ALARM_ANALOG_MULTIPLE", "DCI_STOP", "DCI_START", "DCI_MONITOR", "DCI_STOP_TEST", "DCI_STOP_TEST_MUTE",
        "AUX_VALVE_CONTROL", "CAM_PROFILE_ENTRY", "MOTION_INSTRUCTION_CAM",
        "CONNECTION_STATUS", "SAFETY_MAT", "EIGHT_POS_MODE_SELECTOR", "FIVE_POS_MODE_SELECTOR", "TWO_HAND_RUN_STATION",
        "DUAL_CHANNEL_INPUT", "DUAL_CHANNEL_INPUT_STOP", "DUAL_CHANNEL_INPUT_START", "DUAL_CHANNEL_INPUT_STOP_TEST",
        "REDUNDANT_INPUT", "REDUNDANT_OUTPUT", "LIGHT_CURTAIN", "THRS_ENHANCED", "THRSE", "CONFIGURABLE_ROUT", "SAFETY_FEEDBACK_INTERFACE",
        "CB_CONTINUOUS_MODE", "CB_INCH_MODE", "CB_SINGLE_STROKE_MODE", "CAM_MONITOR", "MAIN_VALVE_CONTROL", "CRANKSHAFT_POS_MONITOR",
        "MESSAGE_RESPONSE", "SFC_TEXT", "PHASE_STATE",
    };

    private static IReadOnlyList<DataTypeDefinition> Build()
    {
        var list = new List<DataTypeDefinition>();
        foreach (AtomicKind kind in Enum.GetValues<AtomicKind>())
        {
            list.Add(new AtomicType { Name = kind.ToLogixName(), Class = DataTypeClass.Atomic, Kind = kind });
        }
        var defined = new Dictionary<string, StructuredType>(StringComparer.OrdinalIgnoreCase);
        foreach (var dsl in StructureDsl)
        {
            var t = ParseDsl(dsl);
            defined[t.Name] = t;
        }
        // Member layouts extracted from the Rockwell reference manuals (motion, general, process, safety instructions).
        // Entries there override the inline DSL only when the inline entry has no members.
        foreach (var line in ReadResourceLines("L5x.PredefinedTypes.txt"))
        {
            var t = ParseDsl(line);
            if (defined.TryGetValue(t.Name, out var existing) && existing.Members.Count > 0 && !existing.IsOpaque) continue;
            defined[t.Name] = t;
        }
        list.AddRange(defined.Values);
        foreach (var name in OpaqueStructures)
        {
            if (defined.ContainsKey(name)) continue;
            list.Add(new StructuredType { Name = name, Class = DataTypeClass.Predefined, IsOpaque = true });
        }
        return list;
    }

    private static IEnumerable<string> ReadResourceLines(string logicalName)
    {
        using var stream = typeof(BuiltInTypes).Assembly.GetManifestResourceStream(logicalName);
        if (stream is null) yield break;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            yield return trimmed;
        }
    }

    /// <summary>Parses one DSL line into a structure definition (exposed for tests and tooling).</summary>
    public static StructuredType ParseDefinition(string dsl) => ParseDsl(dsl);

    private static StructuredType ParseDsl(string dsl)
    {
        var layoutUnknown = dsl.StartsWith('?');
        if (layoutUnknown) dsl = dsl[1..];
        var colon = dsl.IndexOf(':');
        var name = dsl[..colon].Trim();
        var type = new StructuredType
        {
            Name = name,
            Class = DataTypeClass.Predefined,
            Family = name == "STRING" ? DataTypeFamily.StringFamily : DataTypeFamily.NoFamily,
            IsOpaque = layoutUnknown,
        };
        foreach (var raw in dsl[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = raw.IndexOf(':');
            var memberName = raw[..sep].Trim();
            var hidden = memberName.StartsWith('*');
            if (hidden) memberName = memberName[1..];
            var typePart = raw[(sep + 1)..].Trim();

            string? target = null;
            int? bitNumber = null;
            var paren = typePart.IndexOf('(');
            if (paren >= 0)
            {
                var spec = typePart[(paren + 1)..^1];
                var dot = spec.LastIndexOf('.');
                target = spec[..dot];
                bitNumber = int.Parse(spec[(dot + 1)..]);
                typePart = typePart[..paren];
            }

            var radix = Radix.Unknown;
            var at = typePart.IndexOf('@');
            if (at >= 0)
            {
                radix = RadixExtensions.ParseRadix(typePart[(at + 1)..]);
                typePart = typePart[..at];
            }
            var dim = 0;
            var bracket = typePart.IndexOf('[');
            if (bracket >= 0)
            {
                dim = int.Parse(typePart[(bracket + 1)..^1]);
                typePart = typePart[..bracket];
            }
            if (radix == Radix.Unknown)
            {
                radix = typePart is "REAL" or "LREAL" ? Radix.Float : Radix.Decimal;
            }
            type.Members.Add(new DataTypeMember
            {
                Name = memberName,
                DataType = DataTypeRef.Of(typePart),
                Dimension = dim,
                Radix = radix,
                Hidden = hidden,
                Target = target,
                BitNumber = bitNumber,
            });
        }
        return type;
    }
}
