using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Mori.EscaFlow.Transpiler.Schema;

/// <summary>
/// Generates the JSON Schema (draft 2020-12) of the plc-ir model by reflection over the C# classes, so the schema
/// file and the model can never drift: a test compares the committed <c>plc-ir-2.0.schema.json</c> with the output.
/// Every node class becomes an entry of <c>$defs</c> (class name without the <c>Ir</c> prefix); polymorphic bases
/// become <c>oneOf</c> over their derived types with the discriminator as a <c>const</c>; <see cref="IrEnumAttribute"/>
/// values become <c>enum</c>; every object forbids unknown members except the vendor extension slot.
/// </summary>
/// <remarks>
/// The schema exists for consumers outside .NET (editors, other languages) and as documentation of the wire
/// format. Generating it from the model, with a test that fails when the committed file is stale, is cheaper
/// and safer than maintaining it by hand. Rules: properties follow the System.Text.Json order used when writing;
/// non-nullable strings are required (everything else is optional because nulls are omitted); dictionaries map
/// to <c>additionalProperties</c>; <see cref="IrTypeRef"/> gets its string-or-object form; the vendor slot is the
/// only object allowing unknown members.
/// </remarks>
public static class SchemaGenerator
{
    /// <summary>The schema's <c>$id</c>.</summary>
    public const string SchemaId = "https://plc-ir.dev/schema/2.0";

    /// <summary>Logical name of the embedded copy of the committed schema.</summary>
    public const string ResourceName = "Mori.EscaFlow.Transpiler.Schema.plc-ir-2.0.schema.json";

    /// <summary>The embedded copy of the schema, or null when the assembly was built without it.</summary>
    public static string? Embedded()
    {
        using var stream = typeof(SchemaGenerator).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Generates the schema text (indented, LF line endings, deterministic ordering).</summary>
    public static string Generate()
    {
        var g = new Generator();
        g.Define(typeof(IrDocument));
        g.Drain();

        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = SchemaId,
            ["title"] = "plc-ir " + IrDocument.CurrentVersion,
            ["description"] = "Vendor-neutral intermediate representation of a PLC project: IEC 61131-3 core with a vendor extension slot on every node. Generated from the Mori.EscaFlow.Transpiler C# model.",
            ["$ref"] = "#/$defs/Document",
        };
        var defs = new JsonObject();
        foreach (var name in g.Defs.Keys.OrderBy(k => k, StringComparer.Ordinal)) defs[name] = g.Defs[name];
        root["$defs"] = defs;
        // JsonNode indents with Environment.NewLine; the schema file is always LF.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>The property names the schema lists for a model class, camelCased, for tests that check coverage.</summary>
    public static IReadOnlyList<string> SerializedProperties(Type type)
        => Generator.Properties(type).Select(p => Generator.JsonName(p)).ToList();

    /// <summary>Walks the model types reachable from the document and builds one definition per class.</summary>
    private sealed class Generator
    {
        /// <summary>Definitions by name (class name without the <c>Ir</c> prefix).</summary>
        public readonly Dictionary<string, JsonNode> Defs = new(StringComparer.Ordinal);
        private readonly Queue<Type> _pending = new();
        private readonly HashSet<Type> _seen = new();
        private readonly NullabilityInfoContext _nullability = new();

        /// <summary><c>IrDocument</c> becomes <c>Document</c>; types without the prefix keep their name.</summary>
        public static string DefName(Type t) => t.Name.StartsWith("Ir", StringComparison.Ordinal) && t.Name.Length > 2 && char.IsUpper(t.Name[2]) ? t.Name[2..] : t.Name;

        /// <summary>The JSON property name: an explicit attribute, else camelCase of the C# name (the format's naming policy).</summary>
        public static string JsonName(PropertyInfo p) => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);

        /// <summary>Queues a type for definition once.</summary>
        public void Define(Type t)
        {
            if (_seen.Add(t)) _pending.Enqueue(t);
        }

        /// <summary>Builds every queued definition; building one may queue more.</summary>
        public void Drain()
        {
            while (_pending.Count > 0)
            {
                var t = _pending.Dequeue();
                Defs[DefName(t)] = Build(t);
            }
        }

        /// <summary>Serialized properties of a class, in System.Text.Json order (JsonPropertyOrder, then derived-first declaration order).</summary>
        /// <remarks>Matching the serializer's order keeps the schema's property order identical to a written document, which makes the two easy to read side by side.</remarks>
        public static List<PropertyInfo> Properties(Type t)
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .ToList();
            int Depth(Type? d) { var n = 0; while (d is not null) { n++; d = d.BaseType; } return n; }
            return props
                .Select((p, i) => (p, i))
                .OrderBy(x => x.p.GetCustomAttribute<JsonPropertyOrderAttribute>()?.Order ?? 0)
                .ThenByDescending(x => Depth(x.p.DeclaringType))
                .ThenBy(x => x.p.MetadataToken)
                .Select(x => x.p)
                .ToList();
        }

        /// <summary>The definition of one class: a oneOf for polymorphic bases, an object with properties otherwise.</summary>
        private JsonNode Build(Type t)
        {
            if (t == typeof(IrTypeRef)) return TypeRefSchema();

            var poly = t.GetCustomAttribute<JsonPolymorphicAttribute>();
            if (poly is not null)
            {
                var derived = t.GetCustomAttributes<JsonDerivedTypeAttribute>().ToList();
                var oneOf = new JsonArray();
                foreach (var d in derived)
                {
                    Define(d.DerivedType);
                    oneOf.Add(new JsonObject { ["$ref"] = "#/$defs/" + DefName(d.DerivedType) });
                }
                return new JsonObject
                {
                    ["description"] = $"Discriminated by '{poly.TypeDiscriminatorPropertyName}'.",
                    ["oneOf"] = oneOf,
                };
            }

            var obj = new JsonObject { ["type"] = "object" };
            var properties = new JsonObject();
            var required = new JsonArray();

            // Discriminator when this class is a derived type of a polymorphic base.
            var discriminator = FindDiscriminator(t);
            if (discriminator is not null)
            {
                properties[discriminator.Value.Property] = new JsonObject { ["const"] = discriminator.Value.Value };
                required.Add(discriminator.Value.Property);
            }

            foreach (var p in Properties(t))
            {
                var name = JsonName(p);
                var schema = PropertySchema(p);
                properties[name] = schema;
                if (IsRequired(p)) required.Add(name);
            }
            obj["properties"] = properties;
            if (required.Count > 0) obj["required"] = required;
            obj["additionalProperties"] = false;
            return obj;
        }

        /// <summary>The discriminator property and value of a derived type, found on the nearest polymorphic base.</summary>
        private static (string Property, string Value)? FindDiscriminator(Type t)
        {
            for (var b = t.BaseType; b is not null; b = b.BaseType)
            {
                var poly = b.GetCustomAttribute<JsonPolymorphicAttribute>();
                if (poly is null) continue;
                var d = b.GetCustomAttributes<JsonDerivedTypeAttribute>().FirstOrDefault(a => a.DerivedType == t);
                if (d?.TypeDiscriminator is string s) return (poly.TypeDiscriminatorPropertyName ?? "$type", s);
            }
            return null;
        }

        /// <summary>Only non-nullable strings are required: they are the identifying members (names, ids, kinds); everything else may be omitted because nulls are not written.</summary>
        private bool IsRequired(PropertyInfo p)
        {
            if (p.PropertyType != typeof(string)) return false;
            var info = _nullability.Create(p);
            return info.WriteState == NullabilityState.NotNull;
        }

        /// <summary>Schema of one property: the type's schema, narrowed to an enum by <see cref="IrEnumAttribute"/> and annotated with <c>x-refKind</c> by <see cref="IrRefKindAttribute"/>.</summary>
        private JsonNode PropertySchema(PropertyInfo p)
        {
            var schema = TypeSchema(p.PropertyType);
            var values = p.GetCustomAttribute<IrEnumAttribute>()?.Values;
            if (values is not null)
            {
                var e = new JsonArray();
                foreach (var v in values) e.Add(v);
                schema = new JsonObject { ["type"] = "string", ["enum"] = e };
            }
            var refKind = p.GetCustomAttribute<IrRefKindAttribute>();
            if (refKind is not null)
            {
                if (schema is JsonObject o && o["type"]?.GetValue<string>() == "array" && o["items"] is JsonObject items) items["x-refKind"] = refKind.Kind;
                else if (schema is JsonObject o2) o2["x-refKind"] = refKind.Kind;
            }
            return schema;
        }

        /// <summary>Schema of a CLR type: primitives, dates, arrays and lists, dictionaries, enums, and <c>$ref</c>s to model classes (queued for definition).</summary>
        private JsonNode TypeSchema(Type t)
        {
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying is not null) return TypeSchema(underlying);
            if (t == typeof(string)) return new JsonObject { ["type"] = "string" };
            if (t == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return new JsonObject { ["type"] = "integer" };
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return new JsonObject { ["type"] = "number" };
            if (t == typeof(DateTimeOffset) || t == typeof(DateTime)) return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
            if (t == typeof(object)) return new JsonObject();
            if (t == typeof(JsonElement)) return new JsonObject();
            if (t.IsArray) return new JsonObject { ["type"] = "array", ["items"] = TypeSchema(t.GetElementType()!) };
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var args = t.GetGenericArguments();
                if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                {
                    var valueType = args[1];
                    // Dictionary<string, object?> is the vendor slot: the one place where anything goes.
                    if (valueType == typeof(object))
                        return new JsonObject { ["type"] = "object", ["description"] = "Vendor extension slot: one entry per vendor id, any content.", ["additionalProperties"] = true };
                    return new JsonObject { ["type"] = "object", ["additionalProperties"] = TypeSchema(valueType) };
                }
                if (typeof(IEnumerable).IsAssignableFrom(t))
                    return new JsonObject { ["type"] = "array", ["items"] = TypeSchema(args[0]) };
            }
            if (t.IsClass)
            {
                Define(t);
                return new JsonObject { ["$ref"] = "#/$defs/" + DefName(t) };
            }
            if (t.IsEnum)
            {
                var e = new JsonArray();
                foreach (var n in Enum.GetNames(t)) e.Add(n);
                return new JsonObject { ["type"] = "string", ["enum"] = e };
            }
            return new JsonObject();
        }

        /// <summary>The hand-shaped schema of <see cref="IrTypeRef"/>: a string, or the object form written by <see cref="IrTypeRefConverter"/>.</summary>
        private JsonNode TypeRefSchema()
        {
            Define(typeof(IrDim));
            var kinds = new JsonArray();
            foreach (var k in typeof(IrTypeRef).GetProperty(nameof(IrTypeRef.Kind))!.GetCustomAttribute<IrEnumAttribute>()!.Values) kinds.Add(k);
            return new JsonObject
            {
                ["description"] = "A type reference: a plain string names an atomic, structure, POU or standard function block type; the object form describes arrays, anonymous strings, pointers, references and 'any'.",
                ["oneOf"] = new JsonArray
                {
                    new JsonObject { ["type"] = "string" },
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = kinds },
                            ["name"] = new JsonObject { ["type"] = "string" },
                            ["dims"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/$defs/Dim" } },
                            ["element"] = new JsonObject { ["$ref"] = "#/$defs/TypeRef" },
                            ["length"] = new JsonObject { ["type"] = "integer" },
                            ["wide"] = new JsonObject { ["type"] = "boolean" },
                            ["to"] = new JsonObject { ["$ref"] = "#/$defs/TypeRef" },
                        },
                        ["required"] = new JsonArray { "kind" },
                        ["additionalProperties"] = false,
                    },
                },
            };
        }
    }
}
