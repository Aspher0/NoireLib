using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// A signed credential covers the bytes the request actually carries. The listener recomputes the code over the body
/// it read. A signed request whose body was left out of the signature is refused by the listener that issued the
/// secret, and nothing before the wire shows it.
/// </summary>
public sealed class NoireSocketCredentialTests
{
    private const string Secret = "shared-secret";

    [Fact]
    public void BuildHeaderValue_SignsTheBodyItIsGiven()
    {
        var body = Encoding.UTF8.GetBytes("{\"room\":\"chat\"}");

        var (timestamp, nonce, mac) = ReadSignature(NoireSocketCredential.Signed(Secret).BuildHeaderValue("POST", "/poll", body));

        mac.Should().Be(NoireRemoteSignature.ComputeMac(Secret, "POST", "/poll", body, timestamp, nonce));
        mac.Should().NotBe(NoireRemoteSignature.ComputeMac(Secret, "POST", "/poll", null, timestamp, nonce),
            "an empty body and this one hash differently; that hash is what the listener compares");
    }

    [Fact]
    public void BuildHeaderValue_OnABearerCredential_CarriesTheTokenWhateverTheBodyIs()
    {
        var bearer = NoireSocketCredential.Bearer("loopback-token");

        bearer.BuildHeaderValue("POST", "/poll", Encoding.UTF8.GetBytes("{}"))
            .Should().Be(NoireRemoteHeaders.BearerScheme + " loopback-token");
    }

    [Fact]
    public void ApplyHeaders_SignsTheBodyTheRequestCarries()
    {
        var body = Encoding.UTF8.GetBytes("{\"room\":\"chat\"}");
        var http = new NoireSocketHttpOptions { Credential = NoireSocketCredential.Signed(Secret) };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/poll")
        {
            Content = new ByteArrayContent(body),
        };

        SocketHttpFactory.ApplyHeaders(request, http, "/never-used", body);

        var (timestamp, nonce, mac) = ReadSignature(Header(request, NoireRemoteHeaders.Authorization));

        mac.Should().Be(NoireRemoteSignature.ComputeMac(Secret, "POST", "/poll", body, timestamp, nonce));
    }

    [Fact]
    public void ApplyHeaders_WithNoBody_SignsAnEmptyOne()
    {
        var http = new NoireSocketHttpOptions { Credential = NoireSocketCredential.Signed(Secret) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/poll");

        SocketHttpFactory.ApplyHeaders(request, http, "/poll");

        var (timestamp, nonce, mac) = ReadSignature(Header(request, NoireRemoteHeaders.Authorization));

        mac.Should().Be(NoireRemoteSignature.ComputeMac(Secret, "GET", "/poll", null, timestamp, nonce));
    }

    [Fact]
    public async Task ASignedPoll_PutsTheBytesItSignedOnTheWire()
    {
        using var listener = new RecordingListener();
        var accepted = listener.AcceptOneAsync(TestContext.Current.CancellationToken);

        var options = new NoireLongPollOptions
        {
            Method = NoireLongPollMethod.Post,
            Body = new { room = "chat", since = 7 },
            PollTimeout = TimeSpan.FromSeconds(15),
        };

        options.Http.UseSystemProxy = false;
        options.Http.Credential = NoireSocketCredential.Signed(Secret);

        using var client = new NoireLongPollClient(listener.Url, options);

        var answer = await client.PollOnceAsync(TestContext.Current.CancellationToken);
        var request = await accepted;

        answer.Status.Should().Be(200);
        request.RequestLine.Should().StartWith("POST /poll ");
        request.Body.Should().Equal(NoireRemoteJson.WriteBytes(options.Body), "the content is what the serializer produced");

        var (timestamp, nonce, mac) = ReadSignature(request.Headers[NoireRemoteHeaders.Authorization]);

        mac.Should().Be(NoireRemoteSignature.ComputeMac(Secret, "POST", "/poll", request.Body, timestamp, nonce),
            "the listener recomputes the code over the body it read");
        mac.Should().NotBe(NoireRemoteSignature.ComputeMac(Secret, "POST", "/poll", null, timestamp, nonce),
            "signing over an empty body would produce a credential the listener refuses");
    }

    private static (long Timestamp, string Nonce, string Mac) ReadSignature(string headerValue)
    {
        headerValue.Should().StartWith(NoireRemoteHeaders.SignedScheme + " ");
        NoireRemoteSignature.TryParseHeader(headerValue, out var timestamp, out var nonce, out var mac).Should().BeTrue();

        return (timestamp, nonce, mac);
    }

    private static string Header(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

    private sealed record RecordedRequest(string RequestLine, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    // The signature is checked against the body the far end actually got.
    private sealed class RecordingListener : IDisposable
    {
        private readonly TcpListener listener;

        public RecordingListener()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
        }

        public string Url => "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/poll";

        public async Task<RecordedRequest> AcceptOneAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var stream = client.GetStream();

            var head = new List<byte>();
            var one = new byte[1];

            while (!EndsTheHeaderBlock(head))
            {
                if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
                    break;

                head.Add(one[0]);
            }

            var lines = Encoding.ASCII.GetString([.. head]).Split("\r\n");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 1; index < lines.Length; index++)
            {
                var separator = lines[index].IndexOf(':');

                if (separator > 0)
                    headers[lines[index][..separator].Trim()] = lines[index][(separator + 1)..].Trim();
            }

            var body = Array.Empty<byte>();

            if (headers.TryGetValue("Content-Length", out var declared) && int.TryParse(declared, out var length) && length > 0)
            {
                body = new byte[length];

                var read = 0;

                while (read < length)
                {
                    var got = await stream.ReadAsync(body.AsMemory(read), cancellationToken).ConfigureAwait(false);

                    if (got == 0)
                        break;

                    read += got;
                }
            }

            await stream.WriteAsync(
                Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"),
                cancellationToken).ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            return new RecordedRequest(lines[0], headers, body);
        }

        public void Dispose()
            => listener.Stop();

        private static bool EndsTheHeaderBlock(List<byte> head)
            => head.Count >= 4 && head[^4] == (byte)'\r' && head[^3] == (byte)'\n' && head[^2] == (byte)'\r' && head[^1] == (byte)'\n';
    }
}
