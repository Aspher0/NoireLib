using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Covers jobs and the event long poll.</summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteJobAndEventTests : NoireRemoteTestBase
{
    [NoireRemoteClass("Work", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class WorkProbe
    {
        public static readonly ManualResetEventSlim Release = new(false);

        [NoireRemote(Mode = NoireRemoteCallMode.Job, TimeoutSeconds = 60)]
        public static async Task<int> Build(int steps, CancellationToken cancellationToken)
        {
            for (var step = 0; step < steps; step++)
            {
                NoireRemote.ReportProgress((step + 1) / (double)steps);
                await Task.Delay(10, cancellationToken);
            }

            return steps;
        }

        [NoireRemote(Mode = NoireRemoteCallMode.Job)]
        public static async Task<string> WaitForever(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return "never";
        }

        [NoireRemote(Mode = NoireRemoteCallMode.Job)]
        public static Task<int> Fails() => throw new InvalidOperationException("the job said no");

        [NoireRemote]
        public static int Quick() => 1;
    }

    private HttpClient client = null!;
    private string baseUrl = string.Empty;

    private void StartListener()
    {
        NoireRemote.PublishType(typeof(WorkProbe));
        NoireRemote.Start();

        baseUrl = "http://127.0.0.1:" + NoireRemote.Port;
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NoireRemote.Token);
    }

    public override void Dispose()
    {
        client?.Dispose();
        base.Dispose();
    }

    private async Task<(HttpStatusCode Status, NoireRemoteEnvelope Envelope)> PostAsync(string route, object? body = null)
    {
        var content = new StringContent(NoireRemoteJson.Write(body ?? new NoireRemoteRequest()), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + route, content);

        return (response.StatusCode, NoireRemoteJson.Read<NoireRemoteEnvelope>(await response.Content.ReadAsStringAsync())!);
    }

    private async Task<NoireRemoteEnvelope> PollAsync(string jobId)
    {
        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Job(jobId));
        return NoireRemoteJson.Read<NoireRemoteEnvelope>(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task AMemberDeclaredAsAJob_AnswersAcceptedWithAJobId()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Work/Build", new NoireRemoteRequest { Args = JObject.Parse("{\"steps\":3}") });

        status.Should().Be(HttpStatusCode.Accepted);
        envelope.Ok.Should().BeTrue();
        envelope.Job.Should().NotBeNull();
        envelope.Job!.Id.Should().NotBeNullOrEmpty();
        envelope.Job.State.Should().Be(NoireRemoteJobState.Running);
        envelope.Job.PollAfterMs.Should().BeGreaterThan(0);
        envelope.Job.Route.Should().Be("Work/Build");
    }

    [Fact]
    public async Task AJob_PollsToDoneAndCarriesItsResult()
    {
        StartListener();

        var started = await PostAsync("Work/Build", new NoireRemoteRequest { Args = JObject.Parse("{\"steps\":3}") });
        var id = started.Envelope.Job!.Id;

        NoireRemoteEnvelope polled;
        var deadline = DateTime.UtcNow.AddSeconds(20);

        do
        {
            await Task.Delay(25);
            polled = await PollAsync(id);
        }
        while (polled.Job!.State == NoireRemoteJobState.Running && DateTime.UtcNow < deadline);

        polled.Job!.State.Should().Be(NoireRemoteJobState.Done);
        polled.ResultAs<int>().Should().Be(3);
    }

    [Fact]
    public async Task ARunningJob_ReportsTheProgressItsMemberPublishes()
    {
        StartListener();

        var started = await PostAsync("Work/Build", new NoireRemoteRequest { Args = JObject.Parse("{\"steps\":40}") });
        var id = started.Envelope.Job!.Id;

        double? progress = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(30);
            var polled = await PollAsync(id);
            progress = polled.Job!.Progress;

            if (progress is > 0)
                break;
        }

        progress.Should().NotBeNull();
        progress!.Value.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task AJobThatThrows_PollsToFailedAndCarriesTheError()
    {
        StartListener();

        var started = await PostAsync("Work/Fails");
        var id = started.Envelope.Job!.Id;

        NoireRemoteEnvelope polled;
        var deadline = DateTime.UtcNow.AddSeconds(20);

        do
        {
            await Task.Delay(25);
            polled = await PollAsync(id);
        }
        while (polled.Job!.State == NoireRemoteJobState.Running && DateTime.UtcNow < deadline);

        polled.Job!.State.Should().Be(NoireRemoteJobState.Failed);
        polled.Error!.Code.Should().Be(NoireRemoteErrorCodes.HandlerFault);
        polled.Error.Message.Should().Be("the job said no");
    }

    [Fact]
    public async Task DeletingAJob_CancelsTheTokenItsMemberHolds()
    {
        StartListener();

        var started = await PostAsync("Work/WaitForever");
        var id = started.Envelope.Job!.Id;

        using var response = await client.DeleteAsync(baseUrl + NoireRemotePaths.Job(id));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        NoireRemoteEnvelope polled;
        var deadline = DateTime.UtcNow.AddSeconds(20);

        do
        {
            await Task.Delay(25);
            polled = await PollAsync(id);
        }
        while (polled.Job!.State == NoireRemoteJobState.Running && DateTime.UtcNow < deadline);

        polled.Job!.State.Should().Be(NoireRemoteJobState.Cancelled);
    }

    [Fact]
    public async Task AnUnknownJobId_AnswersJobNotFound()
    {
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Job("nosuchjob"));
        var envelope = NoireRemoteJson.Read<NoireRemoteEnvelope>(await response.Content.ReadAsStringAsync())!;

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.JobNotFound);
    }

    [Fact]
    public async Task AFinishedJob_IsDroppedOnceItsRetentionPasses()
    {
        NoireRemote.Options.JobRetention = TimeSpan.FromMilliseconds(1);
        StartListener();

        var started = await PostAsync("Work/Build", new NoireRemoteRequest { Args = JObject.Parse("{\"steps\":1}") });
        var id = started.Envelope.Job!.Id;

        var deadline = DateTime.UtcNow.AddSeconds(20);
        NoireRemoteEnvelope polled;

        do
        {
            await Task.Delay(50);
            polled = await PollAsync(id);
        }
        while (polled.Error == null && DateTime.UtcNow < deadline);

        polled.Error!.Code.Should().Be(NoireRemoteErrorCodes.JobNotFound);
    }

    [Fact]
    public async Task ASynchronousMemberCalledInJobMode_StillAnswersWithAJob()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Work/Quick", new NoireRemoteRequest { Mode = "job" });

        status.Should().Be(HttpStatusCode.Accepted);
        envelope.Job.Should().NotBeNull();
    }

    private async Task<NoireRemoteEventBatch> PollEventsAsync(NoireRemoteEventRequest request)
    {
        var content = new StringContent(NoireRemoteJson.Write(request), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Events, content);

        return NoireRemoteJson.Read<NoireRemoteEventBatch>(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task APollStartedBeforeAnEventArrives_ReturnsIt()
    {
        StartListener();

        var poll = PollEventsAsync(new NoireRemoteEventRequest { Since = 0, WaitMs = 10000 });

        await Task.Delay(100);
        NoireRemote.PublishEvent("nav.pick", new { polygon = 881 });

        var batch = await poll;

        batch.Events.Should().ContainSingle();
        batch.Events[0].Topic.Should().Be("nav.pick");
        batch.Events[0].Data!["polygon"]!.Value<int>().Should().Be(881);
        batch.Cursor.Should().Be(1);
        batch.Missed.Should().BeFalse();
    }

    [Fact]
    public async Task APollStartedAfterAnEvent_ReturnsItFromTheBuffer()
    {
        StartListener();

        NoireRemote.PublishEvent("nav.pick", new { polygon = 12 });

        var batch = await PollEventsAsync(new NoireRemoteEventRequest { Since = 0, WaitMs = 100 });

        batch.Events.Should().ContainSingle();
        batch.Events[0].Seq.Should().Be(1);
    }

    [Fact]
    public async Task ACursorHandedBack_SkipsWhatWasAlreadyRead()
    {
        StartListener();

        NoireRemote.PublishEvent("nav.pick", new { polygon = 1 });
        var first = await PollEventsAsync(new NoireRemoteEventRequest { Since = 0, WaitMs = 50 });

        NoireRemote.PublishEvent("nav.pick", new { polygon = 2 });
        var second = await PollEventsAsync(new NoireRemoteEventRequest { Since = first.Cursor, WaitMs = 50 });

        second.Events.Should().ContainSingle();
        second.Events[0].Data!["polygon"]!.Value<int>().Should().Be(2);
    }

    [Fact]
    public async Task ATopicFilter_MatchesByPrefix()
    {
        StartListener();

        NoireRemote.PublishEvent("nav.pick", new { value = 1 });
        NoireRemote.PublishEvent("chat.line", new { value = 2 });

        var batch = await PollEventsAsync(new NoireRemoteEventRequest { Since = 0, Topics = ["nav"], WaitMs = 50 });

        batch.Events.Should().ContainSingle().Which.Topic.Should().Be("nav.pick");
    }

    [Fact]
    public async Task ACursorBehindTheBuffer_ReportsWhatWasMissed()
    {
        NoireRemote.Options.EventBufferSize = 2;
        StartListener();

        for (var index = 0; index < 6; index++)
            NoireRemote.PublishEvent("nav", new { index });

        var batch = await PollEventsAsync(new NoireRemoteEventRequest { Since = 1, WaitMs = 50 });

        batch.Missed.Should().BeTrue();
    }

    [Fact]
    public async Task APollThatSeesNothing_ReturnsAnEmptyListRatherThanHanging()
    {
        StartListener();

        var batch = await PollEventsAsync(new NoireRemoteEventRequest { Since = 0, WaitMs = 120 });

        batch.Ok.Should().BeTrue();
        batch.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task MorePollsThanTheCap_AnswerBusy()
    {
        NoireRemote.Options.MaxEventStreams = 1;
        StartListener();

        var held = PollEventsAsync(new NoireRemoteEventRequest { Since = 0, WaitMs = 3000 });
        await Task.Delay(150);

        var content = new StringContent(NoireRemoteJson.Write(new NoireRemoteEventRequest { Since = 0, WaitMs = 500 }), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Events, content);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        NoireRemote.PublishEvent("release", null);
        await held;
    }
}
