using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.UI;

/// <summary>
/// A profiler scope's name, resolved to an integer once so that measuring one does not hash a string.<br/>
/// Handles are interned, so equal ids mean equal names.
/// </summary>
internal sealed class UiScopeName
{
    private static readonly ConcurrentDictionary<string, UiScopeName> interned = new(StringComparer.Ordinal);

    /// <summary>
    /// Backs <see cref="Id"/>. Starts at 0 so the first handed out is 1, leaving 0 free to mean no scope.
    /// </summary>
    private static int nextId;

    /// <summary>
    /// The name as it is reported.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// The name's integer stand-in, unique across the process and stable for the life of it.
    /// </summary>
    internal int Id { get; }

    private UiScopeName(string name, int id)
    {
        Name = name;
        Id = id;
    }

    /// <summary>
    /// The handle for a name, creating it the first time that name is seen.<br/>
    /// This hashes the string, so a caller on a hot path resolves its handle once and holds it.
    /// </summary>
    /// <param name="name">The scope name.</param>
    /// <returns>The handle for <paramref name="name"/>.</returns>
    internal static UiScopeName For(string name)
        => interned.GetOrAdd(name, static key => new UiScopeName(key, Interlocked.Increment(ref nextId)));

    /// <summary>
    /// The handle for a name that is guaranteed to arrive as the same string instance every time, such as one built by
    /// <see cref="UiIds"/>.
    /// </summary>
    /// <param name="name">The scope name, as the instance handed out for it every time.</param>
    /// <returns>The handle for <paramref name="name"/>, the same one <see cref="For"/> answers.</returns>
    internal static UiScopeName ForInstance(string name)
        => byInstance.GetOrAdd(name, static key => For(key));

    /// <summary>
    /// The handle for each string instance already asked about through <see cref="ForInstance"/>. A second instance of
    /// the same content resolves through <see cref="For"/> to the same handle, so a miss costs one content hash and
    /// never a wrong answer.
    /// </summary>
    private static readonly ConcurrentDictionary<string, UiScopeName> byInstance = new(StringInstanceComparer.Instance);
}
