using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// No frame at all: the view draws its own background and controls, as a popup attached to a window does. The window
/// keeps every shared behaviour: focus grouping, opacity, stay visible.
/// </summary>
public sealed class BareChrome : IChromeSkin
{
    private BareChrome()
    {
    }

    /// <summary>The single instance.</summary>
    public static BareChrome Instance { get; } = new();

    /// <summary>Always false.</summary>
    public bool Native => false;

    /// <summary>No header, no strip and no handles.</summary>
    public ChromeMetrics Metrics => default;

    /// <summary>NoireLib's default window menu.</summary>
    public WindowMenuStyle? MenuStyle => null;

    /// <summary>Removes the window's padding, border and item spacing.</summary>
    /// <param name="scale">The global UI scale.</param>
    public void PushWindowStyle(float scale)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
    }

    /// <summary>Draws nothing.</summary>
    /// <param name="frame">The window this frame.</param>
    public void Background(in ChromeFrame frame)
    {
    }

    /// <summary>Draws nothing.</summary>
    /// <param name="frame">The window this frame.</param>
    /// <param name="title">The texts.</param>
    /// <param name="buttons">The window's own buttons.</param>
    /// <param name="menuMin">The window's corner.</param>
    /// <param name="menuMax">The window's corner.</param>
    /// <returns>Always <see cref="ChromeControl.None"/>.</returns>
    public ChromeControl Header(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax)
    {
        menuMin = menuMax = frame.Min;
        return ChromeControl.None;
    }

    /// <summary>Draws nothing.</summary>
    /// <param name="frame">The window this frame.</param>
    /// <param name="title">The texts.</param>
    /// <param name="buttons">The window's own buttons.</param>
    /// <param name="menuMin">The window's corner.</param>
    /// <param name="menuMax">The window's corner.</param>
    /// <returns>Always <see cref="ChromeControl.None"/>.</returns>
    public ChromeControl Collapsed(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax)
    {
        menuMin = menuMax = frame.Min;
        return ChromeControl.None;
    }

    /// <summary>Draws nothing.</summary>
    /// <param name="frame">The window this frame.</param>
    public void ResizeGrip(in ChromeFrame frame)
    {
    }

    /// <summary>Draws nothing.</summary>
    /// <param name="frame">The window this frame.</param>
    public void ClickThroughOutline(in ChromeFrame frame)
    {
    }

    /// <summary>Draws nothing.</summary>
    /// <param name="bodyMin">The body's top left corner.</param>
    /// <param name="bodyMax">The body's bottom right corner.</param>
    /// <param name="scale">The global UI scale.</param>
    public void BlockedVeil(Vector2 bodyMin, Vector2 bodyMax, float scale)
    {
    }
}
