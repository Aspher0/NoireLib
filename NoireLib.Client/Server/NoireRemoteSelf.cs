using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// The tags this instance publishes about itself. A caller can aim at it by something other than a character
/// name, including before any character is logged in.
/// </summary>
public sealed class NoireRemoteSelf
{
    private readonly Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();

    /// <summary>
    /// Raised when a tag is added, changed or removed. It carries the tags as they now stand.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, string>>? Changed;

    /// <summary>
    /// Gets a tag's value, or null when this instance carries no such tag.
    /// </summary>
    /// <param name="key">The tag name, compared ignoring case.</param>
    /// <returns>The value, or null.</returns>
    public string? this[string key]
    {
        get
        {
            lock (gate)
                return key != null && tags.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Gets every tag this instance carries.
    /// </summary>
    public IReadOnlyDictionary<string, string> All
    {
        get
        {
            lock (gate)
                return new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Sets a tag. Writing the value it already holds changes nothing and notifies nobody. Calling this from a
    /// per-frame handler costs nothing.
    /// </summary>
    /// <param name="key">The tag name.</param>
    /// <param name="value">The value a caller matches against.</param>
    /// <exception cref="ArgumentException">If the key is null or blank.</exception>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (gate)
        {
            if (tags.TryGetValue(key, out var held) && string.Equals(held, value, StringComparison.Ordinal))
                return;

            tags[key] = value ?? string.Empty;
        }

        Raise();
    }

    /// <summary>
    /// Removes a tag. Removing one that is not there changes nothing.
    /// </summary>
    /// <param name="key">The tag name.</param>
    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (gate)
        {
            if (!tags.Remove(key))
                return;
        }

        Raise();
    }

    /// <summary>
    /// Removes every tag.
    /// </summary>
    public void Clear()
    {
        lock (gate)
        {
            if (tags.Count == 0)
                return;

            tags.Clear();
        }

        Raise();
    }

    private void Raise() => Changed?.Invoke(All);
}
