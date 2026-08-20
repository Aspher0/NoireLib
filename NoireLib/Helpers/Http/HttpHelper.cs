using Dalamud.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// HTTP requests of any verb, with headers, a body and a per-request timeout, over one shared client. Nothing
/// here throws: a network error, a timeout, a non-success status and cancellation all come back as an
/// <see cref="HttpResponse"/> carrying the reason.
/// </summary>
public static class HttpHelper
{
    private const string LogPrefix = "[Http] ";

    private static readonly Lock ClientLock = new();
    private static readonly Dictionary<string, string> NoHeaders = [];

    private static HttpClient? client;

    /// <summary>How long a request may take when its spec does not say.</summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The User-Agent sent when a request does not set one, or null to send none.</summary>
    public static string? DefaultUserAgent { get; set; }

    /// <summary>Whether a failed request is logged.</summary>
    public static bool LogFailures { get; set; } = true;

    /// <summary>Sends a request and reads the response.</summary>
    /// <param name="spec">What to send.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <returns>The outcome, which reports the failure rather than throwing it.</returns>
    public static async Task<HttpResponse> SendAsync(HttpRequestSpec spec, CancellationToken token = default)
    {
        if (spec.Url.IsNullOrWhitespace())
            return Failed("The URL is empty.", token);

        if (!Uri.TryCreate(spec.Url, UriKind.Absolute, out var uri))
            return Failed($"'{spec.Url}' is not an absolute URL.", token);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        timeoutSource.CancelAfter(spec.Timeout ?? DefaultTimeout);

        try
        {
            using var request = new HttpRequestMessage(spec.Method, uri);

            if (spec.Content != null)
                request.Content = spec.Content;

            ApplyHeaders(request, spec.Headers);

            var completion = spec.ReadContent
                ? HttpCompletionOption.ResponseContentRead
                : HttpCompletionOption.ResponseHeadersRead;

            using var response = await Client()
                .SendAsync(request, completion, timeoutSource.Token)
                .ConfigureAwait(false);

            var succeeded = response.IsSuccessStatusCode;
            var wantsBody = spec.ReadContent && (succeeded || spec.ReadContentOnFailure);

            var body = wantsBody
                ? await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false)
                : [];

            if (!succeeded && LogFailures)
                NoireLogger.LogWarning($"'{spec.Url}' answered {(int)response.StatusCode} {response.ReasonPhrase}.", LogPrefix);

            return new HttpResponse
            {
                IsSuccess = succeeded,
                StatusCode = response.StatusCode,
                Content = body,
                Headers = CollectHeaders(response),
                Error = succeeded ? null : $"{(int)response.StatusCode} {response.ReasonPhrase}",
            };
        }
        catch (OperationCanceledException)
        {
            // The caller's token and the timeout both surface here, and only the caller's is a cancellation.
            return token.IsCancellationRequested
                ? Failed("The caller cancelled the request.", token)
                : Failed($"'{spec.Url}' did not answer within {(spec.Timeout ?? DefaultTimeout).TotalSeconds:0.##}s.", token);
        }
        catch (Exception ex)
        {
            if (LogFailures)
                NoireLogger.LogError(ex, $"Requesting '{spec.Url}' failed.", LogPrefix);

            return Failed(ex.Message, token);
        }
    }

    /// <summary>Sends a GET request and reads the response.</summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <returns>The outcome.</returns>
    public static Task<HttpResponse> GetAsync(
        string url, IReadOnlyDictionary<string, string>? headers = null, TimeSpan? timeout = null,
        CancellationToken token = default)
        => SendAsync(new HttpRequestSpec { Url = url, Headers = headers, Timeout = timeout }, token);

    /// <summary>Sends a POST request carrying a body, disposing the body afterwards.</summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="content">The body to send.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <returns>The outcome.</returns>
    public static async Task<HttpResponse> PostAsync(
        string url, HttpContent content, IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null, CancellationToken token = default)
    {
        using (content)
        {
            var spec = new HttpRequestSpec
            {
                Url = url,
                Method = HttpMethod.Post,
                Content = content,
                Headers = headers,
                Timeout = timeout,
            };

            return await SendAsync(spec, token).ConfigureAwait(false);
        }
    }

    /// <summary>Sends a value as a JSON body.</summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="body">The value to serialize.</param>
    /// <param name="method">The verb to send it with, or null for POST.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <param name="settings">Serializer settings, or null for the defaults.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <returns>The outcome.</returns>
    public static async Task<HttpResponse> SendJsonAsync(
        string url, object? body, HttpMethod? method = null, IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null, JsonSerializerSettings? settings = null, CancellationToken token = default)
    {
        string json;

        try
        {
            json = JsonConvert.SerializeObject(body, settings);
        }
        catch (Exception ex)
        {
            if (LogFailures)
                NoireLogger.LogError(ex, $"The body sent to '{url}' could not be serialized.", LogPrefix);

            return Failed(ex.Message, token);
        }

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var spec = new HttpRequestSpec
        {
            Url = url,
            Method = method ?? HttpMethod.Post,
            Content = content,
            Headers = headers,
            Timeout = timeout,
        };

        return await SendAsync(spec, token).ConfigureAwait(false);
    }

    /// <summary>Fetches a URL as text.</summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <returns>The body as text, or null on any failure.</returns>
    public static async Task<string?> GetStringAsync(
        string url, CancellationToken token = default, IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null)
    {
        var response = await GetAsync(url, headers, timeout, token).ConfigureAwait(false);

        return response.IsSuccess ? response.AsString() : null;
    }

    /// <summary>Fetches a URL as bytes.</summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <returns>The body, or null on any failure.</returns>
    public static async Task<byte[]?> GetBytesAsync(
        string url, CancellationToken token = default, IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null)
    {
        var response = await GetAsync(url, headers, timeout, token).ConfigureAwait(false);

        return response.IsSuccess ? response.Content : null;
    }

    /// <summary>Fetches a URL and deserializes the JSON it answers with.</summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="url">The URL to request.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <param name="settings">Serializer settings, or null for the defaults.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <returns>The deserialized value, or null on any failure.</returns>
    public static async Task<T?> GetJsonAsync<T>(
        string url, CancellationToken token = default, JsonSerializerSettings? settings = null,
        IReadOnlyDictionary<string, string>? headers = null, TimeSpan? timeout = null)
    {
        var response = await GetAsync(url, headers, timeout, token).ConfigureAwait(false);

        return response.IsSuccess ? DeserializeJson<T>(response.AsString(), url, settings) : default;
    }

    /// <summary>Fetches a URL and writes it to disk, replacing the destination only once the whole body arrived.</summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="destinationPath">Where the body goes.</param>
    /// <param name="token">A token cancelled when the caller goes away.</param>
    /// <param name="headers">Headers to add, or null for none.</param>
    /// <param name="timeout">How long the request may take, or null for <see cref="DefaultTimeout"/>.</param>
    /// <returns>True when the file is on disk afterwards.</returns>
    public static async Task<bool> DownloadFileAsync(
        string url, string destinationPath, CancellationToken token = default,
        IReadOnlyDictionary<string, string>? headers = null, TimeSpan? timeout = null)
    {
        if (destinationPath.IsNullOrWhitespace())
            return false;

        var response = await GetAsync(url, headers, timeout, token).ConfigureAwait(false);

        if (!response.IsSuccess)
            return false;

        var directory = Path.GetDirectoryName(destinationPath);

        if (!directory.IsNullOrWhitespace() && !FileHelper.EnsureDirectoryExists(directory))
            return false;

        return FileHelper.ReplaceFileAtomically(destinationPath, response.Content);
    }

    internal static T? DeserializeJson<T>(string json, string? url, JsonSerializerSettings? settings)
    {
        if (json.IsNullOrWhitespace())
            return default;

        try
        {
            return JsonConvert.DeserializeObject<T>(json, settings);
        }
        catch (Exception ex)
        {
            if (LogFailures)
            {
                var message = url == null
                    ? $"The response was not readable as {typeof(T).Name}."
                    : $"The response from '{url}' was not readable as {typeof(T).Name}.";

                NoireLogger.LogError(ex, message, LogPrefix);
            }

            return default;
        }
    }

    private static HttpResponse Failed(string error, CancellationToken token) => new()
    {
        IsSuccess = false,
        StatusCode = 0,
        Content = [],
        Headers = NoHeaders,
        Error = error,
        WasCancelled = token.IsCancellationRequested,
    };

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        var sentUserAgent = false;

        if (headers != null)
        {
            foreach (var (name, value) in headers)
            {
                // The request collection rejects content headers, so each header lands where it belongs.
                if (!request.Headers.TryAddWithoutValidation(name, value))
                    request.Content?.Headers.TryAddWithoutValidation(name, value);

                sentUserAgent |= string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!sentUserAgent && !DefaultUserAgent.IsNullOrWhitespace())
            request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
    }

    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var collected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in response.Headers)
            collected[name] = string.Join(", ", values);

        foreach (var (name, values) in response.Content.Headers)
            collected[name] = string.Join(", ", values);

        return collected;
    }

    private static HttpClient Client()
    {
        if (client != null)
            return client;

        lock (ClientLock)
        {
            if (client != null)
                return client;

            // The per-request linked token owns every deadline, so the client itself must never impose one.
            client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            NoireLibMain.RegisterOnDispose("NoireLib_Internal_HttpHelper", Dispose);

            return client;
        }
    }

    private static void Dispose()
    {
        lock (ClientLock)
        {
            client?.Dispose();
            client = null;
        }
    }
}
