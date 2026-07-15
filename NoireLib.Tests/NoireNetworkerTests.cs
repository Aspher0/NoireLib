using FluentAssertions;
using NoireLib.EventBus;
using NoireLib.Networker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Integration tests for the NoireNetworker module. These run the real stack: kernel mutex election,
/// memory-mapped rendezvous, and loopback TCP - multiple module instances inside this test process.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireNetworkerTests : IDisposable
{
    public sealed class TestMessage
    {
        public string Text { get; set; } = string.Empty;
    }

    public sealed class TestRequest
    {
        public int Value { get; set; }
    }

    public sealed class TestReply
    {
        public int Echo { get; set; }
    }

    public sealed class SharedTestEvent : INetworkerEvent
    {
        public string Data { get; set; } = string.Empty;
        public NetworkerPeer? Origin { get; set; }
    }

    private readonly List<NoireNetworker> networkersToClean = new();
    private readonly List<NoireEventBus> busesToClean = new();

    public void Dispose()
    {
        foreach (var networker in networkersToClean)
        {
            try
            {
                networker.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        foreach (var bus in busesToClean)
        {
            try
            {
                bus.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private static string TestNetwork()
        => $"NoireLib.Tests.{Guid.NewGuid():N}";

    private NoireNetworker CreateNetworker(string network, NetworkerOptions? options = null)
    {
        var networker = new NoireNetworker(network, enableLogging: false, options: options);
        networkersToClean.Add(networker);
        return networker;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(25);
        }

        return condition();
    }

    private async Task<List<NoireNetworker>> CreateReadyGroupAsync(string network, int count)
    {
        var networkers = Enumerable.Range(0, count).Select(_ => CreateNetworker(network)).ToList();

        (await WaitUntilAsync(() => networkers.All(n => n.State == NetworkerState.Ready && n.OtherPeers.Count == count - 1)))
            .Should().BeTrue("all instances should become Ready and see each other");

        return networkers;
    }

    [Fact]
    public async Task Three_Instances_Elect_Exactly_One_Hub_And_See_Each_Other()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        networkers.Count(n => n.IsHub).Should().Be(1);
        networkers.SelectMany(n => n.OtherPeers.Select(p => p.Id)).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task Broadcast_Reaches_All_Others_But_Not_The_Sender()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        var receivedBy = new List<Guid>();

        foreach (var networker in networkers)
        {
            var id = networker.SelfId;
            networker.On<TestMessage>((peer, msg) =>
            {
                lock (receivedBy)
                    receivedBy.Add(id);
            });
        }

        networkers[0].Send(new TestMessage { Text = "hello" });

        (await WaitUntilAsync(() => { lock (receivedBy) return receivedBy.Count == 2; })).Should().BeTrue();
        await Task.Delay(200);

        lock (receivedBy)
        {
            receivedBy.Should().HaveCount(2);
            receivedBy.Should().NotContain(networkers[0].SelfId);
        }
    }

    [Fact]
    public async Task SendTo_Reaches_Only_The_Target()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        var receivedBy = new List<Guid>();

        foreach (var networker in networkers)
        {
            var id = networker.SelfId;
            networker.On<TestMessage>((peer, msg) =>
            {
                lock (receivedBy)
                    receivedBy.Add(id);
            });
        }

        var target = networkers[0].OtherPeers.First(p => p.Id == networkers[2].SelfId);
        networkers[0].SendTo(target, new TestMessage { Text = "direct" });

        (await WaitUntilAsync(() => { lock (receivedBy) return receivedBy.Count == 1; })).Should().BeTrue();
        await Task.Delay(200);

        lock (receivedBy)
            receivedBy.Should().Equal(networkers[2].SelfId);
    }

    [Fact]
    public async Task Metadata_Synchronizes_And_Fires_PeerUpdated()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 2);

        var observed = new List<string>();
        networkers[1].OnPeerJoined(peer => { lock (observed) observed.Add($"joined:{peer.Id}"); });
        networkers[1].OnPeerUpdated((peer, key) => { lock (observed) observed.Add($"updated:{key}"); });

        networkers[0].Self.Set("character", "Test Character");

        (await WaitUntilAsync(() =>
            networkers[1].OtherPeers.FirstOrDefault(p => p.Id == networkers[0].SelfId)?["character"] == "Test Character"))
            .Should().BeTrue();

        (await WaitUntilAsync(() => { lock (observed) return observed.Contains("updated:character"); }, timeoutMs: 5_000))
            .Should().BeTrue($"observed events were: [{string.Join(", ", observed)}]");
    }

    [Fact]
    public async Task Request_Response_Round_Trip_Works()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 2);

        networkers[1].OnRequest<TestRequest, TestReply>((peer, request) => new TestReply { Echo = request.Value * 2 });

        var target = networkers[0].OtherPeers.First(p => p.Id == networkers[1].SelfId);
        var reply = await networkers[0].Request<TestRequest, TestReply>(target, new TestRequest { Value = 21 });

        reply.Echo.Should().Be(42);
    }

    [Fact]
    public async Task Request_Without_Remote_Handler_Fails_With_Remote_Error()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 2);

        var target = networkers[0].OtherPeers.First();

        var act = () => networkers[0].Request<TestRequest, TestReply>(target, new TestRequest { Value = 1 }, TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task RequestAll_Collects_All_Successful_Answers()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        networkers[1].OnRequest<TestRequest, TestReply>((peer, request) => new TestReply { Echo = 1 });
        networkers[2].OnRequest<TestRequest, TestReply>((peer, request) => new TestReply { Echo = 2 });

        var answers = await networkers[0].RequestAll<TestRequest, TestReply>(new TestRequest { Value = 0 });

        answers.Should().HaveCount(2);
        answers.Values.Select(reply => reply.Echo).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task Barrier_Completes_When_Everyone_Flags()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        var barriers = networkers
            .Select(n => n.WhenAllFlagged("ready", TimeSpan.FromSeconds(10), minimumOthers: 2))
            .ToList();

        foreach (var networker in networkers)
            networker.SetFlag("ready");

        var results = await Task.WhenAll(barriers).WaitAsync(TimeSpan.FromSeconds(10));
        results.Should().AllSatisfy(result => result.Should().BeTrue());
    }

    [Fact]
    public async Task Barrier_Times_Out_When_Someone_Never_Flags()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 2);

        networkers[0].SetFlag("ready");

        var result = await networkers[0].WhenAllFlagged("ready", TimeSpan.FromMilliseconds(750), minimumOthers: 1);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Clean_Departure_Fires_PeerLeft_Quickly()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        var leaver = networkers.First(n => !n.IsHub);
        var observers = networkers.Where(n => !ReferenceEquals(n, leaver)).ToList();

        leaver.Dispose();

        (await WaitUntilAsync(() => observers.All(n => n.OtherPeers.Count == 1), timeoutMs: 5_000))
            .Should().BeTrue("a clean goodbye should remove the peer everywhere");
    }

    [Fact]
    public async Task Hub_Failover_Elects_New_Hub_And_Network_Recovers()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        var oldHub = networkers.First(n => n.IsHub);
        var survivors = networkers.Where(n => !ReferenceEquals(n, oldHub)).ToList();

        oldHub.Dispose();

        (await WaitUntilAsync(() =>
            survivors.All(n => n.State == NetworkerState.Ready && n.OtherPeers.Count == 1)
            && survivors.Count(n => n.IsHub) == 1, timeoutMs: 15_000))
            .Should().BeTrue("the survivors should re-elect and reconverge");

        // The recovered network must still carry traffic.
        var received = 0;
        survivors[1].On<TestMessage>((peer, msg) => Interlocked.Increment(ref received));
        survivors[0].Send(new TestMessage { Text = "after failover" });

        (await WaitUntilAsync(() => Volatile.Read(ref received) == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task EventBus_ShareEvent_Bridges_Without_Echo_Loops()
    {
        var network = TestNetwork();

        var busA = new NoireEventBus(null, true, enableLogging: false);
        var busB = new NoireEventBus(null, true, enableLogging: false);
        busesToClean.Add(busA);
        busesToClean.Add(busB);

        var networkerA = CreateNetworker(network, new NetworkerOptions { EventBus = busA });
        var networkerB = CreateNetworker(network, new NetworkerOptions { EventBus = busB });

        (await WaitUntilAsync(() =>
            networkerA.State == NetworkerState.Ready && networkerB.State == NetworkerState.Ready
            && networkerA.OtherPeers.Count == 1 && networkerB.OtherPeers.Count == 1))
            .Should().BeTrue();

        networkerA.ShareEvent<SharedTestEvent>();
        networkerB.ShareEvent<SharedTestEvent>();

        var receivedOnA = 0;
        var receivedOnB = 0;
        SharedTestEvent? receivedEventOnB = null;

        busA.Subscribe<SharedTestEvent>(evt => Interlocked.Increment(ref receivedOnA));
        busB.Subscribe<SharedTestEvent>(evt =>
        {
            Interlocked.Increment(ref receivedOnB);
            Volatile.Write(ref receivedEventOnB, evt);
        });

        busA.Publish(new SharedTestEvent { Data = "shared" });

        (await WaitUntilAsync(() => Volatile.Read(ref receivedOnB) == 1)).Should().BeTrue();
        await Task.Delay(300);

        // Exactly once on each side - no ping-pong between the bridges.
        Volatile.Read(ref receivedOnA).Should().Be(1, "the local publish reaches local subscribers once");
        Volatile.Read(ref receivedOnB).Should().Be(1, "the bridged publish must not echo back and forth");

        var bridged = Volatile.Read(ref receivedEventOnB);
        bridged!.Data.Should().Be("shared");
        bridged.Origin.Should().NotBeNull("bridged-in events carry their origin peer");
        bridged.Origin!.Id.Should().Be(networkerA.SelfId);
    }

    [Fact]
    public async Task Flags_Vanish_With_The_Peer_That_Set_Them()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 3);

        networkers[2].SetFlag("ready");

        (await WaitUntilAsync(() =>
            networkers[0].OtherPeers.FirstOrDefault(p => p.Id == networkers[2].SelfId)?.HasFlag("ready") == true))
            .Should().BeTrue();

        var leaver = networkers[2];

        if (leaver.IsHub)
        {
            // Make the flag-holder a non-hub scenario irrelevant: dispose works either way; failover covers hub death.
            leaver.Dispose();
            (await WaitUntilAsync(() => networkers[0].State == NetworkerState.Ready && networkers[0].OtherPeers.Count == 1, timeoutMs: 15_000)).Should().BeTrue();
        }
        else
        {
            leaver.Dispose();
            (await WaitUntilAsync(() => networkers[0].OtherPeers.Count == 1, timeoutMs: 5_000)).Should().BeTrue();
        }

        networkers[0].OtherPeers.Should().NotContain(p => p.HasFlag("ready"));
    }
}
