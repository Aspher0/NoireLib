using FluentAssertions;
using NoireLib.EventBus;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for <see cref="NoireEventBus.GetStatistics"/>, locking the invariant that
/// <see cref="EventBusStatistics.TotalEventsPublished"/> counts every publish call, not every delivery.<br/>
/// The count must not depend on whether an event type has subscribers, whether a filter admits the event, or
/// whether the bus takes its early-return path for an event type nobody subscribed to.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireEventBusStatisticsTests
{
    public record Unheard_Event(int Value);

    public record Heard_Event(int Value);

    private static NoireEventBus CreateEventBus() => new(null, true, enableLogging: false);

    [Fact]
    public void GetStatistics_CountsAPublishThatHasNoSubscribers()
    {
        var eventBus = CreateEventBus();

        eventBus.Publish(new Unheard_Event(1));

        eventBus.GetStatistics().TotalEventsPublished.Should().Be(1,
            "an event type nobody subscribed to was still published");
    }

    [Fact]
    public async Task GetStatistics_CountsAnAsyncPublishThatHasNoSubscribers()
    {
        var eventBus = CreateEventBus();

        await eventBus.PublishAsync(new Unheard_Event(1));

        eventBus.GetStatistics().TotalEventsPublished.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_CountsHeardAndUnheardPublishesAlike()
    {
        var eventBus = CreateEventBus();
        eventBus.Subscribe<Heard_Event>(_ => { });

        eventBus.Publish(new Heard_Event(1));
        eventBus.Publish(new Unheard_Event(2));
        eventBus.Publish(new Heard_Event(3));

        eventBus.GetStatistics().TotalEventsPublished.Should().Be(3,
            "the count does not depend on whether an event reached a handler");
    }

    [Fact]
    public void GetStatistics_CountsOnePublishRegardlessOfSubscriberCount()
    {
        var eventBus = CreateEventBus();
        eventBus.Subscribe<Heard_Event>(_ => { });
        eventBus.Subscribe<Heard_Event>(_ => { });
        eventBus.Subscribe<Heard_Event>(_ => { });

        eventBus.Publish(new Heard_Event(1));

        eventBus.GetStatistics().TotalEventsPublished.Should().Be(1,
            "the count tracks publishes, not handler invocations");
    }

    [Fact]
    public void GetStatistics_DoesNotCountAPublishRejectedByAnInactiveBus()
    {
        var eventBus = CreateEventBus();
        eventBus.SetActive(false);

        eventBus.Publish(new Unheard_Event(1));

        eventBus.GetStatistics().TotalEventsPublished.Should().Be(0,
            "an inactive bus refuses the publish outright, so nothing was published");
    }

    [Fact]
    public void GetStatistics_CountsAPublishWhoseSubscribersWereAllFilteredOut()
    {
        var eventBus = CreateEventBus();
        var handlerRuns = 0;
        eventBus.Subscribe<Heard_Event>(_ => handlerRuns++, filter: evt => evt.Value > 10);

        eventBus.Publish(new Heard_Event(1));

        handlerRuns.Should().Be(0, "the filter rejected the event");
        eventBus.GetStatistics().TotalEventsPublished.Should().Be(1,
            "a filter decides whether a handler runs, not whether the event was published");
    }
}
