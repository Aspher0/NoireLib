using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireWindowChrome"/> window paints itself.
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
    public FrameStyle? Frame { get; set; }

    /// <summary>The room between the window's edge and its contents, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public Vector2 Padding { get; set; } = new(2f, 2f);

    /// <summary>
    /// How opaque the whole window is, from 0 to 1, applied to the surface alone; the text and the controls are not
    /// faded with it.
    /// </summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Returns a copy.</summary>
    /// <returns>A shallow copy.</returns>
    public WindowChromeStyle Clone() => (WindowChromeStyle)MemberwiseClone();
}
