namespace Plc.Ir.Validation;

/// <summary>Every issue code the validator can raise, one per check.</summary>
public static class ValidationCodes
{
    // structural
    public const string JsonRead = "IRV001";
    public const string FormatVersion = "IRV002";
    public const string InvalidEnumValue = "IRV003";
    public const string EmptyNameOrId = "IRV004";
    public const string DuplicateId = "IRV005";
    public const string MalformedId = "IRV006";

    // referential
    public const string UnresolvedType = "IRV010";
    public const string UnresolvedVariable = "IRV011";
    public const string UnresolvedPou = "IRV012";
    public const string UnresolvedAction = "IRV013";
    public const string UnresolvedChannel = "IRV014";
    public const string UnresolvedDevice = "IRV015";
    public const string UnresolvedTask = "IRV016";
    public const string UnresolvedProgramInstance = "IRV017";
    public const string UndeclaredTemporary = "IRV018";
    public const string ThisOutsideFunctionBlock = "IRV019";
    public const string UnresolvedReference = "IRV020";
    public const string FbCallTarget = "IRV021";
    public const string UnknownStandardFb = "IRV022";
    public const string UnknownProfile = "IRV023";
    public const string InstanceTypeMismatch = "IRV024";
    public const string UnknownPin = "IRV025";
    public const string PouPinMismatch = "IRV026";
    public const string UnknownService = "IRV027";
    public const string UnknownServiceCatalog = "IRV028";
    public const string ServiceArgCount = "IRV029";
    public const string UndefinedLabel = "IRV030";
    public const string AliasTarget = "IRV031";
    public const string TaskProgramInconsistent = "IRV032";
    public const string StdFbBinding = "IRV033";

    // typing
    public const string BoolResultExpected = "IRV040";
    public const string LogicalOperandTypes = "IRV041";
    public const string ArithmeticOperandTypes = "IRV042";
    public const string BitOfNotInteger = "IRV043";
    public const string BitIndexNotInteger = "IRV044";
    public const string EdgePinNotBool = "IRV045";
    public const string ConvTypes = "IRV046";
    public const string AssignTargetKind = "IRV047";
    public const string AssignTypeMismatch = "IRV048";
    public const string ConditionNotBool = "IRV049";
    public const string ForVariableNotInteger = "IRV050";
    public const string CaseSelectorType = "IRV051";
    public const string PinTypeMismatch = "IRV052";
    public const string PathMemberNotFound = "IRV053";
    public const string PathIndexCount = "IRV054";
    public const string PathStringSegment = "IRV055";
    public const string RefLeafTypeMismatch = "IRV056";

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        [JsonRead] = "The JSON could not be read strictly (parse error or unknown member).",
        [FormatVersion] = "The document is not a plc-ir document of major version 2.",
        [InvalidEnumValue] = "A closed-set string property holds a value outside its set.",
        [EmptyNameOrId] = "A name or id is empty.",
        [DuplicateId] = "An id or name is declared twice.",
        [MalformedId] = "An id does not follow its pattern or does not match its container.",
        [UnresolvedType] = "A type name resolves to no atomic, project type, POU or standard function block.",
        [UnresolvedVariable] = "A variable id names no declared variable.",
        [UnresolvedPou] = "A POU name names no POU.",
        [UnresolvedAction] = "An action name names no action of the target POU.",
        [UnresolvedChannel] = "An I/O channel id names no channel.",
        [UnresolvedDevice] = "A device id names no device.",
        [UnresolvedTask] = "A task name names no task of the resource.",
        [UnresolvedProgramInstance] = "A program instance name names no instance of the resource.",
        [UndeclaredTemporary] = "A temp reference names no temporary of the enclosing body.",
        [ThisOutsideFunctionBlock] = "A this reference occurs outside a function block.",
        [UnresolvedReference] = "A reference root is marked unresolved.",
        [FbCallTarget] = "A fbCall must name exactly one of fb (standard block) or pou.",
        [UnknownStandardFb] = "A fbCall names a standard function block that is not in the catalog.",
        [UnknownProfile] = "A fbCall or std FB binding names an unknown pin profile.",
        [InstanceTypeMismatch] = "The instance of a standard block call has a type other than the profile's instance type.",
        [UnknownPin] = "A fbCall pin is not a pin of the standard block under its profile.",
        [PouPinMismatch] = "A fbCall pin is not an input/output/inOut variable of the POU, or has the wrong direction.",
        [UnknownService] = "A sysCall / sysFn names a service that its catalog does not define.",
        [UnknownServiceCatalog] = "A sysCall / sysFn names a service catalog that is not registered with the validator.",
        [ServiceArgCount] = "A sysCall / sysFn passes fewer or more arguments than the service accepts.",
        [UndefinedLabel] = "A jump names a label that is not defined in the same body.",
        [AliasTarget] = "An alias has no target variable or the alias chain is cyclic.",
        [TaskProgramInconsistent] = "A program instance's task and the task's program list disagree.",
        [StdFbBinding] = "A type's std FB binding names an unknown standard block.",
        [BoolResultExpected] = "A comparison or logical expression is typed other than BOOL.",
        [LogicalOperandTypes] = "The operands of and/or/xor are not both BOOL or both integer/bit-string.",
        [ArithmeticOperandTypes] = "An arithmetic operand is not numeric.",
        [BitOfNotInteger] = "bit.of is not an integer or bit-string value.",
        [BitIndexNotInteger] = "bit.bit is not an integer.",
        [EdgePinNotBool] = "edge.clk or edge.storage is not BOOL.",
        [ConvTypes] = "conv.to is not atomic or the operand cannot be converted.",
        [AssignTargetKind] = "An assignment target is neither a ref nor a bit expression.",
        [AssignTypeMismatch] = "The assigned value's type is incompatible with the target's type.",
        [ConditionNotBool] = "An if / while / repeat condition is typed other than BOOL.",
        [ForVariableNotInteger] = "A for loop variable is not an integer.",
        [CaseSelectorType] = "A case selector is not an integer or enumeration.",
        [PinTypeMismatch] = "A fbCall pin expression's type is incompatible with the pin's type under the profile.",
        [PathMemberNotFound] = "A ref path names a member the current type does not have.",
        [PathIndexCount] = "A ref path indexes with a different number of indices than the array has dimensions, or indexes a non-array.",
        [PathStringSegment] = "A ref path uses length or char on a non-string value.",
        [RefLeafTypeMismatch] = "The type declared on a ref differs from the type reached by walking its path.",
    };

    public static string Describe(string code) => Descriptions.TryGetValue(code, out var d) ? d : "";

    public static IReadOnlyDictionary<string, string> All => Descriptions;
}
