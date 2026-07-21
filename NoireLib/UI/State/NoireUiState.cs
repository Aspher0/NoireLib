using Newtonsoft.Json.Linq;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// The small amount of widget memory that has to survive a reload: where a user dragged an overlay, which sections they
/// left collapsed, which column they last sorted by.<br/>
/// One JSON file, one flat key space, written on a debounce and on shutdown. Reads are cheap enough to do every frame.
/// </summary>
/// <remarks>
/// <b>This is not a replacement for your configuration.</b> Nothing here is versioned, migrated, validated or backed
/// up, and it is deleted without ceremony when a user resets their layout. Anything a user would be upset to lose
/// belongs in the configuration system, which does all of that. What belongs here is the state a widget would rebuild
/// from scratch without complaint, and which is only worth keeping because rebuilding it is mildly annoying.<br/>
/// <br/>
/// Every <c>Persist</c> switch on a widget defaults to <b>off</b>. Nothing is written until a plugin asks for it, so a
/// plugin that never opts in never grows a state file.<br/>
/// <br/>
/// <b>Draw thread only</b> for reads and writes; the file itself is written on a background task.
/// </remarks>
[NoireFacade("State")]
public static class NoireUiState
{
    private const string DisposeCallbackKey = "NoireLib.UI.NoireUiState";
    private const string SaveDebounceKey = "NoireLib.UI.NoireUiState.Save";

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, JToken> Entries = new(StringComparer.Ordinal);

    private static bool loaded;
    private static bool dirty;
    private static string fileName = "NoireUiState.json";
    private static string? filePathOverride;

    /// <summary>
    /// The file name inside the plugin's own configuration directory. Changing it after anything has been read reloads
    /// from the new file.
    /// </summary>
    public static string FileName
    {
        get => fileName;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (string.Equals(fileName, value, StringComparison.Ordinal))
                return;

            fileName = value;
            Reload();
        }
    }

    /// <summary>
    /// An explicit full path for the state file, overriding <see cref="FileName"/>. Leave it <see langword="null"/> to
    /// keep the file beside the plugin's configuration, which is where it belongs.
    /// </summary>
    public static string? FilePath
    {
        get => filePathOverride;
        set
        {
            if (string.Equals(filePathOverride, value, StringComparison.Ordinal))
                return;

            filePathOverride = value;
            Reload();
        }
    }

    /// <summary>
    /// How long writing waits for the changes to stop before it saves. A user dragging an overlay produces a change
    /// every frame, and none of them is worth a disk write on its own.
    /// </summary>
    public static TimeSpan SaveDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether the file has been read yet. Loading happens lazily, on the first read or write.
    /// </summary>
    public static bool IsLoaded => loaded;

    /// <summary>
    /// Whether there are changes that have not reached the file yet.
    /// </summary>
    public static bool HasUnsavedChanges => dirty;

    /// <summary>
    /// How many entries are stored.
    /// </summary>
    public static int Count
    {
        get
        {
            EnsureLoaded();

            lock (SyncRoot)
                return Entries.Count;
        }
    }

    /// <summary>
    /// Every key currently stored, as a snapshot.
    /// </summary>
    /// <returns>The keys.</returns>
    public static IReadOnlyList<string> GetKeys()
    {
        EnsureLoaded();

        lock (SyncRoot)
        {
            var keys = new string[Entries.Count];
            Entries.Keys.CopyTo(keys, 0);
            return keys;
        }
    }

    /// <summary>
    /// Reads a stored value, returning <paramref name="fallback"/> when there is none or when what is stored is not the
    /// shape <typeparamref name="T"/> expects.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="key">The entry key. Namespace it with your plugin and your widget.</param>
    /// <param name="fallback">The value returned when nothing usable is stored.</param>
    /// <returns>The stored value, or <paramref name="fallback"/>.</returns>
    public static T? Get<T>(string key, T? fallback = default) => TryGet<T>(key, out var value) ? value : fallback;

    /// <summary>
    /// Reads a stored value and reports whether it was there and readable.<br/>
    /// A stored value of the wrong shape reads as absent rather than throwing: the file is editable by hand, and one bad
    /// entry must not take a window down.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="key">The entry key.</param>
    /// <param name="value">The stored value, or the default.</param>
    /// <returns>True when a usable value was read.</returns>
    public static bool TryGet<T>(string key, out T? value)
    {
        value = default;

        if (string.IsNullOrEmpty(key))
            return false;

        EnsureLoaded();

        JToken? token;
        lock (SyncRoot)
        {
            if (!Entries.TryGetValue(key, out token))
                return false;
        }

        try
        {
            value = token.ToObject<T>();
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning($"The UI state entry '{key}' could not be read as {typeof(T).Name} and is being ignored: {ex.Message}", nameof(NoireUiState));
            return false;
        }
    }

    /// <summary>
    /// Stores a value and schedules a save.
    /// </summary>
    /// <typeparam name="T">The stored value type.</typeparam>
    /// <param name="key">The entry key. Namespace it with your plugin and your widget.</param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is blank.</exception>
    public static void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        EnsureLoaded();

        var token = value is null ? JValue.CreateNull() : JToken.FromObject(value);

        lock (SyncRoot)
        {
            if (Entries.TryGetValue(key, out var existing) && JToken.DeepEquals(existing, token))
                return;

            Entries[key] = token;
        }

        ScheduleSave();
    }

    /// <summary>
    /// Drops a single entry.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <returns>True when an entry was removed.</returns>
    public static bool Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        EnsureLoaded();

        bool removed;
        lock (SyncRoot)
            removed = Entries.Remove(key);

        if (removed)
            ScheduleSave();

        return removed;
    }

    /// <summary>
    /// Drops every entry whose key starts with <paramref name="keyPrefix"/>, for forgetting one widget or one window at
    /// once.
    /// </summary>
    /// <param name="keyPrefix">The key prefix to match.</param>
    /// <returns>How many entries were removed.</returns>
    public static int RemoveAll(string keyPrefix)
    {
        if (string.IsNullOrEmpty(keyPrefix))
            return 0;

        EnsureLoaded();

        var removed = 0;

        lock (SyncRoot)
        {
            var stale = new List<string>();

            foreach (var key in Entries.Keys)
            {
                if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
                    stale.Add(key);
            }

            foreach (var key in stale)
            {
                if (Entries.Remove(key))
                    removed++;
            }
        }

        if (removed > 0)
            ScheduleSave();

        return removed;
    }

    /// <summary>
    /// Drops every entry. Widgets fall back to their defaults on the next frame.
    /// </summary>
    public static void Clear()
    {
        lock (SyncRoot)
        {
            loaded = true;
            Entries.Clear();
        }

        ScheduleSave();
    }

    /// <summary>
    /// Writes the file now rather than waiting out <see cref="SaveDelay"/>. Does nothing when there is nothing to write.
    /// </summary>
    public static void Save()
    {
        Dictionary<string, JToken> snapshot;

        lock (SyncRoot)
        {
            if (!dirty)
                return;

            dirty = false;
            snapshot = new Dictionary<string, JToken>(Entries, StringComparer.Ordinal);
        }

        var path = ResolvePath();
        if (path == null)
            return;

        if (!FileHelper.WriteJsonToFile(path, snapshot))
        {
            // The write already logged. Keeping the dirty flag means the next change retries rather than assuming the
            // state reached disk, so a transient failure costs a delay instead of the whole layout.
            lock (SyncRoot)
                dirty = true;
        }
    }

    /// <summary>
    /// Forgets everything held in memory and reads the file again on the next access. Anything unsaved is lost.
    /// </summary>
    public static void Reload()
    {
        lock (SyncRoot)
        {
            Entries.Clear();
            loaded = false;
            dirty = false;
        }
    }

    private static void EnsureLoaded()
    {
        lock (SyncRoot)
        {
            if (loaded)
                return;

            loaded = true;
        }

        var path = ResolvePath();
        if (path == null)
            return;

        var stored = FileHelper.ReadJsonFromFile<Dictionary<string, JToken>>(path);
        if (stored == null)
            return;

        lock (SyncRoot)
        {
            foreach (var entry in stored)
            {
                if (!string.IsNullOrEmpty(entry.Key) && entry.Value != null)
                    Entries[entry.Key] = entry.Value;
            }
        }

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Save);
    }

    private static void ScheduleSave()
    {
        lock (SyncRoot)
            dirty = true;

        if (!NoireService.IsInitialized())
            return;

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Save);

        _ = DebounceHelper.DebounceAsync(SaveDebounceKey, SaveDelay, Save);
    }

    /// <summary>
    /// Resolves the state file path, or null when there is no plugin directory to put it in (unit tests).
    /// </summary>
    private static string? ResolvePath()
        => !string.IsNullOrWhiteSpace(filePathOverride) ? filePathOverride : FileHelper.GetPluginConfigFilePath(fileName);
}
