using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.UI;

/// <summary>
/// A profiler scope's name, resolved to an integer once so that measuring one does not hash a string.
/// </summary>
/// <remarks>
/// The profiler keys a node on its name and the node it sits inside. Keyed on the name itself that lookup hashed a
/// string on every open, which measured 0.14 microseconds against 0.02 for the same lookup on an integer, and was the
/// largest single term in what a scope cost. A surface entered hundreds of times a frame paid it hundreds of times.
/// <br/>
/// Interning is what makes the integer safe to key on: two handles for the same name are the same handle, so equal
/// ids mean equal names and reference equality answers "is this the same scope" without a comparison. Names are drawn
/// from call sites and from widget kinds, both of which are bounded by the code rather than by what a user types, so
/// the table does not grow without limit.
/// </remarks>
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
    /// The handle for a name, creating it the first time that name is seen.
    /// </summary>
    /// <remarks>
    /// This is the one call that hashes the string, so a caller on a hot path resolves its handle once and holds it
    /// rather than calling in per draw. <see cref="UiDraw"/> holds one per call site for exactly that reason.
    /// </remarks>
    /// <param name="name">The scope name.</param>
    /// <returns>The handle for <paramref name="name"/>.</returns>
    internal static UiScopeName For(string name)
        => interned.GetOrAdd(name, static key => new UiScopeName(key, Interlocked.Increment(ref nextId)));
}
