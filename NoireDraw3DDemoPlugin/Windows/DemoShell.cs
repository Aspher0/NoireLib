namespace NoireDraw3DDemoPlugin.Windows;

internal sealed class DemoShell
{
    public DemoPage Current { get; set; } = DemoPage.Showcase;

    // Dalamud cannot hide this window selectively with the game UI.
    public bool KeepWindowWhenUiHidden { get; set; } = true;

    public void Navigate(DemoPage page) => Current = page;
}
