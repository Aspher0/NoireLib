using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using NewtonsoftJsonSerializer = SocketIO.Serializer.NewtonsoftJson.NewtonsoftJsonSerializer;
using PackageEngineIO = SocketIO.Core.EngineIO;
using PackageResponse = SocketIOClient.SocketIOResponse;
using PackageSocket = SocketIOClient.SocketIO;
using PackageTransport = SocketIOClient.Transport.TransportProtocol;

namespace NoireLib.Tests;

/// <summary>Pins the transport selection, the per-event fan-out and the HTTP options a Socket.IO client carries.</summary>
public sealed class NoireSocketIOTests
{
    [Fact]
    public void Constructor_OpensNothing()
    {
        using var client = new NoireSocketIOClient("https://example.invalid/");

        client.State.Should().Be(NoireSocketState.Disconnected);
        client.IsConnected.Should().BeFalse();
        client.SocketId.Should().BeNull();
        client.LastError.Should().BeNull();
        client.UrlWarning.Should().BeNull();
    }

    [Fact]
    public void Constructor_PollingOnly_AlsoClearsAutoUpgrade()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        client.Underlying.Options.Transport.Should().Be(PackageTransport.Polling);
        client.Underlying.Options.AutoUpgrade.Should().BeFalse(
            "polling alone still upgrades, since the package turns auto upgrade on by default");
    }

    [Fact]
    public void Constructor_PollingThenUpgrade_LeavesAutoUpgradeOn()
    {
        using var client = Build(NoireSocketIOTransport.PollingThenUpgrade);

        client.Underlying.Options.Transport.Should().Be(PackageTransport.Polling);
        client.Underlying.Options.AutoUpgrade.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WebSocketOnly_SelectsWebSocket()
    {
        using var client = Build(NoireSocketIOTransport.WebSocketOnly);

        client.Underlying.Options.Transport.Should().Be(PackageTransport.WebSocket);
        client.Underlying.Options.AutoUpgrade.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ReplacesTheSerializerWithTheNewtonsoftOne()
    {
        using var client = new NoireSocketIOClient("https://example.invalid/");

        client.Underlying.Serializer.Should().BeOfType<NewtonsoftJsonSerializer>();
    }

    [Fact]
    public void Constructor_SuppliesTheTransportsTheWholeHttpOptionsObjectReaches()
    {
        using var client = new NoireSocketIOClient("https://example.invalid/");

        client.Underlying.HttpClient.Should().BeOfType<BridgeHttpClient>();

        using var socket = client.Underlying.ClientWebSocketProvider();
        socket.Should().BeOfType<BridgeClientWebSocket>();
    }

    [Fact]
    public void Constructor_MapsTheReconnectScheduleOntoThePackagesOwnValues()
    {
        var options = new NoireSocketIOOptions
        {
            Reconnect = new NoireSocketIOReconnect
            {
                Enabled = true,
                MaxAttempts = 4,
                InitialDelay = TimeSpan.FromMilliseconds(250),
                MaxDelay = TimeSpan.FromSeconds(3),
                Jitter = 0.125,
            },
        };

        using var client = new NoireSocketIOClient("https://example.invalid/", options);

        client.Underlying.Options.Reconnection.Should().BeTrue();
        client.Underlying.Options.ReconnectionAttempts.Should().Be(4);
        client.Underlying.Options.ReconnectionDelay.Should().Be(250);
        client.Underlying.Options.ReconnectionDelayMax.Should().Be(3000);
        client.Underlying.Options.RandomizationFactor.Should().Be(0.125);
    }

    [Fact]
    public void Constructor_WithANamespace_PutsItInThePathThePackageReadsItFrom()
    {
        using var client = new NoireSocketIOClient("https://example.invalid/", new NoireSocketIOOptions { Namespace = "chat" });

        client.Url.Should().Be(new Uri("https://example.invalid/chat"));
    }

    [Fact]
    public async Task CloseAsync_WhileNothingIsOpen_SettlesOnDisconnected()
    {
        using var client = new NoireSocketIOClient("https://example.invalid/");

        await client.CloseAsync();

        client.State.Should().Be(NoireSocketState.Disconnected);
    }

    [Fact]
    public void Constructor_WithASocketScheme_WarnsAndConnectsAnyway()
    {
        using var client = new NoireSocketIOClient("wss://example.invalid/");

        client.UrlWarning.Should().NotBeNull();
        client.UrlWarning.Should().Contain("wss");
    }

    [Fact]
    public void Constructor_RefusesASchemeThatIsNeitherHttpNorSocket()
    {
        var build = () => new NoireSocketIOClient("ftp://example.invalid/");

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RefusesARelativeAddress()
    {
        var build = () => new NoireSocketIOClient("/socket.io");

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task EmitAsync_WhileNothingIsConnected_Throws()
    {
        using var client = new NoireSocketIOClient("https://example.invalid/");

        var emit = async () => await client.EmitAsync("echo", "hello");

        await emit.Should().ThrowAsync<NoireSocketClosedException>();
    }

    [Fact]
    public void On_ForOneName_RegistersExactlyOnePackageHandler()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        client.On("echo", _ => { });
        client.On("echo", _ => { });
        client.On("other", _ => { });

        var handlers = PackageHandlers(client);

        handlers.Should().HaveCount(2);
        handlers.Should().ContainKey("echo");
        handlers.Should().ContainKey("other");
    }

    [Fact]
    public async Task On_WithTwoSubscribers_DeliversToBoth()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        var first = 0;
        var second = 0;

        client.On("echo", _ => first++);
        client.On("echo", _ => second++);

        Dispatch(client, "echo", "\"hi\"");

        await WaitUntilAsync(() => first == 1 && second == 1);
    }

    [Fact]
    public async Task On_DisposingOneSubscription_LeavesTheOtherSubscriberWorking()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        var kept = 0;
        var dropped = 0;

        client.On("echo", _ => kept++);
        var second = client.On("echo", _ => dropped++);

        second.Dispose();
        Dispatch(client, "echo", "\"hi\"");

        await WaitUntilAsync(() => kept == 1);
        dropped.Should().Be(0, "disposing one subscription must not remove the event name from the package");
    }

    [Fact]
    public async Task On_DeliversTheEventNameAndItsArguments()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        NoireSocketIOEvent? received = null;
        client.On("echo", received_ => received = received_);

        Dispatch(client, "echo", "\"hi\"");

        await WaitUntilAsync(() => received != null);

        received!.Name.Should().Be("echo");
        received.Get<string>(0).Should().Be("hi");
        received.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_PastTheLastArgument_AnswersWithTheDefault()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        NoireSocketIOEvent? received = null;
        client.On("echo", received_ => received = received_);

        Dispatch(client, "echo", "\"hi\"");

        await WaitUntilAsync(() => received != null);

        received!.Get<string>(1).Should().BeNull("a null answer is how a caller finds the end of the arguments");
    }

    [Fact]
    public async Task UnsubscribeAll_RemovesTheOwnersEventHandlers()
    {
        using var client = Build(NoireSocketIOTransport.PollingOnly);

        var owner = new object();
        var owned = 0;
        var other = 0;

        client.On("echo", _ => owned++, new NoireSocketSubscribeOptions { Owner = owner });
        client.On("echo", _ => other++);

        client.UnsubscribeAll(owner);
        Dispatch(client, "echo", "\"hi\"");

        await WaitUntilAsync(() => other == 1);
        owned.Should().Be(0);
    }

    private static NoireSocketIOClient Build(NoireSocketIOTransport transport)
        => new("https://example.invalid/", new NoireSocketIOOptions
        {
            Transport = transport,
            Host = new NoireRemoteStandaloneHost(),
        });

    private static Dictionary<string, Action<PackageResponse>> PackageHandlers(NoireSocketIOClient client)
    {
        var field = typeof(PackageSocket).GetField("_eventActionHandlers", BindingFlags.NonPublic | BindingFlags.Instance);

        field.Should().NotBeNull("the fan-out depends on the package keeping one handler per event name");

        return (Dictionary<string, Action<PackageResponse>>)field!.GetValue(client.Underlying)!;
    }

    // The package's receive path is private. The held handler is invoked with a message from the installed serializer.
    private static void Dispatch(NoireSocketIOClient client, string name, string arguments)
    {
        var message = client.Underlying.Serializer.Deserialize(PackageEngineIO.V4, "42[\"" + name + "\"," + arguments + "]");

        PackageHandlers(client)[name].Invoke(new PackageResponse(message, client.Underlying));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(5);

        condition().Should().BeTrue("the callback should have been delivered");
    }
}

/// <summary>
/// Pins that a Socket.IO options object is copied whole. A property added later cannot be left behind by
/// <see cref="NoireSocketIOOptions.Clone"/>.
/// </summary>
public sealed class NoireSocketIOOptionsTests
{
    [Fact]
    public void Constructor_IsACompleteConfiguration()
    {
        var options = new NoireSocketIOOptions();

        options.Transport.Should().Be(NoireSocketIOTransport.PollingThenUpgrade);
        options.Path.Should().Be(NoireSocketIOOptions.DefaultPath);
        options.Namespace.Should().BeNull();
        options.Host.Should().BeNull("a host captured here would be the wrong one inside a plugin");
        options.Reconnect.Should().BeSameAs(NoireSocketIOReconnect.Default);
    }

    [Fact]
    public void Clone_CopiesEveryProperty()
    {
        var source = BuildFullySet();
        var fresh = new NoireSocketIOOptions();
        var copy = source.Clone();

        foreach (var property in typeof(NoireSocketIOOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var original = property.GetValue(source);
            var cloned = property.GetValue(copy);

            if (property.Name == nameof(NoireSocketIOOptions.Query))
            {
                cloned.Should().NotBeSameAs(original);
                ((IDictionary<string, string>)cloned!).Should().Equal((IDictionary<string, string>)original!);
                continue;
            }

            if (property.Name == nameof(NoireSocketIOOptions.Http))
            {
                cloned.Should().NotBeSameAs(original);
                ((NoireSocketHttpOptions)cloned!).ConnectionTimeout
                    .Should().Be(((NoireSocketHttpOptions)original!).ConnectionTimeout);
                continue;
            }

            cloned.Should().Be(original, property.Name + " is not carried by Clone");
            original.Should().NotBe(property.GetValue(fresh), property.Name + " must not still be at its default, or the copy proves nothing");
        }
    }

    [Fact]
    public void Clone_SharesNoMutableStateWithTheOriginal()
    {
        var source = BuildFullySet();
        var copy = source.Clone();

        copy.Query["added"] = "1";
        copy.Http.Headers["X-Added"] = "1";

        source.Query.Should().NotContainKey("added");
        source.Http.Headers.Should().NotContainKey("X-Added");
    }

    private static NoireSocketIOOptions BuildFullySet()
    {
        var options = new NoireSocketIOOptions
        {
            Namespace = "/chat",
            Transport = NoireSocketIOTransport.PollingOnly,
            Path = "/engine.io",
            Auth = "token",
            Http = new NoireSocketHttpOptions { ConnectionTimeout = TimeSpan.FromSeconds(7) },
            Reconnect = NoireSocketIOReconnect.Default with { MaxAttempts = 3 },
            Thread = NoireRemoteThread.Background,
            Host = new NoireRemoteStandaloneHost(),
            ConfigureOptions = _ => { },
        };

        options.Query["room"] = "lobby";
        return options;
    }
}
