using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

// The one place a request leaves this assembly.
internal static class RemoteWireTransport
{
    private static HttpClient client = CreateClient();

    // A plugin unloading while this timer is held never collects. Shutdown swaps in a fresh client.
    public static void Shutdown()
    {
        var replaced = Interlocked.Exchange(ref client, CreateClient());

        try
        {
            replaced.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };

        // Timeouts are applied per call. One client serves very different deadlines.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static async Task<string> SendAsync(
        NoireRemoteInstance instance,
        HttpMethod method,
        string path,
        object? body,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var bytes = body == null ? [] : NoireRemoteJson.WriteBytes(body);

        using var request = new HttpRequestMessage(method, instance.BaseUrl.TrimEnd('/') + path);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Headers.ConnectionClose = true;

        if (method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NoireRemoteHeaders.JsonContentType) { CharSet = "utf-8" };
        }

        Authorize(request, instance, method.Method, path, bytes);

        if (instance.Id != Guid.Empty)
            request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Instance, instance.Id.ToString("D"));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using var response = await Volatile.Read(ref client).SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NoireRemoteTimeoutException(
                "No answer from " + instance + " within " + FormatSeconds(timeout) + ".", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new NoireRemoteNotFoundException(
                "Could not reach " + instance + ". " + Innermost(exception).Message, exception);
        }
    }

    private static Exception Innermost(Exception exception)
    {
        // The reason a socket refused is two levels down.
        while (exception.InnerException != null)
            exception = exception.InnerException;

        return exception;
    }

    // The response streams as it arrives.
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        NoireRemoteInstance instance,
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, instance.BaseUrl.TrimEnd('/') + path);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Headers.ConnectionClose = true;

        Authorize(request, instance, "GET", path, []);

        if (instance.Id != Guid.Empty)
            request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Instance, instance.Id.ToString("D"));

        HttpResponseMessage response;

        try
        {
            response = await Volatile.Read(ref client)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new NoireRemoteNotFoundException("Could not reach " + instance + ". " + Innermost(exception).Message, exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var envelope = NoireRemoteJson.TryRead<NoireRemoteEnvelope>(text);

                throw envelope is { Ok: false } failed
                    ? ToException(failed, instance)
                    : new NoireRemoteException("The stream was refused with status " + (int)response.StatusCode + ".");
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (System.IO.IOException)
                {
                    yield break;
                }

                if (line == null)
                    yield break;

                // A blank line is the keep-alive.
                if (line.Length > 0)
                    yield return line;
            }
        }
    }

    public static async Task<NoireRemoteEnvelope> SendEnvelopeAsync(
        NoireRemoteInstance instance,
        HttpMethod method,
        string path,
        object? body,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var text = await SendAsync(instance, method, path, body, timeout, cancellationToken).ConfigureAwait(false);
        var envelope = ReadDocument<NoireRemoteEnvelope>(text, instance);

        if (!envelope.Ok)
            throw ToException(envelope, instance);

        return envelope;
    }

    public static async Task<TDocument> SendDocumentAsync<TDocument>(
        NoireRemoteInstance instance,
        HttpMethod method,
        string path,
        object? body,
        TimeSpan timeout,
        CancellationToken cancellationToken) where TDocument : class
    {
        var text = await SendAsync(instance, method, path, body, timeout, cancellationToken).ConfigureAwait(false);

        // A failure on any route answers with the error envelope.
        var envelope = NoireRemoteJson.TryRead<NoireRemoteEnvelope>(text);

        if (envelope is { Ok: false, Error: not null })
            throw ToException(envelope, instance);

        return ReadDocument<TDocument>(text, instance);
    }

    private static TDocument ReadDocument<TDocument>(string text, NoireRemoteInstance instance) where TDocument : class
    {
        try
        {
            return NoireRemoteJson.Read<TDocument>(text)
                ?? throw new NoireRemoteProtocolException("The answer from " + instance + " carried no body.");
        }
        catch (NoireRemoteException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NoireRemoteProtocolException("The answer from " + instance + " is not a NoireRemote document.", exception);
        }
    }

    public static Exception ToException(NoireRemoteEnvelope envelope, NoireRemoteInstance instance)
    {
        var error = envelope.Error;

        if (error == null)
            return new NoireRemoteException("The call to " + instance + " failed with no error body.");

        var message = string.IsNullOrEmpty(error.Message) ? error.Code : error.Message;

        switch (error.Code)
        {
            case NoireRemoteErrorCodes.Unauthorized:
                return new NoireRemoteUnauthorizedException(message);

            case NoireRemoteErrorCodes.Forbidden:
                return new NoireRemoteForbiddenException(message);

            case NoireRemoteErrorCodes.ArgumentMissing:
            case NoireRemoteErrorCodes.ArgumentUnknown:
            case NoireRemoteErrorCodes.ArgumentInvalid:
                return new NoireRemoteArgumentException(message, error.Detail, error.Code);

            case NoireRemoteErrorCodes.Timeout:
                return new NoireRemoteTimeoutException(message);

            case NoireRemoteErrorCodes.NotReady:
                return new NoireRemoteNotReadyException(message, TimeSpan.FromSeconds(error.RetryAfterSeconds ?? 1));

            case NoireRemoteErrorCodes.Busy:
                return new NoireRemoteBusyException(message, TimeSpan.FromSeconds(error.RetryAfterSeconds ?? 1));

            case NoireRemoteErrorCodes.HandlerFault:
                return new NoireRemoteRemoteException(message, error.Detail, error.Message, error.StackTrace);

            case NoireRemoteErrorCodes.ProtocolMismatch:
                return new NoireRemoteProtocolException(message);

            case NoireRemoteErrorCodes.UnknownEndpoint:
            case NoireRemoteErrorCodes.UnknownMember:
            case NoireRemoteErrorCodes.JobNotFound:
            case NoireRemoteErrorCodes.InstanceMismatch:
            case NoireRemoteErrorCodes.ShuttingDown:
                return new NoireRemoteNotFoundException(message);

            default:
                return new NoireRemoteException(message) { Code = error.Code };
        }
    }

    private static void Authorize(HttpRequestMessage request, NoireRemoteInstance instance, string method, string path, byte[] body)
    {
        if (!string.IsNullOrEmpty(instance.Secret))
        {
            var header = NoireRemoteSignature.CreateHeader(instance.Secret!, method, path, body);
            request.Headers.TryAddWithoutValidation(NoireRemoteHeaders.Authorization, header);
            return;
        }

        if (!string.IsNullOrEmpty(instance.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue(NoireRemoteHeaders.BearerScheme, instance.Token);
    }

    private static string FormatSeconds(TimeSpan value)
        => value.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
}
