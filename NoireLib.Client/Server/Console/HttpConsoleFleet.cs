using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// The API gate refuses browser headers and the page holds no other listener's credential. The host serving the page acts for it.
// Consent belongs to the driven listener: its AllowFleetControl, read fresh from its record on every action.
internal static class HttpConsoleFleet
{
    public static async Task<HttpRouteOutcome> RouteAsync(
        string path,
        HttpRequestData request,
        HttpRouterContext context,
        CancellationToken cancellationToken)
    {
        if (string.Equals(path, NoireRemoteConsolePaths.Fleet, StringComparison.Ordinal))
            return await ListAsync(context, cancellationToken).ConfigureAwait(false);

        if (string.Equals(path, NoireRemoteConsolePaths.FleetManifest, StringComparison.Ordinal))
            return await ManifestAsync(request, context, cancellationToken).ConfigureAwait(false);

        if (string.Equals(path, NoireRemoteConsolePaths.FleetSockets, StringComparison.Ordinal)
            || path.StartsWith(NoireRemoteConsolePaths.FleetSockets + "/", StringComparison.Ordinal))
            return await SocketsAsync(path, request, context, cancellationToken).ConfigureAwait(false);

        var remainder = path.Substring(NoireRemoteConsolePaths.Fleet.Length).Trim('/');
        var segments = remainder.Length == 0 ? [] : remainder.Split('/');

        if (segments.Length != 2)
            return Failure(context, 404, NoireRemoteErrorCodes.UnknownEndpoint,
                "A fleet call reads as " + NoireRemoteConsolePaths.Fleet + "/{endpoint}/{member}.");

        return await CallAsync(segments[0], segments[1], request, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpRouteOutcome> ListAsync(HttpRouterContext context, CancellationToken cancellationToken)
    {
        var hosts = new List<NoireRemoteFleetHost>();

        foreach (var instance in await DiscoverAsync(context, cancellationToken).ConfigureAwait(false))
        {
            var self = instance.Id == context.Instance;

            if (!self && !instance.AllowFleetControl)
                continue;

            hosts.Add(new NoireRemoteFleetHost
            {
                Instance = instance.Id,
                Machine = instance.Machine,
                Label = instance.Label,
                Plugin = instance.Plugin,
                PluginVersion = instance.PluginVersion,
                Address = instance.Address,
                Port = instance.Port,
                ProcessId = instance.ProcessId,
                Endpoints = instance.Endpoints,
                IsReachable = instance.IsReachable,
                IsSelf = self,
            });
        }

        return new HttpRouteOutcome { Body = new NoireRemoteFleetListing { Hosts = hosts } };
    }

    private static async Task<HttpRouteOutcome> ManifestAsync(HttpRequestData request, HttpRouterContext context, CancellationToken cancellationToken)
    {
        if (!TryInstance(request, out var id))
            return Failure(context, 400, NoireRemoteErrorCodes.BadRequest, "This route names the listener as ?instance={id}.");

        if (id == context.Instance)
            return new HttpRouteOutcome { Body = context.Manifest() };

        var instance = await FindAsync(id, context, cancellationToken).ConfigureAwait(false);

        if (instance == null)
            return Unreachable(context, id);

        try
        {
            return new HttpRouteOutcome { Body = await instance.ManifestAsync(cancellationToken).ConfigureAwait(false) };
        }
        catch (Exception exception)
        {
            return Failure(context, 502, NoireRemoteErrorCodes.BadRequest, "That listener did not hand over its manifest: " + exception.Message);
        }
    }

    // Answers and refusals are forwarded as they came.
    private static async Task<HttpRouteOutcome> SocketsAsync(string path, HttpRequestData request, HttpRouterContext context, CancellationToken cancellationToken)
    {
        if (!TryInstance(request, out var id))
            return Failure(context, 400, NoireRemoteErrorCodes.BadRequest, "This route names the listener as ?instance={id}.");

        var instance = await FindAsync(id, context, cancellationToken).ConfigureAwait(false);

        if (instance == null)
            return Unreachable(context, id);

        var rest = path.Substring(NoireRemoteConsolePaths.FleetSockets.Length);
        var method = request.Method == "POST" ? System.Net.Http.HttpMethod.Post : System.Net.Http.HttpMethod.Get;
        object? body = request.Method == "POST" && request.Body.Length > 0
            ? NoireRemoteJson.Read<NoireRemoteSocketAction>(System.Text.Encoding.UTF8.GetString(request.Body))
            : null;

        string text;

        try
        {
            text = await RemoteWireTransport.SendAsync(
                instance, method, NoireRemotePaths.Prefix + NoireRemotePaths.Sockets + rest, body,
                NoireRemoteClient.Options.CallTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failure(context, 502, NoireRemoteErrorCodes.BadRequest, "That listener did not answer: " + exception.Message);
        }

        return new HttpRouteOutcome
        {
            Write = (stream, token) => HttpResponseWriter.WriteBytesAsync(
                stream, 200, NoireRemoteHeaders.JsonContentTypeWithCharset, System.Text.Encoding.UTF8.GetBytes(text), context.Instance, null, token),
        };
    }

    // Read fresh on every action. Withdrawn consent is refused at once.
    internal static async Task<NoireRemoteInstance?> FindAsync(Guid id, HttpRouterContext context, CancellationToken cancellationToken)
    {
        foreach (var candidate in await DiscoverAsync(context, cancellationToken).ConfigureAwait(false))
        {
            if (candidate.Id != id)
                continue;

            return candidate.Id == context.Instance || candidate.AllowFleetControl ? candidate : null;
        }

        return null;
    }

    // A listener whose member changed contract since the page read it is refused on its own row.
    private static async Task<HttpRouteOutcome> CallAsync(string endpoint, string member, HttpRequestData request, HttpRouterContext context, CancellationToken cancellationToken)
    {
        if (request.Method != "POST")
            return Failure(context, 405, NoireRemoteErrorCodes.BadMethod, "This route takes POST.");

        NoireRemoteFleetCall? body;

        try
        {
            body = NoireRemoteJson.Read<NoireRemoteFleetCall>(System.Text.Encoding.UTF8.GetString(request.Body));
        }
        catch (Exception)
        {
            return Failure(context, 400, NoireRemoteErrorCodes.BadRequest, "The fleet call body is not valid JSON.");
        }

        if (body?.Instances is not { Count: > 0 } wanted)
            return Failure(context, 400, NoireRemoteErrorCodes.BadRequest, "A fleet call names the listeners it goes to.");

        var discovered = await DiscoverAsync(context, cancellationToken).ConfigureAwait(false);
        var started = Stopwatch.StartNew();

        var rows = await Task.WhenAll(wanted.Distinct().Select(id => OneAsync(id))).ConfigureAwait(false);

        return new HttpRouteOutcome
        {
            Body = new NoireRemoteBroadcastWire
            {
                Answered = rows.Count(row => row.Ok),
                Total = rows.Length,
                ElapsedMs = (long)started.Elapsed.TotalMilliseconds,
                Outcomes = rows,
            },
        };

        async Task<NoireRemoteBroadcastRow> OneAsync(Guid id)
        {
            var each = Stopwatch.StartNew();
            var instance = discovered.FirstOrDefault(candidate => candidate.Id == id);
            var row = new NoireRemoteBroadcastRow { Instance = id };

            if (instance == null || (instance.Id != context.Instance && !instance.AllowFleetControl))
                return Refuse(row, each, NoireRemoteErrorCodes.UnknownEndpoint, "That listener is gone, or no longer lets a console drive it.");

            row.Label = instance.Label;
            row.Machine = instance.Machine;
            row.Address = instance.Address;
            row.Port = instance.Port;

            try
            {
                var manifest = instance.Id == context.Instance
                    ? context.Manifest()
                    : await instance.ManifestAsync(cancellationToken).ConfigureAwait(false);

                var described = manifest.Endpoints
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, endpoint, StringComparison.OrdinalIgnoreCase))?
                    .Members.FirstOrDefault(candidate => string.Equals(candidate.Name, member, StringComparison.OrdinalIgnoreCase));

                if (described == null)
                    return Refuse(row, each, NoireRemoteErrorCodes.UnknownMember, "That listener publishes no '" + endpoint + "/" + member + "'.");

                if (!string.IsNullOrEmpty(body.Contract) && !string.Equals(described.Contract, body.Contract, StringComparison.Ordinal))
                    return Refuse(row, each, NoireRemoteErrorCodes.ContractChanged, "That listener's '" + member + "' is not the one the page showed. Its contract changed.");

                var arguments = body.Args ?? new JObject();

                if (described.Transports == null || described.Transports.Contains("http", StringComparer.OrdinalIgnoreCase))
                {
                    var envelope = await new NoireRemoteApi(endpoint, instance).PostToAsync(instance, member, arguments, null, cancellationToken).ConfigureAwait(false);

                    row.Ok = true;
                    row.Result = envelope.Result;
                }
                else
                {
                    // Served over the socket alone.
                    using var client = new NoireRemoteClient(endpoint, NoireRemoteTransport.Websocket);

                    var frame = await client.CallOverSocketAsync(instance, member, arguments, cancellationToken).ConfigureAwait(false);

                    if (frame.Error != null)
                        return Refuse(row, each, frame.Error.Code, frame.Error.Message);

                    row.Ok = true;
                    row.Result = frame.Payload;
                }
            }
            catch (NoireRemoteException exception)
            {
                return Refuse(row, each, exception.Code ?? NoireRemoteErrorCodes.HandlerFault, exception.Message);
            }
            catch (Exception exception)
            {
                return Refuse(row, each, NoireRemoteErrorCodes.HandlerFault, exception.Message);
            }

            row.ElapsedMs = (long)each.Elapsed.TotalMilliseconds;

            return row;
        }
    }

    private static NoireRemoteBroadcastRow Refuse(NoireRemoteBroadcastRow row, Stopwatch each, string code, string message)
    {
        row.Ok = false;
        row.Error = new NoireRemoteError { Code = code, Message = message };
        row.ElapsedMs = (long)each.Elapsed.TotalMilliseconds;

        return row;
    }

    private static async Task<IReadOnlyList<NoireRemoteInstance>> DiscoverAsync(HttpRouterContext context, CancellationToken cancellationToken)
    {
        var options = NoireRemoteClient.Options.Clone();

        options.NetworkSecret = context.Options.RemoteSecret;
        options.DiscoveryPort = context.Options.DiscoveryPort;
        options.ExcludeInstance = null;

        return await NoireRemoteClient.DiscoverAsync(null, cancellationToken, options).ConfigureAwait(false);
    }

    private static bool TryInstance(HttpRequestData request, out Guid id)
    {
        id = Guid.Empty;

        return HttpQueryString.Parse(request.Target).TryGetValue("instance", out var wanted) && Guid.TryParse(wanted, out id);
    }

    private static HttpRouteOutcome Unreachable(HttpRouterContext context, Guid id)
        => Failure(context, 404, NoireRemoteErrorCodes.UnknownEndpoint,
            "No listener answering as " + id + " lets this console drive it.");

    private static HttpRouteOutcome Failure(HttpRouterContext context, int status, string code, string message)
        => HttpRouteOutcome.From(new HttpFailure(status, code, message), context.Instance, null);
}
