using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Locks how socket clients turn HTTP settings into a handler and a request.</summary>
public sealed class NoireSocketHttpFactoryTests
{
    private static readonly Uri Unreachable = new("ws://127.0.0.1:1/");

    [Fact]
    public void CreateHandler_CarriesTheSettingsAWebsocketCannotHold()
    {
        var proxy = new WebProxy("http://127.0.0.1:8080");
        var cookies = new CookieContainer();
        var credentials = new NetworkCredential("user", "secret");
        var certificates = new X509CertificateCollection();
        RemoteCertificateValidationCallback check = (_, _, _, _) => true;

        var http = new NoireSocketHttpOptions
        {
            Proxy = proxy,
            Cookies = cookies,
            Credentials = credentials,
            ClientCertificates = certificates,
            RemoteCertificateValidationCallback = check,
        };

        using var handler = SocketHttpFactory.CreateHandler(http, null);

        handler.Proxy.Should().BeSameAs(proxy);
        handler.UseProxy.Should().BeTrue();
        handler.CookieContainer.Should().BeSameAs(cookies);
        handler.UseCookies.Should().BeTrue();
        handler.Credentials.Should().BeSameAs(credentials);
        handler.SslOptions.ClientCertificates.Should().BeSameAs(certificates);
        handler.SslOptions.RemoteCertificateValidationCallback.Should().BeSameAs(check);
    }

    [Fact]
    public void CreateHandler_AppliesThePooledConnectionLifetime()
    {
        var http = new NoireSocketHttpOptions
        {
            PooledConnectionLifetime = TimeSpan.FromSeconds(42),
            ConnectionTimeout = TimeSpan.FromSeconds(7),
        };

        using var handler = SocketHttpFactory.CreateHandler(http, null);

        handler.PooledConnectionLifetime.Should().Be(TimeSpan.FromSeconds(42));
        handler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void CreateHandler_UsesNoProxyWhenNoneIsSetAndTheSystemOneIsRefused()
    {
        using var handler = SocketHttpFactory.CreateHandler(new NoireSocketHttpOptions { UseSystemProxy = false }, null);

        handler.UseProxy.Should().BeFalse();
        handler.UseCookies.Should().BeFalse("a client that was given no container must not accumulate cookies");
    }

    [Fact]
    public void CreateHandler_RunsTheHookLast()
    {
        var http = new NoireSocketHttpOptions { ConnectionTimeout = TimeSpan.FromSeconds(7) };

        using var handler = SocketHttpFactory.CreateHandler(http, built => built.ConnectTimeout = TimeSpan.FromSeconds(3));

        handler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ApplyHeaders_PutsEveryConfiguredHeaderOnTheRequest()
    {
        var http = new NoireSocketHttpOptions();
        http.Headers["X-Noire-Test"] = "carried";

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/stream");

        SocketHttpFactory.ApplyHeaders(request, http, "/stream");

        Header(request, "X-Noire-Test").Should().Be("carried");
        Header(request, "User-Agent").Should().Be(NoireSocketHttpOptions.DefaultUserAgent,
            "a request carrying no User-Agent at all is refused by some web application firewalls");
    }

    [Fact]
    public void ApplyHeaders_TurnsABearerCredentialIntoAnAuthorizationHeader()
    {
        var http = new NoireSocketHttpOptions { Credential = NoireSocketCredential.Bearer("loopback-token") };

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/stream");

        SocketHttpFactory.ApplyHeaders(request, http, "/stream");

        Header(request, NoireRemoteHeaders.Authorization)
            .Should().Be(NoireRemoteHeaders.BearerScheme + " loopback-token");
    }

    [Fact]
    public void ApplyHeaders_SignsTheVerbAndThePathTheRequestActuallyCarries()
    {
        const string Secret = "shared-secret";
        var http = new NoireSocketHttpOptions { Credential = NoireSocketCredential.Signed(Secret) };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/poll?since=7");

        SocketHttpFactory.ApplyHeaders(request, http, "/never-used");

        var signature = Header(request, NoireRemoteHeaders.Authorization);
        signature.Should().StartWith(NoireRemoteHeaders.SignedScheme + " ");

        var (timestamp, nonce, mac) = ReadSignature(signature);

        mac.Should().Be(NoireRemoteSignature.ComputeMac(Secret, "POST", "/poll?since=7", null, timestamp, nonce));
        mac.Should().NotBe(NoireRemoteSignature.ComputeMac(Secret, "GET", "/poll?since=7", null, timestamp, nonce),
            "the verb the request carries is part of what the listener verifies");
    }

    [Fact]
    public void ApplyHeaders_SignsTheFallbackPathWhenTheAddressIsRelative()
    {
        const string Secret = "shared-secret";
        var http = new NoireSocketHttpOptions { Credential = NoireSocketCredential.Signed(Secret) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "poll?since=7");

        SocketHttpFactory.ApplyHeaders(request, http, "/poll?since=7");

        var (timestamp, nonce, mac) = ReadSignature(Header(request, NoireRemoteHeaders.Authorization));

        mac.Should().Be(NoireRemoteSignature.ComputeMac(Secret, "GET", "/poll?since=7", null, timestamp, nonce));
    }

    [Fact]
    public void ApplyHeaders_AddsNoAuthorizationWhenNoCredentialIsSet()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/stream");

        SocketHttpFactory.ApplyHeaders(request, new NoireSocketHttpOptions(), "/stream");

        request.Headers.Contains(NoireRemoteHeaders.Authorization).Should().BeFalse();
    }

    [Fact]
    public void ReadHeaders_MergesTheContentHeadersIntoTheResponseHeaders()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        };

        response.Headers.TryAddWithoutValidation("X-Cursor", "42");

        var headers = SocketHttpFactory.ReadHeaders(response);

        headers["x-cursor"].Should().Be("42", "a server naming a header in another case still names the same header");
        headers.Should().ContainKey("Content-Type");
    }

    [Fact]
    public async Task ApplyHeaders_LeavesASocketAcceptableToACustomInvoker()
    {
        var http = new NoireSocketHttpOptions
        {
            Proxy = new WebProxy("http://127.0.0.1:8080"),
            Cookies = new CookieContainer(),
            Credentials = new NetworkCredential("user", "secret"),
            ClientCertificates = new X509CertificateCollection(),
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
            Credential = NoireSocketCredential.Bearer("loopback-token"),
        };

        http.Headers["X-Noire-Test"] = "carried";

        using var handler = SocketHttpFactory.CreateHandler(http, null);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var socket = new ClientWebSocket();

        SocketHttpFactory.ApplyHeaders(socket.Options, http, "/socket");

        var failure = await Connect(socket, invoker).ConfigureAwait(false);

        failure.Should().NotBeOfType<ArgumentException>(
            "every setting a custom invoker refuses belongs on the handler; the socket carries only headers");
    }

    [Fact]
    public async Task ClientWebSocketOptions_RefusesTheHandlerSettingsWhenAnInvokerIsSupplied()
    {
        var setters = new Dictionary<string, Action<ClientWebSocketOptions>>
        {
            ["Proxy"] = options => options.Proxy = new WebProxy("http://127.0.0.1:8080"),
            ["Cookies"] = options => options.Cookies = new CookieContainer(),
            ["Credentials"] = options => options.Credentials = new NetworkCredential("user", "secret"),
            ["UseDefaultCredentials"] = options => options.UseDefaultCredentials = true,
            ["ClientCertificates"] = options => options.ClientCertificates.Add(SelfSigned()),
            ["RemoteCertificateValidationCallback"] = options => options.RemoteCertificateValidationCallback = (_, _, _, _) => true,
        };

        using var handler = SocketHttpFactory.CreateHandler(new NoireSocketHttpOptions(), null);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);

        foreach (var setter in setters)
        {
            using var socket = new ClientWebSocket();
            setter.Value(socket.Options);

            var failure = await Connect(socket, invoker).ConfigureAwait(false);

            failure.Should().BeOfType<ArgumentException>(
                setter.Key + " on the socket throws once an invoker is supplied; the factory sets it on the handler instead");
        }
    }

    // The address is refused. The test never holds a connection open.
    private static async Task<Exception?> Connect(ClientWebSocket socket, HttpMessageInvoker invoker)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await socket.ConnectAsync(Unreachable, invoker, deadline.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=noire-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static string Header(HttpRequestMessage request, string name)
        => string.Join(",", request.Headers.GetValues(name));

    private static (long Timestamp, string Nonce, string Mac) ReadSignature(string header)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in header[(NoireRemoteHeaders.SignedScheme.Length + 1)..].Split(','))
        {
            var split = part.IndexOf('=');
            fields[part[..split]] = part[(split + 1)..];
        }

        return (long.Parse(fields["ts"], CultureInfo.InvariantCulture), fields["nonce"], fields["mac"]);
    }
}
