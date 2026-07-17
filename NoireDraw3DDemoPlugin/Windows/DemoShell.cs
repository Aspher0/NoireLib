namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>
/// The state that outlives any one page: which page is open, and whether the window stays up while the game UI is
/// hidden. Shared rather than owned because two parties need the flag - the Renderer page edits it, the window reads it
/// in <c>DrawConditions</c> - and passing one object around keeps the pages from knowing about each other.
/// </summary>
internal sealed class DemoShell
{
    /// <summary>The page currently on screen.</summary>
    public DemoPage Current { get; set; } = DemoPage.Showcase;

    /// <summary>
    /// Whether the demo window keeps drawing while the game UI is hidden. Read by <c>DemoWindow.DrawConditions</c>,
    /// which is what actually hides the window: Dalamud cannot do it for us, because keeping the 3D layer alive while
    /// the UI is hidden means telling Dalamud not to hide this plugin at all.
    /// </summary>
    public bool KeepWindowWhenUiHidden { get; set; } = true;

    /// <summary>Opens a page. Used by the rail.</summary>
    /// <param name="page">The page to show.</param>
    public void Navigate(DemoPage page) => Current = page;
}
