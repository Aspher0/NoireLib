using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// One test per layer of the gate, in the order it runs. The layer that matters most is the first: a page in the
/// player's browser can reach a loopback port, and the browser headers are the only check that does not depend on the
/// browser honouring anything.
/// </summary>
public sealed class NoireRemoteSecurityGateTests
{
    private const int Port = 47821;
    private const string Token = "the-listener-token";

    private static NoireRemoteOptions Options() => new();

    private static async Task<HttpRequestData> ParseAsync(string raw, NoireRemoteOptions options)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));
        var result = await HttpRequestParser.ReadHeadersAsync(stream, options, CancellationToken.None);

        result.Failure.Should().BeNull();

        if (result.Request!.DeclaredBodyLength > 0)
            await HttpRequestParser.ReadBodyAsync(stream, result, CancellationToken.None);

        return result.Request!;
    }

    private static string Request(string extraHeaders = "", string authorization = "Bearer " + Token, string method = "GET", string path = "/noire/v1/_ping")
    {
        var builder = new StringBuilder();
        builder.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
        builder.Append("Host: 127.0.0.1:").Append(Port).Append("\r\n");

        if (authorization.Length > 0)
            builder.Append("Authorization: ").Append(authorization).Append("\r\n");

        builder.Append(extraHeaders);
        builder.Append("\r\n");

        return builder.ToString();
    }

    private static async Task<HttpFailure?> CheckAsync(string raw, NoireRemoteOptions? options = null, bool isLoopback = true, string remote = "127.0.0.1")
    {
        options ??= Options();
        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var request = await ParseAsync(raw, options);

        return gate.CheckHeaders(request, options, Token, Port, isLoopback, remote, out _);
    }

    [Fact]
    public async Task AWellFormedLoopbackRequest_Passes()
    {
        (await CheckAsync(Request())).Should().BeNull();
    }

    [Theory]
    [InlineData("Origin: https://example.com\r\n")]
    [InlineData("Referer: https://example.com/page\r\n")]
    [InlineData("Sec-Fetch-Site: cross-site\r\n")]
    [InlineData("Sec-Fetch-Mode: cors\r\n")]
    [InlineData("Sec-Fetch-Dest: empty\r\n")]
    public async Task ARequestCarryingABrowserHeader_IsRefused(string header)
    {
        var failure = await CheckAsync(Request(header));

        failure.Should().NotBeNull();
        failure!.Status.Should().Be(403);
        failure.Code.Should().Be(NoireRemoteErrorCodes.Forbidden);
    }

    [Fact]
    public async Task AHostNamingAnotherName_IsRefused()
    {
        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: rebound.example.com:" + Port + "\r\nAuthorization: Bearer " + Token + "\r\n\r\n";
        var failure = await CheckAsync(raw);

        failure!.Status.Should().Be(403);
        failure.Message.Should().Contain("host header");
    }

    [Fact]
    public async Task AHostNamingTheRightNameAndTheWrongPort_IsRefused()
    {
        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: 127.0.0.1:1\r\nAuthorization: Bearer " + Token + "\r\n\r\n";

        (await CheckAsync(raw))!.Status.Should().Be(403);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task TheLoopbackNames_AreAccepted(string host)
    {
        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: " + host + ":" + Port + "\r\nAuthorization: Bearer " + Token + "\r\n\r\n";

        (await CheckAsync(raw)).Should().BeNull();
    }

    [Fact]
    public async Task AConfiguredAllowedHost_IsAccepted()
    {
        var options = Options();
        options.AllowedHosts.Add("game-machine");

        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: game-machine:" + Port + "\r\nAuthorization: Bearer " + Token + "\r\n\r\n";

        (await CheckAsync(raw, options)).Should().BeNull();
    }

    [Fact]
    public async Task ARequestWithNoAuthorizationHeader_IsRefused()
    {
        var failure = await CheckAsync(Request(authorization: string.Empty));

        failure!.Status.Should().Be(401);
        failure.Code.Should().Be(NoireRemoteErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task AWrongCredential_IsRefused()
    {
        (await CheckAsync(Request(authorization: "Bearer not-the-token")))!.Status.Should().Be(401);
    }

    [Fact]
    public async Task AnUnknownAuthorizationScheme_IsRefused()
    {
        (await CheckAsync(Request(authorization: "Basic dXNlcjpwYXNz")))!.Status.Should().Be(401);
    }

    [Fact]
    public async Task TheLoopbackCredential_IsRefusedFromAnotherMachine()
    {
        var options = Options();
        options.AllowedHosts.Add("127.0.0.1");

        var failure = await CheckAsync(Request(), options, isLoopback: false, remote: "192.168.1.50");

        failure!.Status.Should().Be(401);
        failure.Message.Should().Contain("loopback");
    }

    [Fact]
    public async Task APostWithABodyAndNoJsonContentType_IsRefused()
    {
        var raw = "POST /noire/v1/MyApi/Ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port
            + "\r\nAuthorization: Bearer " + Token
            + "\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\n{}";

        var failure = await CheckAsync(raw);

        failure!.Status.Should().Be(415);
        failure.Code.Should().Be(NoireRemoteErrorCodes.BadContentType);
    }

    [Fact]
    public async Task APostWithNoBody_NeedsNoContentType()
    {
        var raw = "POST /noire/v1/MyApi/Ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port
            + "\r\nAuthorization: Bearer " + Token + "\r\n\r\n";

        (await CheckAsync(raw)).Should().BeNull();
    }

    [Fact]
    public async Task TenRejectedCredentialsFromOneAddress_BlockTheEleventh()
    {
        var options = Options();
        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var wrong = await ParseAsync(Request(authorization: "Bearer wrong"), options);
        var right = await ParseAsync(Request(), options);

        for (var attempt = 0; attempt < options.FailedAuthLockoutThreshold; attempt++)
            gate.CheckHeaders(wrong, options, Token, Port, true, "10.0.0.7", out _).Should().NotBeNull();

        var failure = gate.CheckHeaders(right, options, Token, Port, true, "10.0.0.7", out _);

        failure.Should().NotBeNull("the address is blocked; even the right credential is refused");
        failure!.Status.Should().Be(403);
    }

    [Fact]
    public async Task ABlockedAddress_DoesNotBlockAnother()
    {
        var options = Options();
        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var wrong = await ParseAsync(Request(authorization: "Bearer wrong"), options);
        var right = await ParseAsync(Request(), options);

        for (var attempt = 0; attempt < options.FailedAuthLockoutThreshold + 1; attempt++)
            gate.CheckHeaders(wrong, options, Token, Port, true, "10.0.0.8", out _);

        gate.CheckHeaders(right, options, Token, Port, true, "10.0.0.9", out _).Should().BeNull();
    }

    [Fact]
    public async Task ASignedRequest_PassesTheHeaderPhaseAndIsVerifiedAgainstTheBody()
    {
        var options = Options();
        options.RemoteSecret = "shared-secret";
        options.AllowedHosts.Add("127.0.0.1");

        var body = "{\"args\":{}}";
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = NoireRemoteSignature.CreateHeader("shared-secret", "POST", "/noire/v1/MyApi/Ping", bytes);

        var raw = "POST /noire/v1/MyApi/Ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port
            + "\r\nAuthorization: " + header
            + "\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\n\r\n" + body;

        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var request = await ParseAsync(raw, options);

        gate.CheckHeaders(request, options, Token, Port, false, "192.168.1.50", out var signed).Should().BeNull();
        signed.Should().BeTrue();
        gate.CheckBody(request, options, "192.168.1.50", signed).Should().BeNull();
    }

    [Fact]
    public async Task ASignedRequestWhoseBodyWasTampered_IsRefused()
    {
        var options = Options();
        options.RemoteSecret = "shared-secret";

        var signedBytes = Encoding.UTF8.GetBytes("{\"args\":{\"radius\":1}}");
        var header = NoireRemoteSignature.CreateHeader("shared-secret", "POST", "/noire/v1/MyApi/Ping", signedBytes);

        var tampered = "{\"args\":{\"radius\":9}}";
        var raw = "POST /noire/v1/MyApi/Ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port
            + "\r\nAuthorization: " + header
            + "\r\nContent-Type: application/json\r\nContent-Length: " + tampered.Length + "\r\n\r\n" + tampered;

        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var request = await ParseAsync(raw, options);

        gate.CheckHeaders(request, options, Token, Port, false, "192.168.1.50", out var signed).Should().BeNull();
        gate.CheckBody(request, options, "192.168.1.50", signed)!.Status.Should().Be(401);
    }

    [Fact]
    public async Task ASignedRequestWithAStaleTimestamp_IsRefused()
    {
        var options = Options();
        options.RemoteSecret = "shared-secret";

        var stale = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var header = NoireRemoteSignature.CreateHeader("shared-secret", "GET", "/noire/v1/_ping", null, stale, NoireRemoteSignature.CreateNonce());

        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port + "\r\nAuthorization: " + header + "\r\n\r\n";

        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var request = await ParseAsync(raw, options);

        gate.CheckHeaders(request, options, Token, Port, false, "192.168.1.50", out var signed).Should().BeNull();
        gate.CheckBody(request, options, "192.168.1.50", signed)!.Message.Should().Contain("window");
    }

    [Fact]
    public async Task AReplayedNonce_IsRefused()
    {
        var options = Options();
        options.RemoteSecret = "shared-secret";

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = NoireRemoteSignature.CreateNonce();
        var header = NoireRemoteSignature.CreateHeader("shared-secret", "GET", "/noire/v1/_ping", null, timestamp, nonce);
        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port + "\r\nAuthorization: " + header + "\r\n\r\n";

        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var first = await ParseAsync(raw, options);
        var second = await ParseAsync(raw, options);

        gate.CheckHeaders(first, options, Token, Port, false, "192.168.1.50", out var signed);
        gate.CheckBody(first, options, "192.168.1.50", signed).Should().BeNull();

        gate.CheckHeaders(second, options, Token, Port, false, "192.168.1.50", out signed);
        gate.CheckBody(second, options, "192.168.1.50", signed)!.Message.Should().Contain("nonce");
    }

    [Fact]
    public async Task ASignedRequestWithNoSecretConfigured_IsRefused()
    {
        var options = Options();
        var header = NoireRemoteSignature.CreateHeader("shared-secret", "GET", "/noire/v1/_ping", null);
        var raw = "GET /noire/v1/_ping HTTP/1.1\r\nHost: 127.0.0.1:" + Port + "\r\nAuthorization: " + header + "\r\n\r\n";

        var gate = new HttpSecurityGate(new NoireRemoteStandaloneHost());
        var request = await ParseAsync(raw, options);

        gate.CheckHeaders(request, options, Token, Port, false, "192.168.1.50", out var signed).Should().BeNull();
        gate.CheckBody(request, options, "192.168.1.50", signed)!.Status.Should().Be(401);
    }
}
