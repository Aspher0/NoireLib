using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="SplitterOptions.CustomDraw"/> hook needs to paint a splitter's divider itself: where the
/// handle is, what state it is in, and the color NoireUI would have used.
/// </summary>
/// <remarks>
/// The hook is called with the handle already submitted and the drag already applied, so hit testing, the resize cursor
/// and the clamped size are handled whatever it draws.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left corner of the grab handle.</param>
/// <param name="Max">The bottom right corner of the grab handle.</param>
/// <param name="Vertical">Whether the divider runs vertically.</param>
/// <param name="Hovered">Whether the mouse is over the handle.</param>
/// <param name="Dragging">Whether the handle is currently being dragged.</param>
/// <param name="Color">The divider color for the current state, already resolved through the options and the theme.</param>
/// <param name="LineWidth">The line thickness NoireUI would have drawn, in real pixels.</param>
public readonly record struct UiSplitterDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    bool Vertical,
    bool Hovered,
    bool Dragging,
    Vector4 Color,
    float LineWidth)
{
    /// <summary>The centre of the grab handle, in screen coordinates.</summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// Draws the divider NoireUI would have drawn: a line down the middle of the handle, in <see cref="Color"/>.
    /// </summary>
    public void DrawLine() => DrawLine(Color);

    /// <summary>
    /// Draws the divider NoireUI would have drawn, in a color of your choosing.
    /// </summary>
    /// <param name="color">The color to draw it in.</param>
    public void DrawLine(Vector4 color)
    {
        if (DrawList.IsNull)
            return;

        var center = Center;
        var packed = ColorHelper.Vector4ToUint(color);

        if (Vertical)
            DrawList.AddLine(new Vector2(center.X, Min.Y), new Vector2(center.X, Max.Y), packed, LineWidth);
        else
            DrawList.AddLine(new Vector2(Min.X, center.Y), new Vector2(Max.X, center.Y), packed, LineWidth);
    }
}
