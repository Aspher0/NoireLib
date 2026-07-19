using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Text at any size, without ImGui's blur.<br/>
/// An ImGui font is a bitmap atlas rasterized once at a fixed size, so <c>SetWindowFontScale</c> and a scaled font push
/// do not rasterize anything larger, they sample that bitmap larger: a heading at twice the base size is a pixel-crawled
/// upscale of a small glyph, and no ImGui setting fixes it. NoireText builds a real font at the size asked for and draws
/// with that.
/// </summary>
/// <remarks>
/// Ask for a size by role (<see cref="TextSize"/>) rather than by number wherever you can. Roles resolve through
/// <see cref="NoireTheme"/>, which is what lets a skin move the whole type scale from one place; a number at a call site
/// is a number thirty other call sites will each pick differently.<br/>
/// Sizes are logical pixels at 100%, like every other measurement in NoireUI. See <see cref="NoireUI.Scale"/>.<br/>
/// Building a size takes a moment, and until it is ready the text is drawn at the right size by scaling the font that is
/// already loaded. It is briefly soft rather than briefly the wrong size, so nothing on screen moves when the real font
/// arrives. Call <see cref="Prewarm"/> at startup to have it ready before anything is drawn.
/// </remarks>
/// <example>
/// <code>
/// NoireText.Draw("Settings", TextSize.Heading);
/// NoireText.Colored(theme.Resolve(ThemeColor.TextMuted), "3 profiles loaded", TextSize.Caption);
///
/// NoireText.At(TextSize.Display, () =>
/// {
///     ImGui.TextUnformatted("Noire");          // raw ImGui inside the scope draws at the size too
///     NoireText.Draw("Deco");
/// });
/// </code>
/// </example>
public static class NoireText
{
    #region Drawing

    /// <summary>
    /// Draws text at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Draw(string text, TextSize size = TextSize.Body)
        => Draw(text, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Draws text at an explicit size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    public static void Draw(string text, float sizePx)
        => At(sizePx, text, static t => ImGui.TextUnformatted(t));

    /// <summary>
    /// Draws text in a color, at a named size.
    /// </summary>
    /// <param name="color">The text color.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Colored(Vector4 color, string text, TextSize size = TextSize.Body)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color);
        Draw(text, size);
    }

    /// <summary>
    /// Draws text in the theme's muted color, at a named size, for anything supporting.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Muted(string text, TextSize size = TextSize.Body)
        => Colored(NoireTheme.Current.Resolve(ThemeColor.TextMuted), text, size);

    /// <summary>
    /// Draws text in the theme's disabled color, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Disabled(string text, TextSize size = TextSize.Body)
        => Colored(NoireTheme.Current.Resolve(ThemeColor.TextDisabled), text, size);

    /// <summary>
    /// Draws text that wraps at the current wrap position, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Wrapped(string text, TextSize size = TextSize.Body)
        => At(NoireTheme.Current.ResolveTextSize(size), text, static t => ImGui.TextWrapped(t));

    /// <summary>
    /// Draws text that wraps at a given width, at a named size.
    /// </summary>
    /// <param name="width">The width to wrap at, in real pixels. Usually a measured one, so it is not scaled.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Wrapped(float width, string text, TextSize size = TextSize.Body)
    {
        At(NoireTheme.Current.ResolveTextSize(size), (width, text), static state =>
            NoireLayout.WrapText(state.width, state.text, static t => ImGui.TextUnformatted(t)));
    }

    /// <summary>
    /// Draws a bulleted line at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Bullet(string text, TextSize size = TextSize.Body)
        => At(NoireTheme.Current.ResolveTextSize(size), text, static t => ImGui.BulletText(t));

    /// <summary>
    /// Draws text centred in the space remaining on the current line, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Centered(string text, TextSize size = TextSize.Body)
    {
        At(NoireTheme.Current.ResolveTextSize(size), text, static t =>
        {
            // Measured inside the scope, so the width comes from the font that is about to draw rather than from
            // whatever was current outside it.
            var offset = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(t).X) * 0.5f;

            if (offset > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

            ImGui.TextUnformatted(t);
        });
    }

    #endregion

    #region Measuring

    /// <summary>
    /// Measures text as it would be drawn at a named size.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="size">The step of the type scale to measure at.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 CalcSize(string text, TextSize size = TextSize.Body)
        => CalcSize(text, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Measures text as it would be drawn at an explicit size.
    /// </summary>
    /// <remarks>
    /// Measured with the font pushed, which is the whole point: a layout built on a measurement taken in one font and
    /// drawn in another is wrong everywhere by a few pixels, and neither of the two looks like the one lying. It
    /// therefore answers for whatever would draw *right now*, including the stretched stand-in used while a size is
    /// still building.
    /// </remarks>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 CalcSize(string text, float sizePx)
    {
        if (!NoireService.IsInitialized())
            return Vector2.Zero;

        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            return ImGui.CalcTextSize(text ?? string.Empty);
        }

        var restore = PushApproximateSize(sizePx);

        try
        {
            return ImGui.CalcTextSize(text ?? string.Empty);
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    /// <summary>
    /// The height of one line at a named size, for reserving space before drawing into it.
    /// </summary>
    /// <param name="size">The step of the type scale to measure.</param>
    /// <returns>The line height in real pixels.</returns>
    public static float LineHeight(TextSize size = TextSize.Body)
        => CalcSize(" ", size).Y;

    #endregion

    #region Building

    /// <summary>
    /// Starts building the current theme's type scale, so it is ready before anything asks to draw with it.
    /// </summary>
    /// <remarks>
    /// Optional. Without it the first draw starts the build and text is drawn at the right size with a scaled stand-in
    /// until it finishes, which is a second or two of slightly soft headings and nothing worse.<br/>
    /// Call it when the plugin loads, or after setting a theme, and even that is gone. Safe to call repeatedly: a size
    /// already built is not built again.
    /// </remarks>
    public static void Prewarm() => UiFontCache.BuildScale();

    #endregion

    #region Scopes

    /// <summary>
    /// Runs a block of drawing at a named size.<br/>
    /// Everything inside draws at that size, raw ImGui included, so a block of mixed text takes the size once rather
    /// than repeating it per call.
    /// </summary>
    /// <param name="size">The step of the type scale to draw at.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void At(TextSize size, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        At(NoireTheme.Current.ResolveTextSize(size), body, static b => b());
    }

    /// <inheritdoc cref="At(TextSize, Action)"/>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="size">The step of the type scale to draw at.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to run.</param>
    public static void At<TState>(TextSize size, TState state, Action<TState> body)
        => At(NoireTheme.Current.ResolveTextSize(size), state, body);

    /// <summary>
    /// Runs a block of drawing at an explicit size.
    /// </summary>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void At(float sizePx, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        At(sizePx, body, static b => b());
    }

    /// <inheritdoc cref="At(float, Action)"/>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void At<TState>(float sizePx, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        NoireUI.EnsureFrameServices();

        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            UiScope.Run(nameof(NoireText), state, body);
            return;
        }

        // Either this is the host's own size and needs no font of its own, or the real one is still building. Both draw
        // with the font already loaded, stretched to the size that was asked for.
        var restore = PushApproximateSize(sizePx);

        try
        {
            UiScope.Run(nameof(NoireText), state, body);
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    /// <summary>
    /// Stretches the current font to a target size, for the frames before the real one at that size has been built.
    /// </summary>
    /// <remarks>
    /// This is the blurry scaling NoireText exists to replace, used deliberately and briefly. The alternative is to
    /// leave the text at whatever size the current font happens to be, which is what an unbuilt handle pushes as, and
    /// that is worse in the way that shows: every heading starts small and jumps when its font arrives, taking the
    /// layout around it along. Scaled, the text is the right size from the first frame and merely sharpens.
    /// </remarks>
    /// <param name="sizePx">The target size at 100%.</param>
    /// <returns>The window font scale to restore, or <see langword="null"/> when nothing was changed.</returns>
    private static float? PushApproximateSize(float sizePx)
    {
        if (!NoireService.IsInitialized())
            return null;

        var current = ImGui.GetFontSize();
        if (current <= 0f)
            return null;

        var factor = NoireUI.Scaled(MathF.Max(1f, sizePx)) / current;

        // A size within a pixel or so of the current font is not worth a stretch, and this is the path the host's own
        // body size takes on every single call.
        if (MathF.Abs(factor - 1f) < 0.02f)
            return null;

        // The window font scale is absolute, so restoring it means knowing what it was. Measuring can legitimately
        // happen before anything has been begun, and there is no window to read it off then.
        var window = ImGuiP.GetCurrentWindow();
        if (window.IsNull)
            return null;

        var previous = window.FontWindowScale;
        ImGui.SetWindowFontScale(previous * factor);

        return previous;
    }

    #endregion
}
