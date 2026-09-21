using NoireLib.Remote;
using NoireLib.Remote.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// For each watched listener, its event stream is read with the credential from its record and forwarded tagged.
// A listener is tailed only while it allows console control, checked on every reconnect.
internal sealed class ConsoleFleetTail : IDisposable
{
    private const string Channels = NoireRemoteChannels.Events + "," + NoireRemoteChannels.Traffic + ","
        + NoireRemoteChannels.Metrics + "," + NoireRemoteChannels.Log;

    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    private readonly HttpRouterContext context;
    private readonly Func<NoireRemoteConsoleLiveFrame, Task> send;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> watched = new();
    private int disposed;

    public ConsoleFleetTail(HttpRouterContext context, Func<NoireRemoteConsoleLiveFrame, Task> send)
    {
        this.context = context;
        this.send = send;
    }

    // A listener no longer named is let go.
    public void Watch(IReadOnlyList<Guid> instances)
    {
        var wanted = new HashSet<Guid>(instances);

        wanted.Remove(context.Instance);

        foreach (var pair in watched)
        {
            if (!wanted.Contains(pair.Key) && watched.TryRemove(pair.Key, out var stop))
            {
                stop.Cancel();
                stop.Dispose();
            }
        }

        foreach (var id in wanted)
        {
            var stop = new CancellationTokenSource();

            if (watched.TryAdd(id, stop))
                _ = TailAsync(id, stop.Token);
            else
                stop.Dispose();
        }
    }

    private async Task TailAsync(Guid id, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var instance = await HttpConsoleFleet.FindAsync(id, context, cancellationToken).ConfigureAwait(false);

                if (instance != null)
                {
                    await foreach (var published in new NoireRemoteApi("_fleet", instance).StreamAsync(Channels, null, cancellationToken).ConfigureAwait(false))
                    {
                        await send(new NoireRemoteConsoleLiveFrame
                        {
                            Op = NoireRemoteConsoleLiveOps.FleetEvents,
                            Instance = context.Instance,
                            Source = id,
                            Events = [published],
                        }).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Retried after the pause, when consent is asked again.
            }

            try
            {
                await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        foreach (var pair in watched)
        {
            pair.Value.Cancel();
            pair.Value.Dispose();
        }

        watched.Clear();
    }
}
