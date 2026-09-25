using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>Dalamud's own window frame, drawn by ImGui. The window's title buttons become Dalamud title bar buttons.</summary>
public sealed class NativeChrome : IChromeSkin
{
    private NativeChrome()
    {
    }

    /// <summary>The single instance.</summary>
    public static NativeChrome Instance { get; } = new();

    /// <summary>Always true.</summary>
    public bool Native => true;

    /// <summary>No measurements: ImGui lays the window out.</summary>
    public ChromeMetrics Metrics => default;

    /// <summary>NoireLib's default window menu.</summary>
    public WindowMenuStyle? MenuStyle => null;

    /// <summary>Pushes nothing.</summary>
    /// <param name="scale">The global UI scale.</param>
    public void PushWindowStyle(float scale)
    {
    }

    /// <summary>Draws nothing: ImGui draws the background.</summary>
    /// <param name="frame">The window this frame.</param>
    public void Background(in ChromeFrame frame)
    {
    }

    /// <summary>Draws nothing: Dalamud draws the title bar.</summary>
    /// <param name="frame">The window this frame.</param>
    /// <param name="title">The texts.</param>
    /// <param name="buttons">The window's own buttons.</param>
    /// <param name="menuMin">Always zero.</param>
    /// <param name="menuMax">Always zero.</param>
    /// <returns>Always <see cref="ChromeControl.None"/>.</returns>
    public ChromeControl Header(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax)
    {
        menuMin = menuMax = default;
        return ChromeControl.None;
    }

    /// <summary>Draws nothing: Dalamud collapses the window.</summary>
    /// <param name="frame">The window this frame.</param>
    /// <param name="title">The texts.</param>
    /// <param name="buttons">The window's own buttons.</param>
    /// <param name="menuMin">Always zero.</param>
    /// <param name="menuMax">Always zero.</param>
    /// <returns>Always <see cref="ChromeControl.None"/>.</returns>
    public ChromeControl Collapsed(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax)
    {
        menuMin = menuMax = default;
        return ChromeControl.None;
    }

    /// <summary>Draws nothing: ImGui draws its own grip.</summary>
    /// <param name="frame">The window this frame.</param>
    public void ResizeGrip(in ChromeFrame frame)
    {
    }

    /// <summary>Draws nothing: Dalamud shows its own click-through state.</summary>
    /// <param name="frame">The window this frame.</param>
    public void ClickThroughOutline(in ChromeFrame frame)
    {
    }

    /// <summary>Dims the body with ImGui's modal dim colour.</summary>
    /// <param name="bodyMin">The body's top left corner.</param>
    /// <param name="bodyMax">The body's bottom right corner.</param>
    /// <param name="scale">The global UI scale.</param>
    public void BlockedVeil(Vector2 bodyMin, Vector2 bodyMax, float scale)
    {
        using var draw = UiDraw.Begin();

        if (!draw.List.IsNull)
            draw.List.AddRectFilled(bodyMin, bodyMax, ImGui.GetColorU32(ImGuiCol.ModalWindowDimBg));
    }
}
