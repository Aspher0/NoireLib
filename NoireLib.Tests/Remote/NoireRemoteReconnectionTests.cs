using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The caller holds its object and the library repairs everything under it: the game restarts on another port, the
/// connection is rebuilt against the same target and every subscription is sent again.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteReconnectionTests : NoireRemoteTestBase
{
    public NoireRemoteReconnectionTests()
    {
        NoireRemoteClient.Options.RegistryDirectory = RegistryDirectory;
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemoteClient.Refresh();
    }

    [NoireRemoteClass("Repair", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class RepairProbe
    {
        [NoireRemote]
        public static string Who() => "probe";

        [NoireRemote]
        public static event Action<string>? OnMessage;

        internal static void Raise(string text) => OnMessage?.Invoke(text);
    }

    [Fact]
    public async Task AClientSurvivesTheHostRestarting()
    {
        NoireRemote.Start();
        var publication = NoireRemote.PublishType(typeof(RepairProbe));

        using var client = new NoireRemoteClient("Repair", NoireRemoteTransport.Websocket);
        (await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("probe");

        publication.Dispose();
        NoireRemote.DisposeAll();

        NoireRemote.Options.RegistryDirectory = RegistryDirectory;
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Options.AutoStart = false;
        NoireRemote.Options.EnableLogging = false;
        NoireRemote.Options.Port = 0;
        NoireRemote.Start();
        using var second = NoireRemote.PublishType(typeof(RepairProbe));
        NoireRemoteClient.Refresh();

        (await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(15))).Should().Be("probe",
            "the target is resolved again on every call, never held as an address");
    }

    [Fact]
    public async Task SubscriptionsComeBackAfterAReconnect()
    {
        NoireRemote.Start();
        var publication = NoireRemote.PublishType(typeof(RepairProbe));

        using var client = new NoireRemoteClient("Repair", NoireRemoteTransport.Websocket);
        var seen = new ConcurrentQueue<string>();
        using var subscription = client.Subscribe("repair.", (_, payload) => seen.Enqueue(payload?.ToObject<string>() ?? string.Empty));

        await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(300);

        publication.Dispose();
        NoireRemote.DisposeAll();

        NoireRemote.Options.RegistryDirectory = RegistryDirectory;
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.Options.AutoStart = false;
        NoireRemote.Options.EnableLogging = false;
        NoireRemote.Options.Port = 0;
        NoireRemote.Start();
        using var second = NoireRemote.PublishType(typeof(RepairProbe));
        NoireRemoteClient.Refresh();

        await Eventually(() => client.HasOpenSocket, TimeSpan.FromSeconds(20));
        await Task.Delay(300);

        RepairProbe.Raise("after");

        await Eventually(() => seen.Any(text => text == "after"), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ACallDuringTheGapFailsAtOnce()
    {
        NoireRemote.Start();
        var publication = NoireRemote.PublishType(typeof(RepairProbe));

        using var client = new NoireRemoteClient("Repair", NoireRemoteTransport.Websocket);
        await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));

        publication.Dispose();
        NoireRemote.DisposeAll();
        NoireRemoteClient.Refresh();

        var act = () => client.InvokeAsync<string>("Who");

        await act.Should().ThrowAsync<NoireRemoteException>("nothing ran, and the caller is told at once, never left waiting");
    }

    [Fact]
    public async Task AMetadataChangeReachesAConnectedCaller()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(RepairProbe));

        using var client = new NoireRemoteClient("Repair", NoireRemoteTransport.Websocket);
        await client.InvokeAsync<string>("Who").WaitAsync(TimeSpan.FromSeconds(10));

        NoireRemote.Self.Set("role", "tank");

        await Eventually(() => client.LastSender?["role"] == "tank", TimeSpan.FromSeconds(10));
    }

    private static async Task Eventually(Func<bool> condition, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(100);
        }

        condition().Should().BeTrue("the condition never held within " + window);
    }
}
