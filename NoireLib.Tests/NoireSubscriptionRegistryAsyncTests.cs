using FluentAssertions;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the additions the EventBus rework needs from the shared registry: <see cref="NoireSubscriptionRegistry{TKey, TContext}.DispatchAsync"/>
/// (awaiting async handlers, sharing the filter/once/priority core with the synchronous dispatch) and the opt-in
/// <c>propagateHandlerExceptions</c> mode (a handler fault surfaces to the dispatcher rather than being caught and reported).<br/>
/// The default catch-and-report behavior is covered by <see cref="NoireSubscriptionRegistryTests"/>; these tests only
/// exercise the two new capabilities.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireSubscriptionRegistryAsyncTests
{
    [Fact]
    public async Task DispatchAsync_Awaits_Async_Handlers_To_Completion()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var completed = false;

        registry.SubscribeAsync("k", async _ =>
        {
            await Task.Delay(50);
            completed = true;
        });

        var delivered = await registry.DispatchAsync("k", 1);

        delivered.Should().Be(1);
        completed.Should().BeTrue("DispatchAsync must await async handlers, unlike the fire-and-forget synchronous Dispatch");
    }

    [Fact]
    public async Task DispatchAsync_Invokes_Handlers_In_Priority_Order()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var order = new List<string>();

        registry.Subscribe("k", _ => order.Add("low"), new() { Priority = 0 });
        registry.Subscribe("k", _ => order.Add("high"), new() { Priority = 100 });
        // Records its position synchronously when invoked (before any await), so the ordering is deterministic and
        // the list is only ever touched on the dispatching thread. Awaiting-to-completion is covered separately.
        registry.SubscribeAsync("k", _ => { order.Add("mid-async"); return Task.CompletedTask; }, new() { Priority = 50 });

        await registry.DispatchAsync("k", 0);

        order.Should().Equal("high", "mid-async", "low");
    }

    [Fact]
    public async Task DispatchAsync_Applies_Filter_And_Once()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();
        var filteredRuns = 0;
        var onceRuns = 0;

        registry.Subscribe("k", _ => filteredRuns++, new() { Filter = value => value > 10 });
        registry.SubscribeAsync("k", async _ => { await Task.Yield(); onceRuns++; }, new() { Once = true });

        await registry.DispatchAsync("k", 1);   // filter rejects, once claims
        await registry.DispatchAsync("k", 20);  // filter passes; once already gone

        filteredRuns.Should().Be(1, "the filter rejected the first context only");
        onceRuns.Should().Be(1, "a one-shot subscription runs exactly once");
    }

    [Fact]
    public async Task DispatchAsync_Returns_Zero_When_No_Subscribers()
    {
        var registry = new NoireSubscriptionRegistry<string, int>();

        (await registry.DispatchAsync("absent", 0)).Should().Be(0);
    }

    [Fact]
    public void Propagate_Sync_Handler_Exception_Escapes_Dispatch_And_Aborts_The_Rest()
    {
        var registry = new NoireSubscriptionRegistry<string, int>(propagateHandlerExceptions: true);
        var afterThrowRan = false;

        registry.Subscribe("k", _ => throw new InvalidOperationException("boom"), new() { Priority = 100 });
        registry.Subscribe("k", _ => afterThrowRan = true, new() { Priority = 0 });

        var act = () => registry.Dispatch("k", 0);

        act.Should().Throw<InvalidOperationException>("with propagation on, a handler fault surfaces to the dispatcher");
        afterThrowRan.Should().BeFalse("the throw aborts the remaining handlers in the snapshot");
    }

    [Fact]
    public void NoPropagate_Sync_Handler_Exception_Is_Reported_Not_Thrown()
    {
        Exception? reported = null;
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) => reported = ex);
        var afterThrowRan = false;

        registry.Subscribe("k", _ => throw new InvalidOperationException("boom"), new() { Priority = 100 });
        registry.Subscribe("k", _ => afterThrowRan = true, new() { Priority = 0 });

        var act = () => registry.Dispatch("k", 0);

        act.Should().NotThrow("the default mode catches handler faults");
        reported.Should().BeOfType<InvalidOperationException>();
        afterThrowRan.Should().BeTrue("catching the fault lets the remaining handlers still run");
    }

    [Fact]
    public async Task Propagate_Async_Handler_Fault_Surfaces_From_DispatchAsync()
    {
        var registry = new NoireSubscriptionRegistry<string, int>(propagateHandlerExceptions: true);

        registry.SubscribeAsync("k", async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
        });

        var act = async () => await registry.DispatchAsync("k", 0);

        await act.Should().ThrowAsync<InvalidOperationException>("DispatchAsync awaits the handler, so its fault surfaces to the caller under propagation");
    }

    [Fact]
    public async Task NoPropagate_Async_Handler_Fault_Is_Reported_From_DispatchAsync()
    {
        Exception? reported = null;
        var registry = new NoireSubscriptionRegistry<string, int>((ex, _) => reported = ex);

        registry.SubscribeAsync("k", async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
        });

        var act = async () => await registry.DispatchAsync("k", 0);

        await act.Should().NotThrowAsync("the default mode catches async faults");
        reported.Should().BeOfType<InvalidOperationException>();
    }
}
