using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireWindowChrome"/> window paints itself, now that ImGui is not painting it.
/// </summary>
public sealed class WindowChromeStyle
{
    /// <summary>
    /// The window's surface. When <see langword="null"/>, a flat fill in the theme's surface colour.
    /// </summary>
    public PlateStyle? Plate { get; set; }

    /// <summary>
    /// The window's border. When <see langword="null"/>, there is none.
    /// </summary>
    /// <remarks>
    /// A <see cref="FrameStyle"/> rather than a colour and a thickness, so a window edge gets the same double rules and
    /// corner ticks anything else drawn with one does.
    /// </remarks>
    public FrameStyle? Frame { get; set; }

    /// <summary>The room between the window's edge and its contents, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public Vector2 Padding { get; set; } = new(2f, 2f);

    /// <summary>
    /// How opaque the whole window is, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Spent on the surface alone, when the chrome is painted. Fading the whole window through ImGui's alpha would
    /// take the text and the controls with it, dimming the window rather than making it translucent.
    /// </remarks>
    public float Opacity { get; set; } = 1f;

    /// <summary>Returns a copy.</summary>
    /// <returns>A shallow copy.</returns>
    public WindowChromeStyle Clone() => (WindowChromeStyle)MemberwiseClone();
}
