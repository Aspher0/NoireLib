using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The facade's contract: a connect call hands back a client whose handlers were attached before anything was
/// written or read, a credential built from a directory record never lands on the caller's own options object, and
/// the whole system is torn down under one key that is handed back afterwards.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireWebsocketFacadeTests : NoireRemoteTestBase
{
    public NoireWebsocketFacadeTests()
        => Clean();

    [Fact]
    public void Connect_RunsTheConfigureCallbackBeforeTheFirstByteMoves()
    {
        var seen = NoireSocketState.Faulted;

        using var client = NoireWebsocket.Connect(UnreachableUrl(), Unreachable(), socket => seen = socket.State);

        seen.Should().Be(NoireSocketState.Disconnected,
            "a client handed back already connecting delivers a message arriving before the caller's next statement to nobody");
    }

    [Fact]
    public void ConnectSse_RunsTheConfigureCallbackBeforeTheFirstByteMoves()
    {
        var seen = NoireSocketState.Faulted;

        using var client = NoireWebsocket.ConnectSse(
            "http://127.0.0.1:" + ClosedPort() + "/events",
            new NoireSseOptions { Retry = NoireRetryPolicy.None },
            stream => seen = stream.State);

        seen.Should().Be(NoireSocketState.Disconnected);
    }

    [Fact]
    public void ConnectLongPoll_RunsTheConfigureCallbackBeforeTheFirstByteMoves()
    {
        var seen = NoireSocketState.Faulted;

        using var client = NoireWebsocket.ConnectLongPoll(
            "http://127.0.0.1:" + ClosedPort() + "/poll",
            new NoireLongPollOptions { Retry = NoireRetryPolicy.None },
            poll => seen = poll.State);

        seen.Should().Be(NoireSocketState.Disconnected);
    }

    [Fact]
    public void ConnectSocketIO_RunsTheConfigureCallbackBeforeTheFirstByteMoves()
    {
        var seen = NoireSocketState.Faulted;

        using var client = NoireWebsocket.ConnectSocketIO(
            "http://127.0.0.1:" + ClosedPort() + "/",
            new NoireSocketIOOptions { Reconnect = NoireSocketIOReconnect.None },
            io => seen = io.State);

        seen.Should().Be(NoireSocketState.Disconnected);
    }

    [Fact]
    public void ConnectTo_BuildsTheSocketUrlAndTheCredentialFromTheRecord()
    {
        var record = Record();
        var options = Unreachable();

        using var client = NoireWebsocket.ConnectTo(record, "chat", options);

        client.Url.Should().Be(new Uri("ws://127.0.0.1:" + record.Port + "/noire/ws/chat"));
        client.Options.Http.Credential.Should().NotBeNull();
        options.Http.Credential.Should().BeNull(
            "the caller's options are copied; an object reused for a second instance must not carry the first one's token");
    }

    [Fact]
    public void ConnectTo_KeepsACredentialTheCallerSetForItself()
    {
        var mine = NoireSocketCredential.Signed("the-shared-secret");
        var options = Unreachable();
        options.Http.Credential = mine;

        using var client = NoireWebsocket.ConnectTo(Record(), "chat", options);

        client.Options.Http.Credential.Should().BeSameAs(mine);
    }

    [Fact]
    public void ConnectTo_RefusesASocketNameTheWireCannotCarry()
    {
        var connect = () => NoireWebsocket.ConnectTo(Record(), "chat/say");

        connect.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheTeardown_IsRegisteredOnceForTheWholeSystemAndHandsItsKeyBack()
    {
        using (NoireWebsocket.Connect(UnreachableUrl(), Unreachable()))
        using (NoireWebsocket.Connect(UnreachableUrl(), Unreachable()))
        {
            NoireWebsocket.Publish("facade-probe");
            NoireLibMain.IsRegisteredOnDispose(NoireWebsocket.DisposeKey).Should().BeTrue();
        }

        NoireWebsocket.DisposeAll();

        NoireLibMain.IsRegisteredOnDispose(NoireWebsocket.DisposeKey).Should().BeFalse(
            "nothing else clears the registration list; a key left claimed would refuse the next registration");
    }

    [Fact]
    public async Task TheTeardown_ClosesEveryConnectionTheFacadeOpenedAndUnpublishesEverySocket()
    {
        var client = NoireWebsocket.Connect(UnreachableUrl(), Unreachable());
        NoireWebsocket.Publish("facade-probe");

        NoireWebsocket.Endpoints.Should().ContainSingle();

        NoireWebsocket.DisposeAll();

        var reconnect = async () => await client.ConnectAsync(TestContext.Current.CancellationToken);

        await reconnect.Should().ThrowAsync<ObjectDisposedException>("the facade owns what it opened");
        NoireWebsocket.Endpoints.Should().BeEmpty();
    }

    [Fact]
    public void TheTeardown_RunsTwiceWithoutThrowing()
    {
        NoireWebsocket.Publish("facade-probe");

        NoireWebsocket.DisposeAll();
        var second = NoireWebsocket.DisposeAll;

        second.Should().NotThrow();
    }

    [Fact]
    public void Publish_AfterATeardown_WorksAgain()
    {
        NoireWebsocket.Publish("facade-probe");
        NoireWebsocket.DisposeAll();

        var again = NoireWebsocket.Publish("facade-probe");

        again.Path.Should().Be("/noire/ws/facade-probe");
        NoireLibMain.IsRegisteredOnDispose(NoireWebsocket.DisposeKey).Should().BeTrue();
    }

    [Fact]
    public void LocalUrl_OnAListenerThatIsNotBound_Throws()
    {
        var url = () => NoireWebsocket.LocalUrl("chat");

        url.Should().Throw<ArgumentOutOfRangeException>("the port is what a program on this machine connects to");
    }

    public override void Dispose()
    {
        Clean();
        base.Dispose();
    }

    private static void Clean()
    {
        NoireWebsocket.DisposeAll();
        NoireLibMain.UnregisterOnDispose(NoireWebsocket.DisposeKey);
    }

    private static NoireRemoteInstanceRecord Record()
        => new()
        {
            Address = "127.0.0.1",
            Port = ClosedPort(),
            Token = "the-loopback-token",
        };

    // A refused port, never retried.
    private static NoireWebsocketClientOptions Unreachable()
        => new()
        {
            Retry = NoireRetryPolicy.None,
            Http = new NoireSocketHttpOptions { ConnectionTimeout = TimeSpan.FromMilliseconds(250) },
        };

    private static string UnreachableUrl()
        => "ws://127.0.0.1:" + ClosedPort() + "/hub";

    private static int ClosedPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}
