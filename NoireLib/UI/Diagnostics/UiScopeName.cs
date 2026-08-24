using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.UI;

// A profiler scope's name, resolved to an integer once so that measuring one does not hash a string.
// Handles are interned, so equal ids mean equal names.
internal sealed class UiScopeName
{
    private static readonly ConcurrentDictionary<string, UiScopeName> interned = new(StringComparer.Ordinal);

    // Starts at 0 so the first handed out is 1, leaving 0 free to mean no scope.
    private static int nextId;

    internal string Name { get; }

    // Unique across the process and stable for the life of it.
    internal int Id { get; }

    private UiScopeName(string name, int id)
    {
        Name = name;
        Id = id;
    }

    // This hashes the string; a caller on a hot path resolves its handle once and holds it.
    internal static UiScopeName For(string name)
        => interned.GetOrAdd(name, static key => new UiScopeName(key, Interlocked.Increment(ref nextId)));

    // The name must arrive as the same string instance every time, such as one built by UiIds.
    internal static UiScopeName ForInstance(string name)
        => byInstance.GetOrAdd(name, static key => For(key));

    // Keyed on string instance: a second instance of the same content resolves through For to the same handle,
    // so a miss costs one content hash and never a wrong answer.
    private static readonly ConcurrentDictionary<string, UiScopeName> byInstance = new(StringInstanceComparer.Instance);
}
