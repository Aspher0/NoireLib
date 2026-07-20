using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws the eye to something: a steady pulse on the thing the user has not noticed yet, a glow around what just
/// became important, a shake or a bounce when something happened to it.
/// </summary>
/// <remarks>
/// Two kinds of motion, and the difference decides which call to reach for. <see cref="Pulse"/> and <see cref="Glow"/>
/// are <em>states</em>: they run for as long as the condition holds, and the caller passes that condition every frame.
/// <see cref="Shake"/> and <see cref="Bounce"/> are <em>events</em>: they are fired once by id and play themselves out.
/// <br/>
/// All of it is decoration, so all of it stops under <see cref="NoireUI.ReducedMotion"/>. What is underneath keeps
/// working: a pulsing button is still a button, a shaken field still holds its text, and nothing moves position in a
/// way that could put a control somewhere the mouse is not.
/// </remarks>
/// <example>
/// <code>
/// ImGui.Button("Apply");
/// NoireAttention.Glow(hasUnsavedChanges);           // around the button just drawn
///
/// NoireAttention.Shake("password");                  // fired from the failure path, once
/// NoireAttention.Offset("password", out var nudge);  // read where it should be drawn this frame
/// </code>
/// </example>
public static class NoireAttention
{
    private const string StateId = "NoireAttention";

    /// <summary>
    /// How much a pulse dims at its lowest, from 0 for fully transparent to 1 for no dimming at all.
    /// </summary>
    /// <remarks>
    /// Shallow on purpose. A pulse that reaches zero reads as something broken rather than something waiting, and it
    /// is impossible to read text through.
    /// </remarks>
    public static float PulseFloor { get; set; } = 0.55f;

    /// <summary>
    /// A multiplier that follows a slow pulse while a condition holds, for tinting or fading something into notice.
    /// </summary>
    /// <param name="active">Whether the thing still wants attention.</param>
    /// <param name="period">How long one pulse takes, in seconds.</param>
    /// <returns>A multiplier from <see cref="PulseFloor"/> to 1, and exactly 1 when nothing is pulsing.</returns>
    public static float Pulse(bool active = true, float period = 1.5f)
        => active && !NoireUI.ReducedMotion ? NoireAnim.Pulse(period, PulseFloor, 1f) : 1f;

    /// <summary>
    /// Draws a soft glow around the widget that was just submitted, for as long as a condition holds.
    /// </summary>
    /// <param name="active">Whether to draw it at all, so this can be called unconditionally.</param>
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
        if (!active || !NoireService.IsInitialized() || target.IsEmpty)
            return;

        // The glow breathes rather than sitting still, because a static halo reads as part of the skin within seconds
        // and stops being noticed at all. Under reduced motion it holds at full strength instead of disappearing: the
        // point is to mark the element, and that survives without the movement.
        var strength = NoireUI.ReducedMotion ? 1f : NoireAnim.Pulse(period, 0.45f, 1f);
        var resolved = ColorHelper.ScaleAlpha(color ?? NoireTheme.Current.Resolve(ThemeColor.Accent), strength);
        var reach = NoireUI.Scaled(spread);

        NoireShapes.On(ImGui.GetWindowDrawList(), (target, resolved, reach), static state =>
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
    /// <remarks>
    /// Fire this from the thing that failed, once. Read it back with <see cref="Offset(string, out Vector2)"/> on the
    /// frames that follow.
    /// </remarks>
    /// <param name="id">A stable id for the thing being shaken.</param>
    public static void Shake(string id) => NoireAnim.Trigger(StateId, ShakeKey(id));

    /// <summary>
    /// Starts a bounce on something, for an arrival: a value that just landed, a row that was just added.
    /// </summary>
    /// <param name="id">A stable id for the thing bouncing.</param>
    public static void Bounce(string id) => NoireAnim.Trigger(StateId, BounceKey(id));

    /// <summary>
    /// Reads where something being shaken or bounced should be drawn this frame.
    /// </summary>
    /// <remarks>
    /// Apply it to the cursor before drawing, and the widget moves without knowing it did. The offset returns to zero
    /// on its own when the motion finishes, and is always zero under <see cref="NoireUI.ReducedMotion"/>, so the branch
    /// on the return value is about skipping work rather than about correctness.
    /// </remarks>
    /// <param name="id">The id passed to <see cref="Shake"/> or <see cref="Bounce"/>.</param>
    /// <param name="offset">Where to draw, relative to where it would otherwise go, in real pixels.</param>
    /// <returns>True while something is moving.</returns>
    public static bool Offset(string id, out Vector2 offset)
    {
        offset = Vector2.Zero;

        if (string.IsNullOrEmpty(id) || NoireUI.ReducedMotion || !NoireService.IsInitialized())
            return false;

        var shake = NoireAnim.Shake(StateId, ShakeKey(id));
        var bounce = BounceOffset(id);

        offset = new Vector2(shake, bounce);
        return offset != Vector2.Zero;
    }

    /// <summary>
    /// Moves the cursor by whatever a shake or bounce asks for, for the widget about to be drawn.
    /// </summary>
    /// <remarks>
    /// The convenience over <see cref="Offset(string, out Vector2)"/>, and what most callers want: one line before the
    /// widget, nothing after it. The cursor is not put back, because the widget is drawn at the moved position and
    /// whatever follows it is laid out from there anyway.
    /// </remarks>
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
        if (string.IsNullOrEmpty(id) || NoireUI.ReducedMotion || !NoireService.IsInitialized())
            return 1f;

        return 1f + NoireAnim.Flash(StateId, FlashKey(id));
    }

    /// <summary>Starts a flash on something, for a value that just changed under the user.</summary>
    /// <param name="id">A stable id for the thing flashing.</param>
    public static void Flash(string id) => NoireAnim.Trigger(StateId, FlashKey(id));

    /// <summary>
    /// Cancels whatever a thing was doing, so a widget being removed or reused does not inherit it.
    /// </summary>
    /// <param name="id">The id to clear.</param>
    public static void Clear(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        NoireAnim.Reset(StateId, ShakeKey(id));
        NoireAnim.Reset(StateId, BounceKey(id));
        NoireAnim.Reset(StateId, FlashKey(id));
    }

    /// <summary>
    /// The vertical offset of a bounce: up quickly, then settling back with a smaller rebound.
    /// </summary>
    /// <remarks>
    /// Built on the shared progress clock rather than on a spring, so a bounce costs no state of its own beyond the
    /// moment it started. Negative is upward, because screen y grows downward.
    /// </remarks>
    private static float BounceOffset(string id)
    {
        const float duration = 0.45f;
        const float height = 6f;

        var progress = NoireAnim.Progress(StateId, BounceKey(id), duration);

        if (progress >= 1f)
            return 0f;

        // A decaying half-sine: one clear hop, then a smaller one, then nothing.
        var decay = 1f - progress;
        return -MathF.Abs(MathF.Sin(progress * MathF.PI * 2f)) * decay * decay * NoireUI.Scaled(height);
    }

    private static string ShakeKey(string id) => $"{id}.shake";

    private static string BounceKey(string id) => $"{id}.bounce";

    private static string FlashKey(string id) => $"{id}.flash";

    /// <summary>The rectangle of the widget just submitted.</summary>
    private static UiRect LastItemRect()
        => UiRect.FromBounds(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
}
