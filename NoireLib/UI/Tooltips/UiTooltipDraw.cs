using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The window rectangle and resolved colours a <see cref="TooltipStyle.CustomDraw"/> hook needs to paint a
/// tooltip's chrome itself.
/// </summary>
/// <remarks>
/// Called from inside the tooltip window before the content, so the chrome sits behind the message, and never while
/// the tooltip is still parked off screen being measured. A tooltip window takes no input, so there is no hover or
/// active state.
/// </remarks>
/// <param name="DrawList">The tooltip window's draw list.</param>
/// <param name="Min">The top left corner of the tooltip window.</param>
/// <param name="Max">The bottom right corner of the tooltip window.</param>
/// <param name="Background">The background colour, opacity already applied, resolved through the style and the host.</param>
/// <param name="BorderColor">The border colour, resolved through the style and the host.</param>
/// <param name="BorderSize">The border thickness in real pixels, zero meaning no border.</param>
/// <param name="Rounding">The corner radius the tooltip would have used, in real pixels.</param>
/// <param name="Padding">The inner padding the content is laid out with, in real pixels.</param>
public readonly record struct UiTooltipDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    Vector4 Background,
    Vector4 BorderColor,
    float BorderSize,
    float Rounding,
    Vector2 Padding)
{
    /// <summary>The size of the tooltip in pixels.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>Draws the tooltip's background.</summary>
    public void DrawBackground()
        => DrawList.AddRectFilled(Min, Max, ColorHelper.Vector4ToUint(Background), Rounding);

    /// <summary>Draws the tooltip's border, or nothing when the thickness is zero.</summary>
    public void DrawBorder()
    {
        if (BorderSize > 0f)
            DrawList.AddRect(Min, Max, ColorHelper.Vector4ToUint(BorderColor), Rounding, ImDrawFlags.None, BorderSize);
    }
}
