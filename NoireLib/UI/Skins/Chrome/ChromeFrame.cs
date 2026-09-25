using System.Numerics;

namespace NoireLib.UI;

/// <summary>What an <see cref="IChromeSkin"/> is told about its window, each frame.</summary>
/// <param name="Min">The window's top left corner, in screen pixels.</param>
/// <param name="Max">The window's bottom right corner.</param>
/// <param name="Scale">The global UI scale.</param>
/// <param name="Opacity">The background opacity from the window menu.</param>
/// <param name="Collapsed">Whether the window is collapsed to its strip.</param>
/// <param name="Focused">Whether the window or one attached to it has focus.</param>
/// <param name="AlwaysOnTop">Whether the window stays above the others.</param>
/// <param name="ClickThrough">Whether the body lets clicks through.</param>
/// <param name="Resizable">Whether the window can be resized.</param>
/// <param name="WindowId">The window's stable id.</param>
/// <param name="Backdrop">The id of the window's group, shared with the windows attached to it.</param>
/// <param name="MenuOpen">Whether the window menu is open.</param>
public readonly record struct ChromeFrame(
    Vector2 Min,
    Vector2 Max,
    float Scale,
    float Opacity,
    bool Collapsed,
    bool Focused,
    bool AlwaysOnTop,
    bool ClickThrough,
    bool Resizable,
    string WindowId,
    string Backdrop,
    bool MenuOpen);
