using FluentAssertions;
using NoireLib.Remote;
using System.Linq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The attributed consumer class: a property of delegate or wrapper type calls a member, an annotated event
/// receives one, and a handler learns which instance sent it. It is the NoireRemote half of NoireIPC's consumers.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteConsumerTests : NoireRemoteTestBase
{
    public NoireRemoteConsumerTests()
    {
        NoireRemoteClient.Options.RegistryDirectory = RegistryDirectory;
        NoireRemoteClient.Refresh();
    }

    // Background: this suite is caller and listener at once, with no framework thread.
    [NoireRemoteClass("Bound", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class BoundProbe
    {
        [NoireRemote]
        public static string Who() => "probe";

        [NoireRemote]
        public static int Double(int value) => value * 2;

        [NoireRemote]
        public static event Action<string>? OnMessage;

        internal static void Raise(string text) => OnMessage?.Invoke(text);
    }

    [NoireRemoteClass("Bound")]
    public static class BoundClient
    {
        [NoireRemote]
        public static NoireRemoteConsumer<Func<Task<string>>> Who { get; set; } = null!;

        [NoireRemote]
        public static NoireRemoteConsumer<Func<int, Task<int>>> Double { get; set; } = null!;
    }

    [NoireRemoteClass("Bound")]
    public static class BoundEventClient
    {
        [NoireRemote("bound.onmessage")]
        public static event Action<string>? OnMessage;

        internal static Action<string>? Handler
        {
            get => OnMessage;
        }
    }

    [NoireRemoteClass("MixedBinding")]
    public static class MixedClient
    {
        [NoireRemote]
        public static NoireRemoteConsumer<Func<Task<string>>> Who { get; set; } = null!;

        [NoireRemote]
        public static int Value() => 1;
    }

    [Fact]
    public void AConsumerWrapper_IsNotMistakenForSomethingToPublish()
    {
        NoireRemote.Start();

        using var publication = NoireRemote.PublishType(typeof(MixedClient));

        var members = NoireRemote.Manifest().Endpoints.Single(endpoint => endpoint.Name == "MixedBinding").Members;

        members.Should().ContainSingle().Which.Name.Should().Be("Value");
    }

    [Fact]
    public async Task AConsumerProperty_CallsTheRemoteMember()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(BoundProbe));

        using var binding = NoireRemote.Bind(typeof(BoundClient));

        (await BoundClient.Who.Invoke().WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("probe");
    }

    [Fact]
    public async Task AConsumerProperty_CarriesItsArgumentsByName()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(BoundProbe));

        using var binding = NoireRemote.Bind(typeof(BoundClient));

        (await BoundClient.Double.Invoke(21).WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(42);
    }

    [Fact]
    public void AConsumer_ReportsWhetherAnythingIsListening()
    {
        using var binding = NoireRemote.Bind(typeof(BoundClient));

        BoundClient.Who.IsAvailable.Should().BeFalse("nothing is publishing the surface yet");

        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(BoundProbe));
        NoireRemoteClient.Refresh();

        BoundClient.Who.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void ABindingClearsWhatItFilled()
    {
        var binding = NoireRemote.Bind(typeof(BoundClient));
        BoundClient.Who.Should().NotBeNull();

        binding.Dispose();

        BoundClient.Who.Should().BeNull("a binding takes back every property it filled");
    }

    [Fact]
    public void ATypeThatBothPublishesAndConsumes_IsRefused()
    {
        var act = () => NoireRemote.Bind(typeof(MixedClient));

        act.Should().Throw<InvalidOperationException>().WithMessage("*publish*consume*");
    }

    [Fact]
    public void ATypeWithNothingToBind_PointsAtPublish()
    {
        var act = () => NoireRemote.Bind(typeof(BoundProbe));

        act.Should().Throw<InvalidOperationException>().WithMessage("*NoireRemote.Publish*");
    }

    [Fact]
    public async Task AConsumerEvent_ReceivesWhatTheHostRaises()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(BoundProbe));

        using var binding = NoireRemote.Bind(typeof(BoundEventClient));
        var seen = new TaskCompletionSource<string>();
        var sender = new TaskCompletionSource<string?>();

        BoundEventClient.OnMessage += text =>
        {
            sender.TrySetResult(NoireRemote.Sender?.Label);
            seen.TrySetResult(text);
        };

        await Task.Delay(500);
        BoundProbe.Raise("hello");

        (await seen.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("hello");
        (await sender.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(NoireRemote.Label);
    }
}
