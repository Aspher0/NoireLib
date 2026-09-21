using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Remote.Internal;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// An open console page follows its plugin: a setting, a style, a socket client or a listener on the fleet that moves
/// reaches the page over the live channel as an invalidation, and the page reads that part of itself again. The
/// sockets view reads who is connected, in which rooms and what has crossed, and acts on it.
/// </summary>
public sealed class NoireRemoteConsoleLiveUpdateTests : IDisposable
{
    private readonly HttpClient client = new();
    private readonly List<IDisposable> owned = [];

    public void Dispose()
    {
        foreach (var item in owned)
            item.Dispose();

        client.Dispose();
    }

    [Fact]
    public async Task ASettingThePageShows_ChangedByThePlugin_ReachesTheOpenPage()
    {
        var server = NewConsole();
        using var socket = await OpenLiveAsync(server);

        server.Options.AllowFleetControl = !server.Options.AllowFleetControl;

        (await ReadInvalidationAsync(socket)).Should().Be("settings");
    }

    [Fact]
    public async Task RestylingTheConsole_ReachesTheOpenPage()
    {
        var server = NewConsole();
        using var socket = await OpenLiveAsync(server);

        server.Console.Style.Variables["accent"] = "#ff00aa";

        (await ReadInvalidationAsync(socket)).Should().Be("settings");
    }

    [Fact]
    public async Task AClientConnectingToASocket_ReachesTheOpenPage()
    {
        var server = NewConsole();

        using var socket = await OpenLiveAsync(server);

        var endpoint = NoireWebsocketServer.For(server).Publish("lobby");

        owned.Add(endpoint);
        (await ReadInvalidationAsync(socket)).Should().Be("sockets");

        using var peer = await ConnectAsync(server, "lobby");

        (await ReadInvalidationAsync(socket)).Should().Be("sockets");
    }

    [Fact]
    public async Task TheSocketsRoute_SaysWhoIsConnectedInWhichRoomsAndWhatCrossed()
    {
        var server = NewConsole();
        var endpoint = NoireWebsocketServer.For(server).Publish("lobby");

        owned.Add(endpoint);
        endpoint.OnMessage((connection, message) => connection.Join("red"));

        using var peer = await ConnectAsync(server, "lobby");
        await peer.SendAsync("hello", TestContext.Current.CancellationToken);

        var state = await ReadStateAsync(server, lobby => lobby.Rooms.Count > 0);
        var lobby = state.Sockets.Single(entry => entry.Name == "lobby");

        lobby.Clients.Should().ContainSingle().Which.Rooms.Should().Equal("red");
        lobby.Rooms.Should().ContainSingle().Which.Should().BeEquivalentTo(new NoireRemoteSocketRoom { Name = "red", Count = 1 });
        lobby.MessagesIn.Should().Be(1);
        lobby.BytesIn.Should().Be(5);

        state.Sockets.Should().NotContain(entry => entry.Name.StartsWith('_'));
    }

    [Fact]
    public async Task ABroadcastFromTheRoute_ReachesTheClients()
    {
        var server = NewConsole();
        var endpoint = NoireWebsocketServer.For(server).Publish("lobby");

        owned.Add(endpoint);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var peer = await ConnectAsync(server, "lobby", text => received.TrySetResult(text));
        await ReadStateAsync(server, lobby => lobby.Clients.Count == 1);

        var (status, body) = await PostWithBodyAsync(server, "/lobby/broadcast", new NoireRemoteSocketAction { Text = "to everyone" });

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"reached\":1");

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Be("to everyone");
    }

    [Fact]
    public async Task ABroadcastToARoomNobodyIsIn_SaysItReachedNobody()
    {
        var server = NewConsole();
        var endpoint = NoireWebsocketServer.For(server).Publish("lobby");

        owned.Add(endpoint);

        using var peer = await ConnectAsync(server, "lobby");
        await ReadStateAsync(server, lobby => lobby.Clients.Count == 1);

        var (status, body) = await PostWithBodyAsync(server, "/lobby/broadcast", new NoireRemoteSocketAction { Text = "hello", Room = "empty" });

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"reached\":0");
    }

    [Fact]
    public async Task ASendToOneClient_ReachesThatClientAndNoOther()
    {
        var server = NewConsole();
        var endpoint = NoireWebsocketServer.For(server).Publish("lobby");

        owned.Add(endpoint);

        var first = new ConcurrentQueue<string>();
        var second = new ConcurrentQueue<string>();

        using var one = await ConnectAsync(server, "lobby", first.Enqueue);
        using var two = await ConnectAsync(server, "lobby", second.Enqueue);

        var state = await ReadStateAsync(server, lobby => lobby.Clients.Count == 2);
        var target = state.Sockets.Single(entry => entry.Name == "lobby").Clients[0].Id;

        (await PostAsync(server, "/lobby/clients/" + target + "/send", new NoireRemoteSocketAction { Text = "just you" })).Should().Be(HttpStatusCode.OK);

        await WaitUntilAsync(() => first.Count + second.Count > 0);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        (first.Count + second.Count).Should().Be(1, "a send names one connection");
    }

    [Fact]
    public async Task ClosingAClientFromTheRoute_TakesItOffTheSocket()
    {
        var server = NewConsole();
        var endpoint = NoireWebsocketServer.For(server).Publish("lobby");

        owned.Add(endpoint);

        using var peer = await ConnectAsync(server, "lobby");

        var state = await ReadStateAsync(server, lobby => lobby.Clients.Count == 1);
        var id = state.Sockets.Single(entry => entry.Name == "lobby").Clients[0].Id;

        (await PostAsync(server, "/lobby/clients/" + id + "/close", new NoireRemoteSocketAction { Reason = "bye" })).Should().Be(HttpStatusCode.OK);

        await ReadStateAsync(server, lobby => lobby.Clients.Count == 0);
    }

    [Fact]
    public void EverySetting_SaysWhichOneMoved()
    {
        // Pins that every setting raises Changed. A silent setter leaves the page drawing the old value.
        var options = new NoireRemoteOptions();
        var heard = new List<string?>();
        var changed = typeof(NoireRemoteOptions).GetEvent("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!;

        changed.GetAddMethod(true)!.Invoke(options, [new Action<string?>(heard.Add)]);

        var properties = typeof(NoireRemoteOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanWrite && property.GetSetMethod() != null)
            .ToList();

        properties.Should().NotBeEmpty();

        foreach (var property in properties)
            property.SetValue(options, Different(property, property.GetValue(options)));

        foreach (var property in properties)
            heard.Should().Contain(property.Name, property.Name + " changed without saying so");
    }

    [Fact]
    public async Task AListenerAppearingOnThisMachine_RedrawsTheFleet_AndAHeartbeatAloneDoesNot()
    {
        var folder = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        var redraws = 0;

        using var watcher = new ConsoleFleetWatcher(new NoireRemoteOptions { RegistryDirectory = folder }, () => Interlocked.Increment(ref redraws));

        var record = Path.Combine(folder, "one.json");

        File.WriteAllText(record, "{\"instance\":\"a\",\"port\":1,\"heartbeatUtc\":\"2026-01-01T00:00:00Z\"}");
        await WaitUntilAsync(() => Volatile.Read(ref redraws) == 1);

        // The same record with only a fresh heartbeat: the fleet has not changed, and the page is left alone.
        File.WriteAllText(record, "{\"instance\":\"a\",\"port\":1,\"heartbeatUtc\":\"2026-01-01T00:00:15Z\"}");
        await Task.Delay(900, TestContext.Current.CancellationToken);

        Volatile.Read(ref redraws).Should().Be(1);

        File.Delete(record);
        await WaitUntilAsync(() => Volatile.Read(ref redraws) == 2);

        try
        {
            Directory.Delete(folder, true);
        }
        catch (IOException)
        {
        }
    }

    private NoireRemoteServer NewConsole()
    {
        var server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "live-update-probe" }, new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EnableConsole = true,
        });

        server.Start();
        owned.Add(server);

        return server;
    }

    private async Task<ClientWebSocket> OpenLiveAsync(NoireRemoteServer server)
    {
        using var response = await client.PostAsync(
            "http://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Session,
            new StringContent(NoireRemoteJson.Write(new NoireRemoteConsoleGrant()), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        var token = NoireRemoteJson.Read<NoireRemoteConsoleSession>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.Token;

        var socket = new ClientWebSocket();

        socket.Options.AddSubProtocol(ConsoleLiveSocket.SubProtocol);
        socket.Options.AddSubProtocol(token);

        await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + server.Console.Port + NoireRemoteConsolePaths.Live), TestContext.Current.CancellationToken);

        (await ReadFrameAsync(socket, TestContext.Current.CancellationToken)).Op.Should().Be(NoireRemoteConsoleLiveOps.Ready);

        return socket;
    }

    private static async Task<string?> ReadInvalidationAsync(ClientWebSocket socket)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            var frame = await ReadFrameAsync(socket, deadline.Token);

            if (frame.Op == NoireRemoteConsoleLiveOps.Invalidate)
                return frame.Scope;
        }
    }

    private static async Task<NoireRemoteConsoleLiveFrame> ReadFrameAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("The channel closed.");

            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
                break;
        }

        return NoireRemoteJson.Read<NoireRemoteConsoleLiveFrame>(text.ToString())!;
    }

    private static async Task<NoireWebsocketClient> ConnectAsync(NoireRemoteServer server, string socket, Action<string>? received = null)
    {
        var peer = new NoireWebsocketClient(
            "ws://127.0.0.1:" + server.Port + NoireWebsocketServer.RoutePrefix + socket,
            new NoireWebsocketClientOptions { Retry = NoireRetryPolicy.None });

        peer.Options.Http.Headers[NoireRemoteHeaders.Authorization] = NoireRemoteHeaders.BearerScheme + " " + server.Token;

        if (received != null)
            peer.OnMessage(message => received(message.Text ?? string.Empty));

        await peer.ConnectAsync(TestContext.Current.CancellationToken);

        return peer;
    }

    private async Task<NoireRemoteSocketsState> ReadStateAsync(NoireRemoteServer server, Func<NoireRemoteSocketState, bool> until)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + server.Port + NoireRemotePaths.Prefix + NoireRemotePaths.Sockets);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            var state = NoireRemoteJson.Read<NoireRemoteSocketsState>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
            var lobby = state.Sockets.FirstOrDefault(entry => entry.Name == "lobby");

            if (lobby != null && until(lobby))
                return state;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The socket state never reached what the test waited for.");

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private async Task<HttpStatusCode> PostAsync(NoireRemoteServer server, string rest, NoireRemoteSocketAction action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + server.Port + NoireRemotePaths.Prefix + NoireRemotePaths.Sockets + rest)
        {
            Content = new StringContent(NoireRemoteJson.Write(action), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, string Body)> PostWithBodyAsync(NoireRemoteServer server, string rest, NoireRemoteSocketAction action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + server.Port + NoireRemotePaths.Prefix + NoireRemotePaths.Sockets + rest)
        {
            Content = new StringContent(NoireRemoteJson.Write(action), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition never held.");

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private static object? Different(PropertyInfo property, object? current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(bool)) return !(bool)(current ?? false);
        if (type == typeof(int)) return (int)(current ?? 0) + 7;
        if (type == typeof(long)) return (long)(current ?? 0L) + 7;
        if (type == typeof(TimeSpan)) return (TimeSpan)(current ?? TimeSpan.Zero) + TimeSpan.FromSeconds(3);
        if (type == typeof(string)) return (current as string ?? string.Empty) + "-moved";
        if (type == typeof(IPAddress)) return IPAddress.Parse("127.0.0.2");
        if (type == typeof(NoireRetryPolicy)) return NoireRetryPolicy.Default with { MaxDelay = TimeSpan.FromSeconds(91) };
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);

            foreach (var value in values)
            {
                if (!Equals(value, current))
                    return value;
            }
        }

        throw new InvalidOperationException(property.Name + " is a " + type.Name + " and this test has no other value for it.");
    }
}
