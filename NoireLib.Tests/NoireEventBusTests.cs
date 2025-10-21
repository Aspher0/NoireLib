using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using NoireLib.EventBus;
using System.Runtime.Versioning;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the NoireEventBus module.
/// </summary>
[SupportedOSPlatform("windows")]
public class NoireEventBusTests
{
    public record Dummy_PlayerJobChangedEvent(uint OldJobId, uint NewJobId, string JobName);
    public record Dummy_Event2(DateTime Timestamp);
    public record Dummy_Event3(string DataType);
    public record Dummy_Event4();

    private NoireEventBus? eventBus;

    public NoireEventBusTests()
    {
        eventBus = new NoireEventBus(active: true, enableLogging: false);
    }

    [Fact]
    public void Subscribe_And_Publish_Simple_Event()
    {
        var handlerCalled = false;
        var receivedJobId = 0u;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(evt =>
        {
            handlerCalled = true;
            receivedJobId = evt.NewJobId;
        });

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));

        handlerCalled.Should().BeTrue();
        receivedJobId.Should().Be(19u);
    }

    [Fact]
    public void Multiple_Subscribers_All_Receive_Event()
    {
        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => handler1Called = true);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => handler2Called = true);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => handler3Called = true);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));

        handler1Called.Should().BeTrue();
        handler2Called.Should().BeTrue();
        handler3Called.Should().BeTrue();
    }

    [Fact]
    public void Priority_Orders_Handler_Execution()
    {
        var executionOrder = new List<int>();

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => executionOrder.Add(3), priority: 10);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => executionOrder.Add(1), priority: 100);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => executionOrder.Add(4), priority: 0);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => executionOrder.Add(2), priority: 50);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));

        executionOrder.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Filter_Conditionally_Invokes_Handler()
    {
        var callCount = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(
            _ => callCount++,
            filter: evt => evt.NewJobId == 19 // Only for Paladin
        );

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin")); // Should call
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior")); // Should not call
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(20, 19, "Paladin")); // Should call

        callCount.Should().Be(2);
    }

    [Fact]
    public void Unsubscribe_By_Token_Removes_Handler()
    {
        var callCount = 0;
        var token = eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount++);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        var unsubscribed = eventBus.Unsubscribe(token);
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));

        unsubscribed.Should().BeTrue();
        callCount.Should().Be(1);
    }

    [Fact]
    public void UnsubscribeFirst_Removes_Only_First_Handler_Without_Owner()
    {
        var callCount1 = 0;
        var callCount2 = 0;
        var callCount3 = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount1++);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount2++);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount3++);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        var removed = eventBus.UnsubscribeFirst<Dummy_PlayerJobChangedEvent>();
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));

        removed.Should().BeTrue();
        callCount1.Should().Be(1); // Removed after first publish
        callCount2.Should().Be(2); // Called both times
        callCount3.Should().Be(2); // Called both times
    }

    [Fact]
    public void UnsubscribeFirst_Removes_Only_First_Handler_With_Owner()
    {
        var owner1 = new object();
        var owner2 = new object();
        var callCount1 = 0;
        var callCount2 = 0;
        var callCount3 = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount1++, owner: owner1);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount2++, owner: owner1);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount3++, owner: owner2);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        var removed = eventBus.UnsubscribeFirst<Dummy_PlayerJobChangedEvent>(owner1);
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));

        removed.Should().BeTrue();
        callCount1.Should().Be(1); // Removed after first publish
        callCount2.Should().Be(2); // Still subscribed (owner1, but second handler)
        callCount3.Should().Be(2); // Different owner
    }

    [Fact]
    public void UnsubscribeFirst_Returns_False_When_No_Handler_Found()
    {
        var owner = new object();
        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => { }, owner: owner);

        var removed1 = eventBus.UnsubscribeFirst<Dummy_Event2>(); // Wrong event type
        var removed2 = eventBus.UnsubscribeFirst<Dummy_PlayerJobChangedEvent>(new object()); // Wrong owner

        removed1.Should().BeFalse();
        removed2.Should().BeFalse();
    }

    [Fact]
    public void UnsubscribeAll_Generic_Removes_All_Handlers_For_Event_Type()
    {
        var callCount1 = 0;
        var callCount2 = 0;
        var callCount3 = 0;
        var callCount4 = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount1++);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount2++);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount3++);
        eventBus.Subscribe<Dummy_Event2>(_ => callCount4++); // Different event type

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        eventBus.Publish(new Dummy_Event2(DateTime.Now));
        
        var removed = eventBus.UnsubscribeAll<Dummy_PlayerJobChangedEvent>();
        
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));
        eventBus.Publish(new Dummy_Event2(DateTime.Now));

        removed.Should().Be(3); // 3 handlers removed
        callCount1.Should().Be(1); // Called once before unsubscribe
        callCount2.Should().Be(1); // Called once before unsubscribe
        callCount3.Should().Be(1); // Called once before unsubscribe
        callCount4.Should().Be(2); // Different event type, still subscribed
    }

    [Fact]
    public void UnsubscribeAll_Generic_With_Owner_Removes_Only_Owner_Handlers()
    {
        var owner1 = new object();
        var owner2 = new object();
        var callCount1 = 0;
        var callCount2 = 0;
        var callCount3 = 0;
        var callCount4 = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount1++, owner: owner1);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount2++, owner: owner1);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount3++, owner: owner2);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount4++); // No owner

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        
        var removed = eventBus.UnsubscribeAll<Dummy_PlayerJobChangedEvent>(owner1);
        
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));

        removed.Should().Be(2); // 2 handlers from owner1 removed
        callCount1.Should().Be(1); // Owner1, removed
        callCount2.Should().Be(1); // Owner1, removed
        callCount3.Should().Be(2); // Owner2, still subscribed
        callCount4.Should().Be(2); // No owner, still subscribed
    }

    [Fact]
    public void UnsubscribeAll_Generic_Returns_Zero_When_No_Handlers()
    {
        var owner = new object();

        var removed1 = eventBus!.UnsubscribeAll<Dummy_PlayerJobChangedEvent>();
        var removed2 = eventBus.UnsubscribeAll<Dummy_PlayerJobChangedEvent>(owner);

        removed1.Should().Be(0);
        removed2.Should().Be(0);
    }

    [Fact]
    public void UnsubscribeAll_Generic_With_Multiple_Owners_And_No_Owner()
    {
        var owner1 = new object();
        var owner2 = new object();
        var callCount1 = 0;
        var callCount2 = 0;
        var callCount3 = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount1++, owner: owner1);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount2++, owner: owner2);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount3++);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        
        var removed = eventBus.UnsubscribeAll<Dummy_PlayerJobChangedEvent>(); // Remove ALL
        
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));

        removed.Should().Be(3); // All 3 handlers removed
        callCount1.Should().Be(1);
        callCount2.Should().Be(1);
        callCount3.Should().Be(1);
        
        eventBus.GetSubscriberCount<Dummy_PlayerJobChangedEvent>().Should().Be(0);
    }

    [Fact]
    public void UnsubscribeAll_By_Owner_Removes_All_Owner_Handlers()
    {
        var owner = new object();
        var callCount1 = 0;
        var callCount2 = 0;
        var callCount3 = 0;

        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount1++, owner: owner);
        eventBus.Subscribe<Dummy_Event2>(_ => callCount2++, owner: owner);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount3++); // Different owner

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        eventBus.Publish(new Dummy_Event2(DateTime.Now));
        
        var removedCount = eventBus.UnsubscribeAll(owner);
        
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));
        eventBus.Publish(new Dummy_Event2(DateTime.Now));

        removedCount.Should().Be(2);
        callCount1.Should().Be(1); // Called once before unsubscribe
        callCount2.Should().Be(1); // Called once before unsubscribe
        callCount3.Should().Be(2); // Called both times (different owner)
    }

    [Fact]
    public async Task SubscribeAsync_And_PublishAsync_Works()
    {
        var handlerCalled = false;
        var taskCompleted = false;

        eventBus!.SubscribeAsync<Dummy_Event3>(async evt =>
        {
            handlerCalled = true;
            await Task.Delay(3000); // Simulate async work
            taskCompleted = true;
        });

        await eventBus.PublishAsync(new Dummy_Event3("TestData"));

        handlerCalled.Should().BeTrue();
        taskCompleted.Should().BeTrue();
    }

    [Fact]
    public void GetSubscriberCount_Returns_Correct_Count()
    {
        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => { });
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => { });
        eventBus.Subscribe<Dummy_Event2>(_ => { });

        var jobChangeCount = eventBus.GetSubscriberCount<Dummy_PlayerJobChangedEvent>();
        var configSavedCount = eventBus.GetSubscriberCount<Dummy_Event2>();
        var noSubscribersCount = eventBus.GetSubscriberCount<Dummy_Event4>();

        jobChangeCount.Should().Be(2);
        configSavedCount.Should().Be(1);
        noSubscribersCount.Should().Be(0);
    }

    [Fact]
    public void GetStatistics_Tracks_Events_And_Subscriptions()
    {
        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => { });
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => { });
        eventBus.Subscribe<Dummy_Event2>(_ => { });

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));
        eventBus.Publish(new Dummy_Event2(DateTime.Now));

        var stats = eventBus.GetStatistics();

        stats.TotalEventsPublished.Should().Be(3);
        stats.ActiveSubscriptions.Should().Be(3);
        stats.RegisteredEventTypes.Should().Be(2);
    }

    [Fact]
    public void Exception_In_Handler_Does_Not_Stop_Other_Handlers()
    {
        var eventBus = new NoireEventBus(
            active: true,
            enableLogging: false,
            exceptionHandling: EventExceptionMode.LogAndContinue
        );

        var handler1Called = false;
        var handler3Called = false;

        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => handler1Called = true);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => throw new Exception("Test exception"));
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => handler3Called = true);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));

        handler1Called.Should().BeTrue();
        handler3Called.Should().BeTrue();
        
        var stats = eventBus.GetStatistics();
        stats.TotalExceptionsCaught.Should().Be(1);

        eventBus.Dispose();
    }

    [Fact]
    public void Inactive_EventBus_Does_Not_Publish()
    {
        var handlerCalled = false;
        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => handlerCalled = true);
        eventBus.SetActive(false);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));

        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public void ClearAllSubscriptions_Removes_All_Handlers()
    {
        var callCount = 0;
        eventBus!.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount++);
        eventBus.Subscribe<Dummy_PlayerJobChangedEvent>(_ => callCount++);
        eventBus.Subscribe<Dummy_Event2>(_ => callCount++);

        eventBus.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        eventBus.ClearAllSubscriptions();
        eventBus.Publish(new Dummy_PlayerJobChangedEvent(19, 20, "Warrior"));

        var stats = eventBus.GetStatistics();

        callCount.Should().Be(2); // Only first publish
        stats.ActiveSubscriptions.Should().Be(0);
    }

    [Fact]
    public void Events_Without_Subscribers_Do_Not_Throw()
    {
        Action act1 = () => eventBus!.Publish(new Dummy_PlayerJobChangedEvent(1, 19, "Paladin"));
        Action act2 = () => eventBus!.Publish(new Dummy_Event2(DateTime.Now));
        Action act3 = () => eventBus!.Publish(new Dummy_Event4());
        
        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
    }
}
