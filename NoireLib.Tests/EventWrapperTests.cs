using System;
using System.Collections.Generic;
using FluentAssertions;
using NoireLib.Events;
using System.Runtime.Versioning;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Unit tests for the event wrapper types.
/// </summary>
[SupportedOSPlatform("windows")]
public class EventWrapperTests
{
    private sealed class DummyPublisher
    {
        public event EventHandler<EventArgs>? Updated;

        public void RaiseUpdated()
        {
            Updated?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void GenericWrapper_EnableAndDisable_SubscribeAndUnsubscribeHandler()
    {
        EventHandler? publishedEvent = null;
        var subscribers = 0;

        using var wrapper = new EventWrapper<EventHandler>(
            subscribe: callback =>
            {
                publishedEvent += callback;
                subscribers++;
            },
            unsubscribe: callback =>
            {
                publishedEvent -= callback;
                subscribers--;
            });

        wrapper.IsEnabled.Should().BeFalse();

        wrapper.Enable();
        wrapper.IsEnabled.Should().BeTrue();
        subscribers.Should().Be(1);
        publishedEvent.Should().NotBeNull();

        wrapper.Disable();
        wrapper.IsEnabled.Should().BeFalse();
        subscribers.Should().Be(0);
        publishedEvent.Should().BeNull();
    }

    [Fact]
    public void GenericWrapper_InvokesRegisteredCallbacks_WhenEventIsRaised()
    {
        var publisher = new DummyPublisher();
        var calls = 0;

        using var wrapper = new EventWrapper<EventHandler<EventArgs>>(publisher, nameof(DummyPublisher.Updated), autoEnable: true);
        wrapper.AddCallback("first", (_, _) => calls++);
        wrapper.AddCallback("second", (_, _) => calls++);

        publisher.RaiseUpdated();

        calls.Should().Be(2);
    }

    [Fact]
    public void GenericWrapper_RegistersInitialCallback_WhenConstructedFromTargetAndEventName()
    {
        var publisher = new DummyPublisher();
        var calls = 0;

        using var wrapper = new EventWrapper<EventHandler<EventArgs>>(publisher, nameof(DummyPublisher.Updated), (_, _) => calls++, autoEnable: true);

        publisher.RaiseUpdated();

        calls.Should().Be(1);
    }

    [Fact]
    public void Wrapper_StateCallbacks_TrackLifecycleTransitions()
    {
        var publisher = new DummyPublisher();
        var states = new List<EventCallbackKind>();

        using var wrapper = new EventWrapper(publisher, nameof(DummyPublisher.Updated));
        wrapper.AddStateCallback("state", (_, state) => states.Add(state));

        wrapper.Enable();
        wrapper.Disable();

        states.Should().Equal(EventCallbackKind.Enabled, EventCallbackKind.Disabled);
    }

    [Fact]
    public void Wrapper_Dispose_UnsubscribesAndClearsCallbacks()
    {
        var publisher = new DummyPublisher();
        var calls = 0;

        var wrapper = new EventWrapper<EventHandler<EventArgs>>(publisher, nameof(DummyPublisher.Updated), autoEnable: true);
        wrapper.AddCallback("callback", (_, _) => calls++);

        wrapper.Dispose();
        publisher.RaiseUpdated();

        wrapper.IsDisposed.Should().BeTrue();
        wrapper.CallbackKeys.Should().BeEmpty();
        calls.Should().Be(0);
    }
}
