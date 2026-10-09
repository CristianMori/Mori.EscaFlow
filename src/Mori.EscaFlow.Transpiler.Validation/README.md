# Mori.EscaFlow.Transpiler.Validation

Validates a middle file in three passes: structural (format, enum values, ids), referential (every id,
name, pin, service, label and alias resolves; tasks and program instances agree) and typing (conditions,
operand categories, bit access, conversions, assignments, pins, reference paths against the declared
types). Codes are `IRVnnn` (`ValidationCodes.Describe`). `IrValidator.ValidateJson` first performs a strict
read that rejects unknown members. Vendor service catalogs are registered through `ValidationOptions`.
