using NoireLib.Localizer;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>Where a <see cref="NoireSettingsWindow"/> lists its pages.</summary>
public sealed class NoireSettingsPages
{
    private readonly List<SettingsPageEntry> pages = [];

    internal NoireSettingsPages()
    {
    }

    internal int Count => pages.Count;

    internal SettingsPageEntry this[int index] => pages[index];

    /// <summary>Adds a page.</summary>
    /// <param name="id">A stable id, remembered as the last page open.</param>
    /// <param name="label">The tab label.</param>
    /// <param name="draw">Declares the page's content, every frame it shows.</param>
    /// <param name="icon">The tab icon.</param>
    /// <exception cref="ArgumentException">Thrown when another page has the id.</exception>
    public void Add(string id, NoireString label, Action<NoireSettingsPage> draw, NoireIcon? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(draw);

        if (IndexOf(id) >= 0)
            throw new ArgumentException($"A settings page with the id '{id}' already exists.", nameof(id));

        pages.Add(new SettingsPageEntry(id, label, draw, icon));
    }

    internal int IndexOf(string? id)
    {
        if (id == null)
            return -1;

        for (var i = 0; i < pages.Count; i++)
        {
            if (pages[i].Id == id)
                return i;
        }

        return -1;
    }
}
