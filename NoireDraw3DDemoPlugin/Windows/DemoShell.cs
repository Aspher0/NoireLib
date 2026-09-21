namespace NoireDraw3DDemoPlugin.Windows;

internal sealed class DemoShell
{
    public DemoPage Current { get; set; } = DemoPage.Showcase;

    /// <summary>Keeps the window open while the game UI is hidden, since Dalamud cannot hide it selectively.</summary>
    public bool KeepWindowWhenUiHidden { get; set; } = true;

    public void Navigate(DemoPage page) => Current = page;
}
