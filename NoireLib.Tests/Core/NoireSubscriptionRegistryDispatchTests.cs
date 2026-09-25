using FluentAssertions;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks the dispatch snapshot, allocation-free inline dispatch, reported marshaled faults and exact owner removal.</summary>
[SupportedOSPlatform("windows")]
public class NoireSubscriptionRegistryDispatchTests
{
    private sealed class Counter
    {
        public int Calls;
    }

    [Fact]
    public void Warm_Inline_Dispatch_Allocates_Nothing()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var counter = new Counter();

        registry.Subscribe("k", _ => counter.Calls++, new() { Priority = 10 });
        registry.Subscribe("k", _ => counter.Calls++, new() { Filter = static value => value >= 0 });
        registry.Subscribe("k", _ => counter.Calls++, new() { Delivery = SubscriptionDelivery.FrameworkThread });

        registry.Dispatch("k", 1);
        registry.Dispatch("k", 1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
            registry.Dispatch("k", 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0, "dispatch reads a cached snapshot instead of copying the subscriber list");
        counter.Calls.Should().Be(18 * 3);
    }

    [Fact]
    public void Dispatch_With_No_Subscribers_Allocates_Nothing()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();

        registry.Dispatch("none", 1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
            registry.Dispatch("none", 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
    }

    [Fact]
    public void Snapshot_Follows_Subscribe_Unsubscribe_And_Keyed_Replacement()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var order = new List<string>();

        registry.Subscribe("k", _ => order.Add("low"));
        registry.Dispatch("k", 0);

        var high = registry.Subscribe("k", _ => order.Add("high"), new() { Priority = 10 });
        registry.Dispatch("k", 0);

        registry.Subscribe("k", _ => order.Add("keyed-a"), new() { Key = "named", Priority = 5 });
        registry.Subscribe("k", _ => order.Add("keyed-b"), new() { Key = "named", Priority = -5 });
        registry.Dispatch("k", 0);

        high.Dispose();
        registry.Dispatch("k", 0);

        registry.Clear("k");
        registry.Dispatch("k", 0).Should().Be(0);

        registry.Subscribe("k", _ => order.Add("again"));
        registry.Dispatch("k", 0);

        order.Should().Equal(
            "low",
            "high", "low",
            "high", "low", "keyed-b",
            "low", "keyed-b",
            "again");
        registry.Count("k").Should().Be(1);
        registry.HasSubscribers("k").Should().BeTrue();
    }

    [Fact]
    public async Task Marshaled_Handler_Fault_Is_Reported_When_Exceptions_Propagate()
    {
        Exception? reported = null;
        var reportedSignal = new SemaphoreSlim(0);
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) =>
        {
            reported = ex;
            reportedSignal.Release();
        }, propagateHandlerExceptions: true);

        registry.FrameworkRunnerOverride = static action => Task.Run(action);
        registry.Subscribe("k", _ => throw new InvalidOperationException("marshaled boom"), new() { Delivery = SubscriptionDelivery.FrameworkThread });

        registry.Dispatch("k", 0).Should().Be(1, "a marshaled delivery is counted when it is handed to the framework thread");

        (await reportedSignal.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("a marshaled fault has no caller left to propagate to");
        reported.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Marshaled_Handler_Fault_Is_Reported_By_Default()
    {
        Exception? reported = null;
        var reportedSignal = new SemaphoreSlim(0);
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) =>
        {
            reported = ex;
            reportedSignal.Release();
        });

        registry.FrameworkRunnerOverride = static action => Task.Run(action);
        registry.Subscribe("k", _ => throw new InvalidOperationException("marshaled boom"), new() { Delivery = SubscriptionDelivery.FrameworkThread });

        registry.Dispatch("k", 0);

        (await reportedSignal.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        reported.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void UnsubscribeOwner_For_A_Key_Leaves_Other_Keys_And_Owners()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var owner = new object();
        var calls = new List<string>();

        registry.Subscribe("a", _ => calls.Add("a-owned"), new() { Owner = owner });
        registry.Subscribe("a", _ => calls.Add("a-other"));
        registry.Subscribe("b", _ => calls.Add("b-owned"), new() { Owner = owner });

        registry.UnsubscribeOwner("a", owner).Should().Be(1);
        registry.UnsubscribeOwner("missing", owner).Should().Be(0);

        registry.Dispatch("a", 0);
        registry.Dispatch("b", 0);

        calls.Should().Equal("a-other", "b-owned");
    }

    [Fact]
    public void UnsubscribeFirst_Removes_The_First_In_Dispatch_Order()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var owner = new object();
        var calls = new List<string>();

        var low = registry.Subscribe("k", _ => calls.Add("low"), new() { Owner = owner });
        registry.Subscribe("k", _ => calls.Add("high"), new() { Priority = 10 });
        registry.Subscribe("k", _ => calls.Add("mid-owned"), new() { Priority = 5, Owner = owner });

        registry.UnsubscribeFirst("k", owner).Should().BeTrue();
        registry.Dispatch("k", 0);
        calls.Should().Equal("high", "low");

        registry.UnsubscribeFirst("k").Should().BeTrue();
        calls.Clear();
        registry.Dispatch("k", 0);
        calls.Should().Equal("low");

        registry.UnsubscribeFirst("k", new object()).Should().BeFalse();
        registry.UnsubscribeFirst("missing").Should().BeFalse();
        low.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Unsubscribe_By_Token_Reports_Whether_It_Removed()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var other = new NoireSubscriptionRegistry<string, int>();

        var token = registry.Subscribe("k", _ => { });
        var foreign = other.Subscribe("k", _ => { });

        registry.Unsubscribe(foreign).Should().BeFalse("a token issued by another registry is not this registry's to remove");
        foreign.IsActive.Should().BeTrue();

        registry.Unsubscribe(token).Should().BeTrue();
        registry.Unsubscribe(token).Should().BeFalse();
        registry.Count("k").Should().Be(0);
    }
}
