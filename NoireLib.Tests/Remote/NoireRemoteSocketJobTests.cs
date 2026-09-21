using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Long work over a socket: the call answers a job id straight away, the job keeps running, and its progress
/// reaches the caller live, as it is reported.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteSocketJobTests : NoireRemoteTestBase
{
    public NoireRemoteSocketJobTests()
    {
        NoireRemoteClient.Options.RegistryDirectory = RegistryDirectory;
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemoteClient.Refresh();
    }

    [NoireRemoteClass("SocketWork", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class SocketWorkProbe
    {
        [NoireRemote]
        public static bool Ready() => true;

        [NoireRemote(Mode = NoireRemoteCallMode.Job, TimeoutSeconds = 60)]
        public static async Task<int> Build(int steps, IProgress<double> progress, CancellationToken cancellationToken)
        {
            for (var step = 0; step < steps; step++)
            {
                progress.Report((step + 1) / (double)steps);
                await Task.Delay(20, cancellationToken);
            }

            return steps;
        }
    }

    [Fact]
    public async Task AJobStartedOverASocket_AnswersItsIdAtOnce()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(SocketWorkProbe));

        using var client = new NoireRemoteClient("SocketWork", NoireRemoteTransport.Websocket);

        var job = await client.StartAsync("Build", new { steps = 4 }).WaitAsync(TimeSpan.FromSeconds(10));

        job.Id.Should().NotBeNullOrWhiteSpace();
        job.State.Should().Be(NoireRemoteJobState.Running);
    }

    [Fact]
    public async Task AJobRunsToItsResult()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(SocketWorkProbe));

        using var client = new NoireRemoteClient("SocketWork", NoireRemoteTransport.Websocket);

        var job = await client.StartAsync("Build", new { steps = 4 }).WaitAsync(TimeSpan.FromSeconds(10));

        (await job.ResultAsync<int>().WaitAsync(TimeSpan.FromSeconds(20))).Should().Be(4);
    }

    [Fact]
    public async Task AJobsProgressReachesTheCallerAsItIsReported()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(SocketWorkProbe));

        using var client = new NoireRemoteClient("SocketWork", NoireRemoteTransport.Websocket);
        var seen = new ConcurrentQueue<double>();

        // Subscribed first. A six-step job ends in a fraction of a second.
        using var following = client.Subscribe(NoireRemotePaths.ProgressTopicPrefix, (_, payload) =>
        {
            var report = payload?.ToObject<NoireRemoteProgressReport>();

            if (report?.Value != null)
                seen.Enqueue(report.Value.Value);
        });

        await Task.Delay(400);

        var job = await client.StartAsync("Build", new { steps = 6 }).WaitAsync(TimeSpan.FromSeconds(10));

        await job.ResultAsync<int>().WaitAsync(TimeSpan.FromSeconds(20));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (seen.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        seen.Should().NotBeEmpty("progress is pushed live, as it happens");
        seen.Max().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AJobsAnswerIsPushedRatherThanPolled()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(SocketWorkProbe));

        using var client = new NoireRemoteClient("SocketWork", NoireRemoteTransport.Websocket);

        var job = await client.StartAsync("Build", new { steps = 3 }).WaitAsync(TimeSpan.FromSeconds(10));

        (await job.ResultAsync<int>().WaitAsync(TimeSpan.FromSeconds(20))).Should().Be(3);
        job.State.Should().Be(NoireRemoteJobState.Done);
    }

    [Fact]
    public async Task AnUnknownJobIdAnswersUnknownRatherThanFailing()
    {
        NoireRemote.Start();
        using var publication = NoireRemote.PublishType(typeof(SocketWorkProbe));

        using var client = new NoireRemoteClient("SocketWork", NoireRemoteTransport.Websocket);
        await client.InvokeAsync<bool>("Ready").WaitAsync(TimeSpan.FromSeconds(10));

        var job = await client.AttachAsync("nope").WaitAsync(TimeSpan.FromSeconds(10));

        job.State.Should().Be(NoireRemoteJobState.Unknown,
            "the listener says what it knows, and it does not know this one");
    }
}
