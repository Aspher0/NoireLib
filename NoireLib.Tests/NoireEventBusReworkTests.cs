using FluentAssertions;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the EventBus behaviors that survived the move onto the shared subscription registry but that
/// <see cref="NoireEventBusTests"/> does not exercise: the <see cref="EventExceptionMode"/> policies beyond
/// LogAndContinue, keyed subscription replacement and key-based unsubscription, the priority order of
/// <see cref="NoireEventBus.UnsubscribeFirst{TEvent}"/>, and routing a throwing filter through the exception policy.<br/>
/// These are the parts of the reimplementation that depend on the registry's exception-propagation mode and on this
/// module's own outer/inner token ledger, so they are the ones a regression would land in first.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireEventBusReworkTests
{
    public record Evt(int Value);

    private static NoireEventBus CreateBus(EventExceptionMode mode)
        => new(active: true, enableLogging: false, exceptionHandling: mode);

    [Fact]
    public void LogAndThrow_Propagates_And_Aborts_Remaining_Handlers()
    {
        var bus = CreateBus(EventExceptionMode.LogAndThrow);
        var afterThrowRan = false;

        bus.Subscribe<Evt>(_ => throw new InvalidOperationException("boom"), priority: 10);
        bus.Subscribe<Evt>(_ => afterThrowRan = true, priority: 0);

        var act = () => bus.Publish(new Evt(1));

        act.Should().Throw<EventBusException>("LogAndThrow re-throws to the publisher");
        afterThrowRan.Should().BeFalse("the throw aborts the remaining handlers");
        bus.GetStatistics().TotalExceptionsCaught.Should().Be(1);
    }

    [Fact]
    public async Task LogAndThrow_Propagates_From_PublishAsync()
    {
        var bus = CreateBus(EventExceptionMode.LogAndThrow);

        bus.SubscribeAsync<Evt>(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        var act = async () => await bus.PublishAsync(new Evt(1));

        await act.Should().ThrowAsync<EventBusException>("PublishAsync awaits the handler, so a LogAndThrow fault reaches the caller");
    }

    [Fact]
    public void Suppress_Swallows_The_Exception_But_Still_Counts_It()
    {
        var bus = CreateBus(EventExceptionMode.Suppress);
        var afterThrowRan = false;

        bus.Subscribe<Evt>(_ => throw new InvalidOperationException("boom"), priority: 10);
        bus.Subscribe<Evt>(_ => afterThrowRan = true, priority: 0);

        var act = () => bus.Publish(new Evt(1));

        act.Should().NotThrow("Suppress does not re-throw");
        afterThrowRan.Should().BeTrue("suppressing a fault lets the remaining handlers run");
        bus.GetStatistics().TotalExceptionsCaught.Should().Be(1, "a suppressed exception is still counted");
    }

    [Fact]
    public void A_Throwing_Filter_Is_Routed_Through_The_Exception_Policy()
    {
        var bus = CreateBus(EventExceptionMode.LogAndContinue);
        var handlerRan = false;

        bus.Subscribe<Evt>(_ => handlerRan = true, filter: _ => throw new InvalidOperationException("bad filter"));

        bus.Publish(new Evt(1));

        handlerRan.Should().BeFalse("a filter that throws does not admit the event");
        bus.GetStatistics().TotalExceptionsCaught.Should().Be(1, "a throwing filter is counted like a throwing handler");
    }

    [Fact]
    public void Keyed_Subscribe_Replaces_The_Previous_Subscription()
    {
        var bus = CreateBus(EventExceptionMode.LogAndContinue);
        var firstCalls = 0;
        var secondCalls = 0;

        bus.Subscribe<Evt>("shared-key", _ => firstCalls++);
        bus.Subscribe<Evt>("shared-key", _ => secondCalls++);

        bus.Publish(new Evt(1));

        firstCalls.Should().Be(0, "the second subscription with the same key replaced the first");
        secondCalls.Should().Be(1);
        bus.GetSubscriberCount<Evt>().Should().Be(1);
    }

    [Fact]
    public void Unsubscribe_By_Key_Removes_The_Subscription()
    {
        var bus = CreateBus(EventExceptionMode.LogAndContinue);
        var calls = 0;
        bus.Subscribe<Evt>("k", _ => calls++);

        bus.Publish(new Evt(1));
        var removed = bus.Unsubscribe("k");
        bus.Publish(new Evt(2));

        removed.Should().BeTrue();
        calls.Should().Be(1);
        bus.GetSubscriberCount<Evt>().Should().Be(0);
    }

    [Fact]
    public void UnsubscribeFirst_Removes_The_Highest_Priority_Handler()
    {
        var bus = CreateBus(EventExceptionMode.LogAndContinue);
        var order = new List<string>();

        bus.Subscribe<Evt>(_ => order.Add("low"), priority: 0);
        bus.Subscribe<Evt>(_ => order.Add("high"), priority: 10);

        bus.UnsubscribeFirst<Evt>();
        bus.Publish(new Evt(1));

        // UnsubscribeFirst removes the first handler in dispatch order, which is the highest priority one.
        order.Should().Equal("low");
    }

    [Fact]
    public void Keyed_Replacement_Preserves_Priority_Ordering_For_UnsubscribeFirst()
    {
        var bus = CreateBus(EventExceptionMode.LogAndContinue);
        var order = new List<string>();

        bus.Subscribe<Evt>(_ => order.Add("plain-high"), priority: 10);
        bus.Subscribe<Evt>("k", _ => order.Add("keyed-low"), priority: 0);
        bus.Subscribe<Evt>("k", _ => order.Add("keyed-mid"), priority: 5); // replaces the keyed-low entry

        bus.Publish(new Evt(1));

        // A replaced keyed subscription re-inserts at its own priority.
        order.Should().Equal("plain-high", "keyed-mid");
    }
}
