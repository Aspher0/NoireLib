using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws the eye to something: a steady pulse on the thing the user has not noticed yet, a glow around what just
/// became important, a shake or a bounce when something happened to it.
/// </summary>
[NoireFacade]
public static class NoireAttention
{
    /// <summary>
    /// How much a pulse dims at its lowest, from 0 for fully transparent to 1 for no dimming at all.
    /// </summary>
    public static float PulseFloor { get; set; } = 0.55f;

    /// <summary>
    /// A multiplier that follows a slow pulse while a condition holds, for tinting or fading something into notice.
    /// </summary>
    /// <param name="active">Whether the thing still wants attention.</param>
    /// <param name="period">How long one pulse takes, in seconds.</param>
    /// <returns>A multiplier from <see cref="PulseFloor"/> to 1.</returns>
    public static float Pulse(bool active = true, float period = 1.5f)
        => active && !NoireUI.ReducedMotion ? NoireAnim.Pulse(period, PulseFloor, 1f) : 1f;

    /// <summary>
    /// Draws a soft glow around the widget that was just submitted, for as long as a condition holds.
    /// </summary>
    /// <param name="active">Whether to draw it at all.</param>
    /// <param name="color">The glow colour. When <see langword="null"/>, the theme's accent.</param>
    /// <param name="spread">How far the glow reaches beyond the element, at 100%.</param>
    /// <param name="period">How long one pulse of the glow takes, in seconds.</param>
    public static void Glow(bool active = true, Vector4? color = null, float spread = 6f, float period = 1.5f)
        => GlowOn(LastItemRect(), active, color, spread, period);

    /// <summary>
    /// Draws a soft glow around a rectangle, for as long as a condition holds.
    /// </summary>
    /// <param name="target">The element to glow around, in screen pixels.</param>
    /// <param name="active">Whether to draw it at all.</param>
    /// <param name="color">The glow colour. When <see langword="null"/>, the theme's accent.</param>
    /// <param name="spread">How far the glow reaches beyond the element, at 100%.</param>
    /// <param name="period">How long one pulse of the glow takes, in seconds.</param>
    public static void GlowOn(UiRect target, bool active = true, Vector4? color = null, float spread = 6f, float period = 1.5f)
    {
        if (!active || !UiDraw.Available || target.IsEmpty)
            return;

        // The glow breathes rather than sitting still: a static halo reads as part of the skin within seconds and
        // stops being noticed. Under reduced motion it holds at full strength instead of disappearing, so the
        // element is still marked.
        var strength = NoireUI.ReducedMotion ? 1f : NoireAnim.Pulse(period, 0.45f, 1f);
        var resolved = ColorHelper.ScaleAlpha(color ?? NoireTheme.Current.Resolve(ThemeColor.Accent), strength);
        var reach = NoireUI.Scaled(spread);

        // The window's own list, because this establishes a redirect: reading NoireShapes.DrawList here would read
        // back a redirect already in force and make the call a no-op.
        using var draw = UiDraw.BeginWindow();

        NoireShapes.On(draw.List, (target, resolved, reach), static state =>
            NoireShapes.Glow(
                state.target.Position,
                state.target.Max,
                state.resolved,
                state.reach,
                CornerShape.Rounded,
                ImGui.GetStyle().FrameRounding));
    }

    /// <summary>
    /// Starts a shake on something, for a rejection: a wrong value, a refused action, a field that has to be fixed.
    /// </summary>
    /// <param name="id">A stable id for the thing being shaken.</param>
    public static void Shake(string id) => NoireAnim.Trigger(id, ShakeKey);

    /// <summary>
    /// Starts a bounce on something, for an arrival: a value that just landed, a row that was just added.
    /// </summary>
    /// <param name="id">A stable id for the thing bouncing.</param>
    public static void Bounce(string id) => NoireAnim.Trigger(id, BounceKey);

    /// <summary>
    /// Reads where something being shaken or bounced should be drawn this frame.
    /// </summary>
    /// <param name="id">The id passed to <see cref="Shake"/> or <see cref="Bounce"/>.</param>
    /// <param name="offset">Where to draw, relative to where it would otherwise go, in real pixels.</param>
    /// <returns>True while something is moving.</returns>
    public static bool Offset(string id, out Vector2 offset)
    {
        offset = Vector2.Zero;

        if (string.IsNullOrEmpty(id) || NoireUI.ReducedMotion || !UiDraw.Available)
            return false;

        var shake = NoireAnim.Shake(id, ShakeKey);
        var bounce = BounceOffset(id);

        offset = new Vector2(shake, bounce);
        return offset != Vector2.Zero;
    }

    /// <summary>
    /// Moves the cursor by whatever a shake or bounce asks for, for the widget about to be drawn.
    /// </summary>
    /// <remarks>The cursor is not put back.</remarks>
    /// <param name="id">The id passed to <see cref="Shake"/> or <see cref="Bounce"/>.</param>
    /// <returns>True while something is moving.</returns>
    public static bool ApplyOffset(string id)
    {
        if (!Offset(id, out var offset))
            return false;

        ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);
        return true;
    }

    /// <summary>
    /// A brightening multiplier that fades away after <see cref="Flash"/>, for a value that just changed.
    /// </summary>
    /// <param name="id">The id passed to <see cref="Flash"/>.</param>
    /// <returns>A multiplier from 1 upward, settling back to 1.</returns>
    public static float FlashStrength(string id)
    {
        if (string.IsNullOrEmpty(id) || NoireUI.ReducedMotion || !UiDraw.Available)
            return 1f;

        return 1f + NoireAnim.Flash(id, FlashKey);
    }

    /// <summary>Starts a flash on something, for a value that just changed under the user.</summary>
    /// <param name="id">A stable id for the thing flashing.</param>
    public static void Flash(string id) => NoireAnim.Trigger(id, FlashKey);

    /// <summary>
    /// Cancels whatever a thing was doing, so a widget being removed or reused does not inherit it.
    /// </summary>
    /// <param name="id">The id to clear.</param>
    public static void Clear(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        NoireAnim.Reset(id, ShakeKey);
        NoireAnim.Reset(id, BounceKey);
        NoireAnim.Reset(id, FlashKey);
    }

    // Negative is upward.
    private static float BounceOffset(string id)
    {
        const float duration = 0.45f;
        const float height = 6f;

        var progress = NoireAnim.Progress(id, BounceKey, duration);

        if (progress >= 1f)
            return 0f;

        // A decaying half-sine: one clear hop, then a smaller one, then nothing.
        var decay = 1f - progress;
        return -MathF.Abs(MathF.Sin(progress * MathF.PI * 2f)) * decay * decay * NoireUI.Scaled(height);
    }

    // The sub keys the three event motions store themselves under. Composed into one string instead, the caller's id
    // would be re-interpolated on every frame.
    private const string ShakeKey = "attention.shake";

    private const string BounceKey = "attention.bounce";

    private const string FlashKey = "attention.flash";

    private static UiRect LastItemRect()
        => UiRect.FromBounds(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
}
