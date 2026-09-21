using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;

namespace NoireLib.Websocket.Internal;

// Keyed by the listener's instance id. The HTTP half carries no field for sockets.
internal static class WebsocketRouting
{
    public static HttpRouteOutcome Route(string path, HttpRequestData request, bool isLoopback, HttpRouterContext context, string? requestId)
    {
        var server = NoireWebsocketServer.Find(context.Instance);

        if (server == null)
            return Refused(404, NoireRemoteErrorCodes.UnknownEndpoint, "This listener publishes no sockets.", context, requestId);

        var name = path.Substring(NoireWebsocketServer.RoutePrefix.Length).Trim('/');

        if (name.Length == 0 || name.IndexOf('/') >= 0)
            return Refused(404, NoireRemoteErrorCodes.UnknownEndpoint, "A socket route reads as " + NoireWebsocketServer.RoutePrefix + "{name}.", context, requestId);

        var endpoint = server.GetEndpoint(name);

        if (endpoint == null)
        {
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "This plugin publishes no socket named '" + name + "'.", name)
                {
                    Candidates = RemoteArgumentBinder.NearNames(server.EndpointNames(), name),
                },
                context.Instance, requestId);
        }

        if (!isLoopback && endpoint.Access != NoireRemoteAccess.Remote)
            return Refused(403, NoireRemoteErrorCodes.Forbidden, "'" + endpoint.Path + "' is reachable from this machine only.", context, requestId);

        string[] declared = [.. endpoint.Options.SubProtocols];
        var negotiated = WebsocketHandshake.Negotiate(request, declared, endpoint.Options.RequireSubProtocol);

        if (negotiated.Failure != null)
            return HttpRouteOutcome.From(negotiated.Failure, context.Instance, requestId);

        // The endpoint claims its slot, registers the connection and writes the 101, in that order.
        return new HttpRouteOutcome
        {
            Adopt = adoption => endpoint.AdoptAsync(adoption, negotiated.Accept, negotiated.SubProtocol),
        };
    }

    private static HttpRouteOutcome Refused(int status, string code, string message, HttpRouterContext context, string? requestId)
        => HttpRouteOutcome.From(new HttpFailure(status, code, message), context.Instance, requestId);
}
