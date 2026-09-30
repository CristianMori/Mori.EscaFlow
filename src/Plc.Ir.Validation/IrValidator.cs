using System.Text.Json;

namespace Plc.Ir.Validation;

/// <summary>
/// Validates a plc-ir document in three passes: structural (format, closed-set values, ids), referential (every
/// name and id resolves, calls match their catalogs, labels, aliases, tasks) and typing (operand and assignment
/// compatibility, reference paths against declared types). Issues carry a code from <see cref="ValidationCodes"/>.
/// </summary>
public sealed class IrValidator
{
    private readonly ValidationOptions _options;

    public IrValidator(ValidationOptions? options = null) => _options = options ?? new ValidationOptions();

    public ValidationResult Validate(IrDocument doc, ValidationOptions? options = null)
    {
        var opts = options ?? _options;
        var result = new ValidationResult();
        doc.InvalidateIndex();
        var types = new TypeResolver(doc);
        new StructuralPass(doc, result, types).Run();
        new LogicPass(doc, result, types, opts).Run();
        return result;
    }

    /// <summary>Reads the JSON strictly (unknown members are reported as IRV001) and validates the document.</summary>
    public ValidationResult ValidateJson(string json, ValidationOptions? options = null)
    {
        IrDocument doc;
        try
        {
            doc = IrJson.Deserialize(json, strict: true);
        }
        catch (JsonException ex)
        {
            var result = new ValidationResult();
            result.Error(ValidationCodes.JsonRead, ex.Message, ex.Path ?? "document");
            return result;
        }
        catch (InvalidDataException ex)
        {
            var result = new ValidationResult();
            result.Error(ValidationCodes.FormatVersion, ex.Message, "document");
            return result;
        }
        return Validate(doc, options);
    }

    public ValidationResult ValidateJsonFile(string path, ValidationOptions? options = null) => ValidateJson(File.ReadAllText(path), options);
}
