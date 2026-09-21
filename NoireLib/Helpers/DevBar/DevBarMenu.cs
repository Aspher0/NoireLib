using System;

namespace NoireLib.Helpers;

/// <summary>
/// A live dev bar menu. Disposing it takes the menu off the bar, and disposing it again does nothing.
/// </summary>
public sealed class DevBarMenu : IDisposable
{
    internal DevBarMenu(DevBarMenuOptions options)
    {
        Options = options;
    }

    /// <summary>The options the menu was registered with.</summary>
    public DevBarMenuOptions Options { get; }

    /// <summary>The menu's name in the bar.</summary>
    public string Name => Options.Name;

    /// <summary>Whether the menu is drawn at all. A hidden menu keeps its registration.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Whether the menu has been removed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Removes the menu from the bar.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        DalamudDevBarHelper.Forget(this);
    }
}
