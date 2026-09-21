using FluentAssertions;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Channel separation, filters, held streams and the built-in publishers.</summary>
// Sets NoireRemoteClient's registry folder. That state is process-wide and the collection runs one class at a time.
[Collection("NoireRemote")]
public sealed class NoireRemoteChannelTests : IDisposable
{
    private readonly NoireRemoteServer server;
    private readonly HttpClient client;

    public NoireRemoteChannelTests()
    {
        server = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
            EventBufferSize = 8,
            LogBufferSize = 8,
            ProgressBufferSize = 8,
            MetricsInterval = TimeSpan.FromMilliseconds(60),
        });

        server.PublishType(typeof(ChannelProbe));
        server.Start();

        client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
    }

    public void Dispose()
    {
        client.Dispose();
        server.Dispose();
    }

    public sealed record Sighting(string Name, int Level);

    [NoireRemoteClass("Channels")]
    public static class ChannelProbe
    {
        [NoireRemote]
        public static event Action<Sighting>? Spotted;

        [NoireRemote("custom.renamed", Channel = "audit")]
        public static event Action? Fired;

        [NoireRemote]
        public static void Spot(string name, int level) => Spotted?.Invoke(new Sighting(name, level));

        [NoireRemote]
        public static void Fire() => Fired?.Invoke();

        [NoireRemote]
        public static async Task<int> Count(int steps, IProgress<double> progress, CancellationToken cancellationToken)
        {
            for (var step = 0; step < steps; step++)
            {
                progress.Report((step + 1) / (double)steps);
                await Task.Delay(1, cancellationToken);
            }

            return steps;
        }
    }

    private string Url(string route) => "http://127.0.0.1:" + server.Port + route;

    private async Task<NoireRemoteEventBatch> PollAsync(NoireRemoteEventRequest body)
    {
        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Events),
            new StringContent(NoireRemoteJson.Write(body), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        return NoireRemoteJson.Read<NoireRemoteEventBatch>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<NoireRemoteEnvelope> CallAsync(string route, object? args = null)
    {
        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Prefix + route),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteRequest { Args = NoireRemoteApiArgs(args) }), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        return NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static Newtonsoft.Json.Linq.JToken? NoireRemoteApiArgs(object? args)
        => args == null ? null : NoireRemoteJson.ToToken(args);

    [Fact]
    public async Task AChattyChannel_DoesNotEvictFromAnother()
    {
        server.PublishEvent("plugin.one", new { value = 1 });

        for (var index = 0; index < 40; index++)
            server.PublishEvent(NoireRemoteChannels.Log, NoireRemotePaths.LogTopicPrefix + "info", new { message = index });

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0 });

        batch.Events.Should().ContainSingle().Which.Topic.Should().Be("plugin.one",
            "a log at a real volume shares no ring with a plugin's own events");
        batch.Missed.Should().BeFalse();
    }

    [Fact]
    public async Task TheShippedPollBody_StillReadsTheEventsChannelAlone()
    {
        server.PublishEvent("plugin.one", null);
        server.PublishEvent(NoireRemoteChannels.Log, "log.info", null);

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Since = 0 });

        batch.Events.Should().ContainSingle();
        batch.Cursor.Should().Be(1);
    }

    [Fact]
    public async Task APollNamingChannels_ReadsEachFromItsOwnCursor()
    {
        server.PublishEvent("plugin.one", null);
        server.PublishEvent(NoireRemoteChannels.Log, "log.info", null);
        server.PublishEvent(NoireRemoteChannels.Log, "log.error", null);

        var first = await PollAsync(new NoireRemoteEventRequest
        {
            WaitMs = 0,
            Channels = [NoireRemoteChannels.Events, NoireRemoteChannels.Log],
        });

        first.Events.Should().HaveCount(3);
        first.Cursors.Should().ContainKey(NoireRemoteChannels.Log).WhoseValue.Should().Be(2);

        server.PublishEvent(NoireRemoteChannels.Log, "log.warning", null);

        var second = await PollAsync(new NoireRemoteEventRequest
        {
            WaitMs = 0,
            Channels = [NoireRemoteChannels.Events, NoireRemoteChannels.Log],
            Cursors = first.Cursors,
        });

        second.Events.Should().ContainSingle().Which.Topic.Should().Be("log.warning");
    }

    [Fact]
    public async Task AFilter_DropsWhatItDoesNotMatch()
    {
        server.PublishEvent("plugin.one", new { level = 3 });
        server.PublishEvent("plugin.two", new { level = 9 });

        var batch = await PollAsync(new NoireRemoteEventRequest
        {
            WaitMs = 0,
            Filters = [new NoireRemoteEventFilter { Path = "level", Op = "gt", Value = 5 }],
        });

        batch.Events.Should().ContainSingle().Which.Topic.Should().Be("plugin.two");
    }

    [Fact]
    public async Task AnUnknownFilterOperator_IsRefused()
    {
        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Events),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteEventRequest
            {
                WaitMs = 0,
                Filters = [new NoireRemoteEventFilter { Path = "level", Op = "approximately" }],
            }), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);

        var envelope = NoireRemoteJson.Read<NoireRemoteEnvelope>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.FilterInvalid);
    }

    [Fact]
    public async Task TooManyFilterClauses_AreRefused()
    {
        var clauses = new List<NoireRemoteEventFilter>();

        for (var index = 0; index < server.Options.MaxFilterClauses + 1; index++)
            clauses.Add(new NoireRemoteEventFilter { Path = "level", Op = "eq", Value = index });

        using var response = await client.PostAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Events),
            new StringContent(NoireRemoteJson.Write(new NoireRemoteEventRequest { WaitMs = 0, Filters = clauses }), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Collapse_KeepsTheNewestPerTopic()
    {
        server.PublishEvent("plugin.value", new { value = 1 });
        server.PublishEvent("plugin.value", new { value = 2 });
        server.PublishEvent("plugin.other", new { value = 3 });

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Collapse = true });

        batch.Events.Should().HaveCount(2);
        batch.Events.Single(item => item.Topic == "plugin.value").Data!["value"]!.ToObject<int>().Should().Be(2);
    }

    [Fact]
    public async Task ADroppedEvent_IsReportedAsMissedNamingItsChannel()
    {
        for (var index = 0; index < 20; index++)
            server.PublishEvent("plugin.one", new { value = index });

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Since = 1 });

        batch.Missed.Should().BeTrue();
        batch.MissedChannels.Should().Contain(NoireRemoteChannels.Events);
    }

    [Fact]
    public async Task AHeldStream_WritesServerSentEventsAndResumesFromACursor()
    {
        server.PublishEvent("plugin.first", new { value = 1 });

        var lines = await ReadStreamAsync("?channels=events", 3, TimeSpan.FromSeconds(5), () => { });

        // event: <topic>, id: <seq>, data: <the event document>: the shape EventSource reads.
        lines.Should().Contain(line => line == "event: plugin.first");
        lines.Should().Contain(line => line.StartsWith("id: ", StringComparison.Ordinal));

        var data = lines.Single(line => line.StartsWith("data: ", StringComparison.Ordinal)).Substring(6);
        var first = NoireRemoteJson.Read<NoireRemoteEvent>(data)!;

        first.Topic.Should().Be("plugin.first");
        first.Channel.Should().Be(NoireRemoteChannels.Events);

        var resumed = await ReadStreamAsync(
            "?channels=events&cursors=events:" + first.Seq,
            3,
            TimeSpan.FromSeconds(5),
            () => server.PublishEvent("plugin.second", new { value = 2 }));

        resumed.Should().Contain(line => line == "event: plugin.second");
    }

    [Fact]
    public async Task TheStreamRoute_RefusesARequestCarryingABrowserHeader()
    {
        using var browser = new HttpClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
        browser.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "http://evil.example");

        using var response = await browser.GetAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Stream),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            "a streamed answer that skips a check by accident is the worst defect this can produce");
    }

    [Fact]
    public async Task TheStreamRoute_RefusesARequestWithNoCredential()
    {
        using var bare = new HttpClient();

        using var response = await bare.GetAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.Stream),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AMemberTakingAProgressReporter_PublishesWithNothingWrittenByItsAuthor()
    {
        var call = CallAsync("Channels/Count", new { steps = 4 });

        var seen = new List<NoireRemoteEvent>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        long cursor = 0;

        while (DateTime.UtcNow < deadline && seen.Count == 0)
        {
            var batch = await PollAsync(new NoireRemoteEventRequest
            {
                WaitMs = 200,
                Channels = [NoireRemoteChannels.Progress],
                Cursors = new Dictionary<string, long> { [NoireRemoteChannels.Progress] = cursor },
            });

            if (batch.Cursors != null && batch.Cursors.TryGetValue(NoireRemoteChannels.Progress, out var next))
                cursor = next;

            seen.AddRange(batch.Events);
        }

        (await call).Ok.Should().BeTrue();

        seen.Should().NotBeEmpty();
        seen[0].Topic.Should().StartWith(NoireRemotePaths.ProgressTopicPrefix);

        var report = seen[^1].DataAs<NoireRemoteProgressReport>()!;

        report.Route.Should().Be("Channels/Count");
        report.Value.Should().BeInRange(0, 1);
        report.Payload.Should().BeNull("a fraction is already in value and repeating it would make a chart read it twice");
    }

    [Fact]
    public async Task ADeclaredEvent_PublishesAndCarriesItsPayload()
    {
        await CallAsync("Channels/Spot", new { name = "gate", level = 2 });

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0 });
        var published = batch.Events.Single();

        published.Topic.Should().Be("channels.spotted");

        var sighting = published.DataAs<Sighting>()!;
        sighting.Name.Should().Be("gate");
        sighting.Level.Should().Be(2);
    }

    [Fact]
    public async Task ADeclaredEventOnItsOwnChannel_PublishesThere()
    {
        await CallAsync("Channels/Fire");

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Channels = ["audit"] });

        batch.Events.Should().ContainSingle().Which.Topic.Should().Be("custom.renamed");
    }

    [Fact]
    public void ADeclaredEvent_ReachesTheManifest()
    {
        var declared = server.Manifest().GetEndpoint("Channels")!.GetEvent("Spotted");

        declared.Should().NotBeNull();
        declared!.Topic.Should().Be("channels.spotted");
        declared.Channel.Should().Be(NoireRemoteChannels.Events);
        declared.Schema!["$ref"]!.ToObject<string>().Should().Be("#/$defs/Sighting");
    }

    [Fact]
    public void ADisposedPublication_LeavesTheTypeWithNoHandlerOfOurs()
    {
        using var lone = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
        });

        var publication = lone.PublishType(typeof(DetachProbe));

        publication.Events.Should().NotBeNull().And.HaveCount(1);
        DetachProbe.HandlerCount().Should().Be(1);

        publication.Dispose();

        DetachProbe.HandlerCount().Should().Be(0, "a static event otherwise survives a reload holding a handler of ours");
    }

    [NoireRemoteClass("Detach")]
    public static class DetachProbe
    {
        [NoireRemote]
        public static event Action? Raised;

        [NoireRemote]
        public static void Raise() => Raised?.Invoke();

        [NoireRemote]
        public static int HandlerCount() => Raised?.GetInvocationList().Length ?? 0;
    }

    private static NoireRemoteLogLine Line(string source, string message, string level = "warning")
        => new() { Source = source, Message = message, Level = level, AtUtc = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId };

    [Fact]
    public async Task ThePluginScope_CarriesThisPluginsLinesAndNoOthers()
    {
        server.Options.LogScope.Should().Be(NoireRemoteLogScope.Plugin);

        var mine = server.Host.Identity.Name;

        NoireRemoteLog.Publish(Line(mine, "the gate is shut"));
        NoireRemoteLog.Publish(Line("SomeOtherPlugin", "not ours"));

        var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Channels = [NoireRemoteChannels.Log] });
        var lines = batch.Events.Select(item => item.DataAs<NoireRemoteLogLine>()!).ToList();

        lines.Should().Contain(line => line.Message == "the gate is shut");
        lines.Should().NotContain(line => line.Message == "not ours");
        batch.Events.Single(item => item.DataAs<NoireRemoteLogLine>()!.Message == "the gate is shut").Topic.Should().Be("log.warning");
    }

    [Fact]
    public async Task TheEverythingScope_CarriesOtherPluginsLinesTagged()
    {
        server.Options.LogScope = NoireRemoteLogScope.Everything;

        try
        {
            NoireRemoteLog.Publish(Line("SomeOtherPlugin", "theirs", "information"));

            var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Channels = [NoireRemoteChannels.Log] });
            var line = batch.Events.Select(item => item.DataAs<NoireRemoteLogLine>()!).Single(item => item.Message == "theirs");

            line.Source.Should().Be("SomeOtherPlugin");
            line.ProcessId.Should().Be(Environment.ProcessId);
        }
        finally
        {
            server.Options.LogScope = NoireRemoteLogScope.Plugin;
        }
    }

    [Fact]
    public async Task TheNoneScope_CarriesNothingAndSaysSo()
    {
        server.Options.LogScope = NoireRemoteLogScope.None;

        try
        {
            NoireRemoteLog.Publish(Line(server.Host.Identity.Name, "nobody is listening"));

            var batch = await PollAsync(new NoireRemoteEventRequest { WaitMs = 0, Channels = [NoireRemoteChannels.Log] });

            batch.Events.Should().NotContain(item => item.DataAs<NoireRemoteLogLine>()!.Message == "nobody is listening");
            server.Manifest().Has(NoireRemoteFeatures.Log).Should().BeFalse();
        }
        finally
        {
            server.Options.LogScope = NoireRemoteLogScope.Plugin;
        }
    }

    [Fact]
    public async Task ARegisteredGauge_AppearsInASample()
    {
        using var gauge = NoireRemoteMetrics.Register("testGauge", static () => 42);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        NoireRemoteEvent? sample = null;
        long cursor = 0;

        // The first sample can predate the gauge's registration.
        while (DateTime.UtcNow < deadline && sample == null)
        {
            var batch = await PollAsync(new NoireRemoteEventRequest
            {
                WaitMs = 300,
                Channels = [NoireRemoteChannels.Metrics],
                Cursors = new Dictionary<string, long> { [NoireRemoteChannels.Metrics] = cursor },
            });

            if (batch.Cursors != null && batch.Cursors.TryGetValue(NoireRemoteChannels.Metrics, out var next))
                cursor = next;

            sample = batch.Events.FirstOrDefault(item => item.Data?["testGauge"] != null);
        }

        sample.Should().NotBeNull("nothing samples while nobody watches, and a poll is watching");
        sample!.Topic.Should().Be(NoireRemotePaths.MetricsTopic);
        sample.Data!["testGauge"]!.ToObject<double>().Should().Be(42);
        sample.Data["heapMb"].Should().NotBeNull();
        sample.Data["httpCallsPerMinute"].Should().NotBeNull();

        NoireRemoteMetrics.Names().Should().Contain("testGauge");
    }

    [Fact]
    public void DeclareChannel_BoundsAPluginsOwnEventsApart()
    {
        server.DeclareChannel("sightings", 3);

        for (var index = 0; index < 10; index++)
            server.PublishEvent("sightings", "sighting.seen", new { value = index });

        server.Manifest().Channels!.Should().Contain(channel => channel.Name == "sightings" && channel.BufferSize == 3);
    }

    [Fact]
    public async Task TheCallerHalf_ReadsTheStreamAsItIsWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NoireRemoteTests", Guid.NewGuid().ToString("N"));

        using var publisher = new NoireRemoteServer(new NoireRemoteStandaloneHost(), new NoireRemoteOptions
        {
            AutoStart = false,
            EnableLogging = false,
            PublishDirectoryRecord = true,
            PublishCharacterIdentity = false,
            RegistryDirectory = directory,
        });

        publisher.PublishType(typeof(ChannelProbe));
        publisher.Start();

        var previousDirectory = NoireRemoteClient.Options.RegistryDirectory;
        var previousExclusion = NoireRemoteClient.Options.ExcludeInstance;

        try
        {
            NoireRemoteClient.Options.RegistryDirectory = directory;
            NoireRemoteClient.Options.ExcludeInstance = null;
            NoireRemoteClient.Refresh();

            using var listening = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var seen = new List<NoireRemoteEvent>();

            var reading = Task.Run(async () =>
            {
                await foreach (var published in NoireRemoteClient.Endpoint("Channels")
                    .StreamAsync("events", null, listening.Token))
                {
                    seen.Add(published);

                    if (seen.Count == 2)
                        listening.Cancel();
                }
            }, TestContext.Current.CancellationToken);

            await Task.Delay(300, TestContext.Current.CancellationToken);

            publisher.PublishEvent("caller.one", new { value = 1 });
            publisher.PublishEvent("caller.two", new { value = 2 });

            try
            {
                await reading;
            }
            catch (OperationCanceledException)
            {
            }

            seen.Should().HaveCount(2);
            seen[0].Topic.Should().Be("caller.one");
            seen[1].Topic.Should().Be("caller.two");
            seen[1].Data!["value"]!.ToObject<int>().Should().Be(2);
        }
        finally
        {
            NoireRemoteClient.Options.RegistryDirectory = previousDirectory;
            NoireRemoteClient.Options.ExcludeInstance = previousExclusion;
            NoireRemoteClient.Refresh();

            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<List<string>> ReadStreamAsync(string query, int wanted, TimeSpan timeout, Action afterConnect)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync("127.0.0.1", server.Port, TestContext.Current.CancellationToken);

        var stream = socket.GetStream();
        var request = "GET " + NoireRemotePaths.Prefix + NoireRemotePaths.Stream + query + " HTTP/1.1\r\n"
            + "Host: 127.0.0.1:" + server.Port + "\r\n"
            + "Authorization: Bearer " + server.Token + "\r\n\r\n";

        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await stream.FlushAsync(TestContext.Current.CancellationToken);

        await Task.Delay(120, TestContext.Current.CancellationToken);
        afterConnect();

        var reader = new StreamReader(stream, Encoding.UTF8);
        var header = new StringBuilder();
        var lines = new List<string>();
        var deadline = DateTime.UtcNow + timeout;
        var inHeader = true;

        while (DateTime.UtcNow < deadline && lines.Count < wanted)
        {
            var line = await reader.ReadLineAsync(TestContext.Current.CancellationToken);

            if (line == null)
                break;

            if (inHeader)
            {
                if (line.Length == 0)
                {
                    inHeader = false;
                    continue;
                }

                header.AppendLine(line);
                continue;
            }

            if (line.Length > 0)
                lines.Add(line);
        }

        header.ToString().Should().Contain(NoireRemoteHeaders.EventStreamContentType);
        header.ToString().Should().NotContain("Content-Length", "a held answer is delimited by the close and not by a length");

        return lines;
    }
}
