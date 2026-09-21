using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// Each handler runs in its own try. A multicast delegate would let one throw stop the rest and reach the read loop.
internal sealed class SocketHandlerSet<TContext>
{
    private readonly object gate = new();
    private readonly List<Entry> entries = [];
    private readonly Action<Exception> report;

    private long nextId;

    public SocketHandlerSet(Action<Exception> report)
        => this.report = report;

    public bool HasHandlers
    {
        get
        {
            lock (gate)
                return entries.Count > 0;
        }
    }

    public NoireSocketSubscription Add(Func<TContext, Task> handler, NoireSocketSubscribeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var entry = new Entry
        {
            Id = Interlocked.Increment(ref nextId),
            Handler = handler,
            Key = options?.Key,
            Once = options?.Once ?? false,
            Owner = options?.Owner,
        };

        lock (gate)
        {
            if (entry.Key != null)
                entries.RemoveAll(e => string.Equals(e.Key, entry.Key, StringComparison.Ordinal));

            entries.Add(entry);
        }

        return new NoireSocketSubscription(() => Remove(entry.Id));
    }

    public void Remove(long id)
    {
        lock (gate)
            entries.RemoveAll(e => e.Id == id);
    }

    public void RemoveOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (gate)
            entries.RemoveAll(e => ReferenceEquals(e.Owner, owner));
    }

    public void Clear()
    {
        lock (gate)
            entries.Clear();
    }

    public async Task DispatchAsync(TContext context)
    {
        Entry[] snapshot;

        lock (gate)
        {
            if (entries.Count == 0)
                return;

            snapshot = entries.ToArray();
        }

        foreach (var entry in snapshot)
        {
            // Claimed before the handler runs. A throwing one-shot is still spent.
            if (entry.Once && Interlocked.Exchange(ref entry.Claimed, 1) != 0)
                continue;

            if (entry.Once)
                Remove(entry.Id);

            try
            {
                await entry.Handler(context).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                report(exception);
            }
        }
    }

    private sealed class Entry
    {
        public long Id;
        public Func<TContext, Task> Handler = null!;
        public string? Key;
        public bool Once;
        public object? Owner;
        public int Claimed;
    }
}
