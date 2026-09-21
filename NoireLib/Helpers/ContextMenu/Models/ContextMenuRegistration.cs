using System;

namespace NoireLib.Helpers;

/// <summary>
/// A live context menu entry. Disposing it removes the entry, and disposing it again does nothing.
/// </summary>
public sealed class ContextMenuRegistration : IDisposable
{
    internal ContextMenuRegistration(ContextMenuEntry entry)
    {
        Entry = entry;
    }

    /// <summary>The entry this registration shows.</summary>
    public ContextMenuEntry Entry { get; }

    /// <summary>Whether the entry has been removed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Removes the entry.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        ContextMenuHelper.Remove(this);
    }
}
