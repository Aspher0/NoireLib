using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

// The entry points views use for skinned pieces that carry behaviour. Plain controls are called on the active skin's
// controls directly.
public static partial class NoireUI
{
    /// <summary>A button bound to an action: its label and icon, disabled with the action's hint while unavailable, run when clicked.</summary>
    /// <typeparam name="T">The action's target type.</typeparam>
    /// <param name="action">The action.</param>
    /// <param name="target">The target.</param>
    /// <param name="tone">What the button means.</param>
    /// <param name="width">The width in pixels, or 0 to fit the label.</param>
    /// <returns>True on the frame the action ran.</returns>
    public static bool Button<T>(UiAction<T> action, T target, ButtonTone tone = ButtonTone.Neutral, float width = 0f)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!UiDraw.Available)
            return false;

        var available = action.IsAvailable(target) && (!action.RequiresCtrl || ImGui.GetIO().KeyCtrl);
        var skin = NoireSkins.Active;
        var clicked = skin.Controls.Button(action.Label.Key, action.Label.Text, action.Icon, tone, available, width);

        if (!available && action.Hint is { } hint && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            skin.Overlays.Tooltip(hint.Text, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        return clicked && action.Run(target);
    }

    /// <summary>A tooltip on the last item, drawn by the active skin while the item is hovered.</summary>
    /// <param name="text">The text.</param>
    public static void Tip(string text)
    {
        if (UiDraw.Available && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            NoireSkins.Active.Overlays.Tooltip(text, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    /// <summary>A tooltip for an area, drawn by the active skin while the mouse is over it.</summary>
    /// <param name="text">The text.</param>
    /// <param name="min">The area's top left corner, in screen pixels.</param>
    /// <param name="max">The area's bottom right corner.</param>
    public static void Tip(string text, Vector2 min, Vector2 max)
    {
        if (UiDraw.Available && ImGui.IsMouseHoveringRect(min, max) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            NoireSkins.Active.Overlays.Tooltip(text, min, max);
    }

    /// <summary>A hover card for an area while the mouse is over it; the body draws its content with the skin's controls.</summary>
    /// <typeparam name="T">The type carried into the body.</typeparam>
    /// <param name="id">An id unique among the cards shown at once.</param>
    /// <param name="anchorMin">The area's top left corner, in screen pixels.</param>
    /// <param name="anchorMax">The area's bottom right corner.</param>
    /// <param name="width">The card's width at 100%.</param>
    /// <param name="state">Passed to the body.</param>
    /// <param name="body">Draws the content; a static lambda keeps it free of allocation.</param>
    public static void Card<T>(string id, Vector2 anchorMin, Vector2 anchorMax, float width, T state, Action<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!UiDraw.Available || !ImGui.IsMouseHoveringRect(anchorMin, anchorMax))
            return;

        var overlays = NoireSkins.Active.Overlays;

        if (!overlays.CardBegin(id, anchorMin, anchorMax, width * Scale))
            return;

        try
        {
            body(state);
        }
        finally
        {
            overlays.CardEnd();
        }
    }

    /// <summary>A line of text in one of the active skin's typefaces.</summary>
    /// <param name="role">What the text is.</param>
    /// <param name="text">The text.</param>
    public static void Line(TextRole role, string text)
    {
        if (!UiDraw.Available)
            return;

        var fonts = NoireSkins.Active.Fonts;
        fonts.Push(role);
        ImGui.TextUnformatted(text);
        fonts.Pop();
    }

    /// <summary>Wrapped text in one of the active skin's typefaces and a theme colour.</summary>
    /// <param name="role">What the text is.</param>
    /// <param name="text">The text.</param>
    /// <param name="color">The colour.</param>
    public static void Paragraph(TextRole role, string text, ThemeColor color = ThemeColor.Text)
    {
        if (!UiDraw.Available)
            return;

        var fonts = NoireSkins.Active.Fonts;
        fonts.Push(role);
        ImGui.PushStyleColor(ImGuiCol.Text, NoireSkins.Theme.Resolve(color));
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        fonts.Pop();
    }

    /// <summary>An icon in the active skin's art at the cursor, advancing it.</summary>
    /// <param name="icon">The icon.</param>
    /// <param name="size">The side at 100%.</param>
    /// <param name="color">The theme colour.</param>
    public static void Icon(NoireIcon icon, float size, ThemeColor color = ThemeColor.TextMuted)
    {
        if (!UiDraw.Available)
            return;

        var side = size * Scale;
        NoireSkins.Active.Icons.Draw(icon, ImGui.GetCursorScreenPos(), side, NoireSkins.Theme.Resolve(color));
        ImGui.Dummy(new Vector2(side, side));
    }
}
