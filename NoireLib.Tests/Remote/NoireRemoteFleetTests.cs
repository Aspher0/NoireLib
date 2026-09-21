using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Discovery on the local network, tested against loopback: the derivation both sides do, the signing that keeps an
/// impostor off the list, and the broadcast rule that no target is ever a missing row.
/// </summary>
public sealed class NoireRemoteFleetTests : IDisposable
{
    private const string Secret = "the-shared-secret";

    private readonly List<NoireRemoteServer> servers = [];
    private readonly string registryDirectory;

    public NoireRemoteFleetTests()
    {
        registryDirectory = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        foreach (var server in servers)
            server.Dispose();

        try
        {
            if (Directory.Exists(registryDirectory))
                Directory.Delete(registryDirectory, true);
        }
        catch (IOException)
        {
        }
    }

    [NoireRemoteClass("Fleet", Access = NoireRemoteAccess.Remote)]
    public static class FleetProbe
    {
        [NoireRemote]
        public static string Who(string name) => "hello " + name;

        [NoireRemote]
        public static int Throws() => throw new InvalidOperationException("no");
    }

    private NoireRemoteInstance Resolve(Guid instance)
    {
        var options = NoireRemoteClient.Options.Clone();
        options.RegistryDirectory = registryDirectory;
        options.ExcludeInstance = null;
        options.RefreshInterval = TimeSpan.Zero;

        return new NoireRemoteResolver().Discover(options, null).Single(item => item.Id == instance);
    }

    private NoireRemoteServer StartServer()
    {
        var server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "fleet-probe" }, new NoireRemoteOptions
        {
            AutoStart = false,
            EnableLogging = false,
            PublishDirectoryRecord = true,
            PublishCharacterIdentity = false,
            RegistryDirectory = registryDirectory,
            RemoteSecret = Secret,
        });

        server.PublishType(typeof(FleetProbe));
        server.Start();
        servers.Add(server);

        return server;
    }

    [Fact]
    public void ThePort_IsDerivedFromTheSecretAndLandsInTheRange()
    {
        var port = NoireRemoteDiscovery.DerivePort(Secret);

        port.Should().BeInRange(NoireRemoteDiscovery.FirstPort, NoireRemoteDiscovery.FirstPort + NoireRemoteDiscovery.PortRange - 1);
        NoireRemoteDiscovery.DerivePort(Secret).Should().Be(port, "a caller holding the secret computes the same one");
        NoireRemoteDiscovery.DerivePort("another-secret").Should().NotBe(port);
    }

    [Fact]
    public void TheSecretIdentifier_IsOneWayAndTellsTwoSecretsApart()
    {
        var first = NoireRemoteDiscovery.SecretId(Secret);

        first.Should().HaveLength(32);
        first.Should().NotContain(Secret);
        NoireRemoteDiscovery.SecretId(Secret).Should().Be(first);
        NoireRemoteDiscovery.SecretId("another-secret").Should().NotBe(first);
        NoireRemoteDiscovery.SecretId(null).Should().BeEmpty();
    }

    [Fact]
    public void ASignedProbe_VerifiesAndAnEditedOneDoesNot()
    {
        var probe = NoireRemoteDiscovery.Probe(Secret, "Fleet");

        NoireRemoteDiscovery.Verify(Secret, NoireRemoteDiscovery.ProbeMethod, probe, TimeSpan.FromMinutes(2)).Should().BeTrue();

        probe.Endpoint = "SomethingElse";

        NoireRemoteDiscovery.Verify(Secret, NoireRemoteDiscovery.ProbeMethod, probe, TimeSpan.FromMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void AProbeSignedWithAnotherSecret_DoesNotVerify()
    {
        var probe = NoireRemoteDiscovery.Probe("impostor-secret");

        NoireRemoteDiscovery.Verify(Secret, NoireRemoteDiscovery.ProbeMethod, probe, TimeSpan.FromMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void AProbeReplayedAsAnAnswer_DoesNotVerify()
    {
        var probe = NoireRemoteDiscovery.Probe(Secret);

        NoireRemoteDiscovery.Verify(Secret, NoireRemoteDiscovery.AnswerMethod, probe, TimeSpan.FromMinutes(2)).Should().BeFalse(
            "the two codes cover different method names on purpose");
    }

    [Fact]
    public void AStaleTimestamp_DoesNotVerify()
    {
        var probe = NoireRemoteDiscovery.Probe(Secret);
        probe.Ts -= 600;
        probe.Mac = NoireRemoteDiscovery.Sign(Secret, NoireRemoteDiscovery.ProbeMethod, probe);

        NoireRemoteDiscovery.Verify(Secret, NoireRemoteDiscovery.ProbeMethod, probe, TimeSpan.FromMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void AnAnswer_CarriesThePortsAndVerifies()
    {
        var answer = NoireRemoteDiscovery.Answer(Secret, "MACHINE", "192.0.2.10", [52100, 52101]);

        NoireRemoteDiscovery.Verify(Secret, NoireRemoteDiscovery.AnswerMethod, answer, TimeSpan.FromMinutes(2)).Should().BeTrue();
        answer.Ports.Should().BeEquivalentTo([52100, 52101]);
        answer.Machine.Should().Be("MACHINE");
        NoireRemoteJson.Write(answer).Should().NotContain(Secret, "an answer never carries a usable credential");
    }

    [Fact]
    public void AnAnswer_StaysSmallEnoughForOneDatagram()
    {
        var answer = NoireRemoteDiscovery.Answer(Secret, Environment.MachineName, "192.168.100.100", [52100, 52101, 52102, 52103]);

        NoireRemoteJson.WriteBytes(answer).Length.Should().BeLessThan(512,
            "a record is around 350 bytes and four of them overflow the practical datagram size");
    }

    [Fact]
    public void ARecord_CarriesItsMachineAndItsSecretIdentifier()
    {
        var server = StartServer();
        var record = NoireRemoteDirectory.ReadAll(registryDirectory).Single(item => item.Instance == server.InstanceId);

        record.Machine.Should().Be(Environment.MachineName);
        record.SecretId.Should().Be(NoireRemoteDiscovery.SecretId(Secret));
        record.SecretId.Should().NotContain(Secret);
    }

    [Fact]
    public async Task ThePingAnswer_NamesTheMachineAndWhatPublishedTheSurface()
    {
        var server = StartServer();
        var instance = NoireRemoteClient.At("127.0.0.1", server.Port);

        var recorded = NoireRemoteDirectory.ReadAll(registryDirectory).Single(item => item.Instance == server.InstanceId);
        var resolved = Resolve(server.InstanceId);

        recorded.Port.Should().Be(server.Port);

        var ping = await resolved.PingAsync(TestContext.Current.CancellationToken);

        ping.Machine.Should().Be(Environment.MachineName);
        ping.Plugin.Should().Be("fleet-probe");
        ping.Endpoints.Should().Contain("Fleet");

        _ = instance;
    }

    [Fact]
    public async Task ABroadcast_ListsEveryTargetInOrderWhateverEachOneDid()
    {
        var first = StartServer();
        var second = StartServer();

        var targets = new[]
        {
            Resolve(first.InstanceId),
            Resolve(second.InstanceId),
        };

        var broadcast = await Broadcast<string>(targets, "Who", new { name = "there" });

        broadcast.Count.Should().Be(2);
        broadcast.AllAnswered.Should().BeTrue();
        broadcast.Values.Should().AllBe("hello there");
        broadcast[0].Instance.Id.Should().Be(first.InstanceId, "a row is never dropped and never reordered");
        broadcast[1].Instance.Id.Should().Be(second.InstanceId);
    }

    [Fact]
    public async Task ABroadcastWhereOneTargetFails_KeepsTheOtherAnswers()
    {
        var alive = StartServer();

        var targets = new[]
        {
            Resolve(alive.InstanceId),
            NoireRemoteClient.At("127.0.0.1", 9, Secret),
        };

        var broadcast = await Broadcast<string>(targets, "Who", new { name = "there" });

        broadcast.Count.Should().Be(2);
        broadcast.Answered.Should().Be(1);
        broadcast.Failed.Should().Be(1);
        broadcast[0].Ok.Should().BeTrue();
        broadcast[1].Ok.Should().BeFalse();
        broadcast[1].Error.Should().NotBeNull();
        broadcast.Values.Should().ContainSingle().Which.Should().Be("hello there");
    }

    [Fact]
    public async Task ABroadcastWhereAMemberThrows_IsThatTargetsRowAndNotAnException()
    {
        var server = StartServer();
        var targets = new[] { Resolve(server.InstanceId) };

        var broadcast = await Broadcast<int>(targets, "Throws", null);

        broadcast.Count.Should().Be(1);
        broadcast[0].Ok.Should().BeFalse();
        broadcast[0].Error.Should().BeOfType<NoireRemoteRemoteException>();
    }

    [Fact]
    public async Task ThrowIfAnyFailed_CarriesTheWholeBroadcast()
    {
        var alive = StartServer();

        var targets = new[]
        {
            Resolve(alive.InstanceId),
            NoireRemoteClient.At("127.0.0.1", 9, Secret),
        };

        var broadcast = await Broadcast<string>(targets, "Who", new { name = "there" });

        var thrown = broadcast.Invoking(target => target.ThrowIfAnyFailed())
            .Should().Throw<NoireRemoteBroadcastException>().Which;

        thrown.Broadcast.Should().BeSameAs(broadcast, "throwing must not discard the successes");
        thrown.Message.Should().Contain("1 of 2 answered");
    }

    [Fact]
    public async Task ABroadcastToNothing_IsAnEmptyListAndNotAFailure()
    {
        var broadcast = await Broadcast<string>([], "Who", new { name = "there" });

        broadcast.Count.Should().Be(0);
        broadcast.AllAnswered.Should().BeTrue();
        broadcast.ToString().Should().StartWith("0 of 0 answered");
    }

    [Fact]
    public async Task DiscoverAsyncWithNoNetworkSecret_ReadsTheRecordFolderAlone()
    {
        var server = StartServer();
        var options = NoireRemoteClient.Options.Clone();
        options.RegistryDirectory = registryDirectory;
        options.ExcludeInstance = null;
        options.NetworkSecret = null;

        NoireRemoteClient.Refresh();

        var found = await NoireRemoteClient.DiscoverAsync("Fleet", TestContext.Current.CancellationToken, options);

        found.Should().ContainSingle().Which.Id.Should().Be(server.InstanceId);
    }

    [Fact]
    public void AResponder_BindsOncePerMachineAndDelegatesAfterThat()
    {
        var first = new NoireRemoteServer(new NoireRemoteStandaloneHost(), Options());
        var second = new NoireRemoteServer(new NoireRemoteStandaloneHost(), Options());

        servers.Add(first);
        servers.Add(second);

        first.PublishType(typeof(FleetProbe));
        second.PublishType(typeof(FleetProbe));
        first.Start();
        second.Start();

        first.IsListening.Should().BeTrue();
        second.IsListening.Should().BeTrue("a listener whose responder is delegated still serves its own port");

        NoireRemoteOptions Options() => new()
        {
            AutoStart = false,
            EnableLogging = false,
            PublishDirectoryRecord = true,
            PublishCharacterIdentity = false,
            RegistryDirectory = registryDirectory,
            RemoteSecret = Secret,
            AnnounceOnNetwork = true,
        };
    }

    [Fact]
    public void AnnouncingWithNoSecret_StaysSilent()
    {
        var server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            EnableLogging = false,
            PublishDirectoryRecord = false,
            RegistryDirectory = registryDirectory,
            AnnounceOnNetwork = true,
            RemoteSecret = null,
        });

        servers.Add(server);
        server.PublishType(typeof(FleetProbe));
        server.Start();

        server.IsListening.Should().BeTrue();
        server.Manifest().Has(NoireRemoteFeatures.Fleet).Should().BeTrue("the option is on even though nothing answers");
    }

    private Task<NoireRemoteBroadcast<TResult>> Broadcast<TResult>(
        IReadOnlyList<NoireRemoteInstance> targets,
        string member,
        object? args)
        => NoireRemoteBroadcast.RunAsync<TResult>(targets, "Fleet", member, args, 0, TestContext.Current.CancellationToken);
}
