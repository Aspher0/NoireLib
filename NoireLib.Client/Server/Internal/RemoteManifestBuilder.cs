using Newtonsoft.Json.Linq;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;

namespace NoireLib.Remote.Internal;

// Read by the console, the OpenAPI transform and the Python stub. Rebuilt when the route table changes.
internal static class RemoteManifestBuilder
{
    public static NoireRemoteManifest Build(
        HttpRouteTable table,
        Guid instance,
        string plugin,
        string pluginVersion,
        NoireRemoteOptions? options = null,
        IReadOnlyList<NoireRemoteManifestChannel>? channels = null)
    {
        var shapes = new Dictionary<string, JObject>(StringComparer.Ordinal);
        var schemas = new RemoteSchemaBuilder();
        var endpoints = new List<NoireRemoteManifestEndpoint>();

        foreach (var endpoint in table.Snapshot())
        {
            var members = new List<NoireRemoteManifestMember>(endpoint.Members.Count);

            foreach (var member in endpoint.Members)
                members.Add(Describe(member, shapes, schemas));

            endpoints.Add(new NoireRemoteManifestEndpoint
            {
                Name = endpoint.Name,
                Access = endpoint.Access.ToString().ToLowerInvariant(),
                Summary = endpoint.Summary,
                Members = members,
                Events = DescribeEvents(endpoint.Events, schemas),
            });
        }

        // A short name is only known unique once every type is in.
        var defs = schemas.Definitions();

        if (defs != null)
        {
            foreach (var endpoint in endpoints)
            {
                foreach (var member in endpoint.Members)
                {
                    RewriteInto(schemas, member.Schema);

                    foreach (var parameter in member.Parameters)
                        RewriteInto(schemas, parameter.Schema);

                    RewriteInto(schemas, member.Returns?.Schema);
                    RewriteInto(schemas, member.Progress?.Schema);
                }

                if (endpoint.Events == null)
                    continue;

                foreach (var declared in endpoint.Events)
                    RewriteInto(schemas, declared.Schema);
            }
        }

        // A fingerprint resolves the same names the document ships.
        var readable = defs == null ? null : new Dictionary<string, JObject>(defs, StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            foreach (var member in endpoint.Members)
                member.Contract = RemoteContract.Fingerprint(endpoint.Name, member, readable);
        }

        return new NoireRemoteManifest
        {
            Protocol = NoireRemotePaths.Protocol,
            Plugin = plugin,
            PluginVersion = pluginVersion,
            Instance = instance,
            // Publishing a socket changes the manifest without touching the route table.
            Revision = table.Version + WebsocketDiscovery.Revision(instance),
            Endpoints = endpoints,
            Types = shapes.Count == 0 ? null : shapes,
            Defs = defs,
            Channels = channels,
            Sockets = WebsocketDiscovery.Describe(instance),
            Features = options == null ? null : FeaturesOf(options),
        };
    }

    public static IReadOnlyList<string> FeaturesOf(NoireRemoteOptions options)
    {
        var features = new List<string>
        {
            NoireRemoteFeatures.Channels,
            NoireRemoteFeatures.Stream,
            NoireRemoteFeatures.Progress,
            NoireRemoteFeatures.Events,
            NoireRemoteFeatures.Metrics,
        };

        if (options.EnableFiles)
            features.Add(NoireRemoteFeatures.Files);

        if (options.EnableOpenApi)
            features.Add(NoireRemoteFeatures.OpenApi);

        if (options.EnableConsole)
            features.Add(NoireRemoteFeatures.Console);

        if (options.LogScope != NoireRemoteLogScope.None)
            features.Add(NoireRemoteFeatures.Log);

        if (options.AnnounceOnNetwork)
            features.Add(NoireRemoteFeatures.Fleet);

        features.Sort(StringComparer.Ordinal);

        return features;
    }

    private static void RewriteInto(RemoteSchemaBuilder schemas, JToken? schema)
    {
        if (schema != null)
            schemas.Rewrite(schema, schemas.Renames);
    }

    private static IReadOnlyList<NoireRemoteManifestEvent>? DescribeEvents(IReadOnlyList<NoireRemoteEventInfo>? events, RemoteSchemaBuilder schemas)
    {
        if (events == null || events.Count == 0)
            return null;

        var described = new List<NoireRemoteManifestEvent>(events.Count);

        foreach (var declared in events)
        {
            described.Add(new NoireRemoteManifestEvent
            {
                Name = declared.Name,
                Topic = declared.Topic,
                Channel = declared.Channel,
                Schema = declared.PayloadClrType == null ? null : schemas.Of(declared.PayloadClrType),
            });
        }

        return described;
    }

    private static NoireRemoteManifestMember Describe(NoireRemoteMemberInfo member, Dictionary<string, JObject> shapes, RemoteSchemaBuilder schemas)
    {
        var parameters = new List<NoireRemoteManifestParameter>(member.Parameters.Count);

        foreach (var parameter in member.Parameters)
        {
            if (parameter.IsCancellationToken || parameter.IsProgress)
                continue;

            var schema = parameter.Source != null ? schemas.OfParameter(parameter.Source) : schemas.Of(parameter.ClrType);

            Decorate(schema, parameter);

            parameters.Add(new NoireRemoteManifestParameter
            {
                Name = parameter.Name,
                JsonType = RemoteJsonContract.JsonTypeOf(parameter.ClrType, shapes),
                ClrType = parameter.ClrType.FullName ?? parameter.ClrType.Name,
                Required = parameter.IsRequired,
                Default = parameter.DefaultValue == null ? null : NoireRemoteJson.ToToken(parameter.DefaultValue),
                EnumValues = parameter.EnumValues,
                Summary = parameter.Summary,
                Schema = schema,
                Control = parameter.Control.ToString().ToLowerInvariant(),
            });
        }

        var returns = member.ReturnClrType == typeof(void)
            ? null
            : new NoireRemoteManifestValue
            {
                JsonType = RemoteJsonContract.JsonTypeOf(member.ReturnClrType, shapes),
                ClrType = member.ReturnClrType.FullName ?? member.ReturnClrType.Name,
                EnumValues = RemoteJsonContract.EnumValuesOf(member.ReturnClrType),
                Schema = schemas.Of(member.ReturnClrType),
            };

        return new NoireRemoteManifestMember
        {
            Name = member.Name,
            Route = member.Route,
            Thread = member.Thread.ToString().ToLowerInvariant(),
            Requires = member.Requires.ToString().ToLowerInvariant(),
            Mode = member.Mode.ToString().ToLowerInvariant(),
            TimeoutSeconds = member.Timeout.TotalSeconds,
            Parameters = parameters,
            Returns = returns,
            Summary = member.Summary,
            Access = member.Access.ToString().ToLowerInvariant(),
            Confirm = member.Confirm,
            Group = member.Group,
            Order = member.Order,
            Deprecated = member.Deprecated,
            ReadOnly = member.ReadOnly,
            Transports = TransportNames(member.Transports),
            Schema = returns?.Schema,
            Progress = member.ProgressClrType == null
                ? null
                : new NoireRemoteManifestValue
                {
                    JsonType = RemoteJsonContract.JsonTypeOf(member.ProgressClrType, shapes),
                    ClrType = member.ProgressClrType.FullName ?? member.ProgressClrType.Name,
                    Schema = schemas.Of(member.ProgressClrType),
                },
        };
    }

    // Attribute data with no JSON Schema keyword sits beside the schema.
    private static void Decorate(JObject schema, NoireRemoteParameterInfo parameter)
    {
        if (parameter.Summary != null)
            schema["description"] = parameter.Summary;

        if (!double.IsNaN(parameter.Minimum))
            schema["minimum"] = parameter.Minimum;

        if (!double.IsNaN(parameter.Maximum))
            schema["maximum"] = parameter.Maximum;

        if (!double.IsNaN(parameter.Step))
            schema["x-noire-step"] = parameter.Step;

        if (parameter.Example != null)
            schema["examples"] = new JArray { parameter.Example };

        if (parameter.DefaultValue != null)
            schema["default"] = NoireRemoteJson.ToToken(parameter.DefaultValue);
    }

    // "http" and "ws", like PROTOCOL.md.
    private static IReadOnlyList<string> TransportNames(NoireRemoteTransport transports)
    {
        var names = new List<string>(2);

        if ((transports & NoireRemoteTransport.Http) != 0)
            names.Add("http");

        if ((transports & NoireRemoteTransport.Websocket) != 0)
            names.Add("ws");

        return names;
    }
}
