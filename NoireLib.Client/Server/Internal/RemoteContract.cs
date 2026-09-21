using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Remote.Internal;

// Two listeners publish the same member when API, name, call kind, parameters with defaults, and answer match, types resolved all the way down.
// Types compare by shape, never by name. Wording is left out.
internal static class RemoteContract
{
    // Keywords that change nothing about what crosses the wire.
    private static readonly HashSet<string> Wording = new(StringComparer.Ordinal)
    {
        "description", "title", "examples", "example", "$comment", "deprecated",
    };

    // Keywords whose array order carries no meaning.
    private static readonly HashSet<string> Unordered = new(StringComparer.Ordinal) { "required", "enum" };

    public static string Fingerprint(string api, NoireRemoteManifestMember member, IReadOnlyDictionary<string, JObject>? defs)
    {
        var parameters = new JArray();

        // Arguments cross by name.
        foreach (var parameter in member.Parameters.OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase))
        {
            parameters.Add(new JObject
            {
                ["name"] = parameter.Name.ToLowerInvariant(),
                ["required"] = parameter.Required,
                // A different default changes what an omitted argument does.
                ["default"] = parameter.Default?.DeepClone() ?? JValue.CreateNull(),
                ["type"] = Canonical(parameter.Schema, defs, []),
            });
        }

        var contract = new JObject
        {
            ["api"] = api.ToLowerInvariant(),
            ["member"] = member.Name.ToLowerInvariant(),
            ["mode"] = member.Mode,
            ["readOnly"] = member.ReadOnly,
            ["parameters"] = parameters,
            ["returns"] = Canonical(member.Returns?.Schema, defs, []),
        };

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(contract.ToString(Formatting.None)));

        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static JToken Canonical(JToken? schema, IReadOnlyDictionary<string, JObject>? defs, List<string> resolving)
    {
        switch (schema)
        {
            case null:
                return JValue.CreateNull();

            case JObject shape when shape.TryGetValue("$ref", out var reference) && reference.Type == JTokenType.String:
                return Resolve((string)reference!, defs, resolving);

            case JObject shape:
            {
                var canonical = new JObject();

                foreach (var property in shape.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (Wording.Contains(property.Name))
                        continue;

                    var value = Canonical(property.Value, defs, resolving);

                    if (Unordered.Contains(property.Name) && value is JArray items)
                        value = new JArray(items.OrderBy(item => item.ToString(Formatting.None), StringComparer.Ordinal));

                    canonical[property.Name] = value;
                }

                return canonical;
            }

            case JArray items:
                return new JArray(items.Select(item => Canonical(item, defs, resolving)));

            default:
                return schema.DeepClone();
        }
    }

    // A recursive type is written as how many levels up it repeats.
    private static JToken Resolve(string reference, IReadOnlyDictionary<string, JObject>? defs, List<string> resolving)
    {
        const string prefix = "#/$defs/";

        var name = reference.StartsWith(prefix, StringComparison.Ordinal) ? reference.Substring(prefix.Length) : reference;
        var depth = resolving.IndexOf(name);

        if (depth >= 0)
            return new JObject { ["$cycle"] = resolving.Count - depth };

        if (defs == null || !defs.TryGetValue(name, out var shape))
            return new JObject { ["$unresolved"] = name };

        resolving.Add(name);

        try
        {
            return Canonical(shape, defs, resolving);
        }
        finally
        {
            resolving.RemoveAt(resolving.Count - 1);
        }
    }
}
