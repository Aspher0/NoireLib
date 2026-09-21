using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// A browser cannot put an authorization header on a WebSocket, and the listener's credential must never reach the page.
// The listener holds the connection. The page says open, send and close over its live channel.
internal sealed class ConsoleSocketRelay : IDisposable
{
    internal const string InstancePrefix = "instance:";

    private readonly ConcurrentDictionary<string, NoireWebsocketClient> open = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpRouterContext context;
    private readonly Func<NoireRemoteConsoleLiveFrame, Task> send;

    private int disposed;

    internal ConsoleSocketRelay(HttpRouterContext context, Func<NoireRemoteConsoleLiveFrame, Task> send)
    {
        this.context = context;
        this.send = send;
    }

    internal async Task<NoireRemoteConsoleLiveFrame> HandleAsync(NoireRemoteConsoleLiveCommand command)
    {
        var name = command.Socket?.Trim();

        if (string.IsNullOrEmpty(name))
            return Refused("A socket command names the socket it is about.");

        if (string.Equals(command.Op, NoireRemoteConsoleLiveOps.SocketOpen, StringComparison.OrdinalIgnoreCase))
            return await OpenAsync(name!).ConfigureAwait(false);

        if (string.Equals(command.Op, NoireRemoteConsoleLiveOps.SocketSend, StringComparison.OrdinalIgnoreCase))
            return await SendToAsync(name!, command.Text ?? string.Empty).ConfigureAwait(false);

        return Close(name!);
    }

    private async Task<NoireRemoteConsoleLiveFrame> OpenAsync(string name)
    {
        if (open.ContainsKey(name))
            return State(name, "open");

        var options = new NoireWebsocketClientOptions { Retry = NoireRetryPolicy.None };
        string url;

        // A typed address gets no credential. A bare name is one of the listener's own sockets and gets its credential.
        if (name.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || name.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            url = name;
        }
        else if (name.StartsWith(InstancePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // A socket of another listener, with the credential from its record.
            var rest = name.Substring(InstancePrefix.Length).Split('/');

            if (rest.Length != 2 || !Guid.TryParse(rest[0], out var id))
                return Refused("A socket on another listener reads as instance:{id}/{socket}.");

            var instance = await HttpConsoleFleet.FindAsync(id, context, CancellationToken.None).ConfigureAwait(false);

            // Also null when the listener does not allow console control.
            if (instance == null)
                return Refused("No listener answering as " + rest[0] + " lets this console drive it.");

            if (string.IsNullOrEmpty(instance.Token))
                return Refused("That listener is on another machine. This console holds no credential for its sockets.");

            url = "ws://" + instance.Address + ":" + instance.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + NoireWebsocketServer.RoutePrefix + rest[1];

            options.Http.Headers[NoireRemoteHeaders.Authorization] = NoireRemoteHeaders.BearerScheme + " " + instance.Token;
        }
        else
        {
            var port = context.BaseUrl() is { } baseUrl && Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ? parsed.Port : 0;

            if (port == 0)
                return Refused("This listener is not serving. It publishes no socket to open.");

            url = "ws://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + NoireWebsocketServer.RoutePrefix + name;

            options.Http.Headers[NoireRemoteHeaders.Authorization] = NoireRemoteHeaders.BearerScheme + " " + Token();
        }

        var client = new NoireWebsocketClient(url, options);

        client.OnMessage(message => _ = send(Message(name, message.Text ?? string.Empty)));
        client.OnClose(close =>
        {
            open.TryRemove(name, out _);

            // The close code and reason tell a refusal from a peer that went away.
            _ = send(State(name, "closed " + (int)close.RawCode + (string.IsNullOrEmpty(close.Reason) ? string.Empty : " " + close.Reason)));
        });

        try
        {
            using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await client.ConnectAsync(connecting.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            client.Dispose();

            return Refused("'" + name + "' did not open: " + exception.Message);
        }

        open[name] = client;

        return State(name, "open");
    }

    private async Task<NoireRemoteConsoleLiveFrame> SendToAsync(string name, string text)
    {
        if (!open.TryGetValue(name, out var client))
            return Refused("'" + name + "' is not open on this session.");

        try
        {
            await client.SendAsync(text).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Refused("'" + name + "' refused the message: " + exception.Message);
        }

        return State(name, "sent");
    }

    private NoireRemoteConsoleLiveFrame Close(string name)
    {
        if (open.TryRemove(name, out var client))
            client.Dispose();

        return State(name, "closed");
    }

    // Never reaches the page.
    private string Token() => context.Token();

    private NoireRemoteConsoleLiveFrame State(string name, string state)
        => new()
        {
            Op = NoireRemoteConsoleLiveOps.SocketState,
            Instance = context.Instance,
            Socket = name,
            State = state,
        };

    private NoireRemoteConsoleLiveFrame Message(string name, string text)
        => new()
        {
            Op = NoireRemoteConsoleLiveOps.SocketMessage,
            Instance = context.Instance,
            Socket = name,
            Text = text,
        };

    private NoireRemoteConsoleLiveFrame Refused(string message)
        => new()
        {
            Op = NoireRemoteConsoleLiveOps.Error,
            Instance = context.Instance,
            Error = new NoireRemoteError { Code = NoireRemoteErrorCodes.BadRequest, Message = message },
        };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        foreach (var pair in open)
            pair.Value.Dispose();

        open.Clear();
    }
}
