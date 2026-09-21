using NoireLib.Remote;
using System;
using System.Collections;
using System.Collections.Generic;

namespace NoireLib.Websocket.Internal;

// A socket is reached directly, never proxied like an HTTP call.
internal static class WebsocketDiscovery
{
    public static IReadOnlyList<NoireRemoteManifestSocket>? Describe(Guid instance)
        => NoireWebsocketServer.Find(instance) == null ? null : new ManifestView(instance);

    // Folded into the manifest revision.
    public static int Revision(Guid instance)
    {
        var server = NoireWebsocketServer.Find(instance);

        return server == null ? 0 : server.Endpoints.Count;
    }

    public static Uri SocketUrl(string address, int port, string name)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("A socket URL needs the listener's address.", nameof(address));

        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "A port is between 1 and 65535.");

        if (!NoireRemotePaths.IsValidName(name))
            throw new ArgumentException("'" + name + "' is not a usable socket name.", nameof(name));

        // BaseUrl brackets an IPv6 address and carries the port.
        return new UriBuilder(NoireRemotePaths.BaseUrl(address, port))
        {
            Scheme = Uri.UriSchemeWs,
            Path = NoireWebsocketServer.RoutePrefix + name,
        }.Uri;
    }

    // The record's token is the credential that listener accepts on a loopback upgrade.
    public static NoireSocketCredential? CredentialFor(NoireRemoteInstanceRecord record)
        => string.IsNullOrEmpty(record.Token) ? null : NoireSocketCredential.Bearer(record.Token);

    // Same precedence as the HTTP transport: a shared secret off this machine, the loopback token on it.
    public static NoireSocketCredential? CredentialFor(NoireRemoteInstance instance)
    {
        if (!string.IsNullOrEmpty(instance.Secret))
            return NoireSocketCredential.Signed(instance.Secret!);

        return string.IsNullOrEmpty(instance.Token) ? null : NoireSocketCredential.Bearer(instance.Token!);
    }

    private static NoireRemoteManifestSocket Describe(NoireWebsocketEndpoint endpoint)
    {
        string[]? protocols = endpoint.Options.SubProtocols.Count == 0 ? null : [.. endpoint.Options.SubProtocols];

        return new NoireRemoteManifestSocket
        {
            Name = endpoint.Name,
            Route = endpoint.Path,
            Access = endpoint.Access.ToString().ToLowerInvariant(),
            Requires = endpoint.Requires.ToString().ToLowerInvariant(),
            Thread = endpoint.Thread.ToString().ToLowerInvariant(),
            SubProtocols = protocols,
            RequireSubProtocol = endpoint.Options.RequireSubProtocol,
        };
    }

    // The manifest is rebuilt only when the HTTP route table changes. Reading endpoints live shows sockets published later.
    private sealed class ManifestView(Guid instance) : IReadOnlyList<NoireRemoteManifestSocket>
    {
        public int Count => Current().Count;

        public NoireRemoteManifestSocket this[int index] => Current()[index];

        public IEnumerator<NoireRemoteManifestSocket> GetEnumerator()
            => Current().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        private List<NoireRemoteManifestSocket> Current()
        {
            var server = NoireWebsocketServer.Find(instance);
            var described = new List<NoireRemoteManifestSocket>();

            if (server == null)
                return described;

            foreach (var endpoint in server.Endpoints)
                described.Add(Describe(endpoint));

            described.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            return described;
        }
    }
}
