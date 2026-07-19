using FluentAssertions;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the subscription registry's dispatch contract: subscribers fire only for their own key, in priority order
/// with ties preserving subscription order, and a keyed subscription replaces the previous one for that key.<br/>
/// A once-subscription fires exactly one time and survives non-matching or throwing filters until one matches, a
/// handler's exception is reported rather than thrown, unsubscribing (by token, owner, or Clear/ClearAll) is safe
/// even from inside a dispatch, and FrameworkThread delivery falls back to inline when Dalamud is not running.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireSubscriptionRegistryTests
{
    [Fact]
    public void Dispatch_Invokes_Subscribers_For_Key_Only()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var received = new List<int>();

        registry.Subscribe("a", value => received.Add(value));
        registry.Subscribe("b", value => received.Add(value * 100));

        registry.Dispatch("a", 5).Should().Be(1);

        received.Should().Equal(5);
    }

    [Fact]
    public void Priority_Higher_Runs_First_Stable_For_Equal()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var order = new List<string>();

        registry.Subscribe("k", _ => order.Add("low"), new() { Priority = 0 });
        registry.Subscribe("k", _ => order.Add("high"), new() { Priority = 100 });
        registry.Subscribe("k", _ => order.Add("mid1"), new() { Priority = 50 });
        registry.Subscribe("k", _ => order.Add("mid2"), new() { Priority = 50 });

        registry.Dispatch("k", 0);

        order.Should().Equal("high", "mid1", "mid2", "low");
    }

    [Fact]
    public void Keyed_Subscription_Replaces_Previous()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var firstCalls = 0;
        var secondCalls = 0;

        var firstToken = registry.Subscribe("k", _ => firstCalls++, new() { Key = "my-key" });
        registry.Subscribe("k", _ => secondCalls++, new() { Key = "my-key" });

        registry.Dispatch("k", 0);

        firstCalls.Should().Be(0);
        secondCalls.Should().Be(1);
        firstToken.IsActive.Should().BeFalse();
        registry.Count("k").Should().Be(1);
    }

    [Fact]
    public void Token_Dispose_Unsubscribes_And_Is_Idempotent()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var calls = 0;

        var token = registry.Subscribe("k", _ => calls++);
        token.IsActive.Should().BeTrue();

        token.Dispose();
        token.Dispose();

        token.IsActive.Should().BeFalse();
        registry.Dispatch("k", 0);
        calls.Should().Be(0);
        registry.HasSubscribers("k").Should().BeFalse();
    }

    [Fact]
    public void Once_Subscription_Fires_Exactly_One_Time()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var calls = 0;

        var token = registry.Subscribe("k", _ => calls++, new() { Once = true });

        registry.Dispatch("k", 0);
        registry.Dispatch("k", 0);

        calls.Should().Be(1);
        token.IsActive.Should().BeFalse();
        registry.Count("k").Should().Be(0);
    }

    [Fact]
    public void Filter_Skips_Non_Matching_Contexts()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var received = new List<int>();

        registry.Subscribe("k", value => received.Add(value), new() { Filter = value => value > 10 });

        registry.Dispatch("k", 5);
        registry.Dispatch("k", 15);

        received.Should().Equal(15);
    }

    [Fact]
    public void Filtered_Once_Survives_Non_Matching_Dispatches_And_Fires_On_First_Match()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var received = new List<int>();

        var token = registry.Subscribe(
            "k",
            value => received.Add(value),
            new() { Once = true, Filter = value => value > 10 });

        // Non-matching dispatches must not consume the one-shot.
        registry.Dispatch("k", 5).Should().Be(0);
        registry.Dispatch("k", 7).Should().Be(0);

        token.IsActive.Should().BeTrue();
        registry.Count("k").Should().Be(1);

        // First match fires and consumes; anything after is ignored.
        registry.Dispatch("k", 15).Should().Be(1);
        registry.Dispatch("k", 20).Should().Be(0);
        registry.Dispatch("k", 25).Should().Be(0);

        received.Should().Equal(15);
        token.IsActive.Should().BeFalse();
        registry.Count("k").Should().Be(0);
    }

    [Fact]
    public void Filtered_Once_A_Throwing_Filter_Does_Not_Consume_The_One_Shot()
    {
        Exception? reported = null;
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) => reported = ex);
        var calls = 0;
        var throwOnFilter = true;

        var token = registry.Subscribe(
            "k",
            _ => calls++,
            new() { Once = true, Filter = _ => throwOnFilter ? throw new InvalidOperationException("filter boom") : true });

        // A filter that throws is treated as non-matching and must not consume the one-shot.
        registry.Dispatch("k", 0);
        calls.Should().Be(0);
        token.IsActive.Should().BeTrue();
        reported.Should().BeOfType<InvalidOperationException>();

        // Once the filter matches, the one-shot fires exactly once and is then removed.
        throwOnFilter = false;
        registry.Dispatch("k", 0);
        registry.Dispatch("k", 0);

        calls.Should().Be(1);
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Owner_Bulk_Unsubscribe_Removes_All_Owned()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var owner = new object();
        var ownedCalls = 0;
        var otherCalls = 0;

        registry.Subscribe("a", _ => ownedCalls++, new() { Owner = owner });
        registry.Subscribe("b", _ => ownedCalls++, new() { Owner = owner });
        registry.Subscribe("a", _ => otherCalls++);

        registry.UnsubscribeOwner(owner).Should().Be(2);

        registry.Dispatch("a", 0);
        registry.Dispatch("b", 0);

        ownedCalls.Should().Be(0);
        otherCalls.Should().Be(1);
    }

    [Fact]
    public void Handler_Exception_Does_Not_Break_Dispatch_And_Is_Reported()
    {
        Exception? reported = null;
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) => reported = ex);
        var secondCalled = false;

        registry.Subscribe("k", _ => throw new InvalidOperationException("boom"), new() { Priority = 10 });
        registry.Subscribe("k", _ => secondCalled = true, new() { Priority = 0 });

        registry.Dispatch("k", 0);

        secondCalled.Should().BeTrue();
        reported.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Async_Handler_Fault_Is_Reported_Not_Thrown()
    {
        Exception? reported = null;
        var reportedSignal = new SemaphoreSlim(0);
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) =>
        {
            reported = ex;
            reportedSignal.Release();
        });

        registry.SubscribeAsync("k", async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
        });

        registry.Dispatch("k", 0);

        (await reportedSignal.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        reported.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void Clear_And_ClearAll_Invalidate_Tokens()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();

        var tokenA = registry.Subscribe("a", _ => { });
        var tokenB1 = registry.Subscribe("b", _ => { });
        var tokenB2 = registry.Subscribe("b", _ => { }, new() { Key = "named" });

        registry.Clear("b").Should().Be(2);
        tokenB1.IsActive.Should().BeFalse();
        tokenB2.IsActive.Should().BeFalse();
        tokenA.IsActive.Should().BeTrue();

        // The keyed slot must be freed by Clear too.
        registry.Subscribe("b", _ => { }, new() { Key = "named" });
        registry.Count("b").Should().Be(1);

        registry.ClearAll().Should().Be(2);
        tokenA.IsActive.Should().BeFalse();
        registry.TotalCount.Should().Be(0);
    }

    [Fact]
    public void Unsubscribing_During_Dispatch_Is_Safe()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        NoireSubscriptionToken? tokenToKill = null;
        var laterCalled = 0;

        registry.Subscribe("k", _ => tokenToKill!.Dispose(), new() { Priority = 10 });
        tokenToKill = registry.Subscribe("k", _ => laterCalled++, new() { Priority = 0 });

        registry.Dispatch("k", 0);

        // The snapshot had already been taken, but the token is inactive by the time it is reached.
        laterCalled.Should().Be(0);
        registry.Count("k").Should().Be(1);
    }

    [Fact]
    public void FrameworkThread_Delivery_Falls_Back_To_Inline_Without_Dalamud()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var calls = 0;

        registry.Subscribe("k", _ => calls++, new() { Delivery = SubscriptionDelivery.FrameworkThread });

        registry.Dispatch("k", 0);

        calls.Should().Be(1);
    }
}
