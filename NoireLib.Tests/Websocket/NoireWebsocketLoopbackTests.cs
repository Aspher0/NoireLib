using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The use case the system exists for, end to end in one process: a client opens a real connection to a socket
/// published on a real listener, a message crosses in each direction and the close handshake completes. The manifest
/// is what tells a caller the socket is there in the first place, and it stays true for a socket published later.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireWebsocketLoopbackTests : IDisposable
{
    public NoireWebsocketLoopbackTests()
        => Clean();

    public void Dispose()
    {
        Clean();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AClientAndAServerInOneProcess_ExchangeAMessageBothWaysAndCloseCleanly()
    {
        using var harness = new WebsocketHarness();
        var echo = harness.Sockets.Publish("echo");

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        echo.OnMessage((connection, message) =>
        {
            received.TrySetResult(message.Text);
            connection.Send("pong:" + message.Text);
        });

        harness.Http.Start();

        using var client = new NoireWebsocketClient(
            WebsocketUrl(harness.Http.Port, "echo"),
            Loopback(harness.Http.Token));

        var answered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessage(message => answered.TrySetResult(message.Text));

        await client.ConnectAsync(TestContext.Current.CancellationToken);
        client.IsConnected.Should().BeTrue();

        await client.SendAsync("ping", TestContext.Current.CancellationToken);

        (await Wait(received)).Should().Be("ping");
        (await Wait(answered)).Should().Be("pong:ping");

        await client.CloseAsync(cancellationToken: TestContext.Current.CancellationToken);

        client.State.Should().Be(NoireSocketState.Disconnected);
        client.LastClose!.Initiator.Should().Be(NoireWebsocketCloseInitiator.Local);
        (await WaitUntilAsync(() => echo.ClientCount == 0)).Should().BeTrue("the peer completed the close handshake");
    }

    [Fact]
    public async Task ConnectTo_TakesADirectoryRecordAndDeliversTheFirstFrameToTheConfiguredHandler()
    {
        using var harness = new WebsocketHarness();
        var echo = harness.Sockets.Publish("echo");

        echo.OnOpen(connection => connection.Send("welcome"));

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        echo.OnMessage((_, message) => received.TrySetResult(message.Text));

        harness.Http.Start();

        var record = new NoireRemoteInstanceRecord
        {
            Instance = harness.Http.InstanceId,
            Address = "127.0.0.1",
            Port = harness.Http.Port,
            Token = harness.Http.Token,
        };

        var greeted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var client = NoireWebsocket.ConnectTo(record, "echo",
            socket => socket.OnMessage(message => greeted.TrySetResult(message.Text)));

        (await Wait(greeted)).Should().Be("welcome",
            "the handler attached before the connection opened; the greeting the server sends on open still reaches it");

        await client.SendAsync("ping", TestContext.Current.CancellationToken);

        (await Wait(received)).Should().Be("ping");
    }

    [Fact]
    public void APublishedSocket_ReachesTheManifestAfterItWasFirstBuilt()
    {
        using var harness = new WebsocketHarness();

        harness.Http.Manifest().Sockets.Should().BeEmpty();

        harness.Sockets.Publish("chat", new NoireWebsocketEndpointOptions { Access = NoireRemoteAccess.Remote });

        var socket = harness.Http.Manifest().GetSocket("CHAT");

        socket.Should().NotBeNull("publishing a socket does not change the HTTP route table the manifest is cached against");
        socket!.Name.Should().Be("chat");
        socket.Route.Should().Be("/noire/ws/chat");
        socket.Access.Should().Be("remote");
    }

    [Fact]
    public void TheSockets_SurviveTheManifestGoingThroughJson()
    {
        using var harness = new WebsocketHarness();
        var options = new NoireWebsocketEndpointOptions { RequireSubProtocol = true };
        options.SubProtocols.Add("noire.v1");

        harness.Sockets.Publish("chat", options);

        var saved = NoireRemoteJson.Read<NoireRemoteManifest>(NoireRemoteJson.Write(harness.Http.Manifest()))!;
        var socket = saved.GetSocket("chat");

        socket.Should().NotBeNull();
        socket!.SubProtocols.Should().ContainSingle().Which.Should().Be("noire.v1");
        socket.RequireSubProtocol.Should().BeTrue();
    }

    [Fact]
    public void AListenerWithNoSocketHalf_SaysNothingAboutSocketsAtAll()
    {
        using var http = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            Port = 0,
            AutoStart = false,
            PublishDirectoryRecord = false,
            PublishCharacterIdentity = false,
            EnableConsole = false,
            EnableLogging = false,
        });

        http.Manifest().Sockets.Should().BeNull();
    }

    private static void Clean()
    {
        NoireWebsocket.DisposeAll();
        NoireLibMain.UnregisterOnDispose(NoireWebsocket.DisposeKey);
    }

    private static string WebsocketUrl(int port, string name)
        => "ws://127.0.0.1:" + port + NoireWebsocketServer.RoutePrefix + name;

    private static NoireWebsocketClientOptions Loopback(string token)
        => new()
        {
            Retry = NoireRetryPolicy.None,
            KeepAliveInterval = TimeSpan.Zero,
            IdleTimeout = TimeSpan.Zero,
            Http = new NoireSocketHttpOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(10),
                Credential = NoireSocketCredential.Bearer(token),
            },
        };

    private static async Task<string> Wait(TaskCompletionSource<string> pending)
        => await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int milliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();

        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (condition())
                return true;

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return condition();
    }
}
