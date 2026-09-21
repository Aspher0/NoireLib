using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

namespace NoireLib.Remote.Internal;

// JSON Schema 2020-12. OpenAPI 3.1 takes it verbatim.
internal sealed class RemoteSchemaBuilder
{
    private const string DefsPointer = "#/$defs/";

    private readonly Dictionary<string, JObject> definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> keys = [];
    private readonly NullabilityInfoContext nullability = new();

    public JObject Of(Type type)
        => Of(type, null);

    // The parameter's nullability annotation reaches the schema.
    public JObject OfParameter(ParameterInfo parameter)
    {
        var schema = Of(parameter.ParameterType, null);

        return IsNullableReference(() => nullability.Create(parameter)) ? WithNull(schema) : schema;
    }

    public IDictionary<string, JObject>? Definitions()
    {
        if (definitions.Count == 0)
            return null;

        // Short names for readers, full names where two share a short name.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var full in definitions.Keys)
        {
            var shortName = ShortNameOf(full);
            counts[shortName] = counts.TryGetValue(shortName, out var seen) ? seen + 1 : 1;
        }

        var renames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var full in definitions.Keys)
        {
            var shortName = ShortNameOf(full);
            renames[full] = counts[shortName] == 1 ? shortName : full;
        }

        var renamed = new Dictionary<string, JObject>(StringComparer.Ordinal);

        foreach (var pair in definitions)
        {
            Rewrite(pair.Value, renames);
            renamed[renames[pair.Key]] = pair.Value;
        }

        Renames = renames;

        return renamed;
    }

    public IReadOnlyDictionary<string, string> Renames { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public void Rewrite(JToken token, IReadOnlyDictionary<string, string> renames)
    {
        switch (token)
        {
            case JObject o:
                if (o["$ref"] is JValue { Type: JTokenType.String } reference)
                {
                    var target = (string)reference.Value!;

                    if (target.StartsWith(DefsPointer, StringComparison.Ordinal))
                    {
                        var key = target.Substring(DefsPointer.Length);

                        if (renames.TryGetValue(key, out var replacement))
                            o["$ref"] = DefsPointer + replacement;
                    }
                }

                foreach (var property in o.Properties())
                    Rewrite(property.Value, renames);

                break;

            case JArray array:
                foreach (var item in array)
                    Rewrite(item, renames);

                break;
        }
    }

    private JObject Of(Type type, PropertyOrField? member)
    {
        var underlying = Nullable.GetUnderlyingType(type);

        if (underlying != null)
            return WithNull(Of(underlying, null));

        var schema = Core(type);

        if (member != null && member.IsNullableReference)
            schema = WithNull(schema);

        return schema;
    }

    private JObject Core(Type type)
    {
        if (type == typeof(void))
            return new JObject { ["type"] = "null" };

        if (type == typeof(string))
            return new JObject { ["type"] = "string" };

        if (type == typeof(char))
            return new JObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1 };

        if (type == typeof(bool))
            return new JObject { ["type"] = "boolean" };

        if (type == typeof(Guid))
            return new JObject { ["type"] = "string", ["format"] = "uuid" };

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return new JObject { ["type"] = "string", ["format"] = "date-time" };

        if (type == typeof(TimeSpan))
        {
            // Newtonsoft writes a TimeSpan as hh:mm:ss, not an ISO 8601 duration.
            return new JObject
            {
                ["type"] = "string",
                ["description"] = "A duration written as hh:mm:ss, with an optional day part and fractional seconds.",
            };
        }

        if (type == typeof(Uri))
            return new JObject { ["type"] = "string", ["format"] = "uri" };

        if (type == typeof(Version))
            return new JObject { ["type"] = "string" };

        if (type == typeof(byte[]))
            return new JObject { ["type"] = "string", ["contentEncoding"] = "base64" };

        if (type.IsEnum)
            return EnumSchema(type);

        if (Integers.TryGetValue(type, out var bounds))
        {
            var schema = new JObject { ["type"] = "integer", ["format"] = bounds.Format };

            if (bounds.Minimum != null)
                schema["minimum"] = bounds.Minimum;

            if (bounds.Maximum != null)
                schema["maximum"] = bounds.Maximum;

            return schema;
        }

        if (type == typeof(float))
            return new JObject { ["type"] = "number", ["format"] = "float" };

        if (type == typeof(double) || type == typeof(decimal))
            return new JObject { ["type"] = "number" };

        if (type == typeof(Vector2))
            return VectorSchema("x", "y");

        if (type == typeof(Vector3))
            return VectorSchema("x", "y", "z");

        if (type == typeof(Vector4) || type == typeof(Quaternion))
            return VectorSchema("x", "y", "z", "w");

        if (type == typeof(JObject))
            return new JObject { ["type"] = "object" };

        if (type == typeof(JArray))
            return new JObject { ["type"] = "array" };

        if (type == typeof(JToken) || type == typeof(JValue))
            return [];

        if (type.IsArray)
            return new JObject { ["type"] = "array", ["items"] = Of(type.GetElementType()!, null) };

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                return new JObject { ["type"] = "object", ["additionalProperties"] = Of(arguments[1], null) };

            if (arguments.Length == 1 && RemoteJsonContract.IsSequenceDefinition(definition))
                return new JObject { ["type"] = "array", ["items"] = Of(arguments[0], null) };
        }

        return Reference(type);
    }

    private JObject Reference(Type type)
    {
        if (keys.TryGetValue(type, out var known))
            return new JObject { ["$ref"] = DefsPointer + known };

        var key = type.FullName ?? type.Name;
        keys[type] = key;

        // Registered before the members are walked. A self-referencing type terminates.
        var shape = new JObject { ["type"] = "object" };
        definitions[key] = shape;

        var properties = new JObject();
        var required = new JArray();

        foreach (var member in MembersOf(type))
        {
            properties[CamelCase(member.Name)] = Of(member.Type, member);

            if (member.IsRequired)
                required.Add(CamelCase(member.Name));
        }

        shape["properties"] = properties;

        if (required.Count > 0)
            shape["required"] = required;

        return new JObject { ["$ref"] = DefsPointer + key };
    }

    private IEnumerable<PropertyOrField> MembersOf(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod == null)
                continue;

            yield return new PropertyOrField(
                property.Name,
                property.PropertyType,
                IsNullableReference(() => nullability.Create(property)));
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            yield return new PropertyOrField(
                field.Name,
                field.FieldType,
                IsNullableReference(() => nullability.Create(field)));
        }
    }

    private static JObject EnumSchema(Type type)
    {
        // StringEnumConverter is in the wire settings.
        var names = new JArray();

        foreach (var name in Enum.GetNames(type))
            names.Add(name);

        var schema = new JObject { ["type"] = "string", ["enum"] = names };

        if (type.GetCustomAttribute<FlagsAttribute>() != null)
        {
            schema["x-noire-flags"] = true;
            schema["enum"] = names;
            schema["description"] = "Several values are sent as one string, separated by a comma and a space.";
        }

        return schema;
    }

    private static JObject VectorSchema(params string[] components)
    {
        var properties = new JObject();
        var required = new JArray();

        foreach (var component in components)
        {
            properties[component] = new JObject { ["type"] = "number", ["format"] = "float" };
            required.Add(component);
        }

        // The converter reads both the object form and the array form.
        return new JObject
        {
            ["oneOf"] = new JArray
            {
                new JObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required,
                },
                new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "number", ["format"] = "float" },
                    ["minItems"] = components.Length,
                    ["maxItems"] = components.Length,
                },
            },
        };
    }

    private static JObject WithNull(JObject schema)
    {
        if (schema["type"] is JValue { Type: JTokenType.String } single)
        {
            schema["type"] = new JArray { (string)single.Value!, "null" };
            return schema;
        }

        // A reference or a oneOf cannot carry a type union.
        return new JObject { ["oneOf"] = new JArray { schema, new JObject { ["type"] = "null" } } };
    }

    // Unknown for an unannotated assembly. A guess would make the document wrong.
    private static bool IsNullableReference(Func<NullabilityInfo> read)
    {
        try
        {
            return read().ReadState == NullabilityState.Nullable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ShortNameOf(string full)
    {
        var separator = full.LastIndexOf('.');
        var name = separator < 0 ? full : full.Substring(separator + 1);
        var nested = name.LastIndexOf('+');

        return nested < 0 ? name : name.Substring(nested + 1);
    }

    private static string CamelCase(string name)
        => name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private sealed record PropertyOrField(string Name, Type Type, bool IsNullableReference)
    {
        public bool IsRequired => !IsNullableReference && Nullable.GetUnderlyingType(Type) == null;
    }

    private sealed record IntegerBounds(string Format, JToken? Minimum, JToken? Maximum);

    private static readonly Dictionary<Type, IntegerBounds> Integers = new()
    {
        [typeof(byte)] = new IntegerBounds("int32", byte.MinValue, byte.MaxValue),
        [typeof(sbyte)] = new IntegerBounds("int32", sbyte.MinValue, sbyte.MaxValue),
        [typeof(short)] = new IntegerBounds("int32", short.MinValue, short.MaxValue),
        [typeof(ushort)] = new IntegerBounds("int32", ushort.MinValue, ushort.MaxValue),
        [typeof(int)] = new IntegerBounds("int32", int.MinValue, int.MaxValue),
        [typeof(uint)] = new IntegerBounds("int64", uint.MinValue, uint.MaxValue),
        [typeof(long)] = new IntegerBounds("int64", long.MinValue, long.MaxValue),
        [typeof(ulong)] = new IntegerBounds("int64", ulong.MinValue, ulong.MaxValue),
    };
}
