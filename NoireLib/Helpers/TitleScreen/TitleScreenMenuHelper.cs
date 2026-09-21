using Dalamud.Interface.Textures;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Adds entries to the title screen's menu, the column of icons and labels down the left of the screen that
/// Dalamud's own settings and plugin installer sit in. One <see cref="Register(TitleScreenEntryOptions)"/> call
/// adds an entry and disposing what it returns takes it away.
/// <br/>
/// An entry is shown only on the title screen. Its callback runs with no character logged in. Every entry still
/// standing is removed when the library is disposed.
/// </summary>
public static class TitleScreenMenuHelper
{
    private const string DisposeKey = "NoireLib.TitleScreenMenuHelper";
    private const string LoggerPrefix = "TitleScreenMenuHelper";

    /// <summary>
    /// The size, in pixels on a side, that the title screen requires of an entry's icon. Dalamud removes an entry
    /// whose texture is anything else the first time the title screen draws.
    /// </summary>
    public const int RequiredIconSize = 64;

    private static readonly List<TitleScreenEntry> Entries = [];
    private static readonly object Gate = new();

    private static bool attached;

    /// <summary>The entries this helper currently holds.</summary>
    public static IReadOnlyList<TitleScreenEntry> Registered
    {
        get
        {
            lock (Gate)
                return Entries.ToArray();
        }
    }

    /// <summary>Adds an entry to the title screen menu.</summary>
    /// <param name="options">What the entry shows and does.</param>
    /// <returns>The entry, disposed to remove it.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">If the name is null or blank, or no icon was supplied.</exception>
    public static TitleScreenEntry Register(TitleScreenEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        ArgumentNullException.ThrowIfNull(options.OnTriggered);

        var texture = ResolveIcon(options)
            ?? throw new ArgumentException("An entry needs an icon: set IconId, IconPath, or Icon.", nameof(options));

        var added = options.Priority.HasValue
            ? NoireService.TitleScreenMenu.AddEntry(options.Priority.Value, options.Name, texture, options.OnTriggered)
            : NoireService.TitleScreenMenu.AddEntry(options.Name, texture, options.OnTriggered);

        var entry = new TitleScreenEntry(added, options, texture);

        if (!options.NormalizeIcon)
            Validate(entry);

        lock (Gate)
        {
            Entries.Add(entry);
            Attach();
        }

        return entry;
    }

    /// <summary>Adds an entry drawn with one of the game's own icons.</summary>
    /// <param name="name">The text shown beside the icon.</param>
    /// <param name="iconId">The game icon row to draw it with.</param>
    /// <param name="onTriggered">Runs when the entry is clicked.</param>
    /// <returns>The entry, disposed to remove it.</returns>
    public static TitleScreenEntry Register(string name, uint iconId, Action onTriggered)
        => Register(new TitleScreenEntryOptions { Name = name, IconId = iconId, OnTriggered = onTriggered });

    /// <summary>Adds an entry drawn with an image on disk.</summary>
    /// <param name="name">The text shown beside the icon.</param>
    /// <param name="iconPath">The image file to draw it with.</param>
    /// <param name="onTriggered">Runs when the entry is clicked.</param>
    /// <returns>The entry, disposed to remove it.</returns>
    public static TitleScreenEntry Register(string name, string iconPath, Action onTriggered)
        => Register(new TitleScreenEntryOptions { Name = name, IconPath = iconPath, OnTriggered = onTriggered });

    /// <summary>Every entry on the title screen menu, Dalamud's own included, as Dalamud holds them.</summary>
    /// <returns>Each entry's name and priority.</returns>
    public static IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();

        SafeExecutor.ExecuteSafely(() =>
        {
            foreach (var entry in NoireService.TitleScreenMenu.Entries)
            {
                // Dalamud gates an entry on held keys. Neither the gate nor the skip is public.
                var reflected = new ReflectedObject(entry);
                var keys = reflected.Get("ShowConditionKeys") is System.Collections.IEnumerable gate
                    ? string.Join("+", gate.Cast<object>())
                    : string.Empty;

                lines.Add(
                    $"{entry.Name} (priority {entry.Priority}, internal={reflected.Get<bool>("IsInternal")}, " +
                    $"shown={reflected.Call<bool>("IsShowConditionSatisfied")}" +
                    $"{(string.IsNullOrEmpty(keys) ? string.Empty : $", keys {keys}")})");
            }
        });

        return lines;
    }

    /// <summary>Removes every entry this helper holds.</summary>
    public static void RemoveAll()
    {
        TitleScreenEntry[] snapshot;
        lock (Gate)
            snapshot = [.. Entries];

        foreach (var entry in snapshot)
            entry.Dispose();
    }

    /// <summary>
    /// Resolves the texture an entry is drawn with, from whichever icon property is set.<br/>
    /// Unless <see cref="TitleScreenEntryOptions.NormalizeIcon"/> is off, it is a copy scaled so its longest side is <see cref="RequiredIconSize"/>.
    /// </summary>
    /// <param name="options">The entry's options.</param>
    /// <returns>The texture, or null when none was set.</returns>
    public static ISharedImmediateTexture? ResolveIcon(TitleScreenEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ISharedImmediateTexture? texture = null;

        if (options.Icon != null)
            texture = options.Icon;
        else if (options.IconId.HasValue)
            texture = NoireService.TextureProvider.GetFromGameIcon(new GameIconLookup(options.IconId.Value));
        else if (!string.IsNullOrWhiteSpace(options.IconPath))
            texture = NoireService.TextureProvider.GetFromFile(options.IconPath);

        if (texture == null || !options.NormalizeIcon)
            return texture;

        return new ResizedSharedTexture(texture, RequiredIconSize);
    }

    internal static void ReleaseIcon(TitleScreenEntry entry)
    {
        if (entry.Texture is ResizedSharedTexture scaled)
            SafeExecutor.ExecuteSafely(scaled.Dispose);
    }

    // The title screen removes an entry with a wrong-sized icon silently. It is measured and logged here.
    private static void Validate(TitleScreenEntry entry)
    {
        _ = AsyncHelper.RunInBackgroundAsync(
            async () =>
            {
                using var wrap = await entry.Texture.RentAsync().ConfigureAwait(false);

                if (wrap.Width != RequiredIconSize && wrap.Height != RequiredIconSize)
                {
                    NoireLogger.LogError(
                        $"The title screen entry '{entry.Name}' has a {wrap.Width}x{wrap.Height} icon. The " +
                        $"title screen removes any entry whose texture is not {RequiredIconSize} on a side. It " +
                        "will never be drawn. Leave NormalizeIcon on and the icon is scaled to fit.",
                        LoggerPrefix);
                }
            },
            "NoireLib title screen icon check");
    }

    internal static void Forget(TitleScreenEntry entry)
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

        NoireLibMain.RegisterOnDispose(DisposeKey, RemoveAll);
        attached = true;
    }

    private static void Detach()
    {
        if (!attached)
            return;

        NoireLibMain.UnregisterOnDispose(DisposeKey);
        attached = false;
    }
}
