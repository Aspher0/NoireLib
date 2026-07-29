using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="ButtonStyle.CustomDraw"/> hook needs to paint a button itself: where it is, what state it is
/// in, and the colors NoireUI would have used.
/// </summary>
/// <remarks>
/// Called with the geometry already resolved and the item already submitted: hit testing, keyboard navigation and
/// the returned click are handled whatever the hook draws. Read <see cref="Color"/> rather than hardcoding one to
/// stay on theme.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left corner of the button.</param>
/// <param name="Max">The bottom right corner of the button.</param>
/// <param name="Label">The button's label, with any id suffix already stripped.</param>
/// <param name="Hovered">Whether the mouse is over the button.</param>
/// <param name="Held">Whether the button is currently pressed.</param>
/// <param name="Color">The fill color for the current state, already resolved through the style and the theme.</param>
/// <param name="TextColor">The label color for the current state.</param>
/// <param name="Rounding">The corner radius the button would have used.</param>
/// <param name="Progress">How far a progressive button has filled, from 0 to 1. Always 1 for an ordinary button.</param>
public readonly record struct UiButtonDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    string Label,
    bool Hovered,
    bool Held,
    Vector4 Color,
    Vector4 TextColor,
    float Rounding,
    float Progress)
{
    /// <summary>The size of the button in pixels.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>The centre of the button in screen coordinates.</summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// Draws the button's own label, centred, in the colour NoireUI would have used.
    /// </summary>
    /// <remarks>
    /// Always centred. For a label positioned elsewhere, use <see cref="Label"/>, <see cref="TextColor"/> and
    /// <see cref="DrawList"/> directly.
    /// </remarks>
    public void DrawLabel() => DrawLabel(TextColor);

    /// <summary>
    /// Draws the button's own label, centred, in a colour of your choosing.
    /// </summary>
    /// <param name="color">The colour to draw it in.</param>
    public void DrawLabel(Vector4 color)
    {
        if (string.IsNullOrEmpty(Label))
            return;

        var size = NoireText.CalcSizeInCurrentFont(Label);

        DrawList.AddText(Center - (size * 0.5f), Helpers.ColorHelper.Vector4ToUint(color), Label);
    }
}
