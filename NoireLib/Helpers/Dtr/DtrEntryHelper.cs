using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Puts entries on the game's server-info bar, the row of icons beside the world name. One
/// <see cref="Register(DtrEntryOptions)"/> call adds an entry and disposing what it returns takes it away.
/// <br/>
/// An entry given a source is polled on the framework thread at its own interval and written only when its value
/// changes. The framework subscription is taken on the first such entry and dropped with the last one, and every
/// entry still standing is removed when the library is disposed.
/// </summary>
public static class DtrEntryHelper
{
    private const string DisposeKey = "NoireLib.DtrEntryHelper";

    private static readonly List<DtrEntry> Entries = [];
    private static readonly object Gate = new();

    private static bool attached;

    /// <summary>The entries this helper currently holds.</summary>
    public static IReadOnlyList<DtrEntry> Registered
    {
        get
        {
            lock (Gate)
                return Entries.ToArray();
        }
    }

    /// <summary>Adds an entry to the bar.</summary>
    /// <param name="options">What the entry shows and does.</param>
    /// <returns>The entry, disposed to remove it.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">If the title is null or blank.</exception>
    public static DtrEntry Register(DtrEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);

        var bar = NoireService.DtrBar.Get(options.Title, options.Text);
        var entry = new DtrEntry(bar, options);

        lock (Gate)
        {
            Entries.Add(entry);
            Attach();
        }

        return entry;
    }

    /// <summary>Adds a plain entry showing a fixed text.</summary>
    /// <param name="title">The entry's title, unique to the plugin.</param>
    /// <param name="text">The text to show.</param>
    /// <param name="onClick">Runs on a left click.</param>
    /// <returns>The entry, disposed to remove it.</returns>
    public static DtrEntry Register(string title, SeString text, Action<DtrInteractionEvent>? onClick = null)
        => Register(new DtrEntryOptions { Title = title, Text = text, OnLeftClick = onClick });

    /// <summary>Adds an entry whose text a source keeps up to date.</summary>
    /// <param name="title">The entry's title, unique to the plugin.</param>
    /// <param name="source">Produces the text, polled on the framework thread.</param>
    /// <param name="refreshInterval">How often to poll. A quarter of a second by default.</param>
    /// <returns>The entry, disposed to remove it.</returns>
    public static DtrEntry Register(string title, Func<SeString> source, TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Register(new DtrEntryOptions
        {
            Title = title,
            Source = source,
            RefreshInterval = refreshInterval ?? TimeSpan.FromMilliseconds(250),
        });
    }

    /// <summary>Finds a registered entry by its title.</summary>
    /// <param name="title">The title to look for.</param>
    /// <returns>The entry, or null when none is registered under that title.</returns>
    public static DtrEntry? Find(string title)
    {
        lock (Gate)
        {
            foreach (var entry in Entries)
            {
                if (string.Equals(entry.Title, title, StringComparison.Ordinal))
                    return entry;
            }
        }

        return null;
    }

    /// <summary>Removes every entry this helper holds.</summary>
    public static void RemoveAll()
    {
        DtrEntry[] snapshot;
        lock (Gate)
            snapshot = [.. Entries];

        foreach (var entry in snapshot)
            entry.Dispose();
    }

    internal static void Forget(DtrEntry entry)
    {
        lock (Gate)
        {
            Entries.Remove(entry);
            if (Entries.Count == 0)
                Detach();
        }
    }

    private static void Attach()
    {
        if (attached)
            return;

        NoireService.Framework.Update += Update;
        NoireLibMain.RegisterOnDispose(DisposeKey, RemoveAll);
        attached = true;
    }

    private static void Detach()
    {
        if (!attached)
            return;

        NoireService.Framework.Update -= Update;
        NoireLibMain.UnregisterOnDispose(DisposeKey);
        attached = false;
    }

    private static void Update(IFramework framework)
    {
        DtrEntry[] snapshot;
        lock (Gate)
        {
            if (Entries.Count == 0)
                return;

            snapshot = [.. Entries];
        }

        foreach (var entry in snapshot)
            entry.Tick();
    }
}
