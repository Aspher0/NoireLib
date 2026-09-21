using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// How a call finds the instance it is meant for. The target is resolved at call time. A caller survives the
/// game restarting on another port.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteTargetingTests : NoireRemoteTestBase
{
    public NoireRemoteTargetingTests()
    {
        NoireRemoteClient.Options.RegistryDirectory = RegistryDirectory;
        NoireRemoteClient.Refresh();
    }

    // Background: these suites are caller and listener at once, with no framework thread.
    [NoireRemoteClass("Who", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class WhoProbe
    {
        [NoireRemote]
        public static string Who() => "probe";
    }

    [Fact]
    public void AMetadataTarget_MatchesTheInstanceCarryingTheTag()
    {
        var tagged = Instance("Aspher Noire @ Omega", ("role", "tank"));
        var other = Instance("Kiri Mao @ Omega", ("role", "healer"));

        NoireRemoteTarget.Meta("role", "tank").Matches(tagged).Should().BeTrue();
        NoireRemoteTarget.Meta("role", "tank").Matches(other).Should().BeFalse();
    }

    [Fact]
    public void ANameTarget_MatchesTheCharacterAloneAndTheCharacterWithItsWorld()
    {
        var instance = Instance("Aspher Noire @ Omega");

        NoireRemoteTarget.Identity("Aspher Noire").Matches(instance).Should().BeTrue();
        NoireRemoteTarget.Identity("Aspher Noire @ Omega").Matches(instance).Should().BeTrue();
        NoireRemoteTarget.Identity("aspher noire").Matches(instance).Should().BeTrue("a character name is matched ignoring case");
        NoireRemoteTarget.Identity("Kiri Mao").Matches(instance).Should().BeFalse();
    }

    [Fact]
    public void ANameAndWorldTarget_TellsTwoCharactersOfTheSameNameApart()
    {
        var omega = Instance("Aspher Noire @ Omega");
        var ragnarok = Instance("Aspher Noire @ Ragnarok");

        NoireRemoteTarget.Identity("Aspher Noire @ Ragnarok").Matches(ragnarok).Should().BeTrue();
        NoireRemoteTarget.Identity("Aspher Noire @ Ragnarok").Matches(omega).Should().BeFalse();
    }

    [Fact]
    public void AProcessTarget_MatchesBeforeAnyCharacterIsLoggedIn()
    {
        var instance = Instance(string.Empty);

        NoireRemoteTarget.Process(instance.ProcessId).Matches(instance).Should().BeTrue(
            "a process id is the one handle that exists on the character selection screen");
    }

    [Fact]
    public void ATargetThatMatchesNothing_ListsWhatIsOnline()
    {
        var online = new[] { Instance("Aspher Noire @ Omega", ("role", "tank")) };

        var message = NoireRemoteTarget.Meta("role", "bard").Describe(online);

        message.Should().Contain("role=bard").And.Contain("Aspher Noire").And.Contain("role=tank");
    }

    [Fact]
    public async Task ACallToAnAbsentTarget_FailsAtOnceRatherThanWaiting()
    {
        using var client = new NoireRemoteClient("Who");
        var started = System.Diagnostics.Stopwatch.StartNew();

        var act = () => client.On("role", "tank").InvokeAsync<string>("Who");

        await act.Should().ThrowAsync<NoireRemoteNotFoundException>();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "nothing ran, and the caller is told so now");
    }

    [Fact]
    public async Task ACallOverTheSocket_ReachesTheSurface()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(WhoProbe));

        using var client = new NoireRemoteClient("Who", NoireRemoteTransport.Websocket);

        (await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("probe");
        client.HasOpenSocket.Should().BeTrue("a client asked for a socket; it keeps one");
    }

    [Fact]
    public async Task AnHttpClient_KeepsNoSocketOpen()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(WhoProbe));

        using var client = new NoireRemoteClient("Who", NoireRemoteTransport.Http);
        await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));

        client.HasOpenSocket.Should().BeFalse();
    }

    [Fact]
    public async Task AnOverride_ReachesTheOtherWireFromEitherClient()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(WhoProbe));

        using var http = new NoireRemoteClient("Who", NoireRemoteTransport.Http);
        await http.Over(NoireRemoteTransport.Websocket).InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));
        http.HasOpenSocket.Should().BeTrue("the constructor sets a default, it does not fence the other wire");

        using var socket = new NoireRemoteClient("Who", NoireRemoteTransport.Websocket);
        await socket.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));
        await socket.Over(NoireRemoteTransport.Http).InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));
        socket.HasOpenSocket.Should().BeTrue("an HTTP call leaves the connection alone");
    }

    private static NoireRemoteInstance Instance(string label, params (string Key, string Value)[] tags)
    {
        var record = new NoireRemoteInstanceRecord
        {
            Instance = Guid.NewGuid(),
            Label = label,
            Pid = Environment.ProcessId,
            Metadata = tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase),
        };

        return new NoireRemoteInstance(record);
    }
}
