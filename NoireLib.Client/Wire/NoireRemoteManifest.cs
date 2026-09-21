using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// The published surface of one listener, as served by the manifest route.
/// </summary>
public sealed class NoireRemoteManifest
{
    /// <summary>
    /// Gets or sets the protocol version the listener speaks.
    /// </summary>
    public int Protocol { get; set; } = NoireRemotePaths.Protocol;

    /// <summary>
    /// Gets or sets the internal name of the plugin that published the surface.
    /// </summary>
    public string Plugin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the plugin that published the surface.
    /// </summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the instance id of the listener.
    /// </summary>
    public Guid Instance { get; set; }

    /// <summary>
    /// Gets or sets the endpoints.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestEndpoint> Endpoints { get; set; } = [];

    /// <summary>
    /// Gets or sets the shapes referenced by a <c>@Name</c> JSON type, keyed by that name. It is the older of the two
    /// type descriptions and it loses element types, required-ness and nullability. Read <see cref="Defs"/> instead.
    /// </summary>
    public IDictionary<string, JObject>? Types { get; set; }

    /// <summary>
    /// Gets or sets the number the route table is on. It changes whenever a publication is added or removed. A
    /// reader checks this number, and skips diffing the whole document when it has not changed.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Gets or sets what this listener serves, as a list of feature names. A feature that is turned off is absent.
    /// </summary>
    public IReadOnlyList<string>? Features { get; set; }

    /// <summary>
    /// Gets or sets the channels events are published on.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestChannel>? Channels { get; set; }

    /// <summary>
    /// Gets or sets the sockets the listener publishes, or null when it publishes none. A socket is opened directly
    /// against the listener that serves it and is never reachable through another listener's fleet proxy.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestSocket>? Sockets { get; set; }

    /// <summary>
    /// Gets or sets the JSON Schema definitions every <c>schema</c> field references.
    /// </summary>
    [JsonProperty("$defs")]
    public IDictionary<string, JObject>? Defs { get; set; }

    /// <summary>
    /// Checks whether the listener advertises a feature.
    /// </summary>
    /// <param name="feature">The feature name, as <see cref="NoireRemoteFeatures"/> spells it.</param>
    /// <returns>True when the listener lists it.</returns>
    public bool Has(string feature)
    {
        if (Features == null)
            return false;

        foreach (var name in Features)
        {
            if (string.Equals(name, feature, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds a published socket by name, ignoring case.
    /// </summary>
    /// <param name="name">The socket name.</param>
    /// <returns>The socket, or null when the manifest does not carry it.</returns>
    public NoireRemoteManifestSocket? GetSocket(string name)
    {
        if (Sockets == null)
            return null;

        foreach (var socket in Sockets)
        {
            if (string.Equals(socket.Name, name, StringComparison.OrdinalIgnoreCase))
                return socket;
        }

        return null;
    }

    /// <summary>
    /// Finds an endpoint by name, ignoring case.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    /// <returns>The endpoint, or null when the manifest does not carry it.</returns>
    public NoireRemoteManifestEndpoint? GetEndpoint(string name)
    {
        foreach (var endpoint in Endpoints)
        {
            if (string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase))
                return endpoint;
        }

        return null;
    }
}

/// <summary>
/// One endpoint in a manifest.
/// </summary>
public sealed class NoireRemoteManifestEndpoint
{
    /// <summary>
    /// Gets or sets the endpoint name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the endpoint accepts a call from another machine.
    /// </summary>
    public string Access { get; set; } = "local";

    /// <summary>
    /// Gets or sets what the endpoint is for, taken from the attribute.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the members.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestMember> Members { get; set; } = [];

    /// <summary>
    /// Gets or sets the events the endpoint publishes.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestEvent>? Events { get; set; }

    /// <summary>
    /// Finds an event by name, ignoring case.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <returns>The event, or null when the endpoint does not publish it.</returns>
    public NoireRemoteManifestEvent? GetEvent(string name)
    {
        if (Events == null)
            return null;

        foreach (var item in Events)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    /// <summary>
    /// Finds a member by name, ignoring case.
    /// </summary>
    /// <param name="name">The member name.</param>
    /// <returns>The member, or null when the endpoint does not publish it.</returns>
    public NoireRemoteManifestMember? GetMember(string name)
    {
        foreach (var member in Members)
        {
            if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                return member;
        }

        return null;
    }
}

/// <summary>
/// One published member in a manifest.
/// </summary>
public sealed class NoireRemoteManifestMember
{
    /// <summary>
    /// Gets or sets the member name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the route, endpoint and member joined by a slash.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the thread the member runs on.
    /// </summary>
    public string Thread { get; set; } = "framework";

    /// <summary>
    /// Gets or sets the readiness gate the member passes.
    /// </summary>
    public string Requires { get; set; } = "none";

    /// <summary>
    /// Gets or sets the default call mode.
    /// </summary>
    public string Mode { get; set; } = "sync";

    /// <summary>
    /// Gets or sets the member's deadline in seconds.
    /// </summary>
    public double TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the parameters, in declaration order.
    /// </summary>
    public IReadOnlyList<NoireRemoteManifestParameter> Parameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the return shape, or null when the member returns nothing.
    /// </summary>
    public NoireRemoteManifestValue? Returns { get; set; }

    /// <summary>
    /// Gets or sets what the member does, taken from the attribute.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets whether the member accepts a call from another machine. A page on a phone has no other way to
    /// tell which members it may call.
    /// </summary>
    public string Access { get; set; } = "local";

    /// <summary>
    /// Gets or sets whether a console asks before firing the member.
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>
    /// Gets or sets the group a console lists the member under.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets where the member sits in its group. Lower comes first and equal orders keep their name order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets whether the member is on its way out.
    /// </summary>
    public bool Deprecated { get; set; }

    /// <summary>Gets or sets the wires the member is reachable on, as "http" and "ws".</summary>
    // Replaced on read. Merging into the default would show a socket-only member as reachable over HTTP.
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public IReadOnlyList<string> Transports { get; set; } = ["http", "ws"];

    /// <summary>
    /// Gets or sets whether the member only reads, the case for a published property.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets a short fingerprint of what calling this member means: its API, its name, its kind of call, its
    /// parameters with their defaults, and its answer, every type resolved by shape. Two listeners whose member carries
    /// the same fingerprint take the same call and answer the same way, whatever their plugin or version.
    /// </summary>
    public string? Contract { get; set; }

    /// <summary>
    /// Gets or sets the JSON Schema of the return value, referencing the manifest's definitions.
    /// </summary>
    public JObject? Schema { get; set; }

    /// <summary>
    /// Gets or sets the shape of the progress reports the member publishes, or null when it publishes none.
    /// </summary>
    public NoireRemoteManifestValue? Progress { get; set; }
}

/// <summary>
/// One parameter of a published member.
/// </summary>
public sealed class NoireRemoteManifestParameter
{
    /// <summary>Gets or sets the parameter name. A caller sends it as the key.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON type, one of the closed vocabulary or a <c>@Name</c> reference.
    /// </summary>
    public string JsonType { get; set; } = "object";

    /// <summary>
    /// Gets or sets the CLR type name, for a C# caller checking its interface.
    /// </summary>
    public string ClrType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether a call has to supply the parameter.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the value used when a call leaves the parameter out.
    /// </summary>
    public JToken? Default { get; set; }

    /// <summary>
    /// Gets or sets the accepted names when the JSON type is an enum.
    /// </summary>
    public IReadOnlyList<string>? EnumValues { get; set; }

    /// <summary>
    /// Gets or sets what the parameter means, taken from the attribute.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the JSON Schema of the parameter, referencing the manifest's definitions.
    /// </summary>
    public JObject? Schema { get; set; }

    /// <summary>
    /// Gets or sets the control a console draws for the parameter. Auto lets the console pick from the schema.
    /// </summary>
    public string Control { get; set; } = "auto";
}

/// <summary>
/// One event a published endpoint declares.
/// </summary>
public sealed class NoireRemoteManifestEvent
{
    /// <summary>
    /// Gets or sets the event name as the type declares it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the topic the event publishes under.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel the event publishes on.
    /// </summary>
    public string Channel { get; set; } = NoireRemoteChannels.Events;

    /// <summary>
    /// Gets or sets the JSON Schema of the payload, referencing the manifest's definitions.
    /// </summary>
    public JObject? Schema { get; set; }
}

/// <summary>
/// One socket the listener publishes. A caller upgrades on <see cref="Route"/> of the same address and port the rest
/// of the listener answers on, with the same credential an HTTP call carries.
/// </summary>
public sealed class NoireRemoteManifestSocket
{
    /// <summary>
    /// Gets or sets the socket name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path an upgrade is sent to.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the socket accepts an upgrade from another machine.
    /// </summary>
    public string Access { get; set; } = "local";

    /// <summary>
    /// Gets or sets the readiness gate a delivery passes.
    /// </summary>
    public string Requires { get; set; } = "none";

    /// <summary>
    /// Gets or sets the thread handlers run on.
    /// </summary>
    public string Thread { get; set; } = "framework";

    /// <summary>
    /// Gets or sets the sub-protocols the socket speaks, in order of preference, or null when it speaks none.
    /// </summary>
    public IReadOnlyList<string>? SubProtocols { get; set; }

    /// <summary>
    /// Gets or sets whether a handshake offering none of <see cref="SubProtocols"/> is refused.
    /// </summary>
    public bool RequireSubProtocol { get; set; }
}

/// <summary>
/// One channel the listener buffers events on.
/// </summary>
public sealed class NoireRemoteManifestChannel
{
    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many events the channel keeps before the oldest are dropped.
    /// </summary>
    public int BufferSize { get; set; }
}

/// <summary>
/// The shape of a member's return value.
/// </summary>
public sealed class NoireRemoteManifestValue
{
    /// <summary>
    /// Gets or sets the JSON type, one of the closed vocabulary or a <c>@Name</c> reference.
    /// </summary>
    public string JsonType { get; set; } = "object";

    /// <summary>
    /// Gets or sets the CLR type name.
    /// </summary>
    public string ClrType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the accepted names when the JSON type is an enum.
    /// </summary>
    public IReadOnlyList<string>? EnumValues { get; set; }

    /// <summary>
    /// Gets or sets the JSON Schema of the value, referencing the manifest's definitions.
    /// </summary>
    public JObject? Schema { get; set; }
}

/// <summary>
/// The closed vocabulary the manifest writes into a <c>jsonType</c> field.
/// </summary>
public static class NoireRemoteJsonTypes
{
    /// <summary>A JSON string.</summary>
    public const string String = "string";

    /// <summary>A whole number.</summary>
    public const string Integer = "integer";

    /// <summary>A number with a fractional part.</summary>
    public const string Number = "number";

    /// <summary>A JSON boolean.</summary>
    public const string Boolean = "boolean";

    /// <summary>Always null.</summary>
    public const string Null = "null";

    /// <summary>A JSON object whose shape the manifest does not describe.</summary>
    public const string Object = "object";

    /// <summary>A JSON array.</summary>
    public const string Array = "array";

    /// <summary>A JSON object used as a string-keyed map.</summary>
    public const string Map = "map";

    /// <summary>An object carrying x and y.</summary>
    public const string Vector2 = "vector2";

    /// <summary>An object carrying x, y and z.</summary>
    public const string Vector3 = "vector3";

    /// <summary>An object carrying x, y, z and w.</summary>
    public const string Vector4 = "vector4";

    /// <summary>An object carrying x, y, z and w, read as a rotation.</summary>
    public const string Quaternion = "quaternion";

    /// <summary>A string drawn from a fixed set, listed in the enum values.</summary>
    public const string Enum = "enum";

    /// <summary>The prefix of a reference into the manifest's type table.</summary>
    public const string ShapePrefix = "@";
}
