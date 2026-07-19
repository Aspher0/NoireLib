using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Time-based animation for immediate-mode widgets.<br/>
/// Nothing is registered, created or disposed: every call is keyed by an id, reads the value for this frame, and stores
/// what it needs in <see cref="UiFrameState"/>. A widget that stops calling stops animating, and its state is pruned on
/// its own.<br/>
/// Everything here degrades under <see cref="NoireUI.ReducedMotion"/>: eased values snap to their target and decorative
/// motion stops, so a UI built on this stays fully usable with motion turned off.
/// </summary>
/// <example>
/// <code>
/// var glow = NoireAnim.Ease("save-button", "hover", ImGui.IsItemHovered() ? 1f : 0f);
/// var lift = NoireAnim.Spring("panel", "open", expanded ? 220f : 0f);
/// </code>
/// </example>
public static class NoireAnim
{
    /// <summary>The largest step the spring integrator takes, so a long frame cannot make it explode.</summary>
    private const float MaxSpringStep = 1f / 120f;

    private struct EaseState
    {
        public float Value;

        public float From;

        public float Target;

        public float StartTime;

        public bool Started;
    }

    private struct SpringState
    {
        public float Value;

        public float Velocity;

        public bool Started;
    }

    /// <summary>
    /// The shared clock every animation reads, in seconds. See <see cref="NoireUI.Time"/>.
    /// </summary>
    public static float Time => NoireUI.Time;

    /// <summary>
    /// Moves a value toward <paramref name="target"/> along an easing curve, and returns where it is this frame.<br/>
    /// Changing the target restarts the curve from wherever the value currently is, so a hover that reverses halfway
    /// never snaps.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which property of that widget this is, for example "hover".</param>
    /// <param name="target">The value to move toward.</param>
    /// <param name="duration">How long the move takes, in seconds.</param>
    /// <param name="easing">The curve to follow.</param>
    /// <returns>The current value.</returns>
    /// <remarks>
    /// Keep <paramref name="id"/> and <paramref name="subKey"/> as separate arguments rather than interpolating them
    /// into one string: two existing strings cost nothing to look up, while <c>$"{id}.hover"</c> allocates on every
    /// property of every widget, every frame.
    /// </remarks>
    public static float Ease(string id, string subKey, float target, float duration = 0.15f, UiEasing easing = UiEasing.OutCubic)
        => Ease(id, subKey, target, duration, easing, null);

    /// <inheritdoc cref="Ease(string, string, float, float, UiEasing)"/>
    public static float Ease(string id, float target, float duration = 0.15f, UiEasing easing = UiEasing.OutCubic)
        => Ease(id, string.Empty, target, duration, easing, null);

    /// <summary>
    /// Moves a value toward <paramref name="target"/> along a curve of your own.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which property of that widget this is.</param>
    /// <param name="target">The value to move toward.</param>
    /// <param name="duration">How long the move takes, in seconds.</param>
    /// <param name="curve">Maps a progress from 0 to 1 onto an eased progress.</param>
    /// <returns>The current value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="curve"/> is <see langword="null"/>.</exception>
    public static float Ease(string id, string subKey, float target, float duration, Func<float, float> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return Ease(id, subKey, target, duration, UiEasing.Linear, curve);
    }

    private static float Ease(string id, string subKey, float target, float duration, UiEasing easing, Func<float, float>? curve)
    {
        var now = Time;
        var state = UiFrameState.Get<EaseState>(id, subKey);

        if (!state.Started)
        {
            state = new EaseState { Value = target, From = target, Target = target, StartTime = now, Started = true };
            UiFrameState.Set(id, subKey, state);
            return target;
        }

        if (NoireUI.ReducedMotion || duration <= 0f)
        {
            state.Value = target;
            state.From = target;
            state.Target = target;
            state.StartTime = now;
            UiFrameState.Set(id, subKey, state);
            return target;
        }

        if (state.Target != target)
        {
            state.From = state.Value;
            state.Target = target;
            state.StartTime = now;
        }

        var progress = Math.Clamp((now - state.StartTime) / duration, 0f, 1f);
        var eased = curve != null ? curve(progress) : easing.Apply(progress);

        state.Value = state.From + ((state.Target - state.From) * eased);
        UiFrameState.Set(id, subKey, state);
        return state.Value;
    }

    /// <summary>
    /// Moves a value toward <paramref name="target"/> like a damped spring, and returns where it is this frame.<br/>
    /// Unlike <see cref="Ease(string, string, float, float, UiEasing)"/> a spring has no fixed duration: it carries its
    /// momentum, so a target that keeps moving is followed smoothly instead of restarting.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which property of that widget this is.</param>
    /// <param name="target">The value to move toward.</param>
    /// <param name="stiffness">How hard the spring pulls. Higher arrives faster.</param>
    /// <param name="damping">How much the motion is resisted. Higher overshoots less; around <c>2 * sqrt(stiffness)</c> is the point where it stops overshooting at all.</param>
    /// <returns>The current value.</returns>
    public static float Spring(string id, string subKey, float target, float stiffness = 180f, float damping = 26f)
    {
        var state = UiFrameState.Get<SpringState>(id, subKey);

        if (!state.Started || NoireUI.ReducedMotion)
        {
            state = new SpringState { Value = target, Velocity = 0f, Started = true };
            UiFrameState.Set(id, subKey, state);
            return target;
        }

        // Integrated in fixed sub-steps: a single step over a long frame is unstable and sends the value to infinity.
        var remaining = NoireUI.DeltaTime;

        while (remaining > 0f)
        {
            var step = MathF.Min(remaining, MaxSpringStep);
            remaining -= step;

            var acceleration = ((target - state.Value) * stiffness) - (state.Velocity * damping);
            state.Velocity += acceleration * step;
            state.Value += state.Velocity * step;
        }

        UiFrameState.Set(id, subKey, state);
        return state.Value;
    }

    /// <inheritdoc cref="Spring(string, string, float, float, float)"/>
    public static float Spring(string id, float target, float stiffness = 180f, float damping = 26f)
        => Spring(id, string.Empty, target, stiffness, damping);

    /// <summary>
    /// Eases a value between 0 (hidden) and 1 (shown), for fading and sliding something in and out.<br/>
    /// The point of using this rather than a bare <see cref="Ease(string, string, float, float, UiEasing)"/> is that it
    /// reads as presence at the call site: a widget draws while the result is above zero, not while the flag is true.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which property of that widget this is.</param>
    /// <param name="visible">Whether the thing should currently be present.</param>
    /// <param name="duration">How long the transition takes, in seconds.</param>
    /// <param name="easing">The curve to follow.</param>
    /// <returns>The presence from 0 to 1.</returns>
    public static float Presence(string id, string subKey, bool visible, float duration = 0.18f, UiEasing easing = UiEasing.OutCubic)
        => Ease(id, subKey, visible ? 1f : 0f, duration, easing);

    /// <summary>
    /// A value oscillating smoothly between <paramref name="min"/> and <paramref name="max"/>, for a breathing highlight
    /// or a slow glow. Stateless: it reads the shared clock and stores nothing.<br/>
    /// Returns <paramref name="max"/> unchanged under <see cref="NoireUI.ReducedMotion"/>.
    /// </summary>
    /// <param name="period">One full cycle, in seconds.</param>
    /// <param name="min">The low end.</param>
    /// <param name="max">The high end.</param>
    /// <param name="phase">Shifts the cycle, from 0 to 1, so several pulses can be offset from each other.</param>
    /// <returns>The current value.</returns>
    public static float Pulse(float period = 1.5f, float min = 0f, float max = 1f, float phase = 0f)
    {
        if (NoireUI.ReducedMotion || period <= 0f)
            return max;

        var wave = (MathF.Sin(((Time / period) + phase) * MathF.PI * 2f) + 1f) / 2f;
        return min + ((max - min) * wave);
    }

    /// <summary>
    /// A value sweeping from 0 to 1 and starting over, for a highlight travelling across something. Stateless.<br/>
    /// Returns 1 under <see cref="NoireUI.ReducedMotion"/>.
    /// </summary>
    /// <param name="period">One full sweep, in seconds.</param>
    /// <param name="phase">Shifts the sweep, from 0 to 1.</param>
    /// <returns>The current position of the sweep, from 0 to 1.</returns>
    public static float Sweep(float period = 2f, float phase = 0f)
    {
        if (NoireUI.ReducedMotion || period <= 0f)
            return 1f;

        var position = ((Time / period) + phase) % 1f;
        return position < 0f ? position + 1f : position;
    }

    /// <summary>
    /// Starts a one-shot animation now. Call it on the frame the thing happened (a save succeeded, a value was rejected);
    /// <see cref="Progress"/>, <see cref="Flash"/> and <see cref="Shake"/> read from it.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which one-shot this is, for example "saved".</param>
    public static void Trigger(string id, string subKey) => UiFrameState.Set(id, subKey, Time);

    /// <inheritdoc cref="Trigger(string, string)"/>
    public static void Trigger(string id) => Trigger(id, string.Empty);

    /// <summary>
    /// How far a one-shot started by <see cref="Trigger(string, string)"/> has got, from 0 to 1.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which one-shot this is.</param>
    /// <param name="duration">How long it lasts, in seconds.</param>
    /// <returns>The progress from 0 to 1, and 1 when it has finished or was never triggered.</returns>
    public static float Progress(string id, string subKey, float duration = 0.5f)
    {
        if (duration <= 0f || !UiFrameState.TryGet<float>(id, subKey, out var startTime))
            return 1f;

        return Math.Clamp((Time - startTime) / duration, 0f, 1f);
    }

    /// <summary>
    /// Whether a one-shot started by <see cref="Trigger(string, string)"/> is still running.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which one-shot this is.</param>
    /// <param name="duration">How long it lasts, in seconds.</param>
    /// <returns>True while it is running.</returns>
    public static bool IsRunning(string id, string subKey, float duration = 0.5f) => Progress(id, subKey, duration) < 1f;

    /// <summary>
    /// A value falling from 1 to 0 after <see cref="Trigger(string, string)"/>, for a confirmation flash behind a control.<br/>
    /// Returns 0 under <see cref="NoireUI.ReducedMotion"/>, so nothing flashes.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which one-shot this is.</param>
    /// <param name="duration">How long the flash lasts, in seconds.</param>
    /// <returns>The flash strength from 1 down to 0.</returns>
    public static float Flash(string id, string subKey, float duration = 0.6f)
    {
        if (NoireUI.ReducedMotion)
            return 0f;

        return 1f - UiEasing.OutCubic.Apply(Progress(id, subKey, duration));
    }

    /// <summary>
    /// A horizontal offset oscillating and dying out after <see cref="Trigger(string, string)"/>, for rejecting an invalid entry.<br/>
    /// Returns 0 under <see cref="NoireUI.ReducedMotion"/>.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which one-shot this is.</param>
    /// <param name="duration">How long the shake lasts, in seconds.</param>
    /// <param name="amplitude">The largest offset in pixels.</param>
    /// <param name="frequency">How many oscillations per second.</param>
    /// <returns>The offset in pixels, positive or negative.</returns>
    public static float Shake(string id, string subKey, float duration = 0.4f, float amplitude = 5f, float frequency = 22f)
    {
        if (NoireUI.ReducedMotion)
            return 0f;

        var progress = Progress(id, subKey, duration);
        if (progress >= 1f)
            return 0f;

        return MathF.Sin(progress * duration * frequency * MathF.PI * 2f) * amplitude * (1f - progress);
    }

    /// <summary>
    /// Interpolates between two colours, straight in RGBA.
    /// </summary>
    /// <param name="from">The colour at 0.</param>
    /// <param name="to">The colour at 1.</param>
    /// <param name="t">The blend, from 0 to 1. Values outside that range are clamped.</param>
    /// <returns>The blended colour.</returns>
    public static Vector4 Blend(Vector4 from, Vector4 to, float t) => Vector4.Lerp(from, to, Math.Clamp(t, 0f, 1f));

    /// <summary>
    /// Drops the stored state of one animation, so the next call starts from scratch.
    /// </summary>
    /// <param name="id">The widget id.</param>
    /// <param name="subKey">Which property of that widget this is.</param>
    public static void Reset(string id, string subKey)
    {
        UiFrameState.Remove<EaseState>(id, subKey);
        UiFrameState.Remove<SpringState>(id, subKey);
        UiFrameState.Remove<float>(id, subKey);
    }
}
