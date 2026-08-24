namespace NoireDraw3DDemoPlugin.Windows;

// The state that outlives any one page: which page is open, and whether the window stays up while the game UI is
// hidden. Shared because the Renderer page edits the flag and the window reads it in DrawConditions.
internal sealed class DemoShell
{
    public DemoPage Current { get; set; } = DemoPage.Showcase;

    /// <summary>
    /// Read by <c>DemoWindow.DrawConditions</c>. Dalamud cannot hide the window for us: keeping the 3D layer alive
    /// while the UI is hidden means telling Dalamud not to hide this plugin at all.
    /// </summary>
    public bool KeepWindowWhenUiHidden { get; set; } = true;

    public void Navigate(DemoPage page) => Current = page;
}
