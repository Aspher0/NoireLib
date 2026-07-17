using FluentAssertions;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using NoireLib.Networker;
using NoireLib.Networker.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    /// <summary>
    /// Builds a networker exactly the way <see cref="NoireLibMain.AddModule{T}(string?)"/> does: by reflecting over the
    /// internal (ModuleId, bool, bool) constructor and asking for an active, logging module.
    /// </summary>
    private static NoireNetworker CreateThroughAddModuleConstructor(string? moduleId)
    {
        var constructor = typeof(NoireNetworker).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(ModuleId), typeof(bool), typeof(bool)],
            null);

        constructor.Should().NotBeNull("AddModule<T> reflects over this exact signature and silently degrades to a new T() path without it");

        return (NoireNetworker)constructor!.Invoke([moduleId == null ? null : new ModuleId(moduleId), true, true]);
    }

    private async Task<List<NoireNetworker>> CreateReadyGroupAsync(string network, int count)
    {
        var networkers = Enumerable.Range(0, count).Select(_ => CreateNetworker(network)).ToList();

        (await WaitUntilAsync(() => networkers.All(n => n.State == NetworkerState.Ready && n.OtherPeers.Count == count - 1)))
            .Should().BeTrue("all instances should become Ready and see each other");

        return networkers;
    }

    [Fact]
    public void AddModule_Construction_Defers_Activation_Rather_Than_Failing_To_Activate()
    {
        var networker = CreateThroughAddModuleConstructor(moduleId: "Deferred");
        networkersToClean.Add(networker);

        networker.ModuleId.Should().Be("Deferred");
        networker.NetworkName.Should().BeNull();
        networker.IsActive.Should().BeFalse("a networker with no network name has nothing to join, so activation waits for the caller");
        networker.State.Should().Be(NetworkerState.Stopped);
    }

    [Fact]
    public async Task Deferred_Networker_Joins_The_Network_Once_Named_And_Activated()
    {
        var network = TestNetwork();

        var deferred = CreateThroughAddModuleConstructor(moduleId: null);
        networkersToClean.Add(deferred);

        deferred.SetEnableLogging(false);
        deferred.SetNetworkName(network);
        deferred.SetActive(true);

        var peer = CreateNetworker(network);

        (await WaitUntilAsync(() =>
            deferred.State == NetworkerState.Ready && peer.State == NetworkerState.Ready
            && deferred.OtherPeers.Count == 1 && peer.OtherPeers.Count == 1))
            .Should().BeTrue("a deferred-configuration networker joins like any other once it is named and activated");
    }

    [Fact]
    public void Activating_Without_A_Network_Name_Is_Refused()
    {
        var networker = new NoireNetworker();
        networkersToClean.Add(networker);

        networker.SetEnableLogging(false);
        networker.SetActive(true);

        networker.IsActive.Should().BeFalse("activating an unnamed networker is a misconfiguration, and is refused");
        networker.State.Should().Be(NetworkerState.Stopped);
    }

    [Fact]
    public void A_Disposed_Delivery_Pump_Discards_Everything_Still_Queued()
    {
        var pump = new DeliveryPump(16, (ex, message) => { }) { ForceQueuedDelivery = true };

        var ran = 0;
        pump.Post(() => Interlocked.Increment(ref ran));

        Volatile.Read(ref ran).Should().Be(0, "a queued delivery waits for a frame to drain it rather than running on the posting thread");

        pump.Dispose();
        pump.Drain();

        Volatile.Read(ref ran).Should().Be(
            0,
            "disposal drops the backlog, which is why a delivery that must reach a consumer has to be run before the pump is disposed rather than posted through it and left behind");
    }

    [Fact]
    public async Task Stopping_Delivers_The_Stopped_State_Rather_Than_Discarding_It_With_The_Pump()
    {
        var networker = new NoireNetworker(TestNetwork(), active: false, enableLogging: false);
        networkersToClean.Add(networker);

        // Inline delivery is what lets the rest of this suite run without a game, and it is also what would hide the
        // behavior under test: inline, the Stopped delivery runs on the thread that posts it and so is already done by
        // the time the pump is disposed. A running game only ever takes the queued path, so it is forced on here.
        networker.ForceQueuedDelivery = true;
        networker.SetActive(true);

        var observed = new List<NetworkerState>();
        networker.OnStateChanged(state => { lock (observed) observed.Add(state); });

        // With queueing forced there is no framework update driving the pump, so draining stands in for a frame.
        (await WaitUntilAsync(() =>
        {
            networker.DrainDeliveries();

            lock (observed)
                return observed.Contains(NetworkerState.Ready);
        })).Should().BeTrue("a lone instance elects itself hub and reaches Ready");

        networker.SetActive(false);

        lock (observed)
        {
            observed.Should().Contain(
                NetworkerState.Stopped,
                "a consumer must observe the networker stopping; the delivery carrying Stopped must not be discarded along with the pump");

            observed[^1].Should().Be(
                NetworkerState.Stopped,
                "Stopped is the terminal transition, so nothing may reach a consumer after it");
        }
    }

    [Fact]
    public void ElectionMutex_Hands_The_Role_To_The_Next_Contender_On_Release()
    {
        var mutexName = NetworkerNames.MutexName(TestNetwork());

        using var first = new ElectionMutex(mutexName);
        using var second = new ElectionMutex(mutexName);

        first.TryAcquire().Should().BeTrue("an uncontended election mutex is acquired immediately");
        first.TryAcquire().Should().BeTrue("re-acquiring while already held is a no-op");
        second.TryAcquire().Should().BeFalse("the mutex is already owned by the first holder thread");
        second.IsHeld.Should().BeFalse();

        first.Release();
        first.IsHeld.Should().BeFalse();

        second.TryAcquire().Should().BeTrue("releasing frees the role for the next contender");
        second.IsHeld.Should().BeTrue();
    }

    [Fact]
    public void ElectionMutex_Refuses_The_Role_Once_Disposed_And_Frees_It_For_The_Next_Contender()
    {
        var mutexName = NetworkerNames.MutexName(TestNetwork());

        var first = new ElectionMutex(mutexName);
        first.TryAcquire().Should().BeTrue();

        first.Dispose();
        first.IsHeld.Should().BeFalse("disposal hands the role back");

        first.TryAcquire()
            .Should().BeFalse("a disposed election mutex must never take the role again, since nothing would be left to release it");

        using var second = new ElectionMutex(mutexName);
        second.TryAcquire().Should().BeTrue("the role a disposed holder gave up belongs to the next contender");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Election_Backoff_Starts_At_The_Base_Delay(int idleAttempts)
        => NoireNetworker.ComputeElectionBackoff(idleAttempts).Should().Be(TimeSpan.FromMilliseconds(100));

    [Fact]
    public void Election_Backoff_Grows_And_Is_Capped()
    {
        NoireNetworker.ComputeElectionBackoff(2).Should().Be(TimeSpan.FromMilliseconds(200));
        NoireNetworker.ComputeElectionBackoff(3).Should().Be(TimeSpan.FromMilliseconds(400));
        NoireNetworker.ComputeElectionBackoff(4).Should().Be(TimeSpan.FromMilliseconds(800));
        NoireNetworker.ComputeElectionBackoff(5).Should().Be(TimeSpan.FromMilliseconds(1600));

        foreach (var idleAttempts in new[] { 6, 10, 100, int.MaxValue })
        {
            NoireNetworker.ComputeElectionBackoff(idleAttempts)
                .Should().Be(TimeSpan.FromSeconds(2), "the retry delay is capped rather than growing without bound");
        }
    }

    [Fact]
    public async Task A_Connected_Client_Never_Re_Elects_While_Its_Hub_Lives()
    {
        var networkers = await CreateReadyGroupAsync(TestNetwork(), 2);

        var client = networkers.First(n => !n.IsHub);
        var attemptsWhenSettled = client.ElectionAttempts;

        await Task.Delay(1_500);

        client.State.Should().Be(NetworkerState.Ready);
        client.ElectionAttempts.Should().Be(
            attemptsWhenSettled,
            "a client holds its hub connection for its whole life, so a settled client must not contend for the role at all");
    }

    [Fact]
    public async Task An_Instance_That_Cannot_Join_Backs_Off_Instead_Of_Re_Electing_Constantly()
    {
        var network = TestNetwork();

        // Holding the role from outside, without ever serving a hub, is the shape of every situation an instance cannot
        // resolve on its own: no rendezvous is ever published, so it can neither elect nor connect.
        using var squatter = new ElectionMutex(NetworkerNames.MutexName(network));
        squatter.TryAcquire().Should().BeTrue();

        var networker = CreateNetworker(network);

        await Task.Delay(3_000);

        networker.State.Should().NotBe(NetworkerState.Ready, "there is no hub to join");

        // Each attempt starts a dedicated election thread. Backing off from 100ms to a 2s ceiling allows roughly six
        // attempts in this window; a fixed short retry would allow around thirty. A slow machine only makes the
        // upper bound safer, since it can only lower the attempt count.
        networker.ElectionAttempts.Should().BeInRange(
            2, 12, "an instance that cannot join must keep contending, but must not spawn an election thread several times a second forever");
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
