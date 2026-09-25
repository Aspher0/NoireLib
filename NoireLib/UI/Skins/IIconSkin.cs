using System.Numerics;

namespace NoireLib.UI;

/// <summary>How a skin draws <see cref="NoireIcon"/>s.</summary>
public interface IIconSkin
{
    /// <summary>Draws an icon fitted into a square on the current window's draw list.</summary>
    /// <param name="icon">The icon.</param>
    /// <param name="topLeft">The square's top left corner, in screen pixels.</param>
    /// <param name="size">The square's side in pixels.</param>
    /// <param name="color">The tint.</param>
    void Draw(NoireIcon icon, Vector2 topLeft, float size, Vector4 color);
}
