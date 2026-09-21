using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NoireLib.Remote;

/// <summary>
/// Turns a manifest into an OpenAPI 3.1.0 document. It reads a saved manifest as happily as a live one. A document
/// can be produced with nothing listening.
/// </summary>
public static class NoireRemoteOpenApi
{
    private const string Version = "3.1.0";
    private const string DefsPointer = "#/$defs/";
    private const string ComponentsPointer = "#/components/schemas/";

    /// <summary>
    /// Builds the document for a manifest.
    /// </summary>
    /// <param name="manifest">The manifest to transform.</param>
    /// <param name="baseUrl">The server URL the document names, such as <c>http://127.0.0.1:52100</c>. Null leaves
    /// the server list carrying a relative URL.</param>
    /// <returns>The document, ready to serialize.</returns>
    /// <exception cref="ArgumentNullException">If the manifest is null.</exception>
    public static JObject FromManifest(NoireRemoteManifest manifest, string? baseUrl = null)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        var paths = new JObject();
        var tags = new JArray();

        foreach (var endpoint in manifest.Endpoints)
        {
            tags.Add(new JObject
            {
                ["name"] = endpoint.Name,
                ["description"] = endpoint.Summary ?? ("Members of the " + endpoint.Name + " endpoint."),
            });

            foreach (var member in endpoint.Members)
                paths[NoireRemotePaths.Member(endpoint.Name, member.Name)] = MemberPath(endpoint, member);
        }

        AddMetaPaths(paths, manifest);

        return new JObject
        {
            ["openapi"] = Version,
            ["info"] = new JObject
            {
                ["title"] = manifest.Plugin + " over NoireRemote",
                ["version"] = string.IsNullOrEmpty(manifest.PluginVersion) ? "0.0.0" : manifest.PluginVersion,
                ["description"] = Description(manifest),
            },
            ["servers"] = new JArray { new JObject { ["url"] = baseUrl ?? "/" } },
            ["tags"] = tags,
            ["paths"] = paths,
            ["components"] = new JObject
            {
                ["schemas"] = Schemas(manifest),
                ["securitySchemes"] = SecuritySchemes(),
            },
            ["security"] = new JArray { new JObject { ["bearer"] = new JArray() } },
        };
    }

    private static string Description(NoireRemoteManifest manifest)
    {
        var text = "The surface instance " + manifest.Instance.ToString("D") + " publishes over NoireRemote protocol "
            + manifest.Protocol.ToString(CultureInfo.InvariantCulture) + ".";

        text += " Every call is a POST whose body is the request envelope, and every answer is the response envelope"
            + " whether the call succeeded or not.";

        text += " A member marked local is refused on a connection that is not loopback.";

        text += " OpenAPI cannot express the signed scheme's message authentication code. A generated client"
            + " reaching a listener off this machine has to compute the signature itself.";

        return text;
    }

    private static JObject SecuritySchemes()
        => new()
        {
            ["bearer"] = new JObject
            {
                ["type"] = "http",
                ["scheme"] = "bearer",
                ["description"] = "The per-launch credential from the instance record. It is accepted on a loopback connection only.",
            },
            ["signed"] = new JObject
            {
                ["type"] = "apiKey",
                ["in"] = "header",
                ["name"] = NoireRemoteHeaders.Authorization,
                ["description"] = "Reads as 'Noire ts=<unix seconds>, nonce=<random>, mac=<hex>', where the code is"
                    + " HMAC-SHA256 over the method, the path, the body, the timestamp and the nonce, keyed by the"
                    + " shared secret. OpenAPI has no way to describe that construction.",
            },
        };

    private static JObject Schemas(NoireRemoteManifest manifest)
    {
        var schemas = new JObject();

        if (manifest.Defs != null)
        {
            foreach (var pair in manifest.Defs)
            {
                var copy = (JObject)pair.Value.DeepClone();
                Repoint(copy);
                schemas[pair.Key] = copy;
            }
        }

        schemas["NoireRemoteError"] = ErrorSchema();
        schemas["NoireRemoteJobStatus"] = JobSchema();

        return schemas;
    }

    // JSON Schema 2020-12 drops into OpenAPI 3.1 verbatim. Only the pointer moves, from $defs to components.
    private static void Repoint(JToken token)
    {
        switch (token)
        {
            case JObject o:
                if (o["$ref"] is JValue { Type: JTokenType.String } reference)
                {
                    var target = (string)reference.Value!;

                    if (target.StartsWith(DefsPointer, StringComparison.Ordinal))
                        o["$ref"] = ComponentsPointer + target.Substring(DefsPointer.Length);
                }

                foreach (var property in o.Properties())
                    Repoint(property.Value);

                break;

            case JArray array:
                foreach (var item in array)
                    Repoint(item);

                break;
        }
    }

    private static JObject MemberPath(NoireRemoteManifestEndpoint endpoint, NoireRemoteManifestMember member)
    {
        var responses = new JObject
        {
            ["200"] = Response("The member ran.", EnvelopeSchema(member)),
        };

        if (string.Equals(member.Mode, "job", StringComparison.OrdinalIgnoreCase))
            responses["202"] = Response("The member started as a job.", EnvelopeSchema(null));

        foreach (var pair in Failures(member))
            responses[pair.Key] = pair.Value;

        var operation = new JObject
        {
            ["operationId"] = endpoint.Name + "_" + member.Name,
            ["summary"] = member.Summary ?? (endpoint.Name + "/" + member.Name),
            ["tags"] = new JArray { endpoint.Name },
            ["parameters"] = HeaderParameters(),
            ["requestBody"] = new JObject
            {
                ["required"] = true,
                ["content"] = new JObject
                {
                    [NoireRemoteHeaders.JsonContentType] = new JObject { ["schema"] = RequestSchema(member) },
                },
            },
            ["responses"] = responses,
        };

        if (member.Deprecated)
            operation["deprecated"] = true;

        if (!string.Equals(member.Access, "remote", StringComparison.OrdinalIgnoreCase))
            operation["x-noire-access"] = member.Access;

        if (member.Confirm)
            operation["x-noire-confirm"] = true;

        if (!string.Equals(member.Requires, "none", StringComparison.OrdinalIgnoreCase))
            operation["x-noire-requires"] = member.Requires;

        if (member.ReadOnly)
            operation["x-noire-readonly"] = true;

        // A published property with no argument is a GET. Everything else is a POST.
        return new JObject { [member.ReadOnly ? "get" : "post"] = operation };
    }

    private static JArray HeaderParameters()
        => new()
        {
            HeaderParameter(NoireRemoteHeaders.Instance, "The instance this call is for. A listener that is not it answers 409."),
            HeaderParameter(NoireRemoteHeaders.RequestId, "The correlation id echoed back, and the suffix of this call's progress topic."),
        };

    private static JObject HeaderParameter(string name, string description)
        => new()
        {
            ["name"] = name,
            ["in"] = "header",
            ["required"] = false,
            ["description"] = description,
            ["schema"] = new JObject { ["type"] = "string" },
        };

    private static JObject RequestSchema(NoireRemoteManifestMember member)
    {
        var properties = new JObject();
        var required = new JArray();

        foreach (var parameter in member.Parameters)
        {
            properties[parameter.Name] = Repointed(parameter.Schema) ?? new JObject();

            if (parameter.Required)
                required.Add(parameter.Name);
        }

        var args = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["description"] = "The arguments, keyed by parameter name. An array in declaration order is accepted too.",
        };

        if (required.Count > 0)
            args["required"] = required;

        var body = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["args"] = args,
                ["timeoutMs"] = new JObject { ["type"] = "integer", ["format"] = "int32", ["description"] = "The deadline for this call. The listener clamps it." },
                ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "sync", "job" } },
                ["id"] = new JObject { ["type"] = "string", ["description"] = "The correlation id echoed back." },
                ["protocol"] = new JObject { ["type"] = "integer", ["const"] = NoireRemotePaths.Protocol },
            },
        };

        if (member.Parameters.Count > 0 && required.Count > 0)
            body["required"] = new JArray { "args" };

        return body;
    }

    private static JObject EnvelopeSchema(NoireRemoteManifestMember? member)
    {
        var properties = new JObject
        {
            ["ok"] = new JObject { ["type"] = "boolean" },
            ["protocol"] = new JObject { ["type"] = "integer" },
            ["instance"] = new JObject { ["type"] = "string", ["format"] = "uuid" },
            ["id"] = new JObject { ["type"] = new JArray { "string", "null" } },
            ["elapsedMs"] = new JObject { ["type"] = new JArray { "integer", "null" }, ["format"] = "int64" },
            ["error"] = new JObject { ["$ref"] = ComponentsPointer + "NoireRemoteError" },
            ["job"] = new JObject { ["$ref"] = ComponentsPointer + "NoireRemoteJobStatus" },
        };

        if (member?.Returns != null)
            properties["result"] = Repointed(member.Returns.Schema) ?? new JObject();

        return new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JArray { "ok", "protocol", "instance" },
        };
    }

    private static JObject? Repointed(JObject? schema)
    {
        if (schema == null)
            return null;

        var copy = (JObject)schema.DeepClone();
        Repoint(copy);

        return copy;
    }

    private static JObject Response(string description, JObject schema)
        => new()
        {
            ["description"] = description,
            ["content"] = new JObject
            {
                [NoireRemoteHeaders.JsonContentType] = new JObject { ["schema"] = schema },
            },
        };

    // A member with no readiness gate cannot answer NotReady.
    private static IEnumerable<KeyValuePair<string, JObject>> Failures(NoireRemoteManifestMember member)
    {
        yield return Failure("400", "The body, the protocol or an argument is wrong.",
            NoireRemoteErrorCodes.BadRequest, NoireRemoteErrorCodes.ArgumentMissing, NoireRemoteErrorCodes.ArgumentUnknown,
            NoireRemoteErrorCodes.ArgumentInvalid, NoireRemoteErrorCodes.ProtocolMismatch);

        yield return Failure("401", "The credential is missing, malformed or wrong.", NoireRemoteErrorCodes.Unauthorized);

        yield return Failure("403", "The request looks like a browser sent it, or a local member was reached from another machine.",
            NoireRemoteErrorCodes.Forbidden);

        yield return Failure("404", "No endpoint or member of that name is published.",
            NoireRemoteErrorCodes.UnknownEndpoint, NoireRemoteErrorCodes.UnknownMember);

        yield return Failure("405", "The route takes POST.", NoireRemoteErrorCodes.BadMethod);

        yield return Failure("408", "The member passed its deadline. It may still be running.", NoireRemoteErrorCodes.Timeout);

        yield return Failure("409", "The pinned instance names another listener, or the idempotency key was used for something else.",
            NoireRemoteErrorCodes.InstanceMismatch, NoireRemoteErrorCodes.IdempotencyConflict);

        yield return Failure("413", "The body passed the listener's cap.", NoireRemoteErrorCodes.TooLarge);

        yield return Failure("415", "The body is not JSON.", NoireRemoteErrorCodes.BadContentType);

        yield return Failure("500", "The member threw.", NoireRemoteErrorCodes.HandlerFault);

        if (string.Equals(member.Requires, "none", StringComparison.OrdinalIgnoreCase))
        {
            yield return Failure("503", "The listener is running as many calls as it accepts.", NoireRemoteErrorCodes.Busy);
            yield break;
        }

        yield return Failure("503", "The listener is busy, or the state the member needs is not there.",
            NoireRemoteErrorCodes.Busy, NoireRemoteErrorCodes.NotReady);
    }

    private static KeyValuePair<string, JObject> Failure(string status, string description, params string[] codes)
    {
        var enumeration = new JArray();

        foreach (var code in codes)
            enumeration.Add(code);

        var error = new JObject
        {
            ["allOf"] = new JArray { new JObject { ["$ref"] = ComponentsPointer + "NoireRemoteError" } },
            ["properties"] = new JObject { ["code"] = new JObject { ["enum"] = enumeration } },
        };

        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["ok"] = new JObject { ["const"] = false },
                ["protocol"] = new JObject { ["type"] = "integer" },
                ["instance"] = new JObject { ["type"] = "string", ["format"] = "uuid" },
                ["id"] = new JObject { ["type"] = new JArray { "string", "null" } },
                ["error"] = error,
            },
            ["required"] = new JArray { "ok", "error" },
        };

        return new KeyValuePair<string, JObject>(status, Response(description, schema));
    }

    private static void AddMetaPaths(JObject paths, NoireRemoteManifest manifest)
    {
        paths[NoireRemotePaths.Prefix + NoireRemotePaths.Ping] = new JObject
        {
            ["get"] = Meta("meta_ping", "Answers whether the listener is serving and what it publishes.", PingSchema()),
        };

        paths[NoireRemotePaths.Prefix + NoireRemotePaths.Manifest] = new JObject
        {
            ["get"] = Meta("meta_manifest", "Answers the whole published surface.", new JObject { ["type"] = "object" }),
        };

        paths[NoireRemotePaths.Prefix + NoireRemotePaths.Jobs + "/{jobId}"] = JobPath();

        paths[NoireRemotePaths.Prefix + NoireRemotePaths.Events] = new JObject
        {
            ["post"] = new JObject
            {
                ["operationId"] = "meta_events",
                ["summary"] = "Holds the connection until an event arrives or the wait elapses.",
                ["tags"] = new JArray { "meta" },
                ["requestBody"] = new JObject
                {
                    ["required"] = false,
                    ["content"] = new JObject
                    {
                        [NoireRemoteHeaders.JsonContentType] = new JObject { ["schema"] = EventRequestSchema() },
                    },
                },
                ["responses"] = new JObject
                {
                    ["200"] = Response("The events published since the cursor.", new JObject { ["type"] = "object" }),
                },
            },
        };

        paths[NoireRemotePaths.Prefix + NoireRemotePaths.Stream] = new JObject
        {
            ["get"] = new JObject
            {
                ["operationId"] = "meta_stream",
                ["summary"] = "Holds the connection open and writes one JSON document per line until it closes.",
                ["tags"] = new JArray { "meta" },
                ["parameters"] = new JArray
                {
                    QueryParameter("channels", "The channels to watch, separated by commas."),
                    QueryParameter("topics", "The topic prefixes to watch, separated by commas."),
                    QueryParameter("cursors", "Where to resume each channel, as channel:sequence pairs separated by commas."),
                    QueryParameter("minIntervalMs", "The shortest gap between two writes."),
                },
                ["responses"] = new JObject
                {
                    ["200"] = new JObject
                    {
                        ["description"] = "A stream of newline-delimited events. It ends when the connection closes.",
                        ["content"] = new JObject
                        {
                            [NoireRemoteHeaders.NdJsonContentType] = new JObject { ["schema"] = new JObject { ["type"] = "string" } },
                        },
                    },
                },
            },
        };

        if (manifest.Has(NoireRemoteFeatures.OpenApi))
        {
            paths[NoireRemotePaths.Prefix + NoireRemotePaths.OpenApi] = new JObject
            {
                ["get"] = Meta("meta_openapi", "Answers this document.", new JObject { ["type"] = "object" }),
            };
        }

    }

    private static JObject QueryParameter(string name, string description)
        => new()
        {
            ["name"] = name,
            ["in"] = "query",
            ["required"] = false,
            ["description"] = description,
            ["schema"] = new JObject { ["type"] = "string" },
        };

    private static JObject Meta(string operationId, string summary, JObject schema)
        => new()
        {
            ["operationId"] = operationId,
            ["summary"] = summary,
            ["tags"] = new JArray { "meta" },
            ["responses"] = new JObject { ["200"] = Response(summary, schema) },
        };

    private static JObject JobPath()
    {
        var parameters = new JArray
        {
            new JObject
            {
                ["name"] = "jobId",
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = new JObject { ["type"] = "string" },
            },
        };

        return new JObject
        {
            ["get"] = new JObject
            {
                ["operationId"] = "meta_jobRead",
                ["summary"] = "Answers how far along a job is, and its result once it has finished.",
                ["tags"] = new JArray { "meta" },
                ["parameters"] = parameters,
                ["responses"] = new JObject
                {
                    ["200"] = Response("The job's state.", EnvelopeSchema(null)),
                    ["404"] = Response("No job of that id is held.", new JObject { ["type"] = "object" }),
                },
            },
            ["delete"] = new JObject
            {
                ["operationId"] = "meta_jobCancel",
                ["summary"] = "Cancels a running job.",
                ["tags"] = new JArray { "meta" },
                ["parameters"] = parameters,
                ["responses"] = new JObject
                {
                    ["200"] = Response("The job's state after the cancel was asked for.", EnvelopeSchema(null)),
                    ["404"] = Response("No job of that id is held.", new JObject { ["type"] = "object" }),
                },
            },
        };
    }

    private static JObject PingSchema()
        => new()
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["ok"] = new JObject { ["type"] = "boolean" },
                ["protocol"] = new JObject { ["type"] = "integer" },
                ["instance"] = new JObject { ["type"] = "string", ["format"] = "uuid" },
                ["uptimeMs"] = new JObject { ["type"] = "integer", ["format"] = "int64" },
                ["endpoints"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
            },
        };

    private static JObject EventRequestSchema()
        => new()
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["channels"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["topics"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                ["since"] = new JObject { ["type"] = "integer", ["format"] = "int64" },
                ["cursors"] = new JObject { ["type"] = "object", ["additionalProperties"] = new JObject { ["type"] = "integer", ["format"] = "int64" } },
                ["waitMs"] = new JObject { ["type"] = "integer", ["format"] = "int32" },
                ["collapse"] = new JObject { ["type"] = "boolean" },
            },
        };

    private static JObject ErrorSchema()
        => new()
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["code"] = new JObject { ["type"] = "string" },
                ["message"] = new JObject { ["type"] = "string" },
                ["detail"] = new JObject { ["type"] = new JArray { "string", "null" } },
                ["candidates"] = new JObject { ["type"] = new JArray { "array", "null" }, ["items"] = new JObject { ["type"] = "string" } },
                ["retryAfterSeconds"] = new JObject { ["type"] = new JArray { "number", "null" } },
                ["stackTrace"] = new JObject { ["type"] = new JArray { "string", "null" } },
            },
            ["required"] = new JArray { "code", "message" },
        };

    private static JObject JobSchema()
        => new()
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["id"] = new JObject { ["type"] = "string" },
                ["state"] = new JObject { ["type"] = "string", ["enum"] = new JArray { "Running", "Done", "Failed", "Cancelled" } },
                ["progress"] = new JObject { ["type"] = new JArray { "number", "null" }, ["minimum"] = 0, ["maximum"] = 1 },
                ["pollAfterMs"] = new JObject { ["type"] = new JArray { "integer", "null" }, ["format"] = "int32" },
                ["route"] = new JObject { ["type"] = new JArray { "string", "null" } },
            },
            ["required"] = new JArray { "id", "state" },
        };
}
