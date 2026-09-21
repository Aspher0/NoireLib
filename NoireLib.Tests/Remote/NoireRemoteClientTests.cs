using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Drives the caller half as a console program does, and covers the message when nothing is listening.</summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteClientTests : NoireRemoteTestBase
{
    public sealed record PickReport(bool Hit, int Polygon, float Height);

    [NoireRemoteClass("Console", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class ConsoleProbe
    {
        [NoireRemote]
        public static Vector3 GetPlayerPosition() => new(12.5f, 0f, -3.25f);

        [NoireRemote]
        public static PickReport RunPick(Vector3 origin, float radius = 2f) => new(true, (int)origin.X, radius);

        [NoireRemote]
        public static void PlaceMarkers(IReadOnlyList<Vector3> points, string? label = null)
        {
            lock (Placed)
            {
                Placed.Clear();
                Placed.AddRange(points);
                Label = label;
            }
        }

        public static readonly List<Vector3> Placed = [];

        public static string? Label;

        [NoireRemote]
        public static int Throws() => throw new InvalidOperationException("no");
    }

    [NoireRemoteClass("Console")]
    public interface IConsoleApi
    {
        Task<Vector3> GetPlayerPosition();

        Task<PickReport> RunPick(Vector3 origin, float radius);
    }

    [NoireRemoteClass("Console")]
    public interface IWrongApi
    {
        Task<Vector3> GetPlayerPositionn();
    }

    public NoireRemoteClientTests()
    {
        NoireRemoteClient.Options.RegistryDirectory = RegistryDirectory;
        NoireRemoteClient.Options.CallTimeout = TimeSpan.FromSeconds(20);
        NoireRemoteClient.Refresh();
    }

    public override void Dispose()
    {
        NoireRemoteClient.Options.RegistryDirectory = NoireRemoteDirectory.DefaultDirectory();
        NoireRemoteClient.Options.CallTimeout = TimeSpan.FromSeconds(10);
        NoireRemoteClient.Refresh();
        base.Dispose();
    }

    private void StartListener()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.PublishType(typeof(ConsoleProbe));
        NoireRemote.Start();
        NoireRemoteClient.Refresh();
    }

    [Fact]
    public async Task OneLine_ReadsAValueOutOfAListeningPlugin()
    {
        StartListener();

        var position = await NoireRemoteClient.Endpoint("Console").CallAsync<Vector3>("GetPlayerPosition");

        position.Should().Be(new Vector3(12.5f, 0f, -3.25f));
    }

    [Fact]
    public async Task TheFlatRouteForm_SplitsOnTheLastSlash()
    {
        StartListener();

        var position = await NoireRemoteClient.CallAsync<Vector3>("Console/GetPlayerPosition");

        position.Should().Be(new Vector3(12.5f, 0f, -3.25f));
    }

    [Fact]
    public async Task AnAnonymousObject_BindsByParameterName()
    {
        StartListener();

        var report = await NoireRemoteClient.Endpoint("Console").CallAsync<PickReport>("RunPick", new
        {
            origin = new Vector3(4, 0, 0),
            radius = 3f,
        });

        report.Should().Be(new PickReport(true, 4, 3f));
    }

    [Fact]
    public async Task AVoidMember_IsCalledThroughTheOverloadWithNoResult()
    {
        StartListener();

        await NoireRemoteClient.Endpoint("Console").CallAsync("PlaceMarkers", new
        {
            points = new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) },
            label = "pick origin",
        });

        lock (ConsoleProbe.Placed)
        {
            ConsoleProbe.Placed.Should().Equal(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
            ConsoleProbe.Label.Should().Be("pick origin");
        }
    }

    [Fact]
    public void TheBlockingOverload_Works()
    {
        StartListener();

        NoireRemoteClient.Endpoint("Console").Call<Vector3>("GetPlayerPosition")
            .Should().Be(new Vector3(12.5f, 0f, -3.25f));
    }

    [Fact]
    public async Task AMemberThatThrows_ArrivesAsARemoteException()
    {
        StartListener();

        var act = async () => await NoireRemoteClient.Endpoint("Console").CallAsync<int>("Throws");

        var thrown = await act.Should().ThrowAsync<NoireRemoteRemoteException>();
        thrown.Which.RemoteType.Should().Be(nameof(InvalidOperationException));
        thrown.Which.RemoteMessage.Should().Be("no");
    }

    [Fact]
    public async Task AMissingArgument_ArrivesAsAnArgumentExceptionNamingIt()
    {
        StartListener();

        var act = async () => await NoireRemoteClient.Endpoint("Console").CallAsync<PickReport>("RunPick");

        var thrown = await act.Should().ThrowAsync<NoireRemoteArgumentException>();
        thrown.Which.ParameterName.Should().Be("origin");
        thrown.Which.Code.Should().Be(NoireRemoteErrorCodes.ArgumentMissing);
    }

    [Fact]
    public async Task WithNothingListening_TheFailureNamesTheFolderItLookedIn()
    {
        var act = async () => await NoireRemoteClient.Endpoint("MyApi").CallAsync<Vector3>("GetPlayerPosition");

        var thrown = await act.Should().ThrowAsync<NoireRemoteNotFoundException>();

        thrown.Which.Message.Should().Contain("MyApi");
        thrown.Which.Message.Should().Contain(RegistryDirectory);
    }

    [Fact]
    public async Task WithNothingListening_TheFailureArrivesQuickly()
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await NoireRemoteClient.Endpoint("MyApi").CallAsync<Vector3>("GetPlayerPosition");
        }
        catch (NoireRemoteNotFoundException)
        {
        }

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "one directory read is all it takes to know nothing is publishing the endpoint");
    }

    [Fact]
    public void WithOnlyADeadRecord_TheRecordIsRemovedAndTheFailureSaysSo()
    {
        Directory.CreateDirectory(RegistryDirectory);

        NoireRemoteDirectory.Write(RegistryDirectory, new NoireRemoteInstanceRecord
        {
            Instance = Guid.NewGuid(),
            Address = "127.0.0.1",
            Port = 47821,
            Token = "stale",
            Pid = int.MaxValue - 1,
            ProcessStartUtc = DateTime.UtcNow,
            StartedUtc = DateTime.UtcNow,
            HeartbeatUtc = DateTime.UtcNow,
            Plugin = "Gone",
            Endpoints = ["MyApi"],
        });

        NoireRemoteClient.Refresh();

        var act = () => NoireRemoteClient.Endpoint("MyApi").Call<int>("Anything");

        act.Should().Throw<NoireRemoteNotFoundException>().WithMessage("*stale record*");
        Directory.GetFiles(RegistryDirectory, "*.json").Should().BeEmpty();
    }

    [Fact]
    public void Discover_ListsTheLiveInstance()
    {
        StartListener();

        var instances = NoireRemoteClient.Discover("Console");

        instances.Should().ContainSingle();
        instances[0].Id.Should().Be(NoireRemote.InstanceId);
        instances[0].Port.Should().Be(NoireRemote.Port);
        instances[0].Endpoints.Should().Contain("Console");
        instances[0].ToString().Should().Contain("port " + NoireRemote.Port);
    }

    [Fact]
    public void Exists_IsTrueWhenSomethingPublishesTheEndpoint()
    {
        StartListener();

        NoireRemoteClient.Endpoint("Console").Exists.Should().BeTrue();
        NoireRemoteClient.Endpoint("NotThere").Exists.Should().BeFalse();
    }

    [Fact]
    public async Task AnInstanceFilterThatMatchesNothing_FailsWithTheFilterNamed()
    {
        StartListener();

        var act = async () => await NoireRemoteClient.Endpoint("Console").Instance(Guid.NewGuid()).CallAsync<Vector3>("GetPlayerPosition");

        var thrown = await act.Should().ThrowAsync<NoireRemoteNotFoundException>();
        thrown.Which.Message.Should().Contain("filter");
    }

    [Fact]
    public async Task AnInstanceNamedById_Resolves()
    {
        StartListener();

        var position = await NoireRemoteClient.Endpoint("Console").Instance(NoireRemote.InstanceId).CallAsync<Vector3>("GetPlayerPosition");

        position.X.Should().Be(12.5f);
    }

    [Fact]
    public async Task TwoInstancesPublishingOneEndpoint_RefuseUntilOneIsNamed()
    {
        StartListener();

        // A second record standing for another running game client, pointing at the same live listener.
        var second = new NoireRemoteInstanceRecord
        {
            Instance = Guid.NewGuid(),
            Address = "127.0.0.1",
            Port = NoireRemote.Port,
            Token = NoireRemote.Token,
            Pid = Environment.ProcessId,
            ProcessStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            StartedUtc = DateTime.UtcNow.AddMinutes(1),
            HeartbeatUtc = DateTime.UtcNow,
            Plugin = "OtherClient",
            Label = "Another Character @ Another World",
            Endpoints = ["Console"],
        };

        NoireRemoteDirectory.Write(RegistryDirectory, second);
        NoireRemoteClient.Refresh();

        var act = async () => await NoireRemoteClient.Endpoint("Console").CallAsync<Vector3>("GetPlayerPosition");

        var thrown = await act.Should().ThrowAsync<NoireRemoteAmbiguousEndpointException>();
        thrown.Which.Candidates.Should().HaveCount(2);
        thrown.Which.Message.Should().Contain("Another Character");
    }

    [Fact]
    public void Fanout_ListsEveryMatchingInstance()
    {
        StartListener();

        NoireRemoteClient.Endpoint("Console").All().Targets().Should().ContainSingle();
    }

    [Fact]
    public async Task Fanout_CollectsTheAnswerOfEveryInstanceThatAnswered()
    {
        StartListener();

        var answers = await NoireRemoteClient.Endpoint("Console").All().CallAsync<Vector3>("GetPlayerPosition");

        answers.Should().ContainSingle();
    }

    [Fact]
    public async Task TheManifest_ReadsThroughTheClient()
    {
        StartListener();

        var manifest = await NoireRemoteClient.Endpoint("Console").ManifestAsync();

        manifest.GetEndpoint("Console").Should().NotBeNull();
        manifest.GetEndpoint("Console")!.GetMember("RunPick").Should().NotBeNull();
    }

    [Fact]
    public async Task ATypedClient_CallsThroughItsInterface()
    {
        StartListener();

        var api = NoireRemoteClient.Connect<IConsoleApi>();

        (await api.GetPlayerPosition()).Should().Be(new Vector3(12.5f, 0f, -3.25f));
        (await api.RunPick(new Vector3(7, 0, 0), 1f)).Should().Be(new PickReport(true, 7, 1f));
    }

    [Fact]
    public void ATypedClientNamingAMemberThatIsNotPublished_RefusesAtConnectTime()
    {
        StartListener();

        var act = () => NoireRemoteClient.Connect<IWrongApi>();

        act.Should().Throw<NoireRemoteContractException>().WithMessage("*GetPlayerPositionn*");
    }

    [Fact]
    public async Task AStaleCredential_IsRetriedOnceAfterRereadingTheDirectory()
    {
        StartListener();

        await NoireRemoteClient.Endpoint("Console").CallAsync<Vector3>("GetPlayerPosition");

        NoireRemote.RotateToken();

        var position = await NoireRemoteClient.Endpoint("Console").CallAsync<Vector3>("GetPlayerPosition");

        position.X.Should().Be(12.5f, "a plugin reload rotates the credential, and one silent retry covers it");
    }

    [Fact]
    public async Task AnInstanceAddressedByHandWithNoCredential_IsRefused()
    {
        StartListener();

        var act = async () => await NoireRemoteClient.At("127.0.0.1", NoireRemote.Port).Endpoint("Console").CallAsync<Vector3>("GetPlayerPosition");

        await act.Should().ThrowAsync<NoireRemoteUnauthorizedException>();
    }

    [Fact]
    public void At_ReadsHostAndPortOutOfOneString()
    {
        var instance = NoireRemoteClient.At("192.168.1.42:47821", "secret");

        instance.Address.Should().Be("192.168.1.42");
        instance.Port.Should().Be(47821);
    }

    [Fact]
    public void At_RefusesAnAddressWithNoPort()
    {
        var act = () => NoireRemoteClient.At("192.168.1.42");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Events_ArriveThroughTheClient()
    {
        StartListener();

        using var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
        var received = new List<NoireRemoteEvent>();

        var reader = Task.Run(async () =>
        {
            await foreach (var item in NoireRemoteClient.Endpoint("Console").EventsAsync("nav", cancellation.Token))
            {
                received.Add(item);
                await cancellation.CancelAsync();
                break;
            }
        }, cancellation.Token);

        await Task.Delay(200, System.Threading.CancellationToken.None);
        NoireRemote.PublishEvent("nav.pick", new { polygon = 77 });

        try
        {
            await reader;
        }
        catch (OperationCanceledException)
        {
        }

        received.Should().ContainSingle().Which.Topic.Should().Be("nav.pick");
    }
}
