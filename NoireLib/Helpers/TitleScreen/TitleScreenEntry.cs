using Dalamud.Interface;
using Dalamud.Interface.Textures;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// A live title-screen menu entry. Disposing it takes the entry off the screen, and disposing it again does nothing.
/// </summary>
public sealed class TitleScreenEntry : IDisposable
{
    internal TitleScreenEntry(IReadOnlyTitleScreenMenuEntry entry, TitleScreenEntryOptions options, ISharedImmediateTexture texture)
    {
        Entry = entry;
        Options = options;
        Texture = texture;
    }

    /// <summary>The Dalamud entry underneath, for anything this wrapper does not expose.</summary>
    public IReadOnlyTitleScreenMenuEntry Entry { get; }

    /// <summary>The options the entry was registered with.</summary>
    public TitleScreenEntryOptions Options { get; }

    /// <summary>The text shown beside the icon.</summary>
    public string Name => Options.Name;

    /// <summary>The texture the entry is drawn with.</summary>
    public ISharedImmediateTexture Texture { get; }

    /// <summary>
    /// Whether the icon has loaded. Textures load asynchronously. This reads false for a moment after
    /// registering, and that means nothing on its own.
    /// </summary>
    /// <param name="error">Why it did not load, when it did not.</param>
    /// <returns>True when the icon is ready to draw.</returns>
    public bool IsIconReady(out Exception? error) => Texture.TryGetWrap(out _, out error);

    /// <summary>
    /// Whether the icon is one the title screen will accept. Dalamud <b>removes</b> an entry whose texture is not
    /// <see cref="TitleScreenMenuHelper.RequiredIconSize"/> pixels on a side, the first time the title screen
    /// draws. An entry with the wrong icon appears in the menu's list and never on the screen.
    /// </summary>
    /// <param name="problem">What is wrong with it, or empty when nothing is.</param>
    /// <returns>True when the icon has loaded and is the right size.</returns>
    public bool IsIconValid(out string problem)
    {
        if (!Texture.TryGetWrap(out var wrap, out var error))
        {
            problem = error?.Message ?? "The icon has not loaded yet.";
            return false;
        }

        var size = TitleScreenMenuHelper.RequiredIconSize;
        if (wrap.Width != size && wrap.Height != size)
        {
            problem = $"The icon is {wrap.Width}x{wrap.Height} and the title screen requires {size}x{size}.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    /// <summary>Whether the entry has been removed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Removes the entry from the title screen.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        SafeExecutor.ExecuteSafely(() => NoireService.TitleScreenMenu.RemoveEntry(Entry));
        TitleScreenMenuHelper.ReleaseIcon(this);
        TitleScreenMenuHelper.Forget(this);
    }
}
