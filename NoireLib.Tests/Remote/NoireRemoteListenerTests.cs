using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Drives a real listener over a real socket, with no game behind it: the envelope, the argument rules, the error
/// table, the meta routes and the two refusals a browser would run into.
/// </summary>
[Collection("NoireRemote")]
public sealed class NoireRemoteListenerTests : NoireRemoteTestBase
{
    public enum Facing
    {
        North,
        South,
    }

    public sealed record PickReport(bool Hit, int Polygon, float Height);

    [NoireRemoteClass("Listener", Thread = NoireRemoteThread.Background, Requires = NoireRemoteReadiness.None)]
    public static class ListenerProbe
    {
        public static readonly List<string> Placed = [];

        [NoireRemote]
        public static Vector3 GetPosition() => new(12.5f, 0f, -3.25f);

        [NoireRemote]
        public static int Add(int left, int right = 10) => left + right;

        [NoireRemote]
        public static PickReport RunPick(Vector3 origin, float radius = 2f)
            => new(true, (int)(origin.X + radius), origin.Z);

        [NoireRemote]
        public static void Place(string label)
        {
            lock (Placed)
                Placed.Add(label);
        }

        [NoireRemote]
        public static Facing Flip(Facing facing) => facing == Facing.North ? Facing.South : Facing.North;

        [NoireRemote]
        public static Task<int> AddAsync(int left, int right) => Task.FromResult(left + right);

        [NoireRemote]
        public static async Task<string> DelayedAsync(int milliseconds, CancellationToken cancellationToken)
        {
            await Task.Delay(milliseconds, cancellationToken);
            return "done";
        }

        [NoireRemote]
        public static int Throws() => throw new InvalidOperationException("the member said no");

        [NoireRemote]
        public static Dictionary<string, int> Counts() => new() { ["one"] = 1, ["two"] = 2 };

        [NoireRemote]
        public static IReadOnlyList<Vector3> Echo(IReadOnlyList<Vector3> points) => points;
    }

    [NoireRemoteClass("Reachable", Access = NoireRemoteAccess.Remote, Thread = NoireRemoteThread.Background)]
    public static class ReachableProbe
    {
        [NoireRemote]
        public static int Ping() => 1;
    }

    [NoireRemoteClass("Gated", Thread = NoireRemoteThread.Background)]
    public static class GatedProbe
    {
        [NoireRemote(Requires = NoireRemoteReadiness.StateReady)]
        public static int NeedsState() => 1;

        [NoireRemote(Requires = NoireRemoteReadiness.PlayerLoaded)]
        public static int NeedsPlayer() => 1;
    }

    private HttpClient client = null!;
    private string baseUrl = string.Empty;

    private void StartListener()
    {
        NoireRemote.Options.PublishDirectoryRecord = true;
        NoireRemote.PublishType(typeof(ListenerProbe));
        NoireRemote.PublishType(typeof(ReachableProbe));
        NoireRemote.PublishType(typeof(GatedProbe));
        NoireRemote.Start();

        baseUrl = "http://127.0.0.1:" + NoireRemote.Port;

        client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NoireRemote.Token);

        NoireRemoteClient.Options.RegistryDirectory = RegistryDirectory;
        NoireRemoteClient.Refresh();
    }

    public override void Dispose()
    {
        client?.Dispose();
        NoireRemoteClient.Options.RegistryDirectory = NoireRemoteDirectory.DefaultDirectory();
        NoireRemoteClient.Refresh();
        base.Dispose();
    }

    private async Task<(HttpStatusCode Status, NoireRemoteEnvelope Envelope)> PostAsync(string route, object? body = null)
    {
        var content = new StringContent(NoireRemoteJson.Write(body ?? new NoireRemoteRequest()), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + route, content);
        var text = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, NoireRemoteJson.Read<NoireRemoteEnvelope>(text)!);
    }

    [Fact]
    public async Task ACall_AnswersTheEnvelopeTheContractDescribes()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/GetPosition");

        status.Should().Be(HttpStatusCode.OK);
        envelope.Ok.Should().BeTrue();
        envelope.Protocol.Should().Be(1);
        envelope.Instance.Should().Be(NoireRemote.InstanceId);
        envelope.Id.Should().NotBeNullOrEmpty();
        envelope.ElapsedMs.Should().NotBeNull();
        envelope.ResultAs<Vector3>().Should().Be(new Vector3(12.5f, 0f, -3.25f));
    }

    [Fact]
    public async Task EveryResponse_CarriesTheProtocolAndInstanceHeadersAndNoCorsHeader()
    {
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Ping);

        response.Headers.GetValues("X-Noire-Protocol").Should().ContainSingle().Which.Should().Be("1");
        response.Headers.GetValues("X-Noire-Instance").Should().ContainSingle();
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task NamedArguments_Bind()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JObject.Parse("{\"left\":2,\"right\":3}") });

        envelope.ResultAs<int>().Should().Be(5);
    }

    [Fact]
    public async Task NamedArguments_BindIgnoringCase()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JObject.Parse("{\"Left\":2,\"RIGHT\":3}") });

        envelope.ResultAs<int>().Should().Be(5);
    }

    [Fact]
    public async Task PositionalArguments_Bind()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JArray.Parse("[2,3]") });

        envelope.ResultAs<int>().Should().Be(5);
    }

    [Fact]
    public async Task AMissingArgumentWithADefault_TakesIt()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JObject.Parse("{\"left\":2}") });

        envelope.ResultAs<int>().Should().Be(12);
    }

    [Fact]
    public async Task AMissingArgumentWithNoDefault_IsRefusedByName()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JObject.Parse("{\"right\":2}") });

        status.Should().Be(HttpStatusCode.BadRequest);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.ArgumentMissing);
        envelope.Error.Detail.Should().Be("left");
    }

    [Fact]
    public async Task AnUnknownArgument_IsRefusedByName()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JObject.Parse("{\"left\":1,\"rihgt\":2}") });

        status.Should().Be(HttpStatusCode.BadRequest);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.ArgumentUnknown);
        envelope.Error.Detail.Should().Be("rihgt",
            "a misspelled argument that quietly takes its default makes the call do something else");
    }

    [Fact]
    public async Task AnArgumentOfTheWrongShape_IsRefusedWithWhatWasWanted()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/Add", new NoireRemoteRequest { Args = JObject.Parse("{\"left\":\"two\",\"right\":3}") });

        status.Should().Be(HttpStatusCode.BadRequest);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.ArgumentInvalid);
        envelope.Error.Detail.Should().Be("left");
        envelope.Error.Message.Should().Contain("integer");
    }

    [Fact]
    public async Task AnAbsentBodyAndAnEmptyObject_AreTheSameThing()
    {
        StartListener();

        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + "Listener/GetPosition", new StringContent(string.Empty));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (status, _) = await PostAsync("Listener/GetPosition", new NoireRemoteRequest());
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AVectorArgument_ReadsBothShapes()
    {
        StartListener();

        var asObject = await PostAsync("Listener/RunPick", new NoireRemoteRequest { Args = JObject.Parse("{\"origin\":{\"x\":4,\"y\":0,\"z\":1},\"radius\":1}") });
        asObject.Envelope.ResultAs<PickReport>().Should().Be(new PickReport(true, 5, 1f));

        var asArray = await PostAsync("Listener/RunPick", new NoireRemoteRequest { Args = JObject.Parse("{\"origin\":[4,0,1],\"radius\":1}") });
        asArray.Envelope.ResultAs<PickReport>().Should().Be(new PickReport(true, 5, 1f));
    }

    [Fact]
    public async Task AnEnumArgument_CrossesAsItsName()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Flip", new NoireRemoteRequest { Args = JObject.Parse("{\"facing\":\"North\"}") });

        envelope.Result!.ToString().Should().Contain("South");
    }

    [Fact]
    public async Task AVoidMember_AnswersWithNoResultKey()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/Place", new NoireRemoteRequest { Args = JObject.Parse("{\"label\":\"pick origin\"}") });

        status.Should().Be(HttpStatusCode.OK);
        envelope.Ok.Should().BeTrue();
        envelope.Result.Should().BeNull();

        lock (ListenerProbe.Placed)
            ListenerProbe.Placed.Should().Contain("pick origin");
    }

    [Fact]
    public async Task AnAsyncMember_IsAwaitedAndItsResultSerializes()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/AddAsync", new NoireRemoteRequest { Args = JObject.Parse("{\"left\":40,\"right\":2}") });

        envelope.ResultAs<int>().Should().Be(42);
    }

    [Fact]
    public async Task ADictionaryResult_CrossesAsAnObject()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Counts");

        envelope.ResultAs<Dictionary<string, int>>().Should().BeEquivalentTo(new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 });
    }

    [Fact]
    public async Task AListOfVectors_RoundTrips()
    {
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Echo", new NoireRemoteRequest { Args = JObject.Parse("{\"points\":[[1,2,3],{\"x\":4,\"y\":5,\"z\":6}]}") });

        envelope.ResultAs<List<Vector3>>().Should().Equal(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
    }

    [Fact]
    public async Task AMemberThatThrows_AnswersFiveHundredWithItsTypeAndMessage()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/Throws");

        status.Should().Be(HttpStatusCode.InternalServerError);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.HandlerFault);
        envelope.Error.Message.Should().Be("the member said no");
        envelope.Error.Detail.Should().Be(nameof(InvalidOperationException));
        envelope.Error.StackTrace.Should().BeNull("a trace carries file paths from this machine and is off by default");
    }

    [Fact]
    public async Task AMemberThatThrows_SendsItsTraceOnlyWhenTheListenerIsToldTo()
    {
        NoireRemote.Options.SendStackTraces = true;
        StartListener();

        var (_, envelope) = await PostAsync("Listener/Throws");

        envelope.Error!.StackTrace.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AMemberPastItsDeadline_AnswersTimeoutAndSaysItMayStillBeRunning()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/DelayedAsync", new NoireRemoteRequest
        {
            Args = JObject.Parse("{\"milliseconds\":5000}"),
            TimeoutMs = 150,
        });

        status.Should().Be(HttpStatusCode.RequestTimeout);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.Timeout);
        envelope.Error.Message.Should().Contain("job");
    }

    [Theory]
    [InlineData("Gated/NeedsState")]
    [InlineData("Gated/NeedsPlayer")]
    public async Task AGatedMemberWithNoCharacter_AnswersNotReadyWithSomethingToWaitOn(string route)
    {
        StartListener();

        var (status, envelope) = await PostAsync(route);

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.NotReady);
        envelope.Error.RetryAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AGatedMember_IsNeverAskedAboutTheCharacterFromTheSocketThread()
    {
        StartListener();

        // Dalamud's object table throws off the framework thread. A gate evaluated on the request thread would drop the connection.
        using var response = await client.PostAsync(baseUrl + NoireRemotePaths.Prefix + "Gated/NeedsPlayer",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Contain(NoireRemoteErrorCodes.NotReady);
    }

    [Fact]
    public async Task AnUnknownEndpoint_AnswersFourOhFourWithNearNames()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listner/GetPosition");

        status.Should().Be(HttpStatusCode.NotFound);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.UnknownEndpoint);
    }

    [Fact]
    public async Task AnUnknownMember_AnswersFourOhFourWithNearNames()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/GetPositon");

        status.Should().Be(HttpStatusCode.NotFound);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.UnknownMember);
        envelope.Error.Candidates.Should().Contain("GetPosition");
    }

    [Fact]
    public async Task AGetOnAMemberRoute_AnswersMethodNotAllowed()
    {
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Prefix + "Listener/GetPosition");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task AProtocolVersionThisListenerDoesNotServe_IsRefused()
    {
        StartListener();

        var (status, envelope) = await PostAsync("Listener/GetPosition", new NoireRemoteRequest { Protocol = 99 });

        status.Should().Be(HttpStatusCode.BadRequest);
        envelope.Error!.Code.Should().Be(NoireRemoteErrorCodes.ProtocolMismatch);
    }

    [Fact]
    public async Task APinnedInstanceThatIsNotThisOne_AnswersConflict()
    {
        StartListener();

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + NoireRemotePaths.Prefix + "Listener/GetPosition")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NoireRemote.Token);
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Instance, Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ARequestCarryingAnOriginHeader_IsRefused()
    {
        StartListener();

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + NoireRemotePaths.Prefix + "Listener/GetPosition")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NoireRemote.Token);
        request.Headers.TryAddWithoutValidation("Origin", "https://example.com");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnOptionsRequest_IsRefusedAndCarriesNoCorsHeader()
    {
        StartListener();

        using var request = new HttpRequestMessage(HttpMethod.Options, baseUrl + NoireRemotePaths.Prefix + "Listener/GetPosition");
        request.Headers.TryAddWithoutValidation("Origin", "https://example.com");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task ARequestWithNoCredential_IsRefused()
    {
        StartListener();

        using var bare = new HttpClient();
        using var response = await bare.PostAsync(baseUrl + NoireRemotePaths.Prefix + "Listener/GetPosition",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ThePingRoute_ReportsTheInstanceAndTheEndpoints()
    {
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Ping);
        var ping = NoireRemoteJson.Read<NoireRemotePing>(await response.Content.ReadAsStringAsync())!;

        ping.Ok.Should().BeTrue();
        ping.Instance.Should().Be(NoireRemote.InstanceId);
        ping.Endpoints.Should().Contain("Listener");
        ping.UptimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ThePingRoute_NeedsTheCredentialToo()
    {
        StartListener();

        using var bare = new HttpClient();
        using var response = await bare.GetAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Ping);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the port never confirms to anything that reaches it that a game plugin is behind it");
    }

    [Fact]
    public async Task TheManifest_DescribesTheSurfaceFieldForField()
    {
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Manifest);
        var manifest = NoireRemoteJson.Read<NoireRemoteManifest>(await response.Content.ReadAsStringAsync())!;

        manifest.Protocol.Should().Be(1);
        manifest.Instance.Should().Be(NoireRemote.InstanceId);

        var endpoint = manifest.GetEndpoint("Listener").Should().NotBeNull().And.Subject as NoireRemoteManifestEndpoint;
        endpoint!.Access.Should().Be("local");

        var pick = endpoint.GetMember("RunPick").Should().NotBeNull().And.Subject as NoireRemoteManifestMember;
        pick!.Route.Should().Be("Listener/RunPick");
        pick.Thread.Should().Be("background");
        pick.Mode.Should().Be("sync");
        pick.Parameters.Should().HaveCount(2);
        pick.Parameters[0].Name.Should().Be("origin");
        pick.Parameters[0].JsonType.Should().Be("vector3");
        pick.Parameters[0].Required.Should().BeTrue();
        pick.Parameters[1].Name.Should().Be("radius");
        pick.Parameters[1].JsonType.Should().Be("number");
        pick.Parameters[1].Required.Should().BeFalse();
        pick.Returns!.JsonType.Should().StartWith("@");

        manifest.Types.Should().NotBeNull();
        manifest.Types!.Should().ContainKey(nameof(PickReport));
    }

    [Fact]
    public async Task TheManifest_CanBeTurnedOff()
    {
        NoireRemote.Options.EnableManifest = false;
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Manifest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AVoidMemberInTheManifest_ReportsNoReturn()
    {
        StartListener();

        using var response = await client.GetAsync(baseUrl + NoireRemotePaths.Prefix + NoireRemotePaths.Manifest);
        var manifest = NoireRemoteJson.Read<NoireRemoteManifest>(await response.Content.ReadAsStringAsync())!;

        manifest.GetEndpoint("Listener")!.GetMember("Place")!.Returns.Should().BeNull();
    }

    [Fact]
    public async Task CallCompleted_ReportsEveryFinishedCall()
    {
        StartListener();

        var reports = new List<NoireRemoteCallReport>();

        void Handler(NoireRemoteCallReport report)
        {
            lock (reports)
                reports.Add(report);
        }

        NoireRemote.CallCompleted += Handler;

        try
        {
            await PostAsync("Listener/GetPosition");
            await PostAsync("Listener/Throws");
        }
        finally
        {
            NoireRemote.CallCompleted -= Handler;
        }

        lock (reports)
        {
            reports.Should().HaveCount(2);
            reports[0].Ok.Should().BeTrue();
            reports[0].Member.Should().Be("GetPosition");
            reports[0].IsLoopback.Should().BeTrue();
            reports[1].Ok.Should().BeFalse();
            reports[1].ErrorCode.Should().Be(NoireRemoteErrorCodes.HandlerFault);
        }
    }

    [Fact]
    public async Task TheCallCount_CountsCalls()
    {
        StartListener();

        await PostAsync("Listener/GetPosition");
        await PostAsync("Listener/GetPosition");

        var member = NoireRemote.GetEndpoint("Listener")!.Members;
        var position = member.Should().ContainSingle(item => item.Name == "GetPosition").Subject;

        position.CallCount.Should().Be(2);
        position.LastCalledUtc.Should().NotBeNull();
    }
}
