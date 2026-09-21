using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Covers what publishing accepts and what it drops: the default public rule, the annotated opt-in, the JSON check
/// that keeps an unsendable member off the surface, the reserved underscore prefix, name collisions, and the removal
/// a disposed publication performs.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemotePublishingTests : NoireRemoteTestBase
{
    [NoireRemoteClass("Probe")]
    public static class PublicProbe
    {
        [NoireRemote]
        public static Vector3 GetPosition() => new(1, 2, 3);

        [NoireRemote]
        public static void PlaceWaypoint(Vector3 position, string label)
        {
            _ = position;
            _ = label;
        }
        public static void ResetCache()
        {
        }

        // The analyzer refuses this signature. The probe exercises the runtime rule behind it.
#pragma warning disable NoireLib_004
        [NoireRemote]
        public static void TakesAPointer(IntPtr address) => _ = address;
#pragma warning restore NoireLib_004
    }

    [NoireRemoteClass("Annotated")]
    public static class AnnotatedProbe
    {
        [NoireRemote]
        public static int Published() => 1;

        public static int NotPublished() => 2;
    }

    [NoireRemoteClass("Values")]
    public static class ValueProbe
    {
        [NoireRemote]
        public static int MajorVersion => 7;

        [NoireRemote]
        public static int Counter { get; set; }

        [NoireRemote]
        public static IntPtr Address => IntPtr.Zero;

        [NoireRemote]
        public static Func<int>? Borrowed { get; set; }
    }

    [NoireRemoteClass("Probe")]
    public static class CollidingProbe
    {
        [NoireRemote]
        public static int Other() => 1;
    }

    [NoireRemoteClass("_reserved")]
    public static class ReservedProbe
    {
        [NoireRemote]
        public static int Value() => 1;
    }

    [NoireRemoteClass("Tuned")]
    public static class TunedProbe
    {
        [NoireRemote("renamed", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.PlayerLoaded, Mode = NoireRemoteCallMode.Job, TimeoutSeconds = 42, Access = NoireRemoteAccess.Remote)]
        public static Task<int> LongOne(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(1);
        }

        [NoireRemote]
        public static void Plain()
        {
        }
    }

    public sealed class InstanceProbe
    {
        public int Seen { get; private set; }

        [NoireRemote]
        public int Add(int value)
        {
            Seen += value;
            return Seen;
        }
    }

    [Fact]
    public void AnAttributedStaticClass_PublishesItsPublicMethods()
    {
        using var publication = NoireRemote.PublishType(typeof(PublicProbe));

        publication.Name.Should().Be("Probe");
        publication.Members.Should().Contain(member => member.Name == "GetPosition");
        publication.Members.Should().Contain(member => member.Name == "PlaceWaypoint");
    }

    [Fact]
    public void AMemberNobodyAnnotated_IsNotPublished()
    {
        using var publication = NoireRemote.PublishType(typeof(PublicProbe));

        publication.Members.Should().NotContain(member => member.Name == "ResetCache");
    }

    [Fact]
    public void AMemberWhoseSignatureCannotCrossJson_IsDroppedAndTheRestPublishes()
    {
        using var publication = NoireRemote.PublishType(typeof(PublicProbe));

        publication.Members.Should().NotContain(member => member.Name == "TakesAPointer");
        publication.Members.Should().HaveCountGreaterThan(1,
            "one unsendable member costs itself, never the whole endpoint");
    }

    [Fact]
    public void AnAnnotatedEndpoint_PublishesOnlyTheAnnotatedMembers()
    {
        using var publication = NoireRemote.PublishType(typeof(AnnotatedProbe));

        publication.Members.Should().ContainSingle().Which.Name.Should().Be("Published");
    }

    [Fact]
    public void AValueProperty_IsPublishedAsAReadOnlyMember()
    {
        using var publication = NoireRemote.PublishType(typeof(ValueProbe));

        var member = publication.Members.Should().ContainSingle(item => item.Name == "MajorVersion").Subject;

        member.Parameters.Should().BeEmpty();
        member.ReturnClrType.Should().Be<int>();
        member.ReadOnly.Should().BeTrue("a property reads, and a console draws it as a plain value");
    }

    [Fact]
    public async Task AValueProperty_IsReadOnEveryCall()
    {
        using var publication = NoireRemote.PublishType(typeof(ValueProbe));
        var member = publication.Members.Single(item => item.Name == "Counter");

        ValueProbe.Counter = 1;
        (await member.Invoker([], TestContext.Current.CancellationToken)).Should().Be(1);

        ValueProbe.Counter = 2;
        (await member.Invoker([], TestContext.Current.CancellationToken)).Should().Be(2,
            "the member reads the property, it does not capture what it held at publication");
    }

    [Fact]
    public void APropertyWhoseValueCannotCrossJson_IsDroppedLikeAMethod()
    {
        using var publication = NoireRemote.PublishType(typeof(ValueProbe));

        publication.Members.Should().NotContain(member => member.Name == "Address");
    }

    [Fact]
    public void AConsumerProperty_IsNotPublishedAsAValue()
    {
        using var publication = NoireRemote.PublishType(typeof(ValueProbe));

        publication.Members.Should().NotContain(member => member.Name == "Borrowed",
            "a property whose type is a delegate calls someone else, it does not publish");
    }

    [Fact]
    public void TwoTypesClaimingOneEndpointName_Refuse()
    {
        using var first = NoireRemote.PublishType(typeof(PublicProbe));

        var act = () => NoireRemote.PublishType(typeof(CollidingProbe));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PublicProbe*")
            .WithMessage("*CollidingProbe*");
    }

    [Fact]
    public void AnEndpointNameStartingWithAnUnderscore_Refuses()
    {
        var act = () => NoireRemote.PublishType(typeof(ReservedProbe));

        act.Should().Throw<InvalidOperationException>("the meta routes own that prefix");
    }

    [Fact]
    public void PublishingTheSameMemberTwice_Refuses()
    {
        using var first = NoireRemote.PublishType(typeof(PublicProbe));

        var act = () => NoireRemote.PublishType(typeof(PublicProbe));

        act.Should().Throw<InvalidOperationException>().WithMessage("*GetPosition*");
    }

    [Fact]
    public void DisposingAPublication_RemovesItsRoutes()
    {
        var publication = NoireRemote.PublishType(typeof(PublicProbe));

        NoireRemote.GetEndpoint("Probe").Should().NotBeNull();

        publication.Dispose();

        NoireRemote.GetEndpoint("Probe").Should().BeNull();
        publication.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void DisposingAPublicationTwice_IsSafe()
    {
        var publication = NoireRemote.PublishType(typeof(PublicProbe));

        publication.Dispose();
        publication.Dispose();

        NoireRemote.Endpoints.Should().BeEmpty();
    }

    [Fact]
    public void AMemberAttribute_OverridesEveryDefault()
    {
        using var publication = NoireRemote.PublishType(typeof(TunedProbe));

        var tuned = publication.Members.Should().ContainSingle(member => member.Name == "renamed").Subject;

        tuned.Thread.Should().Be(NoireRemoteThread.Background);
        tuned.Requires.Should().Be(NoireRemoteReadiness.PlayerLoaded);
        tuned.Mode.Should().Be(NoireRemoteCallMode.Job);
        tuned.Timeout.Should().Be(TimeSpan.FromSeconds(42));
        tuned.Access.Should().Be(NoireRemoteAccess.Remote);
        tuned.Route.Should().Be("Tuned/renamed");
    }

    [Fact]
    public void AMemberWithNoAttribute_TakesTheFrameworkThreadAndTheStateReadyGate()
    {
        using var publication = NoireRemote.PublishType(typeof(TunedProbe));

        var plain = publication.Members.Should().ContainSingle(member => member.Name == "Plain").Subject;

        plain.Thread.Should().Be(NoireRemoteThread.Framework);
        plain.Requires.Should().Be(NoireRemoteReadiness.StateReady,
            "a member on the framework thread exists to touch the game, and touching it before the state loads crashes the client");
        plain.Access.Should().Be(NoireRemoteAccess.Local);
        plain.Mode.Should().Be(NoireRemoteCallMode.Sync);
    }

    [Fact]
    public void ACancellationTokenParameter_NeverAppearsOnTheWire()
    {
        using var publication = NoireRemote.PublishType(typeof(TunedProbe));

        var tuned = publication.Members.Should().ContainSingle(member => member.Name == "renamed").Subject;

        tuned.Parameters.Should().ContainSingle().Which.IsCancellationToken.Should().BeTrue();
    }

    [Fact]
    public void AnInstance_PublishesItsInstanceMethods()
    {
        var probe = new InstanceProbe();
        using var publication = NoireRemote.Publish(probe, "Live");

        publication.Name.Should().Be("Live");
        publication.Members.Should().ContainSingle(member => member.Name == "Add");
    }

    [Fact]
    public void ADelegate_PublishesWithoutAnAttribute()
    {
        using var publication = NoireRemote.Publish("Manual", "Double", (int value) => value * 2);

        publication.Members.Should().ContainSingle().Which.Route.Should().Be("Manual/Double");
    }

    [Fact]
    public void ADelegateWhoseSignatureCannotCrossJson_Refuses()
    {
        var act = () => NoireRemote.Publish("Manual", "Bad", (IntPtr address) => address.ToInt64());

        act.Should().Throw<InvalidOperationException>().WithMessage("*pointer*");
    }

    [Fact]
    public void ADelegateWithMemberOptions_TakesThem()
    {
        using var publication = NoireRemote.Publish("Manual", "Slow", () => 1, new NoireRemoteMemberOptions
        {
            Thread = NoireRemoteThread.Background,
            Timeout = TimeSpan.FromSeconds(90),
            Access = NoireRemoteAccess.Remote,
        });

        var member = publication.Members[0];

        member.Thread.Should().Be(NoireRemoteThread.Background);
        member.Requires.Should().Be(NoireRemoteReadiness.None, "a background member reads no character state by default");
        member.Timeout.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Endpoints_ListsWhatIsPublished()
    {
        using var first = NoireRemote.PublishType(typeof(PublicProbe));
        using var second = NoireRemote.PublishType(typeof(AnnotatedProbe));

        NoireRemote.Endpoints.Should().HaveCount(2);
        NoireRemote.GetEndpoint("probe").Should().NotBeNull("endpoint names match ignoring case");
    }

    [Fact]
    public void AnEndpointName_DefaultsToTheTypeName()
    {
        using var publication = NoireRemote.Publish(new InstanceProbe());

        publication.Name.Should().Be(nameof(InstanceProbe));
    }

    [Fact]
    public void PublishAttributedTypes_PublishesEveryAttributedTypeItCan()
    {
        using var group = NoireRemote.PublishAttributedTypes(typeof(NoireRemotePublishingTests).Assembly);

        NoireRemote.GetEndpoint("Probe").Should().NotBeNull();
        NoireRemote.GetEndpoint("Annotated").Should().NotBeNull();
        NoireRemote.GetEndpoint("Tuned").Should().NotBeNull();
        NoireRemote.GetEndpoint("_reserved").Should().BeNull("a refused type is logged and skipped, never fatal");
    }

    [Fact]
    public void DisposingAGroup_RemovesEveryRouteItAdded()
    {
        var group = NoireRemote.PublishAttributedTypes(typeof(NoireRemotePublishingTests).Assembly);

        group.Count.Should().BeGreaterThan(0);
        group.Dispose();

        NoireRemote.Endpoints.Should().BeEmpty();
    }

    [Fact]
    public void AutoStart_BindsTheListenerOnTheFirstPublication()
    {
        NoireRemote.Options.AutoStart = true;

        using var publication = NoireRemote.PublishType(typeof(PublicProbe));

        NoireRemote.IsListening.Should().BeTrue();
        NoireRemote.Port.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AutoStartOff_LeavesTheSocketClosed()
    {
        using var publication = NoireRemote.PublishType(typeof(PublicProbe));

        NoireRemote.IsListening.Should().BeFalse();
        NoireRemote.Port.Should().Be(0);
    }

    [Fact]
    public void RemoteWithoutASecret_StaysOnLoopback()
    {
        NoireRemote.Options.EnableRemote = true;
        NoireRemote.Options.RemoteSecret = null;

        NoireRemote.Start();

        NoireRemote.Options.EnableRemote.Should().BeFalse("a caller without a secret could drive the game if this only warned. It must refuse");
        NoireRemote.IsListening.Should().BeTrue();
    }

    [Fact]
    public void Start_TwiceKeepsTheSamePort()
    {
        NoireRemote.Start();
        var port = NoireRemote.Port;

        NoireRemote.Start();

        NoireRemote.Port.Should().Be(port);
    }

    [Fact]
    public void Stop_ClosesTheSocketAndKeepsTheSurface()
    {
        using var publication = NoireRemote.PublishType(typeof(PublicProbe));

        NoireRemote.Start();
        NoireRemote.Stop();

        NoireRemote.IsListening.Should().BeFalse();
        NoireRemote.GetEndpoint("Probe").Should().NotBeNull();
    }

    [Fact]
    public void StateChanged_ReportsEveryTransition()
    {
        var seen = new List<NoireRemoteListenerState>();

        void Handler(NoireRemoteListenerState state) => seen.Add(state);

        NoireRemote.StateChanged += Handler;

        try
        {
            NoireRemote.Start();
            NoireRemote.Stop();
        }
        finally
        {
            NoireRemote.StateChanged -= Handler;
        }

        seen.Should().ContainInOrder(NoireRemoteListenerState.Starting, NoireRemoteListenerState.Listening, NoireRemoteListenerState.Stopped);
    }

    [Fact]
    public void RotateToken_HandsBackANewCredential()
    {
        var before = NoireRemote.Token;

        NoireRemote.RotateToken().Should().NotBe(before);
        NoireRemote.Token.Should().NotBe(before);
    }
}
