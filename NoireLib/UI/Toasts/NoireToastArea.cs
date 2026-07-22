using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
    /// The width of a toast, at 100%. See <see cref="NoireUI.Scale"/>.
    /// </summary>
    public float Width { get; set; } = 340f;

    /// <summary>
    /// The width a toast is actually drawn at.
    /// </summary>
    private float ScaledWidth => NoireUI.Scaled(Width);

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

    /// <summary>
    /// Copies the toasts into the reused snapshot buffer, for the drawing to walk.
    /// </summary>
    /// <remarks>
    /// What <see cref="GetToasts"/> does, borrowed rather than allocated, because this one runs on every frame the area
    /// holds a toast. The copy is the point rather than an implementation detail: it is what lets a toast's own action
    /// dismiss it, or raise another, without the frame that drew the button walking a list that changed underneath it.
    /// The lock is held only for the copy, so a callback firing later in the frame is free to take it.
    /// </remarks>
    /// <returns>A borrowed buffer holding the toasts in order. Dispose it to give the array back.</returns>
    private PooledBuffer<NoireToast> SnapshotToasts()
    {
        lock (syncRoot)
        {
            var buffer = PooledBuffer<NoireToast>.Rent(toasts.Count);
            var span = buffer.Span;

            for (var index = 0; index < toasts.Count; index++)
                span[index] = toasts[index];

            return buffer;
        }
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
        // Borrowed for the frame rather than allocated in it, and rented per frame rather than kept between them: a
        // toast's action button runs the consumer's code in the middle of the stack being drawn, so a buffer belonging
        // to the area rather than to the frame could be cleared and refilled underneath the loop still walking it.
        using var snapshot = SnapshotToasts();
        using var buffer = PooledBuffer<NoireToast>.Rent(Math.Min(snapshot.Length, Math.Max(1, MaxVisible)));

        var count = Measure(snapshot.Span, buffer.Span, out var total);

        if (count == 0)
            return;

        var visible = buffer.Span[..count];

        // Sized and placed exactly, from heights measured this frame rather than from what the window happened to be
        // last frame. An auto-resizing window is always one frame behind its own contents, which a bottom-anchored
        // stack turns into visible jitter: the whole column chases the animation upwards as it grows and downwards as
        // it shrinks.
        //
        // The height is rounded up to a whole pixel, and that is load-bearing rather than tidiness. A bottom-anchored
        // window is placed at (fixed edge - its own height), so the edge the stack hangs from comes back as
        // (placed position + height) and the two are meant to cancel. They only cancel while the height is a whole
        // number: a window position is snapped to the pixel grid, and snapping a fractional height leaves behind the
        // fraction, which changes every frame that the height animates. The stack then slides back and forth across a
        // pixel for exactly as long as a toast is arriving or leaving, which is the whole of that jitter. Rounding
        // makes the snap a no-op, because snapping something shifted by a whole number of pixels shifts the result by
        // the same whole number.
        var height = ResolveStackHeight(total);
        var size = new Vector2(ScaledWidth, height);
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(Position.Resolve(size, viewport.Pos, viewport.Size), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        using var padding = UiPush.Style(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var border = UiPush.Style(ImGuiStyleVar.WindowBorderSize, 0f);
        using var minSize = UiPush.Style(ImGuiStyleVar.WindowMinSize, Vector2.One);

        var flags = ToastWindowFlags;
        if (AlwaysOnTop)
            flags |= UiWindowOrder.TopLayerFlag;

        if (ImGui.Begin(ImGuiId, flags))
        {
            if (AlwaysOnTop)
                UiWindowOrder.KeepInFront();

            DrawStack(visible, height);
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
    /// <param name="snapshot">The toasts to consider, oldest first.</param>
    /// <param name="visible">Receives the toasts to draw, in stack order. Never filled beyond its own length.</param>
    /// <param name="total">Receives the total height the stack needs.</param>
    /// <returns>How many of <paramref name="visible"/> were filled.</returns>
    private int Measure(ReadOnlySpan<NoireToast> snapshot, Span<NoireToast> visible, out float total)
    {
        using var buffer = PooledBuffer<NoireToast>.Rent(snapshot.Length);

        var expired = buffer.Span;
        var expiredCount = 0;
        var count = 0;

        total = 0f;

        for (var index = 0; index < snapshot.Length && count < visible.Length; index++)
        {
            var toast = snapshot[NewestFirst ? snapshot.Length - 1 - index : index];

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
                expired[expiredCount++] = toast;
                continue;
            }

            var full = toast.LastHeight > 0f ? toast.LastHeight : EstimateHeight(toast);
            toast.Reserved = ResolveSlotHeight(full * toast.Presence);

            total += toast.Reserved + (count > 0 ? StackGap : 0f);
            visible[count++] = toast;
        }

        Retire(expired[..expiredCount]);
        return count;
    }

    /// <summary>
    /// Removes toasts that have finished leaving and fires their dismissal callbacks.
    /// </summary>
    private void Retire(ReadOnlySpan<NoireToast> expired)
    {
        if (expired.Length == 0)
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
    /// Draws the measured stack, laid out from whichever edge of it is pinned to the screen.
    /// </summary>
    /// <remarks>
    /// The direction matters, and getting it wrong is what makes a stack fidget. The window is sized to the stack every
    /// frame, so on a bottom-anchored area its bottom edge is the fixed one and its top edge moves as toasts arrive,
    /// leave and shrink. Laying out downwards from that moving top edge makes every toast's position depend on the
    /// height of every toast before it, and on the window's own height as well: the two only cancel while nothing else
    /// changes, and any wobble in either (a toast whose contents resize, one entering or leaving the visible window,
    /// a share of height clamped at its floor) leaks into all of them at once.<br/>
    /// Laid out from the pinned edge instead, a toast's position depends only on the toasts between it and that edge.
    /// A toast collapsing as it leaves moves the ones further from the anchor and nothing else, and since toasts expire
    /// oldest first and the oldest sits furthest from the anchor, the usual case moves nothing at all.
    /// </remarks>
    /// <param name="visible">The toasts to draw, in stack order.</param>
    /// <param name="height">The window's own height, which is the measured total rounded up to a whole pixel.</param>
    private void DrawStack(ReadOnlySpan<NoireToast> visible, float height)
    {
        var window = ImGui.GetWindowPos();

        if (!AnchoredAtBottom())
        {
            var top = MathF.Floor(window.Y);

            for (var index = 0; index < visible.Length; index++)
            {
                if (index > 0)
                    top += StackGap;

                DrawToast(visible[index], top);
                top += visible[index].Reserved;
            }

            return;
        }

        // The far edge is the window's own near edge plus its own height: the one pair of numbers guaranteed to cancel
        // back to the fixed screen edge the window was placed against. Reading the size back out of ImGui instead, or
        // using the unrounded measured total, each reintroduces a number that only nearly agrees, and the difference is
        // spent again on every toast in the stack.
        //
        // Everything below hangs off this, so a toast's position depends only on the toasts between it and the anchor.
        // The rounding slack, always under a pixel, lands at the far end where there is nothing to disturb.
        var bottom = MathF.Floor(window.Y + height);

        for (var index = visible.Length - 1; index >= 0; index--)
        {
            if (index < visible.Length - 1)
                bottom -= StackGap;

            bottom -= visible[index].Reserved;
            DrawToast(visible[index], bottom);
        }
    }

    /// <summary>
    /// The gap between two toasts, as a whole number of pixels.
    /// </summary>
    /// <remarks>
    /// Snapped for the same reason the slots are, and it has to be read from one place because the height of the stack
    /// and the layout of the stack both count it: a gap rounded in one and not the other would leave the toasts and the
    /// window they sit in disagreeing by a pixel per toast.
    /// </remarks>
    private float StackGap => MathF.Ceiling(Style.ScaledGap);

    /// <summary>
    /// The room a toast is given in the stack, as a whole number of pixels, never less than one.
    /// </summary>
    /// <remarks>
    /// Whole pixels, and the reason is a feedback loop rather than tidiness. ImGui floors the cursor down to the pixel
    /// grid after every item it lays out, so the height a block of content measures depends on the fraction of a pixel
    /// it started at: the same toast measures a pixel taller or shorter depending on where it happens to sit. That
    /// measurement is what the next frame's slot is built from, and each slot shifts every toast further from the
    /// anchor, so a stack laid out on fractional boundaries has every toast nudging its neighbours' measurements about.
    /// The result is a wobble that grows with distance from the anchor, which is exactly how far the error has had to
    /// accumulate.<br/>
    /// Kept on the grid, a toast always measures the same height, and the stack holds still.
    /// </remarks>
    /// <param name="content">The height the toast's contents want.</param>
    /// <returns>The slot height in whole pixels.</returns>
    internal static float ResolveSlotHeight(float content) => MathF.Max(1f, MathF.Ceiling(content));

    /// <summary>
    /// The height the stack's window is given, from the height its contents measured.
    /// </summary>
    /// <remarks>
    /// A whole pixel, and that is the entire point of the method existing. A bottom-anchored window is placed at
    /// (fixed screen edge - its own height), so the edge the stack hangs from is recovered as (placed position +
    /// height). Window positions are snapped to the pixel grid, and snapping only cancels back out of that sum when the
    /// height is a whole number of pixels: snapping a value shifted by a whole number shifts the result by the same
    /// whole number, while a fractional height leaves its fraction behind. That leftover changes on every frame the
    /// height animates, so the whole stack slides across a pixel for as long as a toast is arriving or leaving.
    /// </remarks>
    /// <param name="total">The height the toasts measured for themselves.</param>
    /// <returns>The window height to use, and to hang the stack from.</returns>
    internal static float ResolveStackHeight(float total) => MathF.Max(1f, MathF.Ceiling(total));

    /// <summary>
    /// Whether the stack hangs from its bottom edge, which is the edge that stays still as the stack resizes.
    /// </summary>
    /// <returns>True when the area is anchored along the bottom of the screen.</returns>
    private bool AnchoredAtBottom()
    {
        if (Position.Mode != UiPositionMode.Anchor)
            return false;

        return Position.Anchor is UiAnchor.BottomLeft or UiAnchor.BottomCenter or UiAnchor.BottomRight;
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
        var slide = (1f - alpha) * Style.ScaledSlideDistance * SlideDirection();

        var left = ImGui.GetWindowPos().X + slide;
        var min = new Vector2(left, top);
        var max = new Vector2(left + ScaledWidth, top + toast.Reserved);

        var hovered = ImGui.IsMouseHoveringRect(min, max);

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

        var rounding = Style.ResolveRounding();

        // The slot is shorter than the toast while it is arriving or leaving, so everything is clipped to it. Without
        // this the contents would spill over the toast above and below during the transition.
        ImGui.PushClipRect(min, max, true);

        try
        {
            // A slot closes toward the edge the stack hangs from: on a bottom-anchored area the bottom of the slot
            // stays put and its top comes down to meet it. The toast has to be painted from that same edge, or it
            // slides down the screen while the slot shrinks around it, which is the movement rather than the fade.
            var full = MathF.Max(toast.Reserved, toast.LastHeight);
            var body = new Vector2(min.X, AnchoredAtBottom() ? max.Y - full : min.Y);
            var painted = new Vector2(max.X, body.Y + full);

            // The background is painted at the toast's full height and cropped to the slot, which is what makes a
            // leaving toast look like it is being covered rather than squashed. The countdown instead uses the slot
            // itself, so its geometry and the clip rectangle are the same rectangle: an outline drawn to one boundary
            // and cropped at another loses a different amount on each side.
            PaintBackground(drawList, body, painted, accent, alpha, rounding, theme);
            PaintTimer(drawList, min, max, toast, accent, alpha, rounding);

            ImGui.SetCursorScreenPos(body + Style.ScaledPadding + new Vector2(Style.ScaledStripeWidth, 0f));
            ImGui.BeginGroup();

            using (UiPush.Style(ImGuiStyleVar.Alpha, alpha))
                DrawBody(toast, accent, theme);

            ImGui.EndGroup();

            // Frozen once a toast starts leaving, so its share of the stack only ever shrinks. Its contents are drawn
            // clipped to a slot that is closing and offset by the slide, and a height re-measured under either would
            // feed back into the share computed from it: the collapse would stop being monotonic and every toast
            // further from the anchor would wobble along with it.
            if (!toast.IsDismissed)
                toast.LastHeight = ResolveSlotHeight(ImGui.GetItemRectSize().Y + (Style.ScaledPadding.Y * 2f));
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

        var stripeWidth = Style.ScaledStripeWidth;
        if (stripeWidth > 0f)
        {
            drawList.AddRectFilled(
                min,
                new Vector2(min.X + stripeWidth, max.Y),
                ColorHelper.Vector4ToUint(ColorHelper.ScaleAlpha(accent, alpha)),
                rounding);
        }

        var borderSize = Style.ScaledBorderSize;
        if (borderSize > 0f)
        {
            drawList.AddRect(
                min,
                max,
                ColorHelper.Vector4ToUint(ColorHelper.ScaleAlpha(Style.BorderColor ?? theme.Resolve(ThemeColor.Border), alpha)),
                rounding,
                ImDrawFlags.None,
                borderSize);
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
        var thickness = MathF.Max(1f, Style.ScaledTimerThickness);
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
                var stripeLeft = min.X + Style.ScaledStripeWidth;
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
        using var textColor = UiPush.Color(ImGuiCol.Text, Style.TextColor ?? theme.Resolve(ThemeColor.Text));

        var contentWidth = ScaledWidth - Style.ScaledPadding.X * 2f - Style.ScaledStripeWidth;
        var closeWidth = toast.Closable ? ImGui.GetFrameHeight() : 0f;

        if (Style.ShowIcon)
        {
            var icon = SeverityIcon(toast.Severity);

            using (UiPush.Font(UiBuilder.IconFont))
                ImGui.TextColored(accent, icon.ToIconString());

            ImGui.SameLine(0f, theme.ResolveItemSpacing().X * 0.75f);
            contentWidth -= ImGui.GetItemRectSize().X + theme.ResolveItemSpacing().X * 0.75f;
        }

        // The close button is placed from the toast's own right edge rather than measured off the text block, so a
        // long message cannot push it out of the toast.
        var contentRight = ImGui.GetCursorScreenPos().X + contentWidth;

        ImGui.BeginGroup();

        var textWidth = MathF.Max(NoireUI.Scaled(40f), contentWidth - closeWidth);

        if (!string.IsNullOrEmpty(toast.Title))
        {
            ImGui.TextColored(Style.TitleColor ?? accent, toast.Title);
        }

        NoireLayout.WrapText(textWidth, toast, static t => t.Content.Draw());

        if (toast.Progress != null)
        {
            var value = ReadProgress(toast);
            ImGui.Dummy(new Vector2(1f, NoireUI.Scaled(2f)));
            DrawProgressBar(textWidth, value, accent, theme);
        }

        if (toast.Actions.Count > 0)
        {
            ImGui.Dummy(new Vector2(1f, NoireUI.Scaled(2f)));
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
    /// <remarks>
    /// Copied before any of them is drawn, because an action's own callback may add to or clear the toast's actions,
    /// and the loop is still walking them when it runs. Borrowed rather than allocated: this is per toast, per frame.
    /// </remarks>
    private void DrawActions(NoireToast toast)
    {
        using var buffer = PooledBuffer<ToastAction>.Rent(toast.Actions.Count);

        var snapshot = buffer.Span;

        for (var index = 0; index < snapshot.Length; index++)
            snapshot[index] = toast.Actions[index];

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
        var height = MathF.Max(1f, Style.ScaledProgressHeight);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));

        var fill = Style.ProgressColor ?? ColorHelper.Darken(accent, Style.ProgressDarken);
        var track = Style.ProgressTrackColor ?? theme.Resolve(ThemeColor.SurfaceSunken);

        using var draw = UiDraw.Begin();
        var drawList = draw.List;

        if (drawList.IsNull)
            return;

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

        return Style.ScaledPadding.Y * 2f + ImGui.GetTextLineHeightWithSpacing() * lines;
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
