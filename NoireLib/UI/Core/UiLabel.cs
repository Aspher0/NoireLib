using NoireLib.Helpers;
using System;

namespace NoireLib.UI;

/// <summary>
/// Takes an ImGui label apart into the text shown and the text that identifies it, once each, and hands the same
/// strings back on every later frame.
/// </summary>
/// <remarks>
/// ImGui packs both jobs into one string: everything after <c>##</c> is hidden from the display and everything after
/// <c>###</c> replaces the id outright. Splitting that costs a substring, and a widget is redrawn every frame, so a
/// settings page whose fields carry stable ids produced two short-lived strings per field sixty times a second for a
/// split that never changed.<br/>
/// The strings handed back are equal to the substrings each call replaces, which matters beyond the bytes: an id
/// travels into <see cref="NoireUiState"/> keys, so a split that returned different text would orphan every value a
/// user had saved under it.<br/>
/// Reached only from the draw thread, like <see cref="UiIds"/>, so the caches need no lock.
/// </remarks>
internal static class UiLabel
{
    /// <summary>
    /// How many labels are kept before a cache starts over. See <see cref="UiIds"/> for why a plugin reaches a stable
    /// set almost immediately.
    /// </summary>
    private const int MaxEntries = 4096;

    /// <summary>
    /// A label, as itself. A record struct rather than a bare string because <see cref="HotPathCache{TKey, TValue}"/>
    /// takes a struct key, which is what keeps a lookup from boxing.
    /// </summary>
    private readonly record struct Key(string Label);

    /// <summary>
    /// The two halves of a label split on <c>###</c>.
    /// </summary>
    private readonly record struct Parts(string Visible, string Id);

    private static readonly HotPathCache<Key, string> Visibles = new(MaxEntries);
    private static readonly HotPathCache<Key, Parts> Stables = new(MaxEntries);

    /// <summary>
    /// The part of a label that is drawn, which is everything before the first <c>##</c>.
    /// </summary>
    /// <param name="label">The label to read.</param>
    /// <returns>The visible text, or <paramref name="label"/> itself when it carries no id marker.</returns>
    internal static string Visible(string label)
    {
        if (label == null)
            return string.Empty;

        var marker = label.IndexOf("##", StringComparison.Ordinal);

        // A label with no marker is already its own visible text, so it returns before the cache is consulted: a
        // dictionary lookup to arrive back at the argument would cost more than the substring this exists to avoid.
        if (marker < 0)
            return label;

        var key = new Key(label);

        if (Visibles.TryGet(key, out var cached))
            return cached;

        var visible = label[..marker];
        Visibles.Set(key, visible);

        return visible;
    }

    /// <summary>
    /// Splits a label on <c>###</c> into the text drawn and the id the widget is remembered under.
    /// </summary>
    /// <remarks>
    /// Only <c>###</c> separates the two here, matching ImGui: <c>##</c> hides text from the display without replacing
    /// the id, and a surface whose label doubles as its id wants the stable part rather than the hidden one.
    /// </remarks>
    /// <param name="label">The label to split.</param>
    /// <param name="visible">The text to draw. The whole label when it carries no stable id.</param>
    /// <param name="id">The stable id. The whole label when it carries no stable id.</param>
    internal static void Split(string label, out string visible, out string id)
    {
        if (label == null)
        {
            visible = string.Empty;
            id = string.Empty;
            return;
        }

        var marker = label.IndexOf("###", StringComparison.Ordinal);

        if (marker < 0)
        {
            visible = label;
            id = label;
            return;
        }

        var key = new Key(label);

        if (Stables.TryGet(key, out var cached))
        {
            visible = cached.Visible;
            id = cached.Id;
            return;
        }

        visible = label[..marker];
        id = label[(marker + 3)..];

        Stables.Set(key, new Parts(visible, id));
    }
}
