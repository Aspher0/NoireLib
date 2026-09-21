using NoireLib.Websocket;
using System;
using System.Linq;

namespace NoireLib.Remote.Internal;

// Who is connected to the published sockets, in which rooms, what crossed, and three actions.
// Reading needs the credential. Sending or closing is allowed only from the console or this machine, never from a signed network request.
internal static class HttpSocketRoutes
{
    public static HttpRouteOutcome Route(string[] segments, HttpRequestData request, bool isLoopback, HttpRouterContext context, string? requestId)
    {
        var sockets = NoireWebsocketServer.Find(context.Instance);

        if (segments.Length == 1)
        {
            if (request.Method != "GET")
                return Failure(context, requestId, 405, NoireRemoteErrorCodes.BadMethod, "This route takes GET.");

            return new HttpRouteOutcome { Body = Describe(sockets) };
        }

        if (request.Method != "POST")
            return Failure(context, requestId, 405, NoireRemoteErrorCodes.BadMethod, "This route takes POST.");

        if (!request.FromConsole && !isLoopback)
            return Failure(context, requestId, 403, NoireRemoteErrorCodes.Forbidden,
                "Acting on a socket's clients is the plugin's own business. Only its console and this machine may.");

        var endpoint = sockets?.Endpoints.FirstOrDefault(candidate => string.Equals(candidate.Name, segments[1], StringComparison.OrdinalIgnoreCase));

        if (endpoint == null || IsReserved(endpoint.Name))
            return Failure(context, requestId, 404, NoireRemoteErrorCodes.UnknownEndpoint, "This listener publishes no socket '" + segments[1] + "'.");

        NoireRemoteSocketAction? action;

        try
        {
            action = request.Body.Length == 0
                ? new NoireRemoteSocketAction()
                : NoireRemoteJson.Read<NoireRemoteSocketAction>(System.Text.Encoding.UTF8.GetString(request.Body));
        }
        catch (Exception)
        {
            return Failure(context, requestId, 400, NoireRemoteErrorCodes.BadRequest, "A socket action is a JSON object.");
        }

        action ??= new NoireRemoteSocketAction();

        if (segments.Length == 3 && segments[2] == "broadcast")
        {
            if (action.Text == null)
                return Failure(context, requestId, 400, NoireRemoteErrorCodes.BadRequest, "A broadcast carries text.");

            var room = string.IsNullOrWhiteSpace(action.Room) ? null : action.Room;

            // A broadcast that reached nobody looks like one that worked.
            var reached = room == null ? endpoint.ClientCount : endpoint.GetRoom(room)?.Count ?? 0;

            endpoint.Broadcast(action.Text, room);

            return Done(reached);
        }

        if (segments.Length == 5 && segments[2] == "clients" && Guid.TryParse(segments[3], out var id))
        {
            var client = endpoint.GetClient(id);

            if (client == null)
                return Failure(context, requestId, 404, NoireRemoteErrorCodes.UnknownEndpoint, "No client " + id + " is connected to '" + endpoint.Name + "'.");

            switch (segments[4])
            {
                case "send":
                    if (action.Text == null)
                        return Failure(context, requestId, 400, NoireRemoteErrorCodes.BadRequest, "A send carries text.");

                    client.Send(action.Text);
                    return Done(1);

                case "close":
                    var code = action.Code is >= 1000 and <= 4999 ? (NoireWebsocketCloseCode)action.Code.Value : NoireWebsocketCloseCode.Normal;

                    _ = client.CloseAsync(code, string.IsNullOrWhiteSpace(action.Reason) ? "Closed from the console." : action.Reason);
                    return Done(1);
            }
        }

        return Failure(context, requestId, 404, NoireRemoteErrorCodes.UnknownEndpoint,
            "A socket action reads as _sockets/{socket}/broadcast or _sockets/{socket}/clients/{id}/send|close.");
    }

    private static NoireRemoteSocketsState Describe(NoireWebsocketServer? sockets)
    {
        var state = new NoireRemoteSocketsState();

        if (sockets == null)
            return state;

        foreach (var endpoint in sockets.Endpoints.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (IsReserved(endpoint.Name))
                continue;

            var entry = new NoireRemoteSocketState
            {
                Name = endpoint.Name,
                Route = endpoint.Path,
                Access = endpoint.Access == NoireRemoteAccess.Remote ? "remote" : "local",
                Thread = endpoint.Thread.ToString().ToLowerInvariant(),
                MessagesIn = endpoint.MessagesIn,
                MessagesOut = endpoint.MessagesOut,
                BytesIn = endpoint.BytesIn,
                BytesOut = endpoint.BytesOut,
            };

            foreach (var client in endpoint.Clients.OrderBy(candidate => candidate.OpenedUtc))
            {
                entry.Clients.Add(new NoireRemoteSocketClient
                {
                    Id = client.Id,
                    Address = client.RemoteAddress,
                    IsLoopback = client.IsLoopback,
                    SubProtocol = client.SubProtocol,
                    OpenedUtc = client.OpenedUtc,
                    Rooms = client.Rooms,
                    MessagesIn = client.MessagesIn,
                    MessagesOut = client.MessagesOut,
                    BytesIn = client.BytesIn,
                    BytesOut = client.BytesOut,
                    Queued = client.QueuedMessages,
                });
            }

            foreach (var room in endpoint.Rooms.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
                entry.Rooms.Add(new NoireRemoteSocketRoom { Name = room.Name, Count = room.Count });

            state.Sockets.Add(entry);
        }

        return state;
    }

    private static HttpRouteOutcome Done(int reached)
        => new() { Body = new NoireRemoteSocketsDone { Reached = reached } };

    private static bool IsReserved(string name)
        => name.Length > 0 && name[0] == '_';

    private static HttpRouteOutcome Failure(HttpRouterContext context, string? requestId, int status, string code, string message)
        => HttpRouteOutcome.From(new HttpFailure(status, code, message), context.Instance, requestId);
}

internal sealed class NoireRemoteSocketsDone
{
    [Newtonsoft.Json.JsonProperty("ok")]
    public bool Ok { get; set; } = true;

    [Newtonsoft.Json.JsonProperty("reached")]
    public int Reached { get; set; }
}
