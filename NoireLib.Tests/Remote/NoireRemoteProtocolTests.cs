using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Remote;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Several calls in one request, a retried call that runs once, and the two documents a manifest is transformed into.
/// </summary>
public sealed class NoireRemoteProtocolTests : IDisposable
{
    private static int slowCalls;

    private readonly NoireRemoteServer server;
    private readonly HttpClient client;

    public NoireRemoteProtocolTests()
    {
        Interlocked.Exchange(ref slowCalls, 0);

        server = new NoireRemoteServer(new NoireRemoteStandaloneHost { Name = "protocol-probe", Version = "1.2.3" }, new NoireRemoteOptions
        {
            AutoStart = false,
            PublishDirectoryRecord = false,
            EnableLogging = false,
        });

        server.PublishType(typeof(ProtocolProbe));
        server.PublishType(typeof(LocalProbe));
        server.Start();

        client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
    }

    public void Dispose()
    {
        client.Dispose();
        server.Dispose();
    }

    [NoireRemoteClass("Protocol", Access = NoireRemoteAccess.Remote)]
    public static class ProtocolProbe
    {
        [NoireRemote]
        public static int Add(int left, int right) => left + right;

        [NoireRemote]
        public static int Throws() => throw new InvalidOperationException("the member said no");

        [NoireRemote]
        public static async Task<int> Slow(int milliseconds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref slowCalls);
            await Task.Delay(milliseconds, cancellationToken);

            return Volatile.Read(ref slowCalls);
        }
    }

    [NoireRemoteClass("LocalOnly", Access = NoireRemoteAccess.Local)]
    public static class LocalProbe
    {
        [NoireRemote]
        public static int Ping() => 1;
    }

    private string Url(string route) => "http://127.0.0.1:" + server.Port + route;

    private Task<HttpResponseMessage> PostAsync(string route, object body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, Url(route))
        {
            Content = new StringContent(NoireRemoteJson.Write(body), Encoding.UTF8, "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        return client.SendAsync(message, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheOpenApiDocument_IsServedAndCarriesEveryMember()
    {
        using var response = await client.GetAsync(
            Url(NoireRemotePaths.Prefix + NoireRemotePaths.OpenApi),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var document = JObject.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        document["openapi"]!.ToObject<string>().Should().Be("3.1.0");
        document["info"]!["version"]!.ToObject<string>().Should().Be("1.2.3");
        document["paths"]![NoireRemotePaths.Member("Protocol", "Add")].Should().NotBeNull();
        document["paths"]![NoireRemotePaths.Prefix + NoireRemotePaths.Ping].Should().NotBeNull();
        document["components"]!["securitySchemes"]!["bearer"].Should().NotBeNull();
        document["components"]!["securitySchemes"]!["signed"]!["description"]!.ToObject<string>().Should().Contain("HMAC");
    }

    [Fact]
    public void TheOpenApiDocument_IsProducedOfflineFromASavedManifest()
    {
        var saved = NoireRemoteJson.Read<NoireRemoteManifest>(NoireRemoteJson.Write(server.Manifest()))!;
        var document = NoireRemoteOpenApi.FromManifest(saved, "http://127.0.0.1:1234");

        document["servers"]![0]!["url"]!.ToObject<string>().Should().Be("http://127.0.0.1:1234");

        var operation = document["paths"]![NoireRemotePaths.Member("Protocol", "Add")]!["post"]!;

        operation["operationId"]!.ToObject<string>().Should().Be("Protocol_Add");
        operation["tags"]!.Values<string>().Should().ContainSingle().Which.Should().Be("Protocol");

        var args = operation["requestBody"]!["content"]!["application/json"]!["schema"]!["properties"]!["args"]!;

        args["properties"]!["left"]!["type"]!.ToObject<string>().Should().Be("integer");
        args["required"]!.Values<string>().Should().BeEquivalentTo("left", "right");

        var responses = operation["responses"]!;

        responses["200"].Should().NotBeNull();
        responses["500"]!["content"]!["application/json"]!["schema"]!["properties"]!["error"]!["properties"]!["code"]!["enum"]!
            .Values<string>().Should().Contain(NoireRemoteErrorCodes.HandlerFault);
    }

    [Fact]
    public void TheOpenApiDocument_DropsNotReadyForAMemberWithNoGate()
    {
        var saved = server.Manifest();
        var document = NoireRemoteOpenApi.FromManifest(saved);

        var codes = document["paths"]![NoireRemotePaths.Member("Protocol", "Add")]!["post"]!["responses"]!["503"]!
            ["content"]!["application/json"]!["schema"]!["properties"]!["error"]!["properties"]!["code"]!["enum"]!
            .Values<string>().ToArray();

        codes.Should().Contain(NoireRemoteErrorCodes.Busy);
        codes.Should().NotContain(NoireRemoteErrorCodes.NotReady, "a member with no gate cannot answer it");
    }

    [Fact]
    public void TheOpenApiDocument_MovesDefinitionsUnderComponents()
    {
        var document = NoireRemoteOpenApi.FromManifest(server.Manifest());
        var schemas = (JObject)document["components"]!["schemas"]!;

        schemas.Should().ContainKey("NoireRemoteError");
        JsonPointersIn(document).Should().NotContain(pointer => pointer.StartsWith("#/$defs/", StringComparison.Ordinal));
    }

    private static System.Collections.Generic.List<string> JsonPointersIn(JToken token)
    {
        var pointers = new System.Collections.Generic.List<string>();

        void Walk(JToken current)
        {
            if (current is JObject o)
            {
                if (o["$ref"] is JValue { Type: JTokenType.String } reference)
                    pointers.Add(reference.ToObject<string>()!);

                foreach (var property in o.Properties())
                    Walk(property.Value);
            }
            else if (current is JArray array)
            {
                foreach (var item in array)
                    Walk(item);
            }
        }

        Walk(token);

        return pointers;
    }
}
