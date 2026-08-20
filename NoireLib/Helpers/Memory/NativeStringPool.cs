using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NoireLib.Helpers.Memory;

/// <summary>
/// Hands out native ANSI buffers for strings the game retains, and never frees them. The game can hold a substituted
/// pointer for as long as it keeps whatever it built from it, so there is no provably safe moment to free; the pool
/// deduplicates instead, returning the same pointer for the same string. A string the callee only reads during the
/// call wants an ordinary marshalled buffer instead.
/// </summary>
public sealed class NativeStringPool
{
    private readonly Dictionary<string, nint> buffers = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>A process-wide pool, for callers with no reason to keep their own.</summary>
    public static NativeStringPool Shared { get; } = new();

    /// <summary>How many distinct strings the pool is holding.</summary>
    public int Count
    {
        get
        {
            lock (gate)
                return buffers.Count;
        }
    }

    /// <summary>A native ANSI buffer holding the string, allocated on first request and reused after that.</summary>
    /// <param name="value">The string to pin; null or empty yields a zero pointer rather than a buffer.</param>
    /// <returns>The buffer, stable for the rest of the session.</returns>
    public nint Pin(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return nint.Zero;

        lock (gate)
        {
            if (buffers.TryGetValue(value, out var existing))
                return existing;

            var buffer = Marshal.StringToHGlobalAnsi(value);
            buffers[value] = buffer;

            return buffer;
        }
    }

    /// <summary>Whether a string has already been pinned, without pinning it.</summary>
    /// <param name="value">The string to look for.</param>
    /// <returns>True when the pool already holds a buffer for it.</returns>
    public bool IsPinned(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        lock (gate)
            return buffers.ContainsKey(value);
    }
}
