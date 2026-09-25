using FluentAssertions;
using NoireLib.EventBus;
using System;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>A token unsubscribes once, on its own bus. Replaced and removed subscriptions leave nothing behind.</summary>
[SupportedOSPlatform("windows")]
public class NoireEventBusTokenTests
{
    public record Evt(int Value);

    public record OtherEvt(int Value);

    private sealed class Counter
    {
        public int Calls;
    }

    private static NoireEventBus CreateBus()
        => new(active: true, enableLogging: false);

    [Fact]
    public void Token_From_Another_Bus_Does_Not_Unsubscribe()
    {
        var bus = CreateBus();
        var other = CreateBus();
        var calls = 0;

        var foreign = other.Subscribe<Evt>(_ => calls++);

        bus.Unsubscribe(foreign).Should().BeFalse();
        other.Publish(new Evt(1));

        calls.Should().Be(1);
        other.GetSubscriberCount<Evt>().Should().Be(1);
    }

    [Fact]
    public void Default_And_Spent_Tokens_Do_Not_Unsubscribe()
    {
        var bus = CreateBus();
        var token = bus.Subscribe<Evt>(_ => { });

        bus.Unsubscribe(default(EventSubscriptionToken)).Should().BeFalse();
        bus.Unsubscribe(token).Should().BeTrue();
        bus.Unsubscribe(token).Should().BeFalse();
    }

    [Fact]
    public void Token_Replaced_By_Key_Is_Spent_And_Leaves_The_Replacement()
    {
        var bus = CreateBus();
        var calls = 0;

        var first = bus.Subscribe<Evt>("k", _ => { });
        var second = bus.Subscribe<Evt>("k", _ => calls++);

        first.Should().NotBe(second);
        bus.Unsubscribe(first).Should().BeFalse("keyed replacement already removed the first subscription");

        bus.Publish(new Evt(1));
        calls.Should().Be(1);

        bus.Unsubscribe(second).Should().BeTrue();
        bus.Unsubscribe("k").Should().BeFalse();
    }

    [Fact]
    public void Keyed_Replacement_Across_Event_Types_Moves_The_Subscription()
    {
        var bus = CreateBus();

        bus.Subscribe<Evt>("k", _ => { });
        bus.Subscribe<OtherEvt>("k", _ => { });

        bus.GetSubscriberCount<Evt>().Should().Be(0);
        bus.GetSubscriberCount<OtherEvt>().Should().Be(1);
        bus.GetStatistics().RegisteredEventTypes.Should().Be(1);
    }

    [Fact]
    public void Statistics_Follow_Owner_Type_And_Clear_Removal()
    {
        var bus = CreateBus();
        var owner = new object();

        bus.Subscribe<Evt>(_ => { }, owner: owner);
        bus.Subscribe<Evt>("keyed", _ => { }, owner: owner);
        bus.Subscribe<Evt>(_ => { });
        bus.Subscribe<OtherEvt>(_ => { }, owner: owner);

        var stats = bus.GetStatistics();
        stats.ActiveSubscriptions.Should().Be(4);
        stats.RegisteredEventTypes.Should().Be(2);

        bus.UnsubscribeAll<Evt>(owner).Should().Be(2);
        bus.Unsubscribe("keyed").Should().BeFalse("owner removal also frees the key");

        stats = bus.GetStatistics();
        stats.ActiveSubscriptions.Should().Be(2);
        stats.RegisteredEventTypes.Should().Be(2);

        bus.UnsubscribeAll(owner).Should().Be(1);
        bus.GetStatistics().RegisteredEventTypes.Should().Be(1);

        bus.ClearAllSubscriptions();
        stats = bus.GetStatistics();
        stats.ActiveSubscriptions.Should().Be(0);
        stats.RegisteredEventTypes.Should().Be(0);
    }

    [Fact]
    public void Warm_Publish_Allocates_Nothing()
    {
        var bus = CreateBus();
        var counter = new Counter();
        var evt = new Evt(1);

        bus.Subscribe<Evt>(_ => counter.Calls++, priority: 5);
        bus.Subscribe<Evt>(_ => counter.Calls++, filter: static e => e.Value > 0);

        bus.Publish(evt);
        bus.Publish(evt);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
            bus.Publish(evt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        counter.Calls.Should().Be(36);
    }
}
