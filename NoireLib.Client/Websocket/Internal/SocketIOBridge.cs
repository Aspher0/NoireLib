using NoireLib.Remote;
using SocketIO.Serializer.NewtonsoftJson;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using PackageHttpClient = SocketIOClient.Transport.Http.IHttpClient;
using PackageMessageType = SocketIOClient.Transport.TransportMessageType;
using PackageOptions = SocketIOClient.SocketIOOptions;
using PackageReceiveResult = SocketIOClient.Transport.WebSockets.WebSocketReceiveResult;
using PackageSocket = SocketIOClient.SocketIO;
using PackageTransport = SocketIOClient.Transport.TransportProtocol;
using PackageWebSocket = SocketIOClient.Transport.WebSockets.IClientWebSocket;
using PackageWebSocketState = SocketIOClient.Transport.WebSockets.WebSocketState;

namespace NoireLib.Websocket.Internal;

// The package reads only Proxy, the certificate callback, ConnectionTimeout, ExtraHeaders, Auth and Query from its own options.
// Its IHttpClient and websocket factory are supplied from NoireSocketHttpOptions to carry the rest.
internal sealed class SocketIOBridge : IDisposable
{
    private readonly SocketsHttpHandler handler;
    private readonly BridgeHttpClient httpClient;

    public SocketIOBridge(Uri serverUri, NoireSocketIOOptions options)
    {
        var http = options.Http.Clone();

        handler = SocketHttpFactory.CreateHandler(http, null);
        httpClient = new BridgeHttpClient(handler, http);

        Client = new PackageSocket(serverUri, BuildOptions(options));

        // The package builds its serializer and HTTP client in its constructor. This is the earliest point to replace them.
        Client.Serializer = new NewtonsoftJsonSerializer(NoireRemoteJson.CreateSettings());

        var abandoned = Client.HttpClient;
        Client.HttpClient = httpClient;
        abandoned?.Dispose();

        Client.ClientWebSocketProvider = () => new BridgeClientWebSocket(http);
    }

    public PackageSocket Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        httpClient.Dispose();
        handler.Dispose();
    }

    private static PackageOptions BuildOptions(NoireSocketIOOptions options)
    {
        var http = options.Http;

        var target = new PackageOptions
        {
            Path = options.Path,
            Auth = options.Auth,
            ConnectionTimeout = http.ConnectionTimeout,
            Proxy = http.Proxy,
            RemoteCertificateValidationCallback = http.RemoteCertificateValidationCallback,
            Reconnection = options.Reconnect.Enabled,
            ReconnectionAttempts = options.Reconnect.MaxAttempts,
            ReconnectionDelay = options.Reconnect.InitialDelay.TotalMilliseconds,
            ReconnectionDelayMax = (int)options.Reconnect.MaxDelay.TotalMilliseconds,
            RandomizationFactor = options.Reconnect.Jitter,
            ExtraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        foreach (var pair in http.Headers)
            target.ExtraHeaders[pair.Key] = pair.Value;

        if (options.Query.Count > 0)
        {
            var query = new List<KeyValuePair<string, string>>(options.Query.Count);

            foreach (var pair in options.Query)
                query.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));

            target.Query = query;
        }

        ApplyTransport(target, options.Transport);

        options.ConfigureOptions?.Invoke(target);
        return target;
    }

    // AutoUpgrade defaults to true and upgrades whenever the server offers WebSocket.
    private static void ApplyTransport(PackageOptions target, NoireSocketIOTransport transport)
    {
        switch (transport)
        {
            case NoireSocketIOTransport.PollingOnly:
                target.Transport = PackageTransport.Polling;
                target.AutoUpgrade = false;
                break;

            case NoireSocketIOTransport.WebSocketOnly:
                target.Transport = PackageTransport.WebSocket;
                target.AutoUpgrade = false;
                break;

            default:
                target.Transport = PackageTransport.Polling;
                target.AutoUpgrade = true;
                break;
        }
    }
}

internal sealed class BridgeHttpClient : PackageHttpClient, IDisposable
{
    private readonly HttpClient transport;
    private readonly NoireSocketHttpOptions http;

    public BridgeHttpClient(SocketsHttpHandler handler, NoireSocketHttpOptions http)
    {
        this.http = http;

        // A long poll is held open until the server has something. The handler carries the connect deadline.
        transport = new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void AddHeader(string name, string value)
    {
        if (transport.DefaultRequestHeaders.Contains(name))
            transport.DefaultRequestHeaders.Remove(name);

        transport.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
    }

    // A SocketsHttpHandler refuses a proxy change once it has sent a request.
    public void SetProxy(IWebProxy proxy)
    {
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A signed credential covers the body. It is read before the header is built. Buffered content is not consumed by the read.
        var payload = http.Credential != null && request.Content != null
            ? await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)
            : null;

        SocketHttpFactory.ApplyHeaders(request, http, request.RequestUri?.OriginalString ?? "/", payload);

        return await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        return SendAsync(request, cancellationToken);
    }

    public async Task<string> GetStringAsync(Uri requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await SendAsync(request, CancellationToken.None).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public void Dispose()
        => transport.Dispose();
}

// A custom HttpMessageInvoker would make every socket setting throw.
internal sealed class BridgeClientWebSocket : PackageWebSocket, IDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly NoireSocketHttpOptions http;

    public BridgeClientWebSocket(NoireSocketHttpOptions http)
    {
        this.http = http;

        if (http.Cookies != null)
            socket.Options.Cookies = http.Cookies;

        if (http.Credentials != null)
            socket.Options.Credentials = http.Credentials;

        if (http.ClientCertificates != null)
            socket.Options.ClientCertificates.AddRange(http.ClientCertificates);

        if (http.RemoteCertificateValidationCallback != null)
            socket.Options.RemoteCertificateValidationCallback = http.RemoteCertificateValidationCallback;

        if (http.Proxy != null)
            socket.Options.Proxy = http.Proxy;
        else if (!http.UseSystemProxy)
            socket.Options.Proxy = null;
    }

    public PackageWebSocketState State => (PackageWebSocketState)socket.State;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        SocketHttpFactory.ApplyHeaders(socket.Options, http, uri.PathAndQuery);
        return socket.ConnectAsync(uri, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
        => socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);

    public Task SendAsync(byte[] bytes, PackageMessageType type, bool endOfMessage, CancellationToken cancellationToken)
        => socket.SendAsync(
            new ArraySegment<byte>(bytes),
            type == PackageMessageType.Binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
            endOfMessage,
            cancellationToken);

    public async Task<PackageReceiveResult> ReceiveAsync(int bufferSize, CancellationToken cancellationToken)
    {
        var buffer = new byte[bufferSize];
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

        return new PackageReceiveResult
        {
            Count = result.Count,
            MessageType = (PackageMessageType)result.MessageType,
            EndOfMessage = result.EndOfMessage,
            Buffer = buffer,
        };
    }

    public void AddHeader(string key, string val)
        => socket.Options.SetRequestHeader(key, val);

    public void SetProxy(IWebProxy proxy)
        => socket.Options.Proxy = proxy;

    public void Dispose()
        => socket.Dispose();
}
