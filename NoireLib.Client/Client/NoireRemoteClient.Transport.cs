using Newtonsoft.Json.Linq;
using NoireLib.Websocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote;

// HTTP for a plain call. A socket only when something must be received or a call asks for one. Never more than one connection.
public sealed partial class NoireRemoteClient
{
    internal async Task<NoireRemoteEnvelope> CallOverHttpAsync(
        NoireRemoteInstance instance,
        string member,
        object? args,
        CancellationToken cancellationToken,
        string? mode = null)
    {
        var api = new NoireRemoteApi(Api, instance);

        return await api.PostToAsync(instance, member, args, mode, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<NoireRemoteFrame> CallOverSocketAsync(
        NoireRemoteInstance instance,
        string member,
        object? args,
        CancellationToken cancellationToken)
    {
        var client = await EnsureSocketAsync(instance, cancellationToken).ConfigureAwait(false);
        var id = Interlocked.Increment(ref nextId);
        var answer = new TaskCompletionSource<NoireRemoteFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        pending[id] = answer;

        try
        {
            var frame = new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Call,
                Id = id,
                Api = Api,
                Member = member,
                Payload = args == null ? null : NoireRemoteJson.ToToken(args),
            };

            await client.SendAsync(frame.Write(), cancellationToken).ConfigureAwait(false);

            using var registration = cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));

            return await answer.Task.ConfigureAwait(false);
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    internal Task<NoireWebsocketClient> EnsureSocketAsync(NoireRemoteTarget target, TimeSpan wait, CancellationToken cancellationToken)
        => EnsureSocketAsync(Resolve(target, wait), cancellationToken);

    internal async Task<NoireWebsocketClient> EnsureSocketAsync(NoireRemoteInstance instance, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var held = socket;

        if (held != null && held.State == NoireSocketState.Connected && connectedTo?.Id == instance.Id)
            return held;

        await socketGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            held = socket;

            if (held != null && held.State == NoireSocketState.Connected && connectedTo?.Id == instance.Id)
                return held;

            // A restarted game invalidates the old credential.
            held?.Dispose();

            var options = new NoireWebsocketClientOptions
            {
                // Rebuild owns reconnection.
                Retry = NoireRetryPolicy.None,
            };

            options.Http.Headers[NoireRemoteHeaders.Authorization] = NoireRemoteHeaders.BearerScheme + " " + instance.Token;

            var created = new NoireWebsocketClient(SocketUrl(instance), options);
            created.OnMessage(message => Receive(message.Text));
            created.OnClose((_) => Rebuild());

            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connecting.CancelAfter(Options.ConnectTimeout);

            await created.ConnectAsync(connecting.Token).ConfigureAwait(false);

            socket = created;
            connectedTo = instance;
            lastTarget = instance;

            await ReplayAsync(cancellationToken).ConfigureAwait(false);

            return created;
        }
        finally
        {
            socketGate.Release();
        }
    }

    // Replayed before the new connection is used.
    private async Task ReplayAsync(CancellationToken cancellationToken)
    {
        string[] prefixes;

        lock (subscriptions)
            prefixes = [.. subscriptions.Select(subscription => subscription.Prefix).Distinct()];

        foreach (var prefix in prefixes)
            await SendSubscribeAsync(prefix, cancellationToken).ConfigureAwait(false);

        await ReattachAsync(cancellationToken).ConfigureAwait(false);
    }

    // The game restarted, the plugin reloaded, or the network dropped.
    private void Rebuild()
    {
        if (disposed || rebuilding)
            return;

        bool wanted;

        lock (subscriptions)
            wanted = subscriptions.Count > 0 || Transport == NoireRemoteTransport.Websocket;

        if (!wanted)
            return;

        rebuilding = true;

        _ = Task.Run(async () =>
        {
            var delay = TimeSpan.FromMilliseconds(250);

            try
            {
                while (!disposed)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));

                    try
                    {
                        // A restarted game gets a new port, credential and instance id. Only the name or tag still matches.
                        var instance = Resolve(lastAim, TimeSpan.Zero);

                        socket = null;
                        connectedTo = null;

                        await EnsureSocketAsync(instance, CancellationToken.None).ConfigureAwait(false);

                        return;
                    }
                    catch (Exception)
                    {
                        // Nothing listens yet. The next attempt waits longer, up to five seconds.
                    }
                }
            }
            finally
            {
                rebuilding = false;
            }
        });
    }

    // Followed again on a new connection. A dropped socket does not lose an answer the listener still holds.
    internal void Follow(NoireRemoteJob job)
    {
        job.IsPushed = true;
        following[job.Id] = job;
    }

    private async Task ReattachAsync(CancellationToken cancellationToken)
    {
        foreach (var id in following.Keys)
        {
            var held = socket;

            if (held == null || held.State != NoireSocketState.Connected)
                return;

            await held.SendAsync(new NoireRemoteFrame
            {
                Kind = NoireRemoteFrameKind.Progress,
                Id = Interlocked.Increment(ref nextId),
                Job = id,
            }.Write(), cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task SendSubscribeAsync(string prefix, CancellationToken cancellationToken)
    {
        var held = socket;

        if (held == null || held.State != NoireSocketState.Connected)
            return;

        await held.SendAsync(new NoireRemoteFrame
        {
            Kind = NoireRemoteFrameKind.Subscribe,
            Id = Interlocked.Increment(ref nextId),
            Member = prefix,
        }.Write(), cancellationToken).ConfigureAwait(false);
    }

    internal async Task SendUnsubscribeAsync(string prefix, CancellationToken cancellationToken)
    {
        var held = socket;

        if (held == null || held.State != NoireSocketState.Connected)
            return;

        await held.SendAsync(new NoireRemoteFrame
        {
            Kind = NoireRemoteFrameKind.Unsubscribe,
            Id = Interlocked.Increment(ref nextId),
            Member = prefix,
        }.Write(), cancellationToken).ConfigureAwait(false);
    }

    private static string SocketUrl(NoireRemoteInstance instance)
        => "ws://" + instance.Address + ":" + instance.Port + NoireRemotePaths.ApiSocketRoute;

    private void Receive(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        NoireRemoteFrame frame;

        try
        {
            frame = NoireRemoteFrame.Parse(text!);
        }
        catch (NoireRemoteProtocolException)
        {
            return;
        }

        switch (frame.Kind)
        {
            case NoireRemoteFrameKind.Result:
                if (frame.Id != 0 && pending.TryRemove(frame.Id, out var answer))
                {
                    answer.TrySetResult(frame);
                    return;
                }

                // Id zero with a job set means the job finished unprompted.
                if (frame.Job != null && following.TryRemove(frame.Job, out var job))
                {
                    job.Complete(new NoireRemoteEnvelope
                    {
                        Ok = frame.Error == null,
                        Result = frame.Payload,
                        Error = frame.Error,
                        Job = new NoireRemoteJobStatus
                        {
                            Id = frame.Job,
                            State = frame.Error == null ? NoireRemoteJobState.Done : NoireRemoteJobState.Failed,
                        },
                    });
                }

                return;

            case NoireRemoteFrameKind.Event:
                var topic = frame.Member ?? string.Empty;

                if (topic.Equals("_instance.metadata", StringComparison.OrdinalIgnoreCase) && connectedTo != null)
                {
                    var tags = frame.Payload?.ToObject<Dictionary<string, string>>();

                    if (tags != null)
                        connectedTo.Metadata = tags;
                }

                Deliver(topic, frame.Payload);
                return;

            default:
                return;
        }
    }

    private void Deliver(string topic, JToken? payload)
    {
        Subscription[] held;

        lock (subscriptions)
            held = [.. subscriptions];

        foreach (var subscription in held)
            subscription.Deliver(topic, payload);
    }

    private static void Warn(string message)
    {
        var sink = WarningSink;

        if (sink != null)
        {
            sink(message);
            return;
        }

        System.Diagnostics.Debug.WriteLine("[NoireRemote] " + message);
        System.Console.Error.WriteLine("[NoireRemote] " + message);
    }

    /// <summary>
    /// Gets or sets where a caller-side warning goes. A program with a logger of its own points this at it. With
    /// nothing set, a warning reaches the standard error stream and the debugger.
    /// </summary>
    public static Action<string>? WarningSink { get; set; }
}
