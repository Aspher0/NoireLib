using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NoireLib.Remote;

// Runs only while somebody is subscribed.
internal static class RemoteInstanceWatch
{
    private static readonly HashSet<Guid> Known = [];
    private static readonly object Gate = new();

    private static Timer? timer;

    internal static event Action<NoireRemoteInstance>? Connected;

    internal static event Action<NoireRemoteInstance>? Disconnected;

    internal static void Start()
    {
        lock (Gate)
        {
            if (timer != null)
                return;

            // Matches the directory's liveness interval.
            timer = new Timer(static _ => Sweep(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            timer?.Dispose();
            timer = null;
            Known.Clear();
        }
    }

    private static void Sweep()
    {
        IReadOnlyList<NoireRemoteInstance> online;

        try
        {
            NoireRemoteClient.Refresh();
            online = NoireRemoteClient.Discover();
        }
        catch (Exception)
        {
            return;
        }

        var seen = online.ToDictionary(instance => instance.Id);
        List<NoireRemoteInstance> arrived;
        List<Guid> left;

        lock (Gate)
        {
            arrived = [.. online.Where(instance => !Known.Contains(instance.Id))];
            left = [.. Known.Where(id => !seen.ContainsKey(id))];

            foreach (var instance in arrived)
                Known.Add(instance.Id);

            foreach (var id in left)
                Known.Remove(id);
        }

        foreach (var instance in arrived)
            Connected?.Invoke(instance);

        foreach (var id in left)
            Disconnected?.Invoke(new NoireRemoteInstance(new NoireRemoteInstanceRecord { Instance = id }));
    }
}
