using System.Numerics;

namespace NoireLib.UI;

/// <summary>How a skin draws menus, tooltips, hover cards, pills, confirmations and toasts. NoireLib decides when they open.</summary>
public interface IOverlaySkin
{
    /// <summary>Pushes the style variables and colours a context menu window is begun with; NoireLib pops them after it begins.</summary>
    void MenuStyle();

    /// <summary>Called inside the menu window before its first line.</summary>
    void MenuBegin()
    {
    }

    /// <summary>Called inside the menu window after its last line.</summary>
    void MenuEnd()
    {
    }

    /// <summary>The menu's title line.</summary>
    /// <param name="title">The title.</param>
    /// <param name="gameIcon">A game icon before it, or 0.</param>
    void MenuTitle(string title, uint gameIcon);

    /// <summary>One menu item. While <paramref name="requiresCtrl"/> and Ctrl is up, it shows disabled with the hint.</summary>
    /// <param name="label">The label.</param>
    /// <param name="icon">The icon, or <see langword="null"/> for none.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <param name="hint">A tooltip, also shown as the reason while disabled.</param>
    /// <param name="requiresCtrl">Whether it only works while Ctrl is held.</param>
    /// <returns>True on the frame a usable item is clicked.</returns>
    bool MenuItem(string label, NoireIcon? icon, bool enabled, string? hint, bool requiresCtrl);

    /// <summary>A menu separator.</summary>
    void MenuSeparator();

    /// <summary>A tooltip for an anchor, shown at once, above it when there is room.</summary>
    /// <param name="text">The text.</param>
    /// <param name="anchorMin">The anchor's top left corner, in screen pixels.</param>
    /// <param name="anchorMax">The anchor's bottom right corner.</param>
    void Tooltip(string text, Vector2 anchorMin, Vector2 anchorMax);

    /// <summary>Starts a hover card anchored to an area; the caller draws its content.</summary>
    /// <param name="id">An id unique among the cards shown at once.</param>
    /// <param name="anchorMin">The anchor's top left corner, in screen pixels.</param>
    /// <param name="anchorMax">The anchor's bottom right corner.</param>
    /// <param name="width">The card's width in pixels.</param>
    /// <returns>True when the content should be drawn; <see cref="CardEnd"/> is called only then.</returns>
    bool CardBegin(string id, Vector2 anchorMin, Vector2 anchorMax, float width);

    /// <summary>Ends the hover card.</summary>
    void CardEnd();

    /// <summary>A pill attached to the bottom of a window.</summary>
    /// <param name="pill">The pill.</param>
    /// <param name="windowMin">The window's top left corner, in screen pixels.</param>
    /// <param name="windowMax">The window's bottom right corner.</param>
    void Pill(NoirePill pill, Vector2 windowMin, Vector2 windowMax);

    /// <summary>A confirmation drawn over a window's body.</summary>
    /// <param name="state">What is asked, this frame.</param>
    /// <param name="bodyMin">The body's top left corner, in screen pixels.</param>
    /// <param name="bodyMax">The body's bottom right corner.</param>
    /// <returns>The answer on the frame it is given, <see cref="ConfirmAnswer.Pending"/> otherwise.</returns>
    ConfirmAnswer Confirm(in ConfirmState state, Vector2 bodyMin, Vector2 bodyMax);

    /// <summary>The look of the default toast area while this skin is active, or <see langword="null"/> for the plugin's own.</summary>
    ToastStyle? Toasts { get; }
}
