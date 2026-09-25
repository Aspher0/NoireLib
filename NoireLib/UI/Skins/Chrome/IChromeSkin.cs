using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>The frame around a skinned window. It only draws and reports clicks: the behaviour runs in the window.</summary>
public interface IChromeSkin
{
    /// <summary>Whether Dalamud's title bar, window options, background, border and resize are kept.</summary>
    bool Native { get; }

    /// <summary>The frame's measurements, at 100%.</summary>
    ChromeMetrics Metrics { get; }

    /// <summary>The window menu's look, or <see langword="null"/> for NoireLib's default.</summary>
    WindowMenuStyle? MenuStyle { get; }

    /// <summary>Pushes the style variables and colours the window is begun with; NoireLib pops them after it ends.</summary>
    /// <param name="scale">The global UI scale.</param>
    void PushWindowStyle(float scale);

    /// <summary>Fill, backdrop, border and highlight, drawn first.</summary>
    /// <param name="frame">The window this frame.</param>
    void Background(in ChromeFrame frame);

    /// <summary>The title bar: title, subtitle, the window's own buttons, then the window controls.</summary>
    /// <param name="frame">The window this frame.</param>
    /// <param name="title">The texts.</param>
    /// <param name="buttons">The window's own buttons the user left visible.</param>
    /// <param name="menuMin">The window menu button's top left corner, where the menu opens.</param>
    /// <param name="menuMax">Its bottom right corner.</param>
    /// <returns>The control clicked this frame.</returns>
    ChromeControl Header(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax);

    /// <summary>The strip shown instead of the window while it is collapsed.</summary>
    /// <param name="frame">The window this frame.</param>
    /// <param name="title">The texts.</param>
    /// <param name="buttons">The window's own buttons the user left visible.</param>
    /// <param name="menuMin">The window menu button's top left corner.</param>
    /// <param name="menuMax">Its bottom right corner.</param>
    /// <returns>The control clicked this frame.</returns>
    ChromeControl Collapsed(in ChromeFrame frame, in ChromeTitle title, ReadOnlySpan<TitleButton> buttons, out Vector2 menuMin, out Vector2 menuMax);

    /// <summary>The mark in the bottom right corner showing the window can be resized.</summary>
    /// <param name="frame">The window this frame.</param>
    void ResizeGrip(in ChromeFrame frame);

    /// <summary>What shows the window lets clicks through.</summary>
    /// <param name="frame">The window this frame.</param>
    void ClickThroughOutline(in ChromeFrame frame);

    /// <summary>The veil over a window's body while a confirmation or a modal window blocks it.</summary>
    /// <param name="bodyMin">The body's top left corner, in screen pixels.</param>
    /// <param name="bodyMax">The body's bottom right corner.</param>
    /// <param name="scale">The global UI scale.</param>
    void BlockedVeil(Vector2 bodyMin, Vector2 bodyMax, float scale);
}
