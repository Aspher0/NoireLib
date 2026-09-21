using Newtonsoft.Json.Linq;
using NoireLib.Websocket;
using NoireLib.Websocket.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

internal sealed class HttpRouterContext
{
    public HttpRouteTable Routes = null!;
    public RemoteCallDispatcher Dispatcher = null!;
    public RemoteJobStore Jobs = null!;
    public RemoteEventHub Events = null!;
    public NoireRemoteOptions Options = null!;
    public INoireRemoteHost Host = null!;
    public Guid Instance;
    public string Plugin = string.Empty;
    public string PluginVersion = string.Empty;
    public DateTime StartedUtc;
    public Func<NoireRemoteManifest> Manifest = null!;
    public Func<string?> BaseUrl = static () => null;
    public Func<string> Label = static () => string.Empty;

    // For the console's socket relay. It never reaches the page.
    public Func<string> Token = static () => string.Empty;
    public Func<IReadOnlyDictionary<string, string>> Metadata = static () => new Dictionary<string, string>();
    public Action<NoireRemoteCallReport>? Report;
    public RemoteCallStatistics Calls = new();
    public HttpFileStore Files = null!;
}

// Carried holds the bytes that came with the header block: for a bodyless upgrade, the peer's first frame.
internal readonly record struct HttpConnectionAdoption(
    TcpClient Client,
    System.IO.Stream Stream,
    ReadOnlyMemory<byte> Carried,
    bool IsLoopback,
    string RemoteAddress);

internal sealed class HttpRouteOutcome
{
    public int Status { get; set; } = 200;

    public object Body { get; set; } = new object();

    public double? RetryAfterSeconds { get; set; }

    // Written directly onto the socket in place of Body. The connection closes when it returns.
    public Func<System.IO.Stream, CancellationToken, Task>? Write { get; set; }

    // Takes the connection over. The server writes nothing more and never disposes the client.
    // The callee writes the 101 itself after claiming its slot and registering the connection.
    public Func<HttpConnectionAdoption, Task>? Adopt { get; set; }

    public bool IsIdempotentReplay { get; set; }

    // Such as the version a 426 names.
    public IReadOnlyList<KeyValuePair<string, string>>? ExtraHeaders { get; set; }

    public static HttpRouteOutcome From(HttpFailure failure, Guid instance, string? requestId)
        => new()
        {
            Status = failure.Status,
            RetryAfterSeconds = failure.RetryAfterSeconds,
            ExtraHeaders = failure.ExtraHeaders,
            Body = new NoireRemoteEnvelope
            {
                Ok = false,
                Instance = instance,
                Id = requestId,
                Error = new NoireRemoteError
                {
                    Code = failure.Code,
                    Message = failure.Message,
                    Detail = failure.Detail,
                    RetryAfterSeconds = failure.RetryAfterSeconds,
                    Candidates = failure.Candidates,
                },
            },
        };
}

internal static class HttpRouter
{
    public static Task<HttpRouteOutcome> RouteAsync(HttpRequestData request, bool isLoopback, HttpRouterContext context, CancellationToken cancellationToken)
        => RouteOnceAsync(request, isLoopback, context, cancellationToken);

    private static async Task<HttpRouteOutcome> RouteOnceAsync(HttpRequestData request, bool isLoopback, HttpRouterContext context, CancellationToken cancellationToken)
    {
        var requestId = request.Header(NoireRemoteHeaders.RequestId);

        var pinned = request.Header(NoireRemoteHeaders.Instance);

        if (!string.IsNullOrEmpty(pinned) && (!Guid.TryParse(pinned, out var wanted) || wanted != context.Instance))
            return HttpRouteOutcome.From(
                new HttpFailure(409, NoireRemoteErrorCodes.InstanceMismatch, "This listener is instance " + context.Instance.ToString("D") + "."),
                context.Instance, requestId);

        // Tested before the member prefix.
        if (request.Path.StartsWith(NoireWebsocketServer.RoutePrefix, StringComparison.Ordinal))
            return WebsocketRouting.Route(HttpQueryString.PathOf(request.Path), request, isLoopback, context, requestId);

        if (!request.Path.StartsWith(NoireRemotePaths.Prefix, StringComparison.Ordinal))
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "Every route sits under " + NoireRemotePaths.Prefix + "."),
                context.Instance, requestId);

        var remainder = HttpQueryString.PathOf(request.Path).Substring(NoireRemotePaths.Prefix.Length).Trim('/');
        string[] segments = remainder.Length == 0 ? [] : remainder.Split('/');

        if (segments.Length == 0)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "The route names no endpoint."),
                context.Instance, requestId);

        if (segments[0].Length > 0 && segments[0][0] == '_')
            return await RouteMetaAsync(segments, request, isLoopback, context, requestId, cancellationToken).ConfigureAwait(false);

        if (segments.Length != 2)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "A member route reads as " + NoireRemotePaths.Prefix + "{endpoint}/{member}."),
                context.Instance, requestId);

        return await RouteMemberAsync(segments[0], segments[1], request, isLoopback, context, requestId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpRouteOutcome> RouteMetaAsync(string[] segments, HttpRequestData request, bool isLoopback, HttpRouterContext context, string? requestId, CancellationToken cancellationToken)
    {
        switch (segments[0])
        {
            case NoireRemotePaths.Ping when segments.Length == 1:
                if (request.Method != "GET")
                    return BadMethod(context, requestId, "GET");

                return new HttpRouteOutcome
                {
                    Body = new NoireRemotePing
                    {
                        Instance = context.Instance,
                        UptimeMs = (long)(DateTime.UtcNow - context.StartedUtc).TotalMilliseconds,
                        Endpoints = context.Routes.EndpointNames(),
                        Machine = Environment.MachineName,
                        Metadata = context.Metadata(),
                        Label = context.Label(),
                        Plugin = context.Plugin,
                        PluginVersion = context.PluginVersion,
                        AllowFleetControl = context.Options.AllowFleetControl,
                    },
                };

            case NoireRemotePaths.Manifest when segments.Length == 1:
                if (request.Method != "GET")
                    return BadMethod(context, requestId, "GET");

                if (!context.Options.EnableManifest)
                    return HttpRouteOutcome.From(
                        new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "This listener does not serve its manifest."),
                        context.Instance, requestId);

                return new HttpRouteOutcome { Body = context.Manifest() };

            case NoireRemotePaths.Jobs when segments.Length == 2:
                return RouteJob(segments[1], request, context, requestId);

            case NoireRemotePaths.Events when segments.Length == 1:
                if (request.Method != "POST")
                    return BadMethod(context, requestId, "POST");

                return await RouteEventsAsync(request, context, requestId, cancellationToken).ConfigureAwait(false);

            case NoireRemotePaths.Stream when segments.Length == 1:
                return RouteStream(request, context, requestId);

            case NoireRemotePaths.OpenApi when segments.Length == 1:
                if (request.Method != "GET")
                    return BadMethod(context, requestId, "GET");

                if (!context.Options.EnableOpenApi)
                    return HttpRouteOutcome.From(
                        new HttpFailure(404, NoireRemoteErrorCodes.FeatureDisabled, "This listener does not serve its OpenAPI document."),
                        context.Instance, requestId);

                return new HttpRouteOutcome { Body = NoireRemoteOpenApi.FromManifest(context.Manifest(), context.BaseUrl()) };

            case NoireRemotePaths.Sockets:
                return HttpSocketRoutes.Route(segments, request, isLoopback, context, requestId);

            case NoireRemotePaths.Stubs when segments.Length == 2:
                if (request.Method != "GET")
                    return BadMethod(context, requestId, "GET");

                if (!string.Equals(segments[1], "python", StringComparison.OrdinalIgnoreCase))
                    return HttpRouteOutcome.From(
                        new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint,
                            "This listener generates a client for 'python'."),
                        context.Instance, requestId);

                if (!context.Options.EnableManifest)
                    return HttpRouteOutcome.From(
                        new HttpFailure(404, NoireRemoteErrorCodes.FeatureDisabled,
                            "A generated client is the manifest in another shape, and this listener does not serve it."),
                        context.Instance, requestId);

                return new HttpRouteOutcome
                {
                    Write = (stream, token) => HttpResponseWriter.WriteBytesAsync(
                        stream,
                        200,
                        "text/x-python; charset=utf-8",
                        System.Text.Encoding.UTF8.GetBytes(NoireRemotePythonStub.FromManifest(context.Manifest())),
                        context.Instance,
                        null,
                        token),
                };

            case NoireRemotePaths.Files when segments.Length is 1 or 2:
                return HttpFileRoutes.Route(segments, request, context, requestId);

            default:
                return HttpRouteOutcome.From(
                    new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "'" + segments[0] + "' is not a route this listener serves."),
                    context.Instance, requestId);
        }
    }

    private static HttpRouteOutcome RouteJob(string id, HttpRequestData request, HttpRouterContext context, string? requestId)
    {
        if (request.Method != "GET" && request.Method != "DELETE")
            return BadMethod(context, requestId, "GET or DELETE");

        var job = request.Method == "DELETE" ? context.Jobs.Cancel(id) : context.Jobs.Get(id);

        if (job == null)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.JobNotFound, "No job '" + id + "' is held. It never existed or its retention has passed."),
                context.Instance, requestId);

        return new HttpRouteOutcome
        {
            Body = new NoireRemoteEnvelope
            {
                Ok = true,
                Instance = context.Instance,
                Id = requestId,
                Job = job.ToStatus(),
                Result = job.State == NoireRemoteJobState.Done ? job.Result : null,
                Error = job.State == NoireRemoteJobState.Failed ? job.Error : null,
            },
        };
    }

    private static async Task<HttpRouteOutcome> RouteEventsAsync(HttpRequestData request, HttpRouterContext context, string? requestId, CancellationToken cancellationToken)
    {
        NoireRemoteEventRequest? body;

        try
        {
            body = NoireRemoteJson.Read<NoireRemoteEventRequest>(System.Text.Encoding.UTF8.GetString(request.Body)) ?? new NoireRemoteEventRequest();
        }
        catch (Exception)
        {
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The event poll body is not valid JSON."),
                context.Instance, requestId);
        }

        var query = HttpEventQuery.From(body, context.Options.MaxFilterClauses, out var invalid);

        if (invalid != null)
            return HttpRouteOutcome.From(invalid, context.Instance, requestId);

        var ceiling = context.Options.MaxCallTimeout - TimeSpan.FromSeconds(5);
        var wait = TimeSpan.FromMilliseconds(Math.Max(0, body.WaitMs ?? 25000));

        if (wait > ceiling)
            wait = ceiling;

        try
        {
            var batch = await context.Events.PollAsync(query, wait, context.Instance, cancellationToken).ConfigureAwait(false);
            return new HttpRouteOutcome { Body = batch };
        }
        catch (HttpBusyException)
        {
            return HttpRouteOutcome.From(
                new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many event polls as it accepts.") { RetryAfterSeconds = 1 },
                context.Instance, requestId);
        }
    }

    // Newline-delimited JSON on a close-delimited response. The parser still refuses Transfer-Encoding.
    private static HttpRouteOutcome RouteStream(HttpRequestData request, HttpRouterContext context, string? requestId)
    {
        if (request.Method != "GET" && request.Method != "POST")
            return BadMethod(context, requestId, "GET or POST");

        NoireRemoteEventRequest body;

        if (request.Method == "POST" && request.Body.Length > 0)
        {
            try
            {
                body = NoireRemoteJson.Read<NoireRemoteEventRequest>(System.Text.Encoding.UTF8.GetString(request.Body)) ?? new NoireRemoteEventRequest();
            }
            catch (Exception)
            {
                return HttpRouteOutcome.From(
                    new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The stream body is not valid JSON."),
                    context.Instance, requestId);
            }
        }
        else
        {
            body = HttpQueryString.ToEventRequest(request.Target);
        }

        var query = HttpEventQuery.From(body, context.Options.MaxFilterClauses, out var invalid);

        if (invalid != null)
            return HttpRouteOutcome.From(invalid, context.Instance, requestId);

        var minInterval = TimeSpan.FromMilliseconds(Math.Max(0, body.WaitMs ?? 0));

        return new HttpRouteOutcome
        {
            Write = (stream, token) => HttpEventStream.RunAsync(stream, context, query, minInterval, token),
        };
    }

    private static async Task<HttpRouteOutcome> RouteMemberAsync(
        string endpointName,
        string memberName,
        HttpRequestData request,
        bool isLoopback,
        HttpRouterContext context,
        string? requestId,
        CancellationToken cancellationToken)
    {
        // A published property is a GET. Everything else stays a POST, like a browser expects of a call that changes something.
        var readOnly = context.Routes.TryGetMember(endpointName, memberName, out var declared, out _)
                       && declared != null
                       && declared.ReadOnly;

        if (request.Method != "POST" && !(readOnly && request.Method == "GET"))
            return BadMethod(context, requestId, readOnly ? "GET or POST" : "POST");

        NoireRemoteRequest? body;

        try
        {
            body = NoireRemoteJson.Read<NoireRemoteRequest>(System.Text.Encoding.UTF8.GetString(request.Body)) ?? new NoireRemoteRequest();
        }
        catch (Exception)
        {
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.BadRequest, "The request body is not valid JSON."),
                context.Instance, requestId);
        }

        requestId ??= body.Id;
        requestId ??= NewRequestId();

        if (body.Protocol is { } protocol && protocol != NoireRemotePaths.Protocol)
            return HttpRouteOutcome.From(
                new HttpFailure(400, NoireRemoteErrorCodes.ProtocolMismatch, "This listener serves protocol " + NoireRemotePaths.Protocol + "."),
                context.Instance, requestId);

        if (!context.Routes.TryGetEndpoint(endpointName, out var endpoint) || endpoint == null)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownEndpoint, "This plugin publishes no endpoint named '" + endpointName + "'.", endpointName)
                {
                    Candidates = RemoteArgumentBinder.NearNames(context.Routes.EndpointNames(), endpointName),
                },
                context.Instance, requestId);

        if (!context.Routes.TryGetMember(endpointName, memberName, out var member, out _) || member == null)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.UnknownMember, "Endpoint '" + endpoint.Name + "' has no member named '" + memberName + "'.", endpoint.Name + "/" + memberName)
                {
                    Candidates = RemoteArgumentBinder.NearNames(context.Routes.MemberNames(endpointName), memberName),
                },
                context.Instance, requestId);

        // A member kept off HTTP answers like a missing one. The console is exempt.
        if ((member.Transports & NoireRemoteTransport.Http) == 0 && !request.FromConsole)
            return HttpRouteOutcome.From(
                new HttpFailure(404, NoireRemoteErrorCodes.TransportNotServed, "'" + member.Route + "' is served over the socket only."),
                context.Instance, requestId);

        if (!isLoopback && member.Access != NoireRemoteAccess.Remote)
            return HttpRouteOutcome.From(
                new HttpFailure(403, NoireRemoteErrorCodes.Forbidden, "'" + member.Route + "' is reachable from this machine only."),
                context.Instance, requestId);

        var timeout = ResolveTimeout(body.TimeoutMs, member, context.Options);
        var job = string.Equals(body.Mode, "job", StringComparison.OrdinalIgnoreCase)
            || (body.Mode == null && member.Mode == NoireRemoteCallMode.Job);

        var stopwatch = Stopwatch.StartNew();

        if (job)
        {
            var started = context.Jobs.Start(member.Route, token => InvokeAsync(member, body.Args, context, requestId, token));

            Report(context, member, requestId, true, null, stopwatch.Elapsed, isLoopback);

            return new HttpRouteOutcome
            {
                Status = 202,
                Body = new NoireRemoteEnvelope
                {
                    Ok = true,
                    Instance = context.Instance,
                    Id = requestId,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Job = started.ToStatus(),
                },
            };
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            var result = await InvokeAsync(member, body.Args, context, requestId, deadline.Token).ConfigureAwait(false);

            member.RecordCall(null);
            Report(context, member, requestId, true, null, stopwatch.Elapsed, isLoopback);

            return new HttpRouteOutcome
            {
                Body = new NoireRemoteEnvelope
                {
                    Ok = true,
                    Instance = context.Instance,
                    Id = requestId,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Result = ResultTokenOf(member, result, context),
                },
            };
        }
        catch (HttpBindingException exception)
        {
            Report(context, member, requestId, false, exception.Failure.Code, stopwatch.Elapsed, isLoopback);
            return HttpRouteOutcome.From(exception.Failure, context.Instance, requestId);
        }
        catch (HttpNotReadyException exception)
        {
            Report(context, member, requestId, false, exception.Failure.Code, stopwatch.Elapsed, isLoopback);
            return HttpRouteOutcome.From(exception.Failure, context.Instance, requestId);
        }
        catch (HttpBusyException)
        {
            Report(context, member, requestId, false, NoireRemoteErrorCodes.Busy, stopwatch.Elapsed, isLoopback);
            return HttpRouteOutcome.From(
                new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener is running as many calls as it accepts.") { RetryAfterSeconds = 1 },
                context.Instance, requestId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Report(context, member, requestId, false, NoireRemoteErrorCodes.Timeout, stopwatch.Elapsed, isLoopback);
            return HttpRouteOutcome.From(
                new HttpFailure(408, NoireRemoteErrorCodes.Timeout,
                    "'" + member.Route + "' passed its deadline of " + timeout.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    + " s. It may still be running; start it as a job to poll it instead."),
                context.Instance, requestId);
        }
        catch (Exception exception)
        {
            member.RecordCall(exception);
            Report(context, member, requestId, false, NoireRemoteErrorCodes.HandlerFault, stopwatch.Elapsed, isLoopback);

            context.Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] " + member.Route + " threw on request " + requestId, exception);

            var failure = new HttpFailure(500, NoireRemoteErrorCodes.HandlerFault, exception.Message,
                context.Options.SendExceptionType ? exception.GetType().Name : null);

            var outcome = HttpRouteOutcome.From(failure, context.Instance, requestId);

            if (context.Options.SendStackTraces && outcome.Body is NoireRemoteEnvelope { Error: not null } envelope)
                envelope.Error!.StackTrace = exception.StackTrace;

            return outcome;
        }
    }

    private static async Task<object?> InvokeAsync(NoireRemoteMemberInfo member, JToken? args, HttpRouterContext context, string requestId, CancellationToken cancellationToken)
    {
        var call = new RemoteArgumentBinder.HttpCallContext
        {
            Route = member.Route,
            RequestId = requestId,
            ResolveFile = file => HttpFileRoutes.Resolve(file, context),
            PublishProgress = member.ProgressClrType == null
                ? null
                : (topic, payload) => context.Events.Publish(NoireRemoteChannels.Progress, topic, payload),
        };

        var failure = RemoteArgumentBinder.Bind(member, args, cancellationToken, call, out var values);

        if (failure != null)
            throw new HttpBindingException(failure);

        try
        {
            return await context.Dispatcher.InvokeAsync(member, values, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The coalescing interval would otherwise swallow the final report.
            foreach (var value in values)
            {
                if (value is IRemoteProgressSink sink)
                    sink.Flush();
            }
        }
    }

    private static TimeSpan ResolveTimeout(int? requested, NoireRemoteMemberInfo member, NoireRemoteOptions options)
    {
        var timeout = requested is > 0 ? TimeSpan.FromMilliseconds(requested.Value) : member.Timeout;

        if (timeout <= TimeSpan.Zero)
            timeout = options.DefaultCallTimeout;

        return timeout > options.MaxCallTimeout ? options.MaxCallTimeout : timeout;
    }

    // The bytes never cross inside an envelope.
    private static JToken? ResultTokenOf(NoireRemoteMemberInfo member, object? result, HttpRouterContext context)
    {
        if (member.ReturnClrType == typeof(void) || result == null)
            return null;

        if (result is NoireRemoteFile file)
            HttpFileRoutes.Park(file, context);

        return NoireRemoteJson.ToToken(result);
    }

    public static string NewRequestId()
        => Guid.NewGuid().ToString("N").Substring(0, 8);

    private static HttpRouteOutcome BadMethod(HttpRouterContext context, string? requestId, string accepted)
        => HttpRouteOutcome.From(
            new HttpFailure(405, NoireRemoteErrorCodes.BadMethod, "This route takes " + accepted + "."),
            context.Instance, requestId);

    private static void Report(HttpRouterContext context, NoireRemoteMemberInfo member, string requestId, bool ok, string? code, TimeSpan elapsed, bool isLoopback)
    {
        context.Calls.Record(elapsed, ok);

        var report = new NoireRemoteCallReport(member.Endpoint, member.Name, requestId, ok, code, elapsed, isLoopback);

        RemoteTraffic.Call(context, report, "http");

        if (context.Report == null)
            return;

        try
        {
            context.Report(report);
        }
        catch (Exception exception)
        {
            context.Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] a call report handler threw", exception);
        }
    }
}

// Binding can fail from inside the invocation path.
internal sealed class HttpBindingException(HttpFailure failure) : Exception(failure.Message)
{
    public HttpFailure Failure { get; } = failure;
}
