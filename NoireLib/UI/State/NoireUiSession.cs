using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// Widget memory that lasts exactly as long as the plugin does. The same idea as <see cref="NoireUiState"/>, with the
/// file taken away: nothing is written to disk, and everything is gone on reload.
/// </summary>
/// <remarks>
/// For the state that is worth keeping while someone works and worth forgetting afterwards: a search a window was left
/// narrowed to, which tab was open, a panel scrolled halfway, a preview left expanded. Persisting those is worse than
/// not, because a plugin that reopens three days later still filtered to something the user has forgotten typing looks
/// broken rather than helpful.<br/>
/// <br/>
/// Two differences from <see cref="NoireUiState"/> follow from there being no file, and both are in this store's
/// favour:
/// <list type="bullet">
/// <item>
/// <b>Any type may be stored</b>, including ones that do not serialize. Values are held as they are rather than
/// round-tripped through JSON, so a reference type comes back as the same instance.
/// </item>
/// <item>
/// <b>A generated widget id is safe to key on.</b> A GUID id is a new one every session, which is exactly why
/// <see cref="NoireUiState"/> refuses it, and exactly why it does not matter here: this store's lifetime is that
/// session too, so the key and the value expire together.
/// </item>
/// </list>
/// Static per plugin, not per process. NoireLib is compiled into each plugin rather than shared, so one plugin's
/// session state cannot collide with another's.
/// </remarks>
/// <example>
/// <code>
/// NoireUiSession.Set("myplugin.roster.search", search);
///
/// // ...somewhere else, or after the window was closed and reopened
/// var search = NoireUiSession.Get("myplugin.roster.search", string.Empty);
/// </code>
/// </example>
public static class NoireUiSession
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, object?> Entries = new();

    /// <summary>How many entries are being held.</summary>
    public static int Count
    {
        get
        {
            lock (SyncRoot)
                return Entries.Count;
        }
    }

    /// <summary>
    /// Every key currently held, for a diagnostics view.
    /// </summary>
    /// <returns>The keys, in no particular order.</returns>
    public static IReadOnlyList<string> GetKeys()
    {
        lock (SyncRoot)
        {
            var keys = new string[Entries.Count];
            Entries.Keys.CopyTo(keys, 0);
            return keys;
        }
    }

    /// <summary>
    /// Reads a stored value, returning <paramref name="fallback"/> when there is none or when what is stored is not
    /// the shape <typeparamref name="T"/> expects.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="key">The entry key. Namespace it with your plugin and your widget.</param>
    /// <param name="fallback">The value returned when nothing usable is stored.</param>
    /// <returns>The stored value, or <paramref name="fallback"/>.</returns>
    public static T? Get<T>(string key, T? fallback = default) => TryGet<T>(key, out var value) ? value : fallback;

    /// <summary>
    /// Reads a stored value and reports whether it was there and of the expected type.
    /// </summary>
    /// <remarks>
    /// A value stored under the same key as a different type reads as absent rather than throwing, matching
    /// <see cref="NoireUiState"/>: one widget's mistake about a key must not take another widget down.
    /// </remarks>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="key">The entry key.</param>
    /// <param name="value">The stored value, or the default.</param>
    /// <returns>True when a usable value was read.</returns>
    public static bool TryGet<T>(string key, out T? value)
    {
        value = default;

        if (string.IsNullOrEmpty(key))
            return false;

        object? stored;

        lock (SyncRoot)
        {
            if (!Entries.TryGetValue(key, out stored))
                return false;
        }

        if (stored is T typed)
        {
            value = typed;
            return true;
        }

        // A stored null is a real value for a reference or nullable type, and reporting it as absent would make it
        // impossible to remember "nothing" as distinct from "never set".
        if (stored == null && default(T) == null)
            return true;

        return false;
    }

    /// <summary>
    /// Stores a value for the rest of the session.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="key">The entry key. Namespace it with your plugin and your widget.</param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is blank.</exception>
    public static void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (SyncRoot)
            Entries[key] = value;
    }

    /// <summary>
    /// Forgets one entry.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <returns>True when there was something to forget.</returns>
    public static bool Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        lock (SyncRoot)
            return Entries.Remove(key);
    }

    /// <summary>
    /// Forgets every entry whose key starts with a prefix, for clearing one widget or one window at once.
    /// </summary>
    /// <param name="keyPrefix">The prefix to match.</param>
    /// <returns>How many entries were forgotten.</returns>
    public static int RemoveAll(string keyPrefix)
    {
        if (string.IsNullOrEmpty(keyPrefix))
            return 0;

        lock (SyncRoot)
        {
            List<string>? doomed = null;

            foreach (var key in Entries.Keys)
            {
                if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
                    (doomed ??= []).Add(key);
            }

            if (doomed == null)
                return 0;

            foreach (var key in doomed)
                Entries.Remove(key);

            return doomed.Count;
        }
    }

    /// <summary>Forgets everything.</summary>
    public static void Clear()
    {
        lock (SyncRoot)
            Entries.Clear();
    }
}
