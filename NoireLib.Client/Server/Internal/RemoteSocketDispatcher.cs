using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Resolves and invokes exactly what the HTTP router does, through the same dispatcher.
// Never throws at the caller. Every failure is a Result carrying an error, and the connection stays open.
internal sealed class RemoteSocketDispatcher
{
    private readonly HttpRouterContext context;

    internal RemoteSocketDispatcher(HttpRouterContext context)
    {
        this.context = context;
    }

    internal RemoteSubscriptionTable Subscriptions { get; } = new();

    // Progress and the frame that ends a job.
    internal Action<Guid, NoireRemoteFrame>? Push { get; set; }

    internal async Task<NoireRemoteFrame?> HandleAsync(Guid connection, NoireRemoteFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Kind)
        {
            case NoireRemoteFrameKind.Progress:
                return Attach(connection, frame);

            case NoireRemoteFrameKind.Ping:
                return new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Pong, Id = frame.Id };

            case NoireRemoteFrameKind.Subscribe:
                Subscriptions.Add(connection, frame.Member);

                // A reconnecting client waits for this before declaring itself connected.
                return new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Result, Id = frame.Id, Member = frame.Member };

            case NoireRemoteFrameKind.Unsubscribe:
                Subscriptions.Remove(connection, frame.Member);
                return new NoireRemoteFrame { Kind = NoireRemoteFrameKind.Result, Id = frame.Id, Member = frame.Member };

            case NoireRemoteFrameKind.Call:
                return await CallAsync(connection, frame, cancellationToken).ConfigureAwait(false);

            default:
                return Failed(frame.Id, NoireRemoteErrorCodes.UnknownFrameKind,
                    "'" + frame.Kind.ToString().ToLowerInvariant() + "' is not a frame this listener answers.");
        }
    }

    internal NoireRemoteFrame Malformed(Exception exception)
    {
        var code = exception is NoireRemoteProtocolException protocol && protocol.Code != null
            ? protocol.Code
            : NoireRemoteErrorCodes.FrameMalformed;

        return Failed(0, code, exception.Message);
    }

    // Lands on the traffic channel like an HTTP call.
    private async Task<NoireRemoteFrame> CallAsync(Guid connection, NoireRemoteFrame frame, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var answer = await CallCoreAsync(connection, frame, cancellationToken).ConfigureAwait(false);

        RemoteTraffic.Call(
            context,
            new NoireRemoteCallReport(
                answer.Api ?? frame.Api ?? string.Empty,
                answer.Member ?? frame.Member ?? string.Empty,
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                answer.Error == null,
                answer.Error?.Code,
                started.Elapsed,
                true),
            "ws");

        return answer;
    }

    private async Task<NoireRemoteFrame> CallCoreAsync(Guid connection, NoireRemoteFrame frame, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frame.Api) || string.IsNullOrWhiteSpace(frame.Member))
            return Failed(frame.Id, NoireRemoteErrorCodes.BadRequest, "A call frame names an api and a member.");

        if (!context.Routes.TryGetMember(frame.Api!, frame.Member!, out var member, out _) || member == null)
        {
            return Failed(frame.Id, NoireRemoteErrorCodes.UnknownMember,
                "This listener publishes no member '" + frame.Api + "/" + frame.Member + "'.");
        }

        if ((member.Transports & NoireRemoteTransport.Websocket) == 0)
        {
            return Failed(frame.Id, NoireRemoteErrorCodes.TransportNotServed,
                "'" + member.Route + "' is served over HTTP only.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(member.Timeout);

        var call = new RemoteArgumentBinder.HttpCallContext
        {
            Route = member.Route,
            RequestId = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ResolveFile = file => HttpFileRoutes.Resolve(file, context),
            PublishProgress = member.ProgressClrType == null
                ? null
                : (topic, payload) => context.Events.Publish(NoireRemoteChannels.Progress, topic, payload),
        };

        var failure = RemoteArgumentBinder.Bind(member, frame.Payload, deadline.Token, call, out var values);

        if (failure != null)
            return Failed(frame.Id, failure.Code, failure.Message);

        // Answers the job id at once. Progress arrives on the progress topic.
        if (member.Mode == NoireRemoteCallMode.Job)
        {
            var started = context.Jobs.Start(member.Route, token => context.Dispatcher.InvokeAsync(member, values, token));

            Follow(connection, started);

            return new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Result,
                Id = frame.Id,
                Api = member.Endpoint,
                Member = member.Name,
                Job = started.Id,
                Payload = NoireRemoteJson.ToToken(started.ToStatus()),
            };
        }

        try
        {
            var result = await context.Dispatcher.InvokeAsync(member, values, deadline.Token).ConfigureAwait(false);

            return new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Result,
                Id = frame.Id,
                Api = member.Endpoint,
                Member = member.Name,
                Payload = result == null ? null : NoireRemoteJson.ToToken(result),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(frame.Id, NoireRemoteErrorCodes.Timeout, "'" + member.Route + "' ran past its deadline.");
        }
        catch (Exception exception)
        {
            return Failed(frame.Id, NoireRemoteErrorCodes.HandlerFault, exception.Message);
        }
        finally
        {
            foreach (var value in values)
            {
                if (value is IRemoteProgressSink sink)
                    sink.Flush();
            }
        }
    }

    // An unknown id answers Unknown.
    private NoireRemoteFrame Attach(Guid connection, NoireRemoteFrame frame)
    {
        var job = string.IsNullOrWhiteSpace(frame.Job) ? null : context.Jobs.Get(frame.Job!);

        if (job == null)
        {
            return new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Result,
                Id = frame.Id,
                Job = frame.Job,
                Payload = NoireRemoteJson.ToToken(new NoireRemoteJobStatus
                {
                    Id = frame.Job ?? string.Empty,
                    State = NoireRemoteJobState.Unknown,
                }),
            };
        }

        Follow(connection, job);

        return new NoireRemoteFrame
        {
            Kind = NoireRemoteFrameKind.Result,
            Id = frame.Id,
            Job = job.Id,
            Payload = NoireRemoteJson.ToToken(job.ToStatus()),
        };
    }

    // A job finished before the caller asked is answered immediately.
    private void Follow(Guid connection, RemoteJobStore.HttpJob job)
    {
        void Send(RemoteJobStore.HttpJob finished)
            => Push?.Invoke(connection, new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Result,
                Id = 0,
                Job = finished.Id,
                Payload = finished.Result,
                Error = finished.Error,
            });

        if (job.FinishedAtUtc != null)
        {
            Send(job);
            return;
        }

        job.Finished += Send;
    }

    private static NoireRemoteFrame Failed(long id, string? code, string message)
        => new()
        {
            Kind = NoireRemoteFrameKind.Result,
            Id = id,
            Error = new NoireRemoteError { Code = code ?? NoireRemoteErrorCodes.BadRequest, Message = message },
        };
}
