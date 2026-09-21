using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// Server-sent events: the topic is the event type and the arguments its data. For a caller that cannot hold a socket.
internal static class HttpEventStream
{
    public static async Task RunAsync(Stream stream, HttpRouterContext context, HttpEventQuery query, TimeSpan minInterval, CancellationToken cancellationToken)
    {
        var hub = context.Events;

        if (!hub.TryHold())
        {
            await HttpResponseWriter.WriteFailureAsync(
                stream,
                new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many event streams as it accepts.") { RetryAfterSeconds = 1 },
                context.Instance,
                null,
                cancellationToken).ConfigureAwait(false);

            return;
        }

        hub.Subscribe(query, 1);

        try
        {
            await HttpResponseWriter.BeginStreamAsync(stream, NoireRemoteHeaders.EventStreamContentType, context.Instance, cancellationToken).ConfigureAwait(false);

            var keepAlive = context.Options.StreamKeepAlive <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : context.Options.StreamKeepAlive;
            var lastWrite = DateTime.UtcNow;

            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = hub.Drain(query, context.Instance, out var pending);

                if (batch.Events.Count > 0 || batch.Missed)
                {
                    query.Advance(batch.Cursors);

                    foreach (var item in batch.Events)
                        await HttpEventStreamWriter.WriteAsync(stream, item.Topic, item.Seq, item, cancellationToken).ConfigureAwait(false);

                    if (batch.Missed)
                        await HttpEventStreamWriter.WriteAsync(stream, "_missed", 0, new { missed = true, channels = batch.MissedChannels }, cancellationToken).ConfigureAwait(false);

                    lastWrite = DateTime.UtcNow;

                    if (minInterval > TimeSpan.Zero)
                        await Task.Delay(minInterval, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                var wait = keepAlive - (DateTime.UtcNow - lastWrite);

                if (wait <= TimeSpan.Zero)
                {
                    // The SSE keep-alive comment. It also notices a peer that vanished without closing.
                    await stream.WriteAsync(HttpEventStreamWriter.KeepAlive, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    lastWrite = DateTime.UtcNow;
                    continue;
                }

                try
                {
                    await pending.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            hub.Subscribe(query, -1);
            hub.Release();
        }
    }
}
