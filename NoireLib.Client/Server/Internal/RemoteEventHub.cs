using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// One bounded ring per channel. Metrics and a log tail would evict a plugin's own events from a shared ring within minutes.
internal sealed class RemoteEventHub : IDisposable
{
    private readonly object gate = new();

    // Asking whether a channel is watched costs no lock. The traffic feed asks on every call and frame. Buffer mutations stay under the gate.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Channel> channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly int defaultCapacity;
    private readonly int maxWaiters;
    private TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int waiters;

    public RemoteEventHub(NoireRemoteOptions options)
    {
        defaultCapacity = Math.Max(1, options.EventBufferSize);
        maxWaiters = Math.Max(1, options.MaxEventStreams);

        Declare(NoireRemoteChannels.Events, defaultCapacity);
        Declare(NoireRemoteChannels.Progress, Math.Max(1, options.ProgressBufferSize));
        Declare(NoireRemoteChannels.Log, Math.Max(1, options.LogBufferSize));
        Declare(NoireRemoteChannels.Metrics, Math.Max(1, options.MetricsBufferSize));
        Declare(NoireRemoteChannels.Traffic, Math.Max(1, options.TrafficBufferSize));
    }

    private sealed class Channel(string name, int capacity)
    {
        public readonly string Name = name;
        public readonly int Capacity = capacity;
        public readonly LinkedList<NoireRemoteEvent> Buffer = new();
        public long Sequence;
        public long Oldest = 1;

        // Written under the gate, read without it.
        public volatile int Subscribers;
    }

    public IReadOnlyList<NoireRemoteManifestChannel> Describe()
    {
        lock (gate)
        {
            var list = new List<NoireRemoteManifestChannel>(channels.Count);

            foreach (var channel in channels.Values)
                list.Add(new NoireRemoteManifestChannel { Name = channel.Name, BufferSize = channel.Capacity });

            list.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            return list;
        }
    }

    public void Declare(string name, int bufferSize)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A channel needs a name.", nameof(name));

        channels.TryAdd(name, new Channel(name, Math.Max(1, bufferSize)));
    }

    // The metrics sampler reads it.
    public bool IsWatched(string name)
        => channels.TryGetValue(name, out var channel) && channel.Subscribers > 0;

    public void Publish(string topic, object? payload)
        => Publish(NoireRemoteChannels.Events, topic, payload);

    public void Publish(string channelName, string topic, object? payload)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("An event needs a topic.", nameof(topic));

        TaskCompletionSource toSignal;

        // Serialized outside the lock. A metrics or log channel publishes at rate.
        var data = payload == null ? null : NoireRemoteJson.ToToken(payload);

        lock (gate)
        {
            var channel = channels.GetOrAdd(channelName, name => new Channel(name, defaultCapacity));

            channel.Sequence++;

            channel.Buffer.AddLast(new NoireRemoteEvent
            {
                Seq = channel.Sequence,
                Channel = channel.Name,
                Topic = topic,
                AtUtc = DateTimeOffset.UtcNow,
                Data = data,
            });

            while (channel.Buffer.Count > channel.Capacity)
            {
                channel.Oldest = channel.Buffer.First!.Value.Seq + 1;
                channel.Buffer.RemoveFirst();
            }

            toSignal = signal;
            signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        toSignal.TrySetResult();

        // Traffic is only sent when named. A socket call generates traffic that is itself traffic.
        if (!string.Equals(channelName, NoireRemoteChannels.Traffic, StringComparison.OrdinalIgnoreCase))
            SocketPush?.Invoke(topic, data);
    }

    // Null while no API socket is published.
    public Action<string, Newtonsoft.Json.Linq.JToken?>? SocketPush { get; set; }

    // A held poll and a held stream share one budget.
    public bool TryHold()
    {
        if (Interlocked.Increment(ref waiters) <= maxWaiters)
            return true;

        Interlocked.Decrement(ref waiters);

        return false;
    }

    public void Release()
        => Interlocked.Decrement(ref waiters);

    public async Task<NoireRemoteEventBatch> PollAsync(HttpEventQuery query, TimeSpan wait, Guid instance, CancellationToken cancellationToken)
    {
        if (!TryHold())
            throw new HttpBusyException();

        Subscribe(query, 1);

        try
        {
            var deadline = DateTime.UtcNow + wait;

            while (true)
            {
                Task pending;
                NoireRemoteEventBatch batch;

                lock (gate)
                {
                    batch = Collect(query, instance, out var candidates);
                    pending = signal.Task;

                    if (candidates > 0)
                        return Filter(batch, query, instance);
                }

                var remaining = deadline - DateTime.UtcNow;

                if (remaining <= TimeSpan.Zero)
                    return Filter(batch, query, instance);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(remaining);

                try
                {
                    await pending.WaitAsync(remaining, timeout.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    lock (gate)
                        batch = Collect(query, instance, out _);

                    return Filter(batch, query, instance);
                }
                catch (OperationCanceledException)
                {
                    lock (gate)
                        batch = Collect(query, instance, out _);

                    return Filter(batch, query, instance);
                }
            }
        }
        finally
        {
            Subscribe(query, -1);
            Release();
        }
    }

    // Pending completes when anything is published.
    public NoireRemoteEventBatch Drain(HttpEventQuery query, Guid instance, out Task pending)
    {
        NoireRemoteEventBatch batch;

        lock (gate)
        {
            batch = Collect(query, instance, out _);
            pending = signal.Task;
        }

        return Filter(batch, query, instance);
    }

    public void Subscribe(HttpEventQuery query, int delta)
    {
        lock (gate)
        {
            foreach (var name in query.Channels)
            {
                if (channels.TryGetValue(name, out var channel))
                    channel.Subscribers += delta;
            }
        }
    }

    // The caller's filter runs after the lock is released.
    private NoireRemoteEventBatch Collect(HttpEventQuery query, Guid instance, out int candidates)
    {
        var events = new List<NoireRemoteEvent>();
        var cursors = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        List<string>? missedChannels = null;

        candidates = 0;

        foreach (var name in query.Channels)
        {
            if (!channels.TryGetValue(name, out var channel))
                continue;

            cursors[channel.Name] = channel.Sequence;

            var since = query.CursorFor(channel.Name);

            if (since > 0 && since + 1 < channel.Oldest)
                (missedChannels ??= []).Add(channel.Name);

            foreach (var item in channel.Buffer)
            {
                if (item.Seq <= since)
                    continue;

                if (!Matches(query.Topics, item.Topic))
                    continue;

                events.Add(item);
                candidates++;
            }
        }

        if (missedChannels != null)
            candidates++;

        return new NoireRemoteEventBatch
        {
            Ok = true,
            Instance = instance,
            Cursor = cursors.TryGetValue(NoireRemoteChannels.Events, out var main) ? main : 0,
            Cursors = cursors,
            Missed = missedChannels != null,
            MissedChannels = missedChannels,
            Events = events,
        };
    }

    private static NoireRemoteEventBatch Filter(NoireRemoteEventBatch batch, HttpEventQuery query, Guid instance)
    {
        _ = instance;

        if (batch.Events.Count == 0)
            return batch;

        var kept = HttpEventFilter.Apply(batch.Events, query.Filters);

        if (query.Collapse)
            kept = CollapseByTopic(kept);

        batch.Events = kept;

        return batch;
    }

    // The newest event per topic.
    private static List<NoireRemoteEvent> CollapseByTopic(List<NoireRemoteEvent> events)
    {
        var newest = new Dictionary<string, NoireRemoteEvent>(StringComparer.Ordinal);

        foreach (var item in events)
            newest[item.Channel + "/" + item.Topic] = item;

        var kept = new List<NoireRemoteEvent>(newest.Values);
        kept.Sort(static (left, right) => left.Seq.CompareTo(right.Seq));

        return kept;
    }

    private static bool Matches(IReadOnlyList<string>? topics, string topic)
    {
        if (topics == null || topics.Count == 0)
            return true;

        foreach (var wanted in topics)
        {
            if (topic.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var channel in channels.Values)
                channel.Buffer.Clear();

            signal.TrySetResult();
        }
    }
}
