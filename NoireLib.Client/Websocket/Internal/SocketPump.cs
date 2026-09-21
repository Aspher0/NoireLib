using NoireLib.Remote;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// One drain takes everything queued and runs it in order inside a single hop. An open callback comes before the first message, and an async handler is awaited before the next item.
internal sealed class SocketPump : IDisposable
{
    private readonly ConcurrentQueue<Func<Task>> queue = new();
    private readonly Func<INoireRemoteHost> host;
    private readonly Func<NoireRemoteThread> thread;
    private readonly Action<Exception> report;

    private int draining;
    private int disposed;

    public SocketPump(Func<INoireRemoteHost> host, Func<NoireRemoteThread> thread, Action<Exception> report)
    {
        this.host = host;
        this.thread = thread;
        this.report = report;
    }

    public int Queued => queue.Count;

    public void Enqueue(Func<Task> work)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        queue.Enqueue(work);
        Schedule();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        while (queue.TryDequeue(out _))
        {
        }
    }

    private void Schedule()
    {
        if (Interlocked.CompareExchange(ref draining, 1, 0) != 0)
            return;

        _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        try
        {
            while (!queue.IsEmpty && Volatile.Read(ref disposed) == 0)
            {
                var current = host();

                // A host can come up mid-session.
                if (Resolve(current) == NoireRemoteThread.Framework && current.HasHostThread)
                    await current.RunOnHostThreadAsync(RunBatchAsync).ConfigureAwait(false);
                else
                    await RunBatchAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            report(exception);
        }
        finally
        {
            Volatile.Write(ref draining, 0);

            // An item enqueued between the emptiness check and clearing the flag would wait for the next message.
            if (!queue.IsEmpty && Volatile.Read(ref disposed) == 0)
                Schedule();
        }
    }

    private async Task<bool> RunBatchAsync()
    {
        // Items a handler enqueues wait for the next batch.
        var budget = queue.Count;

        while (budget-- > 0 && Volatile.Read(ref disposed) == 0 && queue.TryDequeue(out var work))
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                report(exception);
            }
        }

        return true;
    }

    private NoireRemoteThread Resolve(INoireRemoteHost current)
    {
        var declared = thread();

        if (declared != NoireRemoteThread.Inherit)
            return declared;

        var fallback = current.DefaultThread;
        return fallback == NoireRemoteThread.Inherit ? NoireRemoteThread.Framework : fallback;
    }
}
