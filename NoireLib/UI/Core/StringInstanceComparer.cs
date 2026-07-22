using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NoireLib.UI;

/// <summary>
/// Matches strings by reference rather than by content, for dictionaries whose keys are guaranteed to arrive as the
/// same instance every time.
/// </summary>
/// <remarks>
/// Two suppliers give that guarantee. Compile-time literals, including everything the compiler passes for
/// <see cref="CallerFilePathAttribute"/> and <see cref="CallerMemberNameAttribute"/>, are interned, so one call site
/// hands over the same instance on every call. And <see cref="UiIds"/> builds each id once and returns the cached
/// instance forever after.<br/>
/// Hashing such keys by content meant hashing an absolute source path, eighty characters or so, on every scope a
/// surface opened: with several hundred scopes a frame that was the single largest term in what the profiler cost, and
/// it dwarfed the lock and the node lookup underneath it.<br/>
/// Reference hashing cannot go wrong here in the way it usually can. A miss would only mean an extra entry filed under
/// a second instance of the same content, resolving to the same value through the same code, so the worst case is one
/// wasted dictionary slot rather than a wrong answer.
/// </remarks>
internal sealed class StringInstanceComparer : IEqualityComparer<string>
{
    /// <summary>The shared instance.</summary>
    internal static readonly StringInstanceComparer Instance = new();

    private StringInstanceComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(string? left, string? right) => ReferenceEquals(left, right);

    /// <inheritdoc/>
    public int GetHashCode(string value) => RuntimeHelpers.GetHashCode(value);
}
