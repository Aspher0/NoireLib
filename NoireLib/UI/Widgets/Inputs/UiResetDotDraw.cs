using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The position, state and resolved colour a reset-dot hook needs to paint the mark itself.
/// </summary>
/// <remarks>
/// Called only when the dot is shown, with hit testing, layout reservation and the tooltip already handled. The dot
/// is the only mark an input field paints itself; the focus mark has its own hook on <see cref="FocusStyle"/>.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Centre">The centre of the dot, in screen pixels.</param>
/// <param name="Radius">The radius in real pixels, hover growth already applied.</param>
/// <param name="Hovered">Whether the mouse is over the dot.</param>
/// <param name="Color">The colour for the current state, already resolved through the theme.</param>
public readonly record struct UiResetDotDraw(
    ImDrawListPtr DrawList,
    Vector2 Centre,
    float Radius,
    bool Hovered,
    Vector4 Color)
{
    /// <summary>Draws NoireUI's own reset dot.</summary>
    public void DrawDot() => DrawList.AddCircleFilled(Centre, Radius, ColorHelper.Vector4ToUint(Color));
}
