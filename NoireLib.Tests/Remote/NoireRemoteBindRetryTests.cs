using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// What a listener does when the port it was told to use is not free. A plugin that reloads meets the connections its
/// own previous instance left behind, and the port it was configured with stays held for as long as the operating
/// system keeps them. Failing there leaves the listener down with one line in a log nobody is reading.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteBindRetryTests : NoireRemoteTestBase
{
    [NoireRemoteClass("Rebind", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class RebindProbe
    {
        [NoireRemote]
        public static string Who() => "rebind";
    }

    private static int TakeAPort(out TcpListener holder)
    {
        holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();

        return ((IPEndPoint)holder.LocalEndpoint).Port;
    }

    [Fact]
    public async Task APortThatIsBusy_IsTakenAsSoonAsItFrees()
    {
        var port = TakeAPort(out var holder);

        NoireRemote.PublishType(typeof(RebindProbe));
        NoireRemote.Options.Port = port;
        NoireRemote.Options.FallBackToAnyPort = false;
        NoireRemote.Options.BindRetry = NoireRetryPolicy.Default with
        {
            InitialDelay = TimeSpan.FromMilliseconds(100),
            Multiplier = 1.0,
            Jitter = 0,
        };

        NoireRemote.Start();

        NoireRemote.State.Should().Be(NoireRemoteListenerState.Failed);
        NoireRemote.Port.Should().Be(0);

        holder.Stop();

        var gaveUpAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (NoireRemote.State != NoireRemoteListenerState.Listening && DateTime.UtcNow < gaveUpAt)
            await Task.Delay(50, TestContext.Current.CancellationToken);

        NoireRemote.State.Should().Be(NoireRemoteListenerState.Listening);

        NoireRemote.Port.Should().Be(port);
    }

    [Fact]
    public void ABusyPort_ByDefault_IsReplacedByAnyFreeOne()
    {
        var port = TakeAPort(out var holder);

        try
        {
            NoireRemote.PublishType(typeof(RebindProbe));
            NoireRemote.Options.Port = port;
            NoireRemote.Start();

            // Something is accepting on the port. The listener falls back to another one.
            NoireRemote.State.Should().Be(NoireRemoteListenerState.Listening);
            NoireRemote.Port.Should().NotBe(port).And.BeGreaterThan(0);
        }
        finally
        {
            holder.Stop();
        }
    }

    [Fact]
    public void AListenerStoppedWhileWaiting_StaysStopped()
    {
        var port = TakeAPort(out var holder);

        try
        {
            NoireRemote.PublishType(typeof(RebindProbe));
            NoireRemote.Options.Port = port;
            NoireRemote.Options.FallBackToAnyPort = false;
            NoireRemote.Options.BindRetry = NoireRetryPolicy.Default with { InitialDelay = TimeSpan.FromMilliseconds(50) };
            NoireRemote.Start();

            NoireRemote.State.Should().Be(NoireRemoteListenerState.Failed);

            NoireRemote.Stop();

            NoireRemote.State.Should().Be(NoireRemoteListenerState.Stopped);
        }
        finally
        {
            holder.Stop();
        }
    }

    [Fact]
    public void WithNoRetryPolicy_ARefusedBindFailsAtOnce()
    {
        var port = TakeAPort(out var holder);

        try
        {
            NoireRemote.PublishType(typeof(RebindProbe));
            NoireRemote.Options.Port = port;
            NoireRemote.Options.FallBackToAnyPort = false;
            NoireRemote.Options.BindRetry = NoireRetryPolicy.None;
            NoireRemote.Start();

            NoireRemote.State.Should().Be(NoireRemoteListenerState.Failed);
        }
        finally
        {
            holder.Stop();
        }
    }
}
