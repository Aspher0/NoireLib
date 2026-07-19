using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A stack of notifications anchored somewhere on screen, animated in and out, with buttons, live progress and a
/// countdown that pauses while you read.<br/>
/// Most plugins never touch this type: <see cref="NoireToast.Success(string)"/> and friends put a toast in
/// <see cref="Default"/>, which draws itself. Construct one only to put a second stack somewhere else, or to draw the
/// stack inside a window of your own.
/// </summary>
/// <remarks>
/// Adding a toast is safe from any thread; everything else happens on the draw thread. The stack is snapshotted before
/// it is drawn, so an action that shows another toast, or dismisses the one it is on, cannot disturb the frame it fires
/// in.
/// </remarks>
public class NoireToastArea : NoireDrawable
{
    private const ImGuiWindowFlags ToastWindowFlags =
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoBackground;

    private static readonly object DefaultLock = new();
    private static NoireToastArea? defaultArea;

    private readonly object syncRoot = new();
    private readonly List<NoireToast> toasts = new();

    /// <summary>
    /// Creates a toast area and registers it for drawing.
    /// </summary>
    /// <remarks>
    /// An area you construct yourself follows the <see cref="NoireUI.AutoDraw"/> master default, because constructing
    /// one is how you say where the stack goes. <see cref="Default"/> opts itself in instead, so a toast raised from
    /// anywhere appears without the plugin having wired a draw call.
    /// </remarks>
    /// <param name="id">An optional unique identifier. When <see langword="null"/>, a random one is generated.</param>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public NoireToastArea(string? id = null)
        : base(id, "ToastArea")
    {
        Register();
    }

    /// <summary>
    /// The area the static <see cref="NoireToast"/> helpers put their toasts in, created the first time one is raised.
    /// </summary>
    /// <remarks>
    /// It draws itself, so a toast raised from a command handler, a background task or a hotkey callback appears with
    /// no wiring at all. Set its <see cref="NoireDrawable.AutoDraw"/> to <see langword="false"/> to take the drawing
    /// over and place the stack yourself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    public static NoireToastArea Default
    {
        get
        {
            if (defaultArea is { IsDisposed: false })
                return defaultArea;

            lock (DefaultLock)
            {
                if (defaultArea is { IsDisposed: false })
                    return defaultArea;

                defaultArea = new NoireToastArea("Default") { AutoDraw = true };
                return defaultArea;
            }
        }
    }

    #region Configuration

    /// <summary>
    /// Where the stack sits on screen. Defaults to the bottom right corner, clear of the game's own notifications.
    /// </summary>
    public UiPosition Position { get; set; } = UiPosition.AtAnchor(UiAnchor.BottomRight, new Vector2(-20f, -20f));

    /// <summary>
    /// The width of a toast in pixels.
    /// </summary>
    public float Width { get; set; } = 340f;

    /// <summary>
    /// How many toasts are shown at once. The rest wait their turn rather than filling the screen.
    /// </summary>
    public int MaxVisible { get; set; } = 4;

    /// <summary>
    /// How many toasts may wait in total before the oldest are dropped.<br/>
    /// Bounded on purpose: a loop raising a toast per frame against a hidden interface is otherwise a memory leak.
    /// </summary>
    public int Capacity { get; set; } = 64;

    /// <summary>
    /// Whether the newest toast appears at the top of the stack rather than the bottom.<br/>
    /// The default puts the newest nearest a bottom anchor, which is where the eye already is; flip it for a stack
    /// anchored to the top of the screen.
    /// </summary>
    public bool NewestFirst { get; set; }

    /// <summary>
    /// Whether the stack is kept in front of every other window, including the plugin's own, for clicks as well as for
    /// drawing.<br/>
    /// On by default: a notification hidden behind the window that raised it has not notified anyone, and a toast whose
    /// action button cannot be clicked through an overlapping window is no better. Turn it off for a stack that should
    /// sit in the normal window order.
    /// </summary>
    /// <remarks>
    /// Being drawn on top and receiving the mouse are two different orders in ImGui, and moving only the first is what
    /// produces a toast that is plainly visible above a window and whose buttons do nothing. This moves both.
    /// </remarks>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// The look of the toasts in this area.
    /// </summary>
    public ToastStyle Style { get; set; } = new();

    /// <summary>
    /// How many toasts have been dropped because <see cref="Capacity"/> was full.
    /// </summary>
    public int DroppedCount { get; private set; }

    /// <summary>
    /// The toasts currently in the area, newest last.
    /// </summary>
    /// <returns>A snapshot of the toasts.</returns>
    public IReadOnlyList<NoireToast> GetToasts()
    {
        lock (syncRoot)
            return toasts.ToArray();
    }

    #endregion

    #region Adding and removing

    /// <summary>
    /// Adds a toast to this area. Safe to call from any thread.
    /// </summary>
    /// <param name="toast">The toast to show.</param>
    /// <returns>The toast, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="toast"/> is <see langword="null"/>.</exception>
    public NoireToast Add(NoireToast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);

        NoireUI.EnsureFrameServices();

        lock (syncRoot)
        {
            toast.Area = this;
            toasts.Add(toast);

            while (toasts.Count > Math.Max(1, Capacity))
            {
                toasts.RemoveAt(0);
                DroppedCount++;
            }
        }

        return toast;
    }

    /// <summary>
    /// Asks every toast in the area to go away. They play their exit animation on the way out.
    /// </summary>
    public void DismissAll()
    {
        foreach (var toast in GetToasts())
            toast.Dismiss();
    }

    /// <summary>
    /// Removes every toast immediately, without an exit animation and without firing their dismissal callbacks.<br/>
    /// Use <see cref="DismissAll"/> unless the interface is going away entirely.
    /// </summary>
    public void Clear()
    {
        lock (syncRoot)
            toasts.Clear();
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        Clear();

        if (ReferenceEquals(defaultArea, this))
            defaultArea = null;
    }

    #endregion

    #region Drawing

    /// <inheritdoc/>
    protected override void DrawCore()
    {
        var visible = new List<NoireToast>();
        var total = Measure(visible);

        if (visible.Count == 0)
            return;

        // Sized and placed exactly, from heights measured this frame rather than from what the window happened to be
        // last frame. An auto-resizing window is always one frame behind its own contents, which a bottom-anchored
        // stack turns into visible jitter: the whole column chases the animation upwards as it grows and downwards as
        // it shrinks.
        var size = new Vector2(Width, MathF.Max(1f, total));
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(Position.Resolve(size, viewport.Pos, viewport.Size), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var border = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 0f);
        using var minSize = ImRaii.PushStyle(ImGuiStyleVar.WindowMinSize, Vector2.One);

        var flags = ToastWindowFlags;
        if (AlwaysOnTop)
            flags |= UiWindowOrder.TopLayerFlag;

        if (ImGui.Begin(ImGuiId, flags))
        {
            if (AlwaysOnTop)
                UiWindowOrder.KeepInFront();

            DrawStack(visible);
        }

        ImGui.End();
    }

    /// <summary>
    /// Advances every visible toast's transition, works out how much vertical room each one takes this frame, and
    /// retires the ones that have finished leaving.
    /// </summary>
    /// <remarks>
    /// Runs before anything is drawn because the window has to be sized and positioned from the total, and a toast on
    /// its way out contributes a shrinking share of it: that is what makes the stack close up smoothly behind a
    /// dismissed toast instead of the rest jumping into the gap.
    /// </remarks>
    /// <param name="visible">Receives the toasts to draw, in stack order.</param>
    /// <returns>The total height the stack needs.</returns>
    private float Measure(List<NoireToast> visible)
    {
        var snapshot = GetToasts();
        var expired = new List<NoireToast>();
        var total = 0f;

        for (var index = 0; index < snapshot.Count && visible.Count < Math.Max(1, MaxVisible); index++)
        {
            var toast = snapshot[NewestFirst ? snapshot.Count - 1 - index : index];

            // The transition is seeded at zero on the frame a toast first appears: the animation state would otherwise
            // be created already at its target, and the toast would pop into place rather than arrive.
            var firstFrame = !toast.Started;
            if (firstFrame)
            {
                toast.Started = true;
                toast.Remaining = (float)toast.Duration.TotalSeconds;
            }

            var presence = NoireAnim.Presence(toast.Id, "toast", !firstFrame && !toast.IsDismissed, Style.TransitionDuration);
            toast.Presence = Math.Clamp(presence, 0f, 1f);

            if (toast.IsDismissed && toast.Presence <= 0.01f)
            {
                expired.Add(toast);
                continue;
            }

            var full = toast.LastHeight > 0f ? toast.LastHeight : EstimateHeight(toast);
            toast.Reserved = MathF.Max(1f, full * toast.Presence);

            total += toast.Reserved + (visible.Count > 0 ? Style.Gap : 0f);
            visible.Add(toast);
        }

        Retire(expired);
        return total;
    }

    /// <summary>
    /// Removes toasts that have finished leaving and fires their dismissal callbacks.
    /// </summary>
    private void Retire(List<NoireToast> expired)
    {
        if (expired.Count == 0)
            return;

        lock (syncRoot)
        {
            foreach (var toast in expired)
                toasts.Remove(toast);
        }

        foreach (var toast in expired)
            toast.NotifyDismissed();
    }

    /// <summary>
    /// Draws the measured stack.
    /// </summary>
    private void DrawStack(List<NoireToast> visible)
    {
        var top = ImGui.GetWindowPos().Y;

        for (var index = 0; index < visible.Count; index++)
        {
            if (index > 0)
                top += Style.Gap;

            DrawToast(visible[index], top);
            top += visible[index].Reserved;
        }
    }

    /// <summary>
    /// Draws one toast into the vertical slot <see cref="Measure"/> reserved for it, and advances its clock.
    /// </summary>
    /// <param name="toast">The toast to draw.</param>
    /// <param name="top">The top of its slot, in screen coordinates.</param>
    private void DrawToast(NoireToast toast, float top)
    {
        var theme = NoireTheme.Current;
        var accent = SeverityColor(toast.Severity, theme);

        var alpha = toast.Presence;
        var slide = (1f - alpha) * Style.SlideDistance * SlideDirection();

        var left = ImGui.GetWindowPos().X + slide;
        var min = new Vector2(left, top);
        var max = new Vector2(left + Width, top + toast.Reserved);

        var hovered = ImGui.IsMouseHoveringRect(min, max);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = Style.Rounding ?? theme.ResolveSurfaceRounding();

        // The slot is shorter than the toast while it is arriving or leaving, so everything is clipped to it. Without
        // this the contents would spill over the toast above and below during the transition.
        ImGui.PushClipRect(min, max, true);

        try
        {
            var painted = new Vector2(max.X, top + MathF.Max(toast.Reserved, toast.LastHeight));

            // The background is painted at the toast's full height and cropped to the slot, which is what makes a
            // leaving toast look like it is being covered rather than squashed. The countdown instead uses the slot
            // itself, so its geometry and the clip rectangle are the same rectangle: an outline drawn to one boundary
            // and cropped at another loses a different amount on each side.
            PaintBackground(drawList, min, painted, accent, alpha, rounding, theme);
            PaintTimer(drawList, min, max, toast, accent, alpha, rounding);

            ImGui.SetCursorScreenPos(min + Style.Padding + new Vector2(Style.StripeWidth, 0f));
            ImGui.BeginGroup();

            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, alpha))
                DrawBody(toast, accent, theme);

            ImGui.EndGroup();

            toast.LastHeight = MathF.Max(1f, ImGui.GetItemRectSize().Y + Style.Padding.Y * 2f);
        }
        finally
        {
            ImGui.PopClipRect();
        }

        AdvanceClock(toast, hovered);
        HandleBodyClick(toast, hovered);
    }

    /// <summary>
    /// Which way a toast slides in from, taken from the edge the stack is anchored to so it always arrives from off
    /// screen rather than across the middle of it.
    /// </summary>
    /// <returns>1 to arrive from the right, -1 from the left, 0 to fade in place.</returns>
    private float SlideDirection()
    {
        if (Position.Mode != UiPositionMode.Anchor)
            return 1f;

        return Position.Anchor switch
        {
            UiAnchor.TopRight or UiAnchor.MiddleRight or UiAnchor.BottomRight => 1f,
            UiAnchor.TopLeft or UiAnchor.MiddleLeft or UiAnchor.BottomLeft => -1f,
            _ => 0f,
        };
    }

    /// <summary>
    /// Paints a toast's background, border, severity stripe and remaining-time bar.
    /// </summary>
    private void PaintBackground(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 accent, float alpha, float rounding, NoireTheme theme)
    {
        var background = ColorHelper.ScaleAlpha(Style.BackgroundColor ?? theme.Resolve(ThemeColor.SurfaceRaised), alpha);
        drawList.AddRectFilled(min, max, ColorHelper.Vector4ToUint(background), rounding);

        if (Style.StripeWidth > 0f)
        {
            drawList.AddRectFilled(
                min,
                new Vector2(min.X + Style.StripeWidth, max.Y),
                ColorHelper.Vector4ToUint(ColorHelper.ScaleAlpha(accent, alpha)),
                rounding);
        }

        if (Style.BorderSize > 0f)
        {
            drawList.AddRect(
                min,
                max,
                ColorHelper.Vector4ToUint(ColorHelper.ScaleAlpha(Style.BorderColor ?? theme.Resolve(ThemeColor.Border), alpha)),
                rounding,
                ImDrawFlags.None,
                Style.BorderSize);
        }
    }

    /// <summary>
    /// Paints the countdown showing how long a toast has left, in whichever shape the style asked for.
    /// </summary>
    /// <remarks>
    /// Drawn between the background and the contents so the tint modes sit behind the message rather than over it, and
    /// skipped entirely for a toast with no duration, which has nothing to count down to.
    /// </remarks>
    private void PaintTimer(ImDrawListPtr drawList, Vector2 min, Vector2 max, NoireToast toast, Vector4 accent, float alpha, float rounding)
    {
        if (Style.Timer == ToastTimerMode.None || toast.Duration <= TimeSpan.Zero)
            return;

        var total = (float)toast.Duration.TotalSeconds;
        if (total <= 0f)
            return;

        var left = Math.Clamp(toast.Remaining / total, 0f, 1f);
        var fraction = Style.TimerDrains ? left : 1f - left;
        if (fraction <= 0f)
            return;

        var color = ColorHelper.ScaleAlpha(Style.TimerColor ?? accent, alpha);
        var thickness = MathF.Max(1f, Style.TimerThickness);
        var width = max.X - min.X;

        switch (Style.Timer)
        {
            case ToastTimerMode.BottomBar:
                drawList.AddRectFilled(
                    new Vector2(min.X, max.Y - thickness),
                    new Vector2(min.X + width * fraction, max.Y),
                    ColorHelper.Vector4ToUint(color));
                break;

            case ToastTimerMode.TopBar:
                drawList.AddRectFilled(
                    min,
                    new Vector2(min.X + width * fraction, min.Y + thickness),
                    ColorHelper.Vector4ToUint(color));
                break;

            case ToastTimerMode.Stripe:
                // Placed after the severity stripe rather than over it, so the two read as two things and the
                // thickness means what it says: sharing the stripe's column would swallow anything thinner than it.
                var height = max.Y - min.Y;
                var stripeLeft = min.X + Style.StripeWidth;
                drawList.AddRectFilled(
                    new Vector2(stripeLeft, max.Y - height * fraction),
                    new Vector2(stripeLeft + thickness, max.Y),
                    ColorHelper.Vector4ToUint(color));
                break;

            case ToastTimerMode.Border:
                UiOutline.TraceClockwise(drawList, min, max, ColorHelper.Vector4ToUint(color), thickness, fraction);
                break;

            case ToastTimerMode.TintLeftToRight:
                drawList.AddRectFilled(
                    min,
                    new Vector2(min.X + width * fraction, max.Y),
                    ColorHelper.Vector4ToUint(ColorHelper.WithAlpha(color, Style.TimerTintAlpha * alpha)),
                    rounding);
                break;

            case ToastTimerMode.TintRightToLeft:
                drawList.AddRectFilled(
                    new Vector2(max.X - width * fraction, min.Y),
                    max,
                    ColorHelper.Vector4ToUint(ColorHelper.WithAlpha(color, Style.TimerTintAlpha * alpha)),
                    rounding);
                break;
        }
    }

    /// <summary>
    /// Draws the contents of a toast: icon, title, message, progress and actions.
    /// </summary>
    private void DrawBody(NoireToast toast, Vector4 accent, NoireTheme theme)
    {
        // A toast paints its own surface from the theme, so its text has to come from the theme too. Left to inherit
        // the host's ImGui text colour it stays near-white, which is invisible the moment the palette turns light.
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, Style.TextColor ?? theme.Resolve(ThemeColor.Text));

        var contentWidth = Width - Style.Padding.X * 2f - Style.StripeWidth;
        var closeWidth = toast.Closable ? ImGui.GetFrameHeight() : 0f;

        if (Style.ShowIcon)
        {
            var icon = SeverityIcon(toast.Severity);

            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextColored(accent, icon.ToIconString());

            ImGui.SameLine(0f, theme.ResolveItemSpacing().X * 0.75f);
            contentWidth -= ImGui.GetItemRectSize().X + theme.ResolveItemSpacing().X * 0.75f;
        }

        // The close button is placed from the toast's own right edge rather than measured off the text block, so a
        // long message cannot push it out of the toast.
        var contentRight = ImGui.GetCursorScreenPos().X + contentWidth;

        ImGui.BeginGroup();

        var textWidth = MathF.Max(40f, contentWidth - closeWidth);

        if (!string.IsNullOrEmpty(toast.Title))
        {
            ImGui.TextColored(Style.TitleColor ?? accent, toast.Title);
        }

        NoireLayout.WrapText(textWidth, toast, static t => t.Content.Draw());

        if (toast.Progress != null)
        {
            var value = ReadProgress(toast);
            ImGui.Dummy(new Vector2(1f, 2f));
            DrawProgressBar(textWidth, value, accent, theme);
        }

        if (toast.Actions.Count > 0)
        {
            ImGui.Dummy(new Vector2(1f, 2f));
            DrawActions(toast);
        }

        ImGui.EndGroup();

        if (!toast.Closable)
            return;

        var groupTop = ImGui.GetItemRectMin().Y;
        ImGui.SetCursorScreenPos(new Vector2(contentRight - closeWidth, groupTop));

        var closeStyle = new ButtonStyle
        {
            Tone = ButtonTone.Ghost,
            Icon = FontAwesomeIcon.Times,
            TextColor = theme.Resolve(ThemeColor.TextMuted),
            IconColor = theme.Resolve(ThemeColor.TextMuted),
        };

        if (NoireButtons.Button($"##{toast.Id}Close", closeStyle, new Vector2(closeWidth, closeWidth)))
            toast.Dismiss();
    }

    /// <summary>
    /// Draws a toast's action buttons in a wrapping row, so a toast with several actions grows rather than overflowing.
    /// </summary>
    private void DrawActions(NoireToast toast)
    {
        var snapshot = new ToastAction[toast.Actions.Count];
        toast.Actions.CopyTo(snapshot, 0);

        for (var index = 0; index < snapshot.Length; index++)
        {
            var action = snapshot[index];
            var width = ImGui.CalcTextSize(action.Label).X + NoireTheme.Current.ResolveFramePadding().X * 2f;

            NoireLayout.FlowItem(width, index == 0);

            if (!NoireButtons.Button($"{action.Label}##{toast.Id}Action{index}", action.Tone))
                continue;

            try
            {
                action.OnInvoke(toast);
            }
            catch (Exception ex)
            {
                NoireUI.Diagnostics.ReportFault(nameof(NoireToast), $"The toast action '{action.Label}' threw.", ex);
            }

            if (action.DismissesToast)
                toast.Dismiss();
        }
    }

    private void DrawProgressBar(float width, float value, Vector4 accent, NoireTheme theme)
    {
        var height = MathF.Max(2f, Style.ProgressHeight ?? Style.TimerThickness * 2f);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var fill = Style.ProgressColor ?? ColorHelper.Darken(accent, Style.ProgressDarken);
        var track = Style.ProgressTrackColor ?? theme.Resolve(ThemeColor.SurfaceSunken);

        var drawList = ImGui.GetWindowDrawList();
        var max = origin + new Vector2(width, height);

        drawList.AddRectFilled(origin, max, ColorHelper.Vector4ToUint(track), height * 0.5f);
        drawList.AddRectFilled(
            origin,
            new Vector2(origin.X + width * Math.Clamp(value, 0f, 1f), max.Y),
            ColorHelper.Vector4ToUint(fill),
            height * 0.5f);
    }

    /// <summary>
    /// Counts a toast's duration down, pausing while it is hovered.
    /// </summary>
    private static void AdvanceClock(NoireToast toast, bool hovered)
    {
        if (toast.IsDismissed || toast.Duration <= TimeSpan.Zero)
            return;

        if (hovered && toast.PauseOnHover)
            return;

        toast.Remaining -= NoireUI.DeltaTime;

        if (toast.Remaining <= 0f)
            toast.Dismiss();
    }

    private static void HandleBodyClick(NoireToast toast, bool hovered)
    {
        if (toast.OnClick == null || !hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsAnyItemHovered())
            return;

        try
        {
            toast.OnClick(toast);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireToast), "A toast's click callback threw.", ex);
        }
    }

    private static float ReadProgress(NoireToast toast)
    {
        try
        {
            return Math.Clamp(toast.Progress!(), 0f, 1f);
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireToast), "A toast's progress callback threw.", ex);
            toast.Progress = null;
            return 0f;
        }
    }

    /// <summary>
    /// A first-frame height estimate, used only until the toast has measured itself once.
    /// </summary>
    private float EstimateHeight(NoireToast toast)
    {
        var lines = 1;

        if (!string.IsNullOrEmpty(toast.Title))
            lines++;

        if (toast.Progress != null)
            lines++;

        if (toast.Actions.Count > 0)
            lines++;

        return Style.Padding.Y * 2f + ImGui.GetTextLineHeightWithSpacing() * lines;
    }

    private static Vector4 SeverityColor(ToastSeverity severity, NoireTheme theme) => severity switch
    {
        ToastSeverity.Success => theme.Resolve(ThemeColor.Success),
        ToastSeverity.Warning => theme.Resolve(ThemeColor.Warning),
        ToastSeverity.Error => theme.Resolve(ThemeColor.Danger),
        _ => theme.Resolve(ThemeColor.Info),
    };

    private static FontAwesomeIcon SeverityIcon(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Success => FontAwesomeIcon.CheckCircle,
        ToastSeverity.Warning => FontAwesomeIcon.ExclamationTriangle,
        ToastSeverity.Error => FontAwesomeIcon.TimesCircle,
        _ => FontAwesomeIcon.InfoCircle,
    };

    #endregion
}
