using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// The console's live channel. It is not published as a socket.
// Nothing on the send or receive path logs. The log channel is pushed over this channel, and logging here would loop.
internal sealed class ConsoleLiveSocket : IDisposable
{
    // The session token is the second offer.
    public const string SubProtocol = "noire-console";

    private static readonly string[] Declared = [SubProtocol];

    private readonly HttpRouterContext context;
    private readonly ConcurrentDictionary<ConsoleLiveSession, byte> sessions = new();

    public ConsoleLiveSocket(HttpRouterContext context)
        => this.context = context;

    // Handed to the page in its bootstrap.
    public static NoireRetryPolicy Reconnect { get; } = NoireRetryPolicy.Default;

    // ConcurrentDictionary.Count takes every bucket's lock.
    public int SessionCount => Volatile.Read(ref sessionCount);

    private int sessionCount;

    // The only handshake field a browser can choose.
    public static string? TokenOf(string? offered)
    {
        if (string.IsNullOrWhiteSpace(offered))
            return null;

        foreach (var part in offered!.Split(','))
        {
            var candidate = part.Trim();

            if (candidate.Length > 0 && !string.Equals(candidate, SubProtocol, StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }

    // Milliseconds, the shape the page reads.
    public static NoireRemoteConsoleRetry Schedule()
        => new()
        {
            InitialDelayMs = (int)Reconnect.InitialDelay.TotalMilliseconds,
            Multiplier = Reconnect.Multiplier,
            MaxDelayMs = (int)Reconnect.MaxDelay.TotalMilliseconds,
            Jitter = Reconnect.Jitter,
        };

    public void Invalidate(string scope)
    {
        foreach (var session in sessions.Keys)
            session.Invalidate(scope);
    }

    public HttpRouteOutcome Route(HttpRequestData request)
    {
        var negotiated = WebsocketHandshake.Negotiate(request, Declared, true);

        if (negotiated.Failure != null)
            return HttpRouteOutcome.From(negotiated.Failure, context.Instance, null);

        return new HttpRouteOutcome
        {
            Adopt = adoption => RunAsync(adoption, negotiated.Accept, negotiated.SubProtocol),
        };
    }

    public void Dispose()
    {
        foreach (var session in sessions.Keys)
            session.Abort();

        sessions.Clear();
    }

    private async Task RunAsync(HttpConnectionAdoption adoption, string accept, string? subProtocol)
    {
        var session = new ConsoleLiveSession(context);
        sessions[session] = 0;
        Interlocked.Increment(ref sessionCount);

        try
        {
            await session.RunAsync(adoption, accept, subProtocol).ConfigureAwait(false);
        }
        finally
        {
            if (sessions.TryRemove(session, out _))
                Interlocked.Decrement(ref sessionCount);
        }
    }
}

// Holds no socket. Every command is testable.
internal sealed class ConsoleLiveChannel : IDisposable
{
    private readonly RemoteEventHub hub;
    private readonly Guid instance;
    private readonly int maxFilterClauses;
    private readonly object gate = new();
    private readonly Dictionary<string, long> cursors = new(StringComparer.OrdinalIgnoreCase);

    private NoireRemoteEventRequest wanted = new();
    private HttpEventQuery query;
    private bool disposed;

    public ConsoleLiveChannel(RemoteEventHub hub, Guid instance, int maxFilterClauses)
    {
        this.hub = hub;
        this.instance = instance;
        this.maxFilterClauses = maxFilterClauses;

        query = HttpEventQuery.From(wanted, maxFilterClauses, out _);
        hub.Subscribe(query, 1);
    }

    public IReadOnlyList<string> Channels
    {
        get
        {
            lock (gate)
                return query.Channels;
        }
    }

    public NoireRemoteConsoleLiveFrame Ready()
        => new()
        {
            Op = NoireRemoteConsoleLiveOps.Ready,
            Instance = instance,
            Channels = Channels,
        };

    // An unknown command gets an explicit refusal.
    public NoireRemoteConsoleLiveFrame Handle(string? text)
    {
        var command = NoireRemoteJson.TryRead<NoireRemoteConsoleLiveCommand>(text);

        if (command == null || string.IsNullOrWhiteSpace(command.Op))
            return Refused(NoireRemoteErrorCodes.BadRequest, "A command on this channel is a JSON object with an op.");

        if (string.Equals(command.Op, NoireRemoteConsoleLiveOps.Subscribe, StringComparison.OrdinalIgnoreCase))
            return Subscribe(command.Subscription ?? new NoireRemoteEventRequest());

        if (string.Equals(command.Op, NoireRemoteConsoleLiveOps.Unsubscribe, StringComparison.OrdinalIgnoreCase))
            return Unsubscribe(command.Channels);

        return Refused(NoireRemoteErrorCodes.BadRequest, "'" + command.Op + "' is not a command this channel reads.");
    }

    // Null when there is nothing to send. Pending completes on the next publish.
    public NoireRemoteConsoleLiveFrame? Drain(out Task pending)
    {
        lock (gate)
        {
            var batch = hub.Drain(query, instance, out pending);

            if (batch.Events.Count == 0 && !batch.Missed)
                return null;

            query.Advance(batch.Cursors);
            Remember(batch.Cursors);

            return new NoireRemoteConsoleLiveFrame
            {
                Op = NoireRemoteConsoleLiveOps.Events,
                Instance = instance,
                Cursors = batch.Cursors,
                Events = batch.Events,
                Missed = batch.Missed,
                MissedChannels = batch.MissedChannels,
            };
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            hub.Subscribe(query, -1);
        }
    }

    private NoireRemoteConsoleLiveFrame Subscribe(NoireRemoteEventRequest request)
    {
        lock (gate)
        {
            // A cursor from the page wins. A channel named without one resumes where the session left it.
            if (request.Cursors is { Count: > 0 })
            {
                foreach (var pair in request.Cursors)
                    cursors[pair.Key] = pair.Value;
            }

            return Rebuild(request);
        }
    }

    // Dropping every channel leaves the events channel.
    private NoireRemoteConsoleLiveFrame Unsubscribe(IReadOnlyList<string>? channels)
    {
        lock (gate)
        {
            if (channels == null || channels.Count == 0)
                return Refused(NoireRemoteErrorCodes.BadRequest, "An unsubscribe names the channels to drop.");

            var kept = new List<string>(query.Channels.Count);

            foreach (var name in query.Channels)
            {
                if (!Names(channels, name))
                    kept.Add(name);
            }

            return Rebuild(new NoireRemoteEventRequest
            {
                Channels = kept,
                Topics = wanted.Topics,
                Filters = wanted.Filters,
                Collapse = wanted.Collapse,
            });
        }
    }

    // A dropped channel must give its subscriber count back. A refused request keeps the old query.
    private NoireRemoteConsoleLiveFrame Rebuild(NoireRemoteEventRequest request)
    {
        request.Cursors = new Dictionary<string, long>(cursors, StringComparer.OrdinalIgnoreCase);

        var built = HttpEventQuery.From(request, maxFilterClauses, out var failure);

        if (failure != null)
            return Refused(failure.Code, failure.Message);

        if (!disposed)
        {
            hub.Subscribe(query, -1);
            hub.Subscribe(built, 1);

            wanted = request;
            query = built;
        }

        return new NoireRemoteConsoleLiveFrame
        {
            Op = NoireRemoteConsoleLiveOps.Subscribed,
            Instance = instance,
            Channels = built.Channels,
            Topics = built.Topics,
        };
    }

    private void Remember(IReadOnlyDictionary<string, long>? written)
    {
        if (written == null)
            return;

        foreach (var pair in written)
            cursors[pair.Key] = pair.Value;
    }

    private NoireRemoteConsoleLiveFrame Refused(string code, string message)
        => new()
        {
            Op = NoireRemoteConsoleLiveOps.Error,
            Instance = instance,
            Error = new NoireRemoteError { Code = code, Message = message },
        };

    private static bool Names(IReadOnlyList<string> names, string candidate)
    {
        foreach (var name in names)
        {
            if (string.Equals(name?.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

// Nothing here logs. See ConsoleLiveSocket.
internal sealed class ConsoleLiveSession
{
    private const int ReadChunk = 4096;

    // Smaller than what a published socket allows.
    private static readonly WebsocketCodecLimits Limits = new(64 * 1024, 256 * 1024, 32);

    private readonly HttpRouterContext context;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly CancellationTokenSource life = new();

    private Stream stream = Stream.Null;
    private ConsoleSocketRelay? relay;
    private ConsoleFleetTail? fleet;

    public ConsoleLiveSession(HttpRouterContext context)
        => this.context = context;

    public async Task RunAsync(HttpConnectionAdoption adoption, string accept, string? subProtocol)
    {
        var hub = context.Events;

        // A tab holds one of MaxEventStreams for as long as it is open.
        if (!hub.TryHold())
        {
            await RefuseAsync(adoption).ConfigureAwait(false);
            return;
        }

        stream = adoption.Stream;

        var codec = new WebsocketCodec(Limits);
        ConsoleLiveChannel? channel = null;
        Task? pushing = null;

        try
        {
            // Claimed before the 101. A refusal stays an ordinary HTTP answer.
            await HttpResponseWriter.WriteUpgradeAsync(stream, accept, subProtocol, context.Instance, CancellationToken.None).ConfigureAwait(false);

            channel = new ConsoleLiveChannel(hub, context.Instance, context.Options.MaxFilterClauses);

            await SendAsync(channel.Ready()).ConfigureAwait(false);

            pushing = Task.Run(() => PushAsync(channel), CancellationToken.None);

            await ReadAsync(codec, channel, adoption.Carried).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            Abort();

            if (pushing != null)
                await AwaitQuietlyAsync(pushing).ConfigureAwait(false);

            channel?.Dispose();
            hub.Release();
            codec.Dispose();

            try
            {
                adoption.Client.Dispose();
            }
            catch (Exception)
            {
            }

            life.Dispose();
            writeGate.Dispose();
        }
    }

    // A page that has gone away is noticed when the write fails.
    public void Invalidate(string scope)
        => _ = InvalidateAsync(scope);

    private async Task InvalidateAsync(string scope)
    {
        try
        {
            await SendAsync(new NoireRemoteConsoleLiveFrame
            {
                Op = NoireRemoteConsoleLiveOps.Invalidate,
                Instance = context.Instance,
                Scope = scope,
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    public void Abort()
    {
        // Sockets opened by the page exist only for that session.
        relay?.Dispose();
        relay = null;
        fleet?.Dispose();
        fleet = null;

        try
        {
            life.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Cancelling alone does not always unblock a read already inside the socket.
        try
        {
            stream.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private async Task ReadAsync(WebsocketCodec codec, ConsoleLiveChannel channel, ReadOnlyMemory<byte> carried)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadChunk);

        try
        {
            // The bytes after the header block are the peer's first frame.
            if (!carried.IsEmpty)
            {
                codec.Deliver(carried.Span);

                if (!await DispatchAsync(codec, channel).ConfigureAwait(false))
                    return;
            }

            while (!life.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, life.Token).ConfigureAwait(false);

                if (read <= 0)
                    return;

                codec.Deliver(buffer.AsSpan(0, read));

                if (!await DispatchAsync(codec, channel).ConfigureAwait(false))
                    return;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // False once the connection has ended.
    private async Task<bool> DispatchAsync(WebsocketCodec codec, ConsoleLiveChannel channel)
    {
        while (codec.TryRead(out var result))
        {
            switch (result.Event)
            {
                case WebsocketCodecEvent.Message:
                    await AnswerAsync(channel, result).ConfigureAwait(false);
                    break;

                case WebsocketCodecEvent.Ping:
                    await WriteAsync(WebsocketFrameWriter.Pong(result.Payload.Span)).ConfigureAwait(false);
                    break;

                case WebsocketCodecEvent.Pong:
                    break;

                case WebsocketCodecEvent.Close:
                    await WriteAsync(WebsocketFrameWriter.Close(result.Code, result.Reason)).ConfigureAwait(false);
                    return false;

                case WebsocketCodecEvent.Failure:
                    await WriteAsync(WebsocketFrameWriter.Close(result.Code, result.Reason)).ConfigureAwait(false);
                    return false;
            }
        }

        return true;
    }

    private Task AnswerAsync(ConsoleLiveChannel channel, WebsocketCodecResult result)
    {
        if (result.MessageKind != NoireWebsocketMessageKind.Text)
        {
            return SendAsync(new NoireRemoteConsoleLiveFrame
            {
                Op = NoireRemoteConsoleLiveOps.Error,
                Instance = context.Instance,
                Error = new NoireRemoteError
                {
                    Code = NoireRemoteErrorCodes.BadRequest,
                    Message = "This channel only accepts JSON text.",
                },
            });
        }

        var text = Encoding.UTF8.GetString(result.Payload.Span);
        var command = NoireRemoteJson.TryRead<NoireRemoteConsoleLiveCommand>(text);

        // The relay holds the connection and pushes what arrives back over this channel.
        if (command != null && command.Op != null && command.Op.StartsWith("socket.", StringComparison.OrdinalIgnoreCase))
            return SocketAsync(command);

        if (command != null && string.Equals(command.Op, NoireRemoteConsoleLiveOps.FleetWatch, StringComparison.OrdinalIgnoreCase))
        {
            fleet ??= new ConsoleFleetTail(context, frame => SendAsync(frame));
            fleet.Watch(command.Instances ?? []);

            return Task.CompletedTask;
        }

        return SendAsync(channel.Handle(text));
    }

    private async Task SocketAsync(NoireRemoteConsoleLiveCommand command)
    {
        relay ??= new ConsoleSocketRelay(context, frame => SendAsync(frame));

        await SendAsync(await relay.HandleAsync(command).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task PushAsync(ConsoleLiveChannel channel)
    {
        var keepAlive = context.Options.StreamKeepAlive <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : context.Options.StreamKeepAlive;

        try
        {
            while (!life.IsCancellationRequested)
            {
                var frame = channel.Drain(out var pending);

                if (frame != null)
                {
                    await SendAsync(frame).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await pending.WaitAsync(keepAlive, life.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Detects a peer that vanished without closing.
                    await WriteAsync(WebsocketFrameWriter.Ping(ReadOnlySpan<byte>.Empty)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            Abort();
        }
    }

    private Task SendAsync(NoireRemoteConsoleLiveFrame frame)
        => WriteAsync(WebsocketFrameWriter.Message(NoireWebsocketMessageKind.Text, NoireRemoteJson.WriteBytes(frame)));

    // Both loops write. A pong must not cut a batch in half.
    private async Task WriteAsync(byte[] frame)
    {
        await writeGate.WaitAsync(life.Token).ConfigureAwait(false);

        try
        {
            await stream.WriteAsync(frame, life.Token).ConfigureAwait(false);
            await stream.FlushAsync(life.Token).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task RefuseAsync(HttpConnectionAdoption adoption)
    {
        try
        {
            await HttpResponseWriter.WriteFailureAsync(
                adoption.Stream,
                new HttpFailure(503, NoireRemoteErrorCodes.Busy, "This listener holds as many event streams as it accepts.") { RetryAfterSeconds = 1 },
                context.Instance,
                null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            adoption.Client.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static async Task AwaitQuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }
}
