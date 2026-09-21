using NoireLib.Remote;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;

namespace NoireLib.Websocket.Internal;

// The pool's lifetime timer is rooted outside the caller's load context. A shared handler would outlive an unloading plugin.
internal static class SocketHttpFactory
{
    // ClientWebSocketOptions throws for the proxy, cookies, credentials and certificates once a custom HttpMessageInvoker is supplied.
    public static SocketsHttpHandler CreateHandler(NoireSocketHttpOptions http, Action<SocketsHttpHandler>? configure)
    {
        var built = new SocketsHttpHandler
        {
            UseCookies = http.Cookies != null,
            CookieContainer = http.Cookies ?? new CookieContainer(),
            UseProxy = http.Proxy != null || http.UseSystemProxy,
            Proxy = http.Proxy,
            Credentials = http.Credentials,
            ConnectTimeout = http.ConnectionTimeout,
            PooledConnectionLifetime = http.PooledConnectionLifetime,
        };

        if (http.ClientCertificates != null)
            built.SslOptions.ClientCertificates = http.ClientCertificates;

        if (http.RemoteCertificateValidationCallback != null)
            built.SslOptions.RemoteCertificateValidationCallback = http.RemoteCertificateValidationCallback;

        configure?.Invoke(built);
        return built;
    }

    // The credential signs the requested path over the body sent. Content attached with body null here is refused.
    public static void ApplyHeaders(HttpRequestMessage request, NoireSocketHttpOptions http, string fallbackPath, byte[]? body = null)
    {
        foreach (var pair in http.Headers)
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);

        if (http.Credential == null)
            return;

        var path = request.RequestUri?.IsAbsoluteUri == true ? request.RequestUri.PathAndQuery : fallbackPath;
        request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization,
            http.Credential.BuildHeaderValue(request.Method.Method, path, body));
    }

    public static void ApplyHeaders(ClientWebSocketOptions socket, NoireSocketHttpOptions http, string path)
    {
        foreach (var pair in http.Headers)
            socket.SetRequestHeader(pair.Key, pair.Value);

        // An upgrade handshake is a GET.
        if (http.Credential != null)
            socket.SetRequestHeader(NoireRemoteHeaders.Authorization, http.Credential.BuildHeaderValue("GET", path));
    }

    public static IReadOnlyDictionary<string, string> ReadHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        foreach (var header in response.Content.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        return headers;
    }
}
